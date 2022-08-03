using System.Reflection;
using HarmonyLib;

namespace ModManager.src.patches
{

    // Patches carried on from TTMM
    internal static class TTMMPatches
    {
        [HarmonyPatch(typeof(UIScreenBugReport), "Set")]
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

        [HarmonyPatch(typeof(UIScreenBugReport), "Post")]
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

        [HarmonyPatch(typeof(TerraTech.Network.LobbySystem), "GetInstalledModsHash")]
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
