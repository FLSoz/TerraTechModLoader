using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;

namespace ModManager
{
    public interface IManagedMod
    {
        ModBase Instance
        {
            get;
        }

        int InitOrder
        {
            get;
        }

        int EarlyInitOrder
        {
            get;
        }

        int UpdateOrder
        {
            get;
        }

        int FixedUpdateOrder
        {
            get;
        }

        String Name
        {
            get;
        }

        Assembly Assembly
        {
            get;
        }

        // what order to run EarlyInit() things in
        Type[] EarlyLoadAfter
        {
            get;
        }
        Type[] EarlyLoadBefore
        {
            get;
        }

        // What order to run Init things in.
        // Also serves as reverse order to run DeInit in
        Type[] LoadAfter
        {
            get;
        }
        Type[] LoadBefore
        {
            get;
        }

        // What order to run Update things in
        Type[] UpdateAfter
        {
            get;
        }
        Type[] UpdateBefore
        {
            get;
        }

        // What order to run FixedUpdate things in
        Type[] FixedUpdateAfter
        {
            get;
        }
        Type[] FixedUpdateBefore
        {
            get;
        }

        IEnumerator<float> DeInit();

        IEnumerator<float> Init();

        IEnumerator<float> EarlyInit();

        void Update();

        void FixedUpdate();

        bool HasUpdate
        {
            get;
        }

        bool HasFixedUpdate
        {
            get;
        }
    }
}
