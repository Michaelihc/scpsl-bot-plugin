using System;
using System.Collections.Generic;

namespace SCPSLBot.AI.FirstPersonControl.Mind
{
    internal class Belief<S> : IBelief
    {
        private readonly Dictionary<IAction, (Func<IBelief, S>, Predicate<S>)> actionsEnabledByMatchers = new();
        [Obsolete] public void AddEnablingAction<B>(IAction action, Func<B, S> matchGetter, Predicate<S> matchPredicate) where B : class, IBelief
        {
            actionsEnabledByMatchers.Add(action, (b => matchGetter(b as B), s => matchPredicate(s)));
        }

        private readonly Dictionary<IAction, Predicate<S>> actionsImpactingMatchers = new();
        [Obsolete] public void AddActionImpacting(IAction action, Predicate<S> matchPredicate)
        {
            actionsImpactingMatchers.Add(action, s => matchPredicate(s));
        }

        private readonly Dictionary<IGoal, (Func<IBelief, S>, Func<IBelief, S>)> goalsEnabledByGetters = new();
        [Obsolete] public void AddEnablingGoal<B>(IGoal goal, Func<B, S> targetGetter, Func<B, S> currentGetter) where B : class, IBelief
        {
            goalsEnabledByGetters.Add(goal, (b => targetGetter(b as B), b => currentGetter(b as B)));
        }

        [Obsolete] public S GetMatchState(IAction actionToEnable)
        {
            var (enablingMatchGetter, _) = actionsEnabledByMatchers[actionToEnable];
            return enablingMatchGetter(this);
        }

        public event Action OnUpdate;
        protected void InvokeOnUpdate()
        {
            OnUpdate?.Invoke();
        }
    }
}
