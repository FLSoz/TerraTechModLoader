using System.Text;
using System.IO;
using System.Diagnostics;
using HarmonyLib;
using UnityEngine;
using TerraTech.Network;

namespace ModManager.src.patches
{
    internal static class MultiplayerPatches
    {

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
                        Arguments = string.Format("{0} +connect_lobby {1} +custom_mod_list {2} +ttsmm_mod_list {3}", GetCurrentArgs(), lobbyID.m_NetworkID, $"[:{ModManager.WorkshopID}]", GetTTSMMModList(modList))
                    }
                }.Start();
                Application.Quit();
                return false;
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
                if (!ModManager.LoadedWithProperParameters)
                {
                    __result = false;
                    ModSessionInfo session = (ModSessionInfo)ReflectedManMods.m_CurrentSession.GetValue(Singleton.Manager<ManMods>.inst);
                    new Process
                    {
                        StartInfo =
                        {
                            FileName = GetExecutablePath(),
                            Arguments = string.Format("{0} +custom_mod_list {1} +ttsmm_mod_list {2}", GetCurrentArgs(), $"[:{ModManager.WorkshopID}]", GetTTSMMModList(session))
                        }
                    }.Start();
                    Application.Quit();
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
                        if (!modContainer.IsRemote && !modContainer.Local && (ulong)modContainer.Contents.m_WorkshopId > 0)
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

        /*
        [HarmonyPatch(typeof(ManMods), "PreLobbyCreated")]
        public static class PatchLobbyCreation
        {
            internal static FieldInfo m_CurrentLobbySession = AccessTools.Field(typeof(ManMods), "m_CurrentLobbySession");
            internal static FieldInfo m_CurrentSession = AccessTools.Field(typeof(ManMods), "m_CurrentSession");

            internal static void DeepCopy<T1, T2>(Dictionary<T1, T2> source, Dictionary<T1, T2> target)
            {
                if (source != null && target != null)
                {
                    foreach (KeyValuePair<T1, T2> pair in source)
                    {
                        target.Add(pair.Key, pair.Value);
                    }
                }
            }

            [HarmonyPostfix]
            public static void Postfix()
            {
                ModSessionInfo session = (ModSessionInfo) m_CurrentLobbySession.GetValue(Singleton.Manager<ManMods>.inst);
                ModSessionInfo currentSession = (ModSessionInfo) m_CurrentSession.GetValue(Singleton.Manager<ManMods>.inst);
                if (session != null && currentSession != null)
                {
                    ModManager.logger.Info("Patched MP lobby loading");

                    Dictionary<int, string> currentCorpIDs = currentSession.CorpIDs;
                    Dictionary<int, string> targetcorpIDs = session.CorpIDs;
                    DeepCopy(currentCorpIDs, targetcorpIDs);

                    Dictionary<int, string> currentSkinIDs = currentSession.SkinIDs;
                    if (currentSkinIDs != null && currentSkinIDs.Count > 0)
                    {
                        Dictionary<int, string> targetSkinIDs = session.SkinIDs;
                        if (targetSkinIDs == null)
                        {
                            targetSkinIDs = new Dictionary<int, string>();
                            session.SkinIDs = targetSkinIDs;
                        }
                        DeepCopy(currentSkinIDs, targetSkinIDs);
                    }

                    Dictionary<int, string> currentBlockIDs = currentSession.BlockIDs;
                    Dictionary<int, string> targetBlockIDs = session.BlockIDs;
                    DeepCopy(currentBlockIDs, targetBlockIDs);

                    ModManager.logger.Trace("Patched Lobby Session:");
                    PatchContentInjection.PrintSessionInfo(session);
                }
            }
        } */

    }
}
