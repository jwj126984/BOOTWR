using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BOOTWR.Services
{
    public enum MessageDirection
    {
        Send,
        Receive
    }

    public class CanMessageItem
    {
        public uint CanId { get; set; }
        public bool IsExtendedId { get; set; }
        public byte[] Data { get; set; } = Array.Empty<byte>();
        public DateTime Timestamp { get; set; }
        public MessageDirection Direction { get; set; }

        public int DataLength => Data.Length;

        public string IdHex => $"0x{CanId:X8}";

        public string DataHex
        {
            get
            {
                if (Data.Length == 0)
                    return string.Empty;
                return BitConverter.ToString(Data).Replace("-", " ");
            }
        }

        public string DirectionText => Direction == MessageDirection.Send ? "发送" : "接收";

        public string TimestampText => Timestamp.ToString("HH:mm:ss.fff");
    }

    public class MessageManager
    {
        private readonly ObservableCollection<CanMessageItem> _messages = new ObservableCollection<CanMessageItem>();
        private object _lockObj = new object();
        private uint _udsSendId = 0x7E0;
        private uint _udsReceiveId = 0x7E8;

        public ObservableCollection<CanMessageItem> Messages => _messages;

        public event EventHandler<CanMessageItem>? MessageAdded;
        public event EventHandler? MessagesCleared;

        public void SetUdsFilter(uint sendId, uint receiveId)
        {
            _udsSendId = sendId;
            _udsReceiveId = receiveId;
        }

        public void SetDefaultFilter()
        {
            _udsSendId = 0x7E0;
            _udsReceiveId = 0x7E8;
        }

        private bool IsUdsMessage(uint canId)
        {
            return canId == _udsSendId || canId == _udsReceiveId;
        }

        public void AddMessage(CanMessageItem message)
        {
            if (!IsUdsMessage(message.CanId))
                return;

            lock (_lockObj)
            {
                _messages.Add(message);

                if (_messages.Count > 1000)
                {
                    _messages.RemoveAt(0);
                }
            }

            MessageAdded?.Invoke(this, message);
        }

        public void AddSendMessage(uint canId, byte[] data, bool isExtendedId = false)
        {
            AddMessage(new CanMessageItem
            {
                CanId = canId,
                IsExtendedId = isExtendedId,
                Data = data.ToArray(),
                Timestamp = DateTime.Now,
                Direction = MessageDirection.Send
            });
        }

        public void AddReceiveMessage(uint canId, byte[] data, bool isExtendedId = false)
        {
            AddMessage(new CanMessageItem
            {
                CanId = canId,
                IsExtendedId = isExtendedId,
                Data = data.ToArray(),
                Timestamp = DateTime.Now,
                Direction = MessageDirection.Receive
            });
        }

        public void Clear()
        {
            lock (_lockObj)
            {
                _messages.Clear();
            }

            MessagesCleared?.Invoke(this, EventArgs.Empty);
        }

        public IEnumerable<CanMessageItem> FilterById(uint canId)
        {
            lock (_lockObj)
            {
                return _messages.Where(m => m.CanId == canId).ToList();
            }
        }

        public IEnumerable<CanMessageItem> FilterByDirection(MessageDirection direction)
        {
            lock (_lockObj)
            {
                return _messages.Where(m => m.Direction == direction).ToList();
            }
        }
    }
}
