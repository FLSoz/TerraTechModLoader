using NLog;


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
                NLog.Targets.ConsoleTarget logconsole = new NLog.Targets.ConsoleTarget("logconsole")
                {
                    Layout = "[${logger:shortName=true}] ${level:uppercase=true:padding=-5:alignmentOnTruncation=left} | ${time} | ${message}  ${exception}"
                };

                // Rules for mapping loggers to targets            
                Manager.config.AddRule(LogLevel.Info, LogLevel.Fatal, logconsole);

                // Apply config           
                NLog.LogManager.Configuration = Manager.config;

                // Setup
                NLog.LogManager.Setup().SetupExtensions(s =>
                {
                    s.AutoLoadAssemblies(false);
                });
                NLog.LogManager.Setup().SetupInternalLogger(s =>
                   s.SetMinimumLogLevel(LogLevel.Warn).LogToFile("NLogInternal.txt")
                );
                Inited = true;
            }
        }
    }
}
