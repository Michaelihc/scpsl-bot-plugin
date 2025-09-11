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
        }

        public void SetImpactsBeliefs(FpcMind fpcMind)
        {
            doorObstacleBelief = fpcMind.ActionImpacts<Obstacle>(this, navigationBeliefs.Obstacles[transformCell], b => b.IsInteractable(DoorPermissionFlags.None));
        }

        public float Cost => 0f;

        public void Tick()
        {
            var doorToOpen = doorObstacleBelief.Door;
            var playerPosition = botPlayer.BotHub.PlayerHub.transform.position;

            if (!doorToOpen)
            {
                Debug.LogWarning($"doorToOpen is null to open");
                return;
            }

            var doorPlane = new Plane(doorToOpen.transform.forward, doorToOpen.transform.position);
            var dist = Mathf.Abs(doorPlane.GetDistanceToPoint(playerPosition));
            var isTargetStateOpen = doorToOpen.TargetState;

            if (!isTargetStateOpen && dist <= interactDistance)
            {
                Debug.Log($"{doorToOpen} is within interactable distance");

                if (!botPlayer.OpenDoor(doorToOpen, interactDistance))
                {
                    botPlayer.LookToPosition(doorToOpen.transform.position + Vector3.up * 1f);
                    //Log.Debug($"Looking towards door interactable");
                }
            }

            if (!isTargetStateOpen || dist > interactDistance)
            {
                var toPos = doorObstacleBelief.ToPos;
                botPlayer.MoveToPosition(toPos);
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
