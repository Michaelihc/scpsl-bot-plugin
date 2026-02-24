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
        public readonly Vector3 ObstaclePosition;
        private readonly TransformCell transformCell;
        private readonly NavigationBeliefs navigationBeliefs;
        private readonly FpcBotPlayer botPlayer;

        public OpenKeycardDoorObstacle(DoorPermissionFlags permissions, TransformCell transformCell, NavigationBeliefs navigationBeliefs, FpcBotPlayer botPlayer)
        {
            this.Permissions = permissions;
            this.transformCell = transformCell;
            this.navigationBeliefs = navigationBeliefs;
            this.botPlayer = botPlayer;
            this.ObstaclePosition = this.transformCell.MeanPosition + Vector3.up * 2f;
        }

        private Obstacle doorObstacleBelief;
        private ItemInInventory<KeycardWithPermissions> keycardInInventory;
        private const float interactDistance = 2f;

        public void SetEnabledByBeliefs(FpcMind fpcMind)
        {
            keycardInInventory = fpcMind.ActionEnabledBy<ItemInInventory<KeycardWithPermissions>>(this, b => b.Criteria.Equals(new (Permissions)), b => b.Item);

            var navBeliefs = fpcMind.ActionEnabledBy<NavigationBeliefs>(this, b => true);
            fpcMind.ActionEnabledBy<Obstacle>(this, 
                () => navBeliefs.GetReceivedObstacle(doorObstacleBelief.Door.transform.position), 
                b => !(b?.HitResult.HasValue ?? false)
            );

            fpcMind.ActionEnabledBy<NavigationCell>(this, navigationBeliefs.NavigationCells[transformCell], b => doorObstacleBelief.IsNear || b.IsWithin);
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
            var interactablePosition = doorToOpen.transform.position + Vector3.up;

            if (doorToOpen && !doorToOpen.TargetState)
            {
                if (doorObstacleBelief.IsNear)
                {
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
