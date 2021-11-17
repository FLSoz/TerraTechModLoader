using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Reflection;

namespace ModManager
{
    internal class ReflectedManMods
    {
        internal static readonly FieldInfo m_CurrentSession = typeof(ManMods).GetField("m_CurrentSession", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        internal static readonly FieldInfo m_Mods = typeof(ManMods).GetField("m_Mods", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        internal static readonly FieldInfo m_WaitingOnDownloads = typeof(ManMods).GetField("m_WaitingOnDownloads", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

        internal static readonly MethodInfo RequestModLoad = typeof(ManMods).GetMethod("RequestModLoad", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
    }
}
