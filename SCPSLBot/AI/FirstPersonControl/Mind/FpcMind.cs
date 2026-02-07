using System;
using System.Collections.Generic;
using System.Linq;

namespace SCPSLBot.AI.FirstPersonControl.Mind
{
    internal partial class FpcMind
    {
        public Dictionary<IAction, List<(Func<IBelief> Belief, Predicate<IBelief> Predicate)>> BeliefsEnablingActions { get; } = new();

        public Dictionary<IGoal, List<(IBelief Belief, Predicate<IBelief> Predicate)>> BeliefsEnablingGoals { get; } = new();

        public Dictionary<Type, List<IBelief>> Beliefs { get; } = new();
        public Dictionary<IBelief, List<(IAction, Predicate<IBelief>)>> ActionsImpactingBeliefs { get; } = new();

        public B ActionEnabledBy<B>(IAction action, Predicate<B> enablingPredicate) where B : class, IBelief
        {
            var beliefsOfType = Beliefs[typeof(B)];
            var belief = beliefsOfType.Single();

            return ActionEnabledBy(action, belief as B, enablingPredicate);
        }

        public B ActionEnabledBy<B>(IAction action, Predicate<B> beliefPredicate, Predicate<B> enablingPredicate) where B : class, IBelief
        {
            var beliefsOfType = Beliefs[typeof(B)];
            var belief = beliefsOfType.Single(b => beliefPredicate(b as B));

            return ActionEnabledBy(action, belief as B, enablingPredicate);
        }

        public B ActionEnabledBy<B>(IAction action, B belief, Predicate<B> enablingPredicate) where B : class, IBelief
        {
            ActionEnabledBy(action, () => belief, enablingPredicate);

            return belief;
        }

        public Func<B> ActionEnabledBy<B>(IAction action, Func<B> beliefGetter, Predicate<B> enablingPredicate) where B : class, IBelief
        {
            BeliefsEnablingActions[action].Add((beliefGetter, b => enablingPredicate(b as B)));

            return beliefGetter;
        }

        public B ActionImpacts<B>(IAction action, Predicate<B> beliefPredicate) where B : class, IBelief
        {
            return ActionImpacts(action, beliefPredicate, static b => true);
        }

        public B ActionImpacts<B>(IAction action, Predicate<B> beliefPredicate, Predicate<B> impactPredicate) where B : class, IBelief
        {
            var beliefsOfType = Beliefs[typeof(B)];
            var belief = beliefsOfType.Single(b => beliefPredicate(b as B)) as B;

            return ActionImpacts(action, belief, impactPredicate);
        }

        public B ActionImpacts<B>(IAction action, B belief) where B : class, IBelief
        {
            return ActionImpacts(action, belief, static b => true);
        }

        public B ActionImpacts<B>(IAction action, B belief, Predicate<B> impactPredicate) where B : class, IBelief
        {
            ActionsImpactingBeliefs[belief].Add((action, b => impactPredicate(b as B)));

            return belief;
        }

        public B GoalEnabledBy<B>(IGoal goal, Predicate<B> enablingPredicate) where B : class, IBelief
        {
            var beliefsOfType = Beliefs[typeof(B)];
            var belief = beliefsOfType.Single() as B;

            return GoalEnabledBy(goal, belief, enablingPredicate);
        }

        public B GoalEnabledBy<B>(IGoal goal, Predicate<B> beliefPredicate, Predicate<B> enablingPredicate) where B : class, IBelief
        {
            var beliefsOfType = Beliefs[typeof(B)];
            var belief = beliefsOfType.Single(b => beliefPredicate(b as B)) as B;

            return GoalEnabledBy(goal, belief, enablingPredicate);
        }

        public B GoalEnabledBy<B>(IGoal goal, B belief, Predicate<B> enablingPredicate) where B : class, IBelief
        {
            BeliefsEnablingGoals[goal].Add((belief, b => enablingPredicate(b as B)));

            return belief;
        }

        public FpcMind AddAction(IAction action)
        {
            BeliefsEnablingActions.Add(action, new());

            action.SetImpactsBeliefs(this);
            foreach (var impacts in actionImpactsBeliefs)
            {
                impacts.AddTo(this);
            }
            actionImpactsBeliefs.Clear();

            action.SetEnabledByBeliefs(this);
            foreach (var enabledBy in actionEnabledByBeliefs)
            {
                enabledBy.AddTo(this);
            }
            actionEnabledByBeliefs.Clear();

            return this;
        }

        public FpcMind AddActions(Func<int, IAction> actionFactory, int count = 3)
        {
            for (int i = 0; i < count; i++)
            {
                var action = actionFactory.Invoke(i);

                AddAction(action);
            }

            return this;
        }

        public FpcMind AddBelief(IBelief belief)
        {
            if (!Beliefs.TryGetValue(belief.GetType(), out var beliefsOfType))
            {
                beliefsOfType = new();
                Beliefs.Add(belief.GetType(), beliefsOfType);
            }
            beliefsOfType.Add(belief);

            ActionsImpactingBeliefs.Add(belief, new());

            return this;
        }

        public void AddGoal(IGoal goal)
        {
            BeliefsEnablingGoals.Add(goal, new());

            goal.SetEnabledByBeliefs(this);
        }

        public B GetBelief<B>() where B : class, IBelief
        {
            var beliefsOfType = Beliefs[typeof(B)];
            var belief = beliefsOfType.Single();

            return belief as B;
        }

        public B GetBelief<B>(Predicate<B> predicate) where B : class, IBelief
        {
            var beliefsOfType = Beliefs[typeof(B)];
            var belief = beliefsOfType.Find(b => predicate(b as B));

            return belief as B;
        }

        public IEnumerable<B> GetBeliefs<B>() where B : class, IBelief
        {
            return Beliefs[typeof(B)].Select(b => b as B);
        }
    }
}
