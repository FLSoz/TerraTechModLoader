using System;
using System.Diagnostics;
using System.IO;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Linq;
using HarmonyLib;
using NLog;
using Steamworks;
using ModManager.Datastructures;
using LogManager;
using Payload.UI.Commands.Steam;
using Newtonsoft.Json.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace ModManager
{
    // We patch on the EarlyInit hook, after some mods are loaded, before the Init hook
    // As such, some mods may have already loaded
    
    // We assume that EarlyInit happens on the same frame as Init, just earlier in the process

    // Since we assume that any mod that requires dependency management will have this mod as a dependency,
    // we can assume none of them have loaded (the dlls will fail to load properly) until this mod has done its' EarlyInit.
    // Any mod that is able to load before then is outside of the remit of this mod.

    // We handle the case that mods have load order preferences
    //  - Some may not be handled properly
    // We handle the case that mods do not load properly because they are missing dependencies
    //  - these will always be loaded properly in the end
    public class ModManager : ModBase
    {
        internal static readonly string Version = Assembly.GetExecutingAssembly().GetName().Version.ToString();
        internal static ModdedContentLoader contentLoader = new ModdedContentLoader();
        internal static Texture2D _DefaultIcon;

        // Runs during ManMods.ProcessLoadingMod, where it does the Activator.CreateInstance(type) as ModBase
        static ModManager()
        {
            ModuleInitializer.Run();
        }
        public ModManager()
        {
            ModuleInitializer.Run();
            ModManager.ConfigureLogger();
            if (StartedGameWithParameters)
            {
                ModManager.RequestConfiguredModSession();
            }
        }

        internal static bool EnableTTQMMHandling = false;
        private static bool patchedAssemblyLoading = false;
        private static bool patched = false;
        internal static bool LoadedWithProperParameters = false;
        internal static bool StartedGameWithParameters = false;
        internal const string HarmonyID = "com.flsoz.ttmodding.modmanager";
        internal static PublishedFileId_t WorkshopID = new PublishedFileId_t(2790161231);
        internal static string ExecutablePath;

        internal static readonly string TTSteamDir = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => assembly.GetName().Name == "Assembly-CSharp").First().Location
            .Replace("Assembly-CSharp.dll", ""), @"../../"
        ));
        
        // internal static readonly string TTSteamDir = @"E:/Steam/steamapps/common/TerraTech";
        private static readonly string QModsDir = Path.Combine(TTSteamDir, "QMods");
        private static readonly string WorkshopDir = Path.Combine(TTSteamDir, @"../../workshop/content/285920/");
        private static readonly string ModListFileName = Path.Combine(TTSteamDir, "modlist.txt");
        internal const int DEFAULT_LOAD_ORDER = 10;

        internal const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        internal const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        internal static bool KEEP_OLD_LOGS = false;

        internal static NLog.Logger logger = NLog.LogManager.GetLogger("ModManager");
        private static NLog.Logger assemblyLogger = NLog.LogManager.GetLogger("AssemblyLoader");
        public static void ConfigureLogger()
        {
            TargetConfig targetConfig = new TargetConfig
            {
                layout = "${longdate} ${level:uppercase=true:padding=-5:alignmentOnTruncation=left} ${logger:shortName=true} | ${message}  ${exception}",
                filename = "ModManager",
                keepOldFiles = KEEP_OLD_LOGS
            };
            LogTarget target = TTLogManager.RegisterLoggingTarget(targetConfig);

            TTLogManager.RegisterLogger(logger, target);
            TTLogManager.RegisterLogger(assemblyLogger, target);
            TTLogManager.RegisterLogger(ModdedContentLoader.logger, target);
        }

        internal static Harmony harmony = new Harmony(HarmonyID);

        private static List<string> LoadedMods;
        
        internal static List<WrappedMod> EarlyInitQueue = new List<WrappedMod>();
        internal static List<WrappedMod> InitQueue = new List<WrappedMod>();
        private static List<WrappedMod> UpdateQueue = new List<WrappedMod>();
        private static List<WrappedMod> FixedUpdateQueue = new List<WrappedMod>();

        internal static Dictionary<ulong, SteamDownloadItemData> workshopMetadata = new Dictionary<ulong, SteamDownloadItemData>();
        internal static Dictionary<Assembly, ModContainer> assemblyMetadata = new Dictionary<Assembly, ModContainer>();

        internal static Dictionary<Type, WrappedMod> managedMods = new Dictionary<Type, WrappedMod>();
        internal static Dictionary<WrappedMod, ModContainer> modMetadata = new Dictionary<WrappedMod, ModContainer>();
        internal static HashSet<ModContainer> containersWithEarlyHooks = new HashSet<ModContainer>();

        internal static Dictionary<string, QMod> unofficialMods = new Dictionary<string, QMod>();
        internal static QMod BlockInjector = null;
        internal static WrappedMod LegacyBlockLoader = null;

        internal static ModContainer ModManagerContainer;

        internal static string CurrentOperation = null;
        internal static string CurrentOperationSpecifics = null;
        internal static float CurrentOperationProgress = 0.0f;
        internal static float CurrentOperationSpecificProgress = 0.0f;
        internal static Texture2D CurrentOperationIcon = null;

        internal struct OperationDetails
        {
            internal string Name;
            internal string Specifics;
            internal float Progress;
            internal Texture2D Icon;

            internal OperationDetails(string Name, string Specifics, float Progress, Texture2D Icon)
            {
                this.Name = Name;
                this.Specifics = Specifics;
                this.Progress = Progress;
                this.Icon = Icon;
            }
        }

        internal static OperationDetails GetCurrentOperation()
        {
            return new OperationDetails(CurrentOperation, CurrentOperationSpecifics, CurrentOperationProgress, CurrentOperationIcon != null ? CurrentOperationIcon : _DefaultIcon);
        }

        internal static bool CurrentSessionLoaded = false;

        public override void DeInit()
        {
            logger.Info("DeInit fired");
            // We never unpatch. We assume that anybody using this mod knows what they're doing
            // this mod also is non-destructive, so its presence does not change vanilla behavior

            for (int i = InitQueue.Count - 1; i >= 0; i--)
            {
                WrappedMod script = InitQueue[i];
                logger.Trace("Processing DeInit for mod {}", script.Name);
                IEnumerator<float> iterator = script.DeInit();
                while (iterator.MoveNext())
                {
                    CurrentOperationSpecificProgress = iterator.Current;
                }
            }

            // Why did I add this? There is no benefit from attempting to reload any .dlls.
            // If they failed loading once, they will fail loading again because TT's version of mono caches .dlls,
            // which is why this mod is needed in the first place
            // assemblyMetadata.Clear();
        }

        internal static void Patch()
        {
            if (!patched)
            {
                harmony.PatchAll();
                patched = true;
                logger.Info("🩹 Patch applied");
            }
        }

        public override void Init()
        {
            logger.Info("Init fired");
            // Problems with unofficial mods:
            //   undoing patching would not undo changes made to blocks table
            // UnofficialModsInterface.LoadUnofficialMods();
        }

        public override bool HasEarlyInit()
        {
            return true;
        }

        internal static void Setup()
        {
            Dictionary<string, ModContainer> mods = (Dictionary<string, ModContainer>) ReflectedManMods.m_Mods.GetValue(Singleton.Manager<ManMods>.inst);
            if (mods.TryGetValue("0ModManager", out ModContainer thisContainer))
            {
                ModManagerContainer = thisContainer;
            }
            else if (mods.TryGetValue("0ModManager BETA", out thisContainer))
            {
                ModManagerContainer = thisContainer;
            }
            else
            {
                logger.Error("❌ FAILED TO FETCH 0ModManager ModContainer");
            }
        }

        public override void EarlyInit()
        {
            logger.Info("ModManager EarlyInit fired");
            Patch();
            if (!LoadedWithProperParameters)
            {
                Dictionary<string, ModContainer> mods = (Dictionary<string, ModContainer>)ReflectedManMods.m_Mods.GetValue(Singleton.Manager<ManMods>.inst);
                if (mods.Count >= Constants.kNumUGCResultsPerPage)
                {
                    WorkshopLoader.SubscribedModsPatch.CheckForMoreSteamMods(2u);
                }
            }
        }

        internal static void SetHarmonyDebug()
        {
            Harmony.DEBUG = true;
        }

        internal static void RequestConfiguredModSession()
        {
            LoadedWithProperParameters = true;
            string argument = CommandLineReader.GetArgument("+ttsmm_mod_list");
            if (argument != null)
            {
                logger.Info($"Found custom TTSMM mod list: {argument}");
                string[] mods = argument.Split(new char[] { ',' });
                if (argument.Length > 0 && mods.Length > 0)
                {
                    foreach (string mod in mods)
                    {
                        logger.Info($"▶ Processing mod {mod}");
                        string trimmedMod = mod.Trim(new char[] { '[', ']', ' ' });
                        string[] modDescrip = trimmedMod.Split(new char[] { ':' }, 2);
                        if (modDescrip.Length == 2)
                        {
                            string type = modDescrip[0];
                            string modName = modDescrip[1];
                            logger.Debug($"  💿 Mod ({modName}) determined to be of type {type}");
                            switch (type)
                            {
                                case "local":
                                    LocalLoader.LoadLocalMod(modName);
                                    break;
                                case "workshop":
                                    if (ulong.TryParse(modName, out ulong workshopID))
                                    {
                                        PublishedFileId_t steamWorkshopID = new PublishedFileId_t(workshopID);
                                        if (steamWorkshopID != WorkshopID)
                                        {
                                            ManMods manager = Singleton.Manager<ManMods>.inst;
                                            List<PublishedFileId_t> m_WaitingOnDownloads = (List<PublishedFileId_t>)ReflectedManMods.m_WaitingOnDownloads.GetValue(manager);
                                            m_WaitingOnDownloads.Add(steamWorkshopID);
                                            WorkshopLoader.LoadWorkshopMod(new SteamDownloadItemData
                                            {
                                                m_Details = new SteamUGCDetails_t
                                                {
                                                    m_nPublishedFileId = steamWorkshopID
                                                }
                                            },
                                            false);
                                            // We enforce that this is not remote
                                            logger.Debug("  ☁️ Loading workshop mod {WorkshopID}", steamWorkshopID);
                                        }
                                        else
                                        {
                                            logger.Info("  🛈 Requested self - ignoring");
                                        }
                                    }
                                    else
                                    {
                                        logger.Error("  ❌ Attempted to load workshop mod with malformed ID {WorkshopID}", modName);
                                    }
                                    break;
                                case "ttqmm":
                                    logger.Error("  ❌ Attempted to load mod {Mod} from TTMM. This is currently unsupported", modName);
                                    break;
                                default:
                                    logger.Error("  ❌ Found malformed mod request {Mod}", trimmedMod);
                                    break;
                            }
                        }
                    }
                    return;
                }
            }

            // We explicitly loaded only this mod. 
            logger.Info("🚨 No custom mod list found - getting default mod loading behaviour");
            ManMods manMods = Singleton.Manager<ManMods>.inst;
            typeof(ManMods).GetMethod("CheckForLocalMods", InstanceFlags).Invoke(manMods, null);
            typeof(ManMods).GetMethod("CheckForSteamWorkshopMods", InstanceFlags).Invoke(manMods, null);
        }

        internal static bool TryFindAssembly(ModContainer mod, string name, out Assembly assembly)
        {
            assembly = null;
            ModContents contents = mod.Contents;

            // This bit of code should never be run on a normal setup (Local mod)
            // This will null the script parts of any mods, if mod manager is loaded after them.
            // Should only happen in case of Multiplayer
            ModBase script = mod.Script;
            if (script != null && !(script is ModManager))
            {
                contents.Script = null;
            }

            DirectoryInfo parentDirectory = Directory.GetParent(mod.AssetBundlePath);
            FileInfo[] dlls = parentDirectory.GetFiles("*.dll", SearchOption.AllDirectories);

            foreach (FileInfo dll in dlls)
            {
                if (name.Contains(Path.GetFileNameWithoutExtension(dll.Name)))
                {
                    assemblyLogger.Trace("✔️ Found assembly {assembly} at path {path}", name, dll.FullName);
                    assembly = Assembly.LoadFrom(dll.FullName);
                    return true;
                }
            }

            return false;
        }

        public static void PatchAssemblyLoading()
        {
            if (!patchedAssemblyLoading)
            {
                AppDomain.CurrentDomain.AssemblyResolve += delegate (object sender, ResolveEventArgs args)
                {
                    string behalfString = args.RequestingAssembly != null ? $" as dependency of ({args.RequestingAssembly})" : "";
                    assemblyLogger.Trace($"🔍 Searching for assembly ({args.Name}){behalfString}");

                    ModSessionInfo session = (ModSessionInfo)ReflectedManMods.m_CurrentSession.GetValue(Singleton.Manager<ManMods>.inst);
                    if (session == null)
                    {
                        session = (ModSessionInfo)ReflectedManMods.m_CurrentSession.GetValue(Singleton.Manager<ManMods>.inst);
                    }
                    Dictionary<string, ModContainer> mods = (Dictionary<string, ModContainer>)ReflectedManMods.m_Mods.GetValue(Singleton.Manager<ManMods>.inst);

                    // Try to get .dll from mod that shares its name, if extant
                    if (mods.TryGetValue(args.Name, out ModContainer mod))
                    {
                        assemblyLogger.Trace($" 🔎 Found mod with name {args.Name}, searching for assembly");
                        if (TryFindAssembly(mod, args.Name, out Assembly assembly))
                        {
                            assemblyLogger.Trace($" ✔️ Assembly found");
                            return assembly;
                        }
                        else
                        {
                            assemblyLogger.Trace($" 🛈 Mod {args.Name}, did not contain assembly, searching entire session");
                        }
                    }

                    foreach (string key in session.Mods.Keys)
                    {
                        assemblyLogger.Trace($" 🔎 Session contains mod {key}, searching for assembly {args.Name}");
                        if (key != args.Name)
                        {
                            ModContainer modContainer;
                            if (mods.TryGetValue(key, out modContainer) && modContainer != null)
                            {
                                if (TryFindAssembly(modContainer, args.Name, out Assembly assembly))
                                {
                                    assemblyLogger.Trace($" ✔️ Assembly found");
                                    return assembly;
                                }
                            }
                            else
                            {
                                assemblyLogger.Trace(" ⏭ No mod container found, skipping");
                            }
                        }
                        else
                        {
                            assemblyLogger.Trace($" ⏭ Already checked mod {args.Name}, skipping");
                        }
                    }

                    // filter to enabled QMod DLLs
                    if (EnableTTQMMHandling)
                    {
                        FileInfo[] QModDlls = new DirectoryInfo(QModsDir).GetFiles("*.dll", SearchOption.AllDirectories);
                        IEnumerable<FileInfo> enabledQModDlls = QModDlls.Where(dll =>
                        {
                            if (args.Name.Contains(Path.GetFileNameWithoutExtension(dll.Name)))
                            {
                                FileInfo[] modjson = dll.Directory.GetFiles("mod.json", SearchOption.TopDirectoryOnly);
                                if (modjson.Length != 0)
                                {
                                    try
                                    {
                                        JObject jObject = JObject.Parse(File.ReadAllText(modjson[0].FullName));
                                        if (
                                            (jObject.TryGetValue("Enabled", out JToken isEnabled) || jObject.TryGetValue("Enable", out isEnabled))
                                            && !(bool)isEnabled
                                        )
                                        {
                                            logger.Warn("❌ Found QMod assembly {Assembly}, but it's marked as DISABLED", dll.Name);
                                            return false;
                                        }
                                        else
                                        {
                                            logger.Info("✔️ Found QMod assembly {Assembly}, marked as ENABLED", dll.Name);
                                        }
                                    }
                                    catch
                                    {
                                        // If mod.json fails to parse, then assume it's enabled
                                        logger.Error("❌ FAILED to parse QMod status for mod {Assembly}, marking as DISABLED", dll.Name);
                                        return false;
                                    }
                                }
                                return true;
                            }
                            return false;
                        });

                        foreach (FileInfo dll in enabledQModDlls)
                        {
                            if (args.Name.Contains(Path.GetFileNameWithoutExtension(dll.Name)))
                            {
                                logger.Info("✔️ Found QMod assembly {assembly}", args.Name);
                                return Assembly.LoadFrom(dll.FullName);
                            }
                        }
                    }

                    assemblyLogger.Warn("❌ Could not find assembly {assembly}", args.Name);
                    return null;
                };

                patchedAssemblyLoading = true;
                assemblyLogger.Info("🩹 Patched Assembly Loading");
            }
        }

        private static List<WrappedMod> ProcessOrder(Func<IManagedMod, Type[]> getBefore, Func<IManagedMod, Type[]> getAfter, Func<WrappedMod, int> getOrder, Func<WrappedMod, bool> actuallyProcess = null)
        {
            Dictionary<string, ModContainer> mods = (Dictionary<string, ModContainer>)ReflectedManMods.m_Mods.GetValue(Singleton.Manager<ManMods>.inst);
            DependencyGraph<Type> dependencies = new DependencyGraph<Type>();
            // add nodes
            foreach (KeyValuePair<Type, WrappedMod> entry in ModManager.managedMods)
            {
                // Only process currently active mods (in the session)
                string modID = entry.Value.ModID;
                if (mods.ContainsKey(modID))
                {
                    if (actuallyProcess == null || actuallyProcess(entry.Value))
                    {
                        dependencies.AddNode(new DependencyGraph<Type>.Node
                        {
                            value = entry.Key,
                            order = getOrder(entry.Value)
                        });
                    }
                    else
                    {
                        logger.Debug($" ⚠️ Mod {modID} BLOCKED from being processed");
                    }
                }
                else
                {
                    logger.Warn($" 🚨 Mod {modID} is present, but not in session");
                }
            }
            // add edges
            foreach (KeyValuePair<Type, WrappedMod> entry in ModManager.managedMods)
            {
                // Only process currently active mods (in the session)
                WrappedMod mod = entry.Value;
                if (mods.ContainsKey(mod.ModID) && (actuallyProcess == null || actuallyProcess(entry.Value)))
                {
                    IManagedMod managedMod;
                    if (!((managedMod = mod.ManagedMod()) is null))
                    {
                        logger.Debug(" ▶ Processing edges for {Mod}", managedMod.Name);
                        Type[] loadBefore = getBefore(managedMod);
                        if (loadBefore != null && loadBefore.Length > 0)
                        {
                            logger.Debug("  ➤ Found LoadBefore Targets: {Targets}", loadBefore);
                            foreach (Type target in loadBefore)
                            {
                                try
                                {
                                    dependencies.AddEdge(target, entry.Key);
                                }
                                catch (Exception ex)
                                {
                                    logger.Error(ex, " ❌ Unable to set constraint to load {mod} before {target}", mod.Name, target.Name);
                                }
                            }
                        }

                        Type[] loadAfter = getAfter(managedMod);
                        if (loadAfter != null && loadAfter.Length > 0)
                        {
                            logger.Debug("  ➤ Found LoadAfter Targets: {Targets}", loadAfter);
                            foreach (Type target in loadAfter)
                            {
                                try
                                {
                                    dependencies.AddEdge(entry.Key, target);
                                }
                                catch (Exception ex)
                                {
                                    logger.Error(ex, " ❌ Unable to set constraint to load {mod} after {target}", mod.Name, target.Name);
                                }
                            }
                        }
                    }
                }
            }

            // Get dependency trees
            if (dependencies.HasCycles())
            {
                logger.Error(" 🚨 CIRCULAR DEPENDENCY DETECTED! See ModManager.log for details");
                throw new Exception("CIRCULAR DEPENDENCY FOUND");
                List<List<int>> cycles = dependencies.FindCycles();
                dependencies.PrintCycles(LogLevel.Debug, cycles);
                dependencies.ResolveCycles(cycles);
            }

            // Sort into an ordered list
            List<WrappedMod> orderedList = new List<WrappedMod>();
            foreach (Type script in dependencies.OrderedQueue())
            {
                orderedList.Add(ModManager.managedMods[script]);
            }
            return orderedList;
        }

        internal static QMod ResolveUnofficialMod(string cloudName)
        {
            if (ModManager.unofficialMods.TryGetValue(cloudName, out QMod managedMod))
            {
                return managedMod;
            }
            return null;
        }

        /// <summary>
        /// loads and processes TTQMM mods in order of defined dependencies
        /// </summary>
        public static void ProcessUnofficialMods()
        {
            QMod.DefaultOrder = new Dictionary<string, int>{
                { "Aceba1/TTQMM-ModConfigHelper", 1 },
                { "Exund/Nuterra-UI", 2 },
                { "Exund/Nuterra.NativeOptions", 3 },
                { "Aceba1/TTQMM-Nuterra-Block-Injector-Library", 4 }
            };

            // Find all mods
            DirectoryInfo unofficialModsDir = new DirectoryInfo(QModsDir);
            DirectoryInfo[] subDirs = unofficialModsDir.GetDirectories();
            int numMods = subDirs.Length;
            int processed = 0;
            CurrentOperation = "Processing TTMM Mods";
            foreach (DirectoryInfo subDir in subDirs)
            {
                CurrentOperationSpecifics = $"Processing {subDir.Name}";
                CurrentOperationProgress = (float)processed / (float)numMods;

                FileInfo config = new FileInfo(Path.Combine(subDir.FullName, "mod.json"));
                FileInfo ttmmInfo = new FileInfo(Path.Combine(subDir.FullName, "ttmm.json"));
                logger.Debug("Exploring subdirectory {Subdir}", subDir.Name);
                if (config.Exists)
                {
                    QMod mod = QMod.FromConfig(config, ttmmInfo);
                    if (mod != null)
                    {
                        logger.Info("Unofficial Mod Registered: {Mod} ({ID})", mod.Name, mod.ID);
                        ModManager.unofficialMods.Add(mod.ID, mod);
                    }
                }
                processed++;
            }
            CurrentOperationSpecifics = null;
            CurrentOperation = null;

            DependencyGraph<QMod> dependencies = new DependencyGraph<QMod>();

            // Add nodes
            foreach (QMod mod in ModManager.unofficialMods.Values)
            {
                dependencies.AddNode(new DependencyGraph<QMod>.Node
                {
                    value = mod,
                    order = mod.LoadOrder
                });
            }

            // Add Edges
            foreach (QMod mod in ModManager.unofficialMods.Values)
            {
                logger.Debug("Processing edges for {Type} mod {Mod}", "UNOFFICIAL", mod.Name);
                QMod[] modDependencies = mod.Dependencies;
                if (modDependencies != null && modDependencies.Length > 0)
                {
                    logger.Debug("  Dependencies FOUND: {dependencies}", modDependencies);
                    foreach (QMod target in modDependencies)
                    {
                        try
                        {
                            dependencies.AddEdge(mod, target);
                        }
                        catch (Exception ex)
                        {
                            logger.Error(ex, "Unable to set constraint to load {mod} after {target}", mod.Name, target.Name);
                        }
                    }
                }
            }

            // Load them
            numMods = ModManager.unofficialMods.Count;
            processed = 0;
            CurrentOperation = "Loading TTMM Mods";
            foreach (QMod mod in dependencies.OrderedQueue())
            {
                CurrentOperationSpecifics = $"Loading {mod.Name}";
                CurrentOperationProgress = (float)processed / (float)numMods;

                if (!mod.IsBlockInjector)
                {
                    logger.Info("Loading {Mod}", mod);
                    Stopwatch sw = new Stopwatch();
                    bool failed = false;
                    try
                    {
                        sw.Start();
                        mod.Init();
                        sw.Stop();
                    }
                    catch (Exception err)
                    {
                        failed = true;
                        logger.Error(err);
                    }
                    finally
                    {
                        sw.Stop();
                    }
                    logger.Info("Mod {Mod} load {Status} - {time}\n", mod, failed ? "FAILED" : "SUCCEEDED", FormatTime(sw.Elapsed));
                }
                else
                {
                    BlockInjector = mod;
                    logger.Info("Not executing BlockInjector entry method - will wait and see if NuterraSteam is executed first");
                }
                processed++;
            }
            CurrentOperationSpecifics = null;
            CurrentOperation = null;
        }

        /// <summary>
        /// </summary>
        /// <param name="span">time to be formatted</param>
        /// <returns>time in 00h:00m:00s:00ms format</returns>
        private static string FormatTime(TimeSpan span)
        {
            string elapsedTime = "";
            if (span.Hours == 0)
                if (span.Minutes == 0)
                    if (span.Seconds == 0)
                        if (span.Milliseconds == 0)
                            return "Loaded immediately";
                        else
                            elapsedTime = String.Format("{0:00}ms", span.Milliseconds);
                    else
                        elapsedTime = String.Format("{0:00}s{1:00}ms", span.Seconds, span.Milliseconds);
                else
                    elapsedTime = String.Format("{0:00}m{1:00}s{2:00}ms", span.Minutes, span.Seconds, span.Milliseconds);
            else
                elapsedTime = String.Format("{0:00}h{1:00}m{2:00}s{3:00}ms", span.Hours, span.Minutes, span.Seconds, span.Milliseconds);
            return "Loaded in " + elapsedTime;
        }

        private static string[] KNOWN_ASSEMBLIES = { "0Harmony", "NLog", "NLogManager", "TTModManager" };

        /// <summary>
        /// Handle loading of Assembly by specified ModContainer. We expect each assembly to only have one ModBase, but multiple are allowed
        /// </summary>
        /// <remarks>We assume each assembly will only ever be processed once, so we make assumptions here</remarks>
        /// <param name="modContainer"></param>
        /// <param name="assembly"></param>
        private static List<WrappedMod> ProcessLoadAssembly(ModContainer modContainer, Assembly assembly)
        {
            ModContents contents = modContainer.Contents;
            PublishedFileId_t workshopID = contents.m_WorkshopId;
            bool isWorkshop = workshopID != PublishedFileId_t.Invalid && !modContainer.Local;
            string localString = isWorkshop ? "WORKSHOP" : "LOCAL";

            logger.Debug(" 📦 Processing assembly {Assembly}", assembly.FullName);

            Type[] types = null;
            try
            {
                types = assembly.GetExportedTypes();
            }
            catch (System.Reflection.ReflectionTypeLoadException ex)
            {
                logger.Error("  ❌ Failed to get types for {Assembly} - assuming dependency failure, saving for forced reload", assembly.FullName);
                logger.Error(ex);
                return null;
            }

            List<WrappedMod> foundMods = new List<WrappedMod>();

            foreach (Type type in types)
            {
                if (!KNOWN_ASSEMBLIES.ToList().Contains(assembly.GetName().Name)) {
                    logger.Trace("    🔎 Type Found: {Type}", type.FullName);
                }

                if (typeof(ModBase).IsAssignableFrom(type) && !typeof(ModManager).IsAssignableFrom(type))
                {
                    ModSource source = new ModSource
                    {
                        ID = modContainer.ModID,
                        Name = contents.ModName,
                        IsWorkshop = isWorkshop,
                        WorkshopID = workshopID
                    };
                    if (!managedMods.TryGetValue(type, out WrappedMod wrappedMod))
                    {
                        try
                        {
                            ManagedMod managedMod = ManagedMod.FromMod(type);
                            if (!(managedMod is null))
                            {
                                logger.Debug("  🔎 Located MANAGED {Local} mod {Script} in mod {Mod} ({ModId})",
                                    localString, type.Name, contents.ModName,
                                    isWorkshop ? modContainer.ModID + " - " + workshopID.ToString() : modContainer.ModID);
                                wrappedMod = new WrappedMod(managedMod as IManagedMod, modContainer, source);
                            }
                            else
                            {
                                logger.Debug("  🔎 Located NON-MANAGED {Local} mod {Script} in mod {Mod} ({ModId})",
                                    localString, type.Name, contents.ModName,
                                    isWorkshop ? modContainer.ModID + " - " + workshopID.ToString() : modContainer.ModID);
                                ModBase createdMod = (Activator.CreateInstance(type) as ModBase);
                                wrappedMod = new WrappedMod(createdMod, modContainer, source);
                            }
                            wrappedMod.ModID = modContainer.ModID;
                            managedMods.Add(type, wrappedMod);
                            if (assembly.FullName.Contains("LegacyBlockLoader"))
                            {
                                LegacyBlockLoader = wrappedMod;
                            }
                            foundMods.Add(wrappedMod);
                        }
                        catch (Exception e)
                        {
                            logger.Error($"  Failed to create a Managed Mod for {type}:\n{e}");
                        }
                    }
                    else
                    {
                        logger.Debug("  ⏩ Already processed {Local} mod {Script} in mod {Mod} ({ModId}). Using first loaded in {LoadedLocal} {LoadedMod} ({LoadedModId})",
                            localString, type.Name, contents.ModName,
                            isWorkshop ? modContainer.ModID + " - " + workshopID.ToString() : modContainer.ModID,
                            wrappedMod.source.IsWorkshop ? "WORKSHOP" : "LOCAL", wrappedMod.source.Name,
                            wrappedMod.source.IsWorkshop ? wrappedMod.source.ID + " - " + wrappedMod.source.WorkshopID.ToString() : wrappedMod.source.ID);
                    }
                }
            }

            if (foundMods.Count > 0)
            {
                TTLogManager.PatchLoggerSetup(assembly);
            }

            return foundMods;
        }

        // We no longer need to forcibly reprocess everything, since we've added hooks into the loading of mods
        /// <summary>
        /// Handle processing of all Official mods. Will detect everything in the mod list, determine dependency relationships based on reflection, and setup the load queue.
        /// </summary>
        /// <remarks>Assumes that all script mods have been loaded correctly (no bad .dll errors)</remarks>
        public static IEnumerator ReprocessOfficialMods(ModSessionInfo session)
        {
            EarlyInitQueue.Clear();
            InitQueue.Clear();
            UpdateQueue.Clear();
            FixedUpdateQueue.Clear();

            // Why did I add this? There is no benefit from attempting to reload any .dlls.
            // If they failed loading once, they will fail loading again because TT's version of mono caches .dlls,
            // which is why this mod is needed in the first place
            // assemblyMetadata.Clear();
            Dictionary<string, ModContainer> mods = (Dictionary<string, ModContainer>)ReflectedManMods.m_Mods.GetValue(Singleton.Manager<ManMods>.inst);

            // Clear out any and all script mods that are present
            logger.Info($"⏳ Reprocessing all mods in session: {session.Mods.Count}");
            int numMods = session.Mods.Count;
            int processed = 0;
            CurrentOperation = "Processing potential code mods";
            foreach (string key in session.Mods.Keys)
            {
                CurrentOperationSpecifics = $"Post-processing {key}";
                CurrentOperationProgress = (float) processed / (float) numMods;
                logger.Debug($"🔍 Trying to locate mods in mod ID {key}");

                ModContainer modContainer;
                if (mods.TryGetValue(key, out modContainer) && modContainer != null)
                {
                    ModContents contents = modContainer.Contents;

                    // This bit of code should never be run on a normal setup (Local mod)
                    // This will null the script parts of any mods, if mod manager is loaded after them.
                    // Should only happen in case of Multiplayer
                    ModBase script = modContainer.Script;
                    if (script != null && !(script is ModManager))
                    {
                        contents.Script = null;
                    }

                    DirectoryInfo parentDirectory = Directory.GetParent(modContainer.AssetBundlePath);
                    logger.Info($"📁 Checking for mods in directory {parentDirectory} for mod ({key})");
                    foreach (FileInfo fileInfo in parentDirectory.EnumerateFiles())
                    {
                        // Ignore loading ModManager
                        if (fileInfo.Extension == ".dll" && !fileInfo.Name.Contains("ModManager") && !fileInfo.Name.Contains("AssemblyLoader") && !fileInfo.Name.Contains("NLog"))
                        {
                            Assembly assembly = Assembly.LoadFrom(fileInfo.FullName);
                            logger.Trace($" 📦 Checking dll ${fileInfo.FullName}");
                            if (assemblyMetadata.TryGetValue(assembly, out ModContainer existingMod))
                            {
                                logger.Debug("  ⏩ Attempting to load assembly {Assembly} as part of Mod {Mod} ({ModID} - {ModLocal}), but assembly is already loaded by mod {ExistingMod} ({ExistingModID} - {ExistingModLocal})",
                                    assembly.FullName,
                                    modContainer.Contents.ModName, modContainer.ModID,
                                    modContainer.Contents.m_WorkshopId != PublishedFileId_t.Invalid ? modContainer.Contents.m_WorkshopId.ToString() : "LOCAL",
                                    existingMod.Contents.ModName, existingMod.ModID,
                                    existingMod.Contents.m_WorkshopId != PublishedFileId_t.Invalid ? existingMod.Contents.m_WorkshopId.ToString() : "LOCAL"
                                );
                            }
                            else
                            {
                                assemblyMetadata.Add(assembly, modContainer);
                                List<WrappedMod> foundMods = ProcessLoadAssembly(modContainer, assembly);
                                if (foundMods != null)
                                {
                                    foreach (WrappedMod wrappedMod in foundMods)
                                    {
                                        modMetadata.Add(wrappedMod, modContainer);
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    logger.Error($"❌ FAILED to fetch ModContainer for {key}");
                    if (!mods.ContainsKey(key))
                    {
                        logger.Error($"  ⛔ m_Mods missing key");
                    }
                    else if (modContainer == null)
                    {
                        logger.Error("  ❌ ModContainer is NULL");
                    }
                }
                processed++;
                yield return null;
            }
            CurrentOperationSpecifics = null;
            CurrentOperation = null;
            yield break;
        }

        public static void ReprocessModOrders(ModSessionInfo session)
        {
            EarlyInitQueue.Clear();
            InitQueue.Clear();
            UpdateQueue.Clear();
            FixedUpdateQueue.Clear();

            // Process the correct load order of EarlyInits
            logger.Info("🕓 Building EarlyInit dependency graph");
            EarlyInitQueue = ProcessOrder(
                (IManagedMod mod) => { return mod.EarlyLoadBefore; },
                (IManagedMod mod) => { return mod.EarlyLoadAfter; },
                (WrappedMod mod) => { return mod.EarlyInitOrder; },
                (WrappedMod mod) => { return session.m_Multiplayer || !mod.IsRemote; });
            logger.Debug("📑 EarlyInit Mod Queue: ");
            foreach (WrappedMod mod in EarlyInitQueue)
            {
                logger.Debug(" 🗳️ {mod}", mod.Name);
            }

            // Process the correct load order of Inits
            logger.Info("🕓 Building Init dependency graph");
            InitQueue = ProcessOrder(
                (IManagedMod mod) => { return mod.LoadBefore; },
                (IManagedMod mod) => { return mod.LoadAfter; },
                (WrappedMod mod) => { return mod.InitOrder; },
                (WrappedMod mod) => { return session.m_Multiplayer || !mod.IsRemote; });
            logger.Debug("📑 Init Mod Queue: ");
            foreach (WrappedMod mod in InitQueue)
            {
                logger.Debug(" 🗳️ {mod}", mod.Name);
            }

            // Process Update order
            logger.Info("🕓 Building Update dependency graph");
            UpdateQueue = ProcessOrder(
                (IManagedMod mod) => { return mod.UpdateBefore; },
                (IManagedMod mod) => { return mod.UpdateAfter; },
                (WrappedMod mod) => { return mod.UpdateOrder; },
                (WrappedMod mod) => { return mod.HasUpdate; });
            logger.Debug("📑 Update Mod Queue: ");
            foreach (WrappedMod mod in UpdateQueue)
            {
                logger.Debug(" 🗳️ {mod}", mod.Name);
            }

            // Process FixedUpdate order
            logger.Info("🕓 Building FixedUpdate dependency graph");
            FixedUpdateQueue = ProcessOrder(
                (IManagedMod mod) => { return mod.FixedUpdateBefore; },
                (IManagedMod mod) => { return mod.FixedUpdateAfter; },
                (WrappedMod mod) => { return mod.FixedUpdateOrder; },
                (WrappedMod mod) => { return mod.HasFixedUpdate; });
            logger.Debug("📑 FixedUpdate Mod Queue: ");
            foreach (WrappedMod mod in FixedUpdateQueue)
            {
                logger.Debug(" 🗳️ {mod}", mod.Name);
            }
        }

        // Patched initializer for block injector - does initialization, but doesn't load blocks
        private static void PatchedBlockInjectorInitializer()
        {
            logger.Info("Patched BlockInjector Initializer");
            Harmony blockInjectorHarmony = new Harmony("nuterra.block.injector");
            blockInjectorHarmony.PatchAll(BlockInjector.LoadedAssembly);
            Type BlockLoader = BlockInjector.LoadedAssembly.GetType("Nuterra.BlockInjector.BlockLoader");
            #region Tech Prefab Patches
            MethodInfo AddTechComponentToPrefab = BlockLoader
                .GetMethod(
                    "AddTechComponentToPrefab",
                    StaticFlags,
                    null,
                    new Type[] { typeof(Type) },
                    null
                );
            Type TechPhysicsReset = BlockInjector.LoadedAssembly.GetType("TechPhysicsReset");
            AddTechComponentToPrefab.Invoke(null, new object[] { TechPhysicsReset });
            logger.Debug("Patched TechPhysicsReset component");
            #endregion

            Type BlockInjectorPatches = BlockLoader.GetNestedTypes(BindingFlags.Static | InstanceFlags).Where(type => type.Name == "Patches").First();
            #region 1.4.0.1+ Patches
            Type OfficialBlocks = BlockInjectorPatches.GetNestedTypes(StaticFlags).Where(type => type.Name == "OfficialBlocks").First();
            MethodInfo Patch = OfficialBlocks.GetMethod(
                "Patch",
                    StaticFlags,
                    null,
                    new Type[] { typeof(Harmony) },
                    null
            );
            Patch.Invoke(null, new object[] { blockInjectorHarmony });
            logger.Debug("Patched OfficialBlocks");
            #endregion

            #region Miscellaneous Patches
            try
            {
                Type Projectile_UnlockColliderQuantity = BlockInjectorPatches.GetNestedTypes(StaticFlags).Where(type => type.Name == "Projectile_UnlockColliderQuantity").First();
                blockInjectorHarmony.Patch(typeof(Projectile).GetMethod("PrePool", InstanceFlags), null, null, transpiler: new HarmonyMethod(Projectile_UnlockColliderQuantity.GetMethod("Transpiler", StaticFlags)));
            }
            catch (Exception E)
            {
                logger.Error(E, "Error while patching custom blocks");
            }

            typeof(ManCustomSkins).GetMethod("Awake", InstanceFlags).Invoke(Singleton.Manager<ManCustomSkins>.inst, Array.Empty<object>());
            #endregion
            logger.Info("BlockInjector Patches complete");
        }
        private static void RunBlockInjector()
        {
            Type DirectoryBlockLoader = BlockInjector.LoadedAssembly.GetType("Nuterra.BlockInjector.DirectoryBlockLoader");
            MethodInfo method = DirectoryBlockLoader.GetMethod("LoadBlocks", StaticFlags);
            // load resources
            IEnumerator<object> resources = (IEnumerator<object>) method.Invoke(null, new object[] { true, false });
            while (resources.MoveNext())
            {

            }
            // load blocks
            IEnumerator<object>  blocks = (IEnumerator<object>) method.Invoke(null, new object[] { false, true });
            while (blocks.MoveNext())
            {

            }
        }

        // Patch block injector if needed. Let NuterraSteam load the blocks
        public static void PatchCustomBlocksIfNeeded()
        {
            if (BlockInjector != null && LegacyBlockLoader is null)
            {
                logger.Info("🚨 BlockInjector present, but LegacyBlockLoader is not. Injecting blocks now.");
                PatchedBlockInjectorInitializer();
                RunBlockInjector();
            }
            else if (BlockInjector != null && LegacyBlockLoader != null)
            {
                logger.Info("🚨 LegacyBlockLoader is present. It will handle block loading");
                PatchedBlockInjectorInitializer();
            }
            else
            {
                logger.Info("✔️ BlockInjector is disabled.");
            }
        }


        public static readonly FieldInfo InjectedEarlyHooks = typeof(ModContainer).GetFields(InstanceFlags).FirstOrDefault(field =>
                field.CustomAttributes.Any(attr => attr.AttributeType == typeof(CompilerGeneratedAttribute)) &&
                (field.DeclaringType == typeof(ModContainer).GetProperty("InjectedEarlyHooks").DeclaringType) &&
                field.FieldType.IsAssignableFrom(typeof(ModContainer).GetProperty("InjectedEarlyHooks").PropertyType) &&
                field.Name.StartsWith("<" + typeof(ModContainer).GetProperty("InjectedEarlyHooks").Name + ">")
            );

        public static void ProcessUpdate()
        {
            foreach (WrappedMod script in UpdateQueue)
            {
                try
                {
                    logger.Trace($"⏳ Firing Update for '{script.Name}'");
                    script.Update();
                }
                catch (Exception e)
                {
                    logger.Error($"⛔ Failed to process Update() for '{script.Name}':\n{e.ToString()}");
                }
            }
        }

        public static void ProcessFixedUpdate()
        {
            foreach (WrappedMod script in FixedUpdateQueue)
            {
                try
                {
                    logger.Trace($"⏳ Firing FixedUpdate for '{script.Name}'");
                    script.FixedUpdate();
                }
                catch (Exception e)
                {
                    logger.Error($"⛔ Failed to process FixedUpdate() for '{script.Name}':\n{e.ToString()}");
                }
            }
        }
    }
}
