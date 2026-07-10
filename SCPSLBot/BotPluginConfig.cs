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

        public WarmupArena DefaultWarmupArena { get; set; } = WarmupArena.SurfacePve;

        public float WarmupArenaSwitchCooldownSeconds { get; set; } = 30f;

        public float SurfacePveBotFactor { get; set; } = 1.2f;

        public int SurfacePveMaxBotCount { get; set; } = 6;

        public int HeavyEntrancePvpveBotCount { get; set; } = 1;

        public int LightContainmentHumanBotCount { get; set; } = 0;

        public int LightContainmentScpBotCount { get; set; } = 1;

        public bool DisableWarheadInWarmup { get; set; } = true;

        public bool DisableLczDecontaminationInWarmup { get; set; } = true;

        public bool DisableDisarmingInWarmup { get; set; } = true;

        public bool DisableScp207HealthDrainInWarmup { get; set; } = true;

        public bool DisableScp330HandLossInWarmup { get; set; } = true;

        public bool LockCheckpointsAndElevatorsInWarmup { get; set; } = true;

        public bool EnableBotInfiniteReserveAmmo { get; set; } = true;

        public bool EnableHumanInfiniteReserveAmmo { get; set; } = true;

        public int BotReserveAmmoTargetMagazines { get; set; } = 2;

        public int BotReserveAmmoHardCap { get; set; } = 200;

        public float BotReserveAmmoTopUpIntervalSeconds { get; set; } = 2f;

        public bool EnableOverflowCleanup { get; set; } = true;

        public int CleanupItemThreshold { get; set; } = 80;

        public float CleanupCheckIntervalSeconds { get; set; } = 10f;

        public bool EnableMapConnectorCompatibilityPatch { get; set; } = false;

        public bool EnableVerboseBotLogs { get; set; } = false;

        public bool EnableEmptyServerAutoRestart { get; set; } = true;

        public float EmptyServerRestartDelaySeconds { get; set; } = 300f;

        public float EmptyServerRestartCheckIntervalSeconds { get; set; } = 30f;

        public float EmptyServerRestartCooldownSeconds { get; set; } = 900f;
    }

    public enum WarmupMode
    {
        None,
        Standard,
    }

    public enum WarmupArena
    {
        SurfacePve,
        HeavyEntrancePvpve,
        LightContainmentScp,
    }
}
