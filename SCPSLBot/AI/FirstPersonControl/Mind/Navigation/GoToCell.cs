using MapGeneration;
using SCPSLBot.AI.FirstPersonControl.Mind.Room.Beliefs;
using SCPSLBot.Navigation.Mesh;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Navigation
{
    internal class GoToCell(TransformCell toCell, TransformCell fromCell, TransformEdge toEdge, IEnumerable<TransformEdge> fromEdges, FacilityZone fromZone, FpcBotPlayer botPlayer) : IAction
    {
        public TransformEdge ToEdge { get; } = toEdge;

        private readonly Vector3 toCellMeanPosition = toCell.MeanPosition;
        private readonly Vector3 fromCellMeanPosition = fromCell.MeanPosition;
        private readonly Vector3 toEdgePos = toEdge.MiddlePosition;
        private CellWithin cellWithin;
        private NavigationCell navCellTo;
        private NavigationCell navCellFrom;

        public void SetEnabledByBeliefs(FpcMind fpcMind)
        {
            var cellBeliefs = fpcMind.GetBelief<NavigationBeliefs>();
            if (cellBeliefs.Obstacles.TryGetValue(fromCell, out var obstacleBelief))
            {
                fpcMind.ActionEnabledBy(this, obstacleBelief, b => true);
            }

            this.cellWithin = fpcMind.ActionEnabledBy<CellWithin>(this, b => b.TransformCell.HasValue);
            this.navCellFrom = fpcMind.ActionEnabledBy(this, cellBeliefs.NavigationCells[fromCell], c => c.Is(cellWithin.TransformCell!.Value));            
        }

        public void SetImpactsBeliefs(FpcMind fpcMind)
        {
            var cellBeliefs = fpcMind.GetBelief<NavigationBeliefs>();
            this.navCellTo = fpcMind.ActionImpacts(this, cellBeliefs.NavigationCells[toCell]);
        }

        public float Cost => Vector3.Distance(toCellMeanPosition, navCellFrom.IsWithin ? botPlayer.PlayerPosition : fromCellMeanPosition);
        public float HeuristicCost => navCellFrom.IsWithin ? 0f : DistanceToPlayerOrEntryPoint;

        public void Tick()
        {
            var cellToPosition = botPlayer.MindRunner.GetNextCorner(this, botPlayer.PlayerPosition);
            botPlayer.MoveToPosition(cellToPosition);
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
