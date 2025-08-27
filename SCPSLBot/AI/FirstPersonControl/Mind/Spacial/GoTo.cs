using SCPSLBot.AI.FirstPersonControl.Mind.Navigation;
using SCPSLBot.Navigation.Mesh;
using System;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Spacial
{
    internal abstract class GoTo<TLocation>(int idx, FpcBotPlayer botPlayer) : GoTo(idx, botPlayer)
        where TLocation : Location
    {
        protected new TLocation location => base.location as TLocation;

        protected virtual TLocation SetEnabledByLocation(FpcMind fpcMind, Predicate<TLocation> enablingPredicate)
        {
            return fpcMind.ActionEnabledBy<TLocation>(this, enablingPredicate);
        }

        protected override Location SetEnabledByLocation(FpcMind fpcMind, Predicate<Location> enablingPredicate)
        {
            return SetEnabledByLocation(fpcMind, l => enablingPredicate(l));
        }
    }

    internal abstract class GoTo(int idx, FpcBotPlayer botPlayer) : IAction
    {
        public int Idx = idx;
        public Location Location => this.location;

        private readonly FpcBotPlayer botPlayer = botPlayer;
        private readonly NavigationBeliefs navBeliefs = botPlayer.MindRunner.GetBelief<NavigationBeliefs>();

        protected abstract Location SetEnabledByLocation(FpcMind fpcMind, Predicate<Location> enablingPredicate);

        protected Location location;
        private CellWithin cellWithin;
        private Func<NavigationCell> getLocationNavCell;

        public virtual void SetEnabledByBeliefs(FpcMind fpcMind)
        {
            SetEnabledByBeliefs(fpcMind, () => this.location.Positions[Idx]);
        }

        protected virtual void SetEnabledByBeliefs(FpcMind fpcMind, Func<Vector3> targetPositionGetter)
        {
            this.location = SetEnabledByLocation(fpcMind, b => b.Positions.Count > Idx);

            fpcMind.ActionEnabledBy(this, () => this.navBeliefs.GetNavigationObstacle(this.getLocationNavCell()), b => !b.HasHit(targetPositionGetter(), botPlayer.PlayerPosition));
            
            this.cellWithin = fpcMind.ActionEnabledBy<CellWithin>(this, b => b.TransformCell.HasValue);
            this.getLocationNavCell = fpcMind.ActionEnabledBy(this, () => this.navBeliefs.GetNavigationCellWithin(targetPositionGetter()), b => b.Is(cellWithin.TransformCell!.Value));
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
    }
}
