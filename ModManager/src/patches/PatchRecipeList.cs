using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;

namespace ModManager.src.patches
{
    [HarmonyPatch(typeof(ModuleItemConsume), "OnSpawn")]
    internal class PatchRecipeList
    {
        internal static MethodInfo RemoveRecipes = AccessTools.Method(typeof(ModuleItemConsume), "RemoveRecipes");
        internal static MethodInfo AddRecipes = AccessTools.Method(typeof(ModuleItemConsume), "AddRecipes");
        private static FieldInfo m_BaseRecipeProvider = AccessTools.Field(typeof(ModuleItemConsume), "m_BaseRecipeProvider");

        internal static void Postfix(ModuleItemConsume __instance)
        {
            ModuleRecipeProvider recipeProvider = (ModuleRecipeProvider)m_BaseRecipeProvider.GetValue(__instance);
            if (recipeProvider != null)
            {
                RemoveRecipes.Invoke(__instance, new object[] { recipeProvider, false });
                AddRecipes.Invoke(__instance, new object[] { recipeProvider, true });
            }
        }
    }
}
