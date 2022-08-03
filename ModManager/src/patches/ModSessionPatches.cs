using System;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using HarmonyLib;
using TerraTech.Network;
using Steamworks;

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
                ModManager.logger.Info("Recalculating snapshot cache");
                IEnumerator snapshotIterator = Singleton.Manager<ManSnapshots>.inst.UpdateSnapshotCacheOnStartup();
                while (snapshotIterator.MoveNext())
                {
                    // iterate over snapshots
                }
                ModManager.logger.Info("Recalculated snapshot cache");

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
                            bool local = pair.Value.Contents.m_WorkshopId == PublishedFileId_t.Invalid;
                            // If it's multiplayer, we allow remote mods. Else, we disallow remote mods
                            if (pair.Value.IsLoaded && !session.Mods.ContainsKey(modID) && (!session.m_Multiplayer || !local))
                            {
                                ModManager.logger.Debug($"Adding mod {modID} to session");
                                session.Mods.Add(modID, pair.Value.Contents.m_WorkshopId.m_PublishedFileId);
                            }
                        }
                        return;
                    }
                }
                ModManager.logger.Error("Called AutoAddModsToSession for null session");
            }

            internal enum ModLoadStage
            {
                NotLoaded,
                ReprocessMods,
                EarlyInit,
                Init,
                Corps,
                Skins,
                Blocks,
                Done
            }

            internal static ModLoadStage CurrentStage = ModLoadStage.NotLoaded;
            internal static IEnumerator CurrentProcess = null;

            [HarmonyPrefix]
            internal static bool Prefix(ref ManMods __instance)
            {
                ReflectedManMods.CheckReparseAllJsons.Invoke(__instance, null);
                ModSessionInfo requestedSession = (ModSessionInfo)ReflectedManMods.m_RequestedSession.GetValue(__instance);
                ModSessionInfo currentSession = (ModSessionInfo)ReflectedManMods.m_CurrentSession.GetValue(__instance);
                if (requestedSession != null && requestedSession != currentSession)
                {
                    bool loadingRequestedSessionInProgress = (bool)ReflectedManMods.m_LoadingRequestedSessionInProgress.GetValue(__instance);
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
                                ModManager.logger.Info("Mod session remaining the same");
                                ReflectedManMods.m_RequestedSession.SetValue(__instance, null);
                            }
                            else
                            {
                                // Either we're switching to or from multiplayer, or the current session is not loaded
                                ModManager.logger.Info("Determining mod session");
                                AutoAddModsToSession(__instance, requestedSession);
                                CurrentStage = ModLoadStage.NotLoaded;
                                CurrentProcess = null;
                                ReflectedManMods.m_LoadingRequestedSessionInProgress.SetValue(__instance, true);
                                ModManager.CurrentSessionLoaded = false;
                            }
                        }
                        else
                        {
                            CurrentStage = ModLoadStage.NotLoaded;
                            CurrentProcess = null;
                            ReflectedManMods.m_LoadingRequestedSessionInProgress.SetValue(__instance, true);
                            ModManager.logger.Info("Switching mod session");
                            ModManager.CurrentSessionLoaded = false;
                        }
                    }
                    loadingRequestedSessionInProgress = (bool)ReflectedManMods.m_LoadingRequestedSessionInProgress.GetValue(__instance);
                    if (loadingRequestedSessionInProgress && !__instance.HasPendingLoads())
                    {
                        switch (CurrentStage)
                        {
                            case ModLoadStage.NotLoaded:
                                {
                                    if (requestedSession.m_Authoritative)
                                    {
                                        Dictionary<string, string> corpModLookup = new Dictionary<string, string>();
                                        Dictionary<string, List<string>> corpSkinLookup = new Dictionary<string, List<string>>();
                                        List<string> moddedBlocks = new List<string>();
                                        foreach (KeyValuePair<string, ModContainer> pair in (Dictionary<string, ModContainer>)ReflectedManMods.m_Mods.GetValue(__instance))
                                        {
                                            string modID = pair.Key;
                                            if (!pair.Value.IsRemote && requestedSession.Mods.ContainsKey(modID))
                                            {
                                                foreach (ModdedCorpDefinition moddedCorpDefinition in pair.Value.Contents.m_Corps)
                                                {
                                                    if (!corpModLookup.ContainsKey(moddedCorpDefinition.name))
                                                    {
                                                        ModManager.logger.Debug("Found corp {corp} ({short}) in mod {mod}", moddedCorpDefinition.name, moddedCorpDefinition.m_ShortName, modID);
                                                        corpModLookup.Add(moddedCorpDefinition.name, modID);
                                                    }
                                                    else
                                                    {
                                                        ModManager.logger.Warn(
                                                            "Failed to add duplicate corp {corp} from mod {key} because we already have one from mod {mod}",
                                                            moddedCorpDefinition.name,
                                                            modID,
                                                            corpModLookup[moddedCorpDefinition.name]
                                                        );
                                                    }
                                                }
                                                foreach (ModdedSkinDefinition moddedSkinDefinition in pair.Value.Contents.m_Skins)
                                                {
                                                    if (!corpSkinLookup.ContainsKey(moddedSkinDefinition.m_Corporation))
                                                    {
                                                        corpSkinLookup[moddedSkinDefinition.m_Corporation] = new List<string>();
                                                    }
                                                    ModManager.logger.Debug("Found skin {skin} for corp {corp}", moddedSkinDefinition.name, moddedSkinDefinition.m_Corporation);
                                                    corpSkinLookup[moddedSkinDefinition.m_Corporation].Add(ModUtils.CreateCompoundId(modID, moddedSkinDefinition.name));
                                                }
                                                foreach (ModdedBlockDefinition moddedBlockDefinition in pair.Value.Contents.m_Blocks)
                                                {
                                                    ModManager.logger.Trace("Found modded block {block} in mod {mod}", moddedBlockDefinition.name, modID);
                                                    moddedBlocks.Add(ModUtils.CreateCompoundId(modID, moddedBlockDefinition.name));
                                                }
                                            }
                                        }
                                        List<string> moddedCorps = new List<string>(corpModLookup.Count);
                                        foreach (KeyValuePair<string, string> keyValuePair2 in corpModLookup)
                                        {
                                            moddedCorps.Add(ModUtils.CreateCompoundId(keyValuePair2.Value, keyValuePair2.Key));
                                        }
                                        ReflectedManMods.AutoAssignSessionIDs.Invoke(__instance, new object[] { requestedSession, moddedCorps, corpSkinLookup, moddedBlocks });
                                    }
                                    ModManager.logger.Debug("Purging modded content");
                                    ReflectedManMods.PurgeModdedContentFromGame.Invoke(__instance, new object[] { currentSession });
                                    if ((bool)ReflectedManMods.m_ReloadAllPending.GetValue(__instance))
                                    {
                                        ModManager.logger.Debug("Purged content, but reload pending. Moving to reload step.");
                                        ReflectedManMods.m_CurrentSession.SetValue(__instance, null);
                                        ReflectedManMods.CheckReloadAllMods.Invoke(__instance, null);
                                        return false;
                                    }
                                    ModManager.logger.Debug("Purged content, injecting new content.");
                                    CurrentStage = ModLoadStage.ReprocessMods;
                                    break;
                                }
                            case ModLoadStage.ReprocessMods:
                                {
                                    if (CurrentProcess == null)
                                    {
                                        CurrentProcess = ModManager.ReprocessOfficialMods(requestedSession);
                                    }
                                    if (!CurrentProcess.MoveNext())
                                    {
                                        ModManager.logger.Info("All mods reprocessed. Determining dependencies");
                                        ModManager.ReprocessModOrders(requestedSession);

                                        ModManager.PatchCustomBlocksIfNeeded();
                                        CurrentProcess = null;
                                        CurrentStage = ModLoadStage.EarlyInit;
                                    }
                                    break;
                                }
                            case ModLoadStage.EarlyInit:
                                {
                                    if (CurrentProcess == null)
                                    {
                                        CurrentProcess = ModManager.ProcessEarlyInits();
                                    }
                                    if (!CurrentProcess.MoveNext())
                                    {
                                        object[] args = new object[] { null };
                                        bool needsRestart = (bool)ReflectedManMods.SessionRequiresRestart.Invoke(__instance, args);
                                        List<string> failedMods = (List<string>)args[0];

                                        if (!needsRestart)
                                        {
                                            // We succeeded, go to init step
                                            CurrentProcess = null;
                                            CurrentStage = ModLoadStage.Init;
                                            break;
                                        }
                                        else if (Singleton.Manager<ManNetworkLobby>.inst.LobbySystem.CurrentLobby != null)
                                        {
                                            // We failed, and is MP, restart game
                                            UIScreenNotifications uiscreenNotifications = (UIScreenNotifications)Singleton.Manager<ManUI>.inst.GetScreen(ManUI.ScreenType.NotificationScreen);
                                            string notification = string.Format(Singleton.Manager<Localisation>.inst.GetLocalisedString(LocalisationEnums.StringBanks.SteamWorkshop, 45, Array.Empty<Localisation.GlyphInfo>()), Array.Empty<object>());
                                            uiscreenNotifications.Set(notification, delegate ()
                                            {
                                                Singleton.Manager<ManUI>.inst.RemovePopup();
                                                ReflectedManMods.RequestRestartGame.Invoke(Singleton.Manager<ManMods>.inst, new object[] { requestedSession, Singleton.Manager<ManNetworkLobby>.inst.LobbySystem.CurrentLobby.ID });
                                            }, delegate ()
                                            {
                                                Singleton.Manager<ManUI>.inst.RemovePopup();
                                                Singleton.Manager<ManGameMode>.inst.TriggerSwitch<ModeAttract>();
                                            }, Singleton.Manager<Localisation>.inst.GetLocalisedString(LocalisationEnums.StringBanks.MenuMain, 29, Array.Empty<Localisation.GlyphInfo>()), Singleton.Manager<Localisation>.inst.GetLocalisedString(LocalisationEnums.StringBanks.MenuMain, 30, Array.Empty<Localisation.GlyphInfo>()));
                                            uiscreenNotifications.SetUseNewInputHandler(true);
                                            Singleton.Manager<ManUI>.inst.PushScreenAsPopup(uiscreenNotifications, ManUI.PauseType.None);
                                            break;
                                        }

                                        // We failed, but go to init step anyway
                                        string message = String.Join("\n", failedMods.Select((mod) => { return $" - {mod}"; }));
                                        ModManager.logger.Error($"Some mods failed to EarlyInit for singleplayer game?\n{message}");
                                        CurrentProcess = null;
                                        CurrentStage = ModLoadStage.Init;
                                        break;
                                    }
                                    break;
                                }
                            case ModLoadStage.Init:
                                {
                                    if (CurrentProcess == null)
                                    {
                                        CurrentProcess = ModManager.ProcessInits();
                                    }
                                    if (!CurrentProcess.MoveNext())
                                    {
                                        CurrentProcess = null;
                                        CurrentStage = ModLoadStage.Corps;
                                    }
                                    break;
                                }
                            case ModLoadStage.Corps:
                                {
                                    if (CurrentProcess == null)
                                    {
                                        CurrentProcess = ModManager.InjectModdedCorps(__instance, requestedSession);
                                    }
                                    if (!CurrentProcess.MoveNext())
                                    {
                                        CurrentProcess = null;
                                        CurrentStage = ModLoadStage.Skins;
                                    }
                                    break;
                                }
                            case ModLoadStage.Skins:
                                {
                                    if (CurrentProcess == null)
                                    {
                                        CurrentProcess = ModManager.InjectModdedSkins(__instance, requestedSession);
                                    }
                                    if (!CurrentProcess.MoveNext())
                                    {
                                        // Perform corp/skin setup now that everything is loaded
                                        Singleton.Manager<ManTechMaterialSwap>.inst.RebuildCorpArrayTextures();
                                        Dictionary<int, List<ModdedSkinDefinition>> dictionary = new Dictionary<int, List<ModdedSkinDefinition>>();
                                        foreach (KeyValuePair<int, string> keyValuePair in requestedSession.CorpIDs)
                                        {
                                            ModdedCorpDefinition moddedCorpDefinition = __instance.FindModdedAsset<ModdedCorpDefinition>(keyValuePair.Value);
                                            if (moddedCorpDefinition != null)
                                            {
                                                List<ModdedSkinDefinition> list2 = new List<ModdedSkinDefinition>();
                                                list2.Add(moddedCorpDefinition.m_DefaultSkinSlots[0]);
                                                Dictionary<int, string> dictionary2;
                                                if (requestedSession.SkinIDsByCorp.TryGetValue(keyValuePair.Key, out dictionary2))
                                                {
                                                    foreach (KeyValuePair<int, string> keyValuePair2 in dictionary2)
                                                    {
                                                        ModdedSkinDefinition moddedSkinDefinition = __instance.FindModdedAsset<ModdedSkinDefinition>(keyValuePair2.Value);
                                                        if (moddedSkinDefinition != null)
                                                        {
                                                            list2.Add(moddedSkinDefinition);
                                                        }
                                                    }
                                                }
                                                dictionary.Add(keyValuePair.Key, list2);
                                            }
                                        }
                                        Singleton.Manager<ManTechMaterialSwap>.inst.BuildCustomCorpArrayTextures(dictionary);

                                        // Move to next stage
                                        CurrentProcess = null;
                                        CurrentStage = ModLoadStage.Blocks;
                                    }
                                    break;
                                }
                            case ModLoadStage.Blocks:
                                {
                                    if (CurrentProcess == null)
                                    {
                                        CurrentProcess = ModManager.InjectModdedBlocks(__instance, requestedSession, currentSession);
                                    }
                                    if (!CurrentProcess.MoveNext())
                                    {
                                        Singleton.Manager<ManSpawn>.inst.OnDLCLoadComplete();

                                        // Move to next stage
                                        CurrentProcess = null;
                                        CurrentStage = ModLoadStage.Done;
                                    }
                                    break;
                                }
                            case ModLoadStage.Done:
                                {
                                    ReflectedManMods.m_CurrentSession.SetValue(__instance, requestedSession);
                                    ReflectedManMods.m_RequestedSession.SetValue(__instance, null);
                                    ReflectedManMods.m_LoadingRequestedSessionInProgress.SetValue(__instance, false);
                                    ModManager.CurrentSessionLoaded = true;
                                    break;
                                }
                            default:
                                {
                                    CurrentProcess = null;
                                    ModManager.logger.Error("MOD LOADING IN UNKNOWN STATE: {state}", CurrentStage.ToString());
                                    break;
                                }
                        }
                    }
                }
                return false;
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
                if (ModManager.CurrentOperation != null)
                {
                    __instance.loadingBar.SetActive(true);
                    __instance.loadingProgressText.text = $"{ModManager.CurrentOperation} - {(int)(100 * ModManager.CurrentOperationProgress)}%\n{ModManager.CurrentOperationSpecifics}";
                    __instance.loadingProgressImage.fillAmount = ModManager.CurrentOperationProgress;
                    return false;
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
                ModManager.logger.Info($"Trying to register loader {loader.GetModuleKey()}");
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
