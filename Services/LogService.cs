using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BOOTWR.Services
{
    public enum LogLevel
    {
        Info,
        Warning,
        Error,
        Debug
    }

    public class LogMessage
    {
        public DateTime Timestamp { get; set; }
        public LogLevel Level { get; set; }
        public string Message { get; set; } = string.Empty;

        public string LevelText
        {
            get
            {
                switch (Level)
                {
                    case LogLevel.Info:
                        return "INFO";
                    case LogLevel.Warning:
                        return "WARN";
                    case LogLevel.Error:
                        return "ERROR";
                    case LogLevel.Debug:
                        return "DEBUG";
                    default:
                        return "INFO";
                }
            }
        }

        public string FormattedMessage => $"[{Timestamp:HH:mm:ss.fff}] [{LevelText}] {Message}";
    }

    public class LogService
    {
        private readonly ObservableCollection<LogMessage> _logMessages = new ObservableCollection<LogMessage>();
        private readonly string _logFilePath;
        private object _lockObj = new object();

        public ObservableCollection<LogMessage> LogMessages => _logMessages;

        public event EventHandler<LogMessage>? LogAdded;
        public event EventHandler? LogsCleared;

        public LogService()
        {
            _logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs", $"{DateTime.Now:yyyyMMdd}.log");
            Directory.CreateDirectory(Path.GetDirectoryName(_logFilePath)!);
        }

        public void Info(string message)
        {
            AddLog(LogLevel.Info, message);
        }

        public void Warning(string message)
        {
            AddLog(LogLevel.Warning, message);
        }

        public void Error(string message)
        {
            AddLog(LogLevel.Error, message);
        }

        public void Debug(string message)
        {
            AddLog(LogLevel.Debug, message);
        }

        private void AddLog(LogLevel level, string message)
        {
            var logMessage = new LogMessage
            {
                Timestamp = DateTime.Now,
                Level = level,
                Message = message
            };

            lock (_lockObj)
            {
                _logMessages.Add(logMessage);

                if (_logMessages.Count > 1000)
                {
                    _logMessages.RemoveAt(0);
                }
            }

            LogAdded?.Invoke(this, logMessage);

            SaveToFile(logMessage);
        }

        private async void SaveToFile(LogMessage message)
        {
            try
            {
                using (var writer = new StreamWriter(_logFilePath, true, Encoding.UTF8))
                {
                    await writer.WriteLineAsync(message.FormattedMessage);
                }
            }
            catch
            {
            }
        }

        public void Clear()
        {
            lock (_lockObj)
            {
                _logMessages.Clear();
            }

            LogsCleared?.Invoke(this, EventArgs.Empty);
        }

        public IEnumerable<LogMessage> FilterByLevel(LogLevel level)
        {
            lock (_lockObj)
            {
                return _logMessages.Where(m => m.Level == level).ToList();
            }
        }

        public IEnumerable<LogMessage> Search(string keyword)
        {
            if (string.IsNullOrEmpty(keyword))
            {
                return new List<LogMessage>(_logMessages);
            }

            lock (_lockObj)
            {
                return _logMessages.Where(m => m.Message.Contains(keyword, StringComparison.OrdinalIgnoreCase)).ToList();
            }
        }
    }
}
