using PlayerRoles;

namespace SCPSLBot
{
    public sealed class BotPluginConfig
    {
        public WarmupMode DefaultWarmupMode { get; set; } = WarmupMode.Standard;

        public WarmupMode WarmupMode { get; set; } = WarmupMode.None;

        public int HumanRespawnDelayMs { get; set; } = 1200;

        public int BotRespawnDelayMs { get; set; } = 2500;

        public int SpectatorRespawnDelayMs { get; set; } = 5000;

        public RoleTypeId DefaultRespawnRole { get; set; } = RoleTypeId.NtfPrivate;

        public int WarmupBotCount { get; set; } = 3;

        public RoleTypeId WarmupBotRole { get; set; } = RoleTypeId.ChaosRifleman;

        public RoleTypeId WarmupHumanRole { get; set; } = RoleTypeId.NtfPrivate;

        public bool DisableWarheadInWarmup { get; set; } = true;

        public bool DisableLczDecontaminationInWarmup { get; set; } = true;

        public bool DisableDisarmingInWarmup { get; set; } = true;

        public bool DisableScp207HealthDrainInWarmup { get; set; } = true;

        // When true, every non-standard room connector (open hallways, bulk doors, clutter) is
        // forced to an HCZ standard door at map generation so the baked navmesh (which only links
        // rooms that have a door) has a door at every room link. This uniformizes the map and
        // conflicts with map-layout plugins, so it is OFF by default; bots instead get navmesh
        // links built across door-less connectors at load time (see NavigationSystem).
        public bool ForceStandardDoorConnectors { get; set; } = false;
    }

    public enum WarmupMode
    {
        None,
        Standard,
    }
}
