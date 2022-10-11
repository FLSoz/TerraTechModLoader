using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Steamworks;

namespace ModManager
{
    internal struct ModSource
    {
        public string ID;
        public string Name;
        public PublishedFileId_t WorkshopID;
        public bool IsWorkshop;
    }

    internal class WrappedMod
    {
        private IManagedMod managedMod = null;
        internal ModBase officialMod = null;
        public readonly ModSource source;
        internal bool earlyInitRun = false;
        internal string ModID;
        internal ModContainer container;

        public WrappedMod(IManagedMod managedMod, ModContainer container, ModSource source)
        {
            this.managedMod = managedMod;
            this.source = source;
            this.container = container;
        }

        public WrappedMod(ModBase officialMod, ModContainer container, ModSource source)
        {
            this.officialMod = officialMod;
            this.source = source;
            this.container = container;
        }

        public IManagedMod ManagedMod()
        {
            return this.managedMod;
        }

        public string Name
        {
            get
            {
                if (this.managedMod != null)
                {
                    return this.managedMod.Name;
                }
                else
                {
                    return this.officialMod.GetType().Name;
                }
            }
        }

        public int InitOrder
        {
            get
            {
                if (this.managedMod != null)
                {
                    return this.managedMod.InitOrder;
                }
                else
                {
                    return ModManager.DEFAULT_LOAD_ORDER;
                }
            }
        }

        public int EarlyInitOrder
        {
            get
            {
                if (this.managedMod != null)
                {
                    return this.managedMod.EarlyInitOrder;
                }
                else
                {
                    return ModManager.DEFAULT_LOAD_ORDER;
                }
            }
        }

        public int UpdateOrder
        {
            get
            {
                if (this.managedMod != null)
                {
                    return this.managedMod.UpdateOrder;
                }
                else
                {
                    return ModManager.DEFAULT_LOAD_ORDER;
                }
            }
        }

        public int FixedUpdateOrder
        {
            get
            {
                if (this.managedMod != null)
                {
                    return this.managedMod.FixedUpdateOrder;
                }
                else
                {
                    return ModManager.DEFAULT_LOAD_ORDER;
                }
            }
        }

        public bool HasUpdate
        {
            get
            {
                if (this.managedMod != null)
                {
                    return this.managedMod.HasUpdate;
                }
                return false;
            }
        }

        public bool HasFixedUpdate
        {
            get
            {
                if (this.managedMod != null)
                {
                    return this.managedMod.HasFixedUpdate;
                }
                return false;
            }
        }

        public bool IsRemote
        {
            get => this.container.IsRemote;
        }

        public IEnumerator<float> EarlyInit()
        {
            yield return 0.0f;
            if (this.managedMod != null)
            {
                ModdedContentLoader.logger.Trace("  👉 Running EarlyInit for MANAGED Mod {Mod}", Name);
                IEnumerator<float> iterator = this.managedMod.EarlyInit();
                while (iterator.MoveNext())
                {
                    yield return iterator.Current;
                }
            }
            else
            {
                if (this.officialMod.HasEarlyInit())
                {
                    ModdedContentLoader.logger.Info("  👉 Running EarlyInit for Mod {Mod}", Name);
                    this.officialMod.EarlyInit();
                }
                else
                {
                    ModdedContentLoader.logger.Info("  🛈 Mod {Mod} has no EarlyInit", Name);
                }
            }
            yield return 1.0f;
            yield break;
        }

        public IEnumerator<float> Init()
        {
            yield return 0.0f;
            if (this.managedMod != null)
            {
                ModdedContentLoader.logger.Trace("  👉 Running Init for MANAGED Mod {Mod}", Name);
                IEnumerator<float> iterator = this.managedMod.Init();
                while (iterator.MoveNext())
                {
                    yield return iterator.Current;
                }
            }
            else
            {
                ModdedContentLoader.logger.Trace("  👉 Running Init for NON-MANAGED Mod {Mod}", Name);
                this.officialMod.Init();
            }
            yield return 1.0f;
            yield break;
        }

        public IEnumerator<float> DeInit()
        {
            yield return 0.0f;
            if (this.managedMod != null)
            {
                IEnumerator<float> iterator = this.managedMod.DeInit();
                while (iterator.MoveNext())
                {
                    yield return iterator.Current;
                }
            }
            else
            {
                this.officialMod.DeInit();
            }
            yield return 1.0f;
            yield break;
        }

        public void Update()
        {
            if (this.managedMod != null)
            {
                this.managedMod.Update();
            }
            else
            {
                this.officialMod.Update();
            }
        }

        public void FixedUpdate()
        {
            if (this.managedMod != null)
            {
                this.managedMod.FixedUpdate();
            }
            else
            {
                this.officialMod.FixedUpdate();
            }
        }
    }
}
