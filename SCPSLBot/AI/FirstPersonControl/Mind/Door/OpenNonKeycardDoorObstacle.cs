using Interactables.Interobjects.DoorUtils;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Door
{
    internal class OpenNonKeycardDoorObstacle : IAction
    {
        private DoorObstacle doorObstacleBelief;
        private FpcBotPlayer botPlayer;
        private const float interactDistance = 2f;

        public OpenNonKeycardDoorObstacle(FpcBotPlayer botPlayer)
        {
            this.botPlayer = botPlayer;
        }

        public void SetEnabledByBeliefs(FpcMind fpcMind)
        {
        }

        public void SetImpactsBeliefs(FpcMind fpcMind)
        {
            doorObstacleBelief = fpcMind.ActionImpacts<DoorObstacle, DoorEntry?>(this, s => s!.Value.IsInteractable(DoorPermissionFlags.None));
        }

        public float Cost => 0f;

        public void Tick(FpcMatchProvider matchProvider)
        {
            var doorEntry = matchProvider.Get<DoorObstacle, DoorEntry?>(doorObstacleBelief);
            var doorToOpen = doorEntry!.Value.Door;
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
                var goalPos = doorEntry!.Value.GoalPosition;
                botPlayer.MoveToPosition(goalPos);
            }
        }

        public void Reset()
        {

        }

        public override string ToString()
        {
            return $"{nameof(OpenNonKeycardDoorObstacle)}";
        }
    }
}
