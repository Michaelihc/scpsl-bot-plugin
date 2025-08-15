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

            public bool IsInteractable(DoorPermissionFlags permissionFlags) =>
                IsInteractable(obstacleBelief.Door, permissionFlags);
        }

        private static bool IsInteractable(DoorVariant door, DoorPermissionFlags permissions)
        {
            return !IsNonIteractable(door) && (door?.RequiredPermissions.CheckPermissions(permissions) ?? false);
        }

        private static bool IsNonIteractable(DoorVariant d)
        {
            return d is DummyDoor or ElevatorDoor or BasicNonInteractableDoor;
        }
    }
}
