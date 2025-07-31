using SCPSLBot.Navigation.Mesh;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Navigation
{
    internal class GoToCell(TransformCell toCell, TransformCell fromCell, TransformEdge toEdge, TransformEdge fromEdge, FpcBotPlayer botPlayer) : IAction
    {
        private readonly Vector3 toEdgePos = toEdge.MiddlePosition;
        private readonly Vector3 fromEdgePos = fromEdge.MiddlePosition;
        private readonly NavigationBeliefs cellBeliefs = botPlayer.MindRunner.GetBelief<NavigationBeliefs>();
        private NavigationCell navCellTo;
        private NavigationCell navCellFrom;

        public void SetEnabledByBeliefs(FpcMind fpcMind)
        {
            if (cellBeliefs.Obstacles.TryGetValue(fromCell, out var obstacleBelief))
            {
                fpcMind.ActionEnabledBy(this, obstacleBelief, b => !b.HasHit(toEdgePos, fromEdgePos));
            }

            this.navCellFrom = fpcMind.ActionEnabledBy(this, cellBeliefs.NavigationCells[fromCell], c => c.IsWithin);            
        }

        public void SetImpactsBeliefs(FpcMind fpcMind)
        {
            this.navCellTo = fpcMind.ActionImpacts(this, cellBeliefs.NavigationCells[toCell]);
        }

        public float Cost => Vector3.Distance(toEdgePos, navCellFrom.IsWithin ? botPlayer.PlayerPosition : fromEdgePos);

        public void Tick(FpcMatchProvider matchProvider)
        {
            botPlayer.MoveToPosition(toEdgePos);
        }

        public void Reset()
        {
        }

        public override string ToString()
        {
            return $"{nameof(GoToCell)}({toCell}, {fromCell}, {fromEdge})";
        }
    }
}
