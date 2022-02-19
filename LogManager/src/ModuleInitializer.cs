using NLog;
using NLog.Targets;

namespace LogManager
{
    internal static class ModuleInitializer
    {
        private static bool Inited;
        internal static void Run()
        {
            if (!Inited)
            {
                Manager.config = new NLog.Config.LoggingConfiguration();

                // Rules for mapping loggers to targets
                // Manager.config.AddRule(LogLevel.Error, LogLevel.Fatal, Manager.logconsole);

                // Apply config           
                NLog.LogManager.Configuration = Manager.config;

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
                            Manager.ConfiguredGlobalLogLevel = LogLevel.FromString(argValue);
                        }
                        else if (arg[10] == '_')
                        {
                            string loggerName = arg.Substring(11);
                            Manager.ConfiguredLogLevels.Add(loggerName, LogLevel.FromString(argValue));
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
