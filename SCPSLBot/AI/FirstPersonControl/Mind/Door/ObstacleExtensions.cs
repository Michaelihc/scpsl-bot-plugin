using Interactables;
using Interactables.Interobjects;
using Interactables.Interobjects.DoorUtils;
using SCPSLBot.AI.FirstPersonControl.Mind.Navigation;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Door
{
    internal static class ObstacleExtensions
    {
        extension(Obstacle obstacleBelief)
        {
            public DoorVariant Door => obstacleBelief.HitResult?.collider.GetComponentInParent<DoorVariant>();

            public bool IsInteractable(DoorPermissionFlags targetPermissionFlags) =>
                IsInteractable(obstacleBelief.Door) && (obstacleBelief.DoorPermissions?.CheckPermissions(targetPermissionFlags) ?? false);

            private DoorPermissionsPolicy? DoorPermissions => obstacleBelief.DoorInteractableCollider?.Target is DoorVariant targetDoor
                ? targetDoor.RequiredPermissions
                : obstacleBelief.Door?.RequiredPermissions;

            private InteractableCollider DoorInteractableCollider => obstacleBelief.HitResult?.collider.GetComponent<InteractableCollider>();
        }

        private static bool IsInteractable(DoorVariant d) => !IsNonIteractable(d);
        private static bool IsNonIteractable(DoorVariant d)
        {
            return d is DummyDoor or ElevatorDoor or BasicNonInteractableDoor;
        }
    }
}
