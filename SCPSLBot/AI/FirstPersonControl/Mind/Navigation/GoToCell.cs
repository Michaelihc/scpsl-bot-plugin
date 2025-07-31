using SCPSLBot.Navigation.Mesh;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Navigation
{
    internal class GoToCell(TransformCell toCell, TransformCell fromCell, TransformEdge toEdge, IEnumerable<TransformEdge> fromEdges, FpcBotPlayer botPlayer) : IAction
    {
        private readonly Vector3 toCellCenterPosition = toCell.CenterPosition;
        private readonly Vector3 fromCellCenterPosition = fromCell.CenterPosition;
        private readonly Vector3 toEdgePos = toEdge.MiddlePosition;
        private readonly NavigationBeliefs cellBeliefs = botPlayer.MindRunner.GetBelief<NavigationBeliefs>();
        private NavigationCell navCellTo;
        private NavigationCell navCellFrom;

        public void SetEnabledByBeliefs(FpcMind fpcMind)
        {
            if (cellBeliefs.Obstacles.TryGetValue(fromCell, out var obstacleBelief))
            {
                fpcMind.ActionEnabledBy(this, obstacleBelief, b => !fromEdges.Any(e => b.HasHit(toEdgePos, e.MiddlePosition)));
            }

            this.navCellFrom = fpcMind.ActionEnabledBy(this, cellBeliefs.NavigationCells[fromCell], c => c.IsWithin);            
        }

        public void SetImpactsBeliefs(FpcMind fpcMind)
        {
            this.navCellTo = fpcMind.ActionImpacts(this, cellBeliefs.NavigationCells[toCell]);
        }

        public float Cost => Vector3.Distance(toCellCenterPosition, navCellFrom.IsWithin ? botPlayer.PlayerPosition : fromCellCenterPosition);

        public void Tick(FpcMatchProvider matchProvider)
        {
            botPlayer.MoveToPosition(toEdgePos);
        }

        public void Reset()
        {
        }

        public override string ToString()
        {
            return $"{nameof(GoToCell)}({toCell}, {fromCell})";
        }
    }
}
