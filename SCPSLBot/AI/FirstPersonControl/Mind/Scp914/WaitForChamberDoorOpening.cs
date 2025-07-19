using Interactables.Interobjects;
using Interactables.Interobjects.DoorUtils;
using SCPSLBot.AI.FirstPersonControl.Mind.Door;
using SCPSLBot.AI.FirstPersonControl.Mind.Navigation;
using System.Linq;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Scp914
{
    internal class WaitForChamberDoorOpening : IAction
    {
        private Obstacle doorObstacleBelief;
        private FpcBotPlayer botPlayer;

        public WaitForChamberDoorOpening(FpcBotPlayer botPlayer)
        {
            this.botPlayer = botPlayer;
        }

        public void SetEnabledByBeliefs(FpcMind fpcMind)
        {
        }

        public void SetImpactsBeliefs(FpcMind fpcMind)
        {
            doorObstacleBelief = fpcMind.ActionImpacts<Obstacle>(this, b => b.IsScp914ChamberDoor());
        }

        public float Cost => 5f;

        public void Tick(FpcMatchProvider matchProvider)
        {
            // TODO: door waiting idle logic
        }

        public void Reset()
        {

        }
    }

    internal static class DoorObstacleExtensions
    {
        public static bool IsScp914ChamberDoor(this Obstacle obstacle)
        {
            return obstacle.GetDoor() is BasicNonInteractableDoor;
        }
    }
}
