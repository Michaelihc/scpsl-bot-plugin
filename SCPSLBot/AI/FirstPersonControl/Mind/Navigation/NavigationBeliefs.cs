using SCPSLBot.Navigation.Mesh;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Navigation
{
    internal class NavigationBeliefs : IBelief
    {
        public event Action OnUpdate;

        public readonly Dictionary<TransformCell, Obstacle> Obstacles = new();
        public readonly Dictionary<TransformCell, NavigationCell> NavigationCells = new();

        public NavigationCell GetNavigationCellWithin(Vector3 position)
        {
            var cellResult = NavigationMesh.GetCellWithin(position);
            return cellResult.HasValue ? NavigationCells[cellResult.Value] : null;
        }

        public Obstacle GetNavigationObstacle(NavigationCell navCellWithin)
        {
            return this.Obstacles.TryGetValue(navCellWithin.TransformCell, out var obstacle) ? obstacle : null;
        }
    }
}
