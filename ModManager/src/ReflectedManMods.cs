using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace ModManager
{
    internal class ReflectedManMods
    {
        internal static readonly FieldInfo m_CurrentSession = AccessTools.Field(typeof(ManMods), "m_CurrentSession");
        internal static readonly FieldInfo m_RequestedSession = AccessTools.Field(typeof(ManMods), "m_RequestedSession");
        internal static readonly FieldInfo m_LoadingRequestedSessionInProgress = AccessTools.Field(typeof(ManMods), "m_LoadingRequestedSessionInProgress");
        internal static readonly FieldInfo m_AutoAddModsToAuthoritativeSessions = AccessTools.Field(typeof(ManMods), "m_AutoAddModsToAuthoritativeSessions");
        internal static readonly FieldInfo m_Mods = AccessTools.Field(typeof(ManMods), "m_Mods");
        internal static readonly FieldInfo m_WaitingOnDownloads = AccessTools.Field(typeof(ManMods), "m_WaitingOnDownloads");
        internal static readonly FieldInfo m_ReloadAllPending = AccessTools.Field(typeof(ManMods), "m_ReloadAllPending");

        internal static readonly MethodInfo RequestModLoad = AccessTools.Method(typeof(ManMods), "RequestModLoad");
        internal static readonly MethodInfo CheckReparseAllJsons = AccessTools.Method(typeof(ManMods), "CheckReparseAllJsons");
        internal static readonly MethodInfo CheckReloadAllMods = AccessTools.Method(typeof(ManMods), "CheckReloadAllMods");
        internal static readonly MethodInfo AutoAssignSessionIDs =
            AccessTools.Method(
                typeof(ManMods), "AutoAssignIDs",
                parameters: new Type[] { typeof(ModSessionInfo), typeof(List<string>), typeof(Dictionary<string, List<string>>), typeof(List<string>)}
            );
        internal static readonly MethodInfo PurgeModdedContentFromGame = AccessTools.Method(typeof(ManMods), "PurgeModdedContentFromGame");
        internal static readonly MethodInfo InjectModdedContentIntoGame = AccessTools.Method(typeof(ManMods), "InjectModdedContentIntoGame");
        internal static readonly MethodInfo AutoAddModsToSession = AccessTools.Method(typeof(ManMods), "AutoAddModsToSession");
    }
}
