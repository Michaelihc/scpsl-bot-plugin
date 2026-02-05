using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Termination
{
    internal class TerminateTargets : IGoal
    {
        public void SetEnabledByBeliefs(FpcMind fpcMind)
        {
            fpcMind.GoalEnabledBy<RemainingTargets>(this, b => false);
        }
    }
}
