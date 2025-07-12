using MapGeneration;
using SCPSLBot.AI.FirstPersonControl.Mind.Room.Beliefs;
using SCPSLBot.AI.FirstPersonControl.Mind.Spacial;
using System;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Room
{
    internal class GoToZoneEnterLocation : GoTo<ZoneEnterLocation>
    {
        public FacilityZone Zone { get; }
        public FacilityZone FromZone { get; }
        public GoToZoneEnterLocation(FacilityZone zone, FacilityZone fromZone, FpcBotPlayer botPlayer) : base(0, botPlayer)
        {
            Zone = zone;
            FromZone = fromZone;

            this.botPlayer = botPlayer;
        }

        protected override ZoneEnterLocation SetEnabledByLocation(FpcMind fpcMind, Predicate<ZoneEnterLocation> currentGetter)
        {
            return fpcMind.ActionEnabledBy(this, b => b.Zone == Zone && b.FromZone == FromZone, currentGetter);
        }

        public override void SetImpactsBeliefs(FpcMind fpcMind)
        {
            fpcMind.ActionImpacts<ZoneWithin>(this, static b => true, b => b.Zone == Zone);
        }

        public override float Weight { get; } = 1f;

        private readonly FpcBotPlayer botPlayer;

        public override void Tick()
        {
            var enterPosition = location.Positions[Idx];
            var cameraPosition = botPlayer.BotHub.PlayerHub.PlayerCameraReference.position;

            if (Vector3.Distance(enterPosition, cameraPosition) > 1.25f)
            {
                botPlayer.MoveToPosition(enterPosition);
                return;
            }
        }

        public override void Reset()
        { }

        public override string ToString()
        {
            return $"{nameof(GoToZoneEnterLocation)}({Zone} from {FromZone})";
        }
    }
}
