using System;
using System.Buffers.Binary;
using System.Threading;
using System.Threading.Tasks;

namespace BOOTWR.Services
{
    public enum CalibrationServiceError
    {
        None,
        ConnectionFailed,
        Timeout,
        InvalidResponse,
        SessionNotOpened,
        UnknownError
    }

    public enum CalibrationParamType
    {
        K = 0x01,
        B = 0x02,
        Vref = 0x03
    }

    public enum SampleType
    {
        VoltageX = 0x01,
        VoltageY = 0x02
    }

    public class CalibrationService
    {
        private readonly CanCommunicationService _canService;
        private uint _sendId = 0x7E0;
        private uint _receiveId = 0x7E8;
        private const int TIMEOUT_MS = 30000;
        private bool _isSessionOpen = false;
        private CalibrationServiceError _error = CalibrationServiceError.None;
        private string _errorMessage = string.Empty;
        private CancellationTokenSource? _cancellationTokenSource;

        private const byte ECU_CAN_PROG_RES_OK = 0x00;
        private const byte ECU_CAN_PROG_RES_KEY_ERR = 0x02;
        private const byte ECU_CAN_PROG_RES_STATE_ERR = 0x03;
        private const byte ECU_CAN_PROG_RES_PARAM_ERR = 0x04;
        private const byte ECU_CAN_PROG_RES_FLASH_ERR = 0x05;

        public uint SendId
        {
            get => _sendId;
            set => _sendId = value;
        }

        public uint ReceiveId
        {
            get => _receiveId;
            set => _receiveId = value;
        }

        public bool IsSessionOpen => _isSessionOpen;
        public CalibrationServiceError Error => _error;
        public string ErrorMessage => _errorMessage;

        public CalibrationService(CanCommunicationService canService)
        {
            _canService = canService;
        }

        public async Task<bool> OpenSessionAsync()
        {
            if (!_canService.IsConnected)
            {
                SetError(CalibrationServiceError.ConnectionFailed, "CAN设备未连接");
                return false;
            }

            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                byte[] request = { 0x10, 0x42, 0x4D, 0x53, 0x31, 0x00, 0x00, 0x00 };

                if (!_canService.SendMessage(_sendId, request))
                {
                    SetError(CalibrationServiceError.ConnectionFailed, "发送SESSION请求失败");
                    return false;
                }

                if (!await WaitForResponseAsync())
                {
                    SetError(CalibrationServiceError.Timeout, "SESSION响应超时");
                    return false;
                }

                byte[] response = _receivedResponse!;
                if (response.Length < 2 || response[0] != 0x50)
                {
                    SetError(CalibrationServiceError.InvalidResponse, "SESSION响应无效");
                    return false;
                }

                byte resultCode = response[1];
                if (resultCode != ECU_CAN_PROG_RES_OK)
                {
                    SetError(CalibrationServiceError.InvalidResponse, GetErrorMessage(resultCode));
                    return false;
                }

                _isSessionOpen = true;
                return true;
            }
            catch (OperationCanceledException)
            {
                SetError(CalibrationServiceError.Timeout, "操作被取消");
                return false;
            }
            catch (Exception ex)
            {
                SetError(CalibrationServiceError.UnknownError, ex.Message);
                return false;
            }
            finally
            {
                _cancellationTokenSource?.Dispose();
            }
        }

        public async Task<bool> CloseSessionAsync()
        {
            if (!_isSessionOpen)
                return true;

            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                byte[] request = { 0x11, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

                if (!_canService.SendMessage(_sendId, request))
                {
                    SetError(CalibrationServiceError.ConnectionFailed, "发送SESSION_EXIT请求失败");
                    return false;
                }

                if (!await WaitForResponseAsync())
                {
                    SetError(CalibrationServiceError.Timeout, "SESSION_EXIT响应超时");
                    return false;
                }

                byte[] response = _receivedResponse!;
                if (response.Length < 1 || response[0] != 0x51)
                {
                    SetError(CalibrationServiceError.InvalidResponse, "SESSION_EXIT响应无效");
                    return false;
                }

                _isSessionOpen = false;
                return true;
            }
            catch (OperationCanceledException)
            {
                SetError(CalibrationServiceError.Timeout, "操作被取消");
                return false;
            }
            catch (Exception ex)
            {
                SetError(CalibrationServiceError.UnknownError, ex.Message);
                return false;
            }
            finally
            {
                _cancellationTokenSource?.Dispose();
            }
        }

        public async Task<bool> WriteAllCalibrationParamsAsync(float[,] kValues, float[,] bValues, float[] vrefValues)
        {
            if (!_isSessionOpen)
            {
                SetError(CalibrationServiceError.SessionNotOpened, "请先打开会话");
                return false;
            }

            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                _logService?.Invoke("开始批量写入标定参数...");

                for (int afe = 0; afe < 3; afe++)
                {
                    if (_cancellationTokenSource?.IsCancellationRequested ?? false)
                        throw new OperationCanceledException();

                    for (int tc = 0; tc < 8; tc++)
                    {
                        float k = kValues[afe, tc];
                        if (!await WriteCalibrationParamAsync(CalibrationParamType.K, afe, tc, k))
                        {
                            return false;
                        }
                        await Task.Delay(10);
                    }

                    for (int tc = 0; tc < 8; tc++)
                    {
                        float b = bValues[afe, tc];
                        if (!await WriteCalibrationParamAsync(CalibrationParamType.B, afe, tc, b))
                        {
                            return false;
                        }
                        await Task.Delay(10);
                    }

                    if (afe < vrefValues.Length)
                    {
                        float vref = vrefValues[afe];
                        if (!await WriteCalibrationParamAsync(CalibrationParamType.Vref, afe, 0, vref))
                        {
                            return false;
                        }
                        await Task.Delay(10);
                    }
                }

                _logService?.Invoke("批量写入标定参数完成");
                return true;
            }
            catch (OperationCanceledException)
            {
                SetError(CalibrationServiceError.Timeout, "操作被取消");
                return false;
            }
            catch (Exception ex)
            {
                SetError(CalibrationServiceError.UnknownError, ex.Message);
                return false;
            }
            finally
            {
                _cancellationTokenSource?.Dispose();
            }
        }

        private Action<string>? _logService;
        public void SetLogCallback(Action<string> callback)
        {
            _logService = callback;
        }

        public async Task<bool> WriteCalibrationParamAsync(CalibrationParamType paramType, int afeIndex, int tcIndex, float value)
        {
            if (!_isSessionOpen)
            {
                SetError(CalibrationServiceError.SessionNotOpened, "请先打开会话");
                return false;
            }

            if (afeIndex < 0 || afeIndex > 2)
            {
                SetError(CalibrationServiceError.InvalidResponse, "AFE索引必须在0-2范围内");
                return false;
            }

            if (tcIndex < 0 || tcIndex > 7)
            {
                SetError(CalibrationServiceError.InvalidResponse, "热电偶索引必须在0-7范围内");
                return false;
            }

            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                byte[] request = new byte[8];
                request[0] = 0x12;
                request[1] = (byte)paramType;
                request[2] = (byte)afeIndex;
                request[3] = (byte)tcIndex;

                byte[] floatBytes = BitConverter.GetBytes(value);
                if (BitConverter.IsLittleEndian)
                    Array.Reverse(floatBytes);

                Array.Copy(floatBytes, 0, request, 4, 4);

                if (!_canService.SendMessage(_sendId, request))
                {
                    SetError(CalibrationServiceError.ConnectionFailed, "发送TC_CALIB_WRITE请求失败");
                    return false;
                }

                if (!await WaitForResponseAsync())
                {
                    SetError(CalibrationServiceError.Timeout, "TC_CALIB_WRITE响应超时");
                    return false;
                }

                byte[] response = _receivedResponse!;
                if (response.Length < 2 || response[0] != 0x52)
                {
                    SetError(CalibrationServiceError.InvalidResponse, "TC_CALIB_WRITE响应无效");
                    return false;
                }

                byte resultCode = response[1];
                if (resultCode != ECU_CAN_PROG_RES_OK)
                {
                    SetError(CalibrationServiceError.InvalidResponse, GetErrorMessage(resultCode));
                    return false;
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                SetError(CalibrationServiceError.Timeout, "操作被取消");
                return false;
            }
            catch (Exception ex)
            {
                SetError(CalibrationServiceError.UnknownError, ex.Message);
                return false;
            }
            finally
            {
                _cancellationTokenSource?.Dispose();
            }
        }

        public async Task<float?> ReadCalibrationParamAsync(CalibrationParamType paramType, int afeIndex, int tcIndex)
        {
            if (!_isSessionOpen)
            {
                SetError(CalibrationServiceError.SessionNotOpened, "请先打开会话");
                return null;
            }

            if (afeIndex < 0 || afeIndex > 2)
            {
                SetError(CalibrationServiceError.InvalidResponse, "AFE索引必须在0-2范围内");
                return null;
            }

            if (tcIndex < 0 || tcIndex > 7)
            {
                SetError(CalibrationServiceError.InvalidResponse, "热电偶索引必须在0-7范围内");
                return null;
            }

            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                byte[] request = new byte[4];
                request[0] = 0x15;
                request[1] = (byte)paramType;
                request[2] = (byte)afeIndex;
                request[3] = (byte)tcIndex;

                if (!_canService.SendMessage(_sendId, request))
                {
                    SetError(CalibrationServiceError.ConnectionFailed, "发送TC_CALIB_READ请求失败");
                    return null;
                }

                if (!await WaitForResponseAsync())
                {
                    SetError(CalibrationServiceError.Timeout, "TC_CALIB_READ响应超时");
                    return null;
                }

                byte[] response = _receivedResponse!;
                if (response.Length < 6 || response[0] != 0x55)
                {
                    SetError(CalibrationServiceError.InvalidResponse, "TC_CALIB_READ响应无效");
                    return null;
                }

                byte resultCode = response[1];
                if (resultCode != ECU_CAN_PROG_RES_OK)
                {
                    SetError(CalibrationServiceError.InvalidResponse, GetErrorMessage(resultCode));
                    return null;
                }

                byte[] floatBytes = new byte[4];
                Array.Copy(response, 2, floatBytes, 0, 4);

                if (BitConverter.IsLittleEndian)
                    Array.Reverse(floatBytes);

                return BitConverter.ToSingle(floatBytes, 0);
            }
            catch (OperationCanceledException)
            {
                SetError(CalibrationServiceError.Timeout, "操作被取消");
                return null;
            }
            catch (Exception ex)
            {
                SetError(CalibrationServiceError.UnknownError, ex.Message);
                return null;
            }
            finally
            {
                _cancellationTokenSource?.Dispose();
            }
        }

        public async Task<bool> SaveCalibrationAsync()
        {
            if (!_isSessionOpen)
            {
                SetError(CalibrationServiceError.SessionNotOpened, "请先打开会话");
                return false;
            }

            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                byte[] request = { 0x13, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

                if (!_canService.SendMessage(_sendId, request))
                {
                    SetError(CalibrationServiceError.ConnectionFailed, "发送TC_CALIB_SAVE请求失败");
                    return false;
                }

                if (!await WaitForResponseAsync())
                {
                    SetError(CalibrationServiceError.Timeout, "TC_CALIB_SAVE响应超时");
                    return false;
                }

                byte[] response = _receivedResponse!;
                if (response.Length < 2 || response[0] != 0x53)
                {
                    SetError(CalibrationServiceError.InvalidResponse, "TC_CALIB_SAVE响应无效");
                    return false;
                }

                byte resultCode = response[1];
                if (resultCode != ECU_CAN_PROG_RES_OK)
                {
                    SetError(CalibrationServiceError.InvalidResponse, GetErrorMessage(resultCode));
                    return false;
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                SetError(CalibrationServiceError.Timeout, "操作被取消");
                return false;
            }
            catch (Exception ex)
            {
                SetError(CalibrationServiceError.UnknownError, ex.Message);
                return false;
            }
            finally
            {
                _cancellationTokenSource?.Dispose();
            }
        }

        public async Task<bool> RestoreDefaultsAsync()
        {
            if (!_isSessionOpen)
            {
                SetError(CalibrationServiceError.SessionNotOpened, "请先打开会话");
                return false;
            }

            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                byte[] request = { 0x14, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

                if (!_canService.SendMessage(_sendId, request))
                {
                    SetError(CalibrationServiceError.ConnectionFailed, "发送TC_CALIB_DEFAULTS请求失败");
                    return false;
                }

                if (!await WaitForResponseAsync())
                {
                    SetError(CalibrationServiceError.Timeout, "TC_CALIB_DEFAULTS响应超时");
                    return false;
                }

                byte[] response = _receivedResponse!;
                if (response.Length < 2 || response[0] != 0x54)
                {
                    SetError(CalibrationServiceError.InvalidResponse, "TC_CALIB_DEFAULTS响应无效");
                    return false;
                }

                byte resultCode = response[1];
                if (resultCode != ECU_CAN_PROG_RES_OK)
                {
                    SetError(CalibrationServiceError.InvalidResponse, GetErrorMessage(resultCode));
                    return false;
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                SetError(CalibrationServiceError.Timeout, "操作被取消");
                return false;
            }
            catch (Exception ex)
            {
                SetError(CalibrationServiceError.UnknownError, ex.Message);
                return false;
            }
            finally
            {
                _cancellationTokenSource?.Dispose();
            }
        }

        public async Task<float?> ReadSampleAsync(SampleType sampleType, int afeIndex, int tcIndex)
        {
            if (!_isSessionOpen)
            {
                SetError(CalibrationServiceError.SessionNotOpened, "请先打开会话");
                return null;
            }

            if (afeIndex < 0 || afeIndex > 2)
            {
                SetError(CalibrationServiceError.InvalidResponse, "AFE索引必须在0-2范围内");
                return null;
            }

            if (tcIndex < 0 || tcIndex > 7)
            {
                SetError(CalibrationServiceError.InvalidResponse, "热电偶索引必须在0-7范围内");
                return null;
            }

            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                byte[] request = new byte[4];
                request[0] = 0x18;
                request[1] = (byte)sampleType;
                request[2] = (byte)afeIndex;
                request[3] = (byte)tcIndex;

                if (!_canService.SendMessage(_sendId, request))
                {
                    SetError(CalibrationServiceError.ConnectionFailed, "发送TC_SAMPLE_READ请求失败");
                    return null;
                }

                if (!await WaitForResponseAsync(TIMEOUT_MS))
                {
                    SetError(CalibrationServiceError.Timeout, "TC_SAMPLE_READ响应超时");
                    return null;
                }

                byte[] response = _receivedResponse!;
                if (response.Length < 6 || response[0] != 0x58)
                {
                    SetError(CalibrationServiceError.InvalidResponse, "TC_SAMPLE_READ响应无效");
                    return null;
                }

                byte resultCode = response[1];
                if (resultCode != ECU_CAN_PROG_RES_OK)
                {
                    SetError(CalibrationServiceError.InvalidResponse, GetErrorMessage(resultCode));
                    return null;
                }

                byte[] floatBytes = new byte[4];
                Array.Copy(response, 2, floatBytes, 0, 4);

                if (BitConverter.IsLittleEndian)
                    Array.Reverse(floatBytes);

                return BitConverter.ToSingle(floatBytes, 0);
            }
            catch (OperationCanceledException)
            {
                SetError(CalibrationServiceError.Timeout, "操作被取消");
                return null;
            }
            catch (Exception ex)
            {
                SetError(CalibrationServiceError.UnknownError, ex.Message);
                return null;
            }
            finally
            {
                _cancellationTokenSource?.Dispose();
            }
        }

        private async Task<bool> WaitForResponseAsync(int timeoutMs = 5000)
        {
            DateTime startTime = DateTime.Now;
            _receivedResponse = null;

            while ((DateTime.Now - startTime).TotalMilliseconds < timeoutMs)
            {
                if (_cancellationTokenSource?.IsCancellationRequested ?? false)
                    throw new OperationCanceledException();

                if (_canService.ReceiveMessage(out uint canId, out bool _, out byte[] data))
                {
                    if (canId == _receiveId)
                    {
                        _receivedResponse = data;
                        return true;
                    }
                }

                await Task.Delay(1);
            }

            return false;
        }

        private byte[]? _receivedResponse = null;

        private void SetError(CalibrationServiceError error, string message)
        {
            _error = error;
            _errorMessage = message;
        }

        private string GetErrorMessage(byte resultCode)
        {
            switch (resultCode)
            {
                case ECU_CAN_PROG_RES_KEY_ERR:
                    return "密钥错误";
                case ECU_CAN_PROG_RES_STATE_ERR:
                    return "状态错误";
                case ECU_CAN_PROG_RES_PARAM_ERR:
                    return "参数错误";
                case ECU_CAN_PROG_RES_FLASH_ERR:
                    return "Flash错误";
                default:
                    return $"未知错误码: 0x{resultCode:X2}";
            }
        }

        public void Cancel()
        {
            _cancellationTokenSource?.Cancel();
        }
    }
}