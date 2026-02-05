using System.Linq;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Escape
{
    internal class EscapeTheFacility : IGoal
    {
        private readonly FpcBotPlayer player;

        public void SetEnabledByBeliefs(FpcMind fpcMind)
        {
            fpcMind.GoalEnabledBy<PlayerEscaped>(this, b => false);
        }
    }
}
