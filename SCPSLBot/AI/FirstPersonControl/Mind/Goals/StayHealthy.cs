using SCPSLBot.AI.FirstPersonControl.Mind.Survival;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Goals
{
    internal class StayHealthy : IGoal
    {
        public void SetEnabledByBeliefs(FpcMind fpcMind)
        {
            fpcMind.GoalEnabledBy<Health, float>(this, b => (b.MaxAmount - b.Amount), b => false);
        }
    }
}
