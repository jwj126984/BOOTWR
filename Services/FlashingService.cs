using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

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

    public class FlashingService
    {
        private readonly CanCommunicationService _canService;
        private byte[] _flashData = Array.Empty<byte>();
        private uint _crc32;
        private FlashingState _state = FlashingState.Idle;
        private FlashingError _error = FlashingError.None;
        private string _errorMessage = string.Empty;
        private int _currentProgress = 0;
        private object _lockObj = new object();
        private CancellationTokenSource? _cancellationTokenSource;

        private uint _sendId = 0x7E0;
        private uint _receiveId = 0x7E8;
        private const int TIMEOUT_MS = 30000;

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

        public event EventHandler<FlashingStateChangedEventArgs>? StateChanged;
        public event EventHandler<ProgressChangedEventArgs>? ProgressChanged;

        public FlashingService(CanCommunicationService canService)
        {
            _canService = canService;
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
            _cancellationTokenSource?.Cancel();
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

                if (!_canService.SendMessage(_sendId, request))
                {
                    SetError(FlashingError.ConnectionFailed, "发送GET_STATUS请求失败");
                    return null;
                }

                if (!await WaitForResponseAsync(5000))
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

            if (!_canService.SendMessage(_sendId, request))
            {
                SetError(FlashingError.ConnectionFailed, "发送SESSION请求失败");
                return false;
            }

            if (!await WaitForResponseAsync())
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

            if (!_canService.SendMessage(_sendId, request))
            {
                SetError(FlashingError.ConnectionFailed, "发送ERASE请求失败");
                return false;
            }

            if (!await WaitForResponseAsync())
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

                if (!_canService.SendMessage(_sendId, data, false, false))
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
            if (!await WaitForResponseAsync(TIMEOUT_MS))
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

            if (!_canService.SendMessage(_sendId, request))
            {
                SetError(FlashingError.ConnectionFailed, "发送COMMIT请求失败");
                return false;
            }

            if (!await WaitForResponseAsync())
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
