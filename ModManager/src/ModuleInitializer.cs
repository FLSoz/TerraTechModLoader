using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using ModManager.Datastructures;

public static class ModuleInitializer
{
    private static bool Inited = false;

    public static void Run()
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

            // Allow debug settings to happen even in normal operation
            string[] commandLineArgs = CommandLineReader.GetCommandLineArgs();
            ModManager.ModManager.logger.Info($"Running game with params: {String.Join(" ", commandLineArgs)}");
            for (int i = 0; i < commandLineArgs.Length; i++)
            {
                if (i == 0)
                {
                    ModManager.ModManager.ExecutablePath = commandLineArgs[i];
                    ModManager.ModManager.logger.Info($"Backup executable path: {ModManager.ModManager.ExecutablePath}");
                }

                if (commandLineArgs[i] == "+manage_ttmm")
                {
                    ModManager.ModManager.EnableTTQMMHandling = true;
                    ModManager.ModManager.ProcessUnofficialMods();
                }
                else if (commandLineArgs[i] == "+harmony_debug")
                {
                    ModManager.ModManager.SetHarmonyDebug();
                }
                else if (commandLineArgs[i] == "+custom_mod_list")
                {
                    ModManager.ModManager.RequestConfiguredModSession();
                }
            }
            Inited = true;
        }
    }
}
