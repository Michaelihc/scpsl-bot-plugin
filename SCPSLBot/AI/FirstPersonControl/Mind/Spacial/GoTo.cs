using SCPSLBot.AI.FirstPersonControl.Mind.Navigation;
using SCPSLBot.Navigation.Mesh;
using System;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Spacial
{
    internal abstract class GoTo<TLocation> : IAction
        where TLocation : Location
    {
        public int Idx;

        private readonly FpcBotPlayer botPlayer;
        private readonly NavigationBeliefs navBeliefs;

        protected GoTo(int idx, FpcBotPlayer botPlayer)
        {
            this.Idx = idx;
            this.botPlayer = botPlayer;
            this.navBeliefs = botPlayer.MindRunner.GetBelief<NavigationBeliefs>();
        }

        protected virtual TLocation SetEnabledByLocation(FpcMind fpcMind, Predicate<TLocation> enablingPredicate)
        {
            return fpcMind.ActionEnabledBy<TLocation>(this, enablingPredicate);
        }

        protected TLocation location;
        private Func<NavigationCell> getLocationNavCell;

        public virtual void SetEnabledByBeliefs(FpcMind fpcMind)
        {
            SetEnabledByBeliefs(fpcMind, () => this.location.Positions[Idx]);
        }

        protected virtual void SetEnabledByBeliefs(FpcMind fpcMind, Func<Vector3> targetPositionGetter)
        {
            this.location = SetEnabledByLocation(fpcMind, b => b.Positions.Count > Idx);

            fpcMind.ActionEnabledBy(this, () => this.navBeliefs.Obstacles[this.getLocationNavCell().TransformCell], b => b.HasHit(targetPositionGetter(), botPlayer.PlayerPosition));
            this.getLocationNavCell = fpcMind.ActionEnabledBy(this, () => this.navBeliefs.GetNavigationCellWithin(targetPositionGetter()), b => b.IsWithin);
        }

        public abstract void SetImpactsBeliefs(FpcMind fpcMind);

        private const float DefaultDistance = 10f;

        private float Distance => location.Positions.Count > Idx
            ? this.getLocationNavCell().IsWithin
                ? Vector3.Distance(location.Positions[Idx], botPlayer.PlayerPosition)
                : 0f
            : DefaultDistance;

        public abstract float Weight { get; }
        public virtual float Cost => Distance * Weight;

        public abstract void Reset();
        public abstract void Tick();

        public void Tick(FpcMatchProvider matchProvider)
        {
            Tick();
        }
    }
}
