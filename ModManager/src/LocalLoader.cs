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

		internal static void LoadLocalMod(string sanitizedName)
		{
			string localModName = sanitizedName.Replace(":/%20", " ");
			string modPath = Path.Combine(ModManager.TTSteamDir, ManMods.LocalModsDirectory, localModName);
			if (Directory.Exists(modPath))
			{
				ModManager.logger.Info("Found mod in {ModFolder}. Resolved name as {ModName}", modPath, localModName);
				ModContainer modContainer = new ModContainer(localModName, string.Concat(new string[]
				{
					ManMods.LocalModsDirectory,
					"/",
					localModName,
					"/",
					localModName,
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
							localModName,
							mods[modContainer.ModID].AssetBundlePath);
					}
				}
				else
				{
					ModManager.logger.Error("Created mod container {ModName}, but it did not correctly parse an ID", localModName);
				}
				return;
			}
			else
			{
				ModManager.logger.Error("Could not find local mod {Mod} ({Actual}) at {ModPath}", sanitizedName, localModName, modPath);
			}
		}
    }
}