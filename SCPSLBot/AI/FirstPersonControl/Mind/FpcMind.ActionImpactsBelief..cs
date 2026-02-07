using System;
using System.Collections.Generic;
using System.Linq;

namespace SCPSLBot.AI.FirstPersonControl.Mind
{
    internal partial class FpcMind
    {
        private readonly List<ActionImpactsBelief> actionImpactsBeliefs = [];

        public IActionImpactsBelief<TBelief> ActionImpacts<TBelief>(IAction action) where TBelief : class, IBelief
        {
            var beliefsOfType = this.Beliefs[typeof(TBelief)];
            var belief = (TBelief)beliefsOfType.Single();

            var newImpacts = new ActionImpactsBelief<TBelief>(this, action, belief);
            newImpacts.Condition(static b => true);

            actionImpactsBeliefs.Add(newImpacts);
            return newImpacts;
        }

        public interface IActionImpactsBelief<TBelief> where TBelief : class, IBelief
        {
            public IActionImpactsBelief<TBelief> Condition(Predicate<TBelief> condition);
            public IActionImpactsBelief<TBelief> WithPredicate(Predicate<TBelief> beliefPredicate);
        }

        private class ActionImpactsBelief<TBelief>(FpcMind mind, IAction action, TBelief belief) : ActionImpactsBelief(action, belief), IActionImpactsBelief<TBelief>
            where TBelief : class, IBelief
        {
            public IActionImpactsBelief<TBelief> Condition(Predicate<TBelief> impactPredicate)
            {
                this.impactPredicate = b => impactPredicate((TBelief)b);
                return this;
            }

            public IActionImpactsBelief<TBelief> WithPredicate(Predicate<TBelief> beliefPredicate)
            {
                var beliefsOfType = mind.Beliefs[typeof(TBelief)];
                var belief = beliefsOfType.Single(b => beliefPredicate((TBelief)b));

                this.belief = belief;
                return this;
            }
        }

        private class ActionImpactsBelief(IAction action, IBelief belief)
        {
            protected IBelief belief = belief;
            protected Predicate<IBelief> impactPredicate;

            public void AddTo(FpcMind fpcMind)
            {
                fpcMind.ActionImpacts(action, belief, impactPredicate);
            }
        }
    }
}
