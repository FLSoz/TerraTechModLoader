using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Steamworks;
using TerraTech.Network;
using Payload.UI.Commands;
using Payload.UI.Commands.Steam;

namespace ModManager
{
    internal static class WorkshopLoader
    {
		internal static void OnDownloadComplete(SteamDownloadItemData result, bool remote)
		{
			ManMods manager = Singleton.Manager<ManMods>.inst;
			Dictionary<string, ModContainer> mods = (Dictionary<string, ModContainer>)ReflectedManMods.m_Mods.GetValue(manager);
			List<PublishedFileId_t> m_WaitingOnDownloads = (List<PublishedFileId_t>)ReflectedManMods.m_WaitingOnDownloads.GetValue(manager);
			string text = result.m_FileInfo.Name;
			if (text.EndsWith("_bundle"))
			{
				text = text.Substring(0, text.Length - "_bundle".Length);
			}
			else
			{
				ModManager.logger.Error("Mod asset {Name} doesn't end with _bundle", text);
			}
			m_WaitingOnDownloads.Remove(result.m_Details.m_nPublishedFileId);
			ModContainer modContainer = new ModContainer(text, result.m_FileInfo.FullName, false);
			modContainer.IsRemote = remote;
			if (!modContainer.HasValidID)
			{
				ModManager.logger.Error("Created mod container {Name}, but the modID was invalid", text);
				return;
			}
			if (!mods.ContainsKey(modContainer.ModID))
			{
				// Only successful case

				// This particluar scenario should never be false
				if (result.m_Details.m_nPublishedFileId != PublishedFileId_t.Invalid)
				{
					ModManager.workshopMetadata[result.m_Details.m_nPublishedFileId.m_PublishedFileId] = result;
				}
				mods.Add(modContainer.ModID, modContainer);
				ReflectedManMods.RequestModLoad.Invoke(manager, new object[] { modContainer.ModID });
				return;
			}
			ModContainer modContainer2 = mods[modContainer.ModID];
			if (modContainer2.Local)
			{
				ModManager.logger.Warn("Skipping registering Workshop mod with ID {ModID} in folder {WorkshopModFolder}, because we already have a local mod with the same ID from folder {LocalModFolder}",
					modContainer.ModID,
					modContainer.AssetBundlePath,
					modContainer2.AssetBundlePath
				);
				return;
			}
			ModManager.logger.Error("Failed to register Workshop mod with ID {ModID} in folder {WorkshopModFolder}, because we already have a local mod with the same ID from folder {LocalModFolder}",
				modContainer.ModID,
				modContainer.AssetBundlePath,
				modContainer2.AssetBundlePath
			);
		}

		internal static void OnDownloadCancelled(SteamDownloadItemData result, SteamDownloadItemData item)
		{
			ManMods manager = Singleton.Manager<ManMods>.inst;
			List<PublishedFileId_t> m_WaitingOnDownloads = (List<PublishedFileId_t>)ReflectedManMods.m_WaitingOnDownloads.GetValue(manager);
			ModManager.logger.Error("Downloading file {SteamID} from workshop failed", item.m_Details.m_nPublishedFileId);
			m_WaitingOnDownloads.Remove(result.m_Details.m_nPublishedFileId);
			Singleton.Manager<ManNetworkLobby>.inst.LeaveLobby();
			Singleton.Manager<ManGameMode>.inst.TriggerSwitch<ModeAttract>();
		}

		internal static void LoadWorkshopMod(SteamDownloadItemData item, bool remote)
        {
			CommandOperation<SteamDownloadItemData> commandOperation = new CommandOperation<SteamDownloadItemData>();
			commandOperation.AddConditional(new Func<SteamDownloadItemData, bool>(SteamConditions.CheckItemNeedsDownload), new Command<SteamDownloadItemData>[]
			{
				new SteamItemDownloadCommand()
			});
			commandOperation.AddConditional(new Func<SteamDownloadItemData, bool>(SteamConditions.CheckWaitingForDownload), new Command<SteamDownloadItemData>[]
			{
				new SteamItemWaitForDownloadCommand()
			});
			commandOperation.Add(new SteamItemGetDataFile());
			commandOperation.Add(new SteamLoadPreviewImageCommand());
			commandOperation.Cancelled.Subscribe(delegate (SteamDownloadItemData result) {
				OnDownloadCancelled(result, item);
			});
			commandOperation.Completed.Subscribe(delegate (SteamDownloadItemData result) {
				OnDownloadComplete(result, remote);
			});
			commandOperation.Execute(item);
		}
    }
}
