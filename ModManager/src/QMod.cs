using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NLog;
using LogManager;


namespace ModManager
{
    internal class QMod
    {
        internal static Dictionary<string, int> DefaultOrder;
        private static NLog.Logger logger = NLog.LogManager.GetLogger("ModManager.QModLoader");
        internal static void ConfigureLogger()
        {
            Manager.LogConfig config = new Manager.LogConfig
            {
                layout = "${longdate} | ${level:uppercase=true:padding=-5:alignmentOnTruncation=left} | ${logger:shortName=true} | ${message}  ${exception}",
                keepOldFiles = false
            };
            Manager.RegisterLogger(logger, config);
        }

        private const string BlockInjector = "Aceba1/TTQMM-Nuterra-Block-Injector-Library";

        private string[] CloudNameDependencies;
        internal QMod[] Dependencies
        {
            get
            {
                if (this.CloudNameDependencies != null)
                {
                    return this.CloudNameDependencies.Select(name => ModManager.ResolveUnofficialMod(name)).Where(mod => mod != null).ToArray();
                }
                return null;
            }
        }

        private string ModId;
        private string DisplayName;
        private string CloudName;
        private string Author;
        private string Version;

        internal MethodInfo EntryMethod;
        internal Assembly LoadedAssembly;

        internal static QMod FromConfig(FileInfo modConfig, FileInfo ttmmConfig)
        {
            try
            {
                JObject configJson = Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(modConfig.FullName));
                if (configJson.TryGetValue("Enable", out JToken isEnabled) &&
                    !(bool)isEnabled)
                {
                    logger.Info("Found mod in directory {Folder}, but it's marked as disabled", modConfig.Directory.Name);
                    return null;
                }
                else
                {
                    if (configJson.TryGetValue("AssemblyName", out JToken assemblyName)) {
                        string modAssemblyPath = Path.Combine(modConfig.DirectoryName, (string) assemblyName);
                        if (configJson.TryGetValue("EntryMethod", out JToken entryMethodString))
                        {
                            string[] entryMethodSig = ((string) entryMethodString).Split('.');
                            string entryType = String.Join(".", entryMethodSig.Take(entryMethodSig.Length - 1).ToArray());
                            string entryMethod = entryMethodSig[entryMethodSig.Length - 1];

                            if (File.Exists(modAssemblyPath))
                            {
                                logger.Info("Initializing QMod for mod in directory {Directory}", modConfig.Directory.Name);
                                QMod mod = new QMod();

                                mod.LoadedAssembly = Assembly.LoadFrom(modAssemblyPath);
                                mod.EntryMethod = mod.LoadedAssembly.GetType(entryType).GetMethod(entryMethod);

                                if (configJson.TryGetValue("Id", out JToken modId))
                                {
                                    mod.ModId = (string)modId;
                                }
                                else
                                {
                                    mod.ModId = modConfig.Directory.Name;
                                }

                                if (configJson.TryGetValue("DisplayName", out JToken displayName))
                                {
                                    mod.DisplayName = (string)displayName;
                                }
                                else
                                {
                                    mod.DisplayName = modConfig.Directory.Name;
                                }

                                if (configJson.TryGetValue("Author", out JToken author))
                                {
                                    mod.Author = (string)author;
                                }
                                if (configJson.TryGetValue("Version", out JToken version))
                                {
                                    mod.Version = (string)version;
                                }

                                if (ttmmConfig.Exists)
                                {
                                    QModInfo modInfo = modInfo = JsonConvert.DeserializeObject<QModInfo>(File.ReadAllText(ttmmConfig.FullName), new JsonSerializerSettings() { MissingMemberHandling = MissingMemberHandling.Ignore });
                                    mod.CloudName = modInfo.CloudName;
                                    mod.CloudNameDependencies = modInfo.RequiredModNames;
                                    logger.Debug("READ ttmm.json");
                                    logger.Debug("CloudName: {CloudName}", modInfo.CloudName);
                                    logger.Debug("Dependencies: {Dependencies}", modInfo.RequiredModNames);
                                }
                                else
                                {
                                    logger.Debug("COULD NOT FIND ttmm.json");
                                }

                                if (mod.CloudName != null)
                                {
                                    if (QMod.DefaultOrder.TryGetValue(mod.CloudName, out int loadOrder))
                                    {
                                        mod.LoadOrder = loadOrder;
                                    }
                                    else
                                    {
                                        mod.LoadOrder = ModManager.DEFAULT_LOAD_ORDER - 1;
                                    }
                                }
                                else
                                {
                                    mod.LoadOrder = ModManager.DEFAULT_LOAD_ORDER;
                                }
                                return mod;
                            }
                            else
                            {
                                logger.Warn("Target assembly {Assembly} MISSING!", assemblyName);
                            }
                        }
                        else
                        {
                            logger.Warn("No entry method specified! Ignoring...");
                        }
                    }
                    else
                    {
                        logger.Warn("No assembly name specified! Ignoring...");
                    }
                }
            }
            catch
            {
                // If mod.json fails to parse, then assume it's enabled
                return null;
            }
            return null;
        }

        internal int LoadOrder {
            get;
            private set;
        }

        public override string ToString()
        {
            return $"{this.Name} ({this.ID})";
        }

        public string Name => this.DisplayName;

        public string ID => this.CloudName != null ? this.CloudName : this.ModId;

        public bool IsBlockInjector => this.CloudName == BlockInjector;

        public void Init()
        {
            this.EntryMethod.Invoke(this.LoadedAssembly, new object[] { });
        }
    }
}
