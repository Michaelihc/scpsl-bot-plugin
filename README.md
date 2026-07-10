# SCPSLBot Warmup Fork

LabAPI plugin for SCP: Secret Laboratory RA dummy bots, warmup rounds, and navmesh-driven combat.

This fork started from repkins' bot addon, but the runtime behavior is now focused on warmup servers:

- Uses server-side RA dummies instead of external network player clients.
- Ships a default navmesh in `SCPSLBot.dll` and installs it on first startup.
- Maintains standard warmup mode with locked rounds, respawns, default roles, bot count control, and disabled round-ending hazards.
- Adds faction-aware combat between players, human-role bots, and SCP-role bots, including firearm use, reloading, strafing, chase memory, surface chase behavior, item pickup support, door/gate handling, and SCP ability attacks.
- Adds a player-facing Server Specific Settings panel through the companion `WarmupPlayerPanel` plugin.
- Adds native item/corpse/blood/bullet-hole cleanup when item overflow is detected.
- Disables SCP-207 health drain during standard warmup by default.

## Build

Set the SCP:SL managed assembly path before building:

```powershell
$env:SL_REFERENCES = 'C:\Program Files (x86)\Steam\steamapps\common\SCP Secret Laboratory Dedicated Server\SCPSL_Data\Managed'
dotnet build SCPSLBot\SCPSLBot.csproj -c Release -p:Platform=x64
```

Build outputs:

- `SCPSLBot\bin\x64\Release\net48\SCPSLBot.dll`
- `SCPSLBot.Components\bin\x64\Release\net48\SCPSLBot.Components.dll`

The LabAPI plugin folder also needs `0Harmony.dll`.

## Install

Copy these files into the LabAPI plugin folder for the target port:

```text
LabAPI/plugins/<port>/SCPSLBot.dll
LabAPI/plugins/<port>/SCPSLBot.Components.dll
LabAPI/plugins/<port>/0Harmony.dll
```

The default navmesh is embedded as `Assets/navmesh.slnmf`. On startup, if this file does not already exist:

```text
LabAPI/plugins/<port>/SCPSLBot/navmesh.slnmf
```

the plugin writes the embedded copy there. Release packages should include this navmesh behavior; fresh installs should not require users to generate a navmesh before bots can patrol.

Do not deploy the old `ScpslPluginStarter.dll` alongside this fork. It contains legacy warmup/player-panel code and can duplicate menus and commands.

## Server Config

Some warmup behavior belongs in SCP:SL server config, not plugin code. Recommended gameplay config values for warmup servers:

```yaml
auto_warhead_start_minutes: 0
dms_enabled: false
stamina_balance_use: 0
spawn_protect_enabled: true
```

Keep spawn protection enabled in the server config. Stamina drain and spawn protection are server settings, not SCPSLBot settings.

`disable_warhead_in_warmup` blocks normal warhead use during standard warmup while still allowing explicit admin starts. The dead man switch should be disabled with `dms_enabled: false`; do not rely on plugin code to fight DMS every tick.

## SCPSLBot Config

Current defaults:

```yaml
default_warmup_mode: Standard
warmup_mode: None
human_respawn_delay_ms: 1200
bot_respawn_delay_ms: 2500
spectator_respawn_delay_ms: 5000
default_respawn_role: NtfPrivate
warmup_bot_count: 3
warmup_bot_role: ChaosRifleman
warmup_human_role: NtfPrivate
disable_warhead_in_warmup: true
disable_lcz_decontamination_in_warmup: true
disable_disarming_in_warmup: true
disable_scp207_health_drain_in_warmup: true
enable_bot_infinite_reserve_ammo: true
enable_human_infinite_reserve_ammo: true
bot_reserve_ammo_target_magazines: 2
bot_reserve_ammo_hard_cap: 200
bot_reserve_ammo_top_up_interval_seconds: 2
enable_overflow_cleanup: true
cleanup_item_threshold: 80
cleanup_check_interval_seconds: 10
enable_verbose_bot_logs: false
enable_empty_server_auto_restart: true
empty_server_restart_delay_seconds: 300
empty_server_restart_check_interval_seconds: 30
empty_server_restart_cooldown_seconds: 900
```

Notes:

- `default_warmup_mode` is applied on plugin startup. This fork defaults to `Standard`.
- In standard warmup, all spectators become `warmup_human_role` after the spectator respawn delay.
- Standard warmup maintains up to `warmup_bot_count` bots, capped at 10.
- Bots default to `warmup_bot_role`, currently `ChaosRifleman`.
- LCZ decontamination and disarming are disabled only in standard warmup and restored when warmup mode is `None`.
- Role changes made by admins should not be reverted once a bot already has a valid role.
- Empty-server auto restart watches for real human players only. It ignores RA dummy bots, waits for a human to have connected at least once, then restarts after the configured empty delay.

## Commands

Remote Admin commands:

- `bot_add` - increases maintained bot count by one, capped at 10.
- `bot_difficulty [easy|normal|hard|hardest]` or `bot_diff` - views or sets combat difficulty.
- `bot_path` or `bot_follow` - toggles bots pathing to your current position, updated once per second.
- `bot_warmup none|standard`, `bot_mode`, `warmup`, or `warmup_mode` - views or sets warmup mode.
- `nav ...` - navmesh editing and save/load commands.

`WarmupPlayerPanel` also adds:

- `panel`, `adminpanel`, or `menu` - opens the player panel when standard warmup is active.

The player-facing panel must not expose warmup mode changes to players. Warmup mode changes are admin-only through RA commands.

## Warmup Flow

Standard warmup:

- Starts/keeps the round running and blocks normal round end.
- Respawns dead players and spectators.
- Maintains configured bot count instead of allowing infinite bot spawning.
- Enables bots only in warmup arenas that currently have connected human players assigned there.
- Spawns players as NTF Private by default.
- Spawns bots as Chaos Rifleman by default.
- Disables LCZ decontamination, disarming, SCP-207 health drain, and non-admin warhead use.
- Uses SCP:SL server config to disable DMS and auto-warhead.
- Uses native cleanup commands when item count exceeds the configured threshold.

Warmup mode `None`:

- Re-enables normal LCZ decontamination behavior.
- Allows normal disarming.
- Does not show the player-facing warmup panel.

## Combat And Navigation

Bots use the current SCPSLBot navmesh for movement. Combat behavior overlays nav instead of replacing it:

- Targeting is faction-aware. Human players, human-role bots, SCP players, and SCP-role bots can all fight enemy factions, including bot-vs-bot and bot-vs-SCP encounters.
- Human-role bots use guns: they can shoot, reload, strafe, pick up items, chase after losing line of sight, and return to patrol/goal when combat ends.
- Strafing scales with difficulty and also triggers briefly after taking damage.
- Door and surface gate handling are part of navigation. Avoid replacing this with direct movement logic.
- Facility bots return to goals or patrol when no target is present.
- Surface bots chase at very long range when another player is on surface; if surface is empty, they stay still.
- Non-escape roles roam their current zone instead of trying to escape.

SCP behavior:

- SCP-role bots target enemy humans and human-role bots.
- SCPs do not use firearm click attacks; they use SCP-specific dummy abilities.
- SCP-939, SCP-3114, SCP-049, SCP-049-2, and SCP-106 use their dummy ability modules for attacks where possible.
- SCP-096 rages before chasing, keeps pursuing during rage even after line of sight is lost, and exits rage after a difficulty-scaled timeout.
- SCP-173 uses blink/teleport behavior and tries farther blink steps when blocked or out of range.

For custom maps or changed room layouts, update:

```text
LabAPI/plugins/<port>/SCPSLBot/navmesh.slnmf
```

## Player Panel Companion

`WarmupPlayerPanel.dll` is a companion plugin from the adjacent plugin workspace. It is not built by this repository.

Panel behavior:

- Available only while SCPSLBot warmup mode is `Standard`.
- Hidden when warmup mode is `None`.
- Rebuilds/refreshes Server Specific Settings when warmup mode or player lists change.
- Provides role, item, teleport, bot count, difficulty, bot target, and bot role controls.
- Uses a 60 second global cooldown for shared bot/warmup actions.
- Uses long 60 second item cooldowns for flashbang, MicroHID, Particle Disruptor, Jailbird, and HE grenade.
- Hides admins/RA users from the player-facing teleport target list only. Admins can still teleport or bring each other through RA tools.

## Companion Plugins

Current warmup deployments may also use companion DLLs from the adjacent plugin workspace:

- `WarmupPlayerPanel.dll` - player-facing Server Specific Settings controls.
- `WarmupSafezone.dll` - warmup safezone behavior.
- `AdminGlobalBroadcast.dll` - admin global text/CASSIE/audio commands.
- Any user-uploaded audio plugin DLLs that are already present in the server plugin folder.

Do not remove unrelated companion DLLs during SCPSLBot deploys.
