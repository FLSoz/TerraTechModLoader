using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using ModManager.Datastructures;
using HarmonyLib;


namespace ModManager
{
    internal static class ModuleInitializer
    {
        private static bool Inited = false;

        internal static void Run()
        {
            if (!Inited)
            {
                /* foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                    Console.WriteLine($"ASSEMBLY CURRENTLY LOADED: {assembly.FullName}");
                } */

                // Assembly currentAssembly = Assembly.GetExecutingAssembly();
                // Configure all loggers
                Harmony.DEBUG = true;
                ModManager.ConfigureLogger();
                QMod.ConfigureLogger();
                DependencyGraph<QMod>.ConfigureLogger();
                DependencyGraph<Type>.ConfigureLogger();

                // TODO: Add assembly initialization logic.
                ModManager.PatchAssemblyLoading();
                ModManager.Patch();
                ModManager.logger.Info("Assembly Initialization Complete");
                ModManager.ProcessUnofficialMods();
                ModManager.logger.Info("Unofficial Mods Processed");
                Inited = true;
            }
        }
    }
}
