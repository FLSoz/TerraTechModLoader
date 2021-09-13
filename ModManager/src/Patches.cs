using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using UnityEngine;
using Payload.UI.Commands.Steam;


namespace ModManager
{
    public static class Patches
    {
        /// <summary>
        /// Patch InitModScripts to do all EarlyInits, and Inits, in our specified order.
        /// </summary>
        /// <remarks>
        /// It is guaranteed that EarlyInit runs on every mod, before every InitModScripts, so we replicate that behaviour here
        /// </remarks>
        [HarmonyPatch(typeof(ManMods), "InitModScripts")]
        public static class PatchModScriptLoading
        {
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

        [HarmonyPatch(typeof(ManMods), "LoadWorkshopData")]
        public static class PatchWorkshopLoad
        {
            [HarmonyPrefix]
            public static bool Prefix(SteamDownloadItemData item, bool remote)
            {
                WorkshopLoader.LoadWorkshopData(item, remote);
                return false;
            }
        }

        /* [HarmonyPatch(typeof(BlockUnlockTable), "AddModdedBlocks")]
        public static class PatchLogAddModdedBlocks
        {
            [HarmonyPrefix]
            public static bool Prefix(ref BlockUnlockTable __instance, int corpIndex, int gradeIndex, Dictionary<BlockTypes, ModdedBlockDefinition> blocks)
            {
                if (blocks.Count > 0)
                {
                    if (corpIndex == 0)
                    {
                        Console.WriteLine($"INVALID grade {gradeIndex} in corp {corpIndex}");
                        foreach (ModdedBlockDefinition blockDef in blocks.Values)
                        {
                            Console.WriteLine($"{blockDef.m_BlockDisplayName}, {blockDef.m_BlockDescription}");
                        }
                    }

                    BlockUnlockTable.CorpBlockData corpBlockData = __instance.GetCorpBlockData(corpIndex);
                    if (corpBlockData != null)
                    {
                        gradeIndex = Mathf.Clamp(gradeIndex, 0, corpBlockData.m_GradeList.Length - 1);
                        Console.WriteLine($"Getting num blocks for grade {gradeIndex} in corp {corpIndex} ({((FactionSubTypes) corpIndex).ToString()})");
                        int num = corpBlockData.m_GradeList[gradeIndex].m_BlockList.Length;
                        Console.WriteLine($"Found {num} blocks, resizing array to {num + blocks.Count} to handle {blocks.Count} modded blocks");
                        Array.Resize<BlockUnlockTable.UnlockData>(ref corpBlockData.m_GradeList[gradeIndex].m_BlockList, num + blocks.Count);
                        int num2 = 0;
                        foreach (KeyValuePair<BlockTypes, ModdedBlockDefinition> keyValuePair in blocks)
                        {
                            Console.WriteLine($"Adding block {keyValuePair.Value.m_BlockDisplayName} ({keyValuePair.Value.m_BlockIdentifier} - {keyValuePair.Key}) at index {num + num2}");
                            corpBlockData.m_GradeList[gradeIndex].m_BlockList[num + num2] = new BlockUnlockTable.UnlockData
                            {
                                m_BlockType = keyValuePair.Key,
                                m_BasicBlock = true,
                                m_DontRewardOnLevelUp = !keyValuePair.Value.m_UnlockWithLicense,
                                m_HideOnLevelUpScreen = true
                            };
                            num2++;
                        }
                    }
                }
                return false;
            }
        }
        */

        /*
        [HarmonyPatch(typeof(ModuleShieldGenerator), "OnPool")]
        public static class PatchShieldLoad
        {
            private static readonly FieldInfo m_Shield = typeof(ModuleShieldGenerator).GetField("m_Shield", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            private static readonly FieldInfo m_ModuleEnergy = typeof(ModuleShieldGenerator).GetField("m_ModuleEnergy", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            private static readonly FieldInfo m_Warning = typeof(ModuleShieldGenerator).GetField("m_Warning", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            private static readonly FieldInfo m_SequenceNode = typeof(ModuleShieldGenerator).GetField("m_SequenceNode", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            private static readonly FieldInfo m_TechTriggerEvent = typeof(ModuleShieldGenerator).GetField("m_TechTriggerEvent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            private static readonly FieldInfo m_ShieldBulletTriggerEvent = typeof(ModuleShieldGenerator).GetField("m_ShieldBulletTriggerEvent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            private static readonly MethodInfo OnRejectShieldDamage = typeof(ModuleShieldGenerator).GetMethod("OnRejectShieldDamage", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            private static readonly MethodInfo OnUpdateConsumeEnergy = typeof(ModuleShieldGenerator).GetMethod("OnUpdateConsumeEnergy", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            private static readonly MethodInfo OnAttach = typeof(ModuleShieldGenerator).GetMethod("OnAttach", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            private static readonly MethodInfo OnDetach = typeof(ModuleShieldGenerator).GetMethod("OnDetach", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            private static readonly MethodInfo OnSerialize = typeof(ModuleShieldGenerator).GetMethod(
                "OnSerialize",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null,
                new Type[] {typeof(bool), typeof(TankPreset.BlockSpec)},
                null
            );
            private static readonly MethodInfo OnBulletTriggerEvent = typeof(ModuleShieldGenerator).GetMethod("OnBulletTriggerEvent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            private static readonly MethodInfo OnTankTriggerEvent = typeof(ModuleShieldGenerator).GetMethod("OnTankTriggerEvent", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            [HarmonyPrefix]
            public static bool Prefix(ModuleShieldGenerator __instance)
            {
                Console.WriteLine($"SHIELD 1 - {__instance.gameObject.name}");
                BubbleShield shield = __instance.GetComponentsInChildren<BubbleShield>(true).FirstOrDefault<BubbleShield>();
                m_Shield.SetValue(__instance, shield);
                Console.WriteLine("SHIELD 2");
                shield.SetRanges(__instance.m_Radius, new float[]
                {
                    0f,
                    __instance.m_ParticleLife
                }, new float[]
                {
                    0f,
                    __instance.m_ParticleSpeed
                });
                Console.WriteLine("SHIELD 3");
                if (__instance.m_Repulsion)
                {
                    Damageable damageable = shield.Damageable;
                    damageable.InitHealth(-1337f);
                    damageable.SetRejectDamageHandler(new Func<ManDamage.DamageInfo, bool, bool>(
                        (ManDamage.DamageInfo d, bool b1) => (bool) OnRejectShieldDamage.Invoke(__instance, new object[] { d, b1 })
                    ));
                }
                Console.WriteLine("SHIELD 4");
                ModuleEnergy moduleEnergy = __instance.GetComponent<ModuleEnergy>();
                m_ModuleEnergy.SetValue(__instance, moduleEnergy);
                Console.WriteLine("SHIELD 5");
                moduleEnergy.UpdateConsumeEvent.Subscribe(new Action(() => OnUpdateConsumeEnergy.Invoke(__instance, null)));
                Console.WriteLine("SHIELD 6");
                __instance.block.AttachEvent.Subscribe(new Action(() => OnAttach.Invoke(__instance, null)));
                Console.WriteLine("SHIELD 7");
                __instance.block.DetachEvent.Subscribe(new Action(() => OnDetach.Invoke(__instance, null)));
                Console.WriteLine("SHIELD 8");
                m_Warning.SetValue(__instance, new WarningHolder(__instance.block.visible, WarningHolder.WarningType.ShieldPowered));
                Console.WriteLine("SHIELD 9");
                __instance.block.serializeEvent.Subscribe(new Action<bool, TankPreset.BlockSpec>(
                    (bool b, TankPreset.BlockSpec s) => OnSerialize.Invoke(__instance, new object[] { b, s })
                ));
                Console.WriteLine("SHIELD 10");
                TriggerEvent shieldTriggerEvent = (TriggerEvent) m_ShieldBulletTriggerEvent.GetValue(__instance);
                shieldTriggerEvent.m_OnTriggerEventCallback = new Action<GameObject, Collider, TriggerEvent.TriggerType>(
                    (GameObject o, Collider c, TriggerEvent.TriggerType t) => OnBulletTriggerEvent.Invoke(__instance, new object[] { o, c, t })
                );
                Console.WriteLine("SHIELD 11");
                TriggerEvent triggerEvent = (TriggerEvent) m_TechTriggerEvent.GetValue(__instance);
                if (triggerEvent)
                {
                    triggerEvent.m_OnTriggerEventCallback = new Action<GameObject, Collider, TriggerEvent.TriggerType>(
                        (GameObject o, Collider c, TriggerEvent.TriggerType t) => OnTankTriggerEvent.Invoke(__instance, new object[] { o, c, t })
                    );
                }
                Console.WriteLine("SHIELD 12");
                m_SequenceNode.SetValue(__instance, new TechSequencer.SequenceNode(__instance.block));
                Console.WriteLine("SHIELD 13");
                return false;
            }
        }*/
    }
}
