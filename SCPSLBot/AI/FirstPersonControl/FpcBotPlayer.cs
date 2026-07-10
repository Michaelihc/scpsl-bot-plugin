using Hints;
using Interactables;
using Interactables.Interobjects.DoorUtils;
using MapGeneration;
using MapGeneration.Distributors;
using PlayerRoles;
using PlayerRoles.FirstPersonControl;
using PlayerRoles.Spectating;
using SCPSLBot.AI.FirstPersonControl.Combat;
using SCPSLBot.AI.FirstPersonControl.Looking;
using SCPSLBot.AI.FirstPersonControl.Mind;
using SCPSLBot.AI.FirstPersonControl.Movement;
using SCPSLBot.AI.FirstPersonControl.Perception.Senses;
using SCPSLBot.AI.FirstPersonControl.Perception.Senses.Sight;
using SCPSLBot.AI.FirstPersonControl.Roaming;
using SCPSLBot.Warmup;
using SCPSLBot.Components;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Profiling;
using static System.Net.Mime.MediaTypeNames;

namespace SCPSLBot.AI.FirstPersonControl
{
    internal partial class FpcBotPlayer : IBotPlayer
    {
        private const float NonCombatGoalUpdateIntervalSeconds = 1f;
        private const float NonCombatGoalSnapDistanceSqr = 16f;
        private const float MaxMoveTurnDegreesPerSecond = 720f;
        private const float StuckTeleportSeconds = 6f;
        private const float StuckMovementProgressDistanceSqr = 2f * 2f;
        private const float OutOfWorldY = -300f;

        public FpcStandardRoleBase FpcRole { get; set; }

        public BotHub BotHub { get; }
        public FpcBotPerception Perception { get; }
        public PerceptionComponent PerceptionComponent { get; private set; }
        public FpcMindRunner MindRunner { get; }

        public FpcBotCombat Combat { get; }
        public FpcZoneRoam ZoneRoam { get; }
        public FpcBotNavigator Navigator { get; }

        public FpcLook Look { get; }
        public FpcMove Move { get; }

        public Vector3 PlayerPosition { get; private set; }
        public Vector3 PlayerForward { get; private set; }

        public Vector3 CameraPosition { get; private set; }
        public Vector3 CameraForward { get; private set; }

        private Vector3 stuckAnchorPosition;
        private Vector3 stuckIntendedDirection;
        private Vector3 nonCombatMoveGoal;
        private Vector3 smoothedMoveWorldDirection;
        private Vector3 lastSafePosition;
        private FacilityZone lastSafeZone;
        private float stuckAnchorTime;
        private float nextStuckJumpTime;
        private float nextStuckTeleportTime;
        private float nextNonCombatGoalUpdateTime;
        private float nextMovementDiagnosticLogTime;
        private float lastMovementIntentTime;
        private bool hasNonCombatMoveGoal;
        private bool hasSmoothedMoveWorldDirection;
        private bool hasLastSafePosition;

        public FpcBotPlayer(BotHub botHub)
        {
            BotHub = botHub;
            Perception = new FpcBotPerception(this);
            MindRunner = new FpcMindRunner();

            Combat = new(this);
            ZoneRoam = new(this);
            Navigator = new(this);
            Look = new(this);
            Move = new(this);

            FpcMindFactory.BuildMind(MindRunner, this, Perception);

            MindRunner.SubscribeToBeliefUpdates();
        }

        public IEnumerator<JobHandle> Update()
        {
            var playerTransform = FpcRole.transform;
            this.PlayerPosition = playerTransform.position;
            this.PlayerForward = playerTransform.forward;

            var cameraTransform = BotHub.PlayerHub.PlayerCameraReference;
            this.CameraPosition = cameraTransform.position;
            this.CameraForward = cameraTransform.forward;

            if (RecoverIfOutOfWorld())
            {
                yield break;
            }

            UpdateLastSafePosition();

            if (Combat.Tick())
            {
                JumpIfForwardMovementBlocked();
                yield break;
            }

            if (BotManager.Instance.TryGetPathTargetPosition(out var pathTargetPosition))
            {
                MoveToPosition(pathTargetPosition);
                JumpIfForwardMovementBlocked();
                yield break;
            }

            var updatePerceptionHandles = Perception.Update();
            while (updatePerceptionHandles.MoveNext())
            {
                yield return updatePerceptionHandles.Current;
            }

            if (ShouldIdleOnSurfaceWithoutTarget())
            {
                Move.DesiredLocalDirection = Vector3.zero;
                ResetStuckJumpTracking();
                yield break;
            }

            if (!ShouldUseEscapeGoal())
            {
                if (!ZoneRoam.Tick())
                {
                    MindRunner.Tick();
                    DisplayVisitedActionsGraph();
                }

                JumpIfForwardMovementBlocked();
                yield break;
            }

            MindRunner.Tick();

            DisplayVisitedActionsGraph();
            JumpIfForwardMovementBlocked();

            yield break;
        }

        private bool ShouldUseEscapeGoal()
        {
            if (WarmupManager.Instance.IsStandardWarmup)
            {
                return false;
            }

            var role = BotHub.PlayerHub.roleManager.CurrentRole.RoleTypeId;
            return role is RoleTypeId.ClassD or RoleTypeId.Scientist;
        }

        private bool ShouldIdleOnSurfaceWithoutTarget()
        {
            return RoomUtils.TryGetRoom(PlayerPosition, out var room) && room.Zone == FacilityZone.Surface;
        }

        public void OnRoleChanged()
        {
            if (LabApiPlugin.Instance?.Config?.EnableVerboseBotLogs == true)
            {
                Debug.Log($"Bot got FPC role assigned.");
            }

            PerceptionComponent = BotHub.PlayerHub.GetComponentInChildren<PerceptionComponent>();
            PerceptionComponent.enabled = true;

            MindRunner.EvaluateGoalsToActions();
            ResetStuckJumpTracking();
            ResetNonCombatMoveGoal();
        }

        #region Moving

        public void MoveToPosition(Vector3 goalPosition) => MoveToPosition(goalPosition, out _);
        public void MoveToPosition(Vector3 goalPosition, out Vector3 positionTowardsGoal)
        {
            goalPosition = GetRateLimitedNonCombatGoal(goalPosition);
            positionTowardsGoal = Navigator.GetPositionTowards(goalPosition);
            SteerTowardPosition(positionTowardsGoal);
        }

        internal void MoveToPositionImmediate(Vector3 goalPosition)
        {
            var positionTowardsGoal = Navigator.GetPositionTowards(goalPosition);
            SteerTowardPosition(positionTowardsGoal);
        }

        private Vector3 GetRateLimitedNonCombatGoal(Vector3 requestedGoal)
        {
            if (!hasNonCombatMoveGoal
                || Time.time >= nextNonCombatGoalUpdateTime
                || (requestedGoal - nonCombatMoveGoal).sqrMagnitude >= NonCombatGoalSnapDistanceSqr)
            {
                nonCombatMoveGoal = requestedGoal;
                nextNonCombatGoalUpdateTime = Time.time + NonCombatGoalUpdateIntervalSeconds;
                hasNonCombatMoveGoal = true;
            }

            return nonCombatMoveGoal;
        }

        private void ResetNonCombatMoveGoal()
        {
            hasNonCombatMoveGoal = false;
            nextNonCombatGoalUpdateTime = 0f;
        }

        internal void SteerTowardPosition(Vector3 positionTowardsGoal)
        {
            SteerToPosition(positionTowardsGoal);
            SteerAwayFromObstacles();
            SmoothDesiredMoveDirection();
        }

        private void SteerToPosition(Vector3 positionTowardsGoal)
        {
            var relativePos = positionTowardsGoal - this.FpcRole.CameraPosition;
            var relativeHorizontalPos = Vector3.ProjectOnPlane(relativePos, Vector3.up);
            var turnPosition = relativeHorizontalPos + this.FpcRole.CameraPosition;

            this.Look.ToPosition(turnPosition);

            var playerDirection = FpcRole.FpcModule.transform.forward;
            var dirTowardsTarget = Vector3.Normalize(relativeHorizontalPos);

            if (Vector3.Dot(playerDirection, dirTowardsTarget) < 0f)
            {
                this.Move.DesiredLocalDirection = FpcRole.FpcModule.transform.InverseTransformDirection(dirTowardsTarget);
            }
            else
            {
                this.Move.DesiredLocalDirection = Vector3.forward;
            }
        }

        private readonly List<StructureSpawnpoint> structureSpawnpoints = new();
        private readonly List<Collider> spawnableStructureColliders = new();
        private void SteerAwayFromObstacles()
        {
            var roomSightSense = Perception.GetSense<RoomSightSense>();
            var roomWithin = roomSightSense.RoomWithin;
            if (!roomWithin)
            {
                return;
            }

            roomWithin.GetComponentsInChildren(structureSpawnpoints);
            if (structureSpawnpoints.Count < 1)
            {
                return;
            }

            var playerPosition = this.PlayerPosition;
            var moveDirection = this.FpcRole.FpcModule.transform.TransformDirection(this.Move.DesiredLocalDirection);
            var playerRadius = this.BotHub.PlayerHub.GetComponent<CharacterController>().radius;
            var playerHeight = this.BotHub.PlayerHub.GetComponent<CharacterController>().height;

            var playerBottomPosition = playerPosition + Vector3.down * (playerHeight / 2f);

            var obstructingStructure = (SpawnableStructure)null;
            var structureExtent = 0f;
            foreach (var spawnpoint in structureSpawnpoints)
            {
                var structure = spawnpoint.GetComponentInChildren<SpawnableStructure>();
                if (!structure)
                {
                    continue;
                }

                switch (structure.StructureType)
                {
                    case StructureType.Workstation:
                        structureExtent = 1.25f;
                        break;
                    case StructureType.ScpPedestal:
                        structureExtent = .75f;
                        break;
                    default:
                        continue;
                }

                structure.GetComponentsInChildren(spawnableStructureColliders);

                var obstructingCollider = spawnableStructureColliders
                    .Find(c => c.Raycast(new Ray(playerBottomPosition, moveDirection), out var _, 1f) 
                        || ((playerBottomPosition + moveDirection) - c.ClosestPointOnBounds(playerBottomPosition + moveDirection)).sqrMagnitude < playerRadius * playerRadius);
                if (obstructingCollider)
                {
                    obstructingStructure = structure;
                    break;
                }
            }

            if (obstructingStructure == null)
            {
                return;
            }

            var obstructingPosition = obstructingStructure.transform.position;
            var obstructingForward = obstructingStructure.transform.forward;

            var obstructingPlane = new Plane(obstructingForward, obstructingPosition);
            var obstructingDepth = Mathf.Max(structureExtent + playerRadius - obstructingPlane.GetDistanceToPoint(playerPosition + moveDirection), 0f);

            moveDirection = Vector3.Normalize(moveDirection + obstructingForward * obstructingDepth);
            this.Move.DesiredLocalDirection = FpcRole.FpcModule.transform.InverseTransformDirection(moveDirection);
        }

        private void SmoothDesiredMoveDirection()
        {
            var desiredWorldDirection = Vector3.ProjectOnPlane(
                FpcRole.FpcModule.transform.TransformDirection(Move.DesiredLocalDirection),
                Vector3.up);

            if (desiredWorldDirection.sqrMagnitude < 0.01f)
            {
                hasSmoothedMoveWorldDirection = false;
                Move.DesiredLocalDirection = Vector3.zero;
                return;
            }

            desiredWorldDirection.Normalize();

            if (!hasSmoothedMoveWorldDirection)
            {
                smoothedMoveWorldDirection = desiredWorldDirection;
                hasSmoothedMoveWorldDirection = true;
            }
            else
            {
                smoothedMoveWorldDirection = Vector3.RotateTowards(
                    smoothedMoveWorldDirection,
                    desiredWorldDirection,
                    Mathf.Deg2Rad * MaxMoveTurnDegreesPerSecond * Time.deltaTime,
                    0f).normalized;
            }

            Move.DesiredLocalDirection = FpcRole.FpcModule.transform.InverseTransformDirection(smoothedMoveWorldDirection);
        }

        private void JumpIfForwardMovementBlocked()
        {
            var desired = Move.DesiredLocalDirection;
            var intendedWorldMove = Vector3.ProjectOnPlane(
                FpcRole.FpcModule.transform.TransformDirection(desired),
                Vector3.up);

            if (intendedWorldMove.sqrMagnitude < 0.1f)
            {
                if (Time.time - lastMovementIntentTime > 0.75f)
                {
                    ResetStuckJumpTracking();
                }

                return;
            }

            lastMovementIntentTime = Time.time;

            var horizontalPosition = Vector3.ProjectOnPlane(PlayerPosition, Vector3.up);
            var horizontalAnchor = Vector3.ProjectOnPlane(stuckAnchorPosition, Vector3.up);
            var intendedDirection = intendedWorldMove.normalized;
            var displacement = horizontalPosition - horizontalAnchor;

            if (stuckAnchorTime <= 0f || displacement.sqrMagnitude > StuckMovementProgressDistanceSqr)
            {
                stuckAnchorPosition = PlayerPosition;
                stuckIntendedDirection = intendedDirection;
                stuckAnchorTime = Time.time;
                return;
            }

            stuckIntendedDirection = intendedDirection;

            if (Time.time - stuckAnchorTime < 3f || Time.time < nextStuckJumpTime)
            {
                if (Time.time - stuckAnchorTime >= 1.25f)
                {
                    LogMovementDiagnostic("low forward progress");
                }

                return;
            }

            if (Time.time - stuckAnchorTime >= StuckTeleportSeconds && Time.time >= nextStuckTeleportTime)
            {
                if (TryTeleportToRandomRoomInSameZone())
                {
                    LogMovementDiagnostic("blocked for 6 seconds; teleported to random same-zone room");
                    nextStuckTeleportTime = Time.time + 3f;
                    ResetStuckJumpTracking();
                    ResetNonCombatMoveGoal();
                    return;
                }
            }

            LogMovementDiagnostic("blocked for 3 seconds; forcing jump");
            FpcRole.FpcModule.Motor.JumpController.ForceJump(FpcRole.FpcModule.JumpSpeed);
            nextStuckJumpTime = Time.time + 1f;
            stuckIntendedDirection = intendedDirection;
        }

        private void ResetStuckJumpTracking()
        {
            stuckAnchorPosition = PlayerPosition;
            stuckIntendedDirection = Vector3.zero;
            stuckAnchorTime = 0f;
            hasSmoothedMoveWorldDirection = false;
        }

        private bool TryTeleportToRandomRoomInSameZone()
        {
            if (!RoomUtils.TryGetRoom(PlayerPosition, out var currentRoom))
            {
                return TryTeleportToRecoveryRoom();
            }

            var candidates = LabApi.Features.Wrappers.Room.List
                .Where(room => room != null
                               && !room.IsDestroyed
                               && room.Zone == currentRoom.Zone
                               && room.Name != currentRoom.Name)
                .ToArray();

            if (candidates.Length == 0)
            {
                candidates = LabApi.Features.Wrappers.Room.List
                    .Where(room => room != null
                                   && !room.IsDestroyed
                                   && room.Zone == currentRoom.Zone)
                    .ToArray();
            }

            if (candidates.Length == 0)
            {
                return false;
            }

            var selected = candidates[UnityEngine.Random.Range(0, candidates.Length)];
            BotHub.PlayerHub.transform.position = selected.Position + Vector3.up * 1.2f;
            return true;
        }

        private bool RecoverIfOutOfWorld()
        {
            if (PlayerPosition.y > OutOfWorldY)
            {
                return false;
            }

            if (!TryTeleportToRecoveryRoom())
            {
                return false;
            }

            LogMovementDiagnostic("out of world; teleported to recovery room");
            ResetStuckJumpTracking();
            ResetNonCombatMoveGoal();
            return true;
        }

        private void UpdateLastSafePosition()
        {
            if (!RoomUtils.TryGetRoom(PlayerPosition, out var room) || PlayerPosition.y <= OutOfWorldY)
            {
                return;
            }

            lastSafePosition = PlayerPosition;
            lastSafeZone = room.Zone;
            hasLastSafePosition = true;
        }

        private bool TryTeleportToRecoveryRoom()
        {
            if (WarmupManager.Instance.TryGetBotArena(BotHub.PlayerHub, out var arena)
                && TryTeleportToRandomArenaRoom(arena))
            {
                return true;
            }

            if (hasLastSafePosition)
            {
                var candidates = LabApi.Features.Wrappers.Room.List
                    .Where(room => room != null && !room.IsDestroyed && room.Zone == lastSafeZone)
                    .ToArray();

                if (candidates.Length > 0)
                {
                    var selected = candidates[UnityEngine.Random.Range(0, candidates.Length)];
                    BotHub.PlayerHub.transform.position = selected.Position + Vector3.up * 1.2f;
                    return true;
                }

                BotHub.PlayerHub.transform.position = lastSafePosition + Vector3.up * 0.5f;
                return true;
            }

            var fallbackRooms = LabApi.Features.Wrappers.Room.List
                .Where(room => room != null && !room.IsDestroyed)
                .ToArray();
            if (fallbackRooms.Length == 0)
            {
                return false;
            }

            var fallback = fallbackRooms[UnityEngine.Random.Range(0, fallbackRooms.Length)];
            BotHub.PlayerHub.transform.position = fallback.Position + Vector3.up * 1.2f;
            return true;
        }

        private bool TryTeleportToRandomArenaRoom(WarmupArena arena)
        {
            var candidates = LabApi.Features.Wrappers.Room.List
                .Where(room => room != null
                               && !room.IsDestroyed
                               && WarmupManager.IsArenaZone(arena, room.Zone))
                .ToArray();
            if (candidates.Length == 0)
            {
                return false;
            }

            var selected = candidates[UnityEngine.Random.Range(0, candidates.Length)];
            BotHub.PlayerHub.transform.position = selected.Position + Vector3.up * 1.2f;
            return true;
        }

        private void LogMovementDiagnostic(string reason)
        {
            if (LabApiPlugin.Instance?.Config?.EnableVerboseBotLogs != true || Time.time < nextMovementDiagnosticLogTime)
            {
                return;
            }

            nextMovementDiagnosticLogTime = Time.time + 2f;
            var roomName = RoomUtils.TryGetRoom(PlayerPosition, out var room) ? room.Name.ToString() : "unknown";
            var role = BotHub.PlayerHub.roleManager.CurrentRole.RoleTypeId;
            var combatTarget = Combat.DebugCurrentTarget;
            var combatTargetText = combatTarget != null
                ? $"{combatTarget.roleManager.CurrentRole.RoleTypeId}@{FormatVector(combatTarget.transform.position)}"
                : "none";
            Debug.Log(
                $"[SCPSLBot] Movement diag: {reason}; id={BotHub.PlayerHub.PlayerId} role={role} room={roomName} pos={FormatVector(PlayerPosition)} navGoal={FormatVector(Navigator.GoalPosition)} waypoint={FormatVector(Navigator.DebugPositionTowardsGoal)} roamTarget={FormatVector(ZoneRoam.DebugTargetPosition)} combatTarget={combatTargetText} action={MindRunner.RunningAction?.GetType().Name ?? "none"} desired={FormatVector(Move.DesiredLocalDirection)}.");
        }

        private static string FormatVector(Vector3? value)
        {
            return value.HasValue ? FormatVector(value.Value) : "none";
        }

        private static string FormatVector(Vector3 value)
        {
            return $"({value.x:F1},{value.y:F1},{value.z:F1})";
        }

        #endregion

        public void LookToPosition(Vector3 targetPosition)
        {
            LookToPosition(targetPosition, 1f);
        }

        public void LookToPosition(Vector3 targetPosition, float trackingStrength)
        {
            var prevHorizontalRotation = Look.TargetHorizontalRotation;

            Look.ToPosition(targetPosition, trackingStrength);

            Move.DesiredLocalDirection = prevHorizontalRotation * Move.DesiredLocalDirection;
        }

        #region Interaction

        public bool Interact(InteractableCollider interactableCollider)
        {
            if (interactableCollider.Target is not IServerInteractable interactable)
            {
                throw new InvalidOperationException("interactableCollider target is not server interactable.");
            }

            var hub = BotHub.PlayerHub;
            var playerCamera = hub.PlayerCameraReference;

            var isHit = interactableCollider
                .GetComponent<Collider>()
                .Raycast(new Ray(playerCamera.position, hub.PlayerCameraReference.forward), out var hit, 2f);

            if (isHit && hit.collider.GetComponent<InteractableCollider>() == interactableCollider)
            {
                interactable.ServerInteract(hub, interactableCollider.ColliderId);

                //Log.Debug($"ServerInteract(...) called on {interactable}");

                return true;
            }

            return false;
        }

        public bool OpenDoor(DoorVariant targetDoor, float maxInteractDistance)
        {
            var hub = BotHub.PlayerHub;
            var playerCamera = hub.PlayerCameraReference;

            //if (firstDoorOnPath.GetComponentsInChildren<Collider>()
            //        .Any(collider => collider.Raycast(new Ray(playerPosition, hub.PlayerCameraReference.forward), out var hit, 2f))
            if (Physics.Raycast(playerCamera.position, playerCamera.forward, out var hit, maxInteractDistance, LayerMask.GetMask("Door", "Glass"))
                && hit.collider.GetComponent<InteractableCollider>() is InteractableCollider interactableCollider
                && interactableCollider.Target is DoorVariant interactable)
            {
                var colliderId = interactableCollider.ColliderId;

                interactable.ServerInteract(hub, colliderId);
                //Log.Debug($"ServerInteract(...) called on {interactable}");

                return true;
            }

            return false;
        }

        public bool InteractDoorDirectly(DoorVariant targetDoor, float maxInteractDistance)
        {
            if (targetDoor == null)
            {
                return false;
            }

            var hub = BotHub.PlayerHub;
            var playerPosition = hub.PlayerCameraReference.position;

            foreach (var interactableCollider in targetDoor.GetComponentsInChildren<InteractableCollider>())
            {
                var collider = interactableCollider.GetComponent<Collider>();
                if (collider == null)
                {
                    continue;
                }

                var closest = collider.ClosestPoint(playerPosition);
                if (Vector3.Distance(playerPosition, closest) > maxInteractDistance)
                {
                    continue;
                }

                if (interactableCollider.Target is DoorVariant interactable)
                {
                    interactable.ServerInteract(hub, interactableCollider.ColliderId);
                    return true;
                }
            }

            return false;
        }

        public bool OpenLockerDoor(LockerChamber targetDoor, float maxInteractDistance)
        {
            var hub = BotHub.PlayerHub;
            var playerCamera = hub.PlayerCameraReference;

            var (isHit, hit) = targetDoor.GetComponentsInChildren<InteractableCollider>()
                    .Select(interactableCollider => interactableCollider.GetComponent<Collider>())
                    .Select(collider => (isHit: collider.Raycast(new Ray(playerCamera.position, hub.PlayerCameraReference.forward), out var hit, maxInteractDistance), hit))
                    .FirstOrDefault(t => t.isHit);

            if (isHit
                && hit.collider.GetComponent<InteractableCollider>() is InteractableCollider interactableCollider
                && hit.collider.GetComponentInParent<IServerInteractable>() is IServerInteractable interactable)

            //if (Physics.Raycast(playerCamera.position, playerCamera.forward, out var hit, maxInteractDistance)
            //    && hit.collider.GetComponent<InteractableCollider>() is InteractableCollider interactableCollider
            //    && hit.collider.GetComponentInParent<IServerInteractable>() is IServerInteractable interactable)
            {
                var colliderId = interactableCollider.ColliderId;

                interactable.ServerInteract(hub, colliderId);
                //Log.Debug($"ServerInteract(...) called on {interactable}");

                return true;
            }

            return false;
        }

        #endregion

        #region Debug functions

        private readonly StringBuilder debugStringBuilder = new();
        private int numLines;
        private int level;

        private void DisplayVisitedActionsGraph()
        {
            debugStringBuilder.Clear();
            debugStringBuilder.AppendLine("<size=14><align=left>");
            numLines = 0;

            foreach (var (goal, goalEnablingBeliefs) in MindRunner.BeliefsEnablingGoals)
            {
                level = 0;
                debugStringBuilder.AppendLine($"Goal: {goal.GetType().Name}");
                numLines++;

                foreach (var goalBelief in goalEnablingBeliefs)
                {
                    if (!MindRunner.VisitedGoalsEnabledBy.ContainsKey(goalBelief))
                    {
                        continue;
                    }

                    ShowVisitedGoalBelief(goalBelief, goal);
                }
            }

            debugStringBuilder.Append('\n', Mathf.Max(40 - numLines, 0));

            var debugString = debugStringBuilder.ToString();

            SendTextHintToSpectators(debugString, 10);
        }

        private void ShowVisitedGoalBelief(IBelief goalBelief, IGoal goal)
        {
            level++;

            foreach (var actionImpacting in MindRunner.ActionsImpactingBeliefs[goalBelief])
            {
                if (!MindRunner.VisitedGoalsImpactedBy.TryGetValue(actionImpacting, out var goalImpactedBy)
                    || goalImpactedBy != goal)
                {
                    continue;
                }

                ShowVisitedAction(actionImpacting);
            }
        }

        private void ShowVisitedAction(IAction actionImpacting)
        {
            level++;
            debugStringBuilder.Append(' ', level*4);

            var actionTotalCost = MindRunner.VisitedActionsTotalCosts[actionImpacting];
            if (MindRunner.RelevantActionsImpactingActions.ContainsKey(actionImpacting) || actionImpacting == MindRunner.RunningAction)
            {
                debugStringBuilder.AppendLine($"<color=yellow>{actionImpacting}</color> <b>[{actionTotalCost}]</b>");
            }
            else
            {
                debugStringBuilder.AppendLine($"{actionImpacting} <b>[{actionTotalCost}]</b>");
            }
            numLines++;

            foreach (var beliefEnabling in MindRunner.BeliefsEnablingActions[actionImpacting])
            {
                ShowVisitedActionsOfBelief(beliefEnabling, actionImpacting);
            }

            level--;
        }

        private void ShowVisitedActionsOfBelief(IBelief belief, IAction actionToEnable)
        {
            foreach (var actionImpacting in MindRunner.ActionsImpactingBeliefs[belief])
            {
                if (!MindRunner.VisitedActionsImpactedBy.TryGetValue(actionImpacting, out var actionImpactedBy)
                    || actionImpactedBy != actionToEnable)
                {
                    continue;
                }

                ShowVisitedAction(actionImpacting);
            }
        }

        string broadcastMessage = string.Empty;

        public void SendBroadcastToSpectators(string message, ushort duration)
        {
            if (broadcastMessage != message)
            {
                broadcastMessage = message;

                var spectatingPlayers = ReferenceHub.AllHubs.Where(p => p.roleManager.CurrentRole is OverwatchRole s && s.SyncedSpectatedNetId == this.BotHub.PlayerHub.netId);
                foreach (var spectatingPlayer in spectatingPlayers)
                {
                    Broadcast.Singleton.TargetClearElements(spectatingPlayer.connectionToClient);
                    Broadcast.Singleton.TargetAddElement(spectatingPlayer.connectionToClient, message, duration, Broadcast.BroadcastFlags.Normal);
                }
            }
        }

        private IEnumerable<ReferenceHub> spectators;
        public IEnumerable<ReferenceHub> Spectators
        {
            get
            {
                spectators ??= ReferenceHub.AllHubs.Where(p => p.roleManager.CurrentRole is OverwatchRole s && s.SyncedSpectatedNetId == this.BotHub.PlayerHub.netId);
                return spectators;
            }
        }

        private static readonly Dictionary<ReferenceHub, string> playersHintTexts = new();

        public void SendTextHintToSpectators(string message, float duration)
        {
            var spectatingPlayers = Spectators;
            foreach (var spectatingHub in spectatingPlayers)
            {
                if (!playersHintTexts.TryGetValue(spectatingHub, out var prevHintText))
                {
                    prevHintText = string.Empty;
                    playersHintTexts.Add(spectatingHub, prevHintText);
                }
    
                if (prevHintText == message)
                {
                    continue;
                }

                spectatingHub.hints.Show(new TextHint(message, new [] { new StringHintParameter(string.Empty) }, null, duration));

                playersHintTexts[spectatingHub] = message;
            }
        }

        #endregion
    }
}
