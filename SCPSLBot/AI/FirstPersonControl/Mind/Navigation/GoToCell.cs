using SCPSLBot.Navigation.Mesh;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Navigation
{
    internal class GoToCell(TransformCell fromCell, TransformCell toCell, FpcBotPlayer botPlayer) : IAction
    {
        private NavigationCell targetCell;
        private CellWithin cellWithin = botPlayer.MindRunner.GetBelief<CellWithin>();

        public void SetEnabledByBeliefs(FpcMind fpcMind)
        {
            fpcMind.ActionEnabledBy(this, this.cellWithin.NavigationCells[fromCell], c => c.IsPositionWithin(botPlayer.PlayerPosition));
            
            // TODO: stationary obstacle overcoming rewrite
            //fpcMind.ActionEnabledBy<DoorObstacle, DoorEntry?>(this, b => b.GetEntry(toCell.CenterPosition), c => !c.HasValue);
        }

        public void SetImpactsBeliefs(FpcMind fpcMind)
        {
            this.targetCell = fpcMind.ActionImpacts(this, this.cellWithin.NavigationCells[toCell]);
        }

        public float Cost => Vector3.Distance(toCell.CenterPosition, fromCell.CenterPosition);

        public void Tick(FpcMatchProvider matchProvider)
        {
            botPlayer.MoveToPosition(this.targetCell.TransformCell.CenterPosition);
        }

        public void Reset()
        {
        }
    }
}
