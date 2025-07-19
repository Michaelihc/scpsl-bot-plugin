using MapGeneration;
using SCPSLBot.AI.FirstPersonControl.Perception.Senses;
using System;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Room.Beliefs
{
    internal class ZoneWithin : IBelief
    {
        public readonly FacilityZone Zone;
        public event Action OnUpdate;

        private readonly FpcBotNavigator navigator;
        private readonly RoomSightSense roomSightSense;

        public ZoneWithin(FacilityZone zone, RoomSightSense roomSightSense, FpcBotNavigator navigator)
        {
            this.Zone = zone;
            this.roomSightSense = roomSightSense;
            this.navigator = navigator;
        }

        public bool IsWithin = false;

        public void Update()
        {
            var newZoneValue = roomSightSense.RoomWithin?.Zone;

            var newIsWithin = newZoneValue == Zone;
            if (newIsWithin != IsWithin)
            {
                IsWithin = newIsWithin;
                OnUpdate?.Invoke();
            }
        }

        public override string ToString()
        {
            return $"{nameof(ZoneWithin)}({Zone}): {IsWithin}";
        }
    }
}
