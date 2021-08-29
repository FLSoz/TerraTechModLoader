using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Linq;
using UnityEngine;


using ModManager.Datastructures;

namespace ModManager
{
    public static class Patches
    {
        [HarmonyPatch(typeof(ManMods), "InitModScripts")]
        public static class PatchModScriptLoading
        {
            public static bool Prefix()
            {
                ModManager.logger.Info("InitModScripts Hook called");
                ModManager.PatchAssemblyLoading();

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
