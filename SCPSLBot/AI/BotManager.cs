using Interactables.Interobjects.DoorUtils;
using LabApi.Events.Arguments.PlayerEvents;
using LabApi.Events.Handlers;
using LabLogger = LabApi.Features.Console.Logger;
using MapGeneration;
using MEC;
using Mirror;
using NetworkManagerUtils.Dummies;
using PlayerRoles;
using PlayerRoles.FirstPersonControl;
using SCPSLBot.AI.FirstPersonControl;
using SCPSLBot.AI.FirstPersonControl.Perception.Senses.Sight;
using SCPSLBot.Components;
using SCPSLBot.Navigation.Mesh;
using System;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;

namespace SCPSLBot.AI
{
    internal class BotManager
    {
        private const float ArrivalDistance = 0.75f;
        private const float ProgressEpsilon = 0.1f;
        private const float StallSeconds = 5f;
        private const float TeleportThreshold = 1.5f;

        public static BotManager Instance { get; } = new BotManager();

        public Dictionary<ReferenceHub, BotHub> BotPlayers { get; } = new Dictionary<ReferenceHub, BotHub>();

        private readonly Dictionary<ReferenceHub, BotOrderState> orders = new();
        private readonly Dictionary<ReferenceHub, RoleTypeId> requestedRoles = new();

        private CoroutineHandle handle;
        private ReferenceHub pathTarget;
        private Vector3 pathTargetPosition;
        private float nextPathTargetUpdateTime;

        public void Init()
        {
            handle = Timing.RunCoroutine(RunPlayerUpdates());

            PlayerRoleManager.OnRoleChanged += OnRoleChanged;
            ReferenceHub.OnPlayerRemoved += RemovePlayerIfBot;
            PlayerEvents.Hurt += OnPlayerHurt;
            ServerEvents.RoundRestarted += OnRoundRestarted;

            for (int i = 0; i < 32; i++)
            {
                Physics.IgnoreLayerCollision(31, i, true);
            }

            Physics.IgnoreLayerCollision(31, LayerMask.NameToLayer("Door"), false);
            Physics.IgnoreLayerCollision(31, LayerMask.NameToLayer("InteractableNoPlayerCollision"), false);
            Physics.IgnoreLayerCollision(31, LayerMask.NameToLayer("Glass"), false);
            Physics.IgnoreLayerCollision(31, LayerMask.NameToLayer("Hitbox"), false);
        }

        public void Terminate()
        {
            Timing.KillCoroutines(handle);

            PlayerRoleManager.OnRoleChanged -= OnRoleChanged;
            ReferenceHub.OnPlayerRemoved -= RemovePlayerIfBot;
            PlayerEvents.Hurt -= OnPlayerHurt;
            ServerEvents.RoundRestarted -= OnRoundRestarted;

            foreach (var (referenceHub, _) in BotPlayers.ToArray())
            {
                NetworkServer.Destroy(referenceHub.gameObject);
                RemovePlayerIfBot(referenceHub);
            }

            orders.Clear();
            requestedRoles.Clear();
        }

        private void OnRoundRestarted()
        {
            FirstPersonControl.FpcBotPlayer.ResetSpectatorHintState();

            foreach (var (referenceHub, _) in BotPlayers.ToArray())
            {
                RemovePlayerIfBot(referenceHub);
            }

            orders.Clear();
            requestedRoles.Clear();
        }

        public ReferenceHub AddBotPlayer()
            => AddBotPlayer("SCPSL Bot", RoleTypeId.ChaosRifleman);

        public ReferenceHub AddBotPlayer(string nickname, RoleTypeId role)
        {
            if (!CanSpawnBot())
            {
                Debug.LogWarning("Cannot spawn RA dummy bot before the network server is active.");
                return null;
            }

            var referenceHub = DummyUtils.SpawnDummy(string.IsNullOrWhiteSpace(nickname) ? "SCPSL Bot" : nickname);
            if (referenceHub == null)
            {
                Debug.LogError("Failed to spawn RA dummy bot.");
                return null;
            }

            var player = referenceHub.gameObject;
            player.name = $"{NetworkManager.singleton.playerPrefab.name} [bot dummy]";

            BotPlayers.Add(referenceHub, new BotHub(referenceHub));
            requestedRoles[referenceHub] = role;

            Debug.Log($"Spawned RA dummy bot: {referenceHub}; requestedRole={role}");

            var sensing = new GameObject("Bot Sensing");
            sensing.layer = 31;
            sensing.transform.parent = player.transform;

            var perceptionComponent = sensing.AddComponent<PerceptionComponent>();
            BotPlayers[referenceHub].FpcPlayer.Perception.AddTriggerHandlers(perceptionComponent);

            var sensingTrigger = sensing.AddComponent<SphereCollider>();
            sensingTrigger.isTrigger = true;
            sensingTrigger.radius = 32f;

            var sensingRigid = sensing.AddComponent<Rigidbody>();
            sensingRigid.isKinematic = true;

            ScheduleRequestedRole(referenceHub, 0.5f);
            ScheduleRequestedRole(referenceHub, 1.5f);
            return referenceHub;
        }

        public bool CanSpawnBot()
        {
            return NetworkServer.active
                && NetworkManager.singleton != null
                && NetworkManager.singleton.playerPrefab != null;
        }

        private void ScheduleRequestedRole(ReferenceHub referenceHub, float delaySeconds)
        {
            Timing.CallDelayed(delaySeconds, () =>
            {
                if (!TrySetRequestedRole(referenceHub) && delaySeconds < 3f)
                {
                    ScheduleRequestedRole(referenceHub, delaySeconds + 1f);
                }
            });
        }

        private bool TrySetRequestedRole(ReferenceHub referenceHub)
        {
            if (referenceHub == null
                || !BotPlayers.ContainsKey(referenceHub)
                || referenceHub.roleManager == null
                || !requestedRoles.TryGetValue(referenceHub, out var requestedRole))
            {
                return false;
            }

            try
            {
                var currentRole = referenceHub.roleManager.CurrentRole;
                if (currentRole?.RoleTypeId != requestedRole)
                {
                    referenceHub.roleManager.ServerSetRole(requestedRole, RoleChangeReason.RemoteAdmin);
                }

                return referenceHub.roleManager.CurrentRole?.RoleTypeId == requestedRole;
            }
            catch (NullReferenceException)
            {
                return false;
            }
        }

        public IEnumerator<float> RunPlayerUpdates()
        {
            var playersUpdates = new List<IEnumerator<JobHandle>>();

            while (true)
            {
                playersUpdates.Clear();

                var playersCount = BotPlayers.Values.Count;
                foreach (var botHub in BotPlayers.Values)
                {
                    playersUpdates.Add(botHub.Update());
                }

                var jobHandlesBuffer = new NativeArray<JobHandle>(playersCount, Allocator.Temp);
                int jobHandlesCount;

                var completedCount = 0;
                while (completedCount < playersCount)
                {
                    completedCount = 0;
                    jobHandlesCount = 0;
                    foreach (var playerUpdate in playersUpdates)
                    {
                        if (playerUpdate.MoveNext())
                        {
                            jobHandlesBuffer[jobHandlesCount] = playerUpdate.Current;
                            jobHandlesCount++;
                        }
                        else
                        {
                            completedCount++;
                        }
                    }

                    var jobHandles = jobHandlesBuffer.GetSubArray(0, jobHandlesCount);
                    JobHandle.CompleteAll(jobHandles);
                }

                jobHandlesBuffer.Dispose();
                yield return Timing.WaitForOneFrame;
            }
        }

        public void OnRoleChanged(ReferenceHub userHub, PlayerRoleBase prevRole, PlayerRoleBase newRole)
        {
            if (BotPlayers.TryGetValue(userHub, out var botPlayer))
            {
                botPlayer.OnRoleChanged(prevRole, newRole);
                LabLogger.Info($"[BotOrders] ROLE bot={BotName(userHub)} previous={prevRole?.RoleTypeId} current={newRole?.RoleTypeId}");
            }
        }

        private void OnPlayerHurt(PlayerHurtEventArgs ev)
        {
            if (ev.Player == null || ev.Attacker == null)
            {
                return;
            }

            if (BotPlayers.TryGetValue(ev.Player.ReferenceHub, out var botPlayer))
            {
                botPlayer.NotifyHurt(ev.Attacker.ReferenceHub);
            }
        }

        public void RemovePlayerIfBot(ReferenceHub userHub)
        {
            orders.Remove(userHub);
            requestedRoles.Remove(userHub);
            if (BotPlayers.Remove(userHub))
            {
                Debug.Log($"Bot player removed: {userHub}");
            }
        }

        public bool DespawnBot(ReferenceHub hub)
        {
            if (hub == null || !BotPlayers.ContainsKey(hub))
            {
                return false;
            }

            NetworkServer.Destroy(hub.gameObject);
            RemovePlayerIfBot(hub);
            return true;
        }

        public bool IssueMoveToRoomOrder(ReferenceHub hub, RoomName roomName)
        {
            if (hub == null || !BotPlayers.ContainsKey(hub))
            {
                return false;
            }

            var room = RoomIdentifier.AllRoomIdentifiers.FirstOrDefault(candidate => candidate.Name == roomName);
            if (room == null || !NavigationMesh.LocalMeshesByRoom.TryGetValue(room.gameObject, out var mesh) || mesh.Cells.Count == 0)
            {
                LabLogger.Error($"[BotOrders] ROOM_UNAVAILABLE bot={BotName(hub)} room={roomName}");
                return false;
            }

            var origin = hub.transform.position;
            var goal = mesh.Cells
                .Select(cell => room.transform.TransformPoint(cell.CenterPosition))
                .OrderBy(position => HorizontalDistance(origin, position))
                .First();

            return IssueMoveOrder(hub, goal, BotOrderKind.MoveToRoom, $"room:{roomName}");
        }

        public bool IssueMoveOrder(ReferenceHub hub, Vector3 goal, BotOrderKind kind, string description)
        {
            if (hub == null || !BotPlayers.ContainsKey(hub))
            {
                return false;
            }

            var now = Time.time;
            var state = new BotOrderState
            {
                Kind = kind,
                Description = description,
                Goal = goal,
                Active = true,
                HasPath = false,
                DistanceRemaining = HorizontalDistance(hub.transform.position, goal),
                IssuedAt = now,
                LastProgressAt = now,
                LastProgressUtc = DateTime.UtcNow,
                LastPosition = hub.transform.position,
                LastWaypointDistance = float.PositiveInfinity,
                NextBreadcrumbAt = now + 2f,
            };
            orders[hub] = state;

            if (NavigationMesh.LocalMeshesByRoom.Count > 0 && NavigationMesh.GetCellWithin(goal) == null)
            {
                TryGetNearestMeshPoint(goal, out var nearest, out var nearestDistance);
                state.Kind = BotOrderKind.FailedOffMesh;
                state.Active = false;
                state.FailureReason = "goal is outside the navigation mesh";
                state.HasPath = false;
                LabLogger.Error($"[BotOrders] OFF_MESH bot={BotName(hub)} goal={Format(goal)} nearest={Format(nearest)} nearestDistance={nearestDistance:F2}");
                LogSummary(hub, state, "FAIL");
                return false;
            }

            LabLogger.Info($"[BotOrders] ORDER bot={BotName(hub)} kind={kind} description={description} goal={Format(goal)} role={hub.roleManager?.CurrentRole?.RoleTypeId}");
            return true;
        }

        public bool StopOrder(ReferenceHub hub, string reason)
        {
            if (hub == null || !BotPlayers.ContainsKey(hub))
            {
                return false;
            }

            if (!orders.TryGetValue(hub, out var state))
            {
                state = new BotOrderState
                {
                    Goal = hub.transform.position,
                    Description = "hold",
                    IssuedAt = Time.time,
                    LastProgressAt = Time.time,
                    LastProgressUtc = DateTime.UtcNow,
                };
                orders[hub] = state;
            }

            state.Kind = BotOrderKind.Stopped;
            state.Active = false;
            state.HasPath = false;
            state.FailureReason = reason;
            if (BotPlayers.TryGetValue(hub, out var botHub))
            {
                botHub.FpcPlayer.Move.DesiredLocalDirection = Vector3.zero;
            }

            LabLogger.Info($"[BotOrders] STOP bot={BotName(hub)} reason={reason} pos={Format(hub.transform.position)}");
            return true;
        }

        public bool TryGetOrderedTarget(ReferenceHub hub, out Vector3 target)
        {
            if (orders.TryGetValue(hub, out var state) && state.Active)
            {
                target = state.Goal;
                return true;
            }

            target = default;
            return false;
        }

        public bool ShouldHoldPosition(ReferenceHub hub)
            => orders.TryGetValue(hub, out var state) && !state.Active;

        public void ObserveOrderTick(FpcBotPlayer player, Vector3 waypoint)
        {
            var hub = player.BotHub.PlayerHub;
            if (!orders.TryGetValue(hub, out var state) || !state.Active)
            {
                return;
            }

            var now = Time.time;
            var position = player.PlayerPosition;
            var remaining = HorizontalDistance(position, state.Goal);
            state.DistanceRemaining = remaining;
            state.HasPath = player.Navigator.HasPath;

            if (state.SampledPosition)
            {
                var tickDistance = Vector3.Distance(position, state.LastPosition);
                state.MaxTickDistance = Mathf.Max(state.MaxTickDistance, tickDistance);
                if (tickDistance > TeleportThreshold)
                {
                    state.TeleportDetected = true;
                    LabLogger.Error($"[BotOrders] TELEPORT bot={BotName(hub)} tickDistance={tickDistance:F3} from={Format(state.LastPosition)} to={Format(position)} threshold={TeleportThreshold:F2}");
                }
            }
            else
            {
                state.SampledPosition = true;
            }
            state.LastPosition = position;

            TrackRoomAndDoor(hub, state, position);

            if (remaining <= ArrivalDistance)
            {
                CompleteOrder(hub, state);
                return;
            }

            if (!state.HasPath)
            {
                state.Kind = BotOrderKind.FailedNoPath;
                state.Active = false;
                state.FailureReason = "navigator found no connected path";
                LabLogger.Error($"[BotOrders] NO_PATH bot={BotName(hub)} pos={Format(position)} room={RoomNameAt(position)} goal={Format(state.Goal)} goalRoom={RoomNameAt(state.Goal)}");
                LogSummary(hub, state, "FAIL");
                return;
            }

            var waypointDistance = HorizontalDistance(position, waypoint);
            if (Vector3.Distance(waypoint, state.LastWaypoint) > 0.5f)
            {
                state.LastWaypoint = waypoint;
                state.LastWaypointDistance = waypointDistance;
                MarkProgress(state, now);
            }
            else if (waypointDistance < state.LastWaypointDistance - ProgressEpsilon)
            {
                state.LastWaypointDistance = waypointDistance;
                MarkProgress(state, now);
            }

            if (now - state.LastProgressAt >= StallSeconds)
            {
                state.StallCount++;
                var blocker = ProbeBlockingCollider(player, waypoint);
                LabLogger.Warn($"[BotOrders] STALL bot={BotName(hub)} seconds={now - state.LastProgressAt:F1} pos={Format(position)} room={RoomNameAt(position)} goal={Format(state.Goal)} waypoint={Format(waypoint)} blocker={blocker}");
                MarkProgress(state, now);
                player.Navigator.ForceReplan();
            }

            if (now >= state.NextBreadcrumbAt)
            {
                state.NextBreadcrumbAt = now + 2f;
                var ground = ProbeGround(position, out var groundDistance, out var groundCollider);
                if (ground)
                {
                    state.MaxGroundDistance = Mathf.Max(state.MaxGroundDistance, groundDistance);
                }
                else
                {
                    state.GroundProbeMisses++;
                    LabLogger.Error($"[BotOrders] GROUND_MISS bot={BotName(hub)} pos={Format(position)} room={RoomNameAt(position)} misses={state.GroundProbeMisses}");
                }

                LabLogger.Info($"[BotOrders] BREADCRUMB bot={BotName(hub)} t={now - state.IssuedAt:F1} pos={Format(position)} room={RoomNameAt(position)} remaining={remaining:F2} hasPath={state.HasPath} stalls={state.StallCount} doors={state.DoorsTraversed} maxTick={state.MaxTickDistance:F3} ground={(ground ? groundCollider + ":" + groundDistance.ToString("F2") + "m" : "MISS")}");
            }
        }

        public bool TryGetOrderStatus(ReferenceHub hub, out BotOrderStatus status)
        {
            if (hub == null || !orders.TryGetValue(hub, out var state))
            {
                status = null;
                return false;
            }

            var position = hub.transform.position;
            status = new BotOrderStatus
            {
                Bot = hub,
                CurrentOrder = state.Kind,
                Description = state.Description ?? string.Empty,
                Goal = state.Goal,
                IsActive = state.Active,
                HasPath = state.HasPath,
                DistanceRemaining = state.Active ? HorizontalDistance(position, state.Goal) : state.DistanceRemaining,
                LastProgressUtc = state.LastProgressUtc,
                SecondsSinceProgress = Mathf.Max(0f, Time.time - state.LastProgressAt),
                ElapsedSeconds = Mathf.Max(0f, Time.time - state.IssuedAt),
                StallCount = state.StallCount,
                DoorsTraversed = state.DoorsTraversed,
                MaxTickDistance = state.MaxTickDistance,
                TeleportDetected = state.TeleportDetected,
                GroundProbeMisses = state.GroundProbeMisses,
                MaxGroundDistance = state.MaxGroundDistance,
                FailureReason = state.FailureReason ?? string.Empty,
                Room = RoomNameAt(position),
            };
            return true;
        }

        private void CompleteOrder(ReferenceHub hub, BotOrderState state)
        {
            state.Active = false;
            state.HasPath = true;
            state.DistanceRemaining = HorizontalDistance(hub.transform.position, state.Goal);
            state.Kind = BotOrderKind.Completed;
            if (BotPlayers.TryGetValue(hub, out var botHub))
            {
                botHub.FpcPlayer.Move.DesiredLocalDirection = Vector3.zero;
            }

            LogSummary(hub, state, state.TeleportDetected || state.GroundProbeMisses > 0 ? "FAIL" : "PASS");
        }

        private static void MarkProgress(BotOrderState state, float now)
        {
            state.LastProgressAt = now;
            state.LastProgressUtc = DateTime.UtcNow;
        }

        private static bool ProbeGround(Vector3 position, out float distance, out string colliderName)
        {
            if (Physics.Raycast(position + Vector3.up * 0.25f, Vector3.down, out var hit, 4f, FpcStateProcessor.Mask, QueryTriggerInteraction.Ignore))
            {
                distance = hit.distance;
                colliderName = hit.collider.name;
                return true;
            }

            distance = float.PositiveInfinity;
            colliderName = "none";
            return false;
        }

        private static string ProbeBlockingCollider(FpcBotPlayer player, Vector3 waypoint)
        {
            var direction = Vector3.ProjectOnPlane(waypoint - player.PlayerPosition, Vector3.up);
            if (direction.sqrMagnitude < 0.01f)
            {
                return "none";
            }

            direction.Normalize();
            var bottom = player.PlayerPosition + Vector3.up * 0.2f;
            var top = player.PlayerPosition + Vector3.up * 1.6f;
            if (Physics.CapsuleCast(bottom, top, 0.3f, direction, out var hit, 1.5f, FpcStateProcessor.Mask, QueryTriggerInteraction.Ignore))
            {
                return $"{hit.collider.name}@{LayerMask.LayerToName(hit.collider.gameObject.layer)}:{hit.distance:F2}m";
            }

            if (Physics.Raycast(player.CameraPosition, direction, out hit, 2f, FpcStateProcessor.Mask, QueryTriggerInteraction.Ignore))
            {
                return $"{hit.collider.name}@{LayerMask.LayerToName(hit.collider.gameObject.layer)}:{hit.distance:F2}m";
            }

            return "none";
        }

        private static void TrackRoomAndDoor(ReferenceHub hub, BotOrderState state, Vector3 position)
        {
            if (!RoomUtils.TryGetRoom(position, out var currentRoom) || currentRoom == null)
            {
                return;
            }

            if (state.LastRoom != null && state.LastRoom != currentRoom && IsDoorTransition(state.LastRoom, currentRoom, out var doorName))
            {
                state.DoorsTraversed++;
                LabLogger.Info($"[BotOrders] DOOR bot={BotName(hub)} count={state.DoorsTraversed} door={doorName} from={state.LastRoom.Name} to={currentRoom.Name}");
            }

            state.LastRoom = currentRoom;
        }

        private static bool IsDoorTransition(RoomIdentifier from, RoomIdentifier to, out string doorName)
        {
            doorName = "none";
            if (!DoorVariant.DoorsByRoom.TryGetValue(from, out var doors))
            {
                return false;
            }

            var door = doors.FirstOrDefault(candidate => candidate != null && candidate.Rooms.Contains(to));
            if (door == null)
            {
                return false;
            }

            doorName = door.name;
            return true;
        }

        private static bool TryGetNearestMeshPoint(Vector3 goal, out Vector3 nearest, out float distance)
        {
            nearest = Vector3.zero;
            var bestSqr = float.PositiveInfinity;

            foreach (var pair in NavigationMesh.LocalMeshesByRoom)
            {
                var transform = pair.Key.transform;
                foreach (var cell in pair.Value.Cells)
                {
                    var center = transform.TransformPoint(cell.CenterPosition);
                    var centerSqr = (center - goal).sqrMagnitude;
                    if (centerSqr < bestSqr)
                    {
                        bestSqr = centerSqr;
                        nearest = center;
                    }

                    foreach (var vertex in cell.Vertices)
                    {
                        var point = transform.TransformPoint(vertex.Position);
                        var pointSqr = (point - goal).sqrMagnitude;
                        if (pointSqr < bestSqr)
                        {
                            bestSqr = pointSqr;
                            nearest = point;
                        }
                    }
                }
            }

            distance = float.IsPositiveInfinity(bestSqr) ? float.PositiveInfinity : Mathf.Sqrt(bestSqr);
            return !float.IsPositiveInfinity(bestSqr);
        }

        private static void LogSummary(ReferenceHub hub, BotOrderState state, string verdict)
        {
            LabLogger.Info($"[BotOrders] SUMMARY verdict={verdict} bot={BotName(hub)} order={state.Description} elapsed={Time.time - state.IssuedAt:F2} stalls={state.StallCount} doors={state.DoorsTraversed} maxTick={state.MaxTickDistance:F3} teleport={state.TeleportDetected} groundMisses={state.GroundProbeMisses} maxGround={state.MaxGroundDistance:F2} remaining={state.DistanceRemaining:F2} reason={state.FailureReason ?? "none"}");
        }

        private static float HorizontalDistance(Vector3 a, Vector3 b)
            => Vector3.Distance(Vector3.ProjectOnPlane(a, Vector3.up), Vector3.ProjectOnPlane(b, Vector3.up));

        private static string RoomNameAt(Vector3 position)
            => RoomUtils.TryGetRoom(position, out var room) && room != null ? room.Name.ToString() : "none";

        private static string BotName(ReferenceHub hub)
            => hub?.nicknameSync?.MyNick ?? hub?.PlayerId.ToString() ?? "null";

        private static string Format(Vector3 value)
            => $"({value.x:F2},{value.y:F2},{value.z:F2})";

        public bool TogglePathToTarget(ReferenceHub target)
        {
            if (pathTarget == target)
            {
                pathTarget = null;
                return false;
            }

            pathTarget = target;
            pathTargetPosition = target.transform.position;
            nextPathTargetUpdateTime = 0f;
            return true;
        }

        public bool TryGetPathTargetPosition(out Vector3 position)
        {
            if (pathTarget == null)
            {
                position = default;
                return false;
            }

            if (Time.time >= nextPathTargetUpdateTime)
            {
                pathTargetPosition = pathTarget.transform.position;
                nextPathTargetUpdateTime = Time.time + 1f;
            }

            position = pathTargetPosition;
            return true;
        }

        private BotManager()
        { }
    }
}
