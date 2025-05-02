using Interactables.Interobjects;
using Mirror;
using SCPSLBot.AI.FirstPersonControl.Mind.Door;
using System;
using System.Linq;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Elevation
{
    internal class CallAndWaitForElevator : IAction
    {
        private readonly FpcBotPlayer botPlayer;
        private const float interactDistance = 2f;

        public CallAndWaitForElevator(FpcBotPlayer botPlayer)
        {
            this.botPlayer = botPlayer;
        }

        private DoorObstacle doorObstacle;

        public void SetEnabledByBeliefs(FpcMind fpcMind)
        {
        }

        public void SetImpactsBeliefs(FpcMind fpcMind)
        {
            doorObstacle = fpcMind.ActionImpacts<DoorObstacle, DoorEntry?>(this, c => c!.Value.Door is ElevatorDoor);
        }

        public float Cost => 10f;

        public void Tick()
        {
            var elevatorDoor = doorObstacle.GetLastDoor<ElevatorDoor>(out var goalPos);
            var isTargetStateOpen = elevatorDoor.TargetState;
            var panel = elevatorDoor.GetComponentInChildren<ElevatorPanel>();
            var chamber = elevatorDoor.Chamber;

            if (isTargetStateOpen || chamber.DestinationDoor == elevatorDoor)
            {
                // waiting
                return;
            }

            var playerPosition = botPlayer.BotHub.PlayerHub.transform.position;
            var panelPosition = panel.GetComponent<Collider>().bounds.center;

            var dist = Vector3.Distance(panelPosition, playerPosition);
            if (dist > interactDistance)
            {
                botPlayer.MoveToPosition(goalPos);
            }

            var directionToPanel = Vector3.Normalize(panelPosition - playerPosition);
            var playerDirection = botPlayer.BotHub.PlayerHub.transform.forward;
            if (Vector3.Dot(playerDirection, directionToPanel) < .989f)
            {
                botPlayer.LookToPosition(panelPosition);
                return;
            }

            var groupElevatorDoors = ElevatorDoor.GetDoorsForGroup(chamber.AssignedGroup);
            if (!groupElevatorDoors.Any())
            {
                Debug.LogWarning($"Elevator chamber group not added to all elevator doors");
                return;
            }

            var targetLevel = groupElevatorDoors.IndexOf(elevatorDoor);

            chamber.ServerSetDestination(targetLevel, true);
        }

        public void Reset()
        {
        }

        public override string ToString()
        {
            return $"{nameof(CallAndWaitForElevator)}";
        }
    }
}
