using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;

namespace BOOTWR.Services
{
    public enum FlashingState
    {
        Idle,
        Connecting,
        Session,
        Erase,
        SendingData,
        WaitingForResponse,
        Commit,
        Completed,
        Error
    }

    public enum FlashingError
    {
        None,
        ConnectionFailed,
        Timeout,
        InvalidResponse,
        CrcError,
        FileError,
        UnknownError
    }

    public enum DeviceState
    {
        Idle = 0,
        Session = 1,
        Receiving = 2,
        Ready = 3,
        Error = 4
    }

    public class DeviceStatusInfo
    {
        public DeviceState State { get; set; }
        public byte LastError { get; set; }
        public uint WrittenBytes { get; set; }
        public byte NextExpectedCount { get; set; }

        public string StateText
        {
            get
            {
                switch (State)
                {
                    case DeviceState.Idle: return "IDLE";
                    case DeviceState.Session: return "SESSION";
                    case DeviceState.Receiving: return "RECEIVING";
                    case DeviceState.Ready: return "READY";
                    case DeviceState.Error: return "ERROR";
                    default: return $"UNKNOWN({(int)State})";
                }
            }
        }
    }

    /// <summary>广播烧录中的一个产品槽：应答ID = 应答基址 + 序号，追踪该产品的确认进度与失败信息。</summary>
    public partial class FlashSlot : ObservableObject
    {
        public int Index { get; set; }
        public uint ResponseId { get; set; }
        public byte AckedCmd { get; set; }
        public FlashingError Error { get; set; }

        [ObservableProperty] private string _stateText = "待命";
        [ObservableProperty] private string _errorMessage = string.Empty;

        public bool IsOk => Error == FlashingError.None;
        public string IndexText => $"#{Index}  0x{ResponseId:X3}";

        public void SetAck(byte cmd)
        {
            AckedCmd = cmd;
            Error = FlashingError.None;
            ErrorMessage = string.Empty;
            StateText = cmd switch
            {
                0x50 => "会话OK",
                0x60 => "擦除OK",
                0x65 => "烧录OK",
                0x70 => "提交OK",
                _ => "已确认"
            };
        }

        public void SetError(FlashingError err, string message)
        {
            Error = err;
            ErrorMessage = message;
            StateText = "失败";
        }
    }

    public class FlashingService
    {
        private readonly CanCommunicationService _canService;
        private readonly UdsAddressSettings _address;
        private byte[] _flashData = Array.Empty<byte>();
        private uint _crc32;
        private FlashingState _state = FlashingState.Idle;
        private FlashingError _error = FlashingError.None;
        private string _errorMessage = string.Empty;
        private int _currentProgress = 0;
        private object _lockObj = new object();
        private CancellationTokenSource? _cancellationTokenSource;
        private FlashSlot[] _slots = Array.Empty<FlashSlot>();

        private const int TIMEOUT_MS = 30000;

        public uint SendId
        {
            get => _address.PhysicalAddress;
            set => _address.PhysicalAddress = value;
        }

        public uint ReceiveId
        {
            get => _address.ResponseAddress;
            set => _address.ResponseAddress = value;
        }

        private const byte ECU_CAN_PROG_RES_OK = 0x00;
        private const byte ECU_CAN_PROG_RES_BUSY = 0x01;
        private const byte ECU_CAN_PROG_RES_KEY_ERR = 0x02;
        private const byte ECU_CAN_PROG_RES_STATE_ERR = 0x03;
        private const byte ECU_CAN_PROG_RES_PARAM_ERR = 0x04;
        private const byte ECU_CAN_PROG_RES_FLASH_ERR = 0x05;
        private const byte ECU_CAN_PROG_RES_CRC_ERR = 0x06;
        private const byte ECU_CAN_PROG_RES_CNT_ERR = 0x07;

        public bool IsFlashing => _state != FlashingState.Idle && _state != FlashingState.Completed && _state != FlashingState.Error;

        public FlashingState State => _state;
        public FlashingError Error => _error;
        public string ErrorMessage => _errorMessage;
        public int CurrentProgress => _currentProgress;
        public FlashSlot[] Slots => _slots;

        public event EventHandler<FlashingStateChangedEventArgs>? StateChanged;
        public event EventHandler<ProgressChangedEventArgs>? ProgressChanged;

        public FlashingService(CanCommunicationService canService, UdsAddressSettings address)
        {
            _canService = canService;
            _address = address;
        }

        public bool LoadFlashFile(string filePath)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    SetError(FlashingError.FileError, "文件不存在");
                    return false;
                }

                _flashData = File.ReadAllBytes(filePath);
                _crc32 = CalculateCrc32(_flashData);

                return true;
            }
            catch (Exception ex)
            {
                SetError(FlashingError.FileError, ex.Message);
                return false;
            }
        }

        public byte[] FlashData => _flashData;
        public uint Crc32 => _crc32;

        public async Task<bool> StartFlashingAsync()
        {
            if (!_canService.IsConnected)
            {
                SetError(FlashingError.ConnectionFailed, "CAN设备未连接");
                return false;
            }

            if (_flashData.Length == 0)
            {
                SetError(FlashingError.FileError, "未加载刷写文件");
                return false;
            }

            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                _canService.ClearBuffer();

                ChangeState(FlashingState.Session);

                if (!await SendSessionAsync())
                    return false;

                ChangeState(FlashingState.Erase);

                if (!await SendEraseAsync())
                    return false;

                ChangeState(FlashingState.SendingData);

                if (!await SendDataFramesAsync())
                    return false;

                ChangeState(FlashingState.WaitingForResponse);

                if (!await WaitForCompleteResponseAsync())
                    return false;

                ChangeState(FlashingState.Commit);

                if (!await SendCommitAsync())
                    return false;

                ChangeState(FlashingState.Completed);
                SetProgress(100);

                return true;
            }
            catch (OperationCanceledException)
            {
                SetError(FlashingError.Timeout, "操作被取消");
                return false;
            }
            catch (Exception ex)
            {
                SetError(FlashingError.UnknownError, ex.Message);
                return false;
            }
            finally
            {
                _cancellationTokenSource?.Dispose();
            }
        }

        public void Cancel()
        {
            // 内部 CTS 在每次操作结束后会被 Dispose；操作间隙调用时可能指向已释放实例，需容忍 ObjectDisposedException
            try
            {
                _cancellationTokenSource?.Cancel();
            }
            catch (ObjectDisposedException)
            {
            }
        }

        /// <summary>
        /// BMU 广播烧录：广播发送ID发一帧，总线上所有产品同时接收；每个产品用「应答基址+序号」的应答ID回帧区分。
        /// 失败容错：某产品在任一步失败(负响应/无效/超时)只记录该槽、不中断流程，直到全部完成或全部失败。
        /// </summary>
        public async Task<bool> StartBroadcastFlashingAsync(int deviceCount)
        {
            if (!_canService.IsConnected)
            {
                SetError(FlashingError.ConnectionFailed, "CAN设备未连接");
                return false;
            }

            if (_flashData.Length == 0)
            {
                SetError(FlashingError.FileError, "未加载刷写文件");
                return false;
            }

            if (deviceCount < 1)
            {
                SetError(FlashingError.InvalidResponse, "烧录数量必须≥1");
                return false;
            }

            _slots = Enumerable.Range(0, deviceCount)
                .Select(i => new FlashSlot { Index = i, ResponseId = ReceiveId + (uint)i })
                .ToArray();
            var active = _slots.ToList();

            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                _canService.ClearBuffer();

                // 1 SESSION：广播一帧，等存活产品全部 0x50
                ChangeState(FlashingState.Session);
                _canService.SendMessage(SendId, new byte[] { 0x10, 0x42, 0x4D, 0x53, 0x31, 0, 0, 0 });
                await WaitAllAckAsync(active, 0x50, TIMEOUT_MS);
                if (active.Count == 0) { SetError(FlashingError.Timeout, "全部产品失败，流程提前终止"); return false; }

                // 2 ERASE：广播 长度+CRC，等存活产品全部 0x60
                ChangeState(FlashingState.Erase);
                _canService.SendMessage(SendId, BuildEraseFrame());
                await WaitAllAckAsync(active, 0x60, TIMEOUT_MS);
                if (active.Count == 0) { SetError(FlashingError.Timeout, "全部产品失败，流程提前终止"); return false; }

                // 3 数据帧：广播发送，存活产品同时接收，无应答
                ChangeState(FlashingState.SendingData);
                await SendDataFramesAsync();

                // 4 COMPLETE：等存活产品全部 0x65 并校验各自接收长度
                ChangeState(FlashingState.WaitingForResponse);
                await WaitAllAckAsync(active, 0x65, TIMEOUT_MS, checkLength: true);
                if (active.Count == 0) { SetError(FlashingError.Timeout, "全部产品失败，流程提前终止"); return false; }

                // 5 COMMIT：广播，等存活产品全部 0x70
                ChangeState(FlashingState.Commit);
                _canService.SendMessage(SendId, new byte[] { 0x30, 0xA5, 0x5A, 0xA5, 0x5A, 0, 0, 0 });
                await WaitAllAckAsync(active, 0x70, TIMEOUT_MS);

                ChangeState(FlashingState.Completed);
                SetProgress(100);

                return active.Count > 0;   // 至少一台完成 COMMIT 即视为整轮完成
            }
            catch (OperationCanceledException)
            {
                SetError(FlashingError.Timeout, "操作被取消");
                return false;
            }
            catch (Exception ex)
            {
                SetError(FlashingError.UnknownError, ex.Message);
                return false;
            }
            finally
            {
                _cancellationTokenSource?.Dispose();
            }
        }

        /// <summary>
        /// 等待存活产品在本阶段全部应答。失败(负响应/无效/超时)只剔除该槽、不终止流程，
        /// 直到存活槽全部确认，或全部失败(active 为空)由调用方判定是否提前停。
        /// </summary>
        private async Task WaitAllAckAsync(List<FlashSlot> active, byte expectCmd, int timeoutMs, bool checkLength = false)
        {
            var deadline = DateTime.Now.AddMilliseconds(timeoutMs);

            while (active.Count > 0)
            {
                if (_cancellationTokenSource?.IsCancellationRequested ?? false)
                    throw new OperationCanceledException();

                if (DateTime.Now > deadline)
                {
                    foreach (var s in active)
                        s.SetError(FlashingError.Timeout, "等待应答超时");
                    active.Clear();
                    return;
                }

                if (_canService.ReceiveMessage(out uint canId, out _, out byte[] data))
                {
                    var slot = active.FirstOrDefault(x => x.ResponseId == canId);
                    if (slot != null)
                    {
                        bool negative = data.Length > 0 && data[0] == 0x7F;
                        bool bad = data.Length == 0
                            || negative
                            || data[0] != expectCmd
                            || (checkLength && data.Length >= 5 &&
                                (uint)(data[2] << 16 | data[3] << 8 | data[4]) != _flashData.Length);
                        if (bad)
                        {
                            string reason = negative
                                ? $"消极响应 0x{(data.Length > 1 ? data[1] : 0):X2}"
                                : $"期望0x{expectCmd:X2} 收到0x{(data.Length > 0 ? data[0] : 0):X2}";
                            slot.SetError(FlashingError.InvalidResponse, reason);
                            active.Remove(slot);
                            continue;
                        }
                        slot.SetAck(expectCmd);
                    }
                }

                await Task.Delay(1);
            }
        }

        private byte[] BuildEraseFrame()
        {
            int size = _flashData.Length;
            return new byte[]
            {
                0x20,
                (byte)((size >> 16) & 0xFF), (byte)((size >> 8) & 0xFF), (byte)(size & 0xFF),
                (byte)((_crc32 >> 24) & 0xFF), (byte)((_crc32 >> 16) & 0xFF),
                (byte)((_crc32 >> 8) & 0xFF), (byte)(_crc32 & 0xFF)
            };
        }

        public async Task<DeviceStatusInfo?> GetStatusAsync()
        {
            if (!_canService.IsConnected)
            {
                SetError(FlashingError.ConnectionFailed, "CAN设备未连接");
                return null;
            }

            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                byte[] request = { 0xF0 };

                if (!_canService.SendMessage(SendId, request))
                {
                    SetError(FlashingError.ConnectionFailed, "发送GET_STATUS请求失败");
                    return null;
                }

                if (!await WaitForResponseAsync(5000, new byte[] { 0xF0 }))
                {
                    SetError(FlashingError.Timeout, "GET_STATUS响应超时");
                    return null;
                }

                byte[] response = _receivedResponse!;
                if (response.Length < 8 || response[0] != 0xF0)
                {
                    SetError(FlashingError.InvalidResponse, "GET_STATUS响应无效");
                    return null;
                }

                DeviceStatusInfo status = new DeviceStatusInfo
                {
                    State = (DeviceState)response[2],
                    LastError = response[3],
                    WrittenBytes = (uint)(response[4] << 16 | response[5] << 8 | response[6]),
                    NextExpectedCount = response[7]
                };

                return status;
            }
            catch (OperationCanceledException)
            {
                SetError(FlashingError.Timeout, "操作被取消");
                return null;
            }
            catch (Exception ex)
            {
                SetError(FlashingError.UnknownError, ex.Message);
                return null;
            }
            finally
            {
                _cancellationTokenSource?.Dispose();
            }
        }

        private async Task<bool> SendSessionAsync()
        {
            byte[] request = { 0x10, 0x42, 0x4D, 0x53, 0x31, 0x00, 0x00, 0x00 };

            if (!_canService.SendMessage(SendId, request))
            {
                SetError(FlashingError.ConnectionFailed, "发送SESSION请求失败");
                return false;
            }

            if (!await WaitForResponseAsync(expectedResponses: new byte[] { 0x50 }))
            {
                SetError(FlashingError.Timeout, "SESSION响应超时");
                return false;
            }

            byte[] response = _receivedResponse!;
            if (response.Length < 1 || response[0] != 0x50)
            {
                SetError(FlashingError.InvalidResponse, "SESSION响应无效");
                return false;
            }

            return true;
        }

        private async Task<bool> SendEraseAsync()
        {
            int size = _flashData.Length;
            byte[] request = new byte[8];
            request[0] = 0x20;
            request[1] = (byte)((size >> 16) & 0xFF);
            request[2] = (byte)((size >> 8) & 0xFF);
            request[3] = (byte)(size & 0xFF);
            request[4] = (byte)((_crc32 >> 24) & 0xFF);
            request[5] = (byte)((_crc32 >> 16) & 0xFF);
            request[6] = (byte)((_crc32 >> 8) & 0xFF);
            request[7] = (byte)(_crc32 & 0xFF);

            if (!_canService.SendMessage(SendId, request))
            {
                SetError(FlashingError.ConnectionFailed, "发送ERASE请求失败");
                return false;
            }

            if (!await WaitForResponseAsync(expectedResponses: new byte[] { 0x60 }))
            {
                SetError(FlashingError.Timeout, "ERASE响应超时");
                return false;
            }

            byte[] response = _receivedResponse!;
            if (response.Length < 2 || response[0] != 0x60)
            {
                SetError(FlashingError.InvalidResponse, "ERASE响应无效");
                return false;
            }

            return true;
        }

        private async Task<bool> SendDataFramesAsync()
        {
            const int PAYLOAD_SIZE = 7;
            int count = 0;
            int offset = 0;
            int totalFrames = (_flashData.Length + PAYLOAD_SIZE - 1) / PAYLOAD_SIZE;

            while (offset < _flashData.Length)
            {
                if (_cancellationTokenSource?.IsCancellationRequested ?? false)
                    throw new OperationCanceledException();

                int chunkSize = Math.Min(PAYLOAD_SIZE, _flashData.Length - offset);
                byte[] data = new byte[1 + chunkSize];
                data[0] = (byte)(0x70 + (count % 16));
                Array.Copy(_flashData, offset, data, 1, chunkSize);

                if (!_canService.SendMessage(SendId, data, false, false))
                {
                    SetError(FlashingError.ConnectionFailed, "发送数据帧失败");
                    return false;
                }

                count = (count + 1) & 0xFF;
                offset += chunkSize;

                int progress = (int)((offset * 100) / _flashData.Length);
                SetProgress(Math.Min(progress, 95));

                await Task.Delay(5);
            }

            return true;
        }

        private async Task<bool> WaitForCompleteResponseAsync()
        {
            if (!await WaitForResponseAsync(TIMEOUT_MS, new byte[] { 0x65, 0x7F }))
            {
                SetError(FlashingError.Timeout, "等待完成响应超时");
                return false;
            }

            byte[] response = _receivedResponse!;
            if (response.Length < 1)
            {
                SetError(FlashingError.InvalidResponse, "完成响应为空");
                return false;
            }

            if (response[0] == 0x65)
            {
                if (response.Length < 2)
                {
                    SetError(FlashingError.InvalidResponse, "完成响应缺少结果码");
                    return false;
                }

                byte resultCode = response[1];
                switch (resultCode)
                {
                    case ECU_CAN_PROG_RES_OK:
                        break;
                    case ECU_CAN_PROG_RES_BUSY:
                        SetError(FlashingError.InvalidResponse, "设备忙（保留）");
                        return false;
                    case ECU_CAN_PROG_RES_KEY_ERR:
                        SetError(FlashingError.InvalidResponse, "SESSION_ENTER 密钥错误");
                        return false;
                    case ECU_CAN_PROG_RES_STATE_ERR:
                        SetError(FlashingError.InvalidResponse, "状态不允许或会话超时");
                        return false;
                    case ECU_CAN_PROG_RES_PARAM_ERR:
                        SetError(FlashingError.InvalidResponse, "参数/DLC/长度非法");
                        return false;
                    case ECU_CAN_PROG_RES_FLASH_ERR:
                        SetError(FlashingError.InvalidResponse, "擦除或写入 Flash 失败");
                        return false;
                    case ECU_CAN_PROG_RES_CRC_ERR:
                        SetError(FlashingError.CrcError, "镜像 CRC 与声明不一致");
                        return false;
                    case ECU_CAN_PROG_RES_CNT_ERR:
                        SetError(FlashingError.InvalidResponse, "连续帧 count 不连续");
                        return false;
                    default:
                        SetError(FlashingError.InvalidResponse, $"未知结果码: 0x{resultCode:X2}");
                        return false;
                }

                if (response.Length >= 5)
                {
                    uint receivedSize = (uint)(response[2] << 16 | response[3] << 8 | response[4]);
                    if (receivedSize != _flashData.Length)
                    {
                        SetError(FlashingError.CrcError, "数据长度不匹配");
                        return false;
                    }
                }
                return true;
            }
            else if (response[0] == 0x7F)
            {
                SetError(FlashingError.InvalidResponse, $"消极响应: 0x{response[2]:X2}");
                return false;
            }
            else
            {
                SetError(FlashingError.InvalidResponse, "未知响应");
                return false;
            }
        }

        private async Task<bool> SendCommitAsync()
        {
            byte[] request = { 0x30, 0xA5, 0x5A, 0xA5, 0x5A, 0x00, 0x00, 0x00 };

            if (!_canService.SendMessage(SendId, request))
            {
                SetError(FlashingError.ConnectionFailed, "发送COMMIT请求失败");
                return false;
            }

            if (!await WaitForResponseAsync(expectedResponses: new byte[] { 0x70 }))
            {
                SetError(FlashingError.Timeout, "COMMIT响应超时");
                return false;
            }

            byte[] response = _receivedResponse!;
            if (response.Length < 1 || response[0] != 0x70)
            {
                SetError(FlashingError.InvalidResponse, "COMMIT响应无效");
                return false;
            }

            return true;
        }

        private async Task<bool> WaitForResponseAsync(int timeoutMs = 5000, byte[]? expectedResponses = null)
        {
            DateTime startTime = DateTime.Now;
            _receivedResponse = null;

            while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
            {
                if (_cancellationTokenSource?.IsCancellationRequested ?? false)
                    throw new OperationCanceledException();

                // 按响应命令字节条件消费，不匹配的帧保留在待匹配队列，供标定读取等其他等待方使用
                if (_canService.ReceiveMessageFiltered(ReceiveId, expectedResponses, out _, out _, out byte[] data))
                {
                    _receivedResponse = data;
                    return true;
                }

                await Task.Delay(1);
            }

            return false;
        }

        private byte[]? _receivedResponse = null;

        private void ChangeState(FlashingState newState)
        {
            _state = newState;
            StateChanged?.Invoke(this, new FlashingStateChangedEventArgs { NewState = newState });
        }

        private void SetProgress(int progress)
        {
            _currentProgress = progress;
            ProgressChanged?.Invoke(this, new ProgressChangedEventArgs { Progress = progress });
        }

        private void SetError(FlashingError error, string message)
        {
            _error = error;
            _errorMessage = message;
            _state = FlashingState.Error;
            StateChanged?.Invoke(this, new FlashingStateChangedEventArgs { NewState = _state });
        }

        public static uint CalculateCrc32(byte[] data)
        {
            uint crc = 0xFFFFFFFF;
            uint[] table = Crc32Table;

            foreach (byte b in data)
            {
                crc = table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            }

            return ~crc;
        }

        private static readonly uint[] Crc32Table = GenerateCrc32Table();

        private static uint[] GenerateCrc32Table()
        {
            uint[] table = new uint[256];
            const uint polynomial = 0xEDB88320;

            for (uint i = 0; i < 256; i++)
            {
                uint crc = i;
                for (int j = 0; j < 8; j++)
                {
                    crc = (crc & 1) != 0 ? (crc >> 1) ^ polynomial : crc >> 1;
                }
                table[i] = crc;
            }

            return table;
        }
    }

    public class FlashingStateChangedEventArgs : EventArgs
    {
        public FlashingState NewState { get; set; }
    }

    public class ProgressChangedEventArgs : EventArgs
    {
        public int Progress { get; set; }
    }
}
