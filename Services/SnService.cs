using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BOOTWR.Services
{
    public enum SnServiceError
    {
        None,
        ConnectionFailed,
        Timeout,
        InvalidResponse,
        SessionNotOpened,
        UnknownError
    }

    public class SnService
    {
        private readonly CanCommunicationService _canService;
        private uint _sendId = 0x7E0;
        private uint _receiveId = 0x7E8;
        private const int TIMEOUT_MS = 30000;
        private SnServiceError _error = SnServiceError.None;
        private string _errorMessage = string.Empty;
        private CancellationTokenSource? _cancellationTokenSource;

        private const byte ECU_CAN_PROG_RES_OK = 0x00;
        private const byte ECU_CAN_PROG_RES_KEY_ERR = 0x02;
        private const byte ECU_CAN_PROG_RES_STATE_ERR = 0x03;

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

        public SnServiceError Error => _error;
        public string ErrorMessage => _errorMessage;

        public event EventHandler<string>? SnRead;
        public event EventHandler<bool>? SnWritten;

        public SnService(CanCommunicationService canService)
        {
            _canService = canService;
        }

        public async Task<bool> OpenSessionAsync()
        {
            if (!_canService.IsConnected)
            {
                SetError(SnServiceError.ConnectionFailed, "CAN设备未连接");
                return false;
            }

            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                byte[] request = { 0x10, 0x42, 0x4D, 0x53, 0x31, 0x00, 0x00, 0x00 };

                if (!_canService.SendMessage(_sendId, request))
                {
                    SetError(SnServiceError.ConnectionFailed, "发送SESSION请求失败");
                    return false;
                }

                if (!await WaitForResponseAsync())
                {
                    SetError(SnServiceError.Timeout, "SESSION响应超时");
                    return false;
                }

                byte[] response = _receivedResponse!;
                if (response.Length < 2 || response[0] != 0x50)
                {
                    SetError(SnServiceError.InvalidResponse, "SESSION响应无效");
                    return false;
                }

                byte resultCode = response[1];
                if (resultCode != ECU_CAN_PROG_RES_OK)
                {
                    SetError(SnServiceError.InvalidResponse, GetErrorMessage(resultCode));
                    return false;
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                SetError(SnServiceError.Timeout, "操作被取消");
                return false;
            }
            catch (Exception ex)
            {
                SetError(SnServiceError.UnknownError, ex.Message);
                return false;
            }
            finally
            {
                _cancellationTokenSource?.Dispose();
            }
        }

        public async Task<bool> CloseSessionAsync()
        {
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                byte[] request = { 0x11, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

                if (!_canService.SendMessage(_sendId, request))
                {
                    SetError(SnServiceError.ConnectionFailed, "发送SESSION_EXIT请求失败");
                    return false;
                }

                if (!await WaitForResponseAsync())
                {
                    SetError(SnServiceError.Timeout, "SESSION_EXIT响应超时");
                    return false;
                }

                byte[] response = _receivedResponse!;
                if (response.Length < 1 || response[0] != 0x51)
                {
                    SetError(SnServiceError.InvalidResponse, "SESSION_EXIT响应无效");
                    return false;
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                SetError(SnServiceError.Timeout, "操作被取消");
                return false;
            }
            catch (Exception ex)
            {
                SetError(SnServiceError.UnknownError, ex.Message);
                return false;
            }
            finally
            {
                _cancellationTokenSource?.Dispose();
            }
        }

        public async Task<string> ReadSnAsync()
        {
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                StringBuilder sb = new StringBuilder();
                const int SN_MAX_LENGTH = 31;
                const int READ_CHUNK_SIZE = 6;

                for (int offset = 0; offset < SN_MAX_LENGTH; offset += READ_CHUNK_SIZE)
                {
                    if (_cancellationTokenSource?.IsCancellationRequested ?? false)
                        throw new OperationCanceledException();

                    byte[] request = new byte[3];
                    request[0] = 0x17;
                    request[1] = (byte)(offset & 0xFF);
                    request[2] = (byte)Math.Min(READ_CHUNK_SIZE, SN_MAX_LENGTH - offset);

                    if (!_canService.SendMessage(_sendId, request))
                    {
                        SetError(SnServiceError.ConnectionFailed, "发送SN_READ请求失败");
                        return string.Empty;
                    }

                    if (!await WaitForResponseAsync())
                    {
                        SetError(SnServiceError.Timeout, "SN_READ响应超时");
                        return string.Empty;
                    }

                    byte[] response = _receivedResponse!;
                    if (response.Length < 1 || response[0] != 0x57)
                    {
                        SetError(SnServiceError.InvalidResponse, "SN_READ响应无效");
                        return string.Empty;
                    }

                    int dataLen = response.Length - 2;
                    if (dataLen > 0)
                    {
                        for (int i = 0; i < dataLen; i++)
                        {
                            byte b = response[2 + i];
                            if (b == 0)
                                break;
                            sb.Append((char)b);
                        }
                    }

                    if (dataLen < READ_CHUNK_SIZE)
                        break;
                }

                string sn = sb.ToString();
                
                if (string.IsNullOrEmpty(sn))
                {
                    SetError(SnServiceError.InvalidResponse, "SN未写入");
                    SnRead?.Invoke(this, sn);
                    return string.Empty;
                }
                
                SnRead?.Invoke(this, sn);
                return sn;
            }
            catch (OperationCanceledException)
            {
                SetError(SnServiceError.Timeout, "操作被取消");
                return string.Empty;
            }
            catch (Exception ex)
            {
                SetError(SnServiceError.UnknownError, ex.Message);
                return string.Empty;
            }
            finally
            {
                _cancellationTokenSource?.Dispose();
            }
        }

        public async Task<bool> WriteSnAsync(string sn)
        {
            if (string.IsNullOrEmpty(sn))
            {
                SetError(SnServiceError.InvalidResponse, "SN不能为空");
                return false;
            }

            if (sn.Length > 31)
            {
                SetError(SnServiceError.InvalidResponse, "SN长度不能超过31字符");
                return false;
            }

            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                byte[] snBytes = Encoding.ASCII.GetBytes(sn);
                const int WRITE_CHUNK_SIZE = 5;
                int offset = 0;

                while (offset < snBytes.Length)
                {
                    if (_cancellationTokenSource?.IsCancellationRequested ?? false)
                        throw new OperationCanceledException();

                    int chunkSize = Math.Min(WRITE_CHUNK_SIZE, snBytes.Length - offset);
                    byte[] request = new byte[3 + chunkSize];
                    request[0] = 0x16;
                    request[1] = (byte)(offset & 0xFF);
                    request[2] = (byte)chunkSize;
                    Array.Copy(snBytes, offset, request, 3, chunkSize);

                    if (!_canService.SendMessage(_sendId, request))
                    {
                        SetError(SnServiceError.ConnectionFailed, "发送SN_WRITE请求失败");
                        return false;
                    }

                    if (!await WaitForResponseAsync())
                    {
                        SetError(SnServiceError.Timeout, "SN_WRITE响应超时");
                        return false;
                    }

                    byte[] response = _receivedResponse!;
                    if (response.Length < 2 || response[0] != 0x56)
                    {
                        SetError(SnServiceError.InvalidResponse, "SN_WRITE响应无效");
                        return false;
                    }

                    byte resultCode = response[1];
                    if (resultCode != ECU_CAN_PROG_RES_OK)
                    {
                        SetError(SnServiceError.InvalidResponse, GetErrorMessage(resultCode));
                        return false;
                    }

                    offset += chunkSize;
                }

                bool needNullTerminator = snBytes.Length < 31;
                if (needNullTerminator)
                {
                    byte[] request = new byte[3];
                    request[0] = 0x16;
                    request[1] = (byte)(snBytes.Length & 0xFF);
                    request[2] = 0x01;

                    if (!_canService.SendMessage(_sendId, request))
                    {
                        SetError(SnServiceError.ConnectionFailed, "发送SN_WRITE终止符失败");
                        return false;
                    }

                    if (!await WaitForResponseAsync())
                    {
                        SetError(SnServiceError.Timeout, "SN_WRITE终止符响应超时");
                        return false;
                    }

                    byte[] response = _receivedResponse!;
                    if (response.Length < 2 || response[0] != 0x56)
                    {
                        SetError(SnServiceError.InvalidResponse, "SN_WRITE终止符响应无效");
                        return false;
                    }
                }

                SnWritten?.Invoke(this, true);
                return true;
            }
            catch (OperationCanceledException)
            {
                SetError(SnServiceError.Timeout, "操作被取消");
                return false;
            }
            catch (Exception ex)
            {
                SetError(SnServiceError.UnknownError, ex.Message);
                return false;
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

        private void SetError(SnServiceError error, string message)
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
                default:
                    return $"未知错误码: 0x{resultCode:X2}";
            }
        }

        public async Task<bool> SaveSnAsync()
        {
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                byte[] request = { 0x13, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

                if (!_canService.SendMessage(_sendId, request))
                {
                    SetError(SnServiceError.ConnectionFailed, "发送SN_SAVE请求失败");
                    return false;
                }

                if (!await WaitForResponseAsync())
                {
                    SetError(SnServiceError.Timeout, "SN_SAVE响应超时");
                    return false;
                }

                byte[] response = _receivedResponse!;
                if (response.Length < 2 || response[0] != 0x53)
                {
                    SetError(SnServiceError.InvalidResponse, "SN_SAVE响应无效");
                    return false;
                }

                byte resultCode = response[1];
                if (resultCode != ECU_CAN_PROG_RES_OK)
                {
                    SetError(SnServiceError.InvalidResponse, GetErrorMessage(resultCode));
                    return false;
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                SetError(SnServiceError.Timeout, "操作被取消");
                return false;
            }
            catch (Exception ex)
            {
                SetError(SnServiceError.UnknownError, ex.Message);
                return false;
            }
            finally
            {
                _cancellationTokenSource?.Dispose();
            }
        }

        public async Task<bool> WriteDeviceAddressAsync(byte address)
        {
            if (address > 14)
            {
                SetError(SnServiceError.InvalidResponse, "设备地址必须为0或1~14");
                return false;
            }

            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                byte[] request = { 0x19, address, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

                if (!_canService.SendMessage(_sendId, request))
                {
                    SetError(SnServiceError.ConnectionFailed, "发送ADDR_WRITE请求失败");
                    return false;
                }

                if (!await WaitForResponseAsync())
                {
                    SetError(SnServiceError.Timeout, "ADDR_WRITE响应超时");
                    return false;
                }

                byte[] response = _receivedResponse!;
                if (response.Length < 2 || response[0] != 0x59)
                {
                    SetError(SnServiceError.InvalidResponse, "ADDR_WRITE响应无效");
                    return false;
                }

                byte resultCode = response[1];
                if (resultCode != ECU_CAN_PROG_RES_OK)
                {
                    SetError(SnServiceError.InvalidResponse, GetErrorMessage(resultCode));
                    return false;
                }

                return true;
            }
            catch (OperationCanceledException)
            {
                SetError(SnServiceError.Timeout, "操作被取消");
                return false;
            }
            catch (Exception ex)
            {
                SetError(SnServiceError.UnknownError, ex.Message);
                return false;
            }
            finally
            {
                _cancellationTokenSource?.Dispose();
            }
        }

        public async Task<byte?> ReadDeviceAddressAsync()
        {
            _cancellationTokenSource = new CancellationTokenSource();

            try
            {
                byte[] request = { 0x1A, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };

                if (!_canService.SendMessage(_sendId, request))
                {
                    SetError(SnServiceError.ConnectionFailed, "发送ADDR_READ请求失败");
                    return null;
                }

                if (!await WaitForResponseAsync())
                {
                    SetError(SnServiceError.Timeout, "ADDR_READ响应超时");
                    return null;
                }

                byte[] response = _receivedResponse!;
                if (response.Length < 3 || response[0] != 0x5A)
                {
                    SetError(SnServiceError.InvalidResponse, "ADDR_READ响应无效");
                    return null;
                }

                return response[2];
            }
            catch (OperationCanceledException)
            {
                SetError(SnServiceError.Timeout, "操作被取消");
                return null;
            }
            catch (Exception ex)
            {
                SetError(SnServiceError.UnknownError, ex.Message);
                return null;
            }
            finally
            {
                _cancellationTokenSource?.Dispose();
            }
        }

        public void Cancel()
        {
            _cancellationTokenSource?.Cancel();
        }
    }
}