using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;

namespace ModManager
{
    internal class ManagedMod : IManagedMod
    {
        private ModBase _instance;
        public ModBase instance {
            get => this._instance;
            private set => this._instance = value;
        }

        private Type instanceType;
        private int _LoadOrder;

        private MethodInfo _GetEarlyLoadAfter;
        private MethodInfo _GetEarlyLoadBefore;
        private MethodInfo _GetLoadAfter;
        private MethodInfo _GetLoadBefore;

        private MethodInfo _ManagedEarlyInit;

        public String Name
        {
            get => this.instanceType.Name;
        }

        public static ManagedMod FromMod(Type mod)
        {
            FieldInfo _LoadOrder = mod.GetField("LoadOrder", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo _GetLoadAfter = mod.GetMethod("LoadAfter", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo _GetLoadBefore = mod.GetMethod("LoadBefore", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            MethodInfo _ManagedEarlyInit = mod.GetMethod("ManagedEarlyInit", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            if (_LoadOrder is null && _GetLoadAfter is null && _GetLoadBefore is null && _ManagedEarlyInit is null)
            {
                return null;
            }

            ManagedMod managedMod = new ManagedMod();
            ModBase instance = Activator.CreateInstance(mod) as ModBase;
            managedMod.instance = instance;
            managedMod.instanceType = mod;
            managedMod._LoadOrder = _LoadOrder != null ? (int)_LoadOrder.GetValue(null) : ModManager.DEFAULT_LOAD_ORDER;
            managedMod._GetLoadAfter = _GetLoadAfter;
            managedMod._GetLoadBefore = _GetLoadBefore;

            // Check if we want to allow dependency management on the EarlyInit
            if (_ManagedEarlyInit != null)
            {
                managedMod._ManagedEarlyInit = _ManagedEarlyInit;
                managedMod._GetEarlyLoadAfter = mod.GetMethod("EarlyLoadAfter", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                managedMod._GetEarlyLoadBefore = mod.GetMethod("EarlyLoadBefore", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            }
            return managedMod;
        }

        public Assembly Assembly => this.instanceType.Assembly;

        public int loadOrder => this._LoadOrder;

        public Type[] earlyLoadAfter {
            get {
                if (this._GetEarlyLoadAfter != null)
                {
                    return (Type[]) this._GetEarlyLoadAfter.Invoke(null, null);
                }
                return null;
            }
        }

        public Type[] earlyLoadBefore
        {
            get
            {
                if (this._GetEarlyLoadBefore != null)
                {
                    return (Type[])this._GetEarlyLoadBefore.Invoke(null, null);
                }
                return null;
            }
        }

        public Type[] loadAfter
        {
            get
            {
                if (this._GetLoadAfter != null)
                {
                    return (Type[])this._GetLoadAfter.Invoke(null, null);
                }
                return null;
            }
        }

        public Type[] loadBefore
        {
            get
            {
                if (this._GetLoadBefore != null)
                {
                    return (Type[])this._GetLoadBefore.Invoke(null, null);
                }
                return null;
            }
        }

        public void DeInit()
        {
            this.instance.DeInit();
        }

        public void EarlyInit()
        {
            if (this._ManagedEarlyInit != null)
            {
                this._ManagedEarlyInit.Invoke(this.instance, null);
            }
            else if (this.instance.HasEarlyInit())
            {
                this.instance.EarlyInit();
            }
        }

        public void Init()
        {
            this.instance.Init();
        }
    }
}
