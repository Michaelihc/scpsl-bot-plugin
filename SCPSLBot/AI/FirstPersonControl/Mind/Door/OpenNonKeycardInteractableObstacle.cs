using Interactables.Interobjects.DoorUtils;
using SCPSLBot.AI.FirstPersonControl.Mind.Navigation;
using SCPSLBot.Navigation.Mesh;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Door
{
    internal class OpenNonKeycardInteractableObstacle(TransformCell transformCell, NavigationBeliefs navigationBeliefs, FpcBotPlayer botPlayer) : IAction
    {
        private readonly FpcBotPlayer botPlayer = botPlayer;
        private Obstacle doorObstacleBelief;
        private const float interactDistance = 2f;

        public void SetEnabledByBeliefs(FpcMind fpcMind)
        {
            fpcMind.ActionEnabledBy<NavigationCell>(this, navigationBeliefs.NavigationCells[transformCell], b => doorObstacleBelief.IsNear || b.IsWithin);
        }

        public void SetImpactsBeliefs(FpcMind fpcMind)
        {
            doorObstacleBelief = fpcMind.ActionImpacts<Obstacle>(this, navigationBeliefs.Obstacles[transformCell], b => b.IsInteractable(DoorPermissionFlags.None));
        }

        public float Cost => 0f;

        public void Tick()
        {
            var doorToOpen = doorObstacleBelief.Door;
            if (!doorToOpen)
            {
                throw new System.Exception($"{nameof(doorToOpen)} is null to open");
            }

            var isTargetStateOpen = doorToOpen.TargetState;

            var doorPos = doorToOpen.transform.position + Vector3.up * 1f;
            var isNear = doorObstacleBelief.IsNear;

            if (!isTargetStateOpen && isNear)
            {
                if (!botPlayer.OpenDoor(doorToOpen, interactDistance))
                {
                    botPlayer.LookToPosition(doorPos);
                    //Log.Debug($"Looking towards door interactable");
                }
            }

            if (!isTargetStateOpen || !isNear)
            {
                botPlayer.MoveToPosition(doorPos);
            }
        }

        public void Reset()
        {

        }

        public override string ToString()
        {
            return $"{nameof(OpenNonKeycardInteractableObstacle)}({transformCell})";
        }
    }
}
