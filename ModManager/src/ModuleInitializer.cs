using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using ModManager.Datastructures;
using HarmonyLib;

internal static class ModuleInitializer
{
    private static bool Inited = false;

    internal static void Run()
    {
        Console.WriteLine("HELLO WORLD");
        if (!Inited)
        {
            // Assembly currentAssembly = Assembly.GetExecutingAssembly();
            // Configure all loggers
            // Harmony.DEBUG = true;
            ModManager.ModManager.ConfigureLogger();
            ModManager.QMod.ConfigureLogger();
            DependencyGraph<ModManager.QMod>.ConfigureLogger();
            DependencyGraph<Type>.ConfigureLogger();

            ModManager.ModManager.PatchAssemblyLoading();
            // TODO: Add assembly initialization logic.
            ModManager.ModManager.Patch();
            ModManager.ModManager.logger.Info("Assembly Initialization Complete");

            // Only handle session requests if loaded via TTSMM. Otherwise, proceed as normal
            if (CommandLineReader.GetArgument("+custom_mod_list") != null)
            {
                ModManager.ModManager.RequestConfiguredModSession();
            }
            else
            {
                string[] commandLineArgs = CommandLineReader.GetCommandLineArgs();
                for (int i = 0; i < commandLineArgs.Length; i++)
                {
                    if (commandLineArgs[i] == "+manage_ttmm")
                    {
                        ModManager.ModManager.ProcessUnofficialMods();
                    }
                }
            }

            Inited = true;
        }
    }
}
