using Interactables.Interobjects.DoorUtils;
using MapGeneration;
using SCPSLBot.AI.FirstPersonControl.Mind.Door;
using SCPSLBot.AI.FirstPersonControl.Perception.Senses;
using SCPSLBot.Navigation.Mesh;
using SCPSLBot.Warmup;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl.Roaming
{
    internal sealed class FpcZoneRoam
    {
        private const float TargetReachedDistance = 1.75f;
        private const float SameRoomTargetMinDistance = 5f;
        private const float DoorInteractDistance = 2f;

        private readonly FpcBotPlayer botPlayer;
        private readonly System.Random random = new();

        private Vector3? targetPosition;
        private FacilityZone? targetZone;
        private float nextRoamDiagnosticLogTime;

        public Vector3? DebugTargetPosition => targetPosition;

        public FpcZoneRoam(FpcBotPlayer botPlayer)
        {
            this.botPlayer = botPlayer;
        }

        public bool Tick()
        {
            var roomSightSense = botPlayer.Perception.GetSense<RoomSightSense>();
            var roomWithin = roomSightSense.RoomWithin;
            if (!roomWithin)
            {
                return TickWithoutRoom();
            }

            if (ShouldPickTarget(roomWithin))
            {
                PickTarget(roomSightSense, roomWithin);
            }

            if (!targetPosition.HasValue)
            {
                return false;
            }

            var positionTowardsGoal = botPlayer.Navigator.GetPositionTowards(targetPosition.Value);
            if (IsCurrentPathBlockedByLockedDoor())
            {
                LogRoamDiagnostic("discarding roam target behind locked door");
                targetPosition = null;
                botPlayer.Move.DesiredLocalDirection = Vector3.zero;
                return true;
            }

            if (!OpenBlockingNonKeycardDoor())
            {
                botPlayer.SteerTowardPosition(positionTowardsGoal);
            }

            return true;
        }

        private bool ShouldPickTarget(RoomIdentifier roomWithin)
        {
            if (!targetPosition.HasValue || targetZone != roomWithin.Zone)
            {
                return true;
            }

            return Vector3.Distance(botPlayer.PlayerPosition, targetPosition.Value) <= TargetReachedDistance;
        }

        private void PickTarget(RoomSightSense roomSightSense, RoomIdentifier roomWithin)
        {
            if (WarmupManager.Instance.TryGetBotArena(botPlayer.BotHub.PlayerHub, out var arena)
                && !WarmupManager.IsArenaZone(arena, roomWithin.Zone))
            {
                PickArenaTarget(arena);
                return;
            }

            var candidates = GetSameZoneForeignCells(roomSightSense, roomWithin).ToList();
            if (candidates.Count == 0)
            {
                candidates = GetSameRoomCells(roomWithin)
                    .Where(cell => Vector3.Distance(botPlayer.PlayerPosition, cell.CenterPosition) >= SameRoomTargetMinDistance)
                    .ToList();
            }

            if (candidates.Count == 0)
            {
                candidates = GetZoneCells(roomWithin.Zone)
                    .Where(cell => Vector3.Distance(botPlayer.PlayerPosition, cell.CenterPosition) >= SameRoomTargetMinDistance)
                    .ToList();
            }

            if (candidates.Count == 0)
            {
                candidates = GetAllKnownCells()
                    .Where(cell => Vector3.Distance(botPlayer.PlayerPosition, cell.CenterPosition) >= SameRoomTargetMinDistance)
                    .ToList();
            }

            if (candidates.Count == 0)
            {
                targetPosition = null;
                targetZone = roomWithin.Zone;
                return;
            }

            var selected = candidates[random.Next(candidates.Count)];
            targetPosition = selected.CenterPosition;
            targetZone = roomWithin.Zone;
        }

        private void PickArenaTarget(WarmupArena arena)
        {
            var candidates = GetArenaCells(arena)
                .Where(cell => Vector3.Distance(botPlayer.PlayerPosition, cell.CenterPosition) >= SameRoomTargetMinDistance)
                .ToList();

            if (candidates.Count == 0)
            {
                targetPosition = null;
                targetZone = null;
                return;
            }

            var selected = candidates[random.Next(candidates.Count)];
            targetPosition = selected.CenterPosition;
            targetZone = selected.Transform.GetComponent<RoomIdentifier>()?.Zone;
        }

        private bool TickWithoutRoom()
        {
            if (!targetPosition.HasValue || Vector3.Distance(botPlayer.PlayerPosition, targetPosition.Value) <= TargetReachedDistance)
            {
                PickFallbackZoneTarget();
            }

            if (targetPosition.HasValue)
            {
                botPlayer.MoveToPosition(targetPosition.Value);
                return true;
            }

            return false;
        }

        private void PickFallbackZoneTarget()
        {
            var nearestKnownZone = GetNearestKnownZone();
            if (!nearestKnownZone.HasValue)
            {
                targetPosition = null;
                targetZone = null;
                return;
            }

            var candidates = GetZoneCells(nearestKnownZone.Value)
                .Where(cell => Vector3.Distance(botPlayer.PlayerPosition, cell.CenterPosition) >= SameRoomTargetMinDistance)
                .ToList();
            if (candidates.Count == 0)
            {
                candidates = GetAllKnownCells()
                    .Where(cell => Vector3.Distance(botPlayer.PlayerPosition, cell.CenterPosition) >= SameRoomTargetMinDistance)
                    .ToList();
            }

            if (candidates.Count == 0)
            {
                targetPosition = null;
                targetZone = nearestKnownZone;
                return;
            }

            var selected = candidates[random.Next(candidates.Count)];
            targetPosition = selected.CenterPosition;
            targetZone = nearestKnownZone;
        }

        private static IEnumerable<TransformCell> GetSameZoneForeignCells(RoomSightSense roomSightSense, RoomIdentifier roomWithin)
        {
            return roomSightSense.ForeignRoomsCells
                .Where(cell => cell.Transform.GetComponent<RoomIdentifier>() is RoomIdentifier room
                               && room.Zone == roomWithin.Zone
                               && (room.Name == RoomName.Unnamed || room.Name != roomWithin.Name));
        }

        private static IEnumerable<TransformCell> GetSameRoomCells(RoomIdentifier roomWithin)
        {
            if (!roomWithin || !NavigationMesh.LocalMeshesByRoom.TryGetValue(roomWithin.gameObject, out var mesh))
            {
                yield break;
            }

            foreach (var cell in mesh.Cells)
            {
                yield return new TransformCell(cell, roomWithin.transform);
            }
        }

        private static IEnumerable<TransformCell> GetZoneCells(FacilityZone zone)
        {
            foreach (var (roomObject, mesh) in NavigationMesh.LocalMeshesByRoom)
            {
                var room = roomObject.GetComponent<RoomIdentifier>();
                if (!room || room.Zone != zone)
                {
                    continue;
                }

                foreach (var cell in mesh.Cells)
                {
                    yield return new TransformCell(cell, room.transform);
                }
            }
        }

        private static IEnumerable<TransformCell> GetArenaCells(WarmupArena arena)
        {
            foreach (var (roomObject, mesh) in NavigationMesh.LocalMeshesByRoom)
            {
                var room = roomObject.GetComponent<RoomIdentifier>();
                if (!room || !WarmupManager.IsArenaZone(arena, room.Zone))
                {
                    continue;
                }

                foreach (var cell in mesh.Cells)
                {
                    yield return new TransformCell(cell, room.transform);
                }
            }
        }

        private static IEnumerable<TransformCell> GetAllKnownCells()
        {
            foreach (var (roomObject, mesh) in NavigationMesh.LocalMeshesByRoom)
            {
                if (!roomObject.GetComponent<RoomIdentifier>())
                {
                    continue;
                }

                foreach (var cell in mesh.Cells)
                {
                    yield return new TransformCell(cell, roomObject.transform);
                }
            }
        }

        private FacilityZone? GetNearestKnownZone()
        {
            TransformCell? nearest = null;
            var nearestDistance = float.PositiveInfinity;

            foreach (var (roomObject, mesh) in NavigationMesh.LocalMeshesByRoom)
            {
                var room = roomObject.GetComponent<RoomIdentifier>();
                if (!room)
                {
                    continue;
                }

                foreach (var cell in mesh.Cells)
                {
                    var transformCell = new TransformCell(cell, room.transform);
                    var distance = Vector3.SqrMagnitude(transformCell.CenterPosition - botPlayer.PlayerPosition);
                    if (distance >= nearestDistance)
                    {
                        continue;
                    }

                    nearest = transformCell;
                    nearestDistance = distance;
                }
            }

            return nearest?.Transform.GetComponent<RoomIdentifier>()?.Zone;
        }

        private bool OpenBlockingNonKeycardDoor()
        {
            var doorObstacle = botPlayer.MindRunner.GetBelief<DoorObstacle>();
            if (!doorObstacle.IsAny || !doorObstacle.Doors.Values.Any(entry => entry.IsInteractable(DoorPermissionFlags.None)))
            {
                return false;
            }

            var doorToOpen = doorObstacle.GetLastDoor(DoorPermissionFlags.None, out var goalPos);
            if (!doorToOpen)
            {
                return false;
            }

            if (doorToOpen.ActiveLocks != 0)
            {
                return false;
            }

            var doorPlane = new Plane(doorToOpen.transform.forward, doorToOpen.transform.position);
            var distance = Mathf.Abs(doorPlane.GetDistanceToPoint(botPlayer.PlayerPosition));

            if (!doorToOpen.TargetState && distance <= DoorInteractDistance)
            {
                botPlayer.LookToPosition(doorToOpen.transform.position + Vector3.up);
                if (!botPlayer.OpenDoor(doorToOpen, DoorInteractDistance + 0.75f)
                    && !botPlayer.InteractDoorDirectly(doorToOpen, DoorInteractDistance + 0.75f))
                {
                    return true;
                }
            }

            if (!doorToOpen.TargetState || distance > DoorInteractDistance)
            {
                botPlayer.MoveToPositionImmediate(goalPos);
                return true;
            }

            return false;
        }

        private bool IsCurrentPathBlockedByLockedDoor()
        {
            var doorObstacle = botPlayer.MindRunner.GetBelief<DoorObstacle>();
            var entry = doorObstacle.GetEntry(botPlayer.Navigator.GoalPosition);
            return entry.HasValue && entry.Value.Door && entry.Value.Door.ActiveLocks != 0;
        }

        private void LogRoamDiagnostic(string reason)
        {
            if (LabApiPlugin.Instance?.Config?.EnableVerboseBotLogs != true || Time.time < nextRoamDiagnosticLogTime)
            {
                return;
            }

            nextRoamDiagnosticLogTime = Time.time + 2f;
            Debug.Log($"[SCPSLBot] Roam diag: {reason}; pos={FormatVector(botPlayer.PlayerPosition)} goal={FormatVector(targetPosition)} navGoal={FormatVector(botPlayer.Navigator.GoalPosition)} waypoint={FormatVector(botPlayer.Navigator.DebugPositionTowardsGoal)} room={(RoomUtils.TryGetRoom(botPlayer.PlayerPosition, out var room) ? room.Name.ToString() : "unknown")} zone={targetZone?.ToString() ?? "none"} role={botPlayer.BotHub.PlayerHub.roleManager.CurrentRole.RoleTypeId}.");
        }

        private static string FormatVector(Vector3? value)
        {
            return value.HasValue ? FormatVector(value.Value) : "none";
        }

        private static string FormatVector(Vector3 value)
        {
            return $"({value.x:F1},{value.y:F1},{value.z:F1})";
        }
    }
}
