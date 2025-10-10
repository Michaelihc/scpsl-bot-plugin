using MapGeneration;
using SCPSLBot.AI.FirstPersonControl.Mind.Navigation;
using System;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Room.Beliefs
{
    internal class ZoneWithin : IBelief
    {
        public readonly FacilityZone Zone;
        public event Action OnUpdate;

        private readonly FpcBotNavigator navigator;
        private readonly CellWithin cellWithin;

        public ZoneWithin(FacilityZone zone, CellWithin cellWithin, FpcBotNavigator navigator)
        {
            this.Zone = zone;
            this.cellWithin = cellWithin;
            this.navigator = navigator;
        }

        public bool IsWithin = false;

        public void Update()
        {
            var newZoneValue = cellWithin.TransformCell?.Transform.GetComponent<RoomIdentifier>().Zone;

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
