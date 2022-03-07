using NLog;
using NLog.Targets;
using System;

namespace LogManager
{
    public static class ModuleInitializer
    {
        public const string VERSION = "1.0.0.1";    // <major version>.<minor version>.<build number>.<revision>
        private static bool Inited = false;
        public static void Run()
        {
            Console.WriteLine($"[LogManager] Initializing logging manager version v.{VERSION}...");

            if (!Inited)
            {
                TTLogManager.config = new NLog.Config.LoggingConfiguration();

                // Rules for mapping loggers to targets
                // Manager.config.AddRule(LogLevel.Error, LogLevel.Fatal, Manager.logconsole);

                // Apply config           
                NLog.LogManager.Configuration = TTLogManager.config;

                // Setup
                NLog.LogManager.Setup().SetupExtensions(s =>
                {
                    s.AutoLoadAssemblies(false);
                });
                NLog.LogManager.Setup().SetupInternalLogger(s =>
                    s.SetMinimumLogLevel(LogLevel.Trace).LogToFile("NLogInternal.txt")
                );

                // read configuration
                bool defaultLogging = false;
                string[] commandLineArgs = CommandLineReader.GetCommandLineArgs();
                for (int i = 0; i < commandLineArgs.Length; i++)
                {
                    string arg = commandLineArgs[i];
                    if (arg.StartsWith("+log_level") && i < commandLineArgs.Length - 1)
                    {
                        string argValue = commandLineArgs[i + 1];
                        if (arg.Length == 10) // if it is actually exactly +log_level
                        {
                            TTLogManager.ConfiguredGlobalLogLevel = LogLevel.FromString(argValue);
                        }
                        else if (arg[10] == '_')
                        {
                            string loggerName = arg.Substring(11);
                            LogLevel logLevel = LogLevel.FromString(argValue);
                            TTLogManager.ConfiguredLogLevels.Add(loggerName, logLevel);
                            Console.WriteLine($"Detected logging config of {logLevel} for logger {loggerName}");
                        }
                    }
                    else if (arg == "+default_logging")
                    {
                        defaultLogging = true;
                    }
                    else if (arg == "+enable_vanilla_logs")
                    {
                        Patches.EnableVanillaLogs = true;
                    }
                }

                if (!defaultLogging)
                {
                    Patches.Init();
                }
                Inited = true;
            }
        }
    }
}