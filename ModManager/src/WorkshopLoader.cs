using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using Steamworks;
using TerraTech.Network;
using Payload.UI.Commands;
using Payload.UI.Commands.Steam;
using HarmonyLib;

namespace ModManager
{
    internal static class WorkshopLoader
	{
		internal static FieldInfo m_DownloadResultCallback = typeof(SteamItemWaitForDownloadCommand).GetField("m_DownloadResultCallback", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
		internal static MethodInfo OnItemDownloaded = typeof(SteamItemWaitForDownloadCommand).GetMethod("OnItemDownloaded", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

		internal static Callback<DownloadItemResult_t> itemDownloadedCallback = Callback<DownloadItemResult_t>.Create(new Callback<DownloadItemResult_t>.DispatchDelegate(OnAnyItemDownloaded));
		internal static Dictionary<PublishedFileId_t, SteamItemWaitForDownloadCommand> workshopToCommandMap = new Dictionary<PublishedFileId_t, SteamItemWaitForDownloadCommand>();

		internal static void OnAnyItemDownloaded(DownloadItemResult_t result)
        {
			if (workshopToCommandMap.TryGetValue(result.m_nPublishedFileId, out SteamItemWaitForDownloadCommand appropriateCommand))
            {
				OnItemDownloaded.Invoke(appropriateCommand, new object[] { result });
            }
        }

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
			// Actual download operation
			CommandOperation<SteamDownloadItemData> commandOperation = new CommandOperation<SteamDownloadItemData>();
			commandOperation.AddConditional(new Func<SteamDownloadItemData, bool>(SteamConditions.CheckItemNeedsDownload), new Command<SteamDownloadItemData>[]
			{
				new SteamItemDownloadCommand()
			});

			// Use our custom callback
			SteamItemWaitForDownloadCommand downloadCommand = new SteamItemWaitForDownloadCommand();
			m_DownloadResultCallback.SetValue(downloadCommand, itemDownloadedCallback);
			workshopToCommandMap.Add(item.m_Details.m_nPublishedFileId, downloadCommand);
			commandOperation.AddConditional(new Func<SteamDownloadItemData, bool>(SteamConditions.CheckWaitingForDownload), new Command<SteamDownloadItemData>[]
			{
				downloadCommand
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


		// Patch for Steam mod fetching - Included from Community Patch
		[HarmonyPatch(typeof(ManMods), "OnSteamModsFetchComplete")]
		internal static class SubscribedModsPatch
		{
			internal const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
			internal static FieldInfo m_SteamQuerySubscribedOp = typeof(ManMods).GetField("m_SteamQuerySubscribedOp", flags);
			internal static FieldInfo m_WaitingOnDownloads = typeof(ManMods).GetField("m_WaitingOnDownloads", flags);
			internal static FieldInfo m_WaitingOnWorkshopCheck = typeof(ManMods).GetField("m_WaitingOnWorkshopCheck", flags);
			internal static MethodInfo LoadWorkshopData = typeof(ManMods).GetMethod("LoadWorkshopData", flags);

			internal static void CheckForMoreSteamMods(uint page)
			{
				Console.WriteLine($"[CommunityPatch] Attempting to fetch page {page} of subscribed mods");
				ManMods manMods = Singleton.Manager<ManMods>.inst;
				m_WaitingOnWorkshopCheck.SetValue(manMods, false);
				SteamDownloadData nextData = SteamDownloadData.Create(SteamItemCategory.Mods, page);
				CommandOperation<SteamDownloadData> operation = (CommandOperation<SteamDownloadData>)m_SteamQuerySubscribedOp.GetValue(manMods);
				operation.Execute(nextData);
			}

			[HarmonyPrefix]
			internal static bool Prefix(SteamDownloadData data)
			{
				// We're assuming Workshop is enabled if this has been called
				Console.WriteLine("[CommunityPatch] Received query resonse from Steam");
				if (data.HasAnyItems)
				{
					if (data.m_Items.Count >= Constants.kNumUGCResultsPerPage)
					{
						ManMods manMods = Singleton.Manager<ManMods>.inst;

						List<PublishedFileId_t> waitingOnDownloadList = (List<PublishedFileId_t>)m_WaitingOnDownloads.GetValue(manMods);
						for (int i = 0; i < data.m_Items.Count; i++)
						{
							SteamDownloadItemData steamDownloadItemData = data.m_Items[i];
							waitingOnDownloadList.Add(steamDownloadItemData.m_Details.m_nPublishedFileId);
							LoadWorkshopData.Invoke(manMods, new object[] { steamDownloadItemData, false });
						}

						uint currPage = data.m_Page;
						CheckForMoreSteamMods(currPage + 1);
						return false;
					}
					Console.WriteLine($"[CommunityPatch] Found {data.m_Items.Count}, assuming there's no more");
				}
				else
				{
					Console.WriteLine("[CommunityPatch] NO mods found");
				}
				return true;
			}
		}
	}
}
