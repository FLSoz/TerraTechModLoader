using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
public class ModManagerSetup : ModBase
{
    static ModManagerSetup()
    {
        Run();
    }

    public static string modManagerDir = Path.GetDirectoryName(
        AppDomain.CurrentDomain.GetAssemblies()
        .Where(assembly => assembly.GetName().Name == "0AssemblyLoader").First().Location
    );
    public static string HarmonyPath = Path.Combine(modManagerDir, "0Harmony.dll");
    public static string NLogPath = Path.Combine(modManagerDir, "NLog.dll");
    public static string NLogManagerPath = Path.Combine(modManagerDir, "NLogManager.dll");
    public static string ModManagerPath = Path.Combine(modManagerDir, "TTModManager.dll");

    public static Assembly Harmony;
    public static Assembly NLog;
    public static Assembly NLogManager;
    public static Assembly ModManager;

    public static void Run()
    {
        Console.WriteLine("[0AssemblyLoader] Initializing assemblies...");
        PatchAssemblyLoading();
        Console.WriteLine($"[0AssemblyLoader] Force-loading bundled 0Harmony.dll at path {HarmonyPath}");
        Harmony = AppDomain.CurrentDomain.Load("0Harmony.dll");
        Console.WriteLine($"[0AssemblyLoader] Force-loading bundled NLog.dll at path {NLogPath}");
        NLog = AppDomain.CurrentDomain.Load("NLog.dll");
        Console.WriteLine($"[0AssemblyLoader] Force-loading bundled NLogManager.dll at path {NLogManagerPath}");
        NLogManager = AppDomain.CurrentDomain.Load("NLogManager.dll");
        Console.WriteLine($"[0AssemblyLoader] Force-loading bundled ModManager.dll at path {ModManagerPath}");
        ModManager = AppDomain.CurrentDomain.Load("TTModManager.dll");

        Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

        try
        {
            Console.WriteLine("[0AssemblyLoader] Force getting Harmony types");
            Harmony.GetExportedTypes();
            Console.WriteLine("[0AssemblyLoader] Force getting NLog types");
            NLog.GetExportedTypes();
            Console.WriteLine("[0AssemblyLoader] Force getting NLogManager types");
            NLogManager.GetTypes();
            Console.WriteLine("[0AssemblyLoader] Force getting ModManager types");
            ModManager.GetTypes();
        }
        catch (System.Reflection.ReflectionTypeLoadException ex)
        {
            Console.WriteLine("[0AssemblyLoader] FAILED to load types!");
            Exception[] exceptions = ex.LoaderExceptions;
            foreach (Exception exception in exceptions)
            {
                Console.WriteLine(exception);
            }
        }
        // Console.WriteLine("Loaded assemblies:\n" + String.Join("\n", assemblies.Select(x => x.FullName)));
        // UnpatchAssemblyLoading();
    }

    internal static readonly FieldInfo m_CurrentSession = typeof(ManMods).GetField("m_CurrentSession", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
    internal static readonly FieldInfo m_Mods = typeof(ManMods).GetField("m_Mods", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

    public static Assembly ResolveAssembly(object sender, ResolveEventArgs args)
    {
        DirectoryInfo parentDirectory = new DirectoryInfo(modManagerDir);
        FileInfo[] dlls = parentDirectory.GetFiles("*.dll", SearchOption.AllDirectories);
        foreach (FileInfo dll in dlls)
        {
            if (args.Name.Contains(Path.GetFileNameWithoutExtension(dll.Name)))
            {
                return Assembly.LoadFile(dll.FullName);
            }
        }
        return null;
    }

    public static void PatchAssemblyLoading()
    {
        /* AppDomain.CurrentDomain.AssemblyLoad += delegate (object sender, AssemblyLoadEventArgs args)
        {
            Assembly assembly = args.LoadedAssembly;
            Console.WriteLine("[0AssemblyLoader] Loaded Assembly " + assembly.FullName);
        }; */
        AppDomain.CurrentDomain.AssemblyResolve += ResolveAssembly;
        // Console.WriteLine("[0AssemblyLoader] Patched assembly loading?");
    }
    public static void UnpatchAssemblyLoading()
    {
        AppDomain.CurrentDomain.AssemblyResolve -= ResolveAssembly;
    }

    public override void Init()
    {
        return;
    }

    public override void DeInit()
    {
        return;
    }
}
