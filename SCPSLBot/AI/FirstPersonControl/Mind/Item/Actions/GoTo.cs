using SCPSLBot.AI.FirstPersonControl.Mind.Item.Beliefs;
using SCPSLBot.AI.FirstPersonControl.Mind.Spacial;
using System;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Item.Actions
{
    internal abstract class GoTo<TItemLocation, TCriteria> : GoTo<TItemLocation>
        where TItemLocation : ItemLocations<TCriteria>
        where TCriteria : IItemBeliefCriteria, IEquatable<TCriteria>
    {
        public readonly TCriteria Criteria;
        protected GoTo(TCriteria criteria, int idx, FpcBotPlayer botPlayer) : base(idx, botPlayer)
        {
            this.Criteria = criteria;
        }

        protected override TItemLocation SetEnabledByLocation(FpcMind fpcMind, Predicate<TItemLocation> currentGetter)
        {
            return fpcMind.ActionEnabledBy(this, b => b.Criteria.Equals(Criteria), currentGetter);
        }
    }
}
