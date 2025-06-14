using System;
using System.Collections.Generic;

namespace SCPSLBot.AI.FirstPersonControl.Mind
{
    internal class Belief<S> : IBelief
    {
        private readonly Dictionary<IAction, (Func<IBelief, S>, Predicate<S>)> actionsEnabledByMatchers = new();
        public void AddEnablingAction<B>(IAction action, Func<B, S> matchGetter, Predicate<S> matchPredicate) where B : class, IBelief
        {
            actionsEnabledByMatchers.Add(action, (b => matchGetter(b as B), matchPredicate));
        }

        private readonly Dictionary<IAction, Predicate<S>> actionsImpactingMatchers = new();
        public void AddActionImpacting(IAction action, Predicate<S> matchPredicate)
        {
            actionsImpactingMatchers.Add(action, s => matchPredicate(s));
        }

        private readonly Dictionary<IGoal, (Func<Belief<S>, S>, Predicate<S>)> goalsEnabledByMatchers = new();
        public void AddEnablingGoal<B>(IGoal goal, Func<B, S> matchGetter, Predicate<S> matchPredicate) where B: Belief<S>
        {
            goalsEnabledByMatchers.Add(goal, (b => matchGetter(b as B), matchPredicate));
        }

        public bool IsEnabledAction(IAction action)
        {
            var (matchGetter, matchPredicate) = actionsEnabledByMatchers[action];

            return matchPredicate(matchGetter(this));
        }

        public bool CanImpactedByAction(IAction actionImpacting, IAction actionToEnable)
        {
            var (enablingMatchGetter, _) = actionsEnabledByMatchers[actionToEnable];
            var impactMatchPredicate = actionsImpactingMatchers[actionImpacting];

            return impactMatchPredicate(enablingMatchGetter(this));
        }

        public bool EvaluateEnabling(IGoal goal)
        {
            var (matchGetter, matchPredicate) = goalsEnabledByMatchers[goal];

            return matchPredicate(matchGetter(this));
        }

        public bool CanImpactedByAction(IAction actionImpacting, IGoal goalToEnable)
        {
            var (targetGetter, _) = goalsEnabledByMatchers[goalToEnable];
            var impactMatchPredicate = actionsImpactingMatchers[actionImpacting];

            return impactMatchPredicate(targetGetter(this));
        }

        public event Action OnUpdate;
        protected void InvokeOnUpdate()
        {
            OnUpdate?.Invoke();
        }
    }
}
