using SCPSLBot.AI.FirstPersonControl.Mind.Elevation;
using SCPSLBot.AI.FirstPersonControl.Mind.Spacial;
using SCPSLBot.Navigation.Mesh;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Navigation
{
    internal class NavigationBeliefs(FpcMindRunner mindRunner) : IBelief
    {
        public event Action OnUpdate;

        public readonly Dictionary<TransformCell, NavigationCell> NavigationCells = [];
        public readonly Dictionary<TransformCell, ElevatorLevel> ElevatorLevels = [];

        public readonly Dictionary<TransformCell, Obstacle> Obstacles = [];
        public readonly Dictionary<Vector3, Obstacle> ReceivedObstacles = [];

        public NavigationCell GetNavigationCellWithin(Vector3 position)
        {
            const float PlayerRadius = .5f;
            var cellResult = NavigationMesh.GetCellWithinOrClosest(position, PlayerRadius);
            return cellResult.HasValue ? NavigationCells[cellResult.Value] : null;
        }

        public Obstacle GetNavigationObstacle(NavigationCell navCellWithin)
        {
            return navCellWithin is not null && this.Obstacles.TryGetValue(navCellWithin.TransformCell, out var obstacle) ? obstacle : null;
        }

        public Obstacle GetReceivedObstacle(Vector3 targetPosition)
        {
            return ReceivedObstacles.TryGetValue(targetPosition, out var obstacle) ? obstacle : null;
        }

        public void HandleObstacleUpdate(Obstacle obstacle)
        {
            var action = mindRunner.VisitedActionsEnabledBy[obstacle];
            while (action is GoToCell or TravelOnElevator)
            {
                action = mindRunner.VisitedActionsImpactedBy[action];
            }

            Vector3? goalPosResult = action switch
            {
                GoTo goToAction => goToAction.Location.Positions[goToAction.Idx],
                CallAndWaitForElevator callWaitElevatorAction => callWaitElevatorAction.Level.PanelPosition,

                _ => null
            };

            if (!goalPosResult.HasValue)
            {
                return;
            }

            var goalPos = goalPosResult.Value;
            if (obstacle.HitResult.HasValue)
            {
                if (!ReceivedObstacles.TryGetValue(goalPos, out var storedObsacle) || storedObsacle != obstacle)
                {
                    ReceivedObstacles[goalPos] = obstacle;
                    OnUpdate?.Invoke();
                }
            }
            else
            {
                if (ReceivedObstacles.TryGetValue(goalPos, out var storedObsacle) && storedObsacle == obstacle)
                {
                    ReceivedObstacles.Remove(goalPos);
                    OnUpdate?.Invoke();
                }
            }

        }

        public override string ToString()
        {
            return $"{nameof(NavigationBeliefs)}: {{ {nameof(ReceivedObstacles)}: {ReceivedObstacles.Count} }}";
        }
    }
}
