using SCPSLBot.AI.FirstPersonControl.Mind.Spacial;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Termination
{
    internal class GoToTarget(FpcBotPlayer botPlayer) : GoTo<TargetSightedLocation>(0, botPlayer)
    {
        public override void SetImpactsBeliefs(FpcMind fpcMind)
        {
            fpcMind.ActionImpacts<TargetSightedLocation>(this)
                .Condition(b => ! b.NearPositions.Contains(b.Positions[Idx]));
        }

        public override float Weight { get; } = 1f;

        public override void Tick()
        {
            var itemPosition = location.Positions[Idx];

            botPlayer.MoveToPosition(itemPosition);
        }

        public override void Reset()
        {
        }
    }
}
