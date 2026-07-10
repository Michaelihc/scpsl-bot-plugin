using InventorySystem.Items.Firearms;
using LabApi.Features.Wrappers;
using MEC;
using SCPSLBot.Warmup;
using System.Collections.Generic;
using System.Linq;

namespace SCPSLBot.Ammo
{
    internal sealed class HumanReserveAmmoManager
    {
        public static HumanReserveAmmoManager Instance { get; } = new();

        private BotPluginConfig config;
        private CoroutineHandle handle;

        public void Init(BotPluginConfig pluginConfig)
        {
            config = pluginConfig;
            handle = Timing.RunCoroutine(RunAmmoTopUps());
        }

        public void Terminate()
        {
            Timing.KillCoroutines(handle);
            config = null;
        }

        private IEnumerator<float> RunAmmoTopUps()
        {
            while (true)
            {
                var intervalSeconds = ReserveAmmoHelper.GetTopUpIntervalSeconds(config);

                if (config?.EnableHumanInfiniteReserveAmmo == true && WarmupManager.Instance.IsStandardWarmup)
                {
                    MaintainHumanReserveAmmo();
                }

                yield return Timing.WaitForSeconds(intervalSeconds);
            }
        }

        private void MaintainHumanReserveAmmo()
        {
            foreach (var player in Player.ReadyList)
            {
                if (player == null
                    || player.IsDestroyed
                    || player.IsDummy
                    || !player.IsAlive
                    || player.ReferenceHub?.inventory?.UserInventory?.Items == null)
                {
                    continue;
                }

                foreach (var firearm in player.ReferenceHub.inventory.UserInventory.Items.Values.OfType<Firearm>())
                {
                    ReserveAmmoHelper.MaintainReserveAmmo(firearm, config);
                }
            }
        }

        private HumanReserveAmmoManager()
        {
        }
    }
}
