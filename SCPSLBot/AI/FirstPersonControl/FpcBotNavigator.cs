using MapGeneration;
using SCPSLBot.Navigation.Mesh;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl
{
    internal class FpcBotNavigator
    {
        private const float NavigationRecomputeIntervalSeconds = 0.2f;
        private const float FailedPathCooldownSeconds = 1f;
        private const float PathFailureLogIntervalSeconds = 10f;
        private const float CachedGoalReuseDistanceSqr = 1f;
        private const float WaypointReleaseDistanceSqr = 1.4f * 1.4f;
        private const float WaypointSnapGoalChangeDistanceSqr = 4f * 4f;
        private const float WaypointSnapDistanceSqr = 7f * 7f;

        private Vector3 lastPlayerPosition;
        private TransformCell? cellWithin;

        private TransformCell currentCell;
        private TransformCell? goalCell;
        public List<TransformCell> CellsPath { get; } = new();
        public IEnumerable<(TransformCell Cell, TransformCell NextCell)> CellPathSegments { get; }
        private int currentPathIdx = -1;

        public Vector3 GoalPosition { get; private set; }
        public List<Vector3> PointsPath { get; } = new();
        public IEnumerable<(Vector3 point, Vector3 nextPoint)> PathSegments { get; }

        private bool isGoalOutside;
        private Vector3 targetCellClosestPositionToGoal;

        private readonly FpcBotPlayer botPlayer;
        private bool hasCachedPositionTowardsGoal;
        private Vector3 cachedPositionTowardsGoal;
        private Vector3 cachedGoalPosition;
        private bool hasStableWaypoint;
        private Vector3 stableWaypoint;
        private Vector3 stableWaypointGoal;
        private float nextNavigationUpdateTime;
        private float nextPathFailureLogTime;
        private int suppressedPathFailureLogs;

        public Vector3 DebugPositionTowardsGoal => cachedPositionTowardsGoal;

        public FpcBotNavigator(FpcBotPlayer botPlayer)
        {
            this.botPlayer = botPlayer;

            this.PathSegments = PointsPath.Zip(PointsPath.Skip(1), (point, nextPoint) => (point, nextPoint));
            this.CellPathSegments = CellsPath.Zip(CellsPath.Skip(1), (cell, nextCell) => (cell, nextCell));
        }

        public Vector3 GetPositionTowards(Vector3 goalPosition)
        {
            if (hasCachedPositionTowardsGoal
                && Time.time < nextNavigationUpdateTime
                && (goalPosition - cachedGoalPosition).sqrMagnitude <= CachedGoalReuseDistanceSqr)
            {
                return cachedPositionTowardsGoal;
            }

            bool pathUsable = this.UpdateNavigationTo(goalPosition);

            if (!IsAtLastCell())
            {
                Vector3 nextTargetPosition = GetNextCorner(goalPosition);
                CachePositionTowardsGoal(goalPosition, nextTargetPosition, pathUsable);
                return nextTargetPosition;
            }
            else 
            {
                if (goalCell != null && isGoalOutside)
                {
                    CachePositionTowardsGoal(goalPosition, targetCellClosestPositionToGoal, pathUsable);
                    return targetCellClosestPositionToGoal;
                }

                CachePositionTowardsGoal(goalPosition, goalPosition, pathUsable);
                return goalPosition;
            }
        }

        private bool UpdateNavigationTo(Vector3 goalPosition)
        {
            var playerPosition = botPlayer.FpcRole.FpcModule.transform.position;
            var pathUsable = true;

            if (!IsAtLastCell())
            {
                bool isEdgeReached;
                do
                {
                    var nextTargetCell = this.CellsPath[this.currentPathIdx + 1];
                    if (!TryGetCellEdge(this.currentCell, nextTargetCell, out var nextTargetCellEdge))
                    {
                        LogPathFailure(goalPosition);
                        this.CellsPath.Clear();
                        this.currentPathIdx = -1;
                        return false;
                    }

                    isEdgeReached = NavigationMesh.IsAtPositiveEdgeSide(playerPosition, nextTargetCellEdge);
                    if (isEdgeReached)
                    {
                        this.currentCell = this.CellsPath[++this.currentPathIdx];
                        //Log.Debug($"New current cell {this.currentCell}.");
                    }
                }
                while (isEdgeReached && !IsAtLastCell());
            }

            var withinCell = GetCellWithin();
            var targetCell = NavigationMesh.GetCellWithin(goalPosition);

            if (targetCell == null)
            {
                RoomUtils.TryGetRoom(goalPosition, out var goalRoom);

                var nearestEdge = NavigationMesh.GetNearestEdge(goalPosition, out var closestPoint, goalRoom);
                if (nearestEdge.HasValue)
                {
                    var nearestLocalEdge = new Edge(nearestEdge.Value.From, nearestEdge.Value.To);
                    targetCell = NavigationMesh.LocalMeshesByRoom[goalRoom.gameObject].Cells
                        .Where(a => a.Edges.Any(e => e == nearestLocalEdge))
                        .Select(a => new TransformCell?(new (a, goalRoom.transform)))
                        .FirstOrDefault();
                    targetCellClosestPositionToGoal = closestPoint;
                }
                else
                {
                    LogPathFailure(goalPosition);
                    pathUsable = false;
                }

                isGoalOutside = true;
            }
            else
            {
                isGoalOutside = false;
            }

            if (withinCell != null && targetCell != null && (targetCell != this.goalCell || withinCell.Value != this.currentCell))
            {
                this.currentCell = withinCell.Value;
                this.goalCell = targetCell.Value;
                //Log.Debug($"New start cell {withinCell}.");
                //Log.Debug($"New goal cell {targetCell}.");

                NavigationMesh.FindShortestPath(withinCell.Value, targetCell.Value, this.CellsPath);
                this.currentPathIdx = 0;

                //Log.Debug($"New path of {this.CellsPath.Count} cells:");
                //foreach (var cellInPath in CellsPath)
                //{
                //    Log.Debug($"Cell {cellInPath}.");
                //}

                this.GoalPosition = goalPosition;

                this.PointsPath.Clear();
                this.PointsPath.Add(playerPosition);

                var partialPath = false;
                foreach (var (cell, nextCell) in CellPathSegments)
                {
                    if (!TryGetCellEdge(cell, nextCell, out var e))
                    {
                        partialPath = true;
                        break;
                    }
                    this.PointsPath.Add(Vector3.Lerp(e.From.Position, e.To.Position, .5f));
                }

                if (!partialPath) 
                {
                    this.PointsPath.Add(goalPosition);
                }
            }

            return pathUsable;
        }

        public TransformCell? GetCellWithin()
        {
            var playerPosition = botPlayer.PlayerPosition;
            cellWithin = NavigationMesh.GetCellWithin(playerPosition);
            lastPlayerPosition = playerPosition;

            return cellWithin;
        }

        private Vector3 GetNextCorner(Vector3 goalPosition)
        {
            var playerPosition = botPlayer.PlayerPosition;

            var nextTargetCell = this.CellsPath[this.currentPathIdx + 1];
            if (!TryGetCellEdge(currentCell, nextTargetCell, out var targetCellEdge))
            {
                return currentCell.CenterPosition;
            }
            var nextTargetEdgeMiddlePosition = Vector3.Lerp(targetCellEdge.From.Position, targetCellEdge.To.Position, 0.5f);

            var nextTargetPosition = nextTargetEdgeMiddlePosition;

            var aheadPathIdx = this.currentPathIdx + 1;

            while (nextTargetEdgeMiddlePosition == nextTargetPosition && aheadPathIdx < this.CellsPath.Count - 1)
            {
                aheadPathIdx++;

                var relTargetEdgePos = (
                    from: targetCellEdge.From.Position - playerPosition,
                    to: targetCellEdge.To.Position - playerPosition);

                var aheadTargetCell = this.CellsPath[aheadPathIdx];
                if (!TryGetCellEdge(nextTargetCell, aheadTargetCell, out var aheadTargetCellEdge))
                {
                    goalPosition = nextTargetCell.CenterPosition;
                    break;
                }

                var relAheadTargetEdgePos = (
                    from: aheadTargetCellEdge.From.Position - playerPosition,
                    to: aheadTargetCellEdge.To.Position - playerPosition);

                var dirToAheadTargetEdgeNormals = (
                    from: Vector3.Cross(relAheadTargetEdgePos.from, Vector3.up),
                    to: Vector3.Cross(relAheadTargetEdgePos.to, Vector3.up));

                if (Vector3.Dot(relTargetEdgePos.from, dirToAheadTargetEdgeNormals.from) < 0)
                {
                    targetCellEdge.From = aheadTargetCellEdge.From;
                }

                if (Vector3.Dot(relTargetEdgePos.to, dirToAheadTargetEdgeNormals.to) > 0)
                {
                    targetCellEdge.To = aheadTargetCellEdge.To;
                }


                if (Vector3.Dot(relTargetEdgePos.from, dirToAheadTargetEdgeNormals.to) > 0)
                {
                    nextTargetPosition = targetCellEdge.From.Position;
                }

                if (Vector3.Dot(relTargetEdgePos.to, dirToAheadTargetEdgeNormals.from) < 0)
                {
                    nextTargetPosition = targetCellEdge.To.Position;
                }

                nextTargetCell = aheadTargetCell;
            }

            if (nextTargetPosition == nextTargetEdgeMiddlePosition)
            {
                nextTargetPosition = goalPosition;

                var relNextTargetEdgePos = (
                    from: targetCellEdge.From.Position - playerPosition,
                    to: targetCellEdge.To.Position - playerPosition);

                var relGoalPos = goalPosition - playerPosition;
                var dirToGoalNormal = Vector3.Cross(relGoalPos, Vector3.up);

                if (Vector3.Dot(relNextTargetEdgePos.from, dirToGoalNormal) > 0)
                {
                    nextTargetPosition = targetCellEdge.From.Position;
                }

                if (Vector3.Dot(relNextTargetEdgePos.to, dirToGoalNormal) < 0)
                {
                    nextTargetPosition = targetCellEdge.To.Position;
                }
            }

            return nextTargetPosition;
        }

        private static bool TryGetCellEdge(TransformCell cell, TransformCell nextCell, out TransformEdge edge)
        {
            if (cell.AdjacentCellEdges.TryGetValue(nextCell, out edge))
            {
                return true;
            }

            if (NavigationMesh.ForeignConnectedCellEdges.TryGetValue(cell, out var foreignEdges)
                && foreignEdges.TryGetValue(nextCell, out edge))
            {
                return true;
            }

            edge = default;
            return false;
        }

        private bool IsAtLastCell()
        {
            return this.currentPathIdx >= this.CellsPath.Count - 1;
        }

        private void CachePositionTowardsGoal(Vector3 goalPosition, Vector3 positionTowardsGoal, bool pathUsable)
        {
            hasCachedPositionTowardsGoal = true;
            cachedPositionTowardsGoal = StabilizeWaypoint(goalPosition, positionTowardsGoal);
            cachedGoalPosition = goalPosition;
            nextNavigationUpdateTime = Time.time + (pathUsable
                ? NavigationRecomputeIntervalSeconds
                : FailedPathCooldownSeconds);
        }

        private Vector3 StabilizeWaypoint(Vector3 goalPosition, Vector3 proposedWaypoint)
        {
            var playerPosition = botPlayer.PlayerPosition;
            var goalChanged = !hasStableWaypoint
                || (goalPosition - stableWaypointGoal).sqrMagnitude > WaypointSnapGoalChangeDistanceSqr;
            var waypointReached = hasStableWaypoint
                && (playerPosition - stableWaypoint).sqrMagnitude <= WaypointReleaseDistanceSqr;
            var waypointJumped = hasStableWaypoint
                && (proposedWaypoint - stableWaypoint).sqrMagnitude > WaypointSnapDistanceSqr;

            if (goalChanged || waypointReached || waypointJumped)
            {
                stableWaypoint = proposedWaypoint;
                stableWaypointGoal = goalPosition;
                hasStableWaypoint = true;
                return stableWaypoint;
            }

            stableWaypointGoal = goalPosition;
            return stableWaypoint;
        }

        private void LogPathFailure(Vector3 goalPosition)
        {
            if (LabApiPlugin.Instance?.Config?.EnableVerboseBotLogs != true)
            {
                return;
            }

            if (Time.time < nextPathFailureLogTime)
            {
                suppressedPathFailureLogs++;
                return;
            }

            string suppressedSuffix = suppressedPathFailureLogs > 0
                ? $" suppressed={suppressedPathFailureLogs}"
                : string.Empty;
            suppressedPathFailureLogs = 0;
            nextPathFailureLogTime = Time.time + PathFailureLogIntervalSeconds;
            Debug.LogWarning(
                $"Could not find path to goal position ({goalPosition.x:F1}, {goalPosition.y:F1}, {goalPosition.z:F1}); retrying at reduced rate.{suppressedSuffix}");
        }
    }
}
