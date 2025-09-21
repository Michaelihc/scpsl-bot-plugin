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
                IsInteractable(obstacleBelief.Door) && (obstacleBelief.DoorPermissions?.MatchesPermissions(targetPermissionFlags) ?? false);

            private DoorPermissionsPolicy? DoorPermissions => obstacleBelief.DoorInteractableCollider?.Target is DoorVariant targetDoor
                ? targetDoor.RequiredPermissions
                : obstacleBelief.Door?.RequiredPermissions;

            private InteractableCollider DoorInteractableCollider => obstacleBelief.HitResult?.collider.GetComponent<InteractableCollider>();
        }

        const DoorPermissionFlags AllPermissionFlags =
            DoorPermissionFlags.Checkpoints | DoorPermissionFlags.ExitGates | DoorPermissionFlags.Intercom | DoorPermissionFlags.AlphaWarhead |
            DoorPermissionFlags.ContainmentLevelOne | DoorPermissionFlags.ContainmentLevelTwo | DoorPermissionFlags.ContainmentLevelThree |
            DoorPermissionFlags.ArmoryLevelOne | DoorPermissionFlags.ContainmentLevelTwo | DoorPermissionFlags.ContainmentLevelThree;

        private static bool IsInteractable(DoorVariant d) => !IsNonIteractable(d);
        private static bool IsNonIteractable(DoorVariant d)
        {
            return d is DummyDoor or ElevatorDoor or BasicNonInteractableDoor;
        }

        private static bool MatchesPermissions(this DoorPermissionsPolicy policy, DoorPermissionFlags targetPermissionFlags)
        {
            return targetPermissionFlags == (policy.RequiredPermissions & AllPermissionFlags);
        }
    }
}
