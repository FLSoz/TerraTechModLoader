using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using ModManager.Datastructures;

namespace ModManager
{
    public static class ModuleInitializer
    {
        public const string VERSION = "1.0.0.2";    // <major version>.<minor version>.<build number>.<revision>
        private static bool Inited = false;

        public static void Run()
        {
            if (!Inited)
            {
                Console.WriteLine($"[0ModManager] Initializing mod manager v.{VERSION}...");

                // Configure all loggers
                ModManager.ConfigureLogger();
                QMod.ConfigureLogger();
                DependencyGraph<QMod>.ConfigureLogger();
                DependencyGraph<Type>.ConfigureLogger();

                ModManager.PatchAssemblyLoading();
                // TODO: Add assembly initialization logic.
                ModManager.Patch();
                ModManager.logger.Info("Assembly Initialization Complete");

                // Allow debug settings to happen even in normal operation
                string[] commandLineArgs = CommandLineReader.GetCommandLineArgs();
                ModManager.logger.Info($"Running game with params: {String.Join(" ", commandLineArgs)}");
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
                        ModManager.RequestConfiguredModSession();
                    }
                }
                Inited = true;
            }
        }
    }
}