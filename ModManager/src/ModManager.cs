using System;
using System.Diagnostics;
using System.IO;
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
        // Runs during ManMods.ProcessLoadingMod, where it does the Activator.CreateInstance(type) as ModBase
        static ModManager()
        {
            ModuleInitializer.Run();
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

        internal static NLog.Logger logger = NLog.LogManager.GetLogger("ModManager");
        public static void ConfigureLogger()
        {
            LogManager.LogConfig config = new LogManager.LogConfig
            {
                layout = "${longdate} | ${level:uppercase=true:padding=-5:alignmentOnTruncation=left} | ${logger:shortName=true} | ${message}  ${exception}",
                keepOldFiles = false,
                defaultMinLevel = LogLevel.Warn
            };
            TTLogManager.RegisterLogger(logger, config);
        }

        internal static Harmony harmony = new Harmony(HarmonyID);

        private static List<string> LoadedMods;
        
        private static List<WrappedMod> EarlyInitQueue = new List<WrappedMod>();
        private static List<WrappedMod> InitQueue = new List<WrappedMod>();
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

        internal static string CurrentOperation = null;
        internal static string CurrentOperationSpecifics = null;
        internal static float CurrentOperationProgress = 0.0f;

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
                script.DeInit();
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
                logger.Info("Patch applied");
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
                        logger.Info($"Processing mod {mod}");
                        string trimmedMod = mod.Trim(new char[] { '[', ']', ' ' });
                        string[] modDescrip = trimmedMod.Split(new char[] { ':' }, 2);
                        if (modDescrip.Length == 2)
                        {
                            string type = modDescrip[0];
                            string modName = modDescrip[1];
                            logger.Info($"Mod ({modName}) determined to be of type {type}");
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
                                            logger.Info("Loading workshop mod {WorkshopID}", steamWorkshopID);
                                        }
                                        else
                                        {
                                            logger.Info("Requested self - ignoring");
                                        }
                                    }
                                    else
                                    {
                                        logger.Error("Attempted to load workshop mod with malformed ID {WorkshopID}", modName);
                                    }
                                    break;
                                case "ttqmm":
                                    logger.Error("Attempted to load mod {Mod} from TTMM. This is currently unsupported", modName);
                                    break;
                                default:
                                    logger.Error("Found malformed mod request {Mod}", trimmedMod);
                                    break;
                            }
                        }
                    }
                    return;
                }
            }

            // We explicitly loaded only this mod. 
            logger.Info("No custom mod list found - getting default mod loading behaviour");
            ManMods manMods = Singleton.Manager<ManMods>.inst;
            typeof(ManMods).GetMethod("CheckForLocalMods", InstanceFlags).Invoke(manMods, null);
            typeof(ManMods).GetMethod("CheckForSteamWorkshopMods", InstanceFlags).Invoke(manMods, null);
        }

        internal static bool TryFindAssembly(ModContainer mod, string name, out Assembly assembly)
        {
            ModManager.logger.Trace("Checking if mod {mod} with bundle at {path} has {assembly}", mod.ModID, mod.AssetBundlePath, name);
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
                    ModManager.logger.Trace("Found assembly {assembly} at path {path}", name, dll.FullName);
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
                    ModSessionInfo session = (ModSessionInfo)ReflectedManMods.m_CurrentSession.GetValue(Singleton.Manager<ManMods>.inst);
                    Dictionary<string, ModContainer> mods = (Dictionary<string, ModContainer>)ReflectedManMods.m_Mods.GetValue(Singleton.Manager<ManMods>.inst);

                    // Try to get .dll from mod that shares its name, if extant
                    if (mods.TryGetValue(args.Name, out ModContainer mod) && TryFindAssembly(mod, args.Name, out Assembly assembly))
                    {
                        return assembly;
                    }
                    foreach (string key in session.Mods.Keys)
                    {
                        if (key != args.Name)
                        {
                            ModContainer modContainer;
                            if (mods.TryGetValue(key, out modContainer) && modContainer != null)
                            {
                                if (TryFindAssembly(modContainer, args.Name, out assembly))
                                {
                                    return assembly;
                                }
                            }
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
                                            logger.Warn("Found QMod assembly {Assembly}, but it's marked as DISABLED", dll.Name);
                                            return false;
                                        }
                                        else
                                        {
                                            logger.Info("Found QMod assembly {Assembly}, marked as ENABLED", dll.Name);
                                        }
                                    }
                                    catch
                                    {
                                        // If mod.json fails to parse, then assume it's enabled
                                        logger.Error("FAILED to parse QMod status for mod {Assembly}, marking as DISABLED", dll.Name);
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
                                logger.Info("Found QMod assembly {assembly}", args.Name);
                                return Assembly.LoadFrom(dll.FullName);
                            }
                        }
                    }

                    logger.Info("Could not find assembly {assembly}", args.Name);
                    return null;
                };

                patchedAssemblyLoading = true;
                logger.Info("Patched Assembly Loading");
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
                        logger.Debug($"Mod {modID} BLOCKED from being processed");
                    }
                }
                else
                {
                    logger.Warn($"Mod {modID} is present, but not in session");
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
                        logger.Debug("Processing edges for {Mod}", managedMod.Name);
                        Type[] loadBefore = getBefore(managedMod);
                        if (loadBefore != null && loadBefore.Length > 0)
                        {
                            logger.Debug("  Found LoadBefore Targets: {Targets}", loadBefore);
                            foreach (Type target in loadBefore)
                            {
                                try
                                {
                                    dependencies.AddEdge(target, entry.Key);
                                }
                                catch (Exception ex)
                                {
                                    logger.Error(ex, "Unable to set constraint to load {mod} before {target}", mod.Name, target.Name);
                                }
                            }
                        }

                        Type[] loadAfter = getAfter(managedMod);
                        if (loadAfter != null && loadAfter.Length > 0)
                        {
                            logger.Debug("  Found LoadAfter Targets: {Targets}", loadAfter);
                            foreach (Type target in loadAfter)
                            {
                                try
                                {
                                    dependencies.AddEdge(entry.Key, target);
                                }
                                catch (Exception ex)
                                {
                                    logger.Error(ex, "Unable to set constraint to load {mod} after {target}", mod.Name, target.Name);
                                }
                            }
                        }
                    }
                }
            }

            // Get dependency trees
            if (dependencies.HasCycles())
            {
                logger.Error("CIRCULAR DEPENDENCY DETECTED! See ModManager.log for details");
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

            logger.Debug("Processing assembly {Assembly}", assembly.FullName);

            Type[] types = null;
            try
            {
                types = assembly.GetExportedTypes();
            }
            catch (System.Reflection.ReflectionTypeLoadException ex)
            {
                logger.Error("Failed to get types for {Assembly} - assuming dependency failure, saving for forced reload", assembly.FullName);
                logger.Error(ex);
                return null;
            }

            List<WrappedMod> foundMods = new List<WrappedMod>();

            foreach (Type type in types)
            {
                if (!KNOWN_ASSEMBLIES.ToList().Contains(assembly.GetName().Name)) {
                    logger.Trace("Type Found: {Type}", type.FullName);
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
                                logger.Debug("Located MANAGED {Local} mod {Script} in mod {Mod} ({ModId})",
                                    localString, type.Name, contents.ModName,
                                    isWorkshop ? modContainer.ModID + " - " + workshopID.ToString() : modContainer.ModID);
                                wrappedMod = new WrappedMod(managedMod as IManagedMod, modContainer, source);
                            }
                            else
                            {
                                logger.Debug("Located NON-MANAGED {Local} mod {Script} in mod {Mod} ({ModId})",
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
                            logger.Error($"Failed to create a Managed Mod for {type}:\n{e}");
                        }
                    }
                    else
                    {
                        logger.Debug("Already processed {Local} mod {Script} in mod {Mod} ({ModId}). Using first loaded in {LoadedLocal} {LoadedMod} ({LoadedModId})",
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
        public static IEnumerator<float> ReprocessOfficialMods(ModSessionInfo session)
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
            logger.Info($"Reprocessing all mods in session: {session.Mods.Count}");
            int numMods = session.Mods.Count;
            int processed = 0;
            CurrentOperation = "Processing potential code mods";
            foreach (string key in session.Mods.Keys)
            {
                CurrentOperationSpecifics = $"Post-processing {key}";
                CurrentOperationProgress = (float) processed / (float) numMods;
                logger.Debug($"Trying to locate mods in mod ID {key}");

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
                    logger.Info($"Checking for mods in directory {parentDirectory} for mod ({key})");
                    foreach (FileInfo fileInfo in parentDirectory.EnumerateFiles())
                    {
                        // Ignore loading ModManager
                        if (fileInfo.Extension == ".dll" && !fileInfo.Name.Contains("ModManager") && !fileInfo.Name.Contains("AssemblyLoader") && !fileInfo.Name.Contains("NLog"))
                        {
                            Assembly assembly = Assembly.LoadFrom(fileInfo.FullName);
                            logger.Trace($"Checking dll ${fileInfo.FullName}");
                            if (assemblyMetadata.TryGetValue(assembly, out ModContainer existingMod))
                            {
                                logger.Debug("Attempting to load assembly {Assembly} as part of Mod {Mod} ({ModID} - {ModLocal}), but assembly is already loaded by mod {ExistingMod} ({ExistingModID} - {ExistingModLocal})",
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
                    logger.Error($"FAILED to fetch ModContainer for {key}");
                    if (!mods.ContainsKey(key))
                    {
                        logger.Error($"  m_Mods missing key");
                    }
                    else if (modContainer == null)
                    {
                        logger.Error("  ModContainer is NULL");
                    }
                }
                processed++;
                yield return CurrentOperationProgress;
            }
            CurrentOperationSpecifics = null;
            CurrentOperation = null;
            yield return 1.0f;
            yield break;
        }

        public static void ReprocessModOrders(ModSessionInfo session)
        {
            EarlyInitQueue.Clear();
            InitQueue.Clear();
            UpdateQueue.Clear();
            FixedUpdateQueue.Clear();

            // Process the correct load order of EarlyInits
            logger.Info("Building EarlyInit dependency graph");
            EarlyInitQueue = ProcessOrder(
                (IManagedMod mod) => { return mod.EarlyLoadBefore; },
                (IManagedMod mod) => { return mod.EarlyLoadAfter; },
                (WrappedMod mod) => { return mod.EarlyInitOrder; },
                (WrappedMod mod) => { return session.m_Multiplayer || !mod.IsRemote; });
            logger.Debug("EarlyInit Mod Queue: ");
            foreach (WrappedMod mod in EarlyInitQueue)
            {
                logger.Debug(" - {mod}", mod.Name);
            }

            // Process the correct load order of Inits
            logger.Info("Building Init dependency graph");
            InitQueue = ProcessOrder(
                (IManagedMod mod) => { return mod.LoadBefore; },
                (IManagedMod mod) => { return mod.LoadAfter; },
                (WrappedMod mod) => { return mod.InitOrder; },
                (WrappedMod mod) => { return session.m_Multiplayer || !mod.IsRemote; });
            logger.Debug("Init Mod Queue: ");
            foreach (WrappedMod mod in InitQueue)
            {
                logger.Debug(" - {mod}", mod.Name);
            }

            // Process Update order
            logger.Info("Building Update dependency graph");
            UpdateQueue = ProcessOrder(
                (IManagedMod mod) => { return mod.UpdateBefore; },
                (IManagedMod mod) => { return mod.UpdateAfter; },
                (WrappedMod mod) => { return mod.UpdateOrder; },
                (WrappedMod mod) => { return mod.HasUpdate; });
            logger.Debug("Update Mod Queue: ");
            foreach (WrappedMod mod in UpdateQueue)
            {
                logger.Debug(" - {mod}", mod.Name);
            }

            // Process FixedUpdate order
            logger.Info("Building FixedUpdate dependency graph");
            FixedUpdateQueue = ProcessOrder(
                (IManagedMod mod) => { return mod.FixedUpdateBefore; },
                (IManagedMod mod) => { return mod.FixedUpdateAfter; },
                (WrappedMod mod) => { return mod.FixedUpdateOrder; },
                (WrappedMod mod) => { return mod.HasFixedUpdate; });
            logger.Debug("FixedUpdate Mod Queue: ");
            foreach (WrappedMod mod in FixedUpdateQueue)
            {
                logger.Debug(" - {mod}", mod.Name);
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
                logger.Info("BlockInjector present, but LegacyBlockLoader is not. Injecting blocks now.");
                PatchedBlockInjectorInitializer();
                RunBlockInjector();
            }
            else if (BlockInjector != null && LegacyBlockLoader != null)
            {
                logger.Info("LegacyBlockLoader is present. It will handle block loading");
                PatchedBlockInjectorInitializer();
            }
            else
            {
                logger.Info("BlockInjector is disabled.");
            }
        }


        public static readonly FieldInfo InjectedEarlyHooks = typeof(ModContainer).GetFields(InstanceFlags).FirstOrDefault(field =>
                field.CustomAttributes.Any(attr => attr.AttributeType == typeof(CompilerGeneratedAttribute)) &&
                (field.DeclaringType == typeof(ModContainer).GetProperty("InjectedEarlyHooks").DeclaringType) &&
                field.FieldType.IsAssignableFrom(typeof(ModContainer).GetProperty("InjectedEarlyHooks").PropertyType) &&
                field.Name.StartsWith("<" + typeof(ModContainer).GetProperty("InjectedEarlyHooks").Name + ">")
            );
        public static IEnumerator<float> ProcessEarlyInits()
        {
            // Process the EarlyInits
            logger.Info("Processing Early Inits");
            int numMods = EarlyInitQueue.Count;
            int processed = 0;
            CurrentOperation = "Handling code mod first-time setup";
            foreach (WrappedMod script in EarlyInitQueue)
            {
                CurrentOperationSpecifics = $"Processing {script.Name} EarlyInit()";
                CurrentOperationProgress = (float)processed / (float)numMods;
                if (!script.earlyInitRun)
                {
                    try
                    {
                        logger.Debug("Processing EarlyInit for mod {}", script.Name);
                        script.EarlyInit();
                        script.earlyInitRun = true;

                        ModContainer container = modMetadata[script];
                        InjectedEarlyHooks.SetValue(container, true);

                        containersWithEarlyHooks.Add(container);
                    }
                    catch (Exception e)
                    {
                        logger.Error($"Failed to process EarlyInit() for {script.Name}:\n{e.ToString()}");
                    }
                }
                processed++;
                yield return CurrentOperationProgress;
            }
            CurrentOperationSpecifics = null;
            CurrentOperation = null;
            yield return 1.0f;
            yield break;
        }

        public static IEnumerator<float> ProcessInits()
        {
            // Process the Inits
            logger.Info("Processing Inits");
            int numMods = InitQueue.Count;
            int processed = 0;
            CurrentOperation = "Initializing code mods";
            foreach (WrappedMod script in InitQueue)
            {
                CurrentOperationSpecifics = $"Processing {script.Name} Init()";
                CurrentOperationProgress = (float)processed / (float)numMods;
                logger.Debug("Processing Init for mod {}", script.Name);
                try
                {
                    script.Init();
                }
                catch (Exception e)
                {
                    logger.Error($"Failed to process Init() for {script.Name}:\n{e.ToString()}");
                }
                processed++;
                yield return CurrentOperationProgress;
            }
            CurrentOperation = null;
            CurrentOperationSpecifics = null;
            yield return 1.0f;
            yield break;
        }

        internal static IEnumerator<float> InjectModdedCorps(ManMods manMods, ModSessionInfo newSessionInfo)
        {
            // Process the Inits
            logger.Info("Injecting Modded Corps");
            int processed = 0;
            CurrentOperation = "Injecting modded corps";

            if (newSessionInfo.CorpIDs.Count > 0)
            {
                int numCorps = newSessionInfo.CorpIDs.Count;
                Dictionary<string, int> reverseLookup = (Dictionary<string, int>)ReflectedManMods.m_CorpIDReverseLookup.GetValue(manMods);
                Dictionary<int, ModdedCorpDefinition> dictionary = new Dictionary<int, ModdedCorpDefinition>();
                foreach (KeyValuePair<int, string> keyValuePair in newSessionInfo.CorpIDs)
                {
                    CurrentOperationSpecifics = $"{keyValuePair.Value}";
                    logger.Trace("Injecting corp {corp}", keyValuePair.Value);
                    CurrentOperationProgress = (float)processed / (float)numCorps;
                    int corpIndex = keyValuePair.Key;
                    ModdedCorpDefinition moddedCorpDefinition = manMods.FindModdedAsset<ModdedCorpDefinition>(keyValuePair.Value);
                    if (moddedCorpDefinition != null)
                    {
                        dictionary.Add(corpIndex, moddedCorpDefinition);
                        Singleton.Manager<ManPurchases>.inst.AddCustomCorp(corpIndex);
                        Singleton.Manager<ManCustomSkins>.inst.AddCorp(corpIndex);
                        ReflectedManMods.InjectCustomSkinReferences.Invoke(manMods, new object[] { 0, (FactionSubTypes)corpIndex, moddedCorpDefinition.m_DefaultSkinSlots[0] });
                        reverseLookup.Add(moddedCorpDefinition.m_ShortName, corpIndex);
                        logger.Info(string.Format("Injected corp {0} at ID {1}", moddedCorpDefinition.name, corpIndex));
                    }
                    processed++;
                    yield return CurrentOperationProgress;
                }
                Singleton.Manager<ManLicenses>.inst.m_UnlockTable.AddModdedCorps(dictionary);
            }

            CurrentOperation = null;
            CurrentOperationSpecifics = null;
            yield return 1.0f;
            yield break;
        }

        internal static IEnumerator<float> InjectModdedSkins(ManMods manMods, ModSessionInfo newSessionInfo)
        {
            // Process the Inits
            logger.Info("Injecting Modded Skins");
            CurrentOperation = "Injecting modded skins";

            if (newSessionInfo.SkinIDsByCorp.Count > 0)
            {
                foreach (KeyValuePair<int, Dictionary<int, string>> keyValuePair in newSessionInfo.SkinIDsByCorp)
                {
                    int processed = 0;
                    int count = keyValuePair.Value.Count;
                    int key = keyValuePair.Key;
                    foreach (KeyValuePair<int, string> keyValuePair2 in keyValuePair.Value)
                    {
                        CurrentOperationSpecifics = $"{keyValuePair2.Value}";
                        CurrentOperationProgress = (float)processed / (float)count;
                        logger.Trace("Injecting skin {skin}", keyValuePair2.Value);
                        int key2 = keyValuePair2.Key;
                        ModdedSkinDefinition moddedSkinDefinition = manMods.FindModdedAsset<ModdedSkinDefinition>(keyValuePair2.Value);
                        if (moddedSkinDefinition != null)
                        {
                            if (moddedSkinDefinition.m_Albedo == null)
                            {
                                logger.Error(string.Format("Cannot inject skin {0} at ID {1}: Albedo texture was not found. Did you set it?", moddedSkinDefinition.name, key2));
                            }
                            else if (moddedSkinDefinition.m_Combined == null)
                            {
                                logger.Error(string.Format("Cannot inject skin {0} at ID {1}: Combined Metallic/Smoothness texture was not found. Did you set both of them?", moddedSkinDefinition.name, key2));
                            }
                            else if (moddedSkinDefinition.m_Emissive == null)
                            {
                                logger.Error(string.Format("Cannot inject skin {0} at ID {1}: Emmisive texture was not found. Did you set it?", moddedSkinDefinition.name, key2));
                            }
                            else if (moddedSkinDefinition.m_PreviewImage == null)
                            {
                                logger.Error(string.Format("Cannot inject skin {0} at ID {1}: Auto-generated preview texture was not found. This implies a problem with the TTModTool exporter.", moddedSkinDefinition.name, key2));
                            }
                            else if (moddedSkinDefinition.m_SkinButtonImage == null)
                            {
                                logger.Error(string.Format("Cannot inject skin {0} at ID {1}: Skin button texture not found. Did you set it?", moddedSkinDefinition.name, key2));
                            }
                            else
                            {
                                FactionSubTypes corpIndex = manMods.GetCorpIndex(moddedSkinDefinition.m_Corporation, null);
                                if (corpIndex != (FactionSubTypes)(-1))
                                {
                                    ReflectedManMods.InjectCustomSkinReferences.Invoke(manMods, new object[] { key2, corpIndex, moddedSkinDefinition });
                                }
                                else
                                {
                                    logger.Error(string.Format("Cannot inject skin {0} at ID {1}: Corp {2} was not found - is it part of a different mod?", moddedSkinDefinition.name, key2, moddedSkinDefinition.m_Corporation));
                                }
                            }
                        }
                        else
                        {
                            logger.Warn(string.Format("Failed to inject skin {0} at ID {1}. Did the mod remove a skin?", keyValuePair2.Value, keyValuePair2.Key));
                        }
                        processed++;
                        yield return CurrentOperationProgress;
                    }
                }
            }

            CurrentOperation = null;
            CurrentOperationSpecifics = null;
            yield return 1.0f;
            yield break;
        }

        internal static IEnumerator<float> InjectLegacyBlocks(
            ModSessionInfo newSessionInfo,
            Dictionary<int, Dictionary<int, Dictionary<BlockTypes, ModdedBlockDefinition>>> gradeBlockPerCorp,
            Dictionary<int, Sprite> blockSpriteDict
        )
        {
            return null;
        }

        internal static IEnumerator<float> InjectModdedBlocks(ManMods manMods, ModSessionInfo newSessionInfo, ModSessionInfo currentSession)
        {
            // Process the Inits
            logger.Info("Injecting Modded Blocks");
            CurrentOperation = "Injecting modded blocks";
            if (newSessionInfo.BlockIDs.Count > 0)
            {
                int processed = 0;
                int numBlocks = newSessionInfo.BlockIDs.Count;
                Dictionary<int, Dictionary<int, Dictionary<BlockTypes, ModdedBlockDefinition>>> gradeBlocksPerCorp = new Dictionary<int, Dictionary<int, Dictionary<BlockTypes, ModdedBlockDefinition>>>();
                Dictionary<int, Sprite> blockSpriteDict = new Dictionary<int, Sprite>(16);
                Dictionary<int, string> blockNames = (Dictionary<int, string>)ReflectedManMods.m_BlockNames.GetValue(manMods);
                Dictionary<int, string> blockDescriptions = (Dictionary<int, string>)ReflectedManMods.m_BlockDescriptions.GetValue(manMods);
                Dictionary<string, int> reverseLookup = (Dictionary<string, int>)ReflectedManMods.m_BlockIDReverseLookup.GetValue(manMods);
                List<int> failedBlockIDs = new List<int>();
                foreach (KeyValuePair<int, string> blockPair in newSessionInfo.BlockIDs)
                {
                    int blockIndex = blockPair.Key;
                    string blockID = blockPair.Value;
                    CurrentOperationSpecifics = $"{blockID}";
                    CurrentOperationProgress = (float)processed / (float)numBlocks;
                    logger.Trace($"Preparing to inject {blockID} (processed # {processed})");
                    try
                    {
                        ModdedBlockDefinition moddedBlockDefinition = manMods.FindModdedAsset<ModdedBlockDefinition>(blockID);
                        ModContainer mod = null;
                        string modId;
                        string text;
                        if (ModUtils.SplitCompoundId(blockID, out modId, out text))
                        {
                            mod = manMods.FindMod(modId);
                        }
                        if (moddedBlockDefinition != null)
                        {
                            int hashCode = ItemTypeInfo.GetHashCode(ObjectTypes.Block, blockIndex);
                            FactionSubTypes corpIndex = manMods.GetCorpIndex(moddedBlockDefinition.m_Corporation, newSessionInfo);
                            TankBlockTemplate physicalPrefab = moddedBlockDefinition.m_PhysicalPrefab;
                            Visible visible = physicalPrefab.GetComponent<Visible>();
                            if (visible == null)
                            {
                                logger.Debug("Injected block {block} and performed first time setup", moddedBlockDefinition.name);
                                if (visible == null)
                                {
                                    visible = physicalPrefab.gameObject.AddComponent<Visible>();
                                }
                                UnityEngine.Object component = physicalPrefab.gameObject.GetComponent<Damageable>();
                                ModuleDamage moduleDamage = physicalPrefab.gameObject.GetComponent<ModuleDamage>();
                                if (component == null)
                                {
                                    physicalPrefab.gameObject.AddComponent<Damageable>();
                                }
                                if (moduleDamage == null)
                                {
                                    moduleDamage = physicalPrefab.gameObject.AddComponent<ModuleDamage>();
                                }
                                TankBlock component2 = physicalPrefab.gameObject.GetComponent<TankBlock>();
                                component2.m_BlockCategory = moddedBlockDefinition.m_Category;
                                component2.m_BlockRarity = moddedBlockDefinition.m_Rarity;
                                component2.m_DefaultMass = Mathf.Clamp(moddedBlockDefinition.m_Mass, 0.0001f, float.MaxValue);
                                component2.filledCells = physicalPrefab.filledCells.ToArray();
                                component2.attachPoints = physicalPrefab.attachPoints.ToArray();
                                visible.m_ItemType = new ItemTypeInfo(ObjectTypes.Block, blockIndex);

                                logger.Trace("Preparing to load block JSON");
                                JSONBlockLoader.Load(mod, blockIndex, moddedBlockDefinition, component2);
                                logger.Trace("Block JSON loaded");

                                physicalPrefab = moddedBlockDefinition.m_PhysicalPrefab;
                                physicalPrefab.gameObject.SetActive(false);
                                Damageable component3 = physicalPrefab.GetComponent<Damageable>();
                                moduleDamage = physicalPrefab.GetComponent<ModuleDamage>();
                                component2 = physicalPrefab.GetComponent<TankBlock>();
                                visible = physicalPrefab.GetComponent<Visible>();
                                visible.m_ItemType = new ItemTypeInfo(ObjectTypes.Block, blockIndex);
                                component3.m_DamageableType = moddedBlockDefinition.m_DamageableType;
                                moduleDamage.maxHealth = moddedBlockDefinition.m_MaxHealth;
                                if (moduleDamage.deathExplosion == null)
                                {
                                    logger.Trace("Adding default DeathExplosion");
                                    moduleDamage.deathExplosion = manMods.m_DefaultBlockExplosion;
                                }
                                foreach (MeshRenderer meshRenderer in physicalPrefab.GetComponentsInChildren<MeshRenderer>())
                                {
                                    MeshRendererTemplate component4 = meshRenderer.GetComponent<MeshRendererTemplate>();
                                    if (component4 != null)
                                    {
                                        meshRenderer.sharedMaterial = manMods.GetMaterial((int)corpIndex, component4.slot);
                                        d.Assert(meshRenderer.sharedMaterial != null, "[Mods] Custom block " + moddedBlockDefinition.m_BlockDisplayName + " could not load texture. Corp was " + moddedBlockDefinition.m_Corporation);
                                    }
                                }
                                physicalPrefab.gameObject.name = moddedBlockDefinition.name;
                                physicalPrefab.gameObject.tag = "Untagged";
                                physicalPrefab.gameObject.layer = LayerMask.NameToLayer("Tank");
                                MeshCollider[] componentsInChildren2 = component2.GetComponentsInChildren<MeshCollider>();
                                for (int i = 0; i < componentsInChildren2.Length; i++)
                                {
                                    componentsInChildren2[i].convex = true;
                                }

                                logger.Trace("Creating component pool");
                                component2.transform.CreatePool(8);
                            }
                            else
                            {
                                physicalPrefab.gameObject.GetComponent<Visible>().m_ItemType = new ItemTypeInfo(ObjectTypes.Block, blockIndex);

                                logger.Trace("Updating component pool");
                                physicalPrefab.transform.CreatePool(8);
                            }

                            CurrentOperationSpecifics = moddedBlockDefinition.m_BlockDisplayName;
                            blockNames.Add(blockIndex, moddedBlockDefinition.m_BlockDisplayName);
                            blockDescriptions.Add(blockIndex, moddedBlockDefinition.m_BlockDescription);
                            reverseLookup.Add(moddedBlockDefinition.name, blockIndex);
                            Singleton.Manager<ManSpawn>.inst.AddBlockToDictionary(physicalPrefab.gameObject, blockIndex);
                            Singleton.Manager<ManSpawn>.inst.VisibleTypeInfo.SetDescriptor<FactionSubTypes>(hashCode, corpIndex);
                            Singleton.Manager<ManSpawn>.inst.VisibleTypeInfo.SetDescriptor<BlockCategories>(hashCode, moddedBlockDefinition.m_Category);
                            Singleton.Manager<ManSpawn>.inst.VisibleTypeInfo.SetDescriptor<BlockRarity>(hashCode, moddedBlockDefinition.m_Rarity);
                            Singleton.Manager<RecipeManager>.inst.RegisterCustomBlockRecipe(blockIndex, moddedBlockDefinition.m_Price);
                            if (moddedBlockDefinition.m_Icon != null)
                            {
                                blockSpriteDict[blockIndex] = Sprite.Create(moddedBlockDefinition.m_Icon, new Rect(0f, 0f, (float)moddedBlockDefinition.m_Icon.width, (float)moddedBlockDefinition.m_Icon.height), Vector2.zero);
                            }
                            else
                            {
                                logger.Error($"Block {{block}} with ID {blockIndex} failed to inject because icon was not set", moddedBlockDefinition.name);
                            }
                            if (!gradeBlocksPerCorp.ContainsKey((int)corpIndex))
                            {
                                gradeBlocksPerCorp[(int)corpIndex] = new Dictionary<int, Dictionary<BlockTypes, ModdedBlockDefinition>>();
                            }
                            Dictionary<int, Dictionary<BlockTypes, ModdedBlockDefinition>> blockDictPerGrade = gradeBlocksPerCorp[(int)corpIndex];
                            if (!blockDictPerGrade.ContainsKey(moddedBlockDefinition.m_Grade - 1))
                            {
                                blockDictPerGrade[moddedBlockDefinition.m_Grade - 1] = new Dictionary<BlockTypes, ModdedBlockDefinition>();
                            }
                            blockDictPerGrade[moddedBlockDefinition.m_Grade - 1].Add((BlockTypes)blockIndex, moddedBlockDefinition);
                            JSONBlockLoader.Inject(blockIndex, moddedBlockDefinition);
                            logger.Debug($"Injected block {{block}} at ID {blockIndex}", moddedBlockDefinition.name);
                        }
                        else
                        {
                            logger.Warn("Could not find ModdedBlockDefinition for {block}", blockID);
                            failedBlockIDs.Add(blockIndex);
                        }
                    }
                    catch (Exception e)
                    {
                        failedBlockIDs.Add(blockIndex);
                        logger.Error($"FAILED to inject block {blockID}\n:{e}");
                    }
                    processed++;
                    yield return CurrentOperationProgress;
                }
                if (failedBlockIDs.Count > 0)
                {
                    logger.Debug("Removing failed blocks");
                }
                foreach (int key2 in failedBlockIDs)
                {
                    newSessionInfo.BlockIDs.Remove(key2);
                }
                logger.Info("Injected all official blocks");

                IEnumerator<float> legacyBlocksIterator = InjectLegacyBlocks(newSessionInfo, gradeBlocksPerCorp, blockSpriteDict);
                if (legacyBlocksIterator != null)
                {
                    logger.Info("Injecting Legacy Modded Blocks");
                    CurrentOperation = "Injecting legacy modded blocks";
                    while (legacyBlocksIterator.MoveNext())
                    {
                        CurrentOperationProgress = legacyBlocksIterator.Current;
                        yield return CurrentOperationProgress;
                    }
                }

                logger.Debug("Setting up block icons");
                Singleton.Manager<ManUI>.inst.m_SpriteFetcher.SetModSprites(ObjectTypes.Block, blockSpriteDict);

                logger.Debug("Setting up BlockUnlockTable");
                BlockUnlockTable blockUnlockTable = Singleton.Manager<ManLicenses>.inst.GetBlockUnlockTable();

                logger.Trace("Removing modded blocks");
                blockUnlockTable.RemoveModdedBlocks();

                logger.Trace("Adding current modded blocks");
                foreach (KeyValuePair<int, Dictionary<int, Dictionary<BlockTypes, ModdedBlockDefinition>>> corpBlocks in gradeBlocksPerCorp)
                {
                    logger.Debug($"Processing blocks in corp {corpBlocks.Key}");
                    ModdedCorpDefinition corpDefinition = manMods.GetCorpDefinition((FactionSubTypes)corpBlocks.Key, newSessionInfo);
                    foreach (KeyValuePair<int, Dictionary<BlockTypes, ModdedBlockDefinition>> gradeBlocks in corpBlocks.Value)
                    {
                        logger.Trace($"Processing blocks in grade {gradeBlocks.Key}");
                        blockUnlockTable.AddModdedBlocks(corpBlocks.Key, gradeBlocks.Key, gradeBlocks.Value);
                        if (manMods.IsModdedCorp((FactionSubTypes)corpBlocks.Key))
                        {
                            if (corpDefinition.m_RewardCorp != null)
                            {
                                Singleton.Manager<ManLicenses>.inst.GetRewardPoolTable().AddModdedBlockRewards(gradeBlocks.Value, gradeBlocks.Key, manMods.GetCorpIndex(corpDefinition.m_RewardCorp, null));
                            }
                        }
                        else
                        {
                            Singleton.Manager<ManLicenses>.inst.GetRewardPoolTable().AddModdedBlockRewards(gradeBlocks.Value, gradeBlocks.Key, (FactionSubTypes)corpBlocks.Key);
                        }
                    }
                }
                logger.Trace($"Initing table");
                blockUnlockTable.Init();
            }

            CurrentOperation = null;
            CurrentOperationSpecifics = null;
            yield return 1.0f;
            yield break;
        }

        public static void ProcessUpdate()
        {
            foreach (WrappedMod script in UpdateQueue)
            {
                try
                {
                    logger.Trace($"Firing Update for {script.Name}");
                    script.Update();
                }
                catch (Exception e)
                {
                    logger.Error($"Failed to process Update() for {script.Name}:\n{e.ToString()}");
                }
            }
        }

        public static void ProcessFixedUpdate()
        {
            foreach (WrappedMod script in FixedUpdateQueue)
            {
                try
                {
                    logger.Trace($"Firing FixedUpdate for {script.Name}");
                    script.FixedUpdate();
                }
                catch (Exception e)
                {
                    logger.Error($"Failed to process FixedUpdate() for {script.Name}:\n{e.ToString()}");
                }
            }
        }
    }
}
