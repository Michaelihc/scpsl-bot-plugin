using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Arguments.ServerEvents;
using LabApi.Events.Arguments.WarheadEvents;
using LabApi.Events.Handlers;
using CustomPlayerEffects;
using Interactables.Interobjects;
using Interactables.Interobjects.DoorUtils;
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
using LabServer = LabApi.Features.Wrappers.Server;
using Logger = LabApi.Features.Console.Logger;

namespace SCPSLBot.Warmup
{
    internal sealed class WarmupManager
    {
        private const int SurfacePveBotCap = 6;
        private const int HeavyEntrancePvpveBotCap = 5;

        public static WarmupManager Instance { get; } = new WarmupManager();

        private readonly Dictionary<int, RoleTypeId> lastRoles = new();
        private readonly Dictionary<int, WarmupArena> playerArenas = new();
        private readonly Dictionary<int, float> lastArenaSwitchTimes = new();
        private readonly Dictionary<ReferenceHub, WarmupArena> botArenas = new();
        private readonly Dictionary<ReferenceHub, RoleTypeId> warmupAssignedBotRoles = new();
        private readonly HashSet<int> scheduledSpectatorRespawns = new();
        private readonly System.Random random = new();
        private static readonly RoleTypeId[] LczScpRoles =
        {
            RoleTypeId.Scp939,
            RoleTypeId.Scp049,
            RoleTypeId.Scp106,
            RoleTypeId.Scp096,
            RoleTypeId.Scp3114,
            RoleTypeId.Scp173,
            RoleTypeId.Scp0492,
        };
        private BotPluginConfig config;
        private int generation;
        private float dummySpawnNotBeforeTime;
        private bool serverReadyForDummies;
        private int scheduledDummyReadinessRetryGeneration = -1;
        private CoroutineHandle emptyServerWatcherHandle;
        private bool hasSeenHumanConnection;
        private float emptyServerSinceTime = -1f;
        private float lastEmptyServerRestartAttemptTime = -100000f;

        public WarmupMode Mode => config?.WarmupMode ?? WarmupMode.None;

        public bool IsStandardWarmup => Mode == WarmupMode.Standard;

        public WarmupArena DefaultArena => config?.DefaultWarmupArena ?? WarmupArena.HeavyEntrancePvpve;

        public void Init(BotPluginConfig pluginConfig)
        {
            config = pluginConfig;
            config.WarmupMode = config.DefaultWarmupMode;
            generation++;

            ServerEvents.RoundStarted += OnRoundStarted;
            ServerEvents.RoundRestarted += OnRoundRestarted;
            ServerEvents.MapGenerated += OnMapGenerated;
            ServerEvents.WaitingForPlayers += OnWaitingForPlayers;
            ServerEvents.WaveRespawning += OnWaveRespawning;
            ServerEvents.RoundEndingConditionsCheck += OnRoundEndingConditionsCheck;
            PlayerEvents.Joined += OnPlayerJoined;
            PlayerEvents.Cuffing += OnPlayerCuffing;
            PlayerEvents.InteractingScp330 += OnPlayerInteractingScp330;
            PlayerEvents.InteractedScp330 += OnPlayerInteractedScp330;
            PlayerEvents.UnlockingWarheadButton += OnPlayerUnlockingWarheadButton;
            PlayerEvents.InteractingWarheadLever += OnPlayerInteractingWarheadLever;
            PlayerEvents.Spawned += OnPlayerSpawned;
            PlayerEvents.Death += OnPlayerDeath;
            PlayerEvents.Left += OnPlayerLeft;
            LabApi.Events.Handlers.WarheadEvents.Starting += OnWarheadStarting;
            LabApi.Events.Handlers.WarheadEvents.Detonating += OnWarheadDetonating;
            emptyServerWatcherHandle = Timing.RunCoroutine(RunEmptyServerRestartWatcher());

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
            Timing.KillCoroutines(emptyServerWatcherHandle);
            LabApi.Events.Handlers.WarheadEvents.Detonating -= OnWarheadDetonating;
            LabApi.Events.Handlers.WarheadEvents.Starting -= OnWarheadStarting;
            PlayerEvents.Left -= OnPlayerLeft;
            PlayerEvents.Death -= OnPlayerDeath;
            PlayerEvents.Spawned -= OnPlayerSpawned;
            PlayerEvents.InteractingWarheadLever -= OnPlayerInteractingWarheadLever;
            PlayerEvents.UnlockingWarheadButton -= OnPlayerUnlockingWarheadButton;
            PlayerEvents.InteractedScp330 -= OnPlayerInteractedScp330;
            PlayerEvents.InteractingScp330 -= OnPlayerInteractingScp330;
            PlayerEvents.Cuffing -= OnPlayerCuffing;
            PlayerEvents.Joined -= OnPlayerJoined;
            ServerEvents.RoundEndingConditionsCheck -= OnRoundEndingConditionsCheck;
            ServerEvents.WaveRespawning -= OnWaveRespawning;
            ServerEvents.WaitingForPlayers -= OnWaitingForPlayers;
            ServerEvents.MapGenerated -= OnMapGenerated;
            ServerEvents.RoundRestarted -= OnRoundRestarted;
            ServerEvents.RoundStarted -= OnRoundStarted;
            lastRoles.Clear();
            playerArenas.Clear();
            lastArenaSwitchTimes.Clear();
            botArenas.Clear();
            warmupAssignedBotRoles.Clear();
            scheduledSpectatorRespawns.Clear();
            serverReadyForDummies = false;
            hasSeenHumanConnection = false;
            emptyServerSinceTime = -1f;
            lastEmptyServerRestartAttemptTime = -100000f;
            UnlockCheckpointsAndElevatorsIfNeeded();
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
                UnlockCheckpointsAndElevatorsIfNeeded();
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
            LockCheckpointsAndElevatorsIfNeeded();
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
            LockCheckpointsAndElevatorsIfNeeded();
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

        public WarmupArena GetPlayerArena(int playerId)
        {
            return playerArenas.TryGetValue(playerId, out var arena) ? arena : DefaultArena;
        }

        public bool TryGetBotArena(ReferenceHub hub, out WarmupArena arena)
        {
            return botArenas.TryGetValue(hub, out arena);
        }

        public bool TrySetPlayerArena(int playerId, string arenaName, out string response)
        {
            if (config == null)
            {
                response = "SCPSLBot warmup config is not loaded.";
                return false;
            }

            if (!TryParseArena(arenaName, out var arena))
            {
                response = "Unknown warmup arena. Use: surface, pvpve, lcz.";
                return false;
            }

            var previousArena = GetPlayerArena(playerId);
            bool arenaChanged = previousArena != arena;

            if (arenaChanged && !CanSwitchPlayerArena(playerId, out var remainingCooldownSeconds))
            {
                response = $"Please wait {Mathf.CeilToInt(remainingCooldownSeconds)}s before switching warmup arena again.";
                return false;
            }

            playerArenas[playerId] = arena;
            if (arenaChanged)
            {
                lastArenaSwitchTimes[playerId] = Time.realtimeSinceStartup;
            }

            ResetPlayerToArenaDefaultRole(playerId, arena);
            EnsureWarmupBots();
            response = arenaChanged
                ? $"Warmup arena set to {GetArenaDisplayName(arena)}."
                : $"Already in {GetArenaDisplayName(arena)}; role reset.";
            return true;
        }

        public bool TrySetArenaBotCount(string arenaName, int targetCount, out string response)
        {
            if (config == null)
            {
                response = "SCPSLBot warmup config is not loaded.";
                return false;
            }

            if (!TryParseArena(arenaName, out var arena))
            {
                response = "Unknown warmup arena. Use: surface, pvpve, lcz.";
                return false;
            }

            switch (arena)
            {
                case WarmupArena.HeavyEntrancePvpve:
                    config.HeavyEntrancePvpveBotCount = Mathf.Clamp(targetCount, 0, HeavyEntrancePvpveBotCap);
                    response = $"PvPvE bot count set to {config.HeavyEntrancePvpveBotCount}.";
                    break;
                case WarmupArena.LightContainmentScp:
                    config.LightContainmentHumanBotCount = 0;
                    config.LightContainmentScpBotCount = 1;
                    response = "LCZ uses one fixed random SCP bot.";
                    LabApiPlugin.Instance?.SaveSettings();
                    EnsureWarmupBots();
                    return false;
                default:
                    response = "Surface PvE bot count is derived from player count. Set the surface factor instead.";
                    return false;
            }

            LabApiPlugin.Instance?.SaveSettings();
            EnsureWarmupBots();
            return true;
        }

        public bool TrySetLightContainmentScpBotCount(int targetCount, out string response)
        {
            if (config == null)
            {
                response = "SCPSLBot warmup config is not loaded.";
                return false;
            }

            config.LightContainmentScpBotCount = 1;
            config.LightContainmentHumanBotCount = 0;
            LabApiPlugin.Instance?.SaveSettings();
            EnsureWarmupBots();
            response = "LCZ uses one fixed random SCP bot.";
            return false;
        }

        public bool TrySetSurfaceBotFactor(float factor, out string response)
        {
            if (config == null)
            {
                response = "SCPSLBot warmup config is not loaded.";
                return false;
            }

            config.SurfacePveBotFactor = Mathf.Clamp(factor, 1f, 2f);
            LabApiPlugin.Instance?.SaveSettings();
            EnsureWarmupBots();
            response = $"Surface PvE bot factor set to {config.SurfacePveBotFactor:0.##}.";
            return true;
        }

        private void OnRoundStarted()
        {
            if (IsStandardWarmup)
            {
                EnsureWarmupBots();
                DisableLczDecontaminationIfNeeded();
                DisableWarheadIfNeeded();
                LockCheckpointsAndElevatorsIfNeeded();
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

        private void OnMapGenerated(MapGeneratedEventArgs ev)
        {
            if (IsStandardWarmup)
            {
                Timing.CallDelayed(0.2f, LockCheckpointsAndElevatorsIfNeeded);
                Timing.CallDelayed(1f, LockCheckpointsAndElevatorsIfNeeded);
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

        private void OnWaveRespawning(WaveRespawningEventArgs ev)
        {
            if (IsStandardWarmup)
            {
                ev.IsAllowed = false;
            }
        }

        private void OnPlayerJoined(PlayerJoinedEventArgs ev)
        {
            if (CanWarmupRespawn(ev.Player) && !IsBot(ev.Player))
            {
                hasSeenHumanConnection = true;
                emptyServerSinceTime = -1f;
            }

            if (IsStandardWarmup)
            {
                ScheduleSpectatorRespawn(ev.Player);
                EnsureWarmupBots();
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

        private void OnPlayerInteractingScp330(PlayerInteractingScp330EventArgs ev)
        {
            if (!ShouldBlockScp330HandLoss() || ev.Player == null || ev.Player.IsDestroyed)
            {
                return;
            }

            ev.AllowPunishment = false;
            if (ev.Uses >= 2)
            {
                ev.Uses = 1;
            }

            RemoveScp330HandLossIfNeeded(ev.Player);
        }

        private void OnPlayerInteractedScp330(PlayerInteractedScp330EventArgs ev)
        {
            if (!ShouldBlockScp330HandLoss() || ev.Player == null || ev.Player.IsDestroyed)
            {
                return;
            }

            int playerId = ev.Player.PlayerId;
            int scheduleGeneration = generation;
            Timing.CallDelayed(0.05f, () =>
            {
                if (scheduleGeneration == generation
                    && LabPlayer.TryGet(playerId, out var player)
                    && player != null
                    && !player.IsDestroyed)
                {
                    RemoveScp330HandLossIfNeeded(player);
                }
            });
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

            ev.IsAllowed = false;
            DisableWarheadIfNeeded();
            Logger.Info("[SCPSLBot] Warhead start blocked in warmup.");
        }

        private void OnWarheadDetonating(WarheadDetonatingEventArgs ev)
        {
            if (!ShouldBlockWarhead())
            {
                return;
            }

            ev.IsAllowed = false;
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

            if (IsBot(ev.Player))
            {
                ScheduleBotSpawnProtectionRemoval(ev.Player);
                ScheduleBotArenaSpawn(ev.Player);
            }

            if (IsStandardWarmup)
            {
                RemoveScp330HandLossIfNeeded(ev.Player);
                ScheduleSpectatorRespawn(ev.Player);
                if (!IsBot(ev.Player))
                {
                    EnsureWarmupBots();
                }
            }

            if (ev.Player.Role is RoleTypeId.Scp049 or RoleTypeId.Scp173
                && (!IsBot(ev.Player) || GetPlayerArena(ev.Player.PlayerId) != WarmupArena.LightContainmentScp)
                && (!botArenas.TryGetValue(ev.Player.ReferenceHub, out var botArena) || botArena != WarmupArena.LightContainmentScp))
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
            var role = ResolveRespawnRole(ev.Player, ev.OldRole);
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
                playerArenas.Remove(ev.Player.PlayerId);
                lastArenaSwitchTimes.Remove(ev.Player.PlayerId);
                botArenas.Remove(ev.Player.ReferenceHub);
                warmupAssignedBotRoles.Remove(ev.Player.ReferenceHub);
                scheduledSpectatorRespawns.Remove(ev.Player.PlayerId);
            }

            if (IsStandardWarmup)
            {
                Timing.CallDelayed(0.25f, EnsureWarmupBots);
            }
        }

        private IEnumerator<float> RunEmptyServerRestartWatcher()
        {
            while (true)
            {
                float intervalSeconds = Mathf.Max(5f, config?.EmptyServerRestartCheckIntervalSeconds ?? 30f);
                yield return Timing.WaitForSeconds(intervalSeconds);
                UpdateEmptyServerRestartWatcher();
            }
        }

        private void UpdateEmptyServerRestartWatcher()
        {
            if (config == null || !config.EnableEmptyServerAutoRestart)
            {
                emptyServerSinceTime = -1f;
                return;
            }

            if (HasConnectedHumanPlayers())
            {
                hasSeenHumanConnection = true;
                emptyServerSinceTime = -1f;
                return;
            }

            if (!hasSeenHumanConnection)
            {
                return;
            }

            if (emptyServerSinceTime < 0f)
            {
                emptyServerSinceTime = Time.realtimeSinceStartup;
                return;
            }

            float delaySeconds = Mathf.Max(30f, config.EmptyServerRestartDelaySeconds);
            float cooldownSeconds = Mathf.Max(delaySeconds, config.EmptyServerRestartCooldownSeconds);
            float now = Time.realtimeSinceStartup;
            if (now - emptyServerSinceTime < delaySeconds
                || now - lastEmptyServerRestartAttemptTime < cooldownSeconds)
            {
                return;
            }

            lastEmptyServerRestartAttemptTime = now;
            hasSeenHumanConnection = false;
            emptyServerSinceTime = -1f;
            Logger.Info($"[SCPSLBot] No human players connected for {delaySeconds:0.#}s; restarting server.");

            try
            {
                LabServer.Restart();
            }
            catch (Exception ex)
            {
                Logger.Warn($"[SCPSLBot] Empty-server restart failed: {ex.Message}");
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

                RespawnPlayer(player.PlayerId, ResolveRespawnRole(player, player.Role));
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

                var arena = GetPlayerArena(playerId);
                var spawnRole = ResolveArenaRole(arena, config?.WarmupHumanRole ?? RoleTypeId.NtfPrivate, forHumanPlayer: true);
                livePlayer.SetRole(spawnRole, RoleChangeReason.Respawn, RoleSpawnFlags.All);
                CacheRole(playerId, spawnRole);
                ScheduleArenaSpawn(playerId, arena, scheduleGeneration, 0.08f);
                ScheduleArenaSpawn(playerId, arena, scheduleGeneration, 0.35f);
                EnsureWarmupBots();
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
            var arena = GetPlayerArena(playerId);
            ScheduleArenaSpawn(playerId, arena, generation, 0.08f);
            ScheduleArenaSpawn(playerId, arena, generation, 0.35f);
        }

        private RoleTypeId ResolveRespawnRole(LabPlayer player, RoleTypeId fallback)
        {
            int playerId = player.PlayerId;
            bool isBot = IsBot(player);
            var arena = isBot && botArenas.TryGetValue(player.ReferenceHub, out var botArena)
                ? botArena
                : GetPlayerArena(playerId);

            if (isBot && arena == WarmupArena.LightContainmentScp)
            {
                var role = GetRandomLczScpRole();
                warmupAssignedBotRoles[player.ReferenceHub] = role;
                return role;
            }

            if (IsRespawnRole(fallback))
            {
                return ResolveArenaRole(arena, fallback, forHumanPlayer: !isBot);
            }

            if (lastRoles.TryGetValue(playerId, out var cachedRole) && IsRespawnRole(cachedRole))
            {
                return ResolveArenaRole(arena, cachedRole, forHumanPlayer: !isBot);
            }

            return ResolveArenaRole(arena, isBot ? config.WarmupBotRole : config.DefaultRespawnRole, forHumanPlayer: !isBot);
        }

        private void EnsureWarmupBots()
        {
            if (!IsStandardWarmup || config == null)
            {
                return;
            }

            if (!CanManageDummies())
            {
                ScheduleDummyReadinessRetry();
                return;
            }

            scheduledDummyReadinessRetryGeneration = -1;

            foreach (var staleHub in botArenas.Keys
                .Where(hub => hub == null || !BotManager.Instance.BotPlayers.ContainsKey(hub))
                .ToArray())
            {
                botArenas.Remove(staleHub);
                warmupAssignedBotRoles.Remove(staleHub);
            }

            var specs = BuildDesiredBotSpecs();
            int targetCount = specs.Count;
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
                    botArenas.Remove(hub);
                    warmupAssignedBotRoles.Remove(hub);
                }
            }

            var assignedBots = BotManager.Instance.BotPlayers.Keys
                .OrderBy(hub => hub.PlayerId)
                .Take(targetCount)
                .ToArray();

            for (int i = 0; i < assignedBots.Length; i++)
            {
                ReferenceHub hub = assignedBots[i];
                if (hub == null || hub.roleManager == null)
                {
                    continue;
                }

                var spec = specs[i];
                bool changedArena = botArenas.TryGetValue(hub, out var previousArena) && previousArena != spec.Arena;
                botArenas[hub] = spec.Arena;
                if (!TryEnsureBotInitialRole(hub, spec.Role, changedArena))
                {
                    Timing.CallDelayed(0.5f, EnsureWarmupBots);
                }

                ScheduleBotArenaSpawn(hub, spec.Arena, generation, 0.2f);
            }
        }

        private bool TryEnsureBotInitialRole(ReferenceHub hub, RoleTypeId defaultRole, bool changedArena)
        {
            try
            {
                var currentRole = hub.roleManager.CurrentRole;
                bool hasWarmupAssignedRole = warmupAssignedBotRoles.TryGetValue(hub, out var warmupRole);
                bool needsInitialRole = !hasWarmupAssignedRole
                    && (currentRole == null
                        || currentRole.RoleTypeId == RoleTypeId.None
                        || currentRole.RoleTypeId == RoleTypeId.Spectator);
                bool needsWarmupZoneDefault = changedArena
                    && hasWarmupAssignedRole
                    && currentRole != null
                    && currentRole.RoleTypeId == warmupRole
                    && currentRole.RoleTypeId != defaultRole;

                if (needsInitialRole || needsWarmupZoneDefault)
                {
                    hub.roleManager.ServerSetRole(defaultRole, RoleChangeReason.RemoteAdmin);
                    warmupAssignedBotRoles[hub] = defaultRole;
                    CacheRole(hub.PlayerId, defaultRole);
                }
                else if (currentRole != null && IsRespawnRole(currentRole.RoleTypeId))
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

        private List<BotArenaSpec> BuildDesiredBotSpecs()
        {
            var specs = new List<BotArenaSpec>();

            int surfacePlayers = CountHumanPlayersInArena(WarmupArena.SurfacePve);
            int surfaceBots = surfacePlayers > 0
                ? Mathf.Min(
                    Mathf.Clamp(config.SurfacePveMaxBotCount, 0, SurfacePveBotCap),
                    Mathf.Max(2, Mathf.CeilToInt(surfacePlayers * Mathf.Clamp(config.SurfacePveBotFactor, 1f, 2f))))
                : 0;
            for (int i = 0; i < surfaceBots; i++)
            {
                specs.Add(new BotArenaSpec(WarmupArena.SurfacePve, GetSurfaceBotRole(i), EnforceRole: true));
            }

            int pvpvePlayers = CountHumanPlayersInArena(WarmupArena.HeavyEntrancePvpve);
            int pvpveBots = pvpvePlayers > 0
                ? Mathf.Clamp(config.HeavyEntrancePvpveBotCount, 0, HeavyEntrancePvpveBotCap)
                : 0;
            var pvpveDefaultRole = IsRespawnRole(config.WarmupBotRole) ? config.WarmupBotRole : RoleTypeId.ChaosRifleman;
            for (int i = 0; i < pvpveBots; i++)
            {
                specs.Add(new BotArenaSpec(WarmupArena.HeavyEntrancePvpve, pvpveDefaultRole, EnforceRole: false));
            }

            int lczHumanBots = 0;
            for (int i = 0; i < lczHumanBots; i++)
            {
                specs.Add(new BotArenaSpec(WarmupArena.LightContainmentScp, i % 2 == 0 ? RoleTypeId.ClassD : RoleTypeId.Scientist, EnforceRole: true));
            }

            int lczPlayers = CountHumanPlayersInArena(WarmupArena.LightContainmentScp);
            int lczScpBots = lczPlayers > 0 ? 1 : 0;
            for (int i = 0; i < lczScpBots; i++)
            {
                specs.Add(new BotArenaSpec(WarmupArena.LightContainmentScp, GetRandomLczScpRole(), EnforceRole: true));
            }

            return specs;
        }

        private int CountHumanPlayersInArena(WarmupArena arena)
        {
            return LabPlayer.ReadyList.Count(player =>
                CanWarmupRespawn(player)
                && !IsBot(player)
                && GetPlayerArena(player.PlayerId) == arena);
        }

        private bool CanSwitchPlayerArena(int playerId, out float remainingCooldownSeconds)
        {
            remainingCooldownSeconds = 0f;
            float cooldownSeconds = Mathf.Max(0f, config?.WarmupArenaSwitchCooldownSeconds ?? 30f);
            if (cooldownSeconds <= 0f || !lastArenaSwitchTimes.TryGetValue(playerId, out var lastSwitchTime))
            {
                return true;
            }

            remainingCooldownSeconds = cooldownSeconds - (Time.realtimeSinceStartup - lastSwitchTime);
            if (remainingCooldownSeconds <= 0f)
            {
                remainingCooldownSeconds = 0f;
                return true;
            }

            return false;
        }

        private void ResetPlayerToArenaDefaultRole(int playerId, WarmupArena arena)
        {
            if (!LabPlayer.TryGet(playerId, out var player)
                || player == null
                || player.IsDestroyed
                || IsBot(player))
            {
                return;
            }

            var role = GetDefaultHumanRole(arena);
            CacheRole(playerId, role);
            if (player.Role != role)
            {
                player.SetRole(role, RoleChangeReason.RemoteAdmin, RoleSpawnFlags.All);
                ScheduleArenaSpawn(playerId, arena, generation, 0.08f);
            }
            else
            {
                ScheduleArenaSpawn(playerId, arena, generation, 0.02f);
            }

            ScheduleArenaSpawn(playerId, arena, generation, 0.35f);
        }

        private void EnforcePlayerArenaRoleIfNeeded(LabPlayer player)
        {
            if (player == null || player.IsDestroyed || IsBot(player) || !CanWarmupRespawn(player))
            {
                return;
            }

            var arena = GetPlayerArena(player.PlayerId);
            var resolvedRole = ResolveArenaRole(arena, player.Role, forHumanPlayer: true);
            if (resolvedRole == player.Role)
            {
                ScheduleArenaSpawn(player.PlayerId, arena, generation, 0.1f);
                return;
            }

            int scheduleGeneration = generation;
            int playerId = player.PlayerId;
            Timing.CallDelayed(0.05f, () =>
            {
                if (scheduleGeneration != generation
                    || !LabPlayer.TryGet(playerId, out var livePlayer)
                    || livePlayer == null
                    || livePlayer.IsDestroyed
                    || IsBot(livePlayer))
                {
                    return;
                }

                livePlayer.SetRole(resolvedRole, RoleChangeReason.RemoteAdmin, RoleSpawnFlags.All);
                CacheRole(playerId, resolvedRole);
                ScheduleArenaSpawn(playerId, arena, scheduleGeneration, 0.08f);
            });
        }

        private RoleTypeId ResolveArenaRole(WarmupArena arena, RoleTypeId requestedRole, bool forHumanPlayer)
        {
            if (arena == WarmupArena.SurfacePve && forHumanPlayer && !IsNtfRole(requestedRole))
            {
                return RoleTypeId.NtfPrivate;
            }

            if (IsRespawnRole(requestedRole))
            {
                return requestedRole;
            }

            return GetDefaultHumanRole(arena);
        }

        private static RoleTypeId GetDefaultHumanRole(WarmupArena arena)
        {
            return arena switch
            {
                WarmupArena.SurfacePve => RoleTypeId.NtfPrivate,
                WarmupArena.LightContainmentScp => RoleTypeId.ClassD,
                _ => RoleTypeId.NtfPrivate,
            };
        }

        private static RoleTypeId GetSurfaceBotRole(int index)
        {
            return index % 3 == 0 ? RoleTypeId.ChaosConscript : RoleTypeId.ChaosRepressor;
        }

        private RoleTypeId GetRandomLczScpRole()
        {
            return LczScpRoles[random.Next(LczScpRoles.Length)];
        }

        private void ScheduleBotArenaSpawn(LabPlayer player)
        {
            if (player == null || !botArenas.TryGetValue(player.ReferenceHub, out var arena))
            {
                return;
            }

            ScheduleBotArenaSpawn(player.ReferenceHub, arena, generation, 0.12f);
            ScheduleBotArenaSpawn(player.ReferenceHub, arena, generation, 0.45f);
        }

        private void ScheduleBotArenaSpawn(ReferenceHub hub, WarmupArena arena, int scheduleGeneration, float delaySeconds)
        {
            if (arena == WarmupArena.SurfacePve)
            {
                return;
            }

            int playerId = hub.PlayerId;
            Timing.CallDelayed(delaySeconds, () =>
            {
                if (scheduleGeneration != generation
                    || !LabPlayer.TryGet(playerId, out var player)
                    || player == null
                    || player.IsDestroyed
                    || !IsBot(player)
                    || !botArenas.TryGetValue(player.ReferenceHub, out var liveArena)
                    || liveArena != arena)
                {
                    return;
                }

                TeleportToRandomArenaRoom(player, arena);
            });
        }

        private void ScheduleArenaSpawn(int playerId, WarmupArena arena, int scheduleGeneration, float delaySeconds)
        {
            if (arena == WarmupArena.SurfacePve)
            {
                return;
            }

            Timing.CallDelayed(delaySeconds, () =>
            {
                if (scheduleGeneration != generation
                    || !LabPlayer.TryGet(playerId, out var player)
                    || player == null
                    || player.IsDestroyed
                    || IsBot(player))
                {
                    return;
                }

                TeleportToRandomArenaRoom(player, arena);
            });
        }

        private void TeleportToRandomArenaRoom(LabPlayer player, WarmupArena arena)
        {
            var rooms = LabRoom.List
                .Where(room => room != null && !room.IsDestroyed && IsArenaZone(arena, room.Zone))
                .ToArray();
            if (rooms.Length == 0)
            {
                return;
            }

            var room = rooms[random.Next(rooms.Length)];
            player.Position = room.Position + Vector3.up * 1.2f;
        }

        public bool IsHubAllowedInArena(ReferenceHub hub, Vector3 position)
        {
            if (!IsStandardWarmup || hub == null)
            {
                return true;
            }

            if (!RoomUtils.TryGetRoom(position, out var room))
            {
                return true;
            }

            if (BotManager.Instance.BotPlayers.ContainsKey(hub))
            {
                return !botArenas.TryGetValue(hub, out var arena) || IsArenaZone(arena, room.Zone);
            }

            return IsArenaZone(GetPlayerArena(hub.PlayerId), room.Zone);
        }

        public bool AreHubsInSameArena(ReferenceHub left, ReferenceHub right)
        {
            if (!IsStandardWarmup || left == null || right == null)
            {
                return true;
            }

            var leftArena = BotManager.Instance.BotPlayers.ContainsKey(left) && botArenas.TryGetValue(left, out var botArena)
                ? botArena
                : GetPlayerArena(left.PlayerId);
            var rightArena = BotManager.Instance.BotPlayers.ContainsKey(right) && botArenas.TryGetValue(right, out var targetBotArena)
                ? targetBotArena
                : GetPlayerArena(right.PlayerId);
            return leftArena == rightArena;
        }

        public bool CanHubsFightInWarmup(ReferenceHub left, ReferenceHub right)
        {
            if (!IsStandardWarmup || left == null || right == null)
            {
                return true;
            }

            if (TryGetPhysicalArena(left.transform.position, out var leftArena)
                && TryGetPhysicalArena(right.transform.position, out var rightArena))
            {
                return leftArena == rightArena;
            }

            return AreHubsInSameArena(left, right);
        }

        private static bool TryGetPhysicalArena(Vector3 position, out WarmupArena arena)
        {
            if (RoomUtils.TryGetRoom(position, out var room))
            {
                arena = room.Zone switch
                {
                    FacilityZone.Surface => WarmupArena.SurfacePve,
                    FacilityZone.HeavyContainment or FacilityZone.Entrance => WarmupArena.HeavyEntrancePvpve,
                    FacilityZone.LightContainment => WarmupArena.LightContainmentScp,
                    _ => WarmupArena.HeavyEntrancePvpve,
                };
                return true;
            }

            arena = default;
            return false;
        }

        public static bool IsArenaZone(WarmupArena arena, FacilityZone zone)
        {
            return arena switch
            {
                WarmupArena.SurfacePve => zone == FacilityZone.Surface,
                WarmupArena.HeavyEntrancePvpve => zone is FacilityZone.HeavyContainment or FacilityZone.Entrance,
                WarmupArena.LightContainmentScp => zone == FacilityZone.LightContainment,
                _ => true,
            };
        }

        private static bool TryParseArena(string arenaName, out WarmupArena arena)
        {
            switch ((arenaName ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "surface":
                case "surfacepve":
                case "地表pve":
                    arena = WarmupArena.SurfacePve;
                    return true;
                case "pvpve":
                case "heavy":
                case "hcz":
                case "ez":
                case "heavyentrancepvpve":
                case "重收pvpve混战":
                case "重收&入口pvpve混战":
                    arena = WarmupArena.HeavyEntrancePvpve;
                    return true;
                case "lcz":
                case "lcscp":
                case "lightcontainmentscp":
                case "轻收scp":
                    arena = WarmupArena.LightContainmentScp;
                    return true;
                default:
                    return Enum.TryParse(arenaName, true, out arena);
            }
        }

        public static string GetArenaDisplayName(WarmupArena arena)
        {
            return arena switch
            {
                WarmupArena.SurfacePve => "地表PvE",
                WarmupArena.HeavyEntrancePvpve => "重收&入口PvPvE混战",
                WarmupArena.LightContainmentScp => "轻收SCP",
                _ => arena.ToString(),
            };
        }

        private static bool IsNtfRole(RoleTypeId role)
        {
            return role is RoleTypeId.NtfPrivate
                or RoleTypeId.NtfSergeant
                or RoleTypeId.NtfSpecialist
                or RoleTypeId.NtfCaptain;
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

        private void ScheduleBotSpawnProtectionRemoval(LabPlayer player)
        {
            var playerId = player.PlayerId;
            var scheduleGeneration = generation;

            RemoveBotSpawnProtection(player);
            ScheduleBotSpawnProtectionRemoval(playerId, scheduleGeneration, 0.1f);
            ScheduleBotSpawnProtectionRemoval(playerId, scheduleGeneration, 0.5f);
            ScheduleBotSpawnProtectionRemoval(playerId, scheduleGeneration, 1.5f);
        }

        private void ScheduleBotSpawnProtectionRemoval(int playerId, int scheduleGeneration, float delaySeconds)
        {
            Timing.CallDelayed(delaySeconds, () =>
            {
                if (scheduleGeneration != generation
                    || !LabPlayer.TryGet(playerId, out var player)
                    || player == null
                    || player.IsDestroyed
                    || !IsBot(player))
                {
                    return;
                }

                RemoveBotSpawnProtection(player);
            });
        }

        private static void RemoveBotSpawnProtection(LabPlayer player)
        {
            try
            {
                player.ReferenceHub.playerEffectsController
                    .GetEffect<SpawnProtected>()
                    .ServerDisable();
            }
            catch (Exception ex)
            {
                Logger.Warn($"[SCPSLBot] Failed to remove bot spawn protection: {ex.Message}");
            }
        }

        private static void RemoveScp330HandLossIfNeeded(LabPlayer player)
        {
            try
            {
                player.ReferenceHub.playerEffectsController
                    .GetEffect<SeveredHands>()
                    .ServerDisable();
            }
            catch (Exception ex)
            {
                Logger.Warn($"[SCPSLBot] Failed to remove SCP-330 hand-loss effect: {ex.Message}");
            }
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

        private void ScheduleDummyReadinessRetry()
        {
            if (!IsStandardWarmup || config == null)
            {
                return;
            }

            var scheduleGeneration = generation;
            if (scheduledDummyReadinessRetryGeneration == scheduleGeneration)
            {
                return;
            }

            scheduledDummyReadinessRetryGeneration = scheduleGeneration;
            float delaySeconds = 0.5f;
            if (serverReadyForDummies && Time.realtimeSinceStartup < dummySpawnNotBeforeTime)
            {
                delaySeconds = Mathf.Clamp(dummySpawnNotBeforeTime - Time.realtimeSinceStartup + 0.1f, 0.25f, 10f);
            }

            Timing.CallDelayed(delaySeconds, () =>
            {
                if (scheduledDummyReadinessRetryGeneration == scheduleGeneration)
                {
                    scheduledDummyReadinessRetryGeneration = -1;
                }

                if (scheduleGeneration != generation || !IsStandardWarmup)
                {
                    return;
                }

                EnsureWarmupBots();
            });
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

        private bool ShouldBlockScp330HandLoss()
        {
            return IsStandardWarmup && config != null && config.DisableScp330HandLossInWarmup;
        }

        private bool ShouldLockCheckpointsAndElevators()
        {
            return IsStandardWarmup && config != null && config.LockCheckpointsAndElevatorsInWarmup;
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

        private void LockCheckpointsAndElevatorsIfNeeded()
        {
            if (!ShouldLockCheckpointsAndElevators())
            {
                return;
            }

            SetCheckpointsAndElevatorsLocked(true);
        }

        private void UnlockCheckpointsAndElevatorsIfNeeded()
        {
            if (config == null || !config.LockCheckpointsAndElevatorsInWarmup)
            {
                return;
            }

            SetCheckpointsAndElevatorsLocked(false);
        }

        private static void SetCheckpointsAndElevatorsLocked(bool locked)
        {
            try
            {
                foreach (var door in DoorVariant.AllDoors.ToArray())
                {
                    if (door == null)
                    {
                        continue;
                    }

                    if (door is CheckpointDoor checkpointDoor)
                    {
                        bool shouldLockCheckpoint = locked && !IsEntranceHeavyCheckpoint(checkpointDoor);
                        if (shouldLockCheckpoint)
                        {
                            checkpointDoor.ToggleAllDoors(false);
                            checkpointDoor.NetworkTargetState = false;
                        }

                        checkpointDoor.ServerChangeLock(DoorLockReason.SpecialDoorFeature, shouldLockCheckpoint);

                        foreach (var subDoor in checkpointDoor.SubDoors ?? Array.Empty<DoorVariant>())
                        {
                            if (subDoor == null)
                            {
                                continue;
                            }

                            if (shouldLockCheckpoint)
                            {
                                subDoor.NetworkTargetState = false;
                            }

                            subDoor.ServerChangeLock(DoorLockReason.SpecialDoorFeature, shouldLockCheckpoint);
                        }

                        continue;
                    }

                    if (door is ElevatorDoor)
                    {
                        door.NetworkTargetState = false;
                        door.ServerChangeLock(DoorLockReason.SpecialDoorFeature, locked);
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Warn($"[SCPSLBot] Failed to {(locked ? "lock" : "unlock")} checkpoints/elevators: {ex.Message}");
            }
        }

        private static bool IsEntranceHeavyCheckpoint(CheckpointDoor checkpointDoor)
        {
            if (checkpointDoor == null)
            {
                return false;
            }

            if (checkpointDoor.Rooms != null
                && checkpointDoor.Rooms.Any(room => room != null && room.Zone is FacilityZone.HeavyContainment or FacilityZone.Entrance))
            {
                return true;
            }

            string doorName = checkpointDoor.DoorName ?? checkpointDoor.name ?? string.Empty;
            return doorName.IndexOf("HczCheckpoint", StringComparison.OrdinalIgnoreCase) >= 0
                   || doorName.IndexOf("EzCheckpoint", StringComparison.OrdinalIgnoreCase) >= 0
                   || doorName.IndexOf("EntranceCheckpoint", StringComparison.OrdinalIgnoreCase) >= 0
                   || doorName.IndexOf("Checkpoint_EZ", StringComparison.OrdinalIgnoreCase) >= 0
                   || doorName.IndexOf("Checkpoint_HCZ", StringComparison.OrdinalIgnoreCase) >= 0;
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

        private static bool HasConnectedHumanPlayers()
        {
            return LabPlayer.ReadyList.Any(player =>
                CanWarmupRespawn(player)
                && !IsBot(player));
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

        private readonly record struct BotArenaSpec(WarmupArena Arena, RoleTypeId Role, bool EnforceRole);
    }
}
