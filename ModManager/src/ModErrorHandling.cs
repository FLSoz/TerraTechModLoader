using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;

namespace ModManager
{
    internal class ModErrorHandling
    {
        internal enum ModFailReason
        {
            InvalidBundle,
            InvalidBundleContents,
            ErrorLoadingScripts,
            DuplicateID,
            InvalidSkin,
            BrokenBlockPrefab,
            InvalidBlock,
            BlockInjectionError,
            EarlyInitError,
            InitError,
            LateInitError
        }

        internal static ManMods manMods = Singleton.Manager<ManMods>.inst;
        internal static Type enumType = typeof(ManMods).GetNestedType("ModFailReason", AccessTools.all);

        private static string SetNullToEmpty(string text)
        {
            return String.IsNullOrEmpty(text) ? "" : text;
        }

        internal static void SetModFailingReason(ModContainer container, ModFailReason reason, string context = null)
        {
            switch (reason)
            {
                case ModFailReason.InvalidBundle:
                    ReflectedManMods.HandleModLoadingFailed.Invoke(manMods, new object[] { container, Enum.ToObject(enumType, 0), context });
                    break;
                case ModFailReason.InvalidBundleContents:
                    ReflectedManMods.HandleModLoadingFailed.Invoke(manMods, new object[] { container, Enum.ToObject(enumType, 1), context });
                    break;
                case ModFailReason.ErrorLoadingScripts:
                    ReflectedManMods.HandleModLoadingFailed.Invoke(manMods, new object[] { container, Enum.ToObject(enumType, 2), context });
                    break;
                case ModFailReason.DuplicateID:
                    ReflectedManMods.HandleModLoadingFailed.Invoke(manMods, new object[] { container, Enum.ToObject(enumType, 3), context });
                    break;
                case ModFailReason.InvalidSkin:
                    ReflectedManMods.HandleModLoadingFailed.Invoke(manMods, new object[] { container, Enum.ToObject(enumType, 4), context });
                    break;
                case ModFailReason.BrokenBlockPrefab:
                    ReflectedManMods.HandleModLoadingFailed.Invoke(manMods, new object[] { container, Enum.ToObject(enumType, 5), "Broken block prefab, likely code mod failure: " + SetNullToEmpty(context) });
                    break;
                case ModFailReason.InvalidBlock:
                    ReflectedManMods.HandleModLoadingFailed.Invoke(manMods, new object[] { container, Enum.ToObject(enumType, 5), "Failed block initialization, likely block mod/block dependency failure: " + SetNullToEmpty(context) });
                    break;
                case ModFailReason.BlockInjectionError:
                    ReflectedManMods.HandleModLoadingFailed.Invoke(manMods, new object[] { container, Enum.ToObject(enumType, 5), "Failed block injection, likely corp/code mod failure: " + SetNullToEmpty(context) });
                    break;
                case ModFailReason.EarlyInitError:
                    ReflectedManMods.HandleModLoadingFailed.Invoke(manMods, new object[] { container, Enum.ToObject(enumType, 6), "EarlyInitError: " + SetNullToEmpty(context) });
                    break;
                case ModFailReason.InitError:
                    ReflectedManMods.HandleModLoadingFailed.Invoke(manMods, new object[] { container, Enum.ToObject(enumType, 6), "InitError: " + SetNullToEmpty(context) });
                    break;
                case ModFailReason.LateInitError:
                    ReflectedManMods.HandleModLoadingFailed.Invoke(manMods, new object[] { container, Enum.ToObject(enumType, 6), "LateInitError: " + SetNullToEmpty(context) });
                    break;
            }
        }

    }
}
