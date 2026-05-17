using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Arguments.ServerEvents;
using LabApi.Events.Arguments.WarheadEvents;
using LabApi.Events.Handlers;
using LightContainmentZoneDecontamination;
using MapGeneration;
using MEC;
using Mirror;
using PlayerRoles;
using SCPSLBot.AI;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using LabPlayer = LabApi.Features.Wrappers.Player;
using LabRoom = LabApi.Features.Wrappers.Room;
using LabRound = LabApi.Features.Wrappers.Round;
using Logger = LabApi.Features.Console.Logger;

namespace SCPSLBot.Warmup
{
    internal sealed class WarmupManager
    {
        public static WarmupManager Instance { get; } = new WarmupManager();

        private readonly Dictionary<int, RoleTypeId> lastRoles = new();
        private readonly HashSet<int> scheduledSpectatorRespawns = new();
        private BotPluginConfig config;
        private int generation;
        private int adminWarheadOverrideUntilTick;
        private float dummySpawnNotBeforeTime;
        private bool serverReadyForDummies;

        public WarmupMode Mode => config?.WarmupMode ?? WarmupMode.None;

        public bool IsStandardWarmup => Mode == WarmupMode.Standard;

        public void Init(BotPluginConfig pluginConfig)
        {
            config = pluginConfig;
            config.WarmupMode = config.DefaultWarmupMode;
            generation++;

            ServerEvents.RoundStarted += OnRoundStarted;
            ServerEvents.RoundRestarted += OnRoundRestarted;
            ServerEvents.WaitingForPlayers += OnWaitingForPlayers;
            ServerEvents.RoundEndingConditionsCheck += OnRoundEndingConditionsCheck;
            PlayerEvents.Joined += OnPlayerJoined;
            PlayerEvents.Cuffing += OnPlayerCuffing;
            PlayerEvents.UnlockingWarheadButton += OnPlayerUnlockingWarheadButton;
            PlayerEvents.InteractingWarheadLever += OnPlayerInteractingWarheadLever;
            PlayerEvents.Spawned += OnPlayerSpawned;
            PlayerEvents.Death += OnPlayerDeath;
            PlayerEvents.Left += OnPlayerLeft;
            LabApi.Events.Handlers.WarheadEvents.Starting += OnWarheadStarting;
            LabApi.Events.Handlers.WarheadEvents.Detonating += OnWarheadDetonating;

            if (IsStandardWarmup)
            {
                ActivateStandardWarmup("startup");
            }
            else
            {
                EnableLczDecontaminationIfNeeded();
            }
        }

        public void Terminate()
        {
            generation++;
            LabApi.Events.Handlers.WarheadEvents.Detonating -= OnWarheadDetonating;
            LabApi.Events.Handlers.WarheadEvents.Starting -= OnWarheadStarting;
            PlayerEvents.Left -= OnPlayerLeft;
            PlayerEvents.Death -= OnPlayerDeath;
            PlayerEvents.Spawned -= OnPlayerSpawned;
            PlayerEvents.InteractingWarheadLever -= OnPlayerInteractingWarheadLever;
            PlayerEvents.UnlockingWarheadButton -= OnPlayerUnlockingWarheadButton;
            PlayerEvents.Cuffing -= OnPlayerCuffing;
            PlayerEvents.Joined -= OnPlayerJoined;
            ServerEvents.RoundEndingConditionsCheck -= OnRoundEndingConditionsCheck;
            ServerEvents.WaitingForPlayers -= OnWaitingForPlayers;
            ServerEvents.RoundRestarted -= OnRoundRestarted;
            ServerEvents.RoundStarted -= OnRoundStarted;
            lastRoles.Clear();
            scheduledSpectatorRespawns.Clear();
            adminWarheadOverrideUntilTick = 0;
            serverReadyForDummies = false;
            EnableLczDecontaminationIfNeeded();
            config = null;
        }

        public bool TrySetMode(string modeName, out string response)
        {
            if (!Enum.TryParse(modeName, true, out WarmupMode mode))
            {
                response = "Unknown warmup mode. Use: none, standard.";
                return false;
            }

            SetMode(mode);
            response = $"Warmup mode set to {mode}.";
            return true;
        }

        public void SetMode(WarmupMode mode)
        {
            if (config == null)
            {
                return;
            }

            config.WarmupMode = mode;
            generation++;
            LabApiPlugin.Instance?.SaveSettings();

            if (mode == WarmupMode.Standard)
            {
                ActivateStandardWarmup("mode switch");
            }
            else
            {
                EnableLczDecontaminationIfNeeded();
                Logger.Info("[SCPSLBot] Warmup disabled.");
            }
        }

        private void ActivateStandardWarmup(string reason)
        {
            var scheduleGeneration = generation;
            dummySpawnNotBeforeTime = Time.realtimeSinceStartup + 8f;
            StartRoundIfNeeded();
            DisableLczDecontaminationIfNeeded();
            DisableWarheadIfNeeded();
            RespawnDeadPlayers();
            Timing.CallDelayed(0.5f, () => RetryStandardWarmupActivation(scheduleGeneration));
            Timing.CallDelayed(1.5f, () => RetryStandardWarmupActivation(scheduleGeneration));
            Timing.CallDelayed(3f, () => RetryStandardWarmupActivation(scheduleGeneration));
            Timing.CallDelayed(5f, () => RetryStandardWarmupActivation(scheduleGeneration));
            Logger.Info($"[SCPSLBot] Standard warmup enabled ({reason}): rounds locked and deaths will respawn.");
        }

        private void RetryStandardWarmupActivation(int scheduleGeneration)
        {
            if (scheduleGeneration != generation || !IsStandardWarmup)
            {
                return;
            }

            StartRoundIfNeeded();
            EnsureWarmupBots();
            DisableLczDecontaminationIfNeeded();
            DisableWarheadIfNeeded();
            RespawnDeadPlayers();
        }

        public bool TrySetBotCount(int targetCount, int maxBotCount, out string response)
        {
            if (config == null)
            {
                response = "SCPSLBot warmup config is not loaded.";
                return false;
            }

            int max = Mathf.Clamp(maxBotCount, 0, 10);
            int target = Mathf.Clamp(targetCount, 0, max);
            config.WarmupBotCount = target;
            LabApiPlugin.Instance?.SaveSettings();

            if (IsStandardWarmup)
            {
                EnsureWarmupBots();
            }

            response = $"Warmup bot count set to {target} (max {max}).";
            return true;
        }

        private void OnRoundStarted()
        {
            if (IsStandardWarmup)
            {
                EnsureWarmupBots();
                DisableLczDecontaminationIfNeeded();
                DisableWarheadIfNeeded();
                RespawnDeadPlayers();
            }
        }

        private void OnRoundRestarted()
        {
            generation++;
            serverReadyForDummies = false;
            if (IsStandardWarmup)
            {
                Timing.CallDelayed(0.5f, () => RetryStandardWarmupActivation(generation));
            }
        }

        private void OnWaitingForPlayers()
        {
            serverReadyForDummies = true;
            dummySpawnNotBeforeTime = Mathf.Max(dummySpawnNotBeforeTime, Time.realtimeSinceStartup + 0.75f);
            if (IsStandardWarmup)
            {
                Timing.CallDelayed(1f, () => RetryStandardWarmupActivation(generation));
            }
        }

        private void OnRoundEndingConditionsCheck(RoundEndingConditionsCheckEventArgs ev)
        {
            if (IsStandardWarmup)
            {
                ev.CanEnd = false;
            }
        }

        private void OnPlayerJoined(PlayerJoinedEventArgs ev)
        {
            if (IsStandardWarmup)
            {
                ScheduleSpectatorRespawn(ev.Player);
            }
        }

        private void OnPlayerCuffing(PlayerCuffingEventArgs ev)
        {
            if (!ShouldBlockDisarming())
            {
                return;
            }

            ev.IsAllowed = false;
            ev.Player?.SendHint("Disarming is disabled in warmup.", 2f);
        }

        private void OnPlayerUnlockingWarheadButton(PlayerUnlockingWarheadButtonEventArgs ev)
        {
            if (!ShouldBlockWarhead())
            {
                return;
            }

            ev.IsAllowed = false;
            DisableWarheadIfNeeded();
            ev.Player?.SendHint("Warhead is disabled in warmup.", 2f);
        }

        private void OnPlayerInteractingWarheadLever(PlayerInteractingWarheadLeverEventArgs ev)
        {
            if (!ShouldBlockWarhead())
            {
                return;
            }

            ev.IsAllowed = false;
            DisableWarheadIfNeeded();
            ev.Player?.SendHint("Warhead is disabled in warmup.", 2f);
        }

        private void OnWarheadStarting(WarheadStartingEventArgs ev)
        {
            if (!ShouldBlockWarhead())
            {
                return;
            }

            if (!ev.IsAutomatic && IsAdminWarheadStart(ev.Player))
            {
                adminWarheadOverrideUntilTick = unchecked(Environment.TickCount + 10 * 60 * 1000);
                Logger.Info("[SCPSLBot] Admin warhead start allowed in warmup.");
                return;
            }

            ev.IsAllowed = false;
            adminWarheadOverrideUntilTick = 0;
            DisableWarheadIfNeeded();
            Logger.Info("[SCPSLBot] Warhead start blocked in warmup.");
        }

        private void OnWarheadDetonating(WarheadDetonatingEventArgs ev)
        {
            if (!ShouldBlockWarhead())
            {
                return;
            }

            if (IsAdminWarheadOverrideActive() || IsAdminWarheadStart(ev.Player))
            {
                adminWarheadOverrideUntilTick = 0;
                Logger.Info("[SCPSLBot] Admin warhead detonation allowed in warmup.");
                return;
            }

            ev.IsAllowed = false;
            adminWarheadOverrideUntilTick = 0;
            DisableWarheadIfNeeded();
            Logger.Info("[SCPSLBot] Warhead detonation blocked in warmup.");
        }

        private void OnPlayerSpawned(PlayerSpawnedEventArgs ev)
        {
            if (ev.Player == null || ev.Player.IsDestroyed)
            {
                return;
            }

            CacheRole(ev.Player.PlayerId, ev.Player.Role);

            if (IsStandardWarmup)
            {
                ScheduleSpectatorRespawn(ev.Player);
            }

            if (ev.Player.Role is RoleTypeId.Scp049 or RoleTypeId.Scp173)
            {
                ScheduleRoom939Spawn(ev.Player, ev.Player.Role);
            }
        }

        private void OnPlayerDeath(PlayerDeathEventArgs ev)
        {
            if (ev.Player == null || ev.Player.IsDestroyed)
            {
                return;
            }

            CacheRole(ev.Player.PlayerId, ev.OldRole);

            if (!IsStandardWarmup)
            {
                return;
            }

            var playerId = ev.Player.PlayerId;
            var role = ResolveRespawnRole(playerId, ev.OldRole);
            var delayMs = IsBot(ev.Player) ? config.BotRespawnDelayMs : config.SpectatorRespawnDelayMs;
            var scheduleGeneration = generation;

            Timing.CallDelayed(Mathf.Max(0.05f, delayMs / 1000f), () =>
            {
                if (scheduleGeneration != generation || !IsStandardWarmup)
                {
                    return;
                }

                RespawnPlayer(playerId, role);
            });
        }

        private void OnPlayerLeft(PlayerLeftEventArgs ev)
        {
            if (ev.Player != null)
            {
                lastRoles.Remove(ev.Player.PlayerId);
                scheduledSpectatorRespawns.Remove(ev.Player.PlayerId);
            }

            if (IsStandardWarmup)
            {
                Timing.CallDelayed(0.25f, EnsureWarmupBots);
            }
        }

        private void RespawnDeadPlayers()
        {
            if (!IsStandardWarmup)
            {
                return;
            }

            foreach (var player in LabPlayer.ReadyList)
            {
                if (!CanWarmupRespawn(player) || player.IsAlive)
                {
                    continue;
                }

                if (ScheduleSpectatorRespawn(player))
                {
                    continue;
                }

                RespawnPlayer(player.PlayerId, ResolveRespawnRole(player.PlayerId, player.Role));
            }
        }

        private bool ScheduleSpectatorRespawn(LabPlayer player)
        {
            if (!IsStandardWarmup
                || player == null
                || player.IsDestroyed
                || IsBot(player)
                || !CanWarmupRespawn(player)
                || player.Role != RoleTypeId.Spectator)
            {
                return false;
            }

            int playerId = player.PlayerId;
            if (!scheduledSpectatorRespawns.Add(playerId))
            {
                return true;
            }

            int scheduleGeneration = generation;
            float delaySeconds = Mathf.Max(0.05f, (config?.SpectatorRespawnDelayMs ?? 5000) / 1000f);
            Timing.CallDelayed(delaySeconds, () =>
            {
                scheduledSpectatorRespawns.Remove(playerId);
                if (scheduleGeneration != generation
                    || !IsStandardWarmup
                    || !LabPlayer.TryGet(playerId, out var livePlayer)
                    || livePlayer == null
                    || livePlayer.IsDestroyed
                    || IsBot(livePlayer)
                    || !CanWarmupRespawn(livePlayer)
                    || livePlayer.Role != RoleTypeId.Spectator)
                {
                    return;
                }

                var configuredRole = config?.WarmupHumanRole ?? RoleTypeId.NtfPrivate;
                var spawnRole = IsRespawnRole(configuredRole)
                    ? configuredRole
                    : RoleTypeId.NtfPrivate;
                livePlayer.SetRole(spawnRole, RoleChangeReason.Respawn, RoleSpawnFlags.All);
                CacheRole(playerId, spawnRole);
            });

            return true;
        }

        private void RespawnPlayer(int playerId, RoleTypeId role)
        {
            if (!LabPlayer.TryGet(playerId, out var player)
                || player == null
                || player.IsDestroyed
                || !CanWarmupRespawn(player)
                || player.IsAlive)
            {
                return;
            }

            player.SetRole(role, RoleChangeReason.Respawn, RoleSpawnFlags.All);
            CacheRole(playerId, role);
        }

        private RoleTypeId ResolveRespawnRole(int playerId, RoleTypeId fallback)
        {
            if (IsRespawnRole(fallback))
            {
                return fallback;
            }

            if (lastRoles.TryGetValue(playerId, out var cachedRole) && IsRespawnRole(cachedRole))
            {
                return cachedRole;
            }

            return IsRespawnRole(config.DefaultRespawnRole) ? config.DefaultRespawnRole : RoleTypeId.ClassD;
        }

        private void EnsureWarmupBots()
        {
            if (!IsStandardWarmup || config == null)
            {
                return;
            }

            if (!CanManageDummies())
            {
                return;
            }

            int targetCount = Mathf.Clamp(config.WarmupBotCount, 0, 10);
            var currentBots = BotManager.Instance.BotPlayers.Keys.ToArray();

            if (currentBots.Length < targetCount)
            {
                for (int i = currentBots.Length; i < targetCount; i++)
                {
                    BotManager.Instance.AddBotPlayer();
                }

                Timing.CallDelayed(0.5f, EnsureWarmupBots);
                return;
            }
            else if (currentBots.Length > targetCount)
            {
                foreach (ReferenceHub hub in currentBots
                    .OrderBy(hub => hub.PlayerId)
                    .Skip(targetCount)
                    .ToArray())
                {
                    NetworkServer.Destroy(hub.gameObject);
                    BotManager.Instance.RemovePlayerIfBot(hub);
                }
            }

            foreach (ReferenceHub hub in BotManager.Instance.BotPlayers.Keys.ToArray())
            {
                if (hub == null || hub.roleManager == null)
                {
                    continue;
                }

                RoleTypeId defaultRole = IsRespawnRole(config.WarmupBotRole)
                    ? config.WarmupBotRole
                    : RoleTypeId.ChaosRifleman;
                if (!TryEnsureBotInitialRole(hub, defaultRole))
                {
                    Timing.CallDelayed(0.5f, EnsureWarmupBots);
                }
            }
        }

        private bool TryEnsureBotInitialRole(ReferenceHub hub, RoleTypeId defaultRole)
        {
            try
            {
                var currentRole = hub.roleManager.CurrentRole;
                if (currentRole == null
                    || currentRole.RoleTypeId == RoleTypeId.None
                    || currentRole.RoleTypeId == RoleTypeId.Spectator)
                {
                    hub.roleManager.ServerSetRole(defaultRole, RoleChangeReason.RemoteAdmin);
                    CacheRole(hub.PlayerId, defaultRole);
                }
                else if (IsRespawnRole(currentRole.RoleTypeId))
                {
                    CacheRole(hub.PlayerId, currentRole.RoleTypeId);
                }

                return true;
            }
            catch (NullReferenceException)
            {
                return false;
            }
        }

        private void ScheduleRoom939Spawn(LabPlayer player, RoleTypeId role)
        {
            var scheduleGeneration = generation;
            ScheduleRoleSpawnOverride(player.PlayerId, role, scheduleGeneration, 0.08f);
            ScheduleRoleSpawnOverride(player.PlayerId, role, scheduleGeneration, 0.35f);
            ScheduleRoleSpawnOverride(player.PlayerId, role, scheduleGeneration, 0.8f);
        }

        private void ScheduleRoleSpawnOverride(int playerId, RoleTypeId role, int scheduleGeneration, float delaySeconds)
        {
            Timing.CallDelayed(delaySeconds, () =>
            {
                if (scheduleGeneration != generation
                    || !LabPlayer.TryGet(playerId, out var player)
                    || player == null
                    || player.IsDestroyed
                    || player.Role != role)
                {
                    return;
                }

                if (TryGetRoom939Position(out var position))
                {
                    player.Position = position;
                }
            });
        }

        private static bool TryGetRoom939Position(out Vector3 position)
        {
            var room = LabRoom.Get(RoomName.Hcz939).FirstOrDefault();
            if (room == null)
            {
                position = default;
                return false;
            }

            position = room.Position + Vector3.up * 1.2f;
            return true;
        }

        private void StartRoundIfNeeded()
        {
            if (!IsStandardWarmup || LabRound.IsRoundStarted)
            {
                return;
            }

            try
            {
                LabRound.Start();
            }
            catch (NullReferenceException ex)
            {
                Logger.Warn($"[SCPSLBot] Standard warmup round start was not ready yet: {ex.Message}");
            }
        }

        private bool CanManageDummies()
        {
            return serverReadyForDummies
                && Time.realtimeSinceStartup >= dummySpawnNotBeforeTime
                && NetworkServer.active
                && NetworkManager.singleton != null
                && NetworkManager.singleton.playerPrefab != null;
        }

        private bool ShouldBlockWarhead()
        {
            return IsStandardWarmup && config != null && config.DisableWarheadInWarmup;
        }

        private bool ShouldDisableLczDecontamination()
        {
            return IsStandardWarmup && config != null && config.DisableLczDecontaminationInWarmup;
        }

        private bool ShouldBlockDisarming()
        {
            return IsStandardWarmup && config != null && config.DisableDisarmingInWarmup;
        }

        private void DisableWarheadIfNeeded()
        {
            if (!ShouldBlockWarhead())
            {
                return;
            }

            try
            {
                if (!LabApi.Features.Wrappers.Warhead.Exists)
                {
                    return;
                }

                if (LabApi.Features.Wrappers.Warhead.IsDetonationInProgress)
                {
                    LabApi.Features.Wrappers.Warhead.Stop(null);
                }

                LabApi.Features.Wrappers.Warhead.LeverStatus = false;
                LabApi.Features.Wrappers.Warhead.IsAuthorized = false;
                LabApi.Features.Wrappers.Warhead.IsLocked = false;
                LabApi.Features.Wrappers.Warhead.ForceCountdownToggle = false;
                LabApi.Features.Wrappers.Warhead.DeadManSwitchRemaining = 0f;
            }
            catch (Exception ex)
            {
                Logger.Warn($"[SCPSLBot] Failed to disable warhead in warmup: {ex.Message}");
            }
        }

        private void DisableLczDecontaminationIfNeeded()
        {
            if (!ShouldDisableLczDecontamination())
            {
                return;
            }

            SetLczDecontamination(DecontaminationController.DecontaminationStatus.Disabled, "disabled");
        }

        private void EnableLczDecontaminationIfNeeded()
        {
            if (config == null || !config.DisableLczDecontaminationInWarmup)
            {
                return;
            }

            SetLczDecontamination(DecontaminationController.DecontaminationStatus.None, "enabled");
        }

        private static void SetLczDecontamination(DecontaminationController.DecontaminationStatus status, string label)
        {
            try
            {
                var controller = DecontaminationController.Singleton;
                if (controller == null || controller.DecontaminationOverride == status)
                {
                    return;
                }

                controller.DecontaminationOverride = status;
                Logger.Info($"[SCPSLBot] LCZ decontamination {label}.");
            }
            catch (Exception ex)
            {
                Logger.Warn($"[SCPSLBot] Failed to set LCZ decontamination {label}: {ex.Message}");
            }
        }

        private bool IsAdminWarheadOverrideActive()
        {
            return adminWarheadOverrideUntilTick != 0
                   && unchecked(adminWarheadOverrideUntilTick - Environment.TickCount) > 0;
        }

        private static bool IsAdminWarheadStart(LabPlayer player)
        {
            return player == null
                   || player.RemoteAdminAccess
                   || (player.PlayerId == 1 && string.Equals(player.Nickname, "Dedicated Server", StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsRespawnRole(RoleTypeId role)
        {
            return role != RoleTypeId.None && role != RoleTypeId.Spectator;
        }

        private static bool CanWarmupRespawn(LabPlayer player)
        {
            return player != null
                   && !player.IsDestroyed
                   && !player.IsHost
                   && (!player.IsDummy || IsBot(player));
        }

        private static bool IsBot(LabPlayer player)
        {
            return player != null && BotManager.Instance.BotPlayers.ContainsKey(player.ReferenceHub);
        }

        private void CacheRole(int playerId, RoleTypeId role)
        {
            if (IsRespawnRole(role))
            {
                lastRoles[playerId] = role;
            }
        }

        private WarmupManager()
        {
        }
    }
}
