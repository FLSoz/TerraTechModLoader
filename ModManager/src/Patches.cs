using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Payload.UI.Commands.Steam;
using TerraTech.Network;
using Steamworks;


namespace ModManager
{
    public static class Patches
    {
        internal static bool RequiresRestart = false;
        internal const BindingFlags InstanceFlags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        internal const BindingFlags StaticFlags = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;

        [HarmonyPatch(typeof(JSONBlockLoader), "RegisterModuleLoader")]
        public static class PatchModuleRegistration
        {
            [HarmonyPrefix]
            internal static void Prefix(JSONModuleLoader loader)
            {
                ModManager.logger.Info($"Trying to register loader {loader.GetModuleKey()}");
            }
        }

        /// <summary>
        /// Patch module registration so that we only have modules from the current session available
        /// </summary>
        [HarmonyPatch(typeof(ManMods), "PurgeModdedContentFromGame")]
        public static class PatchModuleDeregistration
        {
            internal static readonly FieldInfo sLoaders = typeof(JSONBlockLoader).GetField("sLoaders", StaticFlags);
            [HarmonyPostfix]
            internal static void Postfix(ref ModSessionInfo oldSessionInfo)
            {
                if (oldSessionInfo != null)
                {
                    Dictionary<string, JSONModuleLoader> allLoaders = (Dictionary<string, JSONModuleLoader>) sLoaders.GetValue(null);
                    allLoaders.Clear();
                    VanillaModuleLoaders.RegisterVanillaModules();
                }
            }
        }

        /// <summary>
        /// Patch restarting game with TTSMM to use the requested mod session
        /// </summary>
        [HarmonyPatch(typeof(ManMods), "SessionRequiresRestart")]
        public static class PatchCheckNeedsRestart
        {

            [HarmonyPrefix]
            public static bool Prefix(ref bool __result)
            {
                if (!ModManager.LoadedWithProperParameters) {
                    RequiresRestart = true;
                    __result = false;
                    return false;
                }
                return true;
            }
        }

        internal static string GetTTSMMModList(ModSessionInfo modList)
        {
            ModSessionInfo session = modList;

            if (session == null)
            {
                session = (ModSessionInfo)ReflectedManMods.m_CurrentSession.GetValue(Singleton.Manager<ManMods>.inst);
            }
            StringBuilder sb = new StringBuilder();
            if (session != null)
            {
                foreach (ModContainer modContainer in session)
                {
                    if (modContainer != null)
                    {
                        if (!modContainer.IsRemote && !modContainer.Local && (ulong) modContainer.Contents.m_WorkshopId > 0)
                        {
                            ModManager.logger.Info($"Found workshop mod {modContainer.ModID} with workshop ID {modContainer.Contents.m_WorkshopId}");
                            sb.Append($"[workshop:{modContainer.Contents.m_WorkshopId}],");
                        }
                        else
                        {
                            string path = modContainer.AssetBundlePath;
                            if (modContainer.Local)
                            {
                                string directoryPath = Path.GetDirectoryName(path);
                                string name = new DirectoryInfo(directoryPath).Name;
                                string sanitizedName = name.Replace(" ", ":/%20");
                                sb.Append($"[local:{sanitizedName}],");
                                if (sanitizedName != name)
                                {
                                    ModManager.logger.Warn($"Replacing bad Local Mods directory path {name} with sanitized version {sanitizedName}");
                                }
                            }
                            else
                            {
                                ModManager.logger.Warn($"Unable to add remote mod at {modContainer.AssetBundlePath} with workshop ID {modContainer.Contents.m_WorkshopId}");
                            }
                        }
                    }
                }
            }
            return sb.ToString().Trim(',');
        }

        internal static string GetCurrentArgs()
        {
            string[] currentArgs = CommandLineReader.GetCommandLineArgs();
            StringBuilder sb = new StringBuilder(" ");

            bool ignoreParameter = false;
            for (int i = 1; i < currentArgs.Length; i++)
            {
                string currentArg = currentArgs[i];
                if (currentArg == "+connect_lobby")
                {
                    ignoreParameter = true;
                }
                else if (currentArg == "+custom_mod_list")
                {
                    ignoreParameter = true;
                }
                else if (currentArg == "+ttsmm_mod_list")
                {
                    ignoreParameter = true;
                }
                else if (ignoreParameter)
                {
                    ignoreParameter = false;
                }
                else
                {
                    sb.Append(" " + currentArg);
                }
            }

            return sb.ToString().Trim();
        }

        internal static string GetExecutablePath()
        {
            string attempt = Process.GetCurrentProcess().StartInfo.FileName;
            if (attempt != null && attempt.Trim().Length > 0)
            {
                ModManager.logger.Info($"Process filename found: {attempt}. Using args[0] {ModManager.ExecutablePath} anyway because I don't trust it");
            }
            return ModManager.ExecutablePath;
        }

        /// <summary>
        /// Patch restarting game with TTSMM to use the requested mod session
        /// We will also refresh the snapshot cache
        /// </summary>
        [HarmonyPatch(typeof(ManMods), "InjectModdedContentIntoGame")]
        public static class PatchForceGameRestart
        {

            [HarmonyPostfix]
            public static void Postfix(ModSessionInfo newSessionInfo)
            {
                if (RequiresRestart)
                {
                    new Process
                    {
                        StartInfo =
                        {
                            FileName = GetExecutablePath(),
                            Arguments = string.Format("{0} +custom_mod_list {1} +ttsmm_mod_list {2}", GetCurrentArgs(), "[:2655051786]", GetTTSMMModList(newSessionInfo))
                        }
                    }.Start();
                    Application.Quit();
                }
                else
                {
                    ModManager.logger.Info("Recalculating snapshot cache");
                    IEnumerator snapshotIterator = Singleton.Manager<ManSnapshots>.inst.UpdateSnapshotCacheOnStartup();
                    while (snapshotIterator.MoveNext())
                    {
                        // iterate over snapshots
                    }
                    ModManager.logger.Info("Recalculated snapshot cache");
                }
            }
        }

        /// <summary>
        /// Patch restarting game with TTSMM to use the requested mod session
        /// </summary>
        [HarmonyPatch(typeof(ManMods), "RequestRestartGame")]
        public static class PatchGameRestart
        {
            [HarmonyPrefix]
            public static bool Prefix(ModSessionInfo modList, TTNetworkID lobbyID)
            {
                new Process
                {
                    StartInfo =
                    {
                        FileName = GetExecutablePath(),
                        Arguments = string.Format("{0} +connect_lobby {1} +custom_mod_list {2} +ttsmm_mod_list {3}", GetCurrentArgs(), lobbyID.m_NetworkID, "[:2655051786]", GetTTSMMModList(modList))
                    }
                }.Start();
                Application.Quit();
                return false;
            }
        }

        /// <summary>
        /// Patch InitModScripts to do all EarlyInits, and Inits, in our specified order.
        /// </summary>
        /// <remarks>
        /// It is guaranteed that EarlyInit runs on every mod, before every InitModScripts, so we replicate that behaviour here
        /// </remarks>
        [HarmonyPatch(typeof(ManMods), "InitModScripts")]
        public static class PatchModScriptLoading
        {
            private static bool firstPass = false;

            [HarmonyPrefix]
            public static bool Prefix()
            {
                ModManager.logger.Info("InitModScripts Hook called");

                // If we didn't load with the proper parameters, forcibly reprocess everything
                // We don't expect this to be called at all
                if (!ModManager.LoadedWithProperParameters)
                {
                    ModManager.logger.Fatal("LOADED WITHOUT PROPER PARAMETERS, AND MADE IT TO InitModScripts");
                }

                // The reprocessing is to pick up all new .dlls in this step (so we never have broken assemblies when all dependencies are present)
                ModManager.ReprocessOfficialMods();

                ModManager.logger.Info("All mods reprocessed. Determining dependencies");
                ModManager.ReprocessInitializationOrder();

                ModManager.PatchCustomBlocksIfNeeded();

                ModManager.ProcessEarlyInits();
                ModManager.ProcessInits();
                ModManager.logger.Info("InitModScripts End");
                return false;
            }
        }

        // Notes on mod loading process:
        // Mod ID is enforced unique string based on key inside ManMods.m_Mods
        // *Every* local mod is always processed, and while content may be removed, the ModContainer and key remain in m_Mods
        // Workshop mods do not initially exist inside m_Mods, but will attempt to add themselves after they are downloaded.
        // This has the side effect that any Local mod will *always* override the corresponding workshop item

        // In base game, only one ModBase is allowed per ModContainer, and it will always be the last one present.
        // This allows for a kind of "ghetto compatibility", where multiple different mod .dlls are present, but only the last one will be used.
        // So, if you want your mod to have additional features if another mod is also used, then you can output two .dlls - one without the extra features, and one with them
        //  - In case user does not have the required .dll for extra features, the .dll without them will be loaded, whereas the other one will fail to load, and nothing will happen
        //  - In case user does, if filename of the one without appears later in ASCII-betical order (or whatever order Directory.GetParent(container.AssetBundlePath).EnumerateFiles() uses),
        //    then both will be loaded, but only the one that appears later will have its Init, EarlyInit, and DeInit hooks called.
        //    Note that this does not solve type collisions, where something like NuterraSteam tries to find an appropriate module based on type name

        // This particular feature is no longer possible when using this mod, as we explicitly attempt to load every single .dll, and allow for multiple ModBase objects per ModContainer
        // TODO: implement .json config handling to replicate that (we can configure it to only load certain .dll files, based on a set of criteria that are met)


        /// <summary>
        /// Replace ProcessLoadingMod with our own IENumerator, where we don't load assembly files. We will handle all of that later, once all .dlls are guaranteed loaded
        /// </summary>
        /// <remarks>
        /// Has the desired side effect that modContainer.Script and modContents.script will not be set, so only we will be called via DeInitModScripts and EarlyInit. EarlyInit does nothing, and InitModScripts has been overriden by us
        /// </remarks>
        [HarmonyPatch(typeof(ManMods), "ProcessLoadingMod")]
        public static class PatchLoadingAssembly
        {
            // Basic replacement code taken from this Harmony example: https://gist.github.com/pardeike/c873b95e983e4814a8f6eb522329aee5
            class CustomEnumerator : IEnumerable<float>
            {
                public ModContainer container;

                private float Scale(float subProgress, float lower, float upper)
                {
                    return Mathf.Lerp(lower, upper, subProgress);
                }

                public IEnumerator<float> GetEnumerator()
                {
                    AssetBundleCreateRequest createRequest = AssetBundle.LoadFromFileAsync(container.AssetBundlePath);
                    while (!createRequest.isDone)
                    {
                        yield return this.Scale(createRequest.progress, 0f, 0.2f);
                    }
                    AssetBundle bundle = createRequest.assetBundle;
                    if (bundle == null)
                    {
                        ModManager.logger.Error("Load AssetBundle at path {Path} failed for mod {Mod}", container.AssetBundlePath, container.ModID);
                        container.OnLoadFailed();
                        yield return 1f;
                    }
                    AssetBundleRequest loadRequest = bundle.LoadAssetAsync<ModContents>("Contents.asset");
                    while (!loadRequest.isDone)
                    {
                        yield return this.Scale(loadRequest.progress, 0.25f, 0.4f);
                    }
                    ModContents contents = loadRequest.asset as ModContents;
                    if (contents == null)
                    {
                        ModManager.logger.Error("Load AssetBundle Contents.asset failed for mod {Mod}", container.ModID);
                        container.OnLoadFailed();
                        yield return 1f;
                    }
                    if (contents.m_Corps.Count > 0)
                    {
                        int i;
                        for (int corpIndex = 0; corpIndex < contents.m_Corps.Count; corpIndex = i + 1)
                        {
                            container.RegisterAsset(contents.m_Corps[corpIndex]);
                            yield return this.Scale((float)(corpIndex / contents.m_Corps.Count), 0.4f, 0.5f);
                            i = corpIndex;
                        }
                    }
                    if (contents.m_Skins.Count > 0)
                    {
                        int i;
                        for (int corpIndex = 0; corpIndex < contents.m_Skins.Count; corpIndex = i + 1)
                        {
                            container.RegisterAsset(contents.m_Skins[corpIndex]);
                            yield return this.Scale((float)(corpIndex / contents.m_Skins.Count), 0.5f, 0.75f);
                            i = corpIndex;
                        }
                    }
                    if (contents.m_Blocks.Count > 0)
                    {
                        int i;
                        for (int corpIndex = 0; corpIndex < contents.m_Blocks.Count; corpIndex = i + 1)
                        {
                            container.RegisterAsset(contents.m_Blocks[corpIndex]);
                            yield return this.Scale((float)(corpIndex / contents.m_Blocks.Count), 0.6f, 0.8f);
                            i = corpIndex;
                        }
                    }
                    yield return 0.9f;
                    container.OnLoadComplete(contents);
                    yield return 1f;
                    yield break;
                }

                IEnumerator<float> IEnumerable<float>.GetEnumerator()
                {
                    return GetEnumerator();
                }

                System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
                {
                    return GetEnumerator();
                }
            }

            [HarmonyPostfix]
            static void Postfix(ref IEnumerator<float> __result, ModContainer container)
            {
                var myEnumerator = new CustomEnumerator()
                {
                    container = container
                };
                __result = myEnumerator.GetEnumerator();
            }
        }

        /// <summary>
        /// Make an exception for us, so that we are always included in Multiplayer. Not needed right now, since this mod never has its patches removed
        /// </summary>
        [HarmonyPatch(typeof(ManMods), "AutoAddModsToSession")]
        public static class PatchMultiplayerModSession
        {
        }

        // Patch workshop loading to make sure all dependencies are loaded first
        // Also use our workflow to circumvent callback missed errors. Can only have it fail on first load now.
        [HarmonyPatch(typeof(ManMods), "LoadWorkshopData")]
        public static class PatchWorkshopLoad
        {
            [HarmonyPrefix]
            public static bool Prefix(SteamDownloadItemData item, bool remote)
            {
                WorkshopLoader.LoadWorkshopMod(item, remote);
                return false;
            }
        }

        // Patch the loading screen to show more progress
        [HarmonyPatch(typeof(UILoadingScreenModProgress), "Update")]
        public static class PatchLoadingBar
        {
            [HarmonyPrefix]
            public static bool Prefix(ref UILoadingScreenModProgress __instance)
            {
                if (ModManager.CurrentOperation != null)
                {
                    __instance.loadingBar.SetActive(true);
                    __instance.loadingProgressText.text = $"{ModManager.CurrentOperation}\n{ModManager.CurrentOperationSpecifics} - {ModManager.CurrentOperationProgress}%";
                    __instance.loadingProgressImage.fillAmount = ModManager.CurrentOperationProgress;
                    return false;
                }
                return true;
            }
        }

        // Patches carried on from TTMM
        internal static class TTMMPatches
        {
            [HarmonyLib.HarmonyPatch(typeof(UIScreenBugReport), "Set")]
            internal static class UIScreenBugReport_Set
            {
                internal static void Postfix(UIScreenBugReport __instance)
                {
                    if (ModManager.EnableTTQMMHandling)
                    {
                        typeof(UIScreenBugReport).GetField("m_ErrorCatcher", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(__instance, false);
                    }
                }
            }

            [HarmonyLib.HarmonyPatch(typeof(UIScreenBugReport), "Post")]
            internal static class UIScreenBugReport_Post
            {
                internal static bool Prefix(UIScreenBugReport __instance)
                {
                    if (ModManager.EnableTTQMMHandling)
                    {
                        ManUI.inst.ShowErrorPopup("In-game bug reporting has been disabled for modded clients");
                        __instance.ExitScreen();
                        return false;
                    }
                    return true;
                }
            }

            [HarmonyLib.HarmonyPatch(typeof(TerraTech.Network.LobbySystem), "GetInstalledModsHash")]
            internal static class LobbySystem_GetInstalledModsHash
            {
                internal static void Postfix(ref int __result)
                {
                    if (ModManager.EnableTTQMMHandling)
                    {
                        __result = 0x7AC0BE11;
                    }
                    else
                    {
                        __result = 0x000F1502;
                    }
                }
            }
        }
    }
}
