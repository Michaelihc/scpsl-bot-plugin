# SCP: Secret Laboratory bot addon

Adds goal-oriented AI controlled players to game server running on Unity Engine.

## Overview

 - GOAP inspired **AI framework featuring strongly-typed states persistance** (contained in belief classes), actions traversal from goal to find conditions-fulfilling action with least total cost using pathfinding algorithm and running it (mind runner class).
 - Multithreaded perception system using **Unity Jobs System**.
 - Custom **mesh-based navigation system**, pathfinding algorithm based on A*, navigation mesh editor using primitives for navmesh visualization.
 - Introduced escaping the facility AI of human players with action graph set up to reach such goal on top on general action graph (opening doors, picking up items etc.)
 - Separate testing project with included tests of navigation system.
 - Separate plugin extensions project which enables loading specified new plugins assemblies, replacing loaded ones without restarting server (with some caveats).

![SCPSLBot visisted actions graph](https://github.com/user-attachments/assets/0e79be02-5586-4aa5-88ff-fec765bf8e18)
*Action finder visited actions debugging graph (in yellow - action path starting from found action to goal)*

[Demo 1](https://www.youtube.com/watch?v=i-J-gKiVs8I), [Demo 2](https://www.youtube.com/watch?v=udBzcIiiYt4)

## Project setup
 - **Requires the following enviroment variables** set up:
   - `SL_REFERENCES`: directory where shipped server managed assemblies located of a game.
   - `SL_APPDATA`: directory where application data is located of server.
 - References the following packages:
   - **IsExternalInit** v1.0.3;
   - **Lib.Harmony** v2.3.6.
 - SDK style project file targeting **.NET Framework 4.8**.
 - **C# 12 language support** enabled.

## Quick start
1. In LocalAdmin use command `bot_add` to add bot player to server while waiting for players.
2. Then connect yourself, assign yourself Overwatch and force start the round.
3. Then spectate the bot player. It should start moving with debug graph visible (debug graph visible is only for Overwatch spectators)

**Heads up**: This addon currently forces to generate door-way connectors of HCZ rooms as navigation system was initially designed to not have varying room connector types.

## Plugin extensions project
Plugin "reloading" caveats:
 - Plugins assemblies can only be disabled and removed from plugin manager, but never actually unloading them because of runtime limitations.
 - Debugging does not work with newly loaded assemblies replacing previous ones with same assembly name.
