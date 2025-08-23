using SCPSLBot.Navigation.Mesh;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Navigation
{
    internal class GoToCell(TransformCell toCell, TransformCell fromCell, TransformEdge toEdge, IEnumerable<TransformEdge> fromEdges, FpcBotPlayer botPlayer) : IAction
    {
        private readonly Vector3 toCellMeanPosition = toCell.MeanPosition;
        private readonly Vector3 fromCellMeanPosition = fromCell.MeanPosition;
        private readonly Vector3 toEdgePos = toEdge.MiddlePosition;
        private readonly NavigationBeliefs cellBeliefs = botPlayer.MindRunner.GetBelief<NavigationBeliefs>();
        private CellWithin cellWithin;
        private NavigationCell navCellTo;
        private NavigationCell navCellFrom;

        public void SetEnabledByBeliefs(FpcMind fpcMind)
        {
            if (cellBeliefs.Obstacles.TryGetValue(fromCell, out var obstacleBelief))
            {
                fpcMind.ActionEnabledBy(this, obstacleBelief, b => !fromEdges.Any(e => b.HasHit(toEdgePos, e.MiddlePosition)));
            }

            this.cellWithin = fpcMind.ActionEnabledBy<CellWithin>(this, b => b.TransformCell.HasValue);
            this.navCellFrom = fpcMind.ActionEnabledBy(this, cellBeliefs.NavigationCells[fromCell], c => c.Is(cellWithin.TransformCell!.Value));            
        }

        public void SetImpactsBeliefs(FpcMind fpcMind)
        {
            this.navCellTo = fpcMind.ActionImpacts(this, cellBeliefs.NavigationCells[toCell]);
        }

        public float Cost => Vector3.Distance(toCellMeanPosition, navCellFrom.IsWithin ? botPlayer.PlayerPosition : fromCellMeanPosition);
        public float HeuristicCost => navCellFrom.IsWithin ? 0f : DistanceToPlayerOrEntryPoint;

        public void Tick()
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

        private float DistanceToPlayerOrEntryPoint
        {
            get
            {
                var levelPlayerAt = Mathf.FloorToInt(botPlayer.PlayerPosition.y / NavigationMesh.LevelScale);
                var levelFromCellAt = Mathf.FloorToInt(fromCellMeanPosition.y / NavigationMesh.LevelScale);

                if (Mathf.Abs(levelPlayerAt - levelFromCellAt) <= NavigationMesh.NumLevels)
                {
                    return Vector3.Distance(fromCellMeanPosition, botPlayer.PlayerPosition);
                }

                if (!NavigationMesh.ExitEntryCellsByLevelFrom.TryGetValue(levelPlayerAt, out var entryCellsByDestLevel)
                    || !entryCellsByDestLevel.TryGetValue(levelFromCellAt, out var entryCells))
                {
                    return float.MaxValue;
                }

                var closestDistSqr = float.MaxValue;
                var closestCellsResult = new (TransformCell Exit, TransformCell Enter)?();
                foreach (var (exitCell, entryCell) in entryCells)
                {
                    var distSqr = Vector3.SqrMagnitude(fromCellMeanPosition - entryCell.MeanPosition);
                    if (distSqr < closestDistSqr)
                    {
                        closestDistSqr = distSqr;
                        closestCellsResult = (exitCell, entryCell);
                    }
                }

                if (!closestCellsResult.HasValue)
                {
                    return float.MaxValue;
                }

                var closestCells = closestCellsResult.Value;
                var distExitPlayer = Vector3.Magnitude(closestCells.Exit.MeanPosition - botPlayer.PlayerPosition);
                return Mathf.Sqrt(closestDistSqr) + distExitPlayer;
            }
        }
    }
}
