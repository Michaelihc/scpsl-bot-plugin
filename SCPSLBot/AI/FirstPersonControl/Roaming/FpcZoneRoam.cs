using Interactables.Interobjects.DoorUtils;
using MapGeneration;
using SCPSLBot.AI.FirstPersonControl.Mind.Door;
using SCPSLBot.AI.FirstPersonControl.Perception.Senses;
using SCPSLBot.Navigation.Mesh;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl.Roaming
{
    internal sealed class FpcZoneRoam
    {
        private const float TargetReachedDistance = 1.75f;
        private const float DoorInteractDistance = 2f;

        private readonly FpcBotPlayer botPlayer;
        private readonly System.Random random = new();

        private Vector3? targetPosition;
        private FacilityZone? targetZone;

        public FpcZoneRoam(FpcBotPlayer botPlayer)
        {
            this.botPlayer = botPlayer;
        }

        public void Tick()
        {
            var roomSightSense = botPlayer.Perception.GetSense<RoomSightSense>();
            var roomWithin = roomSightSense.RoomWithin;
            if (!roomWithin)
            {
                return;
            }

            if (ShouldPickTarget(roomWithin))
            {
                PickTarget(roomSightSense, roomWithin);
            }

            if (!targetPosition.HasValue)
            {
                return;
            }

            botPlayer.MoveToPosition(targetPosition.Value);
            OpenBlockingNonKeycardDoor();
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
            var candidates = GetSameZoneForeignCells(roomSightSense, roomWithin).ToList();
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

        private static IEnumerable<TransformCell> GetSameZoneForeignCells(RoomSightSense roomSightSense, RoomIdentifier roomWithin)
        {
            return roomSightSense.ForeignRoomsCells
                .Where(cell => cell.Transform.GetComponent<RoomIdentifier>() is RoomIdentifier room
                               && room.Zone == roomWithin.Zone
                               && (room.Name == RoomName.Unnamed || room.Name != roomWithin.Name));
        }

        private void OpenBlockingNonKeycardDoor()
        {
            var doorObstacle = botPlayer.MindRunner.GetBelief<DoorObstacle>();
            if (!doorObstacle.IsAny || !doorObstacle.Doors.Values.Any(entry => entry.IsInteractable(DoorPermissionFlags.None)))
            {
                return;
            }

            var doorToOpen = doorObstacle.GetLastDoor(DoorPermissionFlags.None, out var goalPos);
            if (!doorToOpen)
            {
                return;
            }

            var doorPlane = new Plane(doorToOpen.transform.forward, doorToOpen.transform.position);
            var distance = Mathf.Abs(doorPlane.GetDistanceToPoint(botPlayer.PlayerPosition));

            if (!doorToOpen.TargetState && distance <= DoorInteractDistance)
            {
                if (!botPlayer.OpenDoor(doorToOpen, DoorInteractDistance))
                {
                    botPlayer.LookToPosition(doorToOpen.transform.position + Vector3.up);
                }
            }

            if (!doorToOpen.TargetState || distance > DoorInteractDistance)
            {
                botPlayer.MoveToPosition(goalPos);
            }
        }
    }
}
