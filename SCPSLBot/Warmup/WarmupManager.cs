using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Arguments.ServerEvents;
using LabApi.Events.Handlers;
using MapGeneration;
using MEC;
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
        private BotPluginConfig config;
        private int generation;

        public WarmupMode Mode => config?.WarmupMode ?? WarmupMode.None;

        public bool IsStandardWarmup => Mode == WarmupMode.Standard;

        public void Init(BotPluginConfig pluginConfig)
        {
            config = pluginConfig;
            generation++;

            ServerEvents.RoundStarted += OnRoundStarted;
            ServerEvents.RoundRestarted += OnRoundRestarted;
            ServerEvents.RoundEndingConditionsCheck += OnRoundEndingConditionsCheck;
            PlayerEvents.Spawned += OnPlayerSpawned;
            PlayerEvents.Death += OnPlayerDeath;
            PlayerEvents.Left += OnPlayerLeft;

            if (IsStandardWarmup)
            {
                StartRoundIfNeeded();
                RespawnDeadPlayers();
            }
        }

        public void Terminate()
        {
            generation++;
            PlayerEvents.Left -= OnPlayerLeft;
            PlayerEvents.Death -= OnPlayerDeath;
            PlayerEvents.Spawned -= OnPlayerSpawned;
            ServerEvents.RoundEndingConditionsCheck -= OnRoundEndingConditionsCheck;
            ServerEvents.RoundRestarted -= OnRoundRestarted;
            ServerEvents.RoundStarted -= OnRoundStarted;
            lastRoles.Clear();
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
                StartRoundIfNeeded();
                RespawnDeadPlayers();
                Logger.Info("[SCPSLBot] Standard warmup enabled: rounds locked and deaths will respawn.");
            }
            else
            {
                Logger.Info("[SCPSLBot] Warmup disabled.");
            }
        }

        private void OnRoundStarted()
        {
            if (IsStandardWarmup)
            {
                RespawnDeadPlayers();
            }
        }

        private void OnRoundRestarted()
        {
            generation++;
            if (IsStandardWarmup)
            {
                Timing.CallDelayed(0.5f, StartRoundIfNeeded);
            }
        }

        private void OnRoundEndingConditionsCheck(RoundEndingConditionsCheckEventArgs ev)
        {
            if (IsStandardWarmup)
            {
                ev.CanEnd = false;
            }
        }

        private void OnPlayerSpawned(PlayerSpawnedEventArgs ev)
        {
            if (ev.Player == null || ev.Player.IsDestroyed)
            {
                return;
            }

            CacheRole(ev.Player.PlayerId, ev.Player.Role);

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
            var delayMs = IsBot(ev.Player) ? config.BotRespawnDelayMs : config.HumanRespawnDelayMs;
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

                RespawnPlayer(player.PlayerId, ResolveRespawnRole(player.PlayerId, player.Role));
            }
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

            LabRound.Start();
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
