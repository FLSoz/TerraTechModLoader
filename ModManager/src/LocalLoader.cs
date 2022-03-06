using System;
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ModManager
{
    internal static class LocalLoader
	{
		internal static readonly ManMods manager = Singleton.Manager<ManMods>.inst;
		internal static readonly Dictionary<string, ModContainer> mods = (Dictionary<string, ModContainer>)ReflectedManMods.m_Mods.GetValue(manager);

		internal static void LoadLocalMod(string name)
		{
			string undoSanitize = name.Replace(":/%20", " ");
			string modPath = Path.Combine(ModManager.TTSteamDir, ManMods.LocalModsDirectory, undoSanitize);
			if (Directory.Exists(modPath))
			{
				foreach (string text in Directory.GetDirectories(ManMods.LocalModsDirectory))
				{
					string text2 = text.Substring(ManMods.LocalModsDirectory.Length + 1);
					ModManager.logger.Info("Found mod in {ModFolder}. Resolved name as {ModName}", text, text2);
					ModContainer modContainer = new ModContainer(text2, string.Concat(new string[]
					{
						ManMods.LocalModsDirectory,
						"/",
						text2,
						"/",
						text2,
						"_bundle"
					}), true);
					if (modContainer.HasValidID)
					{
						if (!mods.ContainsKey(modContainer.ModID))
						{
							mods.Add(modContainer.ModID, modContainer);
							ReflectedManMods.RequestModLoad.Invoke(manager, new object[] { modContainer.ModID });
							ModManager.logger.Info("Loading local mod {ModID}", modContainer.ModID);
						}
						else
						{
							ModManager.logger.Error(
								"Failed to register mod with ID {ModID} from folder {ModFolder}, because we already have a mod with the same ID from folder {ExistingModFolder}",
								modContainer.ModID,
								text2,
								mods[modContainer.ModID].AssetBundlePath);
						}
					}
					else
					{
						ModManager.logger.Error("Created mod container {ModName}, but it did not correctly parse an ID", text2);
					}
				}
				return;
			}
			else
			{
				ModManager.logger.Error("Could not find local mod {Mod} ({Actual}) at {ModPath}", name, undoSanitize, modPath);
			}
		}
    }
}