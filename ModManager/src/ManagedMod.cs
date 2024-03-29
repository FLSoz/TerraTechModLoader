﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;
using HarmonyLib;

namespace ModManager
{
    internal class ManagedMod : IManagedMod
    {
        private ModBase _instance;
        public ModBase Instance {
            get => this._instance;
            private set => this._instance = value;
        }

        private Type instanceType;
        private int _InitOrder;
        private int _EarlyInitOrder;
        private int _LateInitOrder;
        private int _UpdateOrder;
        private int _FixedUpdateOrder;

        private bool _HasUpdate;
        private bool _HasFixedUpdate;

        private MethodInfo _GetEarlyLoadAfter;
        private MethodInfo _GetEarlyLoadBefore;
        private MethodInfo _GetLateLoadAfter;
        private MethodInfo _GetLateLoadBefore;
        private MethodInfo _GetLoadAfter;
        private MethodInfo _GetLoadBefore;
        private MethodInfo _GetUpdateAfter;
        private MethodInfo _GetUpdateBefore;
        private MethodInfo _GetFixedUpdateAfter;
        private MethodInfo _GetFixedUpdateBefore;

        private MethodInfo _ManagedEarlyInit;
        private MethodInfo _LateInit;

        private MethodInfo _ManagedIteratorEarlyInit;
        private MethodInfo _ManagedIteratorInit;
        private MethodInfo _ManagedIteratorDeInit;
        private MethodInfo _ManagedIteratorLateInit;

        public String Name
        {
            get => this.instanceType.Name;
        }

        private static MethodInfo FirstOrNull(IEnumerable<MethodInfo> collection, Func<MethodInfo, bool> func)
        {
            try
            {
                return collection.First(func);
            }
            catch (System.InvalidOperationException)
            {
                return null;
            }
        }

        public static ManagedMod FromMod(Type mod)
        {
            ModManager.logger.Trace($"🟢 Setting up ManagedMod for type {mod}");
            MethodInfo _ManagedEarlyInit = AccessTools.Method(mod, "ManagedEarlyInit");

            MethodInfo[] allMethods = mod.GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            MethodInfo _LateInit = AccessTools.Method(mod, "LateInit");
            MethodInfo _LateInitIterator = FirstOrNull(allMethods, m => m.Name == "LateInitIterator" && m.ReturnType == typeof(IEnumerator<float>));

            MethodInfo _ManagedIteratorEarlyInit = FirstOrNull(allMethods, m => m.Name == "EarlyInitIterator" && m.ReturnType == typeof(IEnumerator<float>));
            MethodInfo _ManagedIteratorInit = FirstOrNull(allMethods, m => m.Name == "InitIterator" && m.ReturnType == typeof(IEnumerator<float>));
            MethodInfo _ManagedIteratorDeInit = FirstOrNull(allMethods, m => m.Name == "DeInitIterator" && m.ReturnType == typeof(IEnumerator<float>));

            FieldInfo _LoadOrder = AccessTools.Field(mod, "LoadOrder");
            FieldInfo _InitOrder = AccessTools.Field(mod, "InitOrder");
            FieldInfo _EarlyInitOrder = AccessTools.Field(mod, "EarlyInitOrder");
            FieldInfo _LateInitOrder = AccessTools.Field(mod, "LateInitOrder");

            MethodInfo _GetLoadAfter = AccessTools.Method(mod, "LoadAfter");
            MethodInfo _GetLoadBefore = AccessTools.Method(mod, "LoadBefore");

            MethodInfo _Update = AccessTools.Method(mod, "Update");
            MethodInfo _FixedUpdate = AccessTools.Method(mod, "FixedUpdate");

            if (
                _LoadOrder is null && _InitOrder is null && _GetLoadAfter is null && _GetLoadBefore is null &&
                _Update is null && _FixedUpdate is null && _ManagedEarlyInit is null
            )
            {
                return null;
            }

            ManagedMod managedMod = new ManagedMod();
            ModBase instance = Activator.CreateInstance(mod) as ModBase;
            managedMod.Instance = instance;
            managedMod.instanceType = mod;

            // setup InitOrder
            if (_InitOrder != null)
            {
                managedMod._InitOrder = (int)_InitOrder.GetValue(null);
            }
            else if (_LoadOrder != null)
            {
                managedMod._InitOrder = (int)_LoadOrder.GetValue(null);
            }
            else
            {
                managedMod._InitOrder = ModManager.DEFAULT_LOAD_ORDER;
            }

            // setup EarlyInitOrder
            if (_EarlyInitOrder != null)
            {
                managedMod._EarlyInitOrder = (int)_EarlyInitOrder.GetValue(null);
            }
            else if (_LoadOrder != null)
            {
                managedMod._EarlyInitOrder = (int)_LoadOrder.GetValue(null);
            }
            else
            {
                managedMod._EarlyInitOrder = ModManager.DEFAULT_LOAD_ORDER;
            }

            managedMod._GetLoadAfter = _GetLoadAfter;
            managedMod._GetLoadBefore = _GetLoadBefore;

            // If it's overridden, then we handle it
            if (_Update != null && _Update.DeclaringType != typeof(ModBase))
            {
                managedMod._GetUpdateAfter = AccessTools.Method(mod, "UpdateAfter");
                managedMod._GetUpdateBefore = AccessTools.Method(mod, "UpdateBefore");

                FieldInfo _UpdateOrder = AccessTools.Field(mod, "UpdateOrder");
                if (_UpdateOrder != null)
                {
                    managedMod._UpdateOrder = (int)_UpdateOrder.GetValue(null);
                }
                else
                {
                    managedMod._UpdateOrder = ModManager.DEFAULT_LOAD_ORDER;
                }

                managedMod._HasUpdate = true;
            }
            if (_FixedUpdate != null && _FixedUpdate.DeclaringType != typeof(ModBase))
            {
                managedMod._GetFixedUpdateAfter = AccessTools.Method(mod, "FixedUpdateAfter");
                managedMod._GetFixedUpdateBefore = AccessTools.Method(mod, "FixedUpdateBefore");

                FieldInfo _FixedUpdateOrder = AccessTools.Field(mod, "FixedUpdateOrder");
                if (_FixedUpdateOrder != null)
                {
                    managedMod._FixedUpdateOrder = (int)_FixedUpdateOrder.GetValue(null);
                }
                else
                {
                    managedMod._FixedUpdateOrder = ModManager.DEFAULT_LOAD_ORDER;
                }

                managedMod._HasFixedUpdate = true;
            }

            // Check if we want to allow dependency management on the EarlyInit
            if (_ManagedEarlyInit != null || _ManagedIteratorEarlyInit != null)
            {
                managedMod._ManagedIteratorEarlyInit = _ManagedIteratorEarlyInit;
                managedMod._ManagedEarlyInit = _ManagedEarlyInit;
                managedMod._GetEarlyLoadAfter = AccessTools.Method(mod, "EarlyLoadAfter");
                managedMod._GetEarlyLoadBefore = AccessTools.Method(mod, "EarlyLoadBefore");
            }

            managedMod._ManagedIteratorInit = _ManagedIteratorInit;
            managedMod._ManagedIteratorDeInit = _ManagedIteratorDeInit;

            if (_LateInitOrder != null)
            {
                managedMod._LateInitOrder = (int)_LateInitOrder.GetValue(null);
            }
            if (_LateInit != null || _LateInitIterator != null)
            {
                managedMod._LateInit = _LateInit;
                managedMod._ManagedIteratorLateInit = _LateInitIterator;
                managedMod._GetLateLoadAfter = AccessTools.Method(mod, "LateLoadAfter");
                managedMod._GetLateLoadBefore = AccessTools.Method(mod, "LateLoadBefore");
            }
            return managedMod;
        }

        public Assembly Assembly => this.instanceType.Assembly;

        public int loadOrder => this._InitOrder;

        public int InitOrder => this._InitOrder;

        public int EarlyInitOrder => this._EarlyInitOrder;

        public int UpdateOrder => this._UpdateOrder;

        public int FixedUpdateOrder => this._FixedUpdateOrder;

        public int LateInitOrder => this._LateInitOrder;

        public Type[] EarlyLoadAfter {
            get {
                if (this._GetEarlyLoadAfter != null)
                {
                    return (Type[]) this._GetEarlyLoadAfter.Invoke(null, null);
                }
                return null;
            }
        }

        public Type[] EarlyLoadBefore
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

        public Type[] LateLoadAfter
        {
            get
            {
                if (this._GetLateLoadAfter != null)
                {
                    return (Type[])this._GetLateLoadAfter.Invoke(null, null);
                }
                return null;
            }
        }

        public Type[] LateLoadBefore
        {
            get
            {
                if (this._GetLateLoadBefore != null)
                {
                    return (Type[])this._GetLateLoadBefore.Invoke(null, null);
                }
                return null;
            }
        }

        public Type[] LoadAfter
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

        public Type[] LoadBefore
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

        public Type[] UpdateAfter
        {
            get
            {
                if (this._GetUpdateAfter != null)
                {
                    return (Type[])this._GetUpdateAfter.Invoke(null, null);
                }
                return null;
            }
        }

        public Type[] UpdateBefore
        {
            get
            {
                if (this._GetUpdateBefore != null)
                {
                    return (Type[])this._GetUpdateBefore.Invoke(null, null);
                }
                return null;
            }
        }

        public Type[] FixedUpdateAfter
        {
            get
            {
                if (this._GetFixedUpdateAfter != null)
                {
                    return (Type[])this._GetFixedUpdateAfter.Invoke(null, null);
                }
                return null;
            }
        }

        public Type[] FixedUpdateBefore
        {
            get
            {
                if (this._GetFixedUpdateBefore != null)
                {
                    return (Type[])this._GetFixedUpdateBefore.Invoke(null, null);
                }
                return null;
            }
        }

        public bool HasUpdate
        {
            get => this._HasUpdate;
        }

        public bool HasFixedUpdate
        {
            get => this._HasFixedUpdate;
        }

        public IEnumerator<float> DeInit()
        {
            if (this._ManagedIteratorDeInit != null)
            {
                ModdedContentLoader.logger.Trace($"   🛠️ Iterator DeInit of {this.instanceType.FullName}");
                IEnumerator<float> iterator = (IEnumerator<float>)this._ManagedIteratorDeInit.Invoke(this.Instance, null);
                while (iterator.MoveNext())
                {
                    yield return iterator.Current;
                }
            }
            else
            {
                ModdedContentLoader.logger.Trace($"   🔨 Standard DeInit of {this.instanceType.FullName}");
                this.Instance.DeInit();
            }
            yield break;
        }

        public IEnumerator<float> EarlyInit()
        {
            if (this._ManagedIteratorEarlyInit != null)
            {
                ModdedContentLoader.logger.Trace($"   🛠️ Iterator EarlyInit of {this.instanceType.FullName}");
                IEnumerator<float> iterator = (IEnumerator<float>) this._ManagedIteratorEarlyInit.Invoke(this.Instance, null);
                while (iterator.MoveNext())
                {
                    yield return iterator.Current;
                }
            }
            else if (this._ManagedEarlyInit != null)
            {
                ModdedContentLoader.logger.Trace($"   🛠️ Managed EarlyInit of {this.instanceType.FullName}");
                this._ManagedEarlyInit.Invoke(this.Instance, null);
            }
            else if (this.Instance.HasEarlyInit())
            {
                ModdedContentLoader.logger.Trace($"   🔨 Standard EarlyInit of {this.instanceType.FullName}");
                this.Instance.EarlyInit();
            }
            else
            {
                ModdedContentLoader.logger.Trace($"   🛈 NO EarlyInit of {this.instanceType.FullName}");
            }
            yield break;
        }

        public IEnumerator<float> Init()
        {
            if (this._ManagedIteratorInit != null)
            {
                ModdedContentLoader.logger.Trace($"   🛠️ Iterator Init of {this.instanceType.FullName}");
                IEnumerator<float> iterator = (IEnumerator<float>)this._ManagedIteratorInit.Invoke(this.Instance, null);
                while (iterator.MoveNext())
                {
                    yield return iterator.Current;
                }
            }
            else
            {
                ModdedContentLoader.logger.Trace($"   🔨 Standard Init of {this.instanceType.FullName}");
                this.Instance.Init();
            }
            yield break;
        }

        public IEnumerator<float> LateInit()
        {
            if (this._ManagedIteratorLateInit != null)
            {
                ModdedContentLoader.logger.Trace($"   🛠️ Iterator LateInit of {this.instanceType.FullName}");
                IEnumerator<float> iterator = (IEnumerator<float>)this._ManagedIteratorLateInit.Invoke(this.Instance, null);
                while (iterator.MoveNext())
                {
                    yield return iterator.Current;
                }
            }
            else if (this._LateInit != null)
            {
                ModdedContentLoader.logger.Trace($"   🛠️ Managed LateInit of {this.instanceType.FullName}");
                this._LateInit.Invoke(this.Instance, null);
            }
            else
            {
                ModdedContentLoader.logger.Trace($"   🛈 NO LateInit of {this.instanceType.FullName}");
            }
            yield break;
        }

        public void Update()
        {
            this.Instance.Update();
        }

        public void FixedUpdate()
        {
            this.Instance.FixedUpdate();
        }
    }
}
