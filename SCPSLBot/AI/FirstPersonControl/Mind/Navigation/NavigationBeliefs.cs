using SCPSLBot.AI.FirstPersonControl.Mind.Elevation;
using SCPSLBot.Navigation.Mesh;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Navigation
{
    internal class NavigationBeliefs : IBelief
    {
        public event Action OnUpdate;

        public readonly Dictionary<TransformCell, Obstacle> Obstacles = [];
        public readonly Dictionary<TransformCell, NavigationCell> NavigationCells = [];
        public readonly Dictionary<(TransformCell, TransformCell), Elevator> Elevators = [];

        public NavigationCell GetNavigationCellWithin(Vector3 position)
        {
            var cellResult = NavigationMesh.GetCellWithinOrClosest(position);
            return cellResult.HasValue ? NavigationCells[cellResult.Value] : null;
        }

        public Obstacle GetNavigationObstacle(NavigationCell navCellWithin)
        {
            return navCellWithin is not null && this.Obstacles.TryGetValue(navCellWithin.TransformCell, out var obstacle) ? obstacle : null;
        }
    }
}
