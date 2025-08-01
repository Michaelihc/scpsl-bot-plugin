using SCPSLBot.Navigation.Mesh;
using System;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Navigation
{
    internal class CellWithin(FpcBotPlayer botPlayer) : IBelief
    {
        public event Action OnUpdate;

        private Vector3 cachedPosition;
        private TransformCell? cachedTransformCell;

        public TransformCell? TransformCell
        {
            get
            {
                var playerPos = botPlayer.PlayerPosition;
                if (cachedPosition != playerPos)
                {
                    this.cachedPosition = playerPos;
                    this.cachedTransformCell = NavigationMesh.GetCellWithin(playerPos) ?? this.cachedTransformCell;
                }

                return this.cachedTransformCell;
            }
        }

        public override string ToString()
        {
            return $"{nameof(CellWithin)}: {TransformCell}";
        }
    }
}
