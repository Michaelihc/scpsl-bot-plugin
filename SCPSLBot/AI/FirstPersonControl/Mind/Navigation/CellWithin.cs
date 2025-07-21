using SCPSLBot.Navigation.Mesh;
using System;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Navigation
{
    internal class CellWithin(FpcBotPlayer botPlayer) : IBelief
    {
        public event Action OnUpdate;
        public TransformCell? TransformCell;

        private readonly FpcBotPlayer botPlayer = botPlayer;

        public void Update()
        {
            this.TransformCell = NavigationMesh.GetCellWithin(botPlayer.PlayerPosition);
        }
    }
}
