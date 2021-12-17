using System;
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
        private ModBase officialMod = null;
        public readonly ModSource source;

        public WrappedMod(IManagedMod managedMod, ModSource source)
        {
            this.managedMod = managedMod;
            this.source = source;
        }

        public WrappedMod(ModBase officialMod, ModSource source)
        {
            this.officialMod = officialMod;
            this.source = source;
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

        public int LoadOrder
        {
            get
            {
                if (this.managedMod != null)
                {
                    return this.managedMod.loadOrder;
                }
                else
                {
                    return ModManager.DEFAULT_LOAD_ORDER;
                }
            }
        }

        public void EarlyInit()
        {
            if (this.managedMod != null)
            {
                this.managedMod.EarlyInit();
            }
            else
            {
                if (this.officialMod.HasEarlyInit())
                {
                    ModManager.logger.Info("Running EarlyInit for Mod {Mod}", Name);
                    this.officialMod.EarlyInit();
                }
                else
                {
                    ModManager.logger.Info("Mod {Mod} has no EarlyInit", Name);
                }
            }
        }

        public void Init()
        {
            if (this.managedMod != null)
            {
                this.managedMod.Init();
            }
            else
            {
                this.officialMod.Init();
            }
        }

        public void DeInit()
        {
            if (this.managedMod != null)
            {
                this.managedMod.DeInit();
            }
            else
            {
                this.officialMod.DeInit();
            }
        }
    }
}
