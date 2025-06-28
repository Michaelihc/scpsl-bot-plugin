using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

namespace SCPSLBot.AI.FirstPersonControl.Mind
{
    internal class FpcMatchProvider
    {
        private IAction actionToEnable;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public S Get<B, S>(B belief) where B : Belief<S>
        {
            return belief.GetMatchState(actionToEnable);
        }

        public void SetActionToEnable(IAction actionToEnable)
        {
            this.actionToEnable = actionToEnable;
        }
    }
}
