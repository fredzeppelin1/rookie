using System;
using System.IO;

namespace AndroidSideloader.Utilities
{
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error,
        Trace,
        Fatal
    }

    public static class Logger
    {
        private static readonly SettingsManager Settings = SettingsManager.Instance;
        private static readonly object LockObject = new object();
        private static string _logFilePath = Settings.CurrentLogPath;

        public static void Initialize()
        {
            try
            {
                // Use path from settings (which is already set to full file path)
                _logFilePath = Settings.CurrentLogPath;

                // Set default log path if settings path is empty
                if (string.IsNullOrEmpty(_logFilePath))
                {
                    _logFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "debuglog.txt");
                    Settings.CurrentLogPath = _logFilePath;
                }

                // Create directory if it doesn't exist
                var logDirectory = Path.GetDirectoryName(_logFilePath);
                if (!string.IsNullOrEmpty(logDirectory) && !Directory.Exists(logDirectory))
                {
                    Directory.CreateDirectory(logDirectory);
                }

                // Create log file if it doesn't exist
                if (!File.Exists(_logFilePath))
                {
                    using (var fs = File.Create(_logFilePath))
                    {
                        // Create empty file
                    }
                }

                // Update settings with log path and save
                Settings.CurrentLogPath = _logFilePath;
                Settings.Save();

                // Initial log entry
                Log($"Logger initialized at: {DateTime.Now:hh:mmtt(UTC)}", LogLevel.Info);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error initializing logger: {ex.Message}");
            }
        }

        public static bool Log(string text, LogLevel logLevel = LogLevel.Info, bool ret = true)
        {
            if (string.IsNullOrWhiteSpace(text) || text.Length <= 5)
                return ret;

            // Initialize logger if not already initialized
            if (string.IsNullOrEmpty(_logFilePath))
            {
                Initialize();
            }

            var time = DateTime.UtcNow.ToString("hh:mm:ss.fff tt (UTC): ");
            var newline = text.Length > 40 && text.Contains("\n") ? "\n\n" : "\n";
            var logEntry = time + "[" + logLevel.ToString().ToUpper() + "] [" + GetCallerInfo() + "] " + text + newline;

            try
            {
                lock (LockObject)
                {
                    File.AppendAllText(_logFilePath, logEntry);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error writing to log: {ex.Message}");
            }

            return ret;
        }

        private static string GetCallerInfo()
        {
            var stackTrace = new System.Diagnostics.StackTrace(true);
            if (stackTrace.FrameCount >= 3)
            {
                var frame = stackTrace.GetFrame(2);
                var method = frame.GetMethod();
                var className = method.DeclaringType?.Name;
                var methodName = method.Name;
                var callerInfo = $"{className}.{methodName}";
                return callerInfo;
            }

            return string.Empty;
        }
    }
}
