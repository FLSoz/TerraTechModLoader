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
            /* foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                Console.WriteLine($"ASSEMBLY CURRENTLY LOADED: {assembly.FullName}");
            } */

            // Assembly currentAssembly = Assembly.GetExecutingAssembly();
            // Configure all loggers
            Harmony.DEBUG = true;
            ModManager.ModManager.ConfigureLogger();
            ModManager.QMod.ConfigureLogger();
            DependencyGraph<ModManager.QMod>.ConfigureLogger();
            DependencyGraph<Type>.ConfigureLogger();

            // TODO: Add assembly initialization logic.
            ModManager.ModManager.PatchAssemblyLoading();
            ModManager.ModManager.Patch();
            ModManager.ModManager.logger.Info("Assembly Initialization Complete");
            // ModManager.ProcessUnofficialMods();
            // ModManager.logger.Info("Unofficial Mods Processed");

            ModManager.ModManager.RequestConfiguredModSession();

            Inited = true;
        }
    }
}
