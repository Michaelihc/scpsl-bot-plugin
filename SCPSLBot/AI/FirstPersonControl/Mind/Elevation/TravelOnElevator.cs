using Interactables.Interobjects;
using SCPSLBot.AI.FirstPersonControl.Mind.Navigation;
using SCPSLBot.Navigation.Mesh;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Elevation
{
    internal class TravelOnElevator(TransformCell toCell, TransformCell fromCell, FpcBotPlayer botPlayer) : IAction
    {
        private readonly Vector3 toCellMeanPosition = toCell.MeanPosition;
        private readonly Vector3 fromCellMeanPosition = fromCell.MeanPosition;
        private readonly NavigationBeliefs cellBeliefs = botPlayer.MindRunner.GetBelief<NavigationBeliefs>();
        private CellWithin cellWithin;
        private NavigationCell navCellFrom;
        private NavigationCell navCellTo;
        private ElevatorLevel elevatorLevelFrom;

        public void SetEnabledByBeliefs(FpcMind fpcMind)
        {
            this.cellWithin = fpcMind.ActionEnabledBy<CellWithin>(this, b => b.TransformCell.HasValue);

            this.elevatorLevelFrom = fpcMind.ActionEnabledBy<ElevatorLevel>(this, cellBeliefs.ElevatorLevels[fromCell], e => cellWithin.TransformCell!.Value == fromCell || e.IsElevatorAt);

            this.navCellFrom = fpcMind.ActionEnabledBy(this, cellBeliefs.NavigationCells[fromCell], c => c.Is(cellWithin.TransformCell!.Value));
        }

        public void SetImpactsBeliefs(FpcMind fpcMind)
        {
            this.navCellTo = fpcMind.ActionImpacts(this, cellBeliefs.NavigationCells[toCell], b => !b.IsWithin);
        }

        public float Cost => Vector3.Distance(toCellMeanPosition, fromCellMeanPosition) / 40f;
        //public float HeuristicCost => 0f;

        public void Tick()
        {
            var elevatorChamber = elevatorLevelFrom.ChamberAtLevel;
            if (elevatorChamber is null)
            {
                return;
            }

            var playerPosition = botPlayer.PlayerPosition;
            var elevatorMiddle = elevatorChamber.WorldspaceBounds.center with { y = playerPosition.y };

            if (Vector3.Distance(playerPosition, elevatorMiddle) > 0.1f)
            {
                botPlayer.MoveToPosition(elevatorMiddle);
                return;
            }

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
            return $"{nameof(TravelOnElevator)}({toCell}, {fromCell})";
        }
    }
}
