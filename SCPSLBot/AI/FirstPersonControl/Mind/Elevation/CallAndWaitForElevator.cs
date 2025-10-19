using Interactables.Interobjects;
using SCPSLBot.AI.FirstPersonControl.Mind.Navigation;
using SCPSLBot.Navigation.Mesh;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Elevation
{
    internal class CallAndWaitForElevator(TransformCell levelCell, FpcBotPlayer botPlayer) : IAction
    {
        public ElevatorLevel Level => elevationLevel;

        private const float interactDistance = 2f;
        private ElevatorLevel elevationLevel;

        public void SetEnabledByBeliefs(FpcMind fpcMind)
        {
            var navBeliefs = fpcMind.ActionEnabledBy<NavigationBeliefs>(this, b => true);
            fpcMind.ActionEnabledBy<Obstacle>(this, () => navBeliefs.GetReceivedObstacle(elevationLevel.PanelPosition), b => !(b?.HitResult.HasValue ?? false));

            var cellWithin = fpcMind.ActionEnabledBy<CellWithin>(this, b => b.TransformCell.HasValue);
            fpcMind.ActionEnabledBy<NavigationCell>(this, () => navBeliefs.GetNavigationCellWithin(elevationLevel.PanelPosition + elevationLevel.PanelUp), b => b?.Is(cellWithin.TransformCell!.Value) ?? false);
        }

        public void SetImpactsBeliefs(FpcMind fpcMind)
        {
            var navBeliefs = fpcMind.GetBelief<NavigationBeliefs>();
            elevationLevel = fpcMind.ActionImpacts<ElevatorLevel>(this, navBeliefs.ElevatorLevels[levelCell]);
        }

        public float Cost => 5f;

        public void Tick()
        {
            var panelPosition = elevationLevel.PanelPosition;
            var playerPosition = botPlayer.BotHub.PlayerHub.transform.position;

            var dist = Vector3.Distance(panelPosition, playerPosition);
            if (dist > interactDistance)
            {
                botPlayer.MoveToPosition(panelPosition);

                var directionToPanel = Vector3.Normalize(panelPosition - playerPosition);
                var playerDirection = botPlayer.BotHub.PlayerHub.transform.forward;
                if (Vector3.Dot(playerDirection, directionToPanel) < .989f)
                {
                    botPlayer.LookToPosition(panelPosition);
                }

                return;
            }

            var panel = elevationLevel.HitPanel;
            if (panel is null)
            {
                return;
            }

            if (panel.Target is not ElevatorDoor elevatorDoor)
            {
                return;
            }

            elevatorDoor.ServerInteract(botPlayer.BotHub.PlayerHub, panel.ColliderId);
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
