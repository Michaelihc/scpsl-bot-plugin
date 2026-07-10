using InventorySystem;
using InventorySystem.Items.Firearms;
using InventorySystem.Items.Firearms.Modules;
using UnityEngine;

namespace SCPSLBot.Ammo
{
    internal static class ReserveAmmoHelper
    {
        private const int DefaultReserveAmmoTargetMagazines = 2;
        private const int DefaultReserveAmmoHardCap = 200;
        private const float DefaultReserveAmmoTopUpIntervalSeconds = 2f;

        public static float GetTopUpIntervalSeconds(BotPluginConfig config)
        {
            return config == null
                ? DefaultReserveAmmoTopUpIntervalSeconds
                : Mathf.Max(1f, config.BotReserveAmmoTopUpIntervalSeconds);
        }

        public static void MaintainReserveAmmo(Firearm firearm, BotPluginConfig config)
        {
            if (!firearm.TryGetModule<IPrimaryAmmoContainerModule>(out var primaryAmmo)
                || primaryAmmo.AmmoType == ItemType.None
                || primaryAmmo.AmmoMax <= 0)
            {
                return;
            }

            var magazineCount = config == null
                ? DefaultReserveAmmoTargetMagazines
                : Mathf.Clamp(config.BotReserveAmmoTargetMagazines, 1, 4);
            var hardCap = config == null
                ? DefaultReserveAmmoHardCap
                : Mathf.Clamp(config.BotReserveAmmoHardCap, 1, 200);
            var targetReserve = Mathf.Clamp(primaryAmmo.AmmoMax * magazineCount, 1, hardCap);
            var inventory = firearm.Owner.inventory;
            var currentReserve = inventory.GetCurAmmo(primaryAmmo.AmmoType);

            if (currentReserve < targetReserve)
            {
                inventory.ServerSetAmmo(primaryAmmo.AmmoType, targetReserve);
            }
            else if (currentReserve > hardCap)
            {
                inventory.ServerSetAmmo(primaryAmmo.AmmoType, hardCap);
            }
        }
    }
}
