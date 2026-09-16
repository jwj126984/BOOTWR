using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using BOOTWR.Services;

namespace BOOTWR.ViewModels
{
    public partial class MainWindowViewModel : ObservableObject
    {
        private readonly CanCommunicationService _canService;
        private readonly FlashingService _flashingService;
        private readonly SnService _snService;
        private readonly CalibrationService _calibrationService;
        private readonly LogService _logService;
        private readonly MessageManager _messageManager;
        private readonly DispatcherTimer _saveTimer;
        private readonly DispatcherTimer _statusTimer;

        [ObservableProperty]
        private bool _isStatusLooping = false;

        public MainWindowViewModel()
        {
            _canService = new CanCommunicationService();
            _flashingService = new FlashingService(_canService);
            _snService = new SnService(_canService);
            _calibrationService = new CalibrationService(_canService);
            _logService = new LogService();
            _messageManager = new MessageManager();

            _saveTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromSeconds(1)
            };
            _saveTimer.Tick += SaveTimer_Tick;

            _statusTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(2000)
            };
            _statusTimer.Tick += StatusTimer_Tick;

            InitializeDeviceTypes();
            InitializeBaudRates();

            LoadSavedConfig();

            _canService.MessageReceived += CanService_MessageReceived;
            _canService.MessageSent += CanService_MessageSent;
            _flashingService.StateChanged += FlashingService_StateChanged;
            _flashingService.ProgressChanged += FlashingService_ProgressChanged;
            _logService.LogAdded += LogService_LogAdded;
            _logService.LogsCleared += LogService_LogsCleared;
            _messageManager.MessageAdded += MessageManager_MessageAdded;
            _messageManager.MessagesCleared += MessageManager_MessagesCleared;

            PropertyChanged += MainWindowViewModel_PropertyChanged;

            InitializeCalibrationValues();
            InitializeKbCalculator();

            _logService.Info("应用启动");
        }

        private void InitializeCalibrationValues()
        {
            AfeCalibrationList.Clear();
            for (int afe = 0; afe < 3; afe++)
            {
                AfeCalibrationList.Add(new AfeCalibrationData(afe));
            }
        }

        private void InitializeKbCalculator()
        {
            KbPointCountOptions.Clear();
            for (int i = 2; i <= 20; i++)
            {
                KbPointCountOptions.Add(i);
            }
            SelectedKbPointCount = 3;
            RebuildKbInputList(SelectedKbPointCount);
        }

        private void RebuildKbInputList(int count)
        {
            KbInputList.Clear();
            for (int i = 0; i < count; i++)
            {
                KbInputList.Add(new KbInputItem { Index = i + 1 });
            }
            KbResultK = string.Empty;
            KbResultB = string.Empty;
            KbResultMessage = string.Empty;
        }

        partial void OnSelectedKbPointCountChanged(int value)
        {
            if (value > 0)
            {
                RebuildKbInputList(value);
            }
        }

        private void SaveTimer_Tick(object? sender, EventArgs e)
        {
            _saveTimer.Stop();
            SaveCurrentConfig();
        }

        private void LoadSavedConfig()
        {
            CanConfig config = ConfigService.LoadConfig();

            SelectedDeviceType = DeviceTypes.FirstOrDefault(d => d.Name == config.SelectedDeviceTypeName) 
                               ?? DeviceTypes.FirstOrDefault(d => d.Name == "USBCANFD-200U");
            SelectedBaudRate = BaudRates.FirstOrDefault(b => b.Name == config.SelectedBaudRateName)
                              ?? BaudRates.FirstOrDefault(b => b.Name == "250 kbps");
            DeviceIndex = config.DeviceIndex;
            ChannelIndex = config.ChannelIndex;
            UdsPhysicalAddress = config.UdsPhysicalAddress;
            UdsResponseAddress = config.UdsResponseAddress;
        }

        private void MainWindowViewModel_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        private void SaveCurrentConfig()
        {
            CanConfig config = new CanConfig
            {
                SelectedDeviceTypeName = SelectedDeviceType?.Name ?? "USBCANFD-200U",
                SelectedBaudRateName = SelectedBaudRate?.Name ?? "250 kbps",
                DeviceIndex = DeviceIndex,
                ChannelIndex = ChannelIndex,
                UdsPhysicalAddress = UdsPhysicalAddress,
                UdsResponseAddress = UdsResponseAddress
            };
            ConfigService.SaveConfig(config);
        }

        private void InitializeDeviceTypes()
        {
            DeviceTypes = new ObservableCollection<DeviceTypeItem>
            {
                new DeviceTypeItem { Name = "USBCANFD-200U", Value = CanDeviceType.USBCANFD_200U },
                new DeviceTypeItem { Name = "USBCAN-Ⅰ", Value = CanDeviceType.USBCAN_1 }
            };
        }

        private void InitializeBaudRates()
        {
            BaudRates = new ObservableCollection<BaudRateItem>
            {
                new BaudRateItem { Name = "10 kbps", Value = CanBaudRate.Bps10000 },
                new BaudRateItem { Name = "20 kbps", Value = CanBaudRate.Bps20000 },
                new BaudRateItem { Name = "50 kbps", Value = CanBaudRate.Bps50000 },
                new BaudRateItem { Name = "100 kbps", Value = CanBaudRate.Bps100000 },
                new BaudRateItem { Name = "125 kbps", Value = CanBaudRate.Bps125000 },
                new BaudRateItem { Name = "250 kbps", Value = CanBaudRate.Bps250000 },
                new BaudRateItem { Name = "500 kbps", Value = CanBaudRate.Bps500000 },
                new BaudRateItem { Name = "800 kbps", Value = CanBaudRate.Bps800000 },
                new BaudRateItem { Name = "1 Mbps", Value = CanBaudRate.Bps1000000 }
            };
        }

        private void CanService_MessageReceived(object? sender, CanMessageReceivedEventArgs e)
        {
            _messageManager.AddReceiveMessage(e.CanId, e.Data, e.IsExtendedId);
        }

        private void CanService_MessageSent(object? sender, CanMessageReceivedEventArgs e)
        {
            _messageManager.AddSendMessage(e.CanId, e.Data, e.IsExtendedId);
        }

        private void FlashingService_StateChanged(object? sender, FlashingStateChangedEventArgs e)
        {
            switch (e.NewState)
            {
                case FlashingState.Session:
                    _logService.Info("正在建立会话...");
                    break;
                case FlashingState.Erase:
                    _logService.Info("正在擦除Flash...");
                    break;
                case FlashingState.SendingData:
                    _logService.Info("正在发送数据...");
                    break;
                case FlashingState.WaitingForResponse:
                    _logService.Info("等待设备响应...");
                    break;
                case FlashingState.Commit:
                    _logService.Info("正在提交...");
                    break;
                case FlashingState.Completed:
                    _logService.Info("刷写完成！");
                    break;
                case FlashingState.Error:
                    _logService.Error($"刷写失败: {_flashingService.ErrorMessage}");
                    break;
            }
        }

        private void FlashingService_ProgressChanged(object? sender, ProgressChangedEventArgs e)
        {
            FlashingProgress = e.Progress;
        }

        private void LogService_LogAdded(object? sender, LogMessage e)
        {
            var dispatcher = App.Current?.Dispatcher;
            if (dispatcher != null)
            {
                dispatcher.BeginInvoke(() =>
                {
                    LogMessages.Add(e);
                });
            }
        }

        private void LogService_LogsCleared(object? sender, EventArgs e)
        {
            var dispatcher = App.Current?.Dispatcher;
            if (dispatcher != null)
            {
                dispatcher.BeginInvoke(() =>
                {
                    LogMessages.Clear();
                });
            }
        }

        private void MessageManager_MessageAdded(object? sender, CanMessageItem e)
        {
            var dispatcher = App.Current?.Dispatcher;
            if (dispatcher != null)
            {
                dispatcher.BeginInvoke(() =>
                {
                    CanMessages.Add(e);
                });
            }
        }

        private void MessageManager_MessagesCleared(object? sender, EventArgs e)
        {
            var dispatcher = App.Current?.Dispatcher;
            if (dispatcher != null)
            {
                dispatcher.BeginInvoke(() =>
                {
                    CanMessages.Clear();
                });
            }
        }

        [ObservableProperty]
        private ObservableCollection<DeviceTypeItem> _deviceTypes = new ObservableCollection<DeviceTypeItem>();

        [ObservableProperty]
        private ObservableCollection<BaudRateItem> _baudRates = new ObservableCollection<BaudRateItem>();

        [ObservableProperty]
        private DeviceTypeItem? _selectedDeviceType;

        [ObservableProperty]
        private BaudRateItem? _selectedBaudRate;

        [ObservableProperty]
        private int _deviceIndex;

        [ObservableProperty]
        private int _channelIndex;

        [ObservableProperty]
        private bool _isConnected;

        [ObservableProperty]
        private bool _isFlashing;

        [ObservableProperty]
        private int _flashingProgress;

        public bool IsFlashingIndeterminate => IsFlashing && FlashingProgress == 0;

        [ObservableProperty]
        private string _flashFilePath = string.Empty;

        [ObservableProperty]
        private long _flashFileSize;

        [ObservableProperty]
        private string _flashFileCrc = string.Empty;

        [ObservableProperty]
        private string _udsPhysicalAddress = "7E0";

        [ObservableProperty]
        private string _udsResponseAddress = "7E8";

        [ObservableProperty]
        private ObservableCollection<LogMessage> _logMessages = new ObservableCollection<LogMessage>();

        [ObservableProperty]
        private ObservableCollection<CanMessageItem> _canMessages = new ObservableCollection<CanMessageItem>();

        [ObservableProperty]
        private string _deviceState = "未知";

        [ObservableProperty]
        private string _deviceLastError = "0";

        [ObservableProperty]
        private string _deviceWrittenBytes = "0";

        [ObservableProperty]
        private string _deviceNextCount = "0";

        [ObservableProperty]
        private string _deviceAddressInput = string.Empty;

        [ObservableProperty]
        private string _deviceAddressReadResult = string.Empty;

        [ObservableProperty]
        private string _snInput = string.Empty;

        [ObservableProperty]
        private string _snReadResult = string.Empty;

        [ObservableProperty]
        private bool _isCalibrationSessionOpen;

        [ObservableProperty]
        private int _selectedAfeIndex = 0;

        [ObservableProperty]
        private int _selectedTcIndex = 0;

        [ObservableProperty]
        private float _kValue = 1.0f;

        [ObservableProperty]
        private float _bValue = 0.0f;

        [ObservableProperty]
        private float _vrefValue = 0.1f;

        [ObservableProperty]
        private float _kReadValue;

        [ObservableProperty]
        private float _bReadValue;

        [ObservableProperty]
        private float _vrefReadValue;

        public ObservableCollection<AfeCalibrationData> AfeCalibrationList { get; } = new();

        public class AfeCalibrationData : ObservableObject
        {
            public int AfeIndex { get; }
            public string AfeLabel => $"AFE {AfeIndex}";

            public ObservableCollection<TcCalibrationData> TcList { get; } = new();

            private float _vrefValue = 0.1f;
            public float VrefValue => _vrefValue;

            private string _vref = "0.1";
            public string Vref
            {
                get => _vref;
                set
                {
                    if (SetProperty(ref _vref, value))
                    {
                        if (float.TryParse(value, out float parsedValue))
                        {
                            _vrefValue = parsedValue;
                        }
                    }
                }
            }

            public void UpdateVrefFromFloat(float vref)
            {
                _vrefValue = vref;
                _vref = vref.ToString("F3");
                OnPropertyChanged(nameof(Vref));
            }

            public AfeCalibrationData(int afeIndex)
            {
                AfeIndex = afeIndex;
                for (int i = 0; i < 8; i++)
                {
                    TcList.Add(new TcCalibrationData(afeIndex, i));
                }
            }
        }

        public class TcCalibrationData : ObservableObject
        {
            public int AfeIndex { get; }
            public int TcIndex { get; }
            public string TcLabel => $"TC {TcIndex}";

            private float _kValue = 1.0f;
            public float KValue => _kValue;

            private string _k = "1.0";
            public string K
            {
                get => _k;
                set
                {
                    if (SetProperty(ref _k, value))
                    {
                        if (float.TryParse(value, out float parsedValue))
                        {
                            _kValue = parsedValue;
                        }
                    }
                }
            }

            private float _bValue = 0.0f;
            public float BValue => _bValue;

            private string _b = "0.0";
            public string B
            {
                get => _b;
                set
                {
                    if (SetProperty(ref _b, value))
                    {
                        if (float.TryParse(value, out float parsedValue))
                        {
                            _bValue = parsedValue;
                        }
                    }
                }
            }

            public void UpdateFromFloat(float k, float b)
            {
                _kValue = k;
                _k = k.ToString("F3");
                _bValue = b;
                _b = b.ToString("F3");
                OnPropertyChanged(nameof(K));
                OnPropertyChanged(nameof(B));
            }

            // 每个 TC 各自的采集电压 x（GP4 电压 [V]）与反解电压 y（热电偶电动势 [V]）
            // 仅用于显示，由 0x18 命令按 AFE/TC 单独读取
            private string _sampleX = "—";
            public string SampleX
            {
                get => _sampleX;
                set => SetProperty(ref _sampleX, value);
            }

            private string _sampleY = "—";
            public string SampleY
            {
                get => _sampleY;
                set => SetProperty(ref _sampleY, value);
            }

            public void UpdateSampleFromFloat(float? x, float? y)
            {
                if (x.HasValue)
                {
                    SampleX = x.Value.ToString("F3");
                }
                if (y.HasValue)
                {
                    SampleY = y.Value.ToString("F3");
                }
            }

            public TcCalibrationData(int afeIndex, int tcIndex)
            {
                AfeIndex = afeIndex;
                TcIndex = tcIndex;
            }
        }

        [ObservableProperty]
        private bool _isBusy;

        // ===== K,B 计算 =====
        public ObservableCollection<int> KbPointCountOptions { get; } = new();

        public ObservableCollection<KbInputItem> KbInputList { get; } = new();

        [ObservableProperty]
        private int _selectedKbPointCount = 3;

        [ObservableProperty]
        private string _kbResultK = string.Empty;

        [ObservableProperty]
        private string _kbResultB = string.Empty;

        [ObservableProperty]
        private string _kbResultMessage = string.Empty;

        public class KbInputItem : ObservableObject
        {
            private int _index;
            public int Index
            {
                get => _index;
                set => SetProperty(ref _index, value);
            }

            public string IndexLabel => $"#{Index}";

            private string _x = string.Empty;
            public string X
            {
                get => _x;
                set => SetProperty(ref _x, value);
            }

            private string _y = string.Empty;
            public string Y
            {
                get => _y;
                set => SetProperty(ref _y, value);
            }
        }

        public enum DeviceMode
        {
            BMU,
            BCMS
        }

        [ObservableProperty]
        private DeviceMode _selectedDeviceMode = DeviceMode.BMU;

        [RelayCommand]
        public void Connect()
        {
            if (SelectedDeviceType == null || SelectedBaudRate == null)
            {
                _logService.Error("请选择设备类型和波特率");
                return;
            }

            try
            {
                _logService.Info($"正在连接设备: {SelectedDeviceType.Name}, 通道: {ChannelIndex}, 波特率: {SelectedBaudRate.Name}");

                bool success = _canService.Connect(
                    SelectedDeviceType.Value,
                    DeviceIndex,
                    ChannelIndex,
                    SelectedBaudRate.Value,
                    useCanFd: SelectedDeviceType.Name == "USBCANFD_200U");

                if (success)
                {
                    IsConnected = true;
                    _logService.Info("CAN设备连接成功");
                }
                else
                {
                    _logService.Error("CAN设备连接失败");
                }
            }
            catch (Exception ex)
            {
                _logService.Error($"连接失败: {ex.Message}");
            }
        }

        [RelayCommand]
        public void Disconnect()
        {
            if (!IsConnected)
                return;

            _canService.Disconnect();
            IsConnected = false;
            IsCalibrationSessionOpen = false;
            IsCalibrationSessionOpen = false;
            _logService.Info("CAN设备已断开连接");
        }

        [RelayCommand]
        public void SelectFlashFile()
        {
            var dialog = new OpenFileDialog();
            dialog.Filter = "BIN文件 (*.bin)|*.bin|所有文件 (*.*)|*.*";
            dialog.Title = "选择刷写文件";

            if (dialog.ShowDialog() == true)
            {
                FlashFilePath = dialog.FileName;
                FlashFileSize = new FileInfo(FlashFilePath).Length;

                if (_flashingService.LoadFlashFile(FlashFilePath))
                {
                    FlashFileCrc = $"0x{_flashingService.Crc32:X8}";
                    _logService.Info($"加载文件成功: {FlashFilePath}, 大小: {FlashFileSize} 字节, CRC: {FlashFileCrc}");
                }
                else
                {
                    _logService.Error($"加载文件失败: {_flashingService.ErrorMessage}");
                }
            }
        }

        [RelayCommand]
        public async Task StartFlashing()
        {
            try
            {
                if (!IsConnected)
                {
                    _logService.Error("请先连接CAN设备");
                    return;
                }

                if (string.IsNullOrEmpty(FlashFilePath))
                {
                    _logService.Error("请先选择刷写文件");
                    return;
                }

                if (IsFlashing)
                    return;

                if (!uint.TryParse(UdsPhysicalAddress, System.Globalization.NumberStyles.HexNumber, null, out uint physicalAddr))
                {
                    _logService.Error("无效的物理地址格式，请输入十六进制地址");
                    return;
                }

                if (!uint.TryParse(UdsResponseAddress, System.Globalization.NumberStyles.HexNumber, null, out uint responseAddr))
                {
                    _logService.Error("无效的响应地址格式，请输入十六进制地址");
                    return;
                }

                _flashingService.SendId = physicalAddr;
                _flashingService.ReceiveId = responseAddr;

                _messageManager.SetUdsFilter(physicalAddr, responseAddr);

                IsFlashing = true;
                FlashingProgress = 0;

                bool success = await _flashingService.StartFlashingAsync();

                IsFlashing = false;

                if (success)
                {
                    _logService.Info("刷写成功！");
                }
                else
                {
                    _logService.Error($"刷写失败: {_flashingService.ErrorMessage}");
                }
            }
            catch (Exception ex)
            {
                IsFlashing = false;
                _logService.Error($"刷写异常: {ex.Message}");
            }
        }

        [RelayCommand]
        public void StopFlashing()
        {
            if (!IsFlashing)
                return;

            _flashingService.Cancel();
            _logService.Info("刷写已取消");
        }

        [RelayCommand]
        public void ClearLogs()
        {
            _logService.Clear();
        }

        [RelayCommand]
        public void ClearMessages()
        {
            _messageManager.Clear();
        }

        [RelayCommand]
        public async Task OpenSnSession()
        {
            if (IsBusy) return;
            IsBusy = true;

            try
            {
                if (!IsConnected)
                {
                    _logService.Error("请先连接CAN设备");
                    return;
                }

                if (!uint.TryParse(UdsPhysicalAddress, System.Globalization.NumberStyles.HexNumber, null, out uint physicalAddr))
                {
                    _logService.Error("无效的物理地址格式");
                    return;
                }

                if (!uint.TryParse(UdsResponseAddress, System.Globalization.NumberStyles.HexNumber, null, out uint responseAddr))
                {
                    _logService.Error("无效的响应地址格式");
                    return;
                }

                _snService.SendId = physicalAddr;
                _snService.ReceiveId = responseAddr;

                _logService.Info("正在打开SN会话...");
                bool success = await _snService.OpenSessionAsync();

                if (success)
                {
                    IsCalibrationSessionOpen = true;
                    _logService.Info("SN会话已打开");
                }
                else
                {
                    _logService.Error($"打开SN会话失败: {_snService.ErrorMessage}");
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task CloseSnSession()
        {
            if (IsBusy) return;
            IsBusy = true;

            try
            {
                _logService.Info("正在关闭SN会话...");
                await _snService.CloseSessionAsync();
                IsCalibrationSessionOpen = false;
                _logService.Info("SN会话已关闭");
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task ReadDeviceAddress()
        {
            if (IsBusy) return;
            IsBusy = true;

            try
            {
                if (!IsCalibrationSessionOpen)
                {
                    _logService.Error("请先打开会话");
                    return;
                }

                _logService.Info("正在读取设备地址...");
                byte? address = await _snService.ReadDeviceAddressAsync();

                if (address.HasValue)
                {
                    DeviceAddressReadResult = address.Value.ToString();
                    _logService.Info($"读取设备地址成功: {address.Value}");
                }
                else
                {
                    _logService.Error($"读取设备地址失败: {_snService.ErrorMessage}");
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task WriteDeviceAddress()
        {
            if (IsBusy) return;
            IsBusy = true;

            try
            {
                if (!IsCalibrationSessionOpen)
                {
                    _logService.Error("请先打开会话");
                    return;
                }

                if (string.IsNullOrEmpty(DeviceAddressInput))
                {
                    _logService.Error("设备地址不能为空");
                    return;
                }

                if (!byte.TryParse(DeviceAddressInput, out byte address))
                {
                    _logService.Error("设备地址格式无效，请输入数字");
                    return;
                }

                int maxAddress = SelectedDeviceMode == DeviceMode.BMU ? 14 : 4;
                
                if (address > maxAddress)
                {
                    _logService.Error($"设备地址必须为0或1~{maxAddress}");
                    return;
                }

                _logService.Info($"正在写入设备地址: {address}");
                bool success = await _snService.WriteDeviceAddressAsync(address);

                if (success)
                {
                    _logService.Info("设备地址写入成功（RAM），请保存到Flash");
                }
                else
                {
                    _logService.Error($"设备地址写入失败: {_snService.ErrorMessage}");
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task ReadSn()
        {
            if (IsBusy) return;
            IsBusy = true;

            try
            {
                if (!IsCalibrationSessionOpen)
                {
                    _logService.Error("请先打开SN会话");
                    return;
                }

                _logService.Info("正在读取SN...");
                string sn = await _snService.ReadSnAsync();

                if (!string.IsNullOrEmpty(sn))
                {
                    SnReadResult = sn;
                    _logService.Info($"读取SN成功: {sn}");
                }
                else
                {
                    _logService.Error($"读取SN失败: {_snService.ErrorMessage}");
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task WriteSn()
        {
            if (IsBusy) return;
            IsBusy = true;

            try
            {
                if (!IsCalibrationSessionOpen)
                {
                    _logService.Error("请先打开SN会话");
                    return;
                }

                if (string.IsNullOrEmpty(SnInput))
                {
                    _logService.Error("SN不能为空");
                    return;
                }

                if (SnInput.Length > 31)
                {
                    _logService.Error("SN长度不能超过31字符");
                    return;
                }

                _logService.Info($"正在写入SN: {SnInput}");
                bool success = await _snService.WriteSnAsync(SnInput);

                if (success)
                {
                    _logService.Info("SN写入成功（RAM），请保存到Flash");
                }
                else
                {
                    _logService.Error($"SN写入失败: {_snService.ErrorMessage}");
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task OpenCalibrationSession()
        {
            if (IsBusy) return;
            IsBusy = true;

            try
            {
                if (!IsConnected)
                {
                    _logService.Error("请先连接CAN设备");
                    return;
                }

                if (!uint.TryParse(UdsPhysicalAddress, System.Globalization.NumberStyles.HexNumber, null, out uint physicalAddr))
                {
                    _logService.Error("无效的物理地址格式");
                    return;
                }

                if (!uint.TryParse(UdsResponseAddress, System.Globalization.NumberStyles.HexNumber, null, out uint responseAddr))
                {
                    _logService.Error("无效的响应地址格式");
                    return;
                }

                _calibrationService.SendId = physicalAddr;
                _calibrationService.ReceiveId = responseAddr;

                _logService.Info("正在打开会话...");
                bool success = await _calibrationService.OpenSessionAsync();

                if (success)
                {
                    IsCalibrationSessionOpen = true;
                    _logService.Info("标定已打开");
                    StartStatusLoop();
                }
                else
                {
                    _logService.Error($"打开会话失败: {_calibrationService.ErrorMessage}");
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task CloseCalibrationSession()
        {
            if (IsBusy) return;
            IsBusy = true;

            try
            {
                _logService.Info("正在关闭会话...");
                await _calibrationService.CloseSessionAsync();
                IsCalibrationSessionOpen = false;
                StopStatusLoop();
                _logService.Info("会话已关闭");
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task WriteKValue()
        {
            if (IsBusy) return;
            IsBusy = true;

            try
            {
                if (!IsCalibrationSessionOpen)
                {
                    _logService.Error("请先打开会话");
                    return;
                }

                _logService.Info($"正在写入k值: AFE={SelectedAfeIndex}, TC={SelectedTcIndex}, k={KValue}");
                bool success = await _calibrationService.WriteCalibrationParamAsync(
                    CalibrationParamType.K, SelectedAfeIndex, SelectedTcIndex, KValue);

                if (success)
                {
                    _logService.Info("k值写入成功（RAM）");
                }
                else
                {
                    _logService.Error($"k值写入失败: {_calibrationService.ErrorMessage}");
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task WriteBValue()
        {
            if (IsBusy) return;
            IsBusy = true;

            try
            {
                if (!IsCalibrationSessionOpen)
                {
                    _logService.Error("请先打开会话");
                    return;
                }

                _logService.Info($"正在写入b值: AFE={SelectedAfeIndex}, TC={SelectedTcIndex}, b={BValue}");
                bool success = await _calibrationService.WriteCalibrationParamAsync(
                    CalibrationParamType.B, SelectedAfeIndex, SelectedTcIndex, BValue);

                if (success)
                {
                    _logService.Info("b值写入成功（RAM）");
                }
                else
                {
                    _logService.Error($"b值写入失败: {_calibrationService.ErrorMessage}");
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task WriteVrefValue()
        {
            if (IsBusy) return;
            IsBusy = true;

            try
            {
                if (!IsCalibrationSessionOpen)
                {
                    _logService.Error("请先打开会话");
                    return;
                }

                _logService.Info($"正在写入vref值: AFE={SelectedAfeIndex}, vref={VrefValue}");
                bool success = await _calibrationService.WriteCalibrationParamAsync(
                    CalibrationParamType.Vref, SelectedAfeIndex, 0, VrefValue);

                if (success)
                {
                    _logService.Info("vref值写入成功（RAM）");
                }
                else
                {
                    _logService.Error($"vref值写入失败: {_calibrationService.ErrorMessage}");
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task ReadKValue()
        {
            if (IsBusy) return;
            IsBusy = true;

            try
            {
                if (!IsCalibrationSessionOpen)
                {
                    _logService.Error("请先打开会话");
                    return;
                }

                _logService.Info($"正在读取k值: AFE={SelectedAfeIndex}, TC={SelectedTcIndex}");
                float? value = await _calibrationService.ReadCalibrationParamAsync(
                    CalibrationParamType.K, SelectedAfeIndex, SelectedTcIndex);

                if (value.HasValue)
                {
                    KReadValue = value.Value;
                    _logService.Info($"读取k值成功: {value.Value}");
                }
                else
                {
                    _logService.Error($"读取k值失败: {_calibrationService.ErrorMessage}");
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task ReadBValue()
        {
            if (IsBusy) return;
            IsBusy = true;

            try
            {
                if (!IsCalibrationSessionOpen)
                {
                    _logService.Error("请先打开会话");
                    return;
                }

                _logService.Info($"正在读取b值: AFE={SelectedAfeIndex}, TC={SelectedTcIndex}");
                float? value = await _calibrationService.ReadCalibrationParamAsync(
                    CalibrationParamType.B, SelectedAfeIndex, SelectedTcIndex);

                if (value.HasValue)
                {
                    BReadValue = value.Value;
                    _logService.Info($"读取b值成功: {value.Value}");
                }
                else
                {
                    _logService.Error($"读取b值失败: {_calibrationService.ErrorMessage}");
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task ReadVrefValue()
        {
            if (IsBusy) return;
            IsBusy = true;

            try
            {
                if (!IsCalibrationSessionOpen)
                {
                    _logService.Error("请先打开会话");
                    return;
                }

                _logService.Info($"正在读取vref值: AFE={SelectedAfeIndex}");
                float? value = await _calibrationService.ReadCalibrationParamAsync(
                    CalibrationParamType.Vref, SelectedAfeIndex, 0);

                if (value.HasValue)
                {
                    VrefReadValue = value.Value;
                    _logService.Info($"读取vref值成功: {value.Value}");
                }
                else
                {
                    _logService.Error($"读取vref值失败: {_calibrationService.ErrorMessage}");
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task SaveCalibration()
        {
            if (IsBusy) return;
            IsBusy = true;

            try
            {
                if (!IsCalibrationSessionOpen)
                {
                    _logService.Error("请先打开会话");
                    return;
                }

                _logService.Info("正在保存标定参数到Flash...");
                bool success = await _calibrationService.SaveCalibrationAsync();

                if (success)
                {
                    _logService.Info("标定参数保存成功（Flash）");
                }
                else
                {
                    _logService.Error($"保存标定参数失败: {_calibrationService.ErrorMessage}");
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task SaveSn()
        {
            if (IsBusy) return;
            IsBusy = true;

            try
            {
                if (!IsCalibrationSessionOpen)
                {
                    _logService.Error("请先打开会话");
                    return;
                }

                _logService.Info("正在保存SN到Flash...");
                bool success = await _snService.SaveSnAsync();

                if (success)
                {
                    _logService.Info("SN保存成功（Flash）");
                }
                else
                {
                    _logService.Error($"保存SN失败: {_snService.ErrorMessage}");
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task OneClickCalibration()
        {
            if (IsBusy) return;
            IsBusy = true;

            try
            {
                if (!IsCalibrationSessionOpen)
                {
                    _logService.Error("请先打开会话");
                    return;
                }

                _logService.Info("开始一键标定...");

                float[,] kValues = new float[3, 8];
                float[,] bValues = new float[3, 8];
                float[] vrefValues = new float[3];

                for (int afe = 0; afe < 3; afe++)
                {
                    var afeData = AfeCalibrationList[afe];
                    vrefValues[afe] = afeData.VrefValue;
                    for (int tc = 0; tc < 8; tc++)
                    {
                        var tcData = afeData.TcList[tc];
                        kValues[afe, tc] = tcData.KValue;
                        bValues[afe, tc] = tcData.BValue;
                    }
                }

                bool success = await _calibrationService.WriteAllCalibrationParamsAsync(kValues, bValues, vrefValues);

                if (success)
                {
                    _logService.Info("一键标定完成（RAM）");
                    _logService.Info("正在保存到Flash...");

                    success = await _calibrationService.SaveCalibrationAsync();
                    if (success)
                    {
                        _logService.Info("标定参数已保存到Flash");
                    }
                    else
                    {
                        _logService.Error($"保存到Flash失败: {_calibrationService.ErrorMessage}");
                    }
                }
                else
                {
                    _logService.Error($"一键标定失败: {_calibrationService.ErrorMessage}");
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task ReadSingleTc(TcCalibrationData tcData)
        {
            if (IsBusy) return;
            IsBusy = true;

            try
            {
                if (!IsCalibrationSessionOpen)
                {
                    _logService.Error("请先打开会话");
                    return;
                }

                _logService.Info($"正在读取热电偶参数: AFE={tcData.AfeIndex}, TC={tcData.TcIndex}");

                float? kValue = await _calibrationService.ReadCalibrationParamAsync(
                    CalibrationParamType.K, tcData.AfeIndex, tcData.TcIndex);
                await Task.Delay(10);
                float? bValue = await _calibrationService.ReadCalibrationParamAsync(
                    CalibrationParamType.B, tcData.AfeIndex, tcData.TcIndex);

                if (kValue.HasValue && bValue.HasValue)
                {
                    tcData.UpdateFromFloat(kValue.Value, bValue.Value);
                    _logService.Info($"热电偶参数读取成功: AFE={tcData.AfeIndex}, TC={tcData.TcIndex}, k={kValue.Value}, b={bValue.Value}");
                }
                else
                {
                    _logService.Error($"热电偶参数读取失败: {_calibrationService.ErrorMessage}");
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task WriteSingleTc(TcCalibrationData tcData)
        {
            if (IsBusy) return;
            IsBusy = true;

            try
            {
                if (!IsCalibrationSessionOpen)
                {
                    _logService.Error("请先打开会话");
                    return;
                }

                _logService.Info($"正在写入热电偶参数: AFE={tcData.AfeIndex}, TC={tcData.TcIndex}, k={tcData.KValue}, b={tcData.BValue}");

                bool kSuccess = await _calibrationService.WriteCalibrationParamAsync(
                    CalibrationParamType.K, tcData.AfeIndex, tcData.TcIndex, tcData.KValue);
                await Task.Delay(10);
                bool bSuccess = await _calibrationService.WriteCalibrationParamAsync(
                    CalibrationParamType.B, tcData.AfeIndex, tcData.TcIndex, tcData.BValue);

                if (kSuccess && bSuccess)
                {
                    _logService.Info($"热电偶参数写入成功（RAM）: AFE={tcData.AfeIndex}, TC={tcData.TcIndex}");
                }
                else
                {
                    _logService.Error($"热电偶参数写入失败: {_calibrationService.ErrorMessage}");
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task ReadSingleVref(AfeCalibrationData afeData)
        {
            if (IsBusy) return;
            IsBusy = true;

            try
            {
                if (!IsCalibrationSessionOpen)
                {
                    _logService.Error("请先打开会话");
                    return;
                }

                _logService.Info($"正在读取Vref: AFE={afeData.AfeIndex}");

                float? value = await _calibrationService.ReadCalibrationParamAsync(
                    CalibrationParamType.Vref, afeData.AfeIndex, 0);

                if (value.HasValue)
                {
                    afeData.UpdateVrefFromFloat(value.Value);
                    _logService.Info($"Vref读取成功: AFE={afeData.AfeIndex}, vref={value.Value}");
                }
                else
                {
                    _logService.Error($"Vref读取失败: {_calibrationService.ErrorMessage}");
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task WriteSingleVref(AfeCalibrationData afeData)
        {
            if (IsBusy) return;
            IsBusy = true;

            try
            {
                if (!IsCalibrationSessionOpen)
                {
                    _logService.Error("请先打开会话");
                    return;
                }

                _logService.Info($"正在写入Vref: AFE={afeData.AfeIndex}, vref={afeData.VrefValue}");

                bool success = await _calibrationService.WriteCalibrationParamAsync(
                    CalibrationParamType.Vref, afeData.AfeIndex, 0, afeData.VrefValue);

                if (success)
                {
                    _logService.Info($"Vref写入成功（RAM）: AFE={afeData.AfeIndex}");
                }
                else
                {
                    _logService.Error($"Vref写入失败: {_calibrationService.ErrorMessage}");
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task ReadAllCalibration()
        {
            if (IsBusy) return;
            IsBusy = true;

            try
            {
                if (!IsCalibrationSessionOpen)
                {
                    _logService.Error("请先打开会话");
                    return;
                }

                _logService.Info("开始一键读取所有标定参数...");

                foreach (var afeData in AfeCalibrationList)
                {
                    float? vref = await _calibrationService.ReadCalibrationParamAsync(
                        CalibrationParamType.Vref, afeData.AfeIndex, 0);
                    if (vref.HasValue)
                    {
                        afeData.UpdateVrefFromFloat(vref.Value);
                    }
                    await Task.Delay(10);

                    foreach (var tcData in afeData.TcList)
                    {
                        float? k = await _calibrationService.ReadCalibrationParamAsync(
                            CalibrationParamType.K, tcData.AfeIndex, tcData.TcIndex);
                        await Task.Delay(10);
                        float? b = await _calibrationService.ReadCalibrationParamAsync(
                            CalibrationParamType.B, tcData.AfeIndex, tcData.TcIndex);

                        if (k.HasValue && b.HasValue)
                        {
                            tcData.UpdateFromFloat(k.Value, b.Value);
                        }
                        await Task.Delay(10);
                    }
                }

                _logService.Info("一键读取所有标定参数完成");
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task RestoreDefaults()
        {
            if (IsBusy) return;
            IsBusy = true;

            try
            {
                if (!IsCalibrationSessionOpen)
                {
                    _logService.Error("请先打开会话");
                    return;
                }

                _logService.Info("正在恢复标定默认值...");
                bool success = await _calibrationService.RestoreDefaultsAsync();

                if (success)
                {
                    _logService.Info("标定默认值恢复成功（RAM）");
                    foreach (var afe in AfeCalibrationList)
                    {
                        afe.UpdateVrefFromFloat(0.1f);
                        foreach (var tc in afe.TcList)
                        {
                            tc.UpdateFromFloat(1.0f, 0.0f);
                        }
                    }
                }
                else
                {
                    _logService.Error($"恢复标定默认值失败: {_calibrationService.ErrorMessage}");
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public async Task ReadSingleSample(TcCalibrationData tcData)
        {
            if (IsBusy) return;
            IsBusy = true;

            try
            {
                if (!IsCalibrationSessionOpen)
                {
                    _logService.Error("请先打开会话");
                    return;
                }

                _logService.Info($"正在读取采集电压: AFE={tcData.AfeIndex}, TC={tcData.TcIndex}");

                // 0x18 按 AFE/TC 单独读取，每个 TC 都有独立的 x 与 y
                float? xValue = await _calibrationService.ReadSampleAsync(
                    SampleType.VoltageX, tcData.AfeIndex, tcData.TcIndex);
                await Task.Delay(10);
                float? yValue = await _calibrationService.ReadSampleAsync(
                    SampleType.VoltageY, tcData.AfeIndex, tcData.TcIndex);

                if (xValue.HasValue && yValue.HasValue)
                {
                    tcData.UpdateSampleFromFloat(xValue, yValue);
                    _logService.Info($"采集电压读取成功: AFE={tcData.AfeIndex}, TC={tcData.TcIndex}, x={xValue.Value:F6} V, y={yValue.Value:F6} V");
                }
                else
                {
                    _logService.Error($"采集电压读取失败: {_calibrationService.ErrorMessage}");
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        [RelayCommand]
        public void ToggleStatusLoop()
        {
            if (IsStatusLooping)
            {
                StopStatusLoop();
            }
            else
            {
                StartStatusLoop();
            }
        }

        private void StartStatusLoop()
        {
            if (!IsConnected)
            {
                _logService.Error("请先连接CAN设备");
                return;
            }

            IsStatusLooping = true;
            _statusTimer.Start();
            _logService.Info("开始循环查询设备状态...");
            _ = GetDeviceStatusAsync();
        }

        private void StopStatusLoop()
        {
            IsStatusLooping = false;
            _statusTimer.Stop();
            _logService.Info("停止循环查询设备状态");
        }

        private async void StatusTimer_Tick(object? sender, EventArgs e)
        {
            await GetDeviceStatusAsync();
        }

        private async Task GetDeviceStatusAsync()
        {
            if (IsBusy) return;
            IsBusy = true;

            try
            {
                if (!uint.TryParse(UdsPhysicalAddress, System.Globalization.NumberStyles.HexNumber, null, out uint physicalAddr))
                {
                    _logService.Error("无效的物理地址格式");
                    return;
                }

                if (!uint.TryParse(UdsResponseAddress, System.Globalization.NumberStyles.HexNumber, null, out uint responseAddr))
                {
                    _logService.Error("无效的响应地址格式");
                    return;
                }

                _flashingService.SendId = physicalAddr;
                _flashingService.ReceiveId = responseAddr;

                var status = await _flashingService.GetStatusAsync();

                if (status != null)
                {
                    DeviceState = status.StateText;
                    DeviceLastError = $"0x{status.LastError:X2}";
                    DeviceWrittenBytes = status.WrittenBytes.ToString();
                    DeviceNextCount = status.NextExpectedCount.ToString();
                }
                else
                {
                    _logService.Error($"设备状态查询失败: {_flashingService.ErrorMessage}");
                }
            }
            finally
            {
                IsBusy = false;
            }
        }

        // ===== K,B 计算 =====
        [RelayCommand]
        public void CalculateKb()
        {
            // 收集并校验输入
            var items = KbInputList.ToList();
            var xs = new List<double>(items.Count);
            var ys = new List<double>(items.Count);

            for (int i = 0; i < items.Count; i++)
            {
                var it = items[i];
                if (!double.TryParse(it.X?.Trim(), out double xv))
                {
                    KbResultMessage = $"第 {it.Index} 行 X 不是合法数字";
                    KbResultK = string.Empty;
                    KbResultB = string.Empty;
                    _logService.Error($"K,B 计算：第 {it.Index} 行 X 不是合法数字");
                    return;
                }
                if (!double.TryParse(it.Y?.Trim(), out double yv))
                {
                    KbResultMessage = $"第 {it.Index} 行 Y 不是合法数字";
                    KbResultK = string.Empty;
                    KbResultB = string.Empty;
                    _logService.Error($"K,B 计算：第 {it.Index} 行 Y 不是合法数字");
                    return;
                }
                xs.Add(xv);
                ys.Add(yv);
            }

            var result = GetAAndB(xs.ToArray(), ys.ToArray());
            if (result == null)
            {
                KbResultMessage = "所有 x 值相同，无法拟合直线";
                KbResultK = string.Empty;
                KbResultB = string.Empty;
                _logService.Error("K,B 计算：所有 x 值相同，无法拟合直线");
                return;
            }

            double a = result.Value.Item1;
            double b = result.Value.Item2;
            if (double.IsNaN(a) || double.IsNaN(b))
            {
                KbResultMessage = "数据长度不一致，无法计算";
                KbResultK = string.Empty;
                KbResultB = string.Empty;
                _logService.Error("K,B 计算：数据长度不一致");
                return;
            }

            KbResultK = a.ToString("F3");
            KbResultB = b.ToString("F3");
            KbResultMessage = $"计算成功：y = {a:F3} * x + {b:F3}";
            _logService.Info($"K,B 计算完成：K(a)={a:F3}, B(b)={b:F3}");
        }

        [RelayCommand]
        public void ClearKb()
        {
            foreach (var it in KbInputList)
            {
                it.X = string.Empty;
                it.Y = string.Empty;
            }
            KbResultK = string.Empty;
            KbResultB = string.Empty;
            KbResultMessage = string.Empty;
        }

        /// <summary>
        /// 最小二乘法拟合 y = a*x + b。
        /// a 经 ushort 量化（×1000 取绝对值），b 经 short 量化（×1000）。
        /// 所有 x 相同时返回 null；所有 y 相同时返回水平线 (0, y0)。
        /// </summary>
        public static (double, double)? GetAAndB(double[] x, double[] y)
        {
            // 确保数组长度一致
            if (x.Length != y.Length)
            {
                return (double.NaN, double.NaN);
            }
            if (x.Distinct().Count() == 1)
            {
                return null;
            }

            if (y.Distinct().Count() == 1)
            {
                double c = y[0];
                return (0, c);
            }

            int n = x.Length;

            double sX = x.Sum();
            double sY = y.Sum();
            double sXY = 0;
            for (int i = 0; i < x.Length; i++)
            {
                sXY += x[i] * y[i];
            }
            double sX2 = x.Sum(m => m * m);
            double slope = (n * sXY - sX * sY) / (n * sX2 - sX * sX);
            double intercept = (sY - slope * sX) / n;

            ushort aW = (ushort)(Math.Abs(slope) * 1000);
            var aWTemp = aW / 1000.0;
            short bW = (short)(intercept * 1000);
            var bWTemp = bW / 1000.0;

            // 计算所需的和
            double sumX = x.Sum();
            double sumY = y.Sum();
            double sumXY = x.Zip(y, (xi, yi) => xi * yi).Sum();
            double sumX2 = x.Sum(xi => xi * xi);

            // 计算斜率a和截距b
            double a = (n * sumXY - sumX * sumY) / (n * sumX2 - sumX * sumX);
            double b = (sumY - a * sumX) / n;
            return (aWTemp, bWTemp);
        }

    }

    public class DeviceTypeItem
    {
        public string Name { get; set; } = string.Empty;
        public CanDeviceType Value { get; set; }
    }

    public class BaudRateItem
    {
        public string Name { get; set; } = string.Empty;
        public CanBaudRate Value { get; set; }
    }
}