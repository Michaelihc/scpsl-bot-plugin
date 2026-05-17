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

        public bool EnableOverflowCleanup { get; set; } = true;

        public int CleanupItemThreshold { get; set; } = 80;

        public float CleanupCheckIntervalSeconds { get; set; } = 10f;
    }

    public enum WarmupMode
    {
        None,
        Standard,
    }
}
