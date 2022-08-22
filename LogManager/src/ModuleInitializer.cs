using NLog;
using NLog.Targets;
using System;

namespace LogManager
{
    public static class ModuleInitializer
    {
        public const string VERSION = "1.1.0.0";    // <major version>.<minor version>.<build number>.<revision>
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
                            try
                            {
                                LogLevel logLevel = LogLevel.FromString(argValue);
                                TTLogManager.ConfiguredLogLevels[loggerName] = logLevel;
                                Console.WriteLine($"Detected logging config of {logLevel} for logger {loggerName}");
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine($"FAILED to get log level for logger {loggerName}, {argValue} is invalid log level");
                            }
                        }
                    }
                    else if (arg == "+default_logging")
                    {
                        defaultLogging = true;
                    }
                    else if (arg == "+enable_vanilla_logs")
                    {
                        TTLogManager.EnableVanillaLogs = true;
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