using SCPSLBot.Navigation.Mesh;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Navigation
{
    internal class NavigationCell(TransformCell transformCell) : Belief<bool>
    {
        public TransformCell TransformCell { get; init; } = transformCell;

        internal bool IsPositionWithin(Vector3 position)
        {
            throw new NotImplementedException();
        }

        public Vector3 GetNextCorner()
        {
            throw new NotImplementedException();
        }
    }
}
