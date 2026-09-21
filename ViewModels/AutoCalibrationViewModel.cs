using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using BOOTWR.Services;

namespace BOOTWR.ViewModels
{
    public enum CalibrationTarget
    {
        BMU,
        HCU
    }

    /// <summary>
    /// 自动标定测试窗口的 ViewModel。
    /// 流程：对每个 AFE 依次设置 5/10/15/20 mV 电压 → 读取 8 个 TC 采集电压 x 与反解电压 y →
    /// 确认/重采/取消 → 以采集电压为 x、设置电压为 y 拟合 K、B → 写入并保存到 Flash → 显示 K、B 结果。
    /// BMU 依次标定 AFE0→AFE1→AFE2（期间提示接线），HCU 仅标定 AFE0。
    /// </summary>
    public partial class AutoCalibrationViewModel : ObservableObject
    {
        private readonly MainWindowViewModel _mainVm;
        private readonly CanCommunicationService _canService;
        private readonly CalibrationService _calibrationService;
        private readonly Window _owner;
        private CancellationTokenSource? _cts;
        private bool _sessionOpenedByUs;

        private static readonly int[] VoltageLevelsMv = { 5, 10, 15, 20 };

        public AutoCalibrationViewModel(MainWindowViewModel mainVm, Window owner)
        {
            _mainVm = mainVm;
            _owner = owner;
            _canService = mainVm.CanService;
            _calibrationService = mainVm.CalibrationService;
        }

        /// <summary>与主窗口共用同一份日志集合，避免日志文件并发写入。</summary>
        public ObservableCollection<LogMessage> LogMessages => _mainVm.LogService.LogMessages;

        /// <summary>最近一次完成的 AFE 标定结果（8 个 TC 的 K、B 值）。</summary>
        public ObservableCollection<KbResultRow> KbResults { get; } = new();

        [ObservableProperty]
        private CalibrationTarget _selectedTarget = CalibrationTarget.BMU;

        [ObservableProperty]
        private bool _isRunning;

        [ObservableProperty]
        private string _statusText = "空闲";

        [ObservableProperty]
        private string _currentStep = "等待开始";

        public bool CanStart => !IsRunning;
        public bool CanStop => IsRunning;

        partial void OnIsRunningChanged(bool value)
        {
            OnPropertyChanged(nameof(CanStart));
            OnPropertyChanged(nameof(CanStop));
            StatusText = value ? "运行中" : "空闲";
        }

        [RelayCommand]
        public void ClearLogs()
        {
            _mainVm.LogService.Clear();
        }

        [RelayCommand]
        public void StopCalibration()
        {
            if (!IsRunning) return;
            _mainVm.LogService.Info("自动标定：正在停止...");
            _cts?.Cancel();
            // 立即中断在途的读取/写入等待，让停止尽快生效
            _calibrationService.Cancel();
        }

        [RelayCommand]
        public async Task StartCalibration()
        {
            if (IsRunning) return;

            IsRunning = true;
            _cts = new CancellationTokenSource();
            _sessionOpenedByUs = false;
            CurrentStep = "准备中...";
            var log = _mainVm.LogService;

            try
            {
                if (!_canService.IsConnected)
                {
                    log.Error("自动标定：请先连接CAN设备");
                    return;
                }

                if (!await EnsureSessionAsync(log)) return;

                int[] afes = SelectedTarget == CalibrationTarget.BMU ? new[] { 0, 1, 2 } : new[] { 0 };
                string targetName = SelectedTarget == CalibrationTarget.BMU ? "BMU" : "HCU";
                log.Info($"自动标定开始：目标={targetName}，AFE={string.Join("/", afes.Select(a => $"AFE{a}"))}");

                for (int i = 0; i < afes.Length; i++)
                {
                    int afe = afes[i];
                    if (i > 0)
                    {
                        CurrentStep = $"等待连接 AFE{afe}";
                        log.Info($"AFE{afe - 1} 标定完成，请连接 AFE{afe}");
                        if (!ShowPrompt($"请接AFE{afe}", "确认"))
                        {
                            log.Warning("自动标定已取消");
                            return;
                        }
                    }

                    if (!await RunSingleAfeAsync(afe)) return;
                }

                CurrentStep = "完成";
                log.Info("自动标定全部完成");
            }
            catch (Exception ex)
            {
                log.Error($"自动标定异常: {ex.Message}");
            }
            finally
            {
                await FinishAsync();
            }
        }

        /// <summary>确保标定会话已打开；若由本窗口打开，结束后由本窗口关闭。</summary>
        private async Task<bool> EnsureSessionAsync(LogService log)
        {
            if (_calibrationService.IsSessionOpen)
            {
                _mainVm.IsCalibrationSessionOpen = true;
                return true;
            }

            log.Info("自动标定：正在打开标定会话...");

            if (!uint.TryParse(_mainVm.UdsPhysicalAddress, NumberStyles.HexNumber, null, out uint phys) ||
                !uint.TryParse(_mainVm.UdsResponseAddress, NumberStyles.HexNumber, null, out uint resp))
            {
                log.Error("自动标定：无效的UDS地址，请在主界面检查物理地址/响应地址");
                return false;
            }

            _calibrationService.SendId = phys;
            _calibrationService.ReceiveId = resp;

            if (!await _calibrationService.OpenSessionAsync())
            {
                log.Error($"自动标定：打开会话失败 - {_calibrationService.ErrorMessage}");
                return false;
            }

            _sessionOpenedByUs = true;
            _mainVm.IsCalibrationSessionOpen = true;
            log.Info("自动标定：标定会话已打开");
            return true;
        }

        /// <summary>单个 AFE 的完整标定流程（4 个电压点）。返回 false 表示流程中止。</summary>
        private async Task<bool> RunSingleAfeAsync(int afe)
        {
            var log = _mainVm.LogService;
            float[][] collected = new float[VoltageLevelsMv.Length][];

            for (int li = 0; li < VoltageLevelsMv.Length; li++)
            {
                int mv = VoltageLevelsMv[li];

                if (_cts?.IsCancellationRequested == true)
                {
                    log.Warning("自动标定已停止");
                    return false;
                }

                // 1. 提示操作员设置电压
                CurrentStep = $"AFE{afe} · {mv}mV 待设置";
                log.Info($"请设置 {mv}mV 电压（AFE{afe}）");
                if (!ShowPrompt($"请设置{mv}mV电压(AFE{afe})", "已设置完成"))
                {
                    log.Warning("自动标定已取消");
                    return false;
                }

                // 2. 采集 8 个 TC 电压，可重新采集
                while (true)
                {
                    if (_cts?.IsCancellationRequested == true)
                    {
                        log.Warning("自动标定已停止");
                        return false;
                    }

                    CurrentStep = $"AFE{afe} · {mv}mV 采集中...";
                    (float[] x, float[] y)? reading = await Read8TcVoltagesAsync(afe);
                    if (reading == null)
                    {
                        if (_cts?.IsCancellationRequested == true)
                        {
                            log.Warning("自动标定已停止");
                            return false;
                        }
                        log.Error($"AFE{afe} · {mv}mV 采集失败，标定终止");
                        return false;
                    }

                    // 3. 显示采集结果供确认
                    CurrentStep = $"AFE{afe} · {mv}mV 待确认";
                    ConfirmResult result = ShowConfirm(afe, mv, reading.Value.x, reading.Value.y);
                    if (result == ConfirmResult.Confirm)
                    {
                        collected[li] = reading.Value.x;
                        break;
                    }
                    if (result == ConfirmResult.Recollect)
                    {
                        log.Info($"AFE{afe} · {mv}mV 重新采集");
                        continue;
                    }
                    log.Warning("自动标定已取消");
                    return false;
                }
            }

            // 4. 计算并写入 K、B
            if (_cts?.IsCancellationRequested == true)
            {
                log.Warning("自动标定已停止");
                return false;
            }
            CurrentStep = $"AFE{afe} 计算并写入KB";
            return await WriteKbForAfeAsync(afe, collected);
        }

        /// <summary>读取指定 AFE 的 8 个 TC 采集电压 x 与反解电压 y（0x18，sample_type=VoltageX/VoltageY）。失败返回 null。</summary>
        private async Task<(float[] x, float[] y)?> Read8TcVoltagesAsync(int afe)
        {
            var log = _mainVm.LogService;
            float[] xs = new float[8];
            float[] ys = new float[8];

            for (int tc = 0; tc < 8; tc++)
            {
                if (_cts?.IsCancellationRequested == true) return null;

                float? x = await _calibrationService.ReadSampleAsync(SampleType.VoltageX, afe, tc);
                if (_cts?.IsCancellationRequested == true) return null;
                if (!x.HasValue)
                {
                    log.Error($"读取采集电压失败：AFE{afe} TC{tc} x - {_calibrationService.ErrorMessage}");
                    return null;
                }

                if (_cts?.IsCancellationRequested == true) return null;
                float? y = await _calibrationService.ReadSampleAsync(SampleType.VoltageY, afe, tc);
                if (_cts?.IsCancellationRequested == true) return null;
                if (!y.HasValue)
                {
                    log.Error($"读取采集电压失败：AFE{afe} TC{tc} y - {_calibrationService.ErrorMessage}");
                    return null;
                }

                xs[tc] = x.Value;
                ys[tc] = y.Value;
                log.Info($"AFE{afe} TC{tc} 采集：x={x.Value:F4} V，y={y.Value:F4} V");
            }

            return (xs, ys);
        }

        /// <summary>
        /// 以 4 个电压点的采集电压为 x、设置电压(mV→V)为 y，对每个 TC 最小二乘拟合 K、B，
        /// 写入设备（RAM）并保存到 Flash。任一 TC 拟合失败则中止且不写入。
        /// </summary>
        private async Task<bool> WriteKbForAfeAsync(int afe, float[][] collected)
        {
            var log = _mainVm.LogService;
            double[] ys = VoltageLevelsMv.Select(mv => mv * 0.001 * 51 + 0.1).ToArray();
            double[] kValues = new double[8];
            double[] bValues = new double[8];

            for (int tc = 0; tc < 8; tc++)
            {
                double[] xs = new double[VoltageLevelsMv.Length];
                for (int li = 0; li < VoltageLevelsMv.Length; li++)
                {
                    xs[li] = collected[li][tc];
                }

                var fit = MainWindowViewModel.GetAAndB(xs, ys);
                if (fit == null || double.IsNaN(fit.Value.Item1) || double.IsNaN(fit.Value.Item2))
                {
                    log.Error($"AFE{afe} TC{tc}：4 个电压点的采集电压无变化（x 相同），无法拟合 K/B，请检查接线或电压源");
                    return false;
                }

                kValues[tc] = fit.Value.Item1;
                bValues[tc] = fit.Value.Item2;
                log.Info($"AFE{afe} TC{tc}：K={fit.Value.Item1:F3}，B={fit.Value.Item2:F3}");
            }

            log.Info($"AFE{afe}：正在写入 8 个 TC 的 K、B 值（RAM）...");
            for (int tc = 0; tc < 8; tc++)
            {
                if (_cts?.IsCancellationRequested == true)
                {
                    log.Warning("自动标定已停止");
                    return false;
                }

                if (!await _calibrationService.WriteCalibrationParamAsync(CalibrationParamType.K, afe, tc, (float)kValues[tc]))
                {
                    log.Error($"AFE{afe} TC{tc}：写入 K 失败 - {_calibrationService.ErrorMessage}");
                    return false;
                }
                await Task.Delay(10);

                if (!await _calibrationService.WriteCalibrationParamAsync(CalibrationParamType.B, afe, tc, (float)bValues[tc]))
                {
                    log.Error($"AFE{afe} TC{tc}：写入 B 失败 - {_calibrationService.ErrorMessage}");
                    return false;
                }
                await Task.Delay(10);
            }
            log.Info($"AFE{afe}：K、B 写入完成（RAM）");

            log.Info($"AFE{afe}：正在保存到 Flash...");
            if (!await _calibrationService.SaveCalibrationAsync())
            {
                log.Error($"AFE{afe}：保存到 Flash 失败 - {_calibrationService.ErrorMessage}");
                return false;
            }
            log.Info($"AFE{afe}：已保存到 Flash");

            // 标定完成：将 8 个 TC 的 K、B 值显示在界面
            KbResults.Clear();
            for (int tc = 0; tc < 8; tc++)
            {
                KbResults.Add(new KbResultRow
                {
                    Afe = afe,
                    Tc = tc,
                    K = kValues[tc],
                    B = bValues[tc]
                });
            }
            log.Info($"AFE{afe}：标定完成，K、B 值已显示");

            return true;
        }

        /// <summary>流程收尾：关闭由本窗口打开的会话、复位 UI 状态。</summary>
        private async Task FinishAsync()
        {
            if (_sessionOpenedByUs)
            {
                _mainVm.LogService.Info("自动标定：正在关闭标定会话...");
                await _calibrationService.CloseSessionAsync();
                _mainVm.IsCalibrationSessionOpen = false;
                _mainVm.LogService.Info("自动标定：标定会话已关闭");
            }

            _cts?.Dispose();
            _cts = null;
            _sessionOpenedByUs = false;
            IsRunning = false;
            if (CurrentStep != "完成")
            {
                CurrentStep = "等待开始";
            }
        }

        private bool ShowPrompt(string message, string buttonText)
        {
            var dialog = new CalibrationPromptDialog(message, buttonText)
            {
                Owner = _owner
            };
            return dialog.ShowDialog() == true;
        }

        private ConfirmResult ShowConfirm(int afe, int mv, float[] xValues, float[] yValues)
        {
            var dialog = new CalibrationConfirmDialog(afe, mv, xValues, yValues)
            {
                Owner = _owner
            };
            return dialog.ShowResult();
        }
    }

    /// <summary>自动标定结果表中的一行：某个 AFE 某个 TC 的 K、B 值。</summary>
    public class KbResultRow
    {
        public int Afe { get; set; }
        public int Tc { get; set; }
        public string TcLabel => $"TC {Tc}";
        public double K { get; set; }
        public double B { get; set; }
        public string KText => K.ToString("F3");
        public string BText => B.ToString("F3");
    }
}
