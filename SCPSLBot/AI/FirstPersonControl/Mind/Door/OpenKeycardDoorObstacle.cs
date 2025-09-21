using Interactables.Interobjects.DoorUtils;
using SCPSLBot.AI.FirstPersonControl.Mind.Item.Beliefs;
using SCPSLBot.AI.FirstPersonControl.Mind.Item.Keycard;
using SCPSLBot.AI.FirstPersonControl.Mind.Navigation;
using SCPSLBot.Navigation.Mesh;
using UnityEngine;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Door
{
    internal class OpenKeycardDoorObstacle : IAction
    {
        public readonly DoorPermissionFlags Permissions;
        private readonly TransformCell transformCell;
        private readonly NavigationBeliefs navigationBeliefs;
        private readonly FpcBotPlayer botPlayer;

        public OpenKeycardDoorObstacle(DoorPermissionFlags permissions, TransformCell transformCell, NavigationBeliefs navigationBeliefs, FpcBotPlayer botPlayer)
        {
            this.Permissions = permissions;
            this.transformCell = transformCell;
            this.navigationBeliefs = navigationBeliefs;
            this.botPlayer = botPlayer;
        }

        private Obstacle doorObstacleBelief;
        private ItemInInventory<KeycardWithPermissions> keycardInInventory;
        private const float interactDistance = 2f;

        public void SetEnabledByBeliefs(FpcMind fpcMind)
        {
            keycardInInventory = fpcMind.ActionEnabledBy<ItemInInventory<KeycardWithPermissions>>(this, b => b.Criteria.Equals(new (Permissions)), b => b.Item);
            fpcMind.ActionEnabledBy<NavigationCell>(this, navigationBeliefs.NavigationCells[transformCell], b => b.IsWithin);
        }

        public void SetImpactsBeliefs(FpcMind fpcMind)
        {
            doorObstacleBelief = fpcMind.ActionImpacts<Obstacle>(this, this.navigationBeliefs.Obstacles[this.transformCell], b => b.IsInteractable(Permissions));
        }

        public float Cost => 0f;

        public void Tick()
        {
            var keycard = keycardInInventory.Item;
            if (!keycard.IsEquipped)
            {
                keycard.Owner.inventory.ServerSelectItem(keycard.ItemSerial);
            }

            var doorToOpen = doorObstacleBelief.Door;
            var playerPosition = botPlayer.BotHub.PlayerHub.transform.position;
            var interactablePosition = doorToOpen.transform.position + Vector3.up;

            if (doorToOpen && !doorToOpen.TargetState)
            {
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

            botPlayer.MoveToPosition(interactablePosition);
        }

        public void Reset()
        {

        }

        public override string ToString()
        {
            return $"{nameof(OpenKeycardDoorObstacle)}({Permissions}, {transformCell})";
        }
    }
}
