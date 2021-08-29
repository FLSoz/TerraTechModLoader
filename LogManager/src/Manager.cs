using System;
using System.Linq;
using System.Reflection;
using System.IO;
using NLog;
using NLog.Config;


namespace LogManager
{
    public static class Manager
    {
        internal static LoggingConfiguration config;
        internal static readonly string TTSteamDir = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => assembly.GetName().Name == "Assembly-CSharp").First().Location
            .Replace("Assembly-CSharp.dll", ""), @"../../"
        ));
        // internal static readonly string TTSteamDir = @"E:/Steam/steamapps/common/TerraTech";
        private static readonly string LogsDir = Path.Combine(TTSteamDir, "Logs");

        public struct LogConfig
        {
            public string path;
            public string layout;
            public LogLevel minLevel;
            public bool keepOldFiles;
        }

        public static void RegisterLogger(Logger logger, LogConfig logConfig)
        {
            string loggerName = logger.Name;
            string shortLoggerName = loggerName.Substring(loggerName.LastIndexOf('.') + 1);
            string path = logConfig.path;
            if (path is null || path.Length == 0)
            {
                path = Path.Combine(LogsDir, $"{shortLoggerName}.log");
            }

            // Targets where to log to: File and Console
            var logfile = new NLog.Targets.FileTarget($"logfile-{loggerName}")
            {
                FileName = path,
                Layout = logConfig.layout is null || logConfig.layout.Length == 0 ?
                    "${longdate} | ${level:uppercase=true:padding=-5:alignmentOnTruncation=left} | ${logger:shortName=true} | ${message}  ${exception}" :
                    logConfig.layout,
                EnableFileDelete = logConfig.keepOldFiles ? false : true,
                DeleteOldFileOnStartup = logConfig.keepOldFiles ? false : true
            };

            Manager.config.AddRule(logConfig.minLevel is null ? LogLevel.Debug : logConfig.minLevel, LogLevel.Fatal, logfile, loggerName);
            NLog.LogManager.Configuration = Manager.config;
        }
    }
}
