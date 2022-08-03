using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Runtime.Serialization.Formatters;
using HarmonyLib;
using LogManager;
using NLog;
using Newtonsoft.Json;
using UnityEngine;
using Ionic.Zlib;
using Snapshots;

namespace ModManager.patches
{
    internal static class SerializationLoggingPatches
    {
        private static NLog.Logger logger = NLog.LogManager.GetLogger("NewtonsoftSerialization");
        private static void ConfigureLogger()
        {
            LogConfig config = new LogManager.LogConfig
            {
                layout = "${longdate} | ${level:uppercase=true:padding=-5:alignmentOnTruncation=left} | ${message}  ${exception}",
                keepOldFiles = false,
                defaultMinLevel = LogLevel.Fatal
            };
            TTLogManager.RegisterLogger(logger, config);
        }

        private static JsonSerializerSettings QuietSerializationSettings;

        public static void Setup()
        {
            ConfigureLogger();
            FieldInfo s_JSONSerialisationSettings = AccessTools.Field(typeof(ManSaveGame), "s_JSONSerialisationSettings");
            JsonSerializerSettings quietSettings = new JsonSerializerSettings
            {
                TypeNameHandling = TypeNameHandling.Auto,
                TypeNameAssemblyFormat = FormatterAssemblyStyle.Simple,
                Formatting = Formatting.None,
                Error = delegate (object sender, Newtonsoft.Json.Serialization.ErrorEventArgs args)
                {
                    SerializationLoggingPatches.logger.Error($"JSON ERROR: {args.ErrorContext.Error.Message}\nPath={args.ErrorContext.Path}\nFullException={args.ErrorContext.Error.ToString()}");
                }
            };
            QuietSerializationSettings = quietSettings;
            s_JSONSerialisationSettings.SetValue(null, quietSettings);
        }

        [HarmonyPatch(typeof(ManScreenshot), "TryDecodeSnapshotRenderInternal")]
        public static class PatchSnapshotRender
        {
            internal static MethodInfo DecodeFromPixels = AccessTools.Method(typeof(ManScreenshot), "DecodeFromPixels");

            internal static int WrappedDecodeFromPixels(Color32[] carrierPixels, int offset, int numBytes, out byte[] messageBytes)
            {
                object[] args = new object[] { carrierPixels, offset, numBytes, new byte[0] };
                int result = (int)DecodeFromPixels.Invoke(null, args);
                object array = args[3];
                messageBytes = (byte[])array;
                return result;
            }

            internal static void LoadObjectFromRawJson<T>(ref T objectToLoad, string rawJson, bool assertOnFail, bool validate)
            {
                bool flag = true;
                if (validate)
                {
                    logger.Error("ManSaveGame | LoadObjectFromRawJson - JSON validation not currently supported!");
                }
                try
                {
                    T t = JsonConvert.DeserializeObject<T>(rawJson, QuietSerializationSettings);
                    // Since this is a struct, we always take the output
                    if (t != null)
                    {
                        objectToLoad = t;
                    }
                    else
                    {
                        logger.Error("LoadObjectFromRawJson: newObject is NULL  RawJSON=" + rawJson);
                        flag = false;
                    }
                }
                catch (Exception ex)
                {
                    string json = (rawJson == null) ? "NULL" : rawJson;
                    logger.Error($"LoadObjectFromRawJson:\n{ex.ToString()}\n  RawJson={json}");
                }
                if (!flag)
                {
                    throw new InvalidCastException("Json object not valid for " + objectToLoad.GetType());
                }
            }

            [HarmonyPrefix]
            internal static bool Prefix(Texture2D snapshotRender, out TechData.SerializedSnapshotData techSnapshotData, string filename, out bool __result)
            {
                bool flag = !SKU.ConsoleUI;
                d.Assert(flag, "TryDecodeSnapshotRender not supported when running console-style");
                if (snapshotRender == null || !flag)
                {
                    techSnapshotData = default(TechData.SerializedSnapshotData);
                    __result = false;
                    return false;
                }
                bool result = false;
                Color32[] pixels = snapshotRender.GetPixels32();
                try
                {
                    byte[] bytes = Encoding.ASCII.GetBytes("TTTechData");
                    int num = 0;
                    num += WrappedDecodeFromPixels(pixels, num, bytes.Length, out byte[] array);
                    if (array.SequenceEqual(bytes))
                    {
                        num += WrappedDecodeFromPixels(pixels, num, 1, out array);
                        byte b = array[0];
                        num += WrappedDecodeFromPixels(pixels, num, 4, out array);
                        int numBytes = BitConverter.ToInt32(array, 0);
                        num += WrappedDecodeFromPixels(pixels, num, numBytes, out array);
                        string rawJson;
                        using (MemoryStream memoryStream = new MemoryStream(array, false))
                        {
                            using (GZipStream gzipStream = new GZipStream(memoryStream, CompressionMode.Decompress))
                            {
                                using (StreamReader streamReader = new StreamReader(gzipStream))
                                {
                                    rawJson = streamReader.ReadLine();
                                }
                            }
                        }
                        TechData.SerializedSnapshotData serializedSnapshotData = default(TechData.SerializedSnapshotData);
                        LoadObjectFromRawJson(ref serializedSnapshotData, rawJson, true, false);
                        result = true;
                        techSnapshotData = serializedSnapshotData;
                    }
                    else
                    {
                        logger.Warn("TryDecodeSnapshotRenderInternal - Skipping potential snapshot file '" + filename + "' as it does not have the correct format identifier.");
                        result = false;
                        techSnapshotData = default(TechData.SerializedSnapshotData);
                    }
                }
                catch (Exception ex)
                {
                    logger.Warn("Deserialisation exception:\n" + ex.ToString());
                    result = false;
                    techSnapshotData = default(TechData.SerializedSnapshotData);
                }
                __result = result;
                return false;
            }
        }

        [HarmonyPatch(typeof(SnapshotServiceDesktop), "GetSnapshotFromCache")]
        public static class PatchSnapshotLoading
        {
            internal static readonly Type TechDataCacheType = AccessTools.Inner(typeof(SnapshotServiceDesktop), "TechDataCache");
            internal static readonly MethodInfo CustomLoadObject = AccessTools.Method(typeof(PatchSnapshotLoading), nameof(PatchSnapshotLoading.LoadObject), generics: new Type[] { TechDataCacheType });
            internal static readonly FieldInfo techSnapshotData = AccessTools.Field(TechDataCacheType, "techSnapshotData");
            internal static readonly FieldInfo m_FavouritedSnapshotUIDS = AccessTools.Field(typeof(SnapshotServiceDesktop), "m_FavouritedSnapshotUIDs");

            internal static bool LoadObject<T>(ref T objectToLoad, string path, bool assertOnFail = true, bool validate = false)
            {
                bool result = false;
                if (File.Exists(path))
                {
                    try
                    {
                        using (FileStream fileStream = File.Open(path, FileMode.Open, FileAccess.Read))
                        {
                            using (GZipStream gzipStream = new GZipStream(fileStream, CompressionMode.Decompress))
                            {
                                using (StreamReader streamReader = new StreamReader(gzipStream))
                                {
                                    string rawJson = streamReader.ReadToEnd();
                                    PatchSnapshotRender.LoadObjectFromRawJson<T>(ref objectToLoad, rawJson, assertOnFail, validate);
                                    result = true;
                                }
                            }
                        }
                        return result;
                    }
                    catch (Exception arg)
                    {
                        logger.Error(string.Format("ManSaveGame.LoadObject Caught Exception:\n{0}", arg));
                        return false;
                    }
                }
                d.Assert(!assertOnFail);
                return result;
            }

            [HarmonyPrefix]
            internal static bool Prefix(SnapshotServiceDesktop __instance, FileInfo fileInfo, out SnapshotDisk __result)
            {
                if (!__instance.IsSnapshotCacheValid(fileInfo))
                {
                    if (!__instance.TryCreateSnapshotCache(fileInfo))
                    {
                        logger.Error("SnapshotServiceDesktop.GetSnapshotFromCache - Snapshot had invalid tech data. Aborting cache retrieval. {0}", new object[]
                        {
                fileInfo.FullName
                        });
                        __result = null;
                        return false;
                    }
                    logger.Warn("SnapshotServiceDesktop.GetSnapshotFromCache - Found invalid cache. Updating this during gameplay is expensive. {0}", new object[]
                    {
            fileInfo.FullName
                    });
                }
                string filePathCachedMetaData = SnapshotServiceDesktop.GetFilePathCachedMetaData(fileInfo);
                object techDataCache = Activator.CreateInstance(TechDataCacheType);
                object[] args = new object[] { techDataCache, filePathCachedMetaData, true, false };
                bool flag = (bool) CustomLoadObject.Invoke(null, args);
                techDataCache = args[0];
                SnapshotDisk snapshotDisk = null;
                if (flag)
                {
                    snapshotDisk = new SnapshotDisk();
                    snapshotDisk.snapName = fileInfo.FullName;
                    TechData.SerializedSnapshotData snapshotData = (TechData.SerializedSnapshotData)techSnapshotData.GetValue(techDataCache);
                    snapshotDisk.techData = snapshotData.CreateTechData();
                    snapshotDisk.UniqueID = fileInfo.FullName;
                    snapshotDisk.DateCreated = fileInfo.CreationTime;
                    HashSet<string> favoriteSnaps = (HashSet<string>) m_FavouritedSnapshotUIDS.GetValue(__instance);
                    snapshotDisk.m_IsFavourite.Value = favoriteSnaps.Contains(snapshotDisk.UniqueID);
                }
                else
                {
                    logger.Error("Failed to load tech data cache from file: " + filePathCachedMetaData);
                }
                __result = snapshotDisk;
                return false;
            }
        }

        [HarmonyPatch(typeof(ManSaveGame), "SaveObjectToRawJson")]
        public static class PatchIgnoreSaveObjectToRawJson
        {
            [HarmonyPrefix]
            internal static bool Prefix(object objectToSave, out string __result)
            {
                string text;
                try
                {
                    text = JsonConvert.SerializeObject(objectToSave, QuietSerializationSettings);
                }
                catch (Exception ex)
                {
                    logger.Error($"SaveObjectToRawJson: objectToSaveType={objectToSave.GetType().Name} Exception={ex.Message}");
                    text = null;
                }
                if (text != null && text == "null")
                {
                    text = null;
                }
                __result = text;
                return false;
            }
        }
    }
}
