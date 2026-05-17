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
        private float stuckAnchorTime;
        private float nextStuckJumpTime;

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
            var role = BotHub.PlayerHub.roleManager.CurrentRole.RoleTypeId;
            return role is RoleTypeId.ClassD or RoleTypeId.Scientist;
        }

        private bool ShouldIdleOnSurfaceWithoutTarget()
        {
            return RoomUtils.TryGetRoom(PlayerPosition, out var room) && room.Zone == FacilityZone.Surface;
        }

        public void OnRoleChanged()
        {
            Debug.Log($"Bot got FPC role assigned.");

            PerceptionComponent = BotHub.PlayerHub.GetComponentInChildren<PerceptionComponent>();
            PerceptionComponent.enabled = true;

            MindRunner.EvaluateGoalsToActions();
            ResetStuckJumpTracking();
        }

        #region Moving

        public void MoveToPosition(Vector3 goalPosition) => MoveToPosition(goalPosition, out _);
        public void MoveToPosition(Vector3 goalPosition, out Vector3 positionTowardsGoal)
        {
            positionTowardsGoal = Navigator.GetPositionTowards(goalPosition);
            SteerToPosition(positionTowardsGoal);
            SteerAwayFromObstacles();
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

        private void JumpIfForwardMovementBlocked()
        {
            var desired = Move.DesiredLocalDirection;
            var intendedWorldMove = Vector3.ProjectOnPlane(
                FpcRole.FpcModule.transform.TransformDirection(desired),
                Vector3.up);

            if (intendedWorldMove.sqrMagnitude < 0.1f)
            {
                ResetStuckJumpTracking();
                return;
            }

            var horizontalPosition = Vector3.ProjectOnPlane(PlayerPosition, Vector3.up);
            var horizontalAnchor = Vector3.ProjectOnPlane(stuckAnchorPosition, Vector3.up);
            if (stuckAnchorTime <= 0f || Vector3.Distance(horizontalPosition, horizontalAnchor) > 0.35f)
            {
                stuckAnchorPosition = PlayerPosition;
                stuckAnchorTime = Time.time;
                return;
            }

            if (Time.time - stuckAnchorTime < 3f || Time.time < nextStuckJumpTime)
            {
                return;
            }

            Debug.Log("[SCPSLBot] Bot movement blocked for 3 seconds; forcing jump.");
            FpcRole.FpcModule.Motor.JumpController.ForceJump(FpcRole.FpcModule.JumpSpeed);
            nextStuckJumpTime = Time.time + 1f;
            stuckAnchorPosition = PlayerPosition;
            stuckAnchorTime = Time.time;
        }

        private void ResetStuckJumpTracking()
        {
            stuckAnchorPosition = PlayerPosition;
            stuckAnchorTime = 0f;
        }

        #endregion

        public void LookToPosition(Vector3 targetPosition)
        {
            var prevHorizontalRotation = Look.TargetHorizontalRotation;

            Look.ToPosition(targetPosition);

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
