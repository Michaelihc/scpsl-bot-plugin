using SCPSLBot.Navigation.Mesh;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Navigation
{
    internal class NavigationCell : Belief<TransformCell?>
    {
        internal TransformCell? GetCellWithin(Vector3 position)
        {
            throw new NotImplementedException();
        }
    }
}
