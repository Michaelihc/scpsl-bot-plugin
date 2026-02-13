using SCPSLBot.AI.FirstPersonControl.Mind.Room.Beliefs;
using SCPSLBot.AI.FirstPersonControl.Mind.Spacial;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Termination
{
    internal class GoToSearchRoom(int idx, FpcBotPlayer botPlayer) : GoTo<RoomEnterLocation>(idx, botPlayer)
    {
        public override void SetImpactsBeliefs(FpcMind fpcMind)
        {
            fpcMind.ActionImpacts<TargetSightedLocation>(this)
                .Condition(b => b.Positions.Count <= Idx);
        }

        public override float Weight => 30f;    // to make this action as last resort as possible
        public override float Cost => Weight * 10;

        public override void Tick()
        {
            var enterPosition = location.Positions[Idx];

            botPlayer.MoveToPosition(enterPosition);
        }

        public override void Reset()
        {
        }
    }
}
