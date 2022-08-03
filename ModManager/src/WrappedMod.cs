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
                ModManager.logger.Trace("Running Init for MANAGED Mod {Mod}", Name);
                this.managedMod.Init();
            }
            else
            {
                ModManager.logger.Trace("Running Init for NON-MANAGED Mod {Mod}", Name);
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
