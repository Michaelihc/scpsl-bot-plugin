using Discord;
using HarmonyLib;
using NUnit.Engine;
using PluginAPI.Helpers;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Threading.Tasks;

namespace SCPSLTests.Runner.Patches
{
    [HarmonyPatch(typeof(AppDomain))]
    public static class AppDomainPatches
    {
        [HarmonyPatch(nameof(AppDomain.RelativeSearchPath), MethodType.Getter)]
        [HarmonyPrefix()]
        public static bool Prefix(ref string __result)
        {
            __result = Path.Combine(Paths.GlobalPlugins.Dependencies, "private");
            return false;
        }
    }
}
