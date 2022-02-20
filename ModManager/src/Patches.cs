using System;
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
        /// <summary>
        /// Patch Lobby filtering to only show lobbies that also have 0ModManager
        /// </summary>
        [HarmonyPatch(typeof(LobbySystem.LobbyFilterOptions), "isLobbyAcceptable")]
        public static class PatchMPLobbies
        {
            [HarmonyPostfix]
            public static void Postfix(ref bool __result, LobbyData ld)
            {
                if (__result)
                {
                    PublishedFileId_t[] workshopIds = ld.m_WorkshopIds;
                    // This mod is present here: https://steamcommunity.com/sharedfiles/filedetails/?id=2655051786
                    if (!workshopIds.Contains(new PublishedFileId_t(2655051786))) {
                        __result = false;
                    }
                }
            }
        }

        /// <summary>
        /// Patch restarting game with TTSMM to use the requested mod session
        /// </summary>
        [HarmonyPatch(typeof(ManMods), "RequestRestartGame")]
        public static class PatchGameRestart
        {
            internal static string GetTTSMMModList(ModSessionInfo modList)
            {
                ModSessionInfo session = modList;
                
                if (session == null)
                {
                    session = (ModSessionInfo) ReflectedManMods.m_CurrentSession.GetValue(Singleton.Manager<ManMods>.inst);
                }
                StringBuilder sb = new StringBuilder();
                if (session != null)
                {
                    foreach (ModContainer modContainer in session)
                    {
                        if (modContainer != null && !modContainer.IsRemote)
                        {
                            sb.Append($"[workshop:{modContainer.Contents.m_WorkshopId}],");
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
                for (int i = 0; i < currentArgs.Length; i++)
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

            [HarmonyPrefix]
            public static bool Prefix(ModSessionInfo modList, TTNetworkID lobbyID)
            {
                new Process
                {
                    StartInfo =
                    {
                        FileName = Process.GetCurrentProcess().StartInfo.FileName,
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

                // We are assuming this is a Local Mod
                // It will remain as such until proper dependency management of .dlls is introduced for Official Mods
                ModManager.ReprocessOfficialMods();

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
                        ModManager.logger.Error("Load AssetBundle failed for mod {Mod}", container.ModID);
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
                }
            }
        }
    }
}
