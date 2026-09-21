using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using ZLGAPI;

namespace BOOTWR.Services
{
    public enum CanDeviceType
    {
        USBCANFD_200U = 41,
        USBCAN_1 = 3
    }

    public enum CanBaudRate
    {
        Bps10000 = 10000,
        Bps20000 = 20000,
        Bps50000 = 50000,
        Bps100000 = 100000,
        Bps125000 = 125000,
        Bps250000 = 250000,
        Bps500000 = 500000,
        Bps800000 = 800000,
        Bps1000000 = 1000000
    }

    public enum CanMode
    {
        Normal = 0,
        LoopBack = 1,
        Silent = 2,
        LoopBackSilent = 3
    }

    public class CanCommunicationService
    {
        private IntPtr _deviceHandle = IntPtr.Zero;
        private IntPtr _channelHandle = IntPtr.Zero;
        private bool _isConnected = false;
        private object _lockObj = new object();

        // 已从设备驱动接收、尚未被任何等待方消费的帧（按响应命令字节条件消费，避免并发等待方互吃响应）
        private readonly List<(uint canId, bool isExtended, byte[] data)> _pendingFrames = new();

        public bool IsConnected => _isConnected;

        public event EventHandler<CanMessageReceivedEventArgs>? MessageReceived;
        public event EventHandler<CanMessageReceivedEventArgs>? MessageSent;

        public bool Connect(CanDeviceType deviceType, int deviceIndex, int channelIndex, CanBaudRate baudRate, bool useCanFd = false)
        {
            return Connect(deviceType, deviceIndex, channelIndex, baudRate, CanMode.Normal, useCanFd, 2000000, true);
        }

        public bool Connect(CanDeviceType deviceType, int deviceIndex, int channelIndex, CanBaudRate baudRate, CanMode mode, bool useCanFd = false, uint dataBitRate = 2000000, bool enableTerminalResistance = true)
        {
            lock (_lockObj)
            {
                if (_isConnected)
                {
                    Disconnect();
                }

                uint zlgDeviceType = (uint)deviceType;
                _deviceHandle = ZLGCAN.ZCAN_OpenDevice(zlgDeviceType, (uint)deviceIndex, 0);

                if (_deviceHandle == IntPtr.Zero)
                {
                    return false;
                }

                if (useCanFd)
                {
                    string canFdArbitrationBitRatePath = string.Format("{0}/canfd_abit_baud_rate", channelIndex);
                    uint setArbitrationBaudRateResult = ZLGCAN.ZCAN_SetValue(_deviceHandle, canFdArbitrationBitRatePath, ((uint)baudRate).ToString());
                    if (setArbitrationBaudRateResult != 1)
                    {
                        ZLGCAN.ZCAN_CloseDevice(_deviceHandle);
                        _deviceHandle = IntPtr.Zero;
                        return false;
                    }

                    string canFdDataBitRatePath = string.Format("{0}/canfd_dbit_baud_rate", channelIndex);
                    uint setDataBaudRateResult = ZLGCAN.ZCAN_SetValue(_deviceHandle, canFdDataBitRatePath, dataBitRate.ToString());
                    if (setDataBaudRateResult != 1)
                    {
                        ZLGCAN.ZCAN_CloseDevice(_deviceHandle);
                        _deviceHandle = IntPtr.Zero;
                        return false;
                    }

                    // 终端电阻（0=断开，1=接入），仅 CAN FD 设备支持软件配置；按实际通道索引设置
                    string resistancePath = string.Format("{0}/initenal_resistance", channelIndex);
                    uint setResistanceResult = ZLGCAN.ZCAN_SetValue(_deviceHandle, resistancePath, enableTerminalResistance ? "1" : "0");
                    if (setResistanceResult != 1)
                    {
                        ZLGCAN.ZCAN_CloseDevice(_deviceHandle);
                        _deviceHandle = IntPtr.Zero;
                        return false;
                    }

                    var initConfig = new ZLGCAN.ZCAN_CHANNEL_INIT_CONFIG();
                    initConfig.can_type = 1U;

                    var canfdConfig = new ZLGCAN._ZCAN_CHANNEL_CANFD_INIT_CONFIG();
                    canfdConfig.acc_code = 0;
                    canfdConfig.acc_mask = 0xFFFFFFFF;
                    canfdConfig.abit_timing = 0;
                    canfdConfig.dbit_timing = 0;
                    canfdConfig.brp = 0;
                    canfdConfig.filter = 0;
                    canfdConfig.mode = (byte)mode;
                    canfdConfig.pad = 0;
                    canfdConfig.reserved = 0;

                    initConfig.config.canfd = canfdConfig;

                    _channelHandle = ZLGCAN.ZCAN_InitCAN(_deviceHandle, (uint)channelIndex, ref initConfig);

                    if (_channelHandle == IntPtr.Zero)
                    {
                        ZLGCAN.ZCAN_CloseDevice(_deviceHandle);
                        _deviceHandle = IntPtr.Zero;
                        return false;
                    }
                }
                else
                {
                    //string canBitRatePath = string.Format("{0}/baud_rate", channelIndex);
                    //uint setBaudRateResult = ZLGCAN.ZCAN_SetValue(_deviceHandle, canBitRatePath, ((uint)baudRate).ToString());
                    //if (setBaudRateResult != 1)
                    //{
                    //    ZLGCAN.ZCAN_CloseDevice(_deviceHandle);
                    //    _deviceHandle = IntPtr.Zero;
                    //    return false;
                    //}

                    var initConfig = new ZLGCAN.ZCAN_CHANNEL_INIT_CONFIG();
                    initConfig.can_type = 0U;

                    var canConfig = new ZLGCAN._ZCAN_CHANNEL_CAN_INIT_CONFIG();
                    canConfig.acc_code = 0;
                    canConfig.acc_mask = 0xFFFFFFFF;
                    canConfig.reserved = 0;
                    canConfig.filter = 0;
                    canConfig.mode = (byte)mode;

                    SetBaudRateConfig(baudRate, ref canConfig);

                    initConfig.config.can = canConfig;

                    _channelHandle = ZLGCAN.ZCAN_InitCAN(_deviceHandle, (uint)channelIndex, ref initConfig);

                    if (_channelHandle == IntPtr.Zero)
                    {
                        ZLGCAN.ZCAN_CloseDevice(_deviceHandle);
                        _deviceHandle = IntPtr.Zero;
                        return false;
                    }
                }

                uint result = ZLGCAN.ZCAN_StartCAN(_channelHandle);

                if (result != 1)
                {
                    ZLGCAN.ZCAN_CloseDevice(_deviceHandle);
                    _deviceHandle = IntPtr.Zero;
                    _channelHandle = IntPtr.Zero;
                    return false;
                }

                _isConnected = true;

                return true;
            }
        }

        private void SetBaudRateConfig(CanBaudRate baudRate, ref ZLGCAN._ZCAN_CHANNEL_CAN_INIT_CONFIG config)
        {
            switch (baudRate)
            {
                case CanBaudRate.Bps10000:
                    config.timing0 = 0x31;
                    config.timing1 = 0x1C;
                    break;
                case CanBaudRate.Bps20000:
                    config.timing0 = 0x18;
                    config.timing1 = 0x1C;
                    break;
                case CanBaudRate.Bps50000:
                    config.timing0 = 0x09;
                    config.timing1 = 0x1C;
                    break;
                case CanBaudRate.Bps100000:
                    config.timing0 = 0x04;
                    config.timing1 = 0x1C;
                    break;
                case CanBaudRate.Bps125000:
                    config.timing0 = 0x03;
                    config.timing1 = 0x1C;
                    break;
                case CanBaudRate.Bps250000:
                    config.timing0 = 0x01;
                    config.timing1 = 0x1C;
                    break;
                case CanBaudRate.Bps500000:
                    config.timing0 = 0x00;
                    config.timing1 = 0x1C;
                    break;
                case CanBaudRate.Bps800000:
                    config.timing0 = 0x00;
                    config.timing1 = 0x16;
                    break;
                case CanBaudRate.Bps1000000:
                    config.timing0 = 0x00;
                    config.timing1 = 0x14;
                    break;
                default:
                    config.timing0 = 0x04;
                    config.timing1 = 0x1C;
                    break;
            }
        }

        public void Disconnect()
        {
            lock (_lockObj)
            {
                _isConnected = false;

                if (_channelHandle != IntPtr.Zero)
                {
                    ZLGCAN.ZCAN_ResetCAN(_channelHandle);
                    _channelHandle = IntPtr.Zero;
                }

                if (_deviceHandle != IntPtr.Zero)
                {
                    ZLGCAN.ZCAN_CloseDevice(_deviceHandle);
                    _deviceHandle = IntPtr.Zero;
                }
            }
        }

        public bool ClearBuffer()
        {
            lock (_lockObj)
            {
                if (!_isConnected || _channelHandle == IntPtr.Zero)
                    return false;

                _pendingFrames.Clear();

                uint result = ZLGCAN.ZCAN_ClearBuffer(_channelHandle);
                return result == 1;
            }
        }

        public bool SendMessage(uint canId, byte[] data, bool isExtendedId = false, bool raiseEvent = true)
        {
            lock (_lockObj)
            {
                if (!_isConnected || _channelHandle == IntPtr.Zero)
                    return false;

                if (data.Length > 8)
                    return false;

                var frame = new ZLGCAN.can_frame();
                frame.can_id = isExtendedId ? canId | 0x80000000 : canId;
                frame.can_dlc = (byte)data.Length;
                frame.data = new byte[8];
                Array.Copy(data, frame.data, data.Length);

                var transmitData = new ZLGCAN.ZCAN_Transmit_Data();
                transmitData.frame = frame;
                transmitData.transmit_type = 0;

                int size = Marshal.SizeOf(transmitData);
                IntPtr pTransmit = Marshal.AllocHGlobal(size);
                Marshal.StructureToPtr(transmitData, pTransmit, false);

                uint result = ZLGCAN.ZCAN_Transmit(_channelHandle, pTransmit, 1);

                Marshal.FreeHGlobal(pTransmit);

                if (result == 1 && raiseEvent)
                {
                    RaiseMessageSent(canId, isExtendedId, data);
                }

                return result == 1;
            }
        }

        private void RaiseMessageSent(uint canId, bool isExtendedId, byte[] data)
        {
            var handler = MessageSent;
            if (handler == null)
                return;

            try
            {
                handler.Invoke(this, new CanMessageReceivedEventArgs
                {
                    CanId = canId,
                    IsExtendedId = isExtendedId,
                    Data = data,
                    Timestamp = DateTime.Now
                });
            }
            catch
            {
            }
        }

        public bool ReceiveMessage(out uint canId, out bool isExtendedId, out byte[] data)
        {
            lock (_lockObj)
            {
                DrainPendingLocked();

                if (_pendingFrames.Count == 0)
                {
                    canId = 0;
                    isExtendedId = false;
                    data = Array.Empty<byte>();
                    return false;
                }

                // 原始语义：取最早的一帧
                (uint cid, bool ext, byte[] bytes) = _pendingFrames[0];
                _pendingFrames.RemoveAt(0);

                canId = cid;
                isExtendedId = ext;
                data = bytes;
                return true;
            }
        }

        /// <summary>
        /// 条件接收：仅当存在 canId 匹配且首字节命中 expectedFirstBytes 的帧时消费并返回；
        /// 未命中时帧保留在待匹配队列，供其他等待方消费。用于多服务并发等待时不互吃响应。
        /// </summary>
        public bool ReceiveMessageFiltered(uint expectedId, byte[]? expectedFirstBytes, out uint canId, out bool isExtendedId, out byte[] data)
        {
            lock (_lockObj)
            {
                DrainPendingLocked();

                for (int i = 0; i < _pendingFrames.Count; i++)
                {
                    var f = _pendingFrames[i];
                    if (f.canId == expectedId &&
                        (expectedFirstBytes == null ||
                         (f.data.Length > 0 && expectedFirstBytes.Contains(f.data[0]))))
                    {
                        _pendingFrames.RemoveAt(i);
                        canId = f.canId;
                        isExtendedId = f.isExtended;
                        data = f.data;
                        return true;
                    }
                }
            }

            canId = 0;
            isExtendedId = false;
            data = Array.Empty<byte>();
            return false;
        }

        /// <summary>从设备驱动接收所有可用的帧，存入待匹配队列并触发消息事件。须在 _lockObj 内调用。</summary>
        private void DrainPendingLocked()
        {
            if (!_isConnected || _channelHandle == IntPtr.Zero)
                return;

            while (true)
            {
                uint count = ZLGCAN.ZCAN_GetReceiveNum(_channelHandle, 0);
                if (count == 0)
                    break;

                IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(ZLGCAN.ZCAN_Receive_Data)));
                try
                {
                    uint result = ZLGCAN.ZCAN_Receive(_channelHandle, ptr, 1, 0);
                    if (result == 0)
                        break;

                    var receiveData = Marshal.PtrToStructure<ZLGCAN.ZCAN_Receive_Data>(ptr);
                    uint frameCanId = receiveData.frame.can_id & 0x7FFFFFFFU;
                    bool frameExt = (receiveData.frame.can_id & 0x80000000U) != 0;
                    byte dlc = receiveData.frame.can_dlc;
                    if (dlc > 8)
                        dlc = 8;
                    byte[] frameData = new byte[dlc];
                    Array.Copy(receiveData.frame.data, frameData, dlc);

                    _pendingFrames.Add((frameCanId, frameExt, frameData));
                    RaiseMessageReceived(frameCanId, frameExt, frameData);
                }
                finally
                {
                    Marshal.FreeHGlobal(ptr);
                }
            }

            while (true)
            {
                uint count = ZLGCAN.ZCAN_GetReceiveNum(_channelHandle, 1);
                if (count == 0)
                    break;

                IntPtr ptr = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(ZLGCAN.ZCAN_ReceiveFD_Data)));
                try
                {
                    uint result = ZLGCAN.ZCAN_ReceiveFD(_channelHandle, ptr, 1, 0);
                    if (result == 0)
                        break;

                    var receiveData = Marshal.PtrToStructure<ZLGCAN.ZCAN_ReceiveFD_Data>(ptr);
                    uint frameCanId = receiveData.frame.can_id & 0x7FFFFFFFU;
                    bool frameExt = (receiveData.frame.can_id & 0x80000000U) != 0;
                    byte len = receiveData.frame.len;
                    byte[] frameData = new byte[len];
                    Array.Copy(receiveData.frame.data, frameData, len);

                    _pendingFrames.Add((frameCanId, frameExt, frameData));
                    RaiseMessageReceived(frameCanId, frameExt, frameData);
                }
                finally
                {
                    Marshal.FreeHGlobal(ptr);
                }
            }
        }

        private void RaiseMessageReceived(uint canId, bool isExtendedId, byte[] data)
        {
            var handler = MessageReceived;
            if (handler == null)
                return;

            try
            {
                handler.Invoke(this, new CanMessageReceivedEventArgs
                {
                    CanId = canId,
                    IsExtendedId = isExtendedId,
                    Data = data,
                    Timestamp = DateTime.Now
                });
            }
            catch
            {
            }
        }
    }

    public class CanMessageReceivedEventArgs : EventArgs
    {
        public uint CanId { get; set; }
        public bool IsExtendedId { get; set; }
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public DateTime Timestamp { get; set; }
    }
}
