# SCPSLBot Improvement Pass — Implementation Notes

Branch: `improve/bot-fixes-perf-ai` (off `master`). All changes build clean
(`dotnet build SCPSLBot/SCPSLBot.csproj -c Release -p:Platform=x64`, 0 errors).
Driven by an exhaustive multi-agent audit (101 findings, 39 adversarially
confirmed) targeting the owner's three priorities: (1) bugs — stuck/jitter +
map-gen conflict, (2) single-thread performance, (3) AI/combat quality.

Behavioral verification (no-stuck, jitter, combat feel) still needs a live
multiplayer session — the `SCPSLBot.Tests` `nav` command runs in-game against a
live map and is not headless-runnable. Items below marked **[live]** need that.

## Done (committed)

### Batch A — bot-freeze / crash / plugin-conflict bugs
- **Navigator funnel bug** (`FpcBotNavigator.GetNextCorner`): the cross-room
  look-ahead edge lookup used `ForeignConnectedCellEdges[currentCell][nextTargetCell]`
  instead of `[nextTargetCell][aheadTargetCell]`, corrupting string-pulling at room
  boundaries → wrong corner targets / **jitter at doorways**. **[live]**
- **KeyNotFoundException hardening**: all `ForeignConnectedCells[...]` /
  `ForeignConnectedCellEdges[...]` reads now go through safe accessors
  (`TryGetForeignConnectedEdge`, `GetForeignConnectedCells`). A single such throw
  used to abort the bot tick every frame. Elevator path segments (which register a
  connected cell but no edge) are now handled by advancing on cell arrival instead
  of throwing.
- **`BotHub.HandleUpdateException`** no longer nulls `CurrentBotPlayer` on a single
  fault (which permanently disabled a bot for the whole round). Now: rate-limited
  log, abort only the current tick, brief park + auto-rearm only under sustained
  per-tick faulting.
- **Elevator-shaft resolution** (`NavigationSystem`) no longer mutates native
  `RoomIdentifier.RoomsByCoords` (was throwing `ArgumentException` on the 2nd nav
  load and leaking a destroyed-room reference into native global state). Replaced
  with a behavior-equivalent local `GetRoomCellWithin` resolution.
- **`FpcMotorPatches` / `FpcMouseLookPatches`**: removed per-frame reflection
  (`FieldInfo.GetValue` / `MethodInfo.Invoke`) that ran for **every** FPC player
  every frame — now use the public `FpcMotor.Hub`/`MainModule` fields and a cached
  `FieldRef`, with a `BotPlayers.Count == 0` fast path.
- **`LabApiPlugin.Disable`**: `UnpatchAll(Id)` so a direload no longer strips every
  other plugin's Harmony patches.

### Batch B — corner/doorway jitter and stuck-on-geometry
- **`SteerToPosition`**: steer straight at the target rather than walking the body's
  current forward while it slowly rotates (the main corner-drift/oscillation
  source); hold still and rotate first when the target is behind. **[live]**
- **`LookToPosition`**: stopped multiplying the desired move vector by the full
  look-to-target rotation (corrupted combat/SCP-chase movement). **[live]**
- **`FpcLook`**: frame-rate-independent turn smoothing (`1-exp(-k*dt)`); matches the
  old feel at 60 FPS but consistent at any tick rate.
- **`JumpIfForwardMovementBlocked`**: escalating unstick starting ~0.7s — open a
  door ahead → alternating lateral nudge → hop → force navigator re-plan — instead
  of only jumping after 3s. **[live]**
- **Navigator**: distinguishes empty/partial path from "at goal" (`HasPath`) and
  holds position instead of charging walls toward an unreachable goal; off-mesh
  goal resolution guarded against null room / missing local mesh.
- Escape-role bots fall back to `ZoneRoam` when the planner has no runnable action.

### Batch C — door & roaming robustness
- **`DoorEntry.AllPermissionFlags`** fixed (had duplicate Containment L2/L3 and was
  missing Armory L2/L3) so armory doors classify correctly.
- **`DoorObstacle`** prunes stale abandoned-goal entries each sensing pass;
  `GetLastDoor` uses `LastOrDefault` instead of `Last` (which threw).
- **`FpcZoneRoam`** abandons and re-picks a roam target after ~4s of no progress
  (e.g. blocked by an unopenable keycard door) instead of grinding into it. **[live]**

### Batch D — single-thread load & log spam
- **`BotLog`** runtime gate (off by default): all per-tick AI / belief / door /
  item / room diagnostics now skip both the console write and the format-string
  allocation when disabled. Fixes the console-spam that made logs hard to read.
- **Combat**: `HasLineOfSight` replaced `RaycastAll(~0)+Array.Sort` (per candidate,
  per tick, heap+delegate alloc) with a single environment-only `Linecast`; the
  dummy-action list is rebuilt at most once per frame instead of on every
  shoot/reload/ability lookup.
- **Spectator overlay** (`DisplayVisitedActionsGraph`) only builds/sends when a
  player is actually spectating the bot, throttled to ~5 Hz; `Spectators` query
  materialized + time-sliced; hint dedupe state cleared on round restart.
- **`MoveableWithinSightSense`** no longer attaches a per-frame-polling
  `ColliderDataComponent` to shared world item objects (removed the leak + the
  per-frame `bounds.center` poll on every sensed item, server-wide). Centers are
  refreshed on demand only for colliders a live bot is sensing.

## Deferred — need owner decision and/or live verification

- **Map-gen connector rewrite (`RoomConnectorSpawnpointBasePatches`)** — *the #1
  map-gen conflict*. It rewrites EVERY non-standard connector (OpenHallway,
  HczBulkDoor, all Clutter*) to `HczStandardDoor` server-wide so the baked navmesh
  (which only links rooms via `DoorVariant.AllDoors`) has a door at every room
  link. Fixing it properly means either (a) re-baking the navmesh against the real
  connector set, or (b) gating the rewrite off + linking navmesh cells across
  door-less connectors (open hallways) so bots still traverse them. Needs an owner
  decision (see questions) — left untouched to avoid breaking bot navigation.
- **Overflow cleanup (`OverflowCleanupManager`)** runs zero-arg native
  `items/corpses/blood/bulletholes` commands that wipe ALL such objects server-wide
  at the threshold (default on, 80) — destructive and conflict-prone. Options:
  default off, or selective trim (oldest excess pickups only, never corpses/blood).
- **Elevator timeout/abandon** (`CallAndWaitForElevator` / `TravelOnElevator`): no
  timeout, so a locked/contested elevator can trap a bot. A proper fix needs route
  blacklisting + live elevator testing.
- **Perception job pipeline** (PERC-01/02/03): jobs are completed synchronously each
  frame and smuggle managed collections via `GCHandle` (so they are not actually
  Burst/parallel). A real fix (cross-frame scheduling, batched `RaycastCommand`
  across all bots, `Allocator.TempJob`/Persistent) is high-value but high-risk and
  needs live profiling. Also: `SightSense` Persistent NativeArrays + GCHandles are
  not disposed when a bot despawns (native leak across rounds) — worth an
  `IDisposable` pass.
- **Combat balance**: bots are effectively infinite-ammo and aim-bot accurate at
  Hardest (default). Whether to add ammo limits / fire-rate caps is a tuning call.
