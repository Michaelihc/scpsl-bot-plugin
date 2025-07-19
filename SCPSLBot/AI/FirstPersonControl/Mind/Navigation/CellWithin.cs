using SCPSLBot.Navigation.Mesh;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Navigation
{
    internal class CellWithin(FpcBotPlayer botPlayer) : IBelief
    {
        public event Action OnUpdate;
        public TransformCell TransformCell;

        private readonly FpcBotPlayer botPlayer = botPlayer;
    }
}
