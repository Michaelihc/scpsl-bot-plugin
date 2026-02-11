using PlayerRoles.PlayableScps.Scp049;
using PlayerRoles.Subroutines;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Termination.Scp049
{
    internal class AttackTarget(int idx, FpcBotPlayer<Scp049Role> botPlayer) : IAction
    {
        private readonly SubroutineBase attackAbility = botPlayer.FpcRole.SubroutineModule.AllSubroutines.First(sr => sr is Scp049AttackAbility);

        public void SetEnabledByBeliefs(FpcMind fpcMind)
        {
            fpcMind.ActionEnabledBy<TargetSightedLocation>(this)
                .Condition(b => b.NearPositions.Contains(b.Positions[idx]));
        }

        public void SetImpactsBeliefs(FpcMind fpcMind)
        {
            fpcMind.ActionImpacts<RemainingTargets>(this);
        }

        public float Cost => 0f;

        public void Tick()
        {
            botPlayer.BotHub.ConnectionToServer.Send<SubroutineMessage>(new(attackAbility, false));
        }

        public void Reset()
        {
        }
    }
}
