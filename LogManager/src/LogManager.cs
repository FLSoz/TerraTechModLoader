using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using NLog;
using NLog.Config;
using NLog.Targets;


namespace LogManager
{
    // LogConfig - for when you only need a single output file for the logger
    public struct LogConfig
    {
        public string path;
        public string layout;
        public LogLevel defaultMinLevel;
        public bool keepOldFiles;
    }

    // Target config - defines a target file for logging
    public struct TargetConfig
    {
        public string path;
        public string layout;
        public bool keepOldFiles;
    }

    public struct LogTarget
    {
        public FileTarget logFile;
        public TargetConfig config;
    }

    public class TTLogManager : ModBase
    {
        static TTLogManager()
        {
            ModuleInitializer.Run();
        }

        internal static LoggingConfiguration config;
        internal static readonly string TTSteamDir = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => assembly.GetName().Name == "Assembly-CSharp").First().Location
            .Replace("Assembly-CSharp.dll", ""), @"../../"
        ));
        // internal static readonly string TTSteamDir = @"E:/Steam/steamapps/common/TerraTech";
        private static readonly string LogsDir = Path.Combine(TTSteamDir, "Logs");

        internal static ConsoleTarget logconsole = new ConsoleTarget("logconsole")
        {
            Layout = "[${logger:shortName=true}] ${level:uppercase=true:padding=-5:alignmentOnTruncation=left} | ${time} | ${message}  ${exception}"
        };

        internal static Dictionary<string, LogLevel> ConfiguredLogLevels = new Dictionary<string, LogLevel>();
        internal static LogLevel ConfiguredGlobalLogLevel = null;

        internal static string GetRelativePath(string logPath, string basePath)
        {
            string fullLogPath = Path.GetFullPath(logPath);
            string fullBasePath = Path.GetFullPath(basePath);

            int index = 0;
            int maxlen = Math.Min(fullBasePath.Length, fullLogPath.Length);
            for (int i = 0; i < maxlen; i++)
            {
                if (fullLogPath[i] != fullBasePath[i])
                {
                    index = i;
                    break;
                }
            }
            return logPath.Substring(index);
        }

        public static LogTarget RegisterLoggingTarget(string targetName, TargetConfig targetConfig)
        {
            string shortTargetName = targetName.Substring(targetName.LastIndexOf('.') + 1);

            Console.WriteLine($"[LogManager] Registering logger {targetName}");

            string fullPath = targetConfig.path;
            if (fullPath is null || fullPath.Length == 0)
            {
                fullPath = Path.Combine(LogsDir, $"{shortTargetName}.log");
            }
            else if (fullPath.Contains(LogsDir))
            {
                // Assume this is a full path already
                if (!fullPath.EndsWith(".log"))
                {
                    fullPath = Path.Combine(fullPath, $"{shortTargetName}.log");
                }
            }
            else if (!fullPath.EndsWith(".log"))
            {
                fullPath = Path.Combine(LogsDir, fullPath, $"{shortTargetName}.log");
            }
            else
            {
                fullPath = Path.Combine(LogsDir, fullPath);
            }

            return new LogTarget {
                logFile = new FileTarget($"logfile-{targetConfig.path}")
                {
                    FileName = fullPath,
                    Layout = targetConfig.layout is null || targetConfig.layout.Length == 0 ?
                        "${longdate} | ${level:uppercase=true:padding=-5:alignmentOnTruncation=left} | ${logger:shortName=true} | ${message}  ${exception}" :
                        targetConfig.layout,
                    EnableFileDelete = false,
                    DeleteOldFileOnStartup = false
                },
                config = new TargetConfig
                {
                    path = fullPath,
                    layout = targetConfig.layout,
                    keepOldFiles = targetConfig.keepOldFiles
                }
            };
        }

        public static void RegisterLogger(Logger logger, LogTarget target, LogLevel defaultMinLevel = null)
        {
            // Manually handle deletion ourselves, or the ModManager log will constantly get reset
            if (!target.config.keepOldFiles)
            {
                if (File.Exists(target.config.path))
                {
                    File.Delete(target.config.path);
                }
            }

            LogLevel minLevel = defaultMinLevel is null ? LogLevel.Debug : defaultMinLevel;

            if (ConfiguredLogLevels.TryGetValue(logger.Name, out LogLevel configuredLevel) && configuredLevel != null)
            {
                Console.WriteLine($"Registering logger {logger.Name} with logging level {configuredLevel}");
                minLevel = configuredLevel;
            }
            else
            {
                string shortLoggerName = logger.Name.Substring(logger.Name.LastIndexOf('.') + 1);
                if (ConfiguredLogLevels.TryGetValue(shortLoggerName, out configuredLevel) && configuredLevel != null)
                {
                    Console.WriteLine($"Registering logger {shortLoggerName} with logging level {configuredLevel}");
                    minLevel = configuredLevel;
                }
                else if (ConfiguredGlobalLogLevel != null)
                {
                    Console.WriteLine($"Registering logger {shortLoggerName} with GLOBAL DEFAULT logging level {ConfiguredGlobalLogLevel}");
                    minLevel = ConfiguredGlobalLogLevel;
                }
                else
                {
                    Console.WriteLine($"Registering logger {shortLoggerName} with default logging level {minLevel}");
                }
            }

            config.AddRule(minLevel, LogLevel.Fatal, target.logFile, logger.Name);
            NLog.LogManager.Configuration = config;
        }

        public static void RegisterLogger(Logger logger, LogConfig logConfig)
        {
            TargetConfig generatedConfig = new TargetConfig
            {
                path = logConfig.path,
                layout = logConfig.layout,
                keepOldFiles = logConfig.keepOldFiles
            };
            LogTarget logTarget = RegisterLoggingTarget(logger.Name, generatedConfig);
            RegisterLogger(logger, logTarget, logConfig.defaultMinLevel);
        }

        public override void Init()
        {
            return;
        }

        public override void DeInit()
        {
            return;
        }
    }
}
