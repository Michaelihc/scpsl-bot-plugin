using MapGeneration.RoomConnectors.Spawners;
using HarmonyLib;
using System.Collections.Generic;
using MapGeneration.RoomConnectors;

namespace SCPSLBot.MapGeneration
{
    [HarmonyPatch(typeof(RoomConnectorSpawnpointBase))]
    internal static class RoomConnectorSpawnpointBasePatches
    {
        private static readonly HashSet<SpawnableRoomConnectorType> SupportedConnectorTypes = new()
        {
            SpawnableRoomConnectorType.LczStandardDoor,
            SpawnableRoomConnectorType.HczStandardDoor,
            SpawnableRoomConnectorType.EzStandardDoor,
        };

        [HarmonyPatch(nameof(RoomConnectorSpawnpointBase.Spawn))]
        [HarmonyPrefix]
        public static void SpawnWithSupportedType(RoomConnectorSpawnpointBase __instance, ref SpawnableRoomConnectorType type)
        {
            if (!SupportedConnectorTypes.Contains(type))
            {
                type = SpawnableRoomConnectorType.HczStandardDoor;
            }
        }
    }
}
