using Interactables.Interobjects;
using Interactables.Interobjects.DoorUtils;
using SCPSLBot.AI.FirstPersonControl.Mind.Door;
using SCPSLBot.AI.FirstPersonControl.Mind.Navigation;
using SCPSLBot.Navigation.Mesh;
using System.Linq;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Scp914
{
    internal class WaitForChamberDoorOpening : IAction
    {
        private TransformCell transformCell;
        private NavigationBeliefs navigationBeliefs;
        private FpcBotPlayer botPlayer;
        private Obstacle doorObstacleBelief;

        public WaitForChamberDoorOpening(TransformCell transformCell, NavigationBeliefs navigationBeliefs, FpcBotPlayer botPlayer)
        {
            this.transformCell = transformCell;
            this.navigationBeliefs = navigationBeliefs;
            this.botPlayer = botPlayer;
        }

        public void SetEnabledByBeliefs(FpcMind fpcMind)
        {
        }

        public void SetImpactsBeliefs(FpcMind fpcMind)
        {
            doorObstacleBelief = fpcMind.ActionImpacts<Obstacle>(this, this.navigationBeliefs.Obstacles[this.transformCell], b => b.IsScp914ChamberDoor());
        }

        public float Cost => 5f;

        public void Tick()
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
            return obstacle.Door is BasicNonInteractableDoor;
        }
    }
}
