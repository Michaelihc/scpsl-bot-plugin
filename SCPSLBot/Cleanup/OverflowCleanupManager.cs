using CommandSystem;
using CommandSystem.Commands.RemoteAdmin.Cleanup;
using InventorySystem.Items.Pickups;
using LabApi.Events.Handlers;
using MEC;
using System;
using UnityEngine;
using Logger = LabApi.Features.Console.Logger;

namespace SCPSLBot.Cleanup
{
    internal sealed class OverflowCleanupManager
    {
        public static OverflowCleanupManager Instance { get; } = new OverflowCleanupManager();

        private readonly ICommandSender commandSender = new ServerConsoleSender();
        private BotPluginConfig config;
        private int generation;
        private int? baselineItemCount;

        public void Init(BotPluginConfig pluginConfig)
        {
            config = pluginConfig;
            generation++;
            baselineItemCount = null;
            ServerEvents.RoundStarted += OnRoundStarted;
            ServerEvents.RoundRestarted += OnRoundRestarted;
            ScheduleNext();
        }

        public void Terminate()
        {
            generation++;
            ServerEvents.RoundRestarted -= OnRoundRestarted;
            ServerEvents.RoundStarted -= OnRoundStarted;
            baselineItemCount = null;
            config = null;
        }

        private void OnRoundStarted()
        {
            baselineItemCount = null;
        }

        private void OnRoundRestarted()
        {
            baselineItemCount = null;
        }

        private void ScheduleNext()
        {
            int scheduleGeneration = generation;
            float interval = Mathf.Max(1f, config?.CleanupCheckIntervalSeconds ?? 10f);
            Timing.CallDelayed(interval, () => Tick(scheduleGeneration));
        }

        private void Tick(int scheduleGeneration)
        {
            if (scheduleGeneration != generation || config == null)
            {
                return;
            }

            try
            {
                if (config.EnableOverflowCleanup)
                {
                    CleanupIfNeeded();
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[SCPSLBot] Overflow cleanup failed: {ex}");
            }
            finally
            {
                if (scheduleGeneration == generation && config != null)
                {
                    ScheduleNext();
                }
            }
        }

        private void CleanupIfNeeded()
        {
            int threshold = Mathf.Max(1, config.CleanupItemThreshold);
            int itemCount = UnityEngine.Object.FindObjectsOfType<ItemPickupBase>().Length;
            if (!baselineItemCount.HasValue || itemCount < baselineItemCount.Value)
            {
                baselineItemCount = itemCount;
                if (config.EnableVerboseBotLogs)
                {
                    Logger.Info($"[SCPSLBot] Overflow cleanup baseline captured at {itemCount} items.");
                }

                return;
            }

            int excessItemCount = itemCount - baselineItemCount.Value;
            if (excessItemCount <= threshold)
            {
                return;
            }

            RunNativeCleanup(itemCount, baselineItemCount.Value, excessItemCount, threshold);
            baselineItemCount = null;
        }

        private void RunNativeCleanup(int itemCount, int baselineCount, int excessItemCount, int threshold)
        {
            ExecuteNativeCommand(new ItemsCommand(), "items");
            ExecuteNativeCommand(new CorpsesCommand(), "corpses");
            ExecuteNativeCommand(new BloodCommand(), "blood");
            ExecuteNativeCommand(new BulletHolesCommand(), "bullet holes");
            if (config.EnableVerboseBotLogs)
            {
                Logger.Info($"[SCPSLBot] Native overflow cleanup ran at {itemCount} items ({excessItemCount} above baseline {baselineCount}, threshold {threshold}).");
            }
        }

        private void ExecuteNativeCommand(ICommand command, string label)
        {
            string response;
            bool success = command.Execute(new ArraySegment<string>(Array.Empty<string>()), commandSender, out response);
            if (!success)
            {
                Logger.Warn($"[SCPSLBot] Native cleanup command '{label}' failed: {response}");
            }
        }

        private OverflowCleanupManager()
        {
        }
    }
}
