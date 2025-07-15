using SCPSLBot.Navigation.Mesh;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Navigation
{
    internal class GoToCell(TransformCell toCell, TransformCell fromCell, TransformEdge fromEdge, FpcBotPlayer botPlayer) : IAction
    {
        private readonly Vector3 toEdgePos = fromCell.AdjacentCellEdges[toCell].MiddlePosition;
        private readonly Vector3 fromEdgePos = fromEdge.MiddlePosition; 
        private readonly NavigationBeliefs cellBeliefs = botPlayer.MindRunner.GetBelief<NavigationBeliefs>();
        private NavigationCell targetCell;
        private NavigationCell navCellFrom;

        public void SetEnabledByBeliefs(FpcMind fpcMind)
        {
            fpcMind.ActionEnabledBy(this, cellBeliefs.Obstacles[fromCell], b => b.HasHit(toEdgePos, fromEdgePos));

            this.navCellFrom = fpcMind.ActionEnabledBy(this, cellBeliefs.NavigationCells[fromCell], c => c.IsWithin);            
        }

        public void SetImpactsBeliefs(FpcMind fpcMind)
        {
            this.targetCell = fpcMind.ActionImpacts(this, cellBeliefs.NavigationCells[toCell]);
        }

        public float Cost => Vector3.Distance(toEdgePos, navCellFrom.IsWithin ? botPlayer.PlayerPosition : fromEdgePos);

        public void Tick(FpcMatchProvider matchProvider)
        {
            botPlayer.MoveToPosition(toEdgePos);
        }

        public void Reset()
        {
        }
    }
}
