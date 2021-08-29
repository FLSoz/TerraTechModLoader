using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ModManager
{
    internal class WrappedMod
    {
        private IManagedMod managedMod = null;
        private ModBase officialMod = null;

        public WrappedMod(IManagedMod managedMod)
        {
            this.managedMod = managedMod;
        }

        public WrappedMod(ModBase officialMod)
        {
            this.officialMod = officialMod;
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
                    this.officialMod.EarlyInit();
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
