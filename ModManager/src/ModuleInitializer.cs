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
        if (!Inited)
        {
            Console.WriteLine("[0ModManager] Initializing manager...");

            // Configure all loggers
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
                    else if (commandLineArgs[i] == "+harmony_debug")
                    {
                        Harmony.DEBUG = true;
                    }
                }
            }

            Inited = true;
        }
    }
}
