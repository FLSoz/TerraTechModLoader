using System;
using System.Linq;
using System.Collections.Generic;
using System.Reflection;
using System.IO;
using NLog;
using NLog.Config;
using NLog.Targets;
using HarmonyLib;


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
    public struct TargetConfig : IEquatable<TargetConfig>
    {
        // filename excludes extension. e.g. filename of TEST will output TEST.log
        public string filename;
        public string path;
        public string layout;
        public bool keepOldFiles;

        public bool Equals(TargetConfig other)
        {
            return this.filename == other.filename && this.path == other.path && this.layout == other.layout && this.keepOldFiles == other.keepOldFiles;
        }
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

        internal const string HarmonyID = "com.flsoz.ttmodding.logmanager";
        internal static Harmony harmony = new Harmony(HarmonyID);

        internal static LoggingConfiguration config;
        internal static bool EnableVanillaLogs = false;
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
        private static Dictionary<string, LogTarget> TargetPathDictionary = new Dictionary<string, LogTarget>();
        internal static LogLevel ConfiguredGlobalLogLevel = null;

        internal static string GetFilePath(string path, string filename)
        {
            string adjPath = path == null ? "" : path;
            string adjFilename = filename == null ? "" : filename;
            return Path.Combine(adjPath, adjFilename);
        }

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

        public static LogTarget RegisterLoggingTarget(TargetConfig targetConfig)
        {
            // Preliminary cache check
            string targetPath = Path.ChangeExtension(GetFilePath(targetConfig.path, targetConfig.filename), ".log").Trim(Path.DirectorySeparatorChar);
            if (!TargetPathDictionary.TryGetValue(targetPath, out LogTarget target))
            {
                string shortTargetName = targetConfig.filename;
                Console.WriteLine($"[LogManager] Registering logger {shortTargetName}");

                // Calculate full path
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
                targetPath = fullPath.Substring(fullPath.IndexOf(LogsDir) + LogsDir.Length).Trim(Path.DirectorySeparatorChar);

                // Seconday cache check
                if (!TargetPathDictionary.TryGetValue(targetPath, out target))
                {
                    target = new LogTarget
                    {
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

                    TargetPathDictionary.Add(targetPath, target);
                }
                else
                {
                    Console.WriteLine($"[LogManager] Already registered logger with path {targetPath}");
                }
            }
            else
            {
                Console.WriteLine($"[LogManager] Already registered logger with path {targetPath}");
            }
            return target;
        }

        public static LogTarget RegisterLoggingTarget(string targetName, TargetConfig targetConfig)
        {
            if (targetConfig.filename == null)
            {
                string shortTargetName = targetName.Substring(targetName.LastIndexOf('.') + 1);
                targetConfig.filename = shortTargetName;
            }
            return RegisterLoggingTarget(targetConfig);
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

            LogLevel minLevel = defaultMinLevel is null ? LogLevel.Error : defaultMinLevel;

            if (ConfiguredLogLevels.TryGetValue(logger.Name, out LogLevel configuredLevel) && configuredLevel != null)
            {
                Console.WriteLine($"[LogManager]  Registering logger {logger.Name} with logging level {configuredLevel} at {target.config.path}");
                minLevel = configuredLevel;
            }
            else
            {
                string shortLoggerName = logger.Name.Substring(logger.Name.LastIndexOf('.') + 1);
                if (ConfiguredLogLevels.TryGetValue(shortLoggerName, out configuredLevel) && configuredLevel != null)
                {
                    Console.WriteLine($"[LogManager]  Registering logger {shortLoggerName} with logging level {configuredLevel} at {target.config.path}");
                    minLevel = configuredLevel;
                }
                else if (ConfiguredGlobalLogLevel != null)
                {
                    Console.WriteLine($"[LogManager]  Registering logger {shortLoggerName} with GLOBAL DEFAULT logging level {ConfiguredGlobalLogLevel} at {target.config.path}");
                    minLevel = ConfiguredGlobalLogLevel;
                }
                else
                {
                    Console.WriteLine($"[LogManager]  Registering logger {shortLoggerName} with default logging level {minLevel} at {target.config.path}");
                }
            }

            if (EnableVanillaLogs)
            {
                config.AddRule(minLevel, LogLevel.Fatal, logconsole, logger.Name);
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

        /// <summary>
        /// We patch any loggers in here that match our criteria to be NLog loggers
        /// </summary>
        /// <remarks>We assume each assembly will only ever be processed once, so we make assumptions here</remarks>
        /// <param name="assembly"></param>
        public static void PatchLoggerSetup(Assembly assembly)
        {
            Type[] allTypes = AccessTools.GetTypesFromAssembly(assembly);
            // Console.WriteLine($"[LogManager] Checking assembly for loggers: \"{assembly.FullName}\"");
            foreach (Type type in allTypes)
            {
                // Console.WriteLine($"[LogManager]  Found type: {type.FullName}");
                if (type.Name == "Logger")
                {
                    // I'm trying to setup a log manager that will auto-create NLog loggers based on any "Logger" classes I find in an arbitrary target assembly that meet a set of criteria I set
                    Console.WriteLine($"[LogManager] Found logger in assembly {assembly}, testing");
                    FieldInfo logger = AccessTools.Field(type, "logger");
                    MethodInfo setup = AccessTools.Method(type, "Setup");
                    MethodInfo log = AccessTools.Method(type, "Log", parameters: new Type[] { typeof(byte), typeof(string) });
                    MethodInfo logException = AccessTools.Method(type, "LogException", parameters: new Type[] { typeof(byte), typeof(Exception) });
                    MethodInfo logExceptionParams = AccessTools.Method(type, "LogException", parameters: new Type[] { typeof(byte), typeof(Exception), typeof(string) });

                    if (logger != null && setup != null && log != null)
                    {
                        Console.WriteLine($"[LogManager] Logger {type.FullName} validated, attempting patch");
                        // Setup patch
                        harmony.Patch(setup, prefix: new HarmonyMethod(AccessTools.Method(typeof(Patches.SupportedLoggerPatch), nameof(Patches.SupportedLoggerPatch.SetupPrefix))));
                        Console.WriteLine("[LogManager]  Patched logger setup");

                        // Actual logging patches
                        harmony.Patch(log, prefix: new HarmonyMethod(AccessTools.Method(typeof(Patches.SupportedLoggerPatch), nameof(Patches.SupportedLoggerPatch.LogPrefix))));
                        Console.WriteLine("[LogManager]  Patched logger logging");
                        if (logException != null)
                        {
                            harmony.Patch(logException, prefix: new HarmonyMethod(AccessTools.Method(typeof(Patches.SupportedLoggerPatch), nameof(Patches.SupportedLoggerPatch.LogExceptionPrefix))));
                            Console.WriteLine("[LogManager]  Patched logger exception logging");
                        }
                        if (logExceptionParams != null)
                        {
                            harmony.Patch(logExceptionParams, prefix: new HarmonyMethod(AccessTools.Method(typeof(Patches.SupportedLoggerPatch), nameof(Patches.SupportedLoggerPatch.LogExceptionParamsPrefix))));
                            Console.WriteLine("[LogManager]  Patched logger detailed exception logging");
                        }
                    }
                    else
                    {
                        Console.WriteLine("[LogManager]  Logger invalid");
                        if (logger == null)
                        {
                            Console.WriteLine("[LogManager]    Missing logger slot!");

                        }
                        if (setup == null)
                        {
                            Console.WriteLine("[LogManager]    Missing Setup hook");

                        }
                        if (log == null)
                        {
                            Console.WriteLine("[LogManager]    Missing Log func");

                        }
                    }
                }
            }
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
