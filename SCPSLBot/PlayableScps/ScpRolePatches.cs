using HarmonyLib;
using PlayerRoles.PlayableScps.Scp079;
using PlayerRoles.PlayableScps.Scp096;
using PlayerRoles.PlayableScps.Scp106;
using PlayerRoles.PlayableScps.Scp173;
using PlayerRoles.PlayableScps.Scp939;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SCPSLBot.PlayableScps
{
    [HarmonyPatch]
    internal static class ScpRolePatches
    {
        [HarmonyPatch(typeof(Scp079Role), nameof(Scp079Role.GetSpawnChance))]
        [HarmonyPrefix]
        public static bool ReturnZeroChanceScp079(ref float __result)
        {
            __result = 0f;

            return false;
        }

        [HarmonyPatch(typeof(Scp096Role), nameof(Scp096Role.GetSpawnChance))]
        [HarmonyPrefix]
        public static bool ReturnZeroChanceScp096(ref float __result)
        {
            __result = 0f;

            return false;
        }

        [HarmonyPatch(typeof(Scp173Role), nameof(Scp173Role.GetSpawnChance))]
        [HarmonyPrefix]
        public static bool ReturnZeroChanceScp173(ref float __result)
        {
            __result = 0f;

            return false;
        }

        [HarmonyPatch(typeof(Scp106Role), nameof(Scp106Role.GetSpawnChance))]
        [HarmonyPrefix]
        public static bool ReturnZeroChanceScp106(ref float __result)
        {
            __result = 0f;

            return false;
        }

        [HarmonyPatch(typeof(Scp939Role), nameof(Scp939Role.GetSpawnChance))]
        [HarmonyPrefix]
        public static bool ReturnZeroChanceScp939(ref float __result)
        {
            __result = 0f;

            return false;
        }
    }
}
