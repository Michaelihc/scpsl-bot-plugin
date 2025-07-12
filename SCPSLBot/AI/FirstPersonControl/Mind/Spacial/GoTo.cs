using SCPSLBot.AI.FirstPersonControl.Mind.Door;
using SCPSLBot.AI.FirstPersonControl.Mind.Elevation;
using SCPSLBot.AI.FirstPersonControl.Mind.Misc;
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
        private readonly CellWithin cellWithin;

        protected GoTo(int idx, FpcBotPlayer botPlayer)
        {
            this.Idx = idx;
            this.botPlayer = botPlayer;
            this.cellWithin = botPlayer.MindRunner.GetBelief<CellWithin>();
        }

        protected virtual TLocation SetEnabledByLocation(FpcMind fpcMind, Predicate<TLocation> enablingPredicate)
        {
            return fpcMind.ActionEnabledBy<TLocation>(this, enablingPredicate);
        }

        protected TLocation location;
        private Func<NavigationCell> getNavCell;

        public virtual void SetEnabledByBeliefs(FpcMind fpcMind)
        {
            SetEnabledByBeliefs(fpcMind, () => this.location.Positions[Idx]);
        }

        protected virtual void SetEnabledByBeliefs(FpcMind fpcMind, Func<Vector3> targetPositionGetter)
        {
            this.location = SetEnabledByLocation(fpcMind, b => b.Positions.Count > Idx);

            this.getNavCell = fpcMind.ActionEnabledBy(this, () => this.cellWithin.GetNavigationCellWithin(targetPositionGetter()), b => b.IsPositionWithin(botPlayer.PlayerPosition));

            // TODO: stationary obstacle overcoming rewrite

            //fpcMind.ActionEnabledBy<DoorObstacle, DoorEntry?>(this, b => b.GetEntry(targetPositionGetter()), c => !c.HasValue);
            //fpcMind.ActionEnabledBy<GlassObstacle>(this, b => !b.Is(targetPositionGetter()));
            //fpcMind.ActionEnabledBy<ElevationObstacle, ElevationObstacleMode>(this, ElevationObstacleMode.NoElevator, b => b.Has(targetPositionGetter()));
        }

        public abstract void SetImpactsBeliefs(FpcMind fpcMind);

        private const float DefaultDistance = 10f;

        private float Distance => location.Positions.Count > Idx
            ? this.getNavCell().TransformCell.IsPositionWithin(botPlayer.PlayerPosition)
                ? Vector3.Distance(location.Positions[Idx], botPlayer.CameraPosition)
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
