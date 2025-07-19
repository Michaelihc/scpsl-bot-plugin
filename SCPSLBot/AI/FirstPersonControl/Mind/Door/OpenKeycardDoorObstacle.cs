using Interactables.Interobjects.DoorUtils;
using SCPSLBot.AI.FirstPersonControl.Mind.Item.Beliefs;
using SCPSLBot.AI.FirstPersonControl.Mind.Item.Keycard;
using SCPSLBot.AI.FirstPersonControl.Mind.Navigation;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Door
{
    internal class OpenKeycardDoorObstacle : IAction
    {
        public readonly DoorPermissionFlags Permissions;
        public OpenKeycardDoorObstacle(DoorPermissionFlags permissions, FpcBotPlayer botPlayer)
        {
            this.Permissions = permissions;
            this.botPlayer = botPlayer;
        }

        private Obstacle doorObstacleBelief;
        private ItemInInventory<KeycardWithPermissions> keycardInInventory;
        private readonly FpcBotPlayer botPlayer;
        private const float interactDistance = 2f;

        public void SetEnabledByBeliefs(FpcMind fpcMind)
        {
            keycardInInventory = fpcMind.ActionEnabledBy<ItemInInventory<KeycardWithPermissions>>(this, b => b.Criteria.Equals(new (Permissions)), b => b.Item);
        }

        public void SetImpactsBeliefs(FpcMind fpcMind)
        {
            doorObstacleBelief = fpcMind.ActionImpacts<Obstacle>(this, b => b.IsInteractable(Permissions));
        }

        public float Cost => 0f;

        public void Tick(FpcMatchProvider matchProvider)
        {
            var keycard = keycardInInventory.Item;
            if (!keycard.IsEquipped)
            {
                keycard.Owner.inventory.ServerSelectItem(keycard.ItemSerial);
            }

            var doorToOpen = doorObstacleBelief.GetDoor();
            var playerPosition = botPlayer.BotHub.PlayerHub.transform.position;

            if (doorToOpen && !doorToOpen.TargetState)
            {
                var interactablePosition = doorToOpen.transform.position + Vector3.up;

                if (Vector3.Distance(interactablePosition, playerPosition) <= interactDistance)
                {
                    Debug.Log($"{doorToOpen} is within interactable distance");

                    if (!botPlayer.OpenDoor(doorToOpen, interactDistance))
                    {
                        botPlayer.LookToPosition(interactablePosition);
                        //Log.Debug($"Looking towards door interactable");
                    }
                }
            }

            var toPos = doorObstacleBelief.ToPos;
            botPlayer.MoveToPosition(toPos);
        }

        public void Reset()
        {

        }

        public override string ToString()
        {
            return $"{nameof(OpenKeycardDoorObstacle)}({Permissions})";
        }
    }
}
