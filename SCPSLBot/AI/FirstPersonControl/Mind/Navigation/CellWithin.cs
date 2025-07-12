using SCPSLBot.Navigation.Mesh;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Navigation
{
    internal class CellWithin : Belief<bool>
    {
        public readonly Dictionary<TransformCell, NavigationCell> NavigationCells;

        public NavigationCell GetNavigationCellWithin(Vector3 position)
        {
            var cellResult = NavigationMesh.GetCellWithin(position);
            return cellResult.HasValue ? NavigationCells[cellResult.Value] : null;
        }
    }
}
