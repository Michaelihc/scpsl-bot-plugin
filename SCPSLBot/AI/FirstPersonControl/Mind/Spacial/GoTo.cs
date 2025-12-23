using SCPSLBot.AI.FirstPersonControl.Mind.Navigation;
using SCPSLBot.AI.FirstPersonControl.Mind.Room.Beliefs;
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

        protected abstract Location SetEnabledByLocation(FpcMind fpcMind, Predicate<Location> enablingPredicate);

        protected Location location;
        private NavigationBeliefs navBeliefs;
        private CellWithin cellWithin;
        private Func<NavigationCell> getLocationNavCell;

        public virtual void SetEnabledByBeliefs(FpcMind fpcMind)
        {
            SetEnabledByBeliefs(fpcMind, () => this.location.Positions[Idx]);
        }

        protected virtual void SetEnabledByBeliefs(FpcMind fpcMind, Func<Vector3> getTargetPosition)
        {
            this.location = SetEnabledByLocation(fpcMind, b => b.Positions.Count > Idx);

            this.navBeliefs = fpcMind.ActionEnabledBy<NavigationBeliefs>(this, b => true);
            fpcMind.ActionEnabledBy<Obstacle>(this, () => this.navBeliefs.GetReceivedObstacle(this.location.Positions[Idx]), b => !(b?.HitResult.HasValue ?? false));

            // Add obstacle belief enabling this action once needed.

            this.cellWithin = fpcMind.ActionEnabledBy<CellWithin>(this, b => location.NearPositions.Contains(getTargetPosition()) || b.TransformCell.HasValue);
            this.getLocationNavCell = fpcMind.ActionEnabledBy(this, () => this.navBeliefs.GetNavigationCellWithin(getTargetPosition()), b => location.NearPositions.Contains(getTargetPosition()) || (b?.Is(cellWithin.TransformCell!.Value) ?? false));
        }

        public abstract void SetImpactsBeliefs(FpcMind fpcMind);

        private const float DefaultDistance = 10f;

        private float Distance => location.Positions.Count > Idx
            ? this.getLocationNavCell()?.IsWithin ?? false
                ? Vector3.Distance(location.Positions[Idx], botPlayer.PlayerPosition)
                : DefaultDistance
            : DefaultDistance;

        public abstract float Weight { get; }
        public virtual float Cost => Distance * Weight;

        public abstract void Reset();
        public abstract void Tick();
    }
}
