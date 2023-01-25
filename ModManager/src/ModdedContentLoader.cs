using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using System.Reflection;
using HarmonyLib;
using TerraTech.Network;
using Steamworks;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace ModManager
{
    internal class ModdedContentLoader
    {
        internal static NLog.Logger logger = NLog.LogManager.GetLogger("ModdedContentLoader");
        internal static ManMods manMods = Singleton.Manager<ManMods>.inst;

        internal enum ModLoadStage : byte
        {
            NotLoaded = 0,
            ReprocessMods = 1,
            EarlyInit = 2,
            Init = 3,
            Corps = 4,
            Skins = 5,
            Blocks = 6,
            LateInit = 7,
            Done = 8
        }

        internal ModLoadStage CurrentStage = ModLoadStage.NotLoaded;

        private ModSessionInfo currentSession;
        private ModSessionInfo requestedSession;

        internal void Start(ModSessionInfo currentSession, ModSessionInfo requestedSession)
        {
            CurrentStage = ModLoadStage.NotLoaded;
            this.currentSession = currentSession;
            this.requestedSession = requestedSession;
        }

        internal void Finish()
        {
            CurrentStage = ModLoadStage.Done;
            this.currentSession = null;
            this.requestedSession = null;
        }

        private IEnumerator CurrentProcess = null;

        // return true if done
        internal bool InjectModdedContent()
        {
            switch (this.CurrentStage)
            {
                case ModdedContentLoader.ModLoadStage.NotLoaded:
                    {
                        if (requestedSession.m_Authoritative)
                        {
                            this.SetupAuthoritativeSession();
                        }
                        ModManager.logger.Debug("✨ Purging modded content");
                        ReflectedManMods.PurgeModdedContentFromGame.Invoke(manMods, new object[] { currentSession });
                        if ((bool)ReflectedManMods.m_ReloadAllPending.GetValue(manMods))
                        {
                            ModManager.logger.Debug("↻ Purged content, but reload pending. Moving to reload step.");
                            ReflectedManMods.m_CurrentSession.SetValue(manMods, null);
                            ReflectedManMods.CheckReloadAllMods.Invoke(manMods, null);
                            return false;
                        }
                        ModManager.logger.Debug("🏁 Purged content, injecting new content.");
                        ReflectedManMods.m_CurrentSession.SetValue(manMods, requestedSession);

                        this.CurrentStage = ModdedContentLoader.ModLoadStage.ReprocessMods;
                        break;
                    }
                case ModdedContentLoader.ModLoadStage.ReprocessMods:
                    {
                        if (CurrentProcess == null)
                        {
                            CurrentProcess = ModManager.ReprocessOfficialMods(requestedSession);
                        }
                        if (!CurrentProcess.MoveNext())
                        {
                            ModManager.logger.Info("🏁 All mods reprocessed. Determining dependencies");
                            ModManager.ReprocessModOrders(requestedSession);
                            ModManager.PatchCustomBlocksIfNeeded();

                            CurrentProcess = null;
                            this.CurrentStage = ModdedContentLoader.ModLoadStage.EarlyInit;
                        }
                        break;
                    }
                case ModdedContentLoader.ModLoadStage.EarlyInit:
                    {
                        if (CurrentProcess == null)
                        {
                            CurrentProcess = ProcessEarlyInits();
                        }
                        if (!CurrentProcess.MoveNext())
                        {
                            RequestRestartIfNeeded();
                            this.CurrentStage = ModdedContentLoader.ModLoadStage.Init;
                            CurrentProcess = null;
                        }
                        break;
                    }
                case ModdedContentLoader.ModLoadStage.Init:
                    {
                        if (CurrentProcess == null)
                        {
                            CurrentProcess = ProcessInits();
                        }
                        if (!CurrentProcess.MoveNext())
                        {
                            CurrentProcess = null;
                            this.CurrentStage = ModdedContentLoader.ModLoadStage.Corps;
                        }
                        break;
                    }
                case ModdedContentLoader.ModLoadStage.Corps:
                    {
                        if (CurrentProcess == null)
                        {
                            CurrentProcess = InjectModdedCorps();
                        }
                        if (!CurrentProcess.MoveNext())
                        {
                            CurrentProcess = null;
                            this.CurrentStage = ModdedContentLoader.ModLoadStage.Skins;
                        }
                        break;
                    }
                case ModdedContentLoader.ModLoadStage.Skins:
                    {
                        if (CurrentProcess == null)
                        {
                            CurrentProcess = InjectModdedSkins();
                        }
                        if (!CurrentProcess.MoveNext())
                        {
                            CurrentProcess = null;
                            this.CurrentStage = ModdedContentLoader.ModLoadStage.Blocks;
                        }
                        break;
                    }
                case ModdedContentLoader.ModLoadStage.Blocks:
                    {
                        if (CurrentProcess == null)
                        {
                            CurrentProcess = InjectModdedBlocks();
                        }

                        float waitTime = Time.realtimeSinceStartup + maxProcessingInterval;
                        while (true)
                        {
                            bool toContinue = CurrentProcess.MoveNext();
                            if (!toContinue)
                            {
                                Singleton.Manager<ManSpawn>.inst.OnDLCLoadComplete();
                                CurrentProcess = null;
                                this.CurrentStage = ModdedContentLoader.ModLoadStage.LateInit;
                                return false;
                            }
                            if (Time.realtimeSinceStartup > waitTime)
                            {
                                break;
                            }
                        }
                        break;
                    }
                case ModdedContentLoader.ModLoadStage.LateInit:
                    {
                        if (CurrentProcess == null)
                        {
                            CurrentProcess = ProcessLateInits();
                        }
                        if (!CurrentProcess.MoveNext())
                        {
                            CurrentProcess = null;
                            return true;
                        }
                        break;
                    }
                default:
                    {
                        ModManager.logger.Error("❌ MOD LOADING IN INVALID STATE: {state}", this.CurrentStage.ToString());
                        break;
                    }
            }
            return false;
        }

        private const float maxProcessingInterval = 0.05f;

        private void RequestRestartIfNeeded()
        {
            object[] args = new object[] { null };
            bool needsRestart = (bool)ReflectedManMods.SessionRequiresRestart.Invoke(manMods, args);
            List<string> failedMods = (List<string>)args[0];

            if (!needsRestart)
            {
                // We succeeded, go to init step
                this.CurrentStage = ModdedContentLoader.ModLoadStage.Init;
                return;
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
            }

            // We failed, but go to init step anyway
            string message = String.Join("\n", failedMods.Select((mod) => { return $" - \"{mod}\""; }));
            ModManager.logger.Error($"🚨 Some mods failed to EarlyInit for singleplayer game?\n{message}");
        }

        private void SetupAuthoritativeSession()
        {
            Dictionary<string, string> corpModLookup = new Dictionary<string, string>();
            Dictionary<string, List<string>> corpSkinLookup = new Dictionary<string, List<string>>();
            List<string> moddedBlocks = new List<string>();
            foreach (KeyValuePair<string, ModContainer> pair in (Dictionary<string, ModContainer>)ReflectedManMods.m_Mods.GetValue(manMods))
            {
                string modID = pair.Key;
                if (!pair.Value.IsRemote && requestedSession.Mods.ContainsKey(modID))
                {
                    foreach (ModdedCorpDefinition moddedCorpDefinition in pair.Value.Contents.m_Corps)
                    {
                        if (!corpModLookup.ContainsKey(moddedCorpDefinition.name))
                        {
                            ModdedContentLoader.logger.Debug("🚩 Found corp {corp} ({short}) in mod {mod}", moddedCorpDefinition.name, moddedCorpDefinition.m_ShortName, modID);
                            corpModLookup.Add(moddedCorpDefinition.name, modID);
                        }
                        else
                        {
                            ModdedContentLoader.logger.Warn(
                                "❌ Failed to add duplicate corp {corp} from mod {key} because we already have one from mod {mod}",
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
                        ModdedContentLoader.logger.Debug("🖌️ Found skin {skin} for corp {corp}", moddedSkinDefinition.name, moddedSkinDefinition.m_Corporation);
                        corpSkinLookup[moddedSkinDefinition.m_Corporation].Add(ModUtils.CreateCompoundId(modID, moddedSkinDefinition.name));
                    }
                    foreach (ModdedBlockDefinition moddedBlockDefinition in pair.Value.Contents.m_Blocks)
                    {
                        ModdedContentLoader.logger.Trace("🔎 Found modded block {block} in mod {mod}", moddedBlockDefinition.name, modID);
                        moddedBlocks.Add(ModUtils.CreateCompoundId(modID, moddedBlockDefinition.name));
                    }
                }
            }
            List<string> moddedCorps = new List<string>(corpModLookup.Count);
            foreach (KeyValuePair<string, string> keyValuePair2 in corpModLookup)
            {
                moddedCorps.Add(ModUtils.CreateCompoundId(keyValuePair2.Value, keyValuePair2.Key));
            }
            ReflectedManMods.AutoAssignSessionIDs.Invoke(manMods, new object[] { requestedSession, moddedCorps, corpSkinLookup, moddedBlocks });
        }

        private IEnumerator ProcessEarlyInits()
        {
            // Process the EarlyInits
            logger.Info("⏳ Processing Early Inits");
            int numMods = ModManager.EarlyInitQueue.Count;
            int processed = 0;
            ModManager.CurrentOperation = "Code mod first-time setup";
            foreach (WrappedMod script in ModManager.EarlyInitQueue)
            {
                ModManager.CurrentOperationSpecifics = $"Processing {script.Name} EarlyInit()";
                bool failed = false;
                if (!script.earlyInitRun)
                {
                    logger.Debug(" 💿 Processing EarlyInit for mod {}", script.Name);
                    IEnumerator<float> iterator = script.EarlyInit();
                    while (true)
                    {
                        try
                        {
                            bool toContinue = iterator.MoveNext();
                            if (toContinue)
                            {
                                ModManager.CurrentOperationSpecificProgress = iterator.Current;
                            }
                            else
                            {
                                break;
                            }
                        }
                        catch (Exception e)
                        {
                            logger.Error($" ❌ Failed to process EarlyInit() for {script.Name}:\n{e.ToString()}");
                            failed = true;
                            break;
                        }
                        yield return null;
                    }
                }
                ModContainer container = ModManager.modMetadata[script];
                if (!failed)
                {
                    script.earlyInitRun = true;
                    ModManager.InjectedEarlyHooks.SetValue(container, true);
                }
                ModManager.containersWithEarlyHooks.Add(container);
                logger.Debug("  ✔️ EarlyInit success");
                processed++;
                ModManager.CurrentOperationProgress = (float)processed / (float)numMods;
                yield return null;
            }
            ModManager.CurrentOperationSpecifics = null;
            yield return null;
            yield break;
        }

        private IEnumerator ProcessInits()
        {
            // Process the Inits
            logger.Info("⏳ Processing Inits");
            int numMods = ModManager.InitQueue.Count;
            int processed = 0;
            ModManager.CurrentOperation = "Initializing code mods";
            foreach (WrappedMod script in ModManager.InitQueue)
            {
                ModManager.CurrentOperationSpecifics = $"Processing {script.Name} Init()";
                logger.Debug(" 💿 Processing Init for mod {}", script.Name);
                IEnumerator<float> iterator = script.Init();
                while (true)
                {
                    try
                    {
                        bool toContinue = iterator.MoveNext();
                        if (toContinue)
                        {
                            ModManager.CurrentOperationSpecificProgress = iterator.Current;
                        }
                        else
                        {
                            break;
                        }
                    }
                    catch (Exception e)
                    {
                        logger.Error($" ❌ Failed to process Init() for {script.Name}:\n{e.ToString()}");
                        break;
                    }
                    yield return null;
                }
                logger.Debug("  ✔️ Init success");
                processed++;
                ModManager.CurrentOperationProgress = (float)processed / (float)numMods;
                yield return null;
            }
            ModManager.CurrentOperationSpecifics = null;
            yield return null;
            yield break;
        }

        private IEnumerator InjectModdedCorps()
        {
            // Process the Inits
            logger.Info("⏳ Injecting Modded Corps");
            int processed = 0;
            ModManager.CurrentOperation = "Injecting modded corps";

            if (this.requestedSession.CorpIDs.Count > 0)
            {
                int numCorps = this.requestedSession.CorpIDs.Count;
                Dictionary<string, int> reverseLookup = (Dictionary<string, int>)ReflectedManMods.m_CorpIDReverseLookup.GetValue(manMods);
                Dictionary<int, ModdedCorpDefinition> dictionary = new Dictionary<int, ModdedCorpDefinition>();
                foreach (KeyValuePair<int, string> keyValuePair in this.requestedSession.CorpIDs)
                {
                    ModManager.CurrentOperationSpecifics = $"{keyValuePair.Value}";
                    logger.Trace(" 🚩 Injecting corp {corp}", keyValuePair.Value);
                    int corpIndex = keyValuePair.Key;
                    ModdedCorpDefinition moddedCorpDefinition = manMods.FindModdedAsset<ModdedCorpDefinition>(keyValuePair.Value);
                    if (moddedCorpDefinition != null)
                    {
                        dictionary.Add(corpIndex, moddedCorpDefinition);
                        Singleton.Manager<ManPurchases>.inst.AddCustomCorp(corpIndex);
                        Singleton.Manager<ManCustomSkins>.inst.AddCorp(corpIndex);
                        ReflectedManMods.InjectCustomSkinReferences.Invoke(manMods, new object[] { 0, (FactionSubTypes)corpIndex, moddedCorpDefinition.m_DefaultSkinSlots[0] });
                        reverseLookup.Add(moddedCorpDefinition.m_ShortName, corpIndex);
                        logger.Info(string.Format(" ✔️ Injected corp {0} at ID {1}", moddedCorpDefinition.name, corpIndex));
                    }
                    processed++;
                    ModManager.CurrentOperationProgress = (float)processed / (float)numCorps;
                    yield return null;
                }
                Singleton.Manager<ManLicenses>.inst.m_UnlockTable.AddModdedCorps(dictionary);
            }
            ModManager.CurrentOperationSpecifics = null;
            yield return null;
            yield break;
            yield break;
        }

        private IEnumerator InjectModdedSkins()
        {
            // Process the Inits
            logger.Info("⏳ Injecting Modded Skins");
            ModManager.CurrentOperation = "Injecting modded skins";

            if (this.requestedSession.SkinIDsByCorp.Count > 0)
            {
                foreach (KeyValuePair<int, Dictionary<int, string>> keyValuePair in this.requestedSession.SkinIDsByCorp)
                {
                    int processed = 0;
                    int count = keyValuePair.Value.Count;
                    int key = keyValuePair.Key;
                    foreach (KeyValuePair<int, string> keyValuePair2 in keyValuePair.Value)
                    {
                        ModManager.CurrentOperationSpecifics = $"{keyValuePair2.Value}";
                        logger.Trace(" 🖌️ Injecting skin {skin}", keyValuePair2.Value);
                        int key2 = keyValuePair2.Key;
                        ModdedSkinDefinition moddedSkinDefinition = manMods.FindModdedAsset<ModdedSkinDefinition>(keyValuePair2.Value);
                        if (moddedSkinDefinition != null)
                        {
                            if (moddedSkinDefinition.m_Albedo == null)
                            {
                                logger.Error(string.Format(" ❌ Cannot inject skin {0} at ID {1}: Albedo texture was not found. Did you set it?", moddedSkinDefinition.name, key2));
                            }
                            else if (moddedSkinDefinition.m_Combined == null)
                            {
                                logger.Error(string.Format(" ❌ Cannot inject skin {0} at ID {1}: Combined Metallic/Smoothness texture was not found. Did you set both of them?", moddedSkinDefinition.name, key2));
                            }
                            else if (moddedSkinDefinition.m_Emissive == null)
                            {
                                logger.Error(string.Format(" ❌ Cannot inject skin {0} at ID {1}: Emmisive texture was not found. Did you set it?", moddedSkinDefinition.name, key2));
                            }
                            else if (moddedSkinDefinition.m_PreviewImage == null)
                            {
                                logger.Error(string.Format(" ❌ Cannot inject skin {0} at ID {1}: Auto-generated preview texture was not found. This implies a problem with the TTModTool exporter.", moddedSkinDefinition.name, key2));
                            }
                            else if (moddedSkinDefinition.m_SkinButtonImage == null)
                            {
                                logger.Error(string.Format(" ❌ Cannot inject skin {0} at ID {1}: Skin button texture not found. Did you set it?", moddedSkinDefinition.name, key2));
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
                                    logger.Error(string.Format(" ❌ Cannot inject skin {0} at ID {1}: Corp {2} was not found - is it part of a different mod?", moddedSkinDefinition.name, key2, moddedSkinDefinition.m_Corporation));
                                }
                            }
                        }
                        else
                        {
                            logger.Warn(string.Format(" ❌ Failed to inject skin {0} at ID {1}. Did the mod remove a skin?", keyValuePair2.Value, keyValuePair2.Key));
                        }
                        processed++;
                        ModManager.CurrentOperationProgress = (float)processed / (float)count;
                        yield return null;
                    }
                }
            }

            IEnumerator postInjectionSetup = SetupModdedCorpSkins();
            while (postInjectionSetup.MoveNext())
            {
                yield return null;
            }
            ModManager.CurrentOperationSpecifics = null;
            yield return null;
            yield break;
            yield break;
        }

        private IEnumerator ProcessLateInits()
        {
            // Process the Late Inits
            logger.Info("⏳ Processing LateInits");
            int numMods = ModManager.LateInitQueue.Count;
            int processed = 0;
            ModManager.CurrentOperation = "Code mod final setup";
            foreach (WrappedMod script in ModManager.LateInitQueue)
            {
                ModManager.CurrentOperationSpecifics = $"Processing {script.Name} LateInit()";
                logger.Debug(" 💿 Processing LateInit for mod {}", script.Name);
                IEnumerator<float> iterator = script.LateInit();
                while (true)
                {
                    try
                    {
                        bool toContinue = iterator.MoveNext();
                        if (toContinue)
                        {
                            ModManager.CurrentOperationSpecificProgress = iterator.Current;
                        }
                        else
                        {
                            break;
                        }
                    }
                    catch (Exception e)
                    {
                        logger.Error($" ❌ Failed to process LateInit() for {script.Name}:\n{e.ToString()}");
                        break;
                    }
                    yield return null;
                }
                logger.Debug("  ✔️ LateInit success");
                processed++;
                ModManager.CurrentOperationProgress = (float)processed / (float)numMods;
                yield return null;
            }
            ModManager.CurrentOperationSpecifics = null;
            yield return null;
            yield break;
        }


        private static FieldInfo sLoaders = AccessTools.Field(typeof(JSONBlockLoader), "sLoaders");
        private static IEnumerator LoadBlockJSON(ModContainer mod, int blockID, ModdedBlockDefinition def)
        {
            // do break here - potential fix for linux?
            if (def == null)
            {
                yield break;
            }

            TankBlock block = def.m_PhysicalPrefab.gameObject.GetComponent<TankBlock>();
            JObject jobject = null;
            try
            {
                if (Singleton.Manager<ManMods>.inst.ShouldReadFromRawJSON)
                {
                    string text = mod.AssetBundlePath.Substring(0, mod.AssetBundlePath.LastIndexOf('/')) + "/BlockJSON/" + def.name + ".json";
                    if (File.Exists(text))
                    {
                        jobject = JObject.Parse(File.ReadAllText(text));
                        logger.Info("⚠️ Read JSON from " + text + " as an override");
                    }
                    else
                    {
                        logger.Info("Block " + def.name + " could not find a JSON override at " + text);
                    }
                }
                if (jobject == null)
                {
                    jobject = JObject.Parse(def.m_Json.text);
                    logger.Trace("   ✔️ Read JSON from asset bundle for " + def.name);
                }
            }
            catch (Exception e)
            {
                logger.Error("   ❌ FAILED to read BlockJSON");
                logger.Error(e);
                yield break;
            }

            // do break here - potential fix for linux?
            if (jobject == null)
            {
                yield break;
            }
            Dictionary<string, JSONModuleLoader> loaders = (Dictionary<string, JSONModuleLoader>)sLoaders.GetValue(null);
            foreach (KeyValuePair<string, JToken> keyValuePair in jobject)
            {
                JSONModuleLoader jsonmoduleLoader;
                if (loaders.TryGetValue(keyValuePair.Key, out jsonmoduleLoader))
                {
                    try
                    {
                        logger.Trace($"   💿 Processing Loader {keyValuePair.Key}");
                        if (!jsonmoduleLoader.CreateModuleForBlock(blockID, def, block, keyValuePair.Value))
                        {
                            logger.Error(string.Format("   ❌ Failed to parse module {0} in JSON for {1}", keyValuePair.Key, def));
                        }
                    }
                    catch (Exception e)
                    {
                        logger.Error($"   ❌ FAILED to process block module {keyValuePair.Key}");
                        logger.Error(e);
                    }
                }
                else
                {
                    logger.Error(string.Format("   ❌ Could not parse module {0} in JSON for {1}", keyValuePair.Key, def));
                }
                yield return null;
            }
            yield break;
        }

        private IEnumerator InjectModdedBlocks()
        {
            // Process the Inits
            logger.Info("⏳ Injecting Modded Blocks");
            ModManager.CurrentOperation = "Injecting modded blocks";
            if (this.requestedSession.BlockIDs.Count > 0)
            {
                int processed = 0;
                int numBlocks = this.requestedSession.BlockIDs.Count;
                Dictionary<int, Dictionary<int, Dictionary<BlockTypes, ModdedBlockDefinition>>> gradeBlocksPerCorp = new Dictionary<int, Dictionary<int, Dictionary<BlockTypes, ModdedBlockDefinition>>>();
                Dictionary<int, Sprite> blockSpriteDict = new Dictionary<int, Sprite>(16);
                Dictionary<int, string> blockNames = (Dictionary<int, string>)ReflectedManMods.m_BlockNames.GetValue(manMods);
                Dictionary<int, string> blockDescriptions = (Dictionary<int, string>)ReflectedManMods.m_BlockDescriptions.GetValue(manMods);
                Dictionary<string, int> reverseLookup = (Dictionary<string, int>)ReflectedManMods.m_BlockIDReverseLookup.GetValue(manMods);
                List<int> failedBlockIDs = new List<int>();
                foreach (KeyValuePair<int, string> blockPair in this.requestedSession.BlockIDs)
                {
                    int blockIndex = blockPair.Key;
                    string blockID = blockPair.Value;
                    ModManager.CurrentOperationSpecifics = $"{blockID}";
                    ModManager.CurrentOperationProgress = (float)processed / (float)numBlocks;
                    logger.Debug($" 💉 Preparing to inject {blockID} (processed # {processed})");
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
                        FactionSubTypes corpIndex = manMods.GetCorpIndex(moddedBlockDefinition.m_Corporation, this.requestedSession);
                        TankBlockTemplate physicalPrefab = moddedBlockDefinition.m_PhysicalPrefab;
                        Visible visible = physicalPrefab.GetComponent<Visible>();
                        if (visible == null)
                        {
                            TankBlock tankBlock = null;
                            ModuleDamage moduleDamage = null;
                            try
                            {
                                logger.Trace("  🎯 Injected block {block} and performed first time setup", moddedBlockDefinition.name);
                                if (visible == null)
                                {
                                    visible = physicalPrefab.gameObject.AddComponent<Visible>();
                                }
                                UnityEngine.Object component = physicalPrefab.gameObject.GetComponent<Damageable>();
                                moduleDamage = physicalPrefab.gameObject.GetComponent<ModuleDamage>();
                                if (component == null)
                                {
                                    physicalPrefab.gameObject.AddComponent<Damageable>();
                                }
                                if (moduleDamage == null)
                                {
                                    moduleDamage = physicalPrefab.gameObject.AddComponent<ModuleDamage>();
                                }
                                tankBlock = physicalPrefab.gameObject.GetComponent<TankBlock>();
                                tankBlock.m_BlockCategory = moddedBlockDefinition.m_Category;
                                tankBlock.m_BlockRarity = moddedBlockDefinition.m_Rarity;
                                tankBlock.m_DefaultMass = Mathf.Clamp(moddedBlockDefinition.m_Mass, 0.0001f, float.MaxValue);
                                tankBlock.filledCells = physicalPrefab.filledCells.ToArray();
                                tankBlock.attachPoints = physicalPrefab.attachPoints.ToArray();
                                visible.m_ItemType = new ItemTypeInfo(ObjectTypes.Block, blockIndex);
                            }
                            catch (Exception e)
                            {
                                logger.Error("  ❌ FAILED block setup for " + blockID);
                                logger.Error(e);
                                failedBlockIDs.Add(blockIndex);
                                processed++;
                                continue;
                            }

                            logger.Trace("  📜 Preparing to load block JSON");
                            IEnumerator jsonIterator = LoadBlockJSON(mod, blockIndex, moddedBlockDefinition);
                            while (jsonIterator.MoveNext())
                            {
                                yield return null;
                            }
                            logger.Trace("  ✔️ Block JSON loaded");
                            try
                            {
                                physicalPrefab = moddedBlockDefinition.m_PhysicalPrefab;
                                physicalPrefab.gameObject.SetActive(false);
                                Damageable component3 = physicalPrefab.GetComponent<Damageable>();
                                moduleDamage = physicalPrefab.GetComponent<ModuleDamage>();
                                tankBlock = physicalPrefab.GetComponent<TankBlock>();
                                visible = physicalPrefab.GetComponent<Visible>();
                                visible.m_ItemType = new ItemTypeInfo(ObjectTypes.Block, blockIndex);
                                component3.m_DamageableType = moddedBlockDefinition.m_DamageableType;
                                moduleDamage.maxHealth = moddedBlockDefinition.m_MaxHealth;
                                if (moduleDamage.deathExplosion == null)
                                {
                                    logger.Trace("  💥 Adding default DeathExplosion");
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
                                MeshCollider[] componentsInChildren2 = tankBlock.GetComponentsInChildren<MeshCollider>();
                                for (int i = 0; i < componentsInChildren2.Length; i++)
                                {
                                    componentsInChildren2[i].convex = true;
                                }
                            }
                            catch (Exception e)
                            {
                                logger.Error("  ❌ FAILED block finalization " + blockID);
                                logger.Error(e);
                                failedBlockIDs.Add(blockIndex);
                                processed++;
                                continue;
                            }

                            logger.Trace("  🛠️ Creating component pool");
                            tankBlock.transform.CreatePool(8);
                        }
                        else
                        {
                            physicalPrefab.gameObject.GetComponent<Visible>().m_ItemType = new ItemTypeInfo(ObjectTypes.Block, blockIndex);

                            logger.Trace(" 🛠️ Updating component pool");
                            physicalPrefab.transform.CreatePool(8);
                        }

                        ModManager.CurrentOperationSpecifics = moddedBlockDefinition.m_BlockDisplayName;

                        try
                        {
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
                                logger.Error($" ❌ Block {{block}} with ID {blockIndex} failed to inject because icon was not set", moddedBlockDefinition.name);
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
                            logger.Debug($" ✔️ Injected block {{block}} at ID {blockIndex}", moddedBlockDefinition.name);
                        }
                        catch (Exception e)
                        {
                            logger.Error(" ❌ FAILED block injection " + blockID);
                            logger.Error(e);
                            failedBlockIDs.Add(blockIndex);
                            processed++;
                            continue;
                        }
                    }
                    else
                    {
                        logger.Error(" ❌ Could not find ModdedBlockDefinition for {block}", blockID);
                        failedBlockIDs.Add(blockIndex);
                    }
                    processed++;
                }
                if (failedBlockIDs.Count > 0)
                {
                    logger.Debug(" 💣 Removing failed blocks");
                }
                foreach (int key2 in failedBlockIDs)
                {
                    this.requestedSession.BlockIDs.Remove(key2);
                }
                logger.Info("🏁 Injected all official blocks");
                ModManager.CurrentOperationProgress = 1.0f;
                yield return null;

                ModManager.CurrentOperation = "Setting up Block Tables";
                ModManager.CurrentOperationSpecifics = null;
                ModManager.CurrentOperationProgress = 1.0f;
                yield return null;

                logger.Debug(" 🖼️ Setting up block icons");
                Singleton.Manager<ManUI>.inst.m_SpriteFetcher.SetModSprites(ObjectTypes.Block, blockSpriteDict);

                logger.Debug(" 🗃️ Setting up BlockUnlockTable");
                BlockUnlockTable blockUnlockTable = Singleton.Manager<ManLicenses>.inst.GetBlockUnlockTable();

                logger.Trace(" 🗑️ Removing modded blocks");
                blockUnlockTable.RemoveModdedBlocks();

                logger.Trace(" 🟢 Adding ModManager.Current modded blocks");
                foreach (KeyValuePair<int, Dictionary<int, Dictionary<BlockTypes, ModdedBlockDefinition>>> corpBlocks in gradeBlocksPerCorp)
                {
                    logger.Debug($"  ▶ Processing blocks in corp '{GetCorpName(corpBlocks.Key)}'");
                    ModdedCorpDefinition corpDefinition = manMods.GetCorpDefinition((FactionSubTypes)corpBlocks.Key, this.requestedSession);
                    foreach (KeyValuePair<int, Dictionary<BlockTypes, ModdedBlockDefinition>> gradeBlocks in corpBlocks.Value)
                    {
                        logger.Trace($"   ➤ Processing blocks in grade {gradeBlocks.Key}");
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
                logger.Trace($" 🟢 Initing table");
                blockUnlockTable.Init();
            }
            ModManager.CurrentOperationSpecifics = null;
            yield return null;
            logger.Info("🏁 Block injection and setup complete");
            yield break;
        }

        internal static Dictionary<FactionSubTypes, string> vanillaCorpToString = new Dictionary<FactionSubTypes, string>();

        internal static void Setup()
        {
            int[] values = Enum.GetValues(typeof(FactionSubTypes)).Cast<int>().ToArray();
            foreach (int corpID in values)
            {
                FactionSubTypes corp = (FactionSubTypes) corpID;
                string name = Enum.GetName(typeof(FactionSubTypes), corp);
                vanillaCorpToString.Add(corp, name);
            }
        }

        private static string GetCorpName(int corpID)
        {
            if (vanillaCorpToString.TryGetValue((FactionSubTypes)corpID, out string name))
            {
                return name;
            }
            string moddedCorpName = manMods.FindCorpShortName((FactionSubTypes) corpID);
            if (moddedCorpName != null)
            {
                return moddedCorpName;
            }
            return corpID.ToString();
        }

        private IEnumerator SetupModdedCorpSkins()
        {
            Singleton.Manager<ManTechMaterialSwap>.inst.RebuildCorpArrayTextures();
            Dictionary<int, List<ModdedSkinDefinition>> dictionary = new Dictionary<int, List<ModdedSkinDefinition>>();
            foreach (KeyValuePair<int, string> keyValuePair in requestedSession.CorpIDs)
            {
                ModdedCorpDefinition moddedCorpDefinition = manMods.FindModdedAsset<ModdedCorpDefinition>(keyValuePair.Value);
                if (moddedCorpDefinition != null)
                {
                    List<ModdedSkinDefinition> moddedSkins = new List<ModdedSkinDefinition>();
                    moddedSkins.Add(moddedCorpDefinition.m_DefaultSkinSlots[0]);
                    Dictionary<int, string> dictionary2;
                    if (requestedSession.SkinIDsByCorp.TryGetValue(keyValuePair.Key, out dictionary2))
                    {
                        foreach (KeyValuePair<int, string> keyValuePair2 in dictionary2)
                        {
                            ModdedSkinDefinition moddedSkinDefinition = manMods.FindModdedAsset<ModdedSkinDefinition>(keyValuePair2.Value);
                            if (moddedSkinDefinition != null)
                            {
                                moddedSkins.Add(moddedSkinDefinition);
                            }
                        }
                    }
                    dictionary.Add(keyValuePair.Key, moddedSkins);
                }
            }
            Singleton.Manager<ManTechMaterialSwap>.inst.BuildCustomCorpArrayTextures(dictionary);
            yield break;
            yield break;
        }
    }
}
