using SCPSLBot.AI.FirstPersonControl.Mind;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Profiling;

namespace SCPSLBot.AI.FirstPersonControl
{
    internal class FpcMindRunner : FpcMind
    {
        public IAction RunningAction { get; private set; }
        public float RunningActionCost { get; private set; }
        public FpcMatchProvider MatchProvider { get; private set; }

        [Obsolete]
        public readonly HashSet<IBelief> RelevantBeliefs = new();
        private bool isBeliefsUpdated = false;

        public void SubscribeToBeliefUpdates()
        {
            foreach (var belief in Beliefs.Values.SelectMany(bc => bc))
            {
                belief.OnUpdate += () => OnBeliefUpdate(belief);
            }
        }

        public void EvaluateGoalsToActions()
        {
            isBeliefsUpdated = true;
        }

        public void Tick()
        {
            Profiler.BeginSample($"{nameof(FpcMindRunner)}.{nameof(Tick)}");

            // Run update on all relevant beliefs in undefined order (uses HashSet)
            foreach (var belief in VisitedActionsEnabledBy.Keys)
            {
                belief.Update();
            }

            if (isBeliefsUpdated)
            {
                isBeliefsUpdated = false;

                IEnumerable<(IAction, FpcMatchProvider)> enabledActions = GetEnabledActionsTowardsGoals();

                SelectActionAndRun(enabledActions);
            }

            RunningAction?.Tick(MatchProvider);

            Profiler.EndSample();
        }

        private void OnBeliefUpdate(IBelief updatedBelief)
        {
            if (!VisitedActionsEnabledBy.ContainsKey(updatedBelief))
            {
                Debug.Log($"[I] Belief updated: {updatedBelief}");
                return;
            }

            isBeliefsUpdated = true;
            Debug.Log($"[V] Belief updated: {updatedBelief}");
        }

        #region Action Finding

        public readonly Dictionary<IAction, float> VisitedActionsTotalCosts = new();
        private readonly Dictionary<IAction, float> remainingActionsToExplore = new();

        public readonly Dictionary<IBelief, IAction> VisitedActionsEnabledBy = new();
        public readonly Dictionary<IBelief, IGoal> VisitedGoalsEnabledBy = new();

        public readonly Dictionary<IAction, IAction> VisitedActionsImpactedBy = new();
        public readonly Dictionary<IAction, IGoal> VisitedGoalsImpactedBy = new();

        public readonly Dictionary<IAction, IAction> RelevantActionsImpactingActions = new();
        public readonly Dictionary<IGoal, IAction> RelevantActionsImpactingGoals = new();

        private IEnumerable<(IAction, FpcMatchProvider)> GetEnabledActionsTowardsGoals()
        {
            Profiler.BeginSample($"{nameof(FpcMindRunner)}.{nameof(GetEnabledActionsTowardsGoals)}");

            VisitedGoalsEnabledBy.Clear();
            VisitedActionsEnabledBy.Clear();
            VisitedActionsImpactedBy.Clear();
            VisitedGoalsImpactedBy.Clear();

            foreach (var goal in BeliefsEnablingGoals.Keys)
            {
                var matchProvider = new FpcMatchProvider();

                foreach (var enabledAction in FindEnabledActions(goal))
                {
                    RelevantActionsImpactingActions.Clear();
                    RelevantActionsImpactingGoals.Clear();

                    var actionImpacting = enabledAction;

                    while (VisitedActionsImpactedBy.TryGetValue(actionImpacting, out var actionImpactedBy))
                    {
                        RelevantActionsImpactingActions[actionImpactedBy] = actionImpacting;                        

                        actionImpacting = actionImpactedBy;
                    }

                    RelevantActionsImpactingGoals[goal] = actionImpacting;

                    yield return (enabledAction, matchProvider);
                    break;
                }
            }

            Profiler.EndSample();
        }

        private IEnumerable<IAction> FindEnabledActions(IGoal goal)
        {
            Debug.Log($"  Goal {goal.GetType().Name}...");

            VisitedActionsTotalCosts.Clear();
            remainingActionsToExplore.Clear();

            foreach (var (b, isSatisfied) in BeliefsEnablingGoals[goal])
            {
                VisitedGoalsEnabledBy[b] = goal;

                if (isSatisfied(b))
                {
                    Debug.Log($"    Belief {b} already satisfies goal.");
                    continue;
                }

                Debug.Log($"    Belief {b} needs to satisfy goal.");

                ProcessActionsImpacting(b, goal);
            }

            while (remainingActionsToExplore.Any())
            {
                var actionImpacting = remainingActionsToExplore.Aggregate((a, c) => c.Value < a.Value ? c : a).Key;
                remainingActionsToExplore.Remove(actionImpacting);

                Debug.Log($"      Exploring action {actionImpacting}.");
                foreach (var enabledAction in GetEnabledActionsEnabling(actionImpacting))
                {
                    yield return enabledAction;
                }
            }
        }

        private void ProcessActionsImpacting(IBelief belief, IGoal goalToEnable)
        {
            foreach (var (actionImpacting, canImpact) in ActionsImpactingBeliefs[belief])
            {
                VisitedGoalsImpactedBy[actionImpacting] = goalToEnable;

                if (!canImpact(belief))
                {
                    Debug.Log($"      Action {actionImpacting} cannot impact belief.");
                    continue;
                }

                var actionImpactingCost = actionImpacting.Cost;
                remainingActionsToExplore.Add(actionImpacting, actionImpactingCost);
                VisitedActionsTotalCosts[actionImpacting] = actionImpactingCost;

                Debug.Log($"      Action {actionImpacting} can impact belief with cost {actionImpactingCost}.");
            }
        }

        private IEnumerable<IAction> GetEnabledActionsEnabling(IAction actionToEnable)
        {
            var prefix = "      ";

            var beliefsEnabling = BeliefsEnablingActions[actionToEnable];

            var actionEnabled = true;
            foreach (var (getBelief, isSatisfied) in beliefsEnabling)
            {
                var b = getBelief();
                if (b == null)
                {
                    Debug.Log($"{prefix}  Belief getter {getBelief} returned null.");
                    continue;
                }

                VisitedActionsEnabledBy[b] = actionToEnable;

                if (isSatisfied(b))
                {
                    Debug.Log($"{prefix}  Belief {b} already satisfies action.");
                    continue;
                }

                Debug.Log($"{prefix}  Belief {b} needs to satisfy action.");
                actionEnabled = false;

                ProcessActionsImpacting(b, actionToEnable);

                break;
            }

            if (actionEnabled)
            {
                Debug.Log($"{prefix}Action {actionToEnable} conditions fulfilled.");

                yield return actionToEnable;
            }
        }

        private void ProcessActionsImpacting(IBelief belief, IAction actionToEnable)
        {
            var prefix = "        ";

            var actionToEnableCostToGoal = VisitedActionsTotalCosts[actionToEnable];

            foreach (var (actionImpacting, canImpact) in ActionsImpactingBeliefs[belief])
            {
                if (!canImpact(belief))
                {
                    Debug.Log($"{prefix}  Action {actionImpacting} cannot impact belief.");
                    continue;
                }

                var actionImpactingCostToGoal = actionToEnableCostToGoal + actionImpacting.Cost;
                if (VisitedActionsTotalCosts.ContainsKey(actionImpacting) && VisitedActionsTotalCosts[actionImpacting] < actionImpactingCostToGoal)
                {
                    Debug.Log($"{prefix}  Action {actionImpacting} can impact belief but general cost takes more ({VisitedActionsTotalCosts[actionImpacting]} < {actionImpactingCostToGoal}).");
                    continue;
                }

                var actionImpactingHeuristicCost = actionImpacting.HeuristicCost;

                Debug.Log($"{prefix}  Action {actionImpacting} can impact belief with general cost {actionImpactingCostToGoal}.");

                VisitedActionsTotalCosts[actionImpacting] = actionImpactingCostToGoal;
                remainingActionsToExplore[actionImpacting] = actionImpactingCostToGoal + actionImpactingHeuristicCost;

                VisitedActionsImpactedBy[actionImpacting] = actionToEnable;
            }
        }

        #endregion

        private void SelectActionAndRun(IEnumerable<(IAction, FpcMatchProvider)> enabledActions)
        {
            Profiler.BeginSample($"{nameof(FpcMindRunner)}.{nameof(SelectActionAndRun)}");

            var (action, provider) = enabledActions.FirstOrDefault();

            var prevAction = RunningAction;

            RunningAction = action ?? null;
            RunningActionCost = action?.Cost ?? 0f;
            MatchProvider = provider ?? null;

            Debug.Log($"New Action for bot: {RunningAction} (Cost: {RunningActionCost})");

            if (RunningAction != prevAction)
            {
                RunningAction?.Reset();
            }

            Profiler.EndSample();
        }
    }
}
