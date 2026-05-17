using PlayerRoles;

namespace SCPSLBot
{
    public sealed class BotPluginConfig
    {
        public WarmupMode WarmupMode { get; set; } = WarmupMode.None;

        public int HumanRespawnDelayMs { get; set; } = 1200;

        public int BotRespawnDelayMs { get; set; } = 2500;

        public RoleTypeId DefaultRespawnRole { get; set; } = RoleTypeId.ClassD;
    }

    public enum WarmupMode
    {
        None,
        Standard,
    }
}
