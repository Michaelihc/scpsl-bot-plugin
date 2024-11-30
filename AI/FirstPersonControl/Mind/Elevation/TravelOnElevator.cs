using Interactables.Interobjects;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Elevation
{
    internal class TravelOnElevator : IAction
    {
        private readonly FpcBotPlayer botPlayer;

        public TravelOnElevator(FpcBotPlayer botPlayer)
        {
            this.botPlayer = botPlayer;
        }

        private ElevationObstacle elevatorObstacle;

        public void SetEnabledByBeliefs(FpcMind fpcMind)
        {
        }

        public void SetImpactsBeliefs(FpcMind fpcMind)
        {
            elevatorObstacle = fpcMind.ActionImpacts<ElevationObstacle, bool>(this, b => b.Elevator);
        }

        public float Cost { get; } = 10f;

        public void Tick()
        {
            var playerPosition = botPlayer.PlayerPosition;
            var elevatorMiddle = elevatorObstacle.Elevator.WorldspaceBounds.center with { y = playerPosition.y };

            if (Vector3.Distance(playerPosition, elevatorMiddle) > 0.1f)
            {
                botPlayer.MoveToPosition(elevatorMiddle);
                return;
            }

            var elevatorChamber = elevatorObstacle.Elevator;
            var panelPosition = elevatorChamber.GetComponentInChildren<ElevatorPanel>().GetComponent<Collider>().bounds.center;

            var directionToPanel = Vector3.Normalize(panelPosition - playerPosition);
            var playerDirection = botPlayer.BotHub.PlayerHub.transform.forward;
            if (Vector3.Dot(playerDirection, directionToPanel) < .989f)
            {
                botPlayer.LookToPosition(panelPosition);
                return;
            }

            if (!elevatorChamber.IsReady)
            {
                return;
            }

            var targetLvl = elevatorChamber.NextLevel;

            elevatorChamber.ServerSetDestination(targetLvl, true);
        }

        public void Reset()
        {
        }

        public override string ToString()
        {
            return $"{nameof(TravelOnElevator)}";
        }
    }
}
