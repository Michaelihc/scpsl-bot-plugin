using Cmdbinding;
using System;
using System.Collections.Generic;
using System.Linq;

namespace SCPSLBot.AI.FirstPersonControl.Mind
{
    internal partial class FpcMind
    {
        private readonly List<ActionEnabledByBelief> actionEnabledByBeliefs = [];

        public IActionEnabledByBelief<TBelief> ActionEnabledBy<TBelief>(IAction action) where TBelief : class, IBelief
        {
            var newEnabledBy = new ActionEnabledByBelief<TBelief>(this, action);

            var beliefsOfType = this.Beliefs[typeof(TBelief)];
            var belief = (TBelief)beliefsOfType.Single();
            newEnabledBy.WithProvider(() => belief)
                .Condition(b => true);

            actionEnabledByBeliefs.Add(newEnabledBy);
            return newEnabledBy;
        }

        public interface IActionEnabledByBelief<TBelief> where TBelief : class, IBelief
        {
            public IActionEnabledByBelief<TBelief> Condition(Predicate<TBelief> condition);
            public IActionEnabledByBelief<TBelief> WithPredicate(Predicate<TBelief> beliefPredicate);
            public IActionEnabledByBelief<TBelief> WithProvider(Func<TBelief> beliefProvider);
        }

        private class ActionEnabledByBelief<TBelief>(FpcMind mind, IAction action) : ActionEnabledByBelief(action), IActionEnabledByBelief<TBelief>
            where TBelief : class, IBelief
        {
            public IActionEnabledByBelief<TBelief> Condition(Predicate<TBelief> condition)
            {
                this.enablingPredicate = b => condition((TBelief)b);
                return this;
            }

            public IActionEnabledByBelief<TBelief> WithPredicate(Predicate<TBelief> beliefPredicate)
            {
                var beliefsOfType = mind.Beliefs[typeof(TBelief)];
                var belief = beliefsOfType.Single(b => beliefPredicate((TBelief)b));

                this.beliefProvider = () => belief;
                return this;
            }

            public IActionEnabledByBelief<TBelief> WithProvider(Func<TBelief> beliefProvider)
            {
                this.beliefProvider = beliefProvider;
                return this;
            }
        }

        private class ActionEnabledByBelief(IAction action)
        {
            protected Func<IBelief> beliefProvider;
            protected Predicate<IBelief> enablingPredicate;

            public void AddTo(FpcMind fpcMind)
            {
                fpcMind.ActionEnabledBy(action, beliefProvider, enablingPredicate);
            }
        }
    }
}
