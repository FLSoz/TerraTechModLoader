﻿using System;
using System.Linq;
using System.Text;
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
        public TTLogManager()
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
            Layout = "[${logger:shortName=true}] ${level:uppercase=true:padding=-5:alignmentOnTruncation=left} ${time} | ${message}  ${exception}"
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

        internal static char[] InvalidFileChars = Path.GetInvalidFileNameChars();
        internal static char[] InvalidPathChars = Path.GetInvalidPathChars();
        internal static char[] InvalidFilePathChars = new HashSet<char>(InvalidFileChars).Concat(InvalidPathChars).ToArray();
        private static string SanitizePath(string path)
        {
            if (path == null)
            {
                return null;
            }
            StringBuilder sb = new StringBuilder();
            foreach (char letter in path)
            {
                if (!InvalidFilePathChars.Contains(letter) || letter == Path.PathSeparator)
                {
                    sb.Append(letter);
                }
                else
                {
                    sb.Append('_');
                }
            }
            return sb.ToString();
        }
        private static string SanitizeFileName(string filename)
        {
            if (filename == null)
            {
                return null;
            }
            StringBuilder sb = new StringBuilder();
            foreach (char letter in filename)
            {
                if (!InvalidFileChars.Contains(letter))
                {
                    sb.Append(letter);
                }
                else
                {
                    sb.Append('_');
                }
            }
            return sb.ToString();
        }

        public static LogTarget RegisterLoggingTarget(TargetConfig targetConfig)
        {
            // Preliminary cache check
            string targetPath = Path.ChangeExtension(GetFilePath(targetConfig.path, targetConfig.filename), ".log").Trim(Path.DirectorySeparatorChar);
            if (!TargetPathDictionary.TryGetValue(targetPath, out LogTarget target))
            {
                string shortTargetName = SanitizeFileName(targetConfig.filename);
                InfoPrint($"[LogManager] Registering logger {shortTargetName}");

                // Calculate full path
                string fullPath = SanitizePath(targetConfig.path);
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
                    // Manually handle deletion ourselves, or the ModManager log will constantly get reset
                    if (!targetConfig.keepOldFiles)
                    {
                        if (File.Exists(fullPath))
                        {
                            File.Delete(fullPath);
                        }
                    }

                    target = new LogTarget
                    {
                        logFile = new FileTarget($"logfile-{targetConfig.path}")
                        {
                            FileName = fullPath,
                            Layout = targetConfig.layout is null || targetConfig.layout.Length == 0 ?
                        "${longdate} ${level:uppercase=true:padding=-5:alignmentOnTruncation=left} ${logger:shortName=true} | ${message}  ${exception}" :
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
                    ErrorPrint($"[LogManager] Already registered logger with path {targetPath}");
                }
            }
            else
            {
                ErrorPrint($"[LogManager] Already registered logger with path {targetPath}");
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
            targetConfig.filename = SanitizeFileName(targetConfig.filename);
            targetConfig.path = SanitizePath(targetConfig.path);
            return RegisterLoggingTarget(targetConfig);
        }

        public static void RegisterLogger(Logger logger, LogTarget target, LogLevel defaultMinLevel = null)
        {
            LogLevel minLevel = defaultMinLevel;
            if (minLevel == null)
            {
                minLevel = LogLevel.Error;
                if (ConfiguredLogLevels.TryGetValue(logger.Name, out LogLevel configuredLevel) && configuredLevel != null)
                {
                    DebugPrint($"[LogManager]  Registering logger {logger.Name} with logging level {configuredLevel} at {target.config.path}");
                    minLevel = configuredLevel;
                }
                else
                {
                    string shortLoggerName = logger.Name.Substring(logger.Name.LastIndexOf('.') + 1);
                    if (ConfiguredLogLevels.TryGetValue(shortLoggerName, out configuredLevel) && configuredLevel != null)
                    {
                        DebugPrint($"[LogManager]  Registering logger {shortLoggerName} with logging level {configuredLevel} at {target.config.path}");
                        minLevel = configuredLevel;
                    }
                    else if (ConfiguredGlobalLogLevel != null)
                    {
                        DebugPrint($"[LogManager]  Registering logger {shortLoggerName} with GLOBAL DEFAULT logging level {ConfiguredGlobalLogLevel} at {target.config.path}");
                        minLevel = ConfiguredGlobalLogLevel;
                    }
                    else
                    {
                        DebugPrint($"[LogManager]  Registering logger {shortLoggerName} with default logging level {minLevel} at {target.config.path}");
                    }
                }
            }
            else
            {
                DebugPrint($"[LogManager]  Registering logger {logger.Name} with specified logging level {minLevel} at {target.config.path}");
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

        internal static void DebugPrint(string message)
        {
            if (ConfiguredGlobalLogLevel != null && ConfiguredGlobalLogLevel <= LogLevel.Debug)
            {
                Console.WriteLine(message);
            }
        }

        internal static void InfoPrint(string message)
        {
            if (ConfiguredGlobalLogLevel != null && ConfiguredGlobalLogLevel <= LogLevel.Info)
            {
                Console.WriteLine(message);
            }
        }
        internal static void ErrorPrint(string message)
        {
            if (ConfiguredGlobalLogLevel != null && ConfiguredGlobalLogLevel <= LogLevel.Fatal)
            {
                Console.WriteLine(message);
            }
        }

        /// <summary>
        /// We patch any loggers in here that match our criteria to be NLog loggers
        /// </summary>
        /// <remarks>We assume each assembly will only ever be processed once, so we make assumptions here</remarks>
        /// <param name="assembly"></param>
        public static void PatchLoggerSetup(Assembly assembly)
        {
            Type[] allTypes = AccessTools.GetTypesFromAssembly(assembly);
            // DebugPrint($"[LogManager] Checking assembly for loggers: \"{assembly.FullName}\"");
            foreach (Type type in allTypes)
            {
                // DebugPrint($"[LogManager]  Found type: {type.FullName}");
                if (type.Name == "Logger")
                {
                    // I'm trying to setup a log manager that will auto-create NLog loggers based on any "Logger" classes I find in an arbitrary target assembly that meet a set of criteria I set
                    InfoPrint($"[LogManager] Found logger in assembly {assembly}, testing");
                    FieldInfo logger = AccessTools.Field(type, "logger");
                    MethodInfo setup = AccessTools.Method(type, "Setup");
                    MethodInfo log = AccessTools.Method(type, "Log", parameters: new Type[] { typeof(byte), typeof(string) });
                    MethodInfo logException = AccessTools.Method(type, "LogException", parameters: new Type[] { typeof(byte), typeof(Exception) });
                    MethodInfo logExceptionParams = AccessTools.Method(type, "LogException", parameters: new Type[] { typeof(byte), typeof(Exception), typeof(string) });

                    if (logger != null && setup != null && log != null)
                    {
                        InfoPrint($"[LogManager] Logger {type.FullName} validated, attempting patch");
                        // Setup patch
                        harmony.Patch(
                            setup,
                            prefix: new HarmonyMethod(AccessTools.Method(typeof(Patches.SupportedLoggerPatch), nameof(Patches.SupportedLoggerPatch.SetupPrefix))),
                            finalizer: new HarmonyMethod(AccessTools.Method(typeof(Patches.SupportedLoggerPatch), nameof(Patches.SupportedLoggerPatch.Finalizer)))
                        );
                        DebugPrint("[LogManager]  Patched logger setup");

                        // Actual logging patches
                        harmony.Patch(log, prefix: new HarmonyMethod(AccessTools.Method(typeof(Patches.SupportedLoggerPatch), nameof(Patches.SupportedLoggerPatch.LogPrefix))));
                        DebugPrint("[LogManager]  Patched logger logging");
                        if (logException != null)
                        {
                            harmony.Patch(logException, prefix: new HarmonyMethod(AccessTools.Method(typeof(Patches.SupportedLoggerPatch), nameof(Patches.SupportedLoggerPatch.LogExceptionPrefix))));
                            DebugPrint("[LogManager]  Patched logger exception logging");
                        }
                        if (logExceptionParams != null)
                        {
                            harmony.Patch(logExceptionParams, prefix: new HarmonyMethod(AccessTools.Method(typeof(Patches.SupportedLoggerPatch), nameof(Patches.SupportedLoggerPatch.LogExceptionParamsPrefix))));
                            DebugPrint("[LogManager]  Patched logger detailed exception logging");
                        }

                        MethodInfo flush = AccessTools.Method(type, "Flush", parameters: null);
                        if (flush != null)
                        {
                            harmony.Patch(flush, prefix: new HarmonyMethod(AccessTools.Method(typeof(Patches.SupportedLoggerPatch), nameof(Patches.SupportedLoggerPatch.FlushPrefix))));
                            DebugPrint("[LogManager]  Patched logger flush");
                        }

                        ErrorPrint("[LogManager]  Patch Successful");
                    }
                    else
                    {
                        ErrorPrint("[LogManager]  Logger invalid");
                        if (logger == null)
                        {
                            ErrorPrint("[LogManager]    Missing logger slot!");
                        }
                        if (setup == null)
                        {
                            ErrorPrint("[LogManager]    Missing Setup hook");
                        }
                        if (log == null)
                        {
                            ErrorPrint("[LogManager]    Missing Log func");
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
