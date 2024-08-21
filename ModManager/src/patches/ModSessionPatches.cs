using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using HarmonyLib;
using TerraTech.Network;
using Steamworks;
using NLog;
using static CompoundExpression;

namespace ModManager.patches
{
    internal static class ModSessionPatches
    {

        // Patch mod session switching
        [HarmonyPatch(typeof(ManMods), "RequestModSession")]
        public static class PatchLoadingState
        {
            [HarmonyPostfix]
            internal static void Postfix(bool isMultiplayer)
            {
                if (isMultiplayer)
                {
                    Lobby lobby = Singleton.Manager<ManNetworkLobby>.inst.LobbySystem.CurrentLobby;
                    if (lobby != null && !lobby.IsLobbyOwner() && CommandLineReader.GetArgument("+connect_lobby") == null)
                    {
                        // If you are joining a lobby, and you are not the lobby owner, and the game hasn't been restarted with the +connect_lobby custom parameters, then you must restart with the proper parameters
                        ModManager.LoadedWithProperParameters = false;
                        return;
                    }
                }
            }
        }

        /// <summary>
        /// Patch module registration so that we only have modules from the current session available
        /// </summary>
        [HarmonyPatch(typeof(ManMods), "PurgeModdedContentFromGame")]
        public static class PatchModuleDeregistration
        {
            internal static readonly FieldInfo sLoaders = AccessTools.Field(typeof(JSONBlockLoader), "sLoaders");
            [HarmonyPostfix]
            internal static void Postfix(ModSessionInfo oldSessionInfo)
            {
                if (oldSessionInfo != null)
                {
                    Dictionary<string, JSONModuleLoader> allLoaders = (Dictionary<string, JSONModuleLoader>)sLoaders.GetValue(null);
                    allLoaders.Clear();
                    ModManager.logger.Info("🗑️ Purged modded JSONLoaders, reloading vanilla loaders");
                    VanillaModuleLoaders.RegisterVanillaModules();
                }
            }
        }

        /// <summary>
        /// Patch restarting game with TTSMM to use the requested mod session
        /// We will also refresh the snapshot cache
        /// </summary>
        [HarmonyPatch(typeof(ManMods), "InjectModdedContentIntoGame")]
        public static class PatchContentInjection
        {
            internal static void PrintDictionary<T1, T2>(Dictionary<T1, T2> dictionary, StringBuilder sb)
            {
                sb.AppendLine("{");
                if (dictionary != null)
                {
                    foreach (KeyValuePair<T1, T2> pair in dictionary)
                    {
                        sb.AppendLine($"\t{pair.Key}: {pair.Value}");
                    }
                }
                sb.AppendLine("}");
            }

            internal static void PrintSessionInfo(ModSessionInfo sessionInfo)
            {
                ModManager.logger.Trace("Printing session info:");
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("Mods:");
                PrintDictionary(sessionInfo.Mods, sb);

                sb.AppendLine("Corp IDs:");
                PrintDictionary(sessionInfo.CorpIDs, sb);

                sb.AppendLine("Skin IDs:");
                PrintDictionary(sessionInfo.SkinIDs, sb);

                sb.AppendLine("Block IDs:");
                PrintDictionary(sessionInfo.BlockIDs, sb);

                ModManager.logger.Trace(sb.ToString());
            }

            [HarmonyPrefix]
            public static void Prefix(ModSessionInfo newSessionInfo)
            {
                PrintSessionInfo(newSessionInfo);
            }

            [HarmonyPostfix]
            public static void Postfix(ModSessionInfo newSessionInfo)
            {
                ModManager.logger.Info("📸 Recalculating snapshot cache");
                IEnumerator snapshotIterator = Singleton.Manager<ManSnapshots>.inst.UpdateSnapshotCacheOnStartup();
                while (snapshotIterator.MoveNext())
                {
                    // iterate over snapshots
                }
                ModManager.logger.Info("🏁 Recalculated snapshot cache");

                PrintSessionInfo(newSessionInfo);
            }
        }


        [HarmonyPatch(typeof(ModSessionInfo), "Write")]
        public static class PatchModSessionWriting
        {
            [HarmonyPrefix]
            public static void PatchLogSession(ModSessionInfo __instance)
            {
                PatchContentInjection.PrintSessionInfo(__instance);
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
                throw new Exception("InitModScripts WAS CALLED");

                /*
                // If we didn't load with the proper parameters, forcibly reprocess everything
                // We don't expect this to be called at all
                if (!ModManager.LoadedWithProperParameters)
                {
                    ModManager.logger.Fatal("LOADED WITHOUT PROPER PARAMETERS, AND MADE IT TO InitModScripts");
                }

                // The reprocessing is to pick up all new .dlls in this step (so we never have broken assemblies when all dependencies are present)
                ModSessionInfo session = (ModSessionInfo)ReflectedManMods.m_CurrentSession.GetValue(Singleton.Manager<ManMods>.inst);
                IEnumerator<float> iterator = ModManager.ReprocessOfficialMods(session);
                while (iterator.MoveNext()) { }

                ModManager.logger.Info("All mods reprocessed. Determining dependencies");
                ModManager.ReprocessModOrders(session);

                ModManager.PatchCustomBlocksIfNeeded();

                ModManager.ProcessEarlyInits();
                ModManager.ProcessInits();
                ModManager.logger.Info("InitModScripts End");
                */
                return false;
            }
        }

        [HarmonyPatch(typeof(ManMods), "UpdateModSession")]
        public static class PatchModSession
        {
            internal static void AutoAddModsToSession(ManMods __instance, ModSessionInfo session)
            {
                if (session != null)
                {
                    Dictionary<string, ModContainer> mods = (Dictionary<string, ModContainer>)ReflectedManMods.m_Mods.GetValue(__instance);
                    using (Dictionary<string, ModContainer>.Enumerator enumerator = mods.GetEnumerator())
                    {
                        while (enumerator.MoveNext())
                        {
                            KeyValuePair<string, ModContainer> pair = enumerator.Current;
                            string modID = pair.Key;
                            ModContainer container = pair.Value;
                            bool local = container.Contents == null || container.Contents.m_WorkshopId == PublishedFileId_t.Invalid;
                            // If it's multiplayer, we allow remote mods. Else, we disallow remote mods
                            if (container.IsLoaded && !session.Mods.ContainsKey(modID) && (!session.m_Multiplayer || !local))
                            {
                                PublishedFileId_t id = PublishedFileId_t.Invalid;
                                if (container.Contents != null)
                                {
                                    id = container.Contents.m_WorkshopId;
                                }
                                ModManager.logger.Debug($" 📦 Adding mod {modID} to session");
                                session.Mods.Add(modID, id.m_PublishedFileId);
                            }
                        }
                        return;
                    }
                }
                ModManager.logger.Error("❌ Called AutoAddModsToSession for null session");
            }

            [HarmonyPrefix]
            internal static bool Prefix(ManMods __instance)
            {
                ReflectedManMods.CheckReparseAllJsons.Invoke(__instance, null);
                ModSessionInfo requestedSession = (ModSessionInfo)ReflectedManMods.m_RequestedSession.GetValue(__instance);
                ModSessionInfo currentSession = (ModSessionInfo)ReflectedManMods.m_CurrentSession.GetValue(__instance);
                bool loadingRequestedSessionInProgress = (bool)ReflectedManMods.m_LoadingRequestedSessionInProgress.GetValue(__instance);
                if (requestedSession != null && (loadingRequestedSessionInProgress || requestedSession != currentSession))
                {
                    if (!loadingRequestedSessionInProgress)
                    {
                        if (
                            (bool)ReflectedManMods.m_AutoAddModsToAuthoritativeSessions.GetValue(__instance) &&
                            requestedSession.m_Authoritative &&
                            Singleton.Manager<ManGameMode>.inst.GetCurrentGameType() != ManGameMode.GameType.Gauntlet &&
                            Singleton.Manager<ManGameMode>.inst.GetCurrentGameType() != ManGameMode.GameType.SumoShowdown &&
                            Singleton.Manager<ManGameMode>.inst.GetCurrentGameType() != ManGameMode.GameType.RacingChallenge &&
                            Singleton.Manager<ManGameMode>.inst.GetCurrentGameType() != ManGameMode.GameType.FlyingChallenge
                        )
                        {
                            // This normally adds all the global mods to the session. We don't bother doing this since we know the session will be the same
                            if (ModManager.CurrentSessionLoaded && (currentSession.m_Multiplayer == requestedSession.m_Multiplayer))
                            {
                                ModManager.logger.Info("✔️ Mod session remaining the same");
                                ReflectedManMods.m_RequestedSession.SetValue(__instance, null);
                                __instance.ModSessionLoadCompleteEvent.Send();
                            }
                            else
                            {
                                // Either we're switching to or from multiplayer, or the current session is not loaded
                                ModManager.logger.Info("🕓 Determining mod session");
                                AutoAddModsToSession(__instance, requestedSession);
                                ReflectedManMods.m_LoadingRequestedSessionInProgress.SetValue(__instance, true);
                                ModManager.CurrentSessionLoaded = false;
                                ModManager.CurrentOperation = "Purging";
                                ModManager.contentLoader.Start(currentSession, requestedSession);

                                PatchContentInjection.PrintSessionInfo(requestedSession);
                            }
                        }
                        else
                        {
                            ReflectedManMods.m_LoadingRequestedSessionInProgress.SetValue(__instance, true);
                            ModManager.logger.Info("🔀 Switching mod session");
                            ModManager.CurrentSessionLoaded = false;
                            ModManager.CurrentOperation = "Purging";
                            ModManager.contentLoader.Start(currentSession, requestedSession);
                        }
                    }
                    loadingRequestedSessionInProgress = (bool)ReflectedManMods.m_LoadingRequestedSessionInProgress.GetValue(__instance);
                    if (loadingRequestedSessionInProgress)
                    {
                        if (!__instance.HasPendingLoads())
                        {
                            ModdedContentLoader loader = ModManager.contentLoader;
                            if (loader.InjectModdedContent())
                            {
                                ReflectedManMods.m_RequestedSession.SetValue(__instance, null);
                                ReflectedManMods.m_LoadingRequestedSessionInProgress.SetValue(__instance, false);
                                ModManager.CurrentSessionLoaded = true;
                                loader.Finish();

                                __instance.ModSessionLoadCompleteEvent.Send();
                                PatchContentInjection.Postfix(requestedSession);
                            }
                        }
                        else
                        {
                            ModManager.logger.Trace("Pending loads detected:");
                            ModManager.logger.Trace($"  Currently loading: [{ReflectedManMods.m_CurrentlyLoading.GetValue(__instance)}]");
                            Queue<string> pendingLoads = (Queue<string>) ReflectedManMods.m_PendingLoads.GetValue(__instance);
                            ModManager.logger.Trace($"  Pending loads");
                            foreach (string pendingLoad in pendingLoads)
                            {
                                ModManager.logger.Trace($"    - {pendingLoad}");
                            }
                        }
                    }
                }
                else if (loadingRequestedSessionInProgress)
                {
                    ModManager.logger.Error("LOAD REQUESTED WHILE REQUESTED SESSION IS NULL");
                }
                return false;
            }
        }

        [HarmonyPatch(typeof(ManMods), "Update")]
        public static class PatchPendingLoadsLog
        {
            public static void Postfix(ManMods __instance)
            {
                if (__instance.HasPendingLoads())
                {
                    ModManager.logger.Trace("Pending loads detected:");
                    ModManager.logger.Trace($"  Currently loading: [{ReflectedManMods.m_CurrentlyLoading.GetValue(__instance)}]");
                    Queue<string> pendingLoads = (Queue<string>)ReflectedManMods.m_PendingLoads.GetValue(__instance);
                    ModManager.logger.Trace($"  Pending loads");
                    foreach (string pendingLoad in pendingLoads)
                    {
                        ModManager.logger.Trace($"    - {pendingLoad}");
                    }
                }
                if (__instance.IsPollingWorkshop())
                {
                    ModManager.logger.Trace("Waiting on workshop detected:");
                    List<PublishedFileId_t> waitingOnDownload = (List<PublishedFileId_t>)ReflectedManMods.m_WaitingOnDownloads.GetValue(__instance);
                    ModManager.logger.Trace($"  Waiting on workshop polls for:");
                    foreach (PublishedFileId_t workshopId in waitingOnDownload)
                    {
                        ModManager.logger.Trace($"    - {workshopId}");
                    }
                    ModManager.logger.Trace($"  Waiting on workshop check: {ReflectedManMods.m_WaitingOnWorkshopCheck.GetValue(__instance)}");
                }
            }
        }

        /// <summary>
        /// Make an exception for us, so that we are always included in Multiplayer. Not needed right now, since this mod never has its patches removed
        /// </summary>
        [HarmonyPatch(typeof(ManMods), "AutoAddModsToSession")]
        public static class PatchMultiplayerModSession
        {
        }

        // Patch the loading screen to show more progress
        [HarmonyPatch(typeof(UILoadingScreenModProgress), "Update")]
        public static class PatchLoadingBar
        {
            [HarmonyPrefix]
            public static bool Prefix(UILoadingScreenModProgress __instance)
            {
                if (!ModManager.CurrentSessionLoaded)
                {
                    __instance.loadingBar.SetActive(true);
                    ModManager.OperationDetails details = ModManager.GetCurrentOperation();
                    if (details.Name != null)
                    {
                        __instance.loadingProgressText.text = $"{details.Name} - {(int)(100 * details.Progress)}%\n{details.Specifics}";
                        __instance.loadingProgressImage.fillAmount = details.Progress;
                        return false;
                    }
                    return true;
                }
                return true;
            }
        }

        [HarmonyPatch(typeof(JSONBlockLoader), "RegisterModuleLoader")]
        public static class PatchModuleRegistration
        {
            [HarmonyPrefix]
            internal static void Prefix(JSONModuleLoader loader)
            {
                ModManager.logger.Info($"🗳️ Trying to register loader {loader.GetModuleKey()}");
            }
        }

        [HarmonyPatch(typeof(ManMods), "UpdateModScripts")]
        public static class PatchScriptUpdate
        {
            [HarmonyPrefix]
            public static bool Prefix()
            {
                ModManager.ProcessUpdate();
                return false;
            }
        }

        [HarmonyPatch(typeof(ManMods), "FixedUpdateModScripts")]
        public static class PatchScriptFixedUpdate
        {
            [HarmonyPrefix]
            public static bool Prefix()
            {
                ModManager.ProcessFixedUpdate();
                return false;
            }
        }
    }
}
