using Interactables.Interobjects;
using Interactables.Interobjects.DoorUtils;
using SCPSLBot.AI.FirstPersonControl.Mind.Navigation;

namespace SCPSLBot.AI.FirstPersonControl.Mind.Door
{
    internal static class ObstacleExtensions
    {
        public static bool IsInteractable(this Obstacle obstacleBelief, DoorPermissionFlags permissionFlags) =>
            IsInteractable(obstacleBelief.GetDoor(), permissionFlags);

        public static DoorVariant GetDoor(this Obstacle obstacleBelief) =>
            obstacleBelief.hitResult?.collider.GetComponentInParent<DoorVariant>();

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
