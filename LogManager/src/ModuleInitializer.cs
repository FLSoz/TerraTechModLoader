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

                // Rule for default console
                ConsoleTarget logconsole = new ConsoleTarget("logconsole")
                {
                    Layout = "[${logger:shortName=true}] ${level:uppercase=true:padding=-5:alignmentOnTruncation=left} | ${time} | ${message}  ${exception}"
                };

                // Rules for mapping loggers to targets
                // Manager.config.AddRule(LogLevel.Error, LogLevel.Fatal, logconsole);

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
                        else
                        {
                            string loggerName = arg.Substring(10);
                            Manager.ConfiguredLogLevels.Add(loggerName, LogLevel.FromString(argValue));
                        }
                    }
                }

                Inited = true;
            }
        }
    }
}
