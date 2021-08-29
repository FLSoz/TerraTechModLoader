using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;

namespace ModManager
{
    public interface IManagedMod
    {
        int loadOrder
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
        Type[] earlyLoadAfter
        {
            get;
        }
        Type[] earlyLoadBefore
        {
            get;
        }

        // What order to run Init things in.
        // Also serves as reverse order to run DeInit in
        Type[] loadAfter
        {
            get;
        }
        Type[] loadBefore
        {
            get;
        }

        void DeInit();

        void Init();

        void EarlyInit();
    }
}
