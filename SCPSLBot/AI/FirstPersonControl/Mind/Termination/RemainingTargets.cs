using System;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Termination
{
    internal class RemainingTargets : IBelief
    {
        public event Action OnUpdate;

        public override string ToString()
        {
            return $"{nameof(RemainingTargets)}";
        }
    }
}
