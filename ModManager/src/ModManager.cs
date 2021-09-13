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
        private static bool patchedAssemblyLoading = false;
        private static bool patched = false;
        internal const string HarmonyID = "com.flsoz.ttmodding.modmanager";

        internal static readonly string TTSteamDir = Path.GetFullPath(Path.Combine(
            AppDomain.CurrentDomain.GetAssemblies()
            .Where(assembly => assembly.GetName().Name == "Assembly-CSharp").First().Location
            .Replace("Assembly-CSharp.dll", ""), @"../../"
        ));
        // internal static readonly string TTSteamDir = @"E:/Steam/steamapps/common/TerraTech";
        private static readonly string QModsDir = Path.Combine(TTSteamDir, "QMods");
        private static readonly string WorkshopDir = Path.Combine(TTSteamDir, @"../../workshop/content/285920/");
        internal const int DEFAULT_LOAD_ORDER = 10;

        internal static NLog.Logger logger = NLog.LogManager.GetCurrentClassLogger();
        internal static void ConfigureLogger()
        {
            Manager.LogConfig config = new Manager.LogConfig
            {
                layout = "${longdate} | ${level:uppercase=true:padding=-5:alignmentOnTruncation=left} | ${logger:shortName=true} | ${message}  ${exception}",
                keepOldFiles = false,
                minLevel = LogLevel.Trace
            };
            Manager.RegisterLogger(logger, config);
        }

        internal static Harmony harmony = new Harmony(HarmonyID);
        
        private static List<WrappedMod> EarlyInitQueue;
        private static List<WrappedMod> InitQueue;

        internal static Dictionary<ulong, SteamDownloadItemData> workshopMetadata = new Dictionary<ulong, SteamDownloadItemData>();
        internal static Dictionary<Assembly, ModContainer> assemblyMetadata = new Dictionary<Assembly, ModContainer>();

        internal static Dictionary<Type, WrappedMod> managedMods = new Dictionary<Type, WrappedMod>();
        internal static Dictionary<string, QMod> unofficialMods = new Dictionary<string, QMod>();
        internal static QMod BlockInjector = null;
        internal static WrappedMod LegacyBlockLoader = null;

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
            assemblyMetadata.Clear();
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
        }

        public static void PatchAssemblyLoading()
        {
            if (!patchedAssemblyLoading)
            {
                AppDomain.CurrentDomain.AssemblyResolve += delegate (object sender, ResolveEventArgs args)
                {
                    FileInfo[] RemoteDlls = new DirectoryInfo(WorkshopDir).GetFiles("*.dll", SearchOption.AllDirectories);
                    FileInfo[] LocalDlls = new DirectoryInfo(ManMods.LocalModsDirectory).GetFiles("*.dll", SearchOption.AllDirectories);
                    
                    // filter to enabled QMod DLLs
                    FileInfo[] QModDlls = new DirectoryInfo(QModsDir).GetFiles("*.dll", SearchOption.AllDirectories);
                    var enabledQModDlls = QModDlls.Where(dll => {
                        if (args.Name.Contains(Path.GetFileNameWithoutExtension(dll.Name)))
                        {
                            FileInfo[] modjson = dll.Directory.GetFiles("mod.json", SearchOption.TopDirectoryOnly);
                            if (modjson.Length != 0)
                            {
                                try
                                {
                                    if (Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(modjson[0].FullName))
                                        .TryGetValue("Enabled", out Newtonsoft.Json.Linq.JToken isEnabled) &&
                                        !(bool)isEnabled)
                                    {
                                        logger.Info("Found assembly {Assembly}, but it's marked as disabled", dll.Name);
                                        return false;
                                    }
                                }
                                catch {
                                    // If mod.json fails to parse, then assume it's enabled
                                }
                            }
                            return true;
                        }
                        return false;
                    });

                    foreach (FileInfo dll in RemoteDlls.Concat(LocalDlls).Concat(enabledQModDlls)) {
                        if (args.Name.Contains(Path.GetFileNameWithoutExtension(dll.Name)))
                        {
                            logger.Info("Found assembly {assembly}", args.Name);
                            return Assembly.LoadFrom(dll.FullName);
                        }
                    }
                    logger.Info("Could not find assembly {assembly}", args.Name);
                    return null;
                };

                AppDomain.CurrentDomain.AssemblyLoad += delegate (object sender, AssemblyLoadEventArgs args)
                {
                    Assembly assembly = args.LoadedAssembly;
                    logger.Trace("Loaded Assembly {Assembly}", assembly.FullName);
                };

                patchedAssemblyLoading = true;
                logger.Info("Patched Assembly Loading");
            }
        }

        private static List<WrappedMod> ProcessOrder(Func<IManagedMod, Type[]> getBefore, Func<IManagedMod, Type[]> getAfter)
        {
            DependencyGraph<Type> dependencies = new DependencyGraph<Type>();
            // add nodes
            foreach (KeyValuePair<Type, WrappedMod> entry in ModManager.managedMods)
            {
                dependencies.AddNode(new DependencyGraph<Type>.Node {
                    value = entry.Key,
                    order = entry.Value.LoadOrder
                });
            }
            // add edges
            foreach (KeyValuePair<Type, WrappedMod> entry in ModManager.managedMods)
            {
                WrappedMod mod = entry.Value;
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
            foreach (DirectoryInfo subDir in subDirs)
            {
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
            }

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
            foreach (QMod mod in dependencies.OrderedQueue())
            {
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
            }
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

        /// <summary>
        /// Handle loading of Assembly by specified ModContainer
        /// </summary>
        /// <param name="modContainer"></param>
        /// <param name="assembly"></param>
        private static void ProcessLoadAssembly(ModContainer modContainer, Assembly assembly)
        {
            ModContents contents = modContainer.Contents;
            PublishedFileId_t workshopID = contents.m_WorkshopId;
            bool isWorkshop = workshopID != PublishedFileId_t.Invalid;
            string localString = isWorkshop ? "WORKSHOP" : "LOCAL";

            logger.Debug("Processing assembly {Assembly}", assembly.FullName);

            Type[] types = null;
            try
            {
                types = assembly.GetExportedTypes();
            }
            catch (System.Reflection.ReflectionTypeLoadException ex)
            {
                logger.Error("Failed to get types for {Assembly} - assuming dependency failure and reloading", assembly.FullName);
                Assembly reloadedAssembly = Assembly.Load(assembly.GetName());
                types = reloadedAssembly.GetExportedTypes();
            }

            foreach (Type type in types)
            {
                logger.Trace("Type Found: {Type}", type.FullName);
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
                        ManagedMod managedMod = ManagedMod.FromMod(type);
                        if (!(managedMod is null))
                        {
                            logger.Debug("Located MANAGED {Local} mod {Script} in mod {Mod} ({ModId})",
                                localString, type.Name, contents.ModName,
                                isWorkshop ? modContainer.ModID + " - " + workshopID.ToString() : modContainer.ModID);
                            wrappedMod = new WrappedMod(managedMod as IManagedMod, source);
                        }
                        else
                        {
                            logger.Debug("Located NON-MANAGED {Local} mod {Script} in mod {Mod} ({ModId})",
                                localString, type.Name, contents.ModName,
                                isWorkshop ? modContainer.ModID + " - " + workshopID.ToString() : modContainer.ModID);
                            wrappedMod = new WrappedMod((Activator.CreateInstance(type) as ModBase), source);
                        }
                        managedMods.Add(type, wrappedMod);
                        if (assembly.FullName.Contains("LegacyBlockLoader"))
                        {
                            LegacyBlockLoader = wrappedMod;
                        }
                    }
                    else
                    {
                        logger.Debug("Located DUPLICATE {Local} mod {Script} in mod {Mod} ({ModId}). Using first loaded in {LoadedLocal} {LoadedMod} ({LoadedModId})",
                            localString, type.Name, contents.ModName,
                            isWorkshop ? modContainer.ModID + " - " + workshopID.ToString() : modContainer.ModID,
                            wrappedMod.source.IsWorkshop ? "WORKSHOP" : "LOCAL", wrappedMod.source.Name,
                            wrappedMod.source.IsWorkshop ? wrappedMod.source.ID + " - " + wrappedMod.source.WorkshopID.ToString() : wrappedMod.source.ID);
                    }
                }
            }
        }

        /// <summary>
        /// Handle processing of all Official mods. Will detect everything in the mod list, determine dependency relationships based on reflection, and setup the load queue.
        /// </summary>
        /// <remarks>Assumes that all script mods have been loaded correctly (no bad .dll errors)</remarks>
        public static void ReprocessOfficialMods()
        {
            EarlyInitQueue = new List<WrappedMod>();
            InitQueue = new List<WrappedMod>();

            managedMods.Clear();
            assemblyMetadata.Clear();

            ModSessionInfo session = (ModSessionInfo) ReflectedManMods.m_CurrentSession.GetValue(Singleton.Manager<ManMods>.inst);
            Dictionary<string, ModContainer> mods = (Dictionary<string, ModContainer>)ReflectedManMods.m_Mods.GetValue(Singleton.Manager<ManMods>.inst);

            // Clear out any and all script mods that are present
            logger.Info("Reprocessing all mods in session");
            foreach (string key in session.Mods.Keys)
            {
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

                    foreach (FileInfo fileInfo in Directory.GetParent(modContainer.AssetBundlePath).EnumerateFiles())
                    {
                        // Ignore loading ModManager
                        if (fileInfo.Extension == ".dll" && !fileInfo.Name.Contains("ModManager"))
                        {
                            Assembly assembly = Assembly.LoadFrom(fileInfo.FullName);

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
                                ProcessLoadAssembly(modContainer, assembly);
                            }
                        }
                    }
                }
            }
            logger.Info("All mods reprocessed. Determining dependencies");

            // Process the correct load order of EarlyInits
            logger.Info("Building EarlyInit dependency graph");
            EarlyInitQueue = ProcessOrder((IManagedMod mod) => {return mod.earlyLoadBefore;}, (IManagedMod mod) => {return mod.earlyLoadAfter;});
            logger.Info("EarlyInit Mod Queue: ");
            foreach (WrappedMod mod in EarlyInitQueue)
            {
                logger.Info(" - {mod}", mod.Name);
            }

            // Process the correct load order of Inits
            logger.Info("Building Init dependency graph");
            InitQueue = ProcessOrder((IManagedMod mod) => { return mod.loadBefore; }, (IManagedMod mod) => { return mod.loadAfter; });
            logger.Info("Init Mod Queue: ");
            foreach (WrappedMod mod in InitQueue)
            {
                logger.Info(" - {mod}", mod.Name);
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
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
                    null,
                    new Type[] { typeof(Type) },
                    null
                );
            Type TechPhysicsReset = BlockInjector.LoadedAssembly.GetType("TechPhysicsReset");
            AddTechComponentToPrefab.Invoke(null, new object[] { TechPhysicsReset });
            logger.Debug("Patched TechPhysicsReset component");
            #endregion

            Type Patches = BlockLoader.GetNestedTypes(BindingFlags.Static | BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Where(type => type.Name == "Patches").First();
            #region 1.4.0.1+ Patches
            Type OfficialBlocks = Patches.GetNestedTypes(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Where(type => type.Name == "OfficialBlocks").First();
            MethodInfo Patch = OfficialBlocks.GetMethod(
                "Patch",
                    BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic,
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
                Type Projectile_UnlockColliderQuantity = Patches.GetNestedTypes(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).Where(type => type.Name == "Projectile_UnlockColliderQuantity").First();
                blockInjectorHarmony.Patch(typeof(Projectile).GetMethod("PrePool", BindingFlags.NonPublic | BindingFlags.Instance), null, null, transpiler: new HarmonyMethod(Projectile_UnlockColliderQuantity.GetMethod("Transpiler", BindingFlags.Static | BindingFlags.NonPublic)));
            }
            catch (Exception E)
            {
                logger.Error(E, "Error while patching custom blocks");
            }

            typeof(ManCustomSkins).GetMethod("Awake", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).Invoke(Singleton.Manager<ManCustomSkins>.inst, Array.Empty<object>());
            #endregion
            logger.Info("BlockInjector Patches complete");
        }
        private static void RunBlockInjector()
        {
            Type DirectoryBlockLoader = BlockInjector.LoadedAssembly.GetType("Nuterra.BlockInjector.DirectoryBlockLoader");
            MethodInfo method = DirectoryBlockLoader.GetMethod("LoadBlocks", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
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

        public static void ProcessEarlyInits()
        {
            // Process the EarlyInits
            logger.Info("Processing Early Inits");
            foreach (WrappedMod script in EarlyInitQueue)
            {
                logger.Trace("Processing EarlyInit for mod {}", script.Name);
                script.EarlyInit();
            }
        }

        public static void ProcessInits()
        {
            // Process the Inits
            logger.Info("Processing Inits");
            foreach (WrappedMod script in InitQueue)
            {
                logger.Trace("Processing Init for mod {}", script.Name);
                script.Init();
            }
        }
    }
}
