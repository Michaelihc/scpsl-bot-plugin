using SCPSLBot.AI.FirstPersonControl.Mind.Elevation;
using SCPSLBot.AI.FirstPersonControl.Mind.Spacial;
using SCPSLBot.Navigation.Mesh;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Navigation
{
    internal static class FpcMindRunnerExtensions
    {
        public static Vector3 GetNextCorner(this FpcMindRunner mindRunner, GoToCell currentGoToCell, Vector3 playerPosition)
        {
            IAction nextAction;
            var nextCellEdge = currentGoToCell.ToEdge;
            while (mindRunner.VisitedActionsImpactedBy.TryGetValue(currentGoToCell, out nextAction) && nextAction is GoToCell nextGoToCell)
            {
                var relTargetEdgePos = (
                    from: nextCellEdge.From.Position - playerPosition,
                    to: nextCellEdge.To.Position - playerPosition);

                var aheadCellEdge = nextGoToCell.ToEdge;
                var relAheadTargetEdgePos = (
                    from: aheadCellEdge.From.Position - playerPosition,
                    to: aheadCellEdge.To.Position - playerPosition);

                var dirToAheadTargetEdgeNormals = (
                    from: Vector3.Cross(relAheadTargetEdgePos.from, Vector3.up),
                    to: Vector3.Cross(relAheadTargetEdgePos.to, Vector3.up));

                if (Vector3.Dot(relTargetEdgePos.from, dirToAheadTargetEdgeNormals.from) < 0)
                {
                    nextCellEdge.From = aheadCellEdge.From;
                }

                if (Vector3.Dot(relTargetEdgePos.to, dirToAheadTargetEdgeNormals.to) > 0)
                {
                    nextCellEdge.To = aheadCellEdge.To;
                }


                if (Vector3.Dot(relTargetEdgePos.from, dirToAheadTargetEdgeNormals.to) > 0)
                {
                    return nextCellEdge.From.Position;
                }

                if (Vector3.Dot(relTargetEdgePos.to, dirToAheadTargetEdgeNormals.from) < 0)
                {
                    return nextCellEdge.To.Position;
                }

                currentGoToCell = nextGoToCell;
            }

            Vector3? goalPosResult = nextAction switch
            {
                GoTo goTo => goTo.Location.Positions[goTo.Idx],
                CallAndWaitForElevator callWaitElevator => callWaitElevator.Level.PanelPosition,

                _ => null
            };

            if (goalPosResult.HasValue)
            {
                var relTargetEdgePos = (
                    from: nextCellEdge.From.Position - playerPosition,
                    to: nextCellEdge.To.Position - playerPosition);

                var goalPos = goalPosResult.Value;
                var relGoalPos = goalPos - playerPosition;
                var dirToGoalNormal = Vector3.Cross(relGoalPos, Vector3.up);

                if (Vector3.Dot(relTargetEdgePos.from, dirToGoalNormal) > 0)
                {
                    return nextCellEdge.From.Position;
                }

                if (Vector3.Dot(relTargetEdgePos.to, dirToGoalNormal) < 0)
                {
                    return nextCellEdge.To.Position;
                }

                return goalPos;
            }

            return nextCellEdge.MiddlePosition;
        }
    }
}
