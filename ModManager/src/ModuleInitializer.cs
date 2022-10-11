using System;
using System.Reflection;
using ModManager.Datastructures;
using ModManager.patches;

namespace ModManager
{
    public static class ModuleInitializer
    {
        private static bool Inited = false;

        public static void Run()
        {
            if (!Inited)
            {
                Inited = true;
                Console.WriteLine($"[0ModManager] Initializing mod manager version v.{ModManager.Version} ...");

                // Configure all loggers
                QMod.ConfigureLogger();
                DependencyGraph<QMod>.ConfigureLogger();
                DependencyGraph<Type>.ConfigureLogger();

                ModManager.Setup();
                ModManager.PatchAssemblyLoading();
                // TODO: Add assembly initialization logic.
                ModManager.Patch();
                Console.WriteLine("Assembly Initialization Complete");

                // Allow debug settings to happen even in normal operation
                string[] commandLineArgs = CommandLineReader.GetCommandLineArgs();
                Console.WriteLine($"Running game with params: {String.Join(" ", commandLineArgs)}");
                for (int i = 0; i < commandLineArgs.Length; i++)
                {
                    if (i == 0)
                    {
                        ModManager.ExecutablePath = commandLineArgs[i];
                        ModManager.logger.Info($"Backup executable path: {ModManager.ExecutablePath}");
                    }

                    if (commandLineArgs[i] == "+manage_ttmm")
                    {
                        ModManager.logger.Info($"Enabling TTQMM handling");
                        ModManager.EnableTTQMMHandling = true;
                        ModManager.ProcessUnofficialMods();
                    }
                    else if (commandLineArgs[i] == "+harmony_debug")
                    {
                        ModManager.SetHarmonyDebug();
                    }
                    else if (commandLineArgs[i] == "+custom_mod_list")
                    {
                        ModManager.StartedGameWithParameters = true;
                    }
                    else if (commandLineArgs[i] == "+keep_0mm_logs")
                    {
                        ModManager.KEEP_OLD_LOGS = true;
                    }
                }

                // Patch snapshot load failure logging
                SerializationLoggingPatches.Setup();

                Console.WriteLine($"0MM version v.{ModManager.Version} initialized");
            }
        }
    }
}