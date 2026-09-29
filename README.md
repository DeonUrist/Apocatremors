# Apocatremors

BepInEx 5 plugin for **Apocalypter**: desert ambushes. While you drive, mutants burst out of the sand ahead of your car —
the game's own sand-worm effect (`Burrower_Effect`: sand burst + sound), then the creature rises out of the ground and its AI takes over.
The farther you get from the starting area, the more often they come and the bigger the groups; tougher creatures only show up far out.

## How it works
- **Heat.** Based on the game's own *Distance Travelled* (km from the starting area, the value the game shows): 0 km = 0 % = no ambushes,
  +`HeatPer10Km` (default 50 %) per 10 km — 50 % at 10 km, 100 % at 20 km, 150 % at 30 km … up to `MaxHeat`.
  Heat multiplies the group size (`HeatScalesGroup`, at least 1) and the speed of the ambush clock (`HeatScalesCooldown`:
  50 % heat = twice the wait, 200 % = half).
- **Clock.** A cooldown (random between `CooldownMinSeconds` and `CooldownMaxSeconds`, at 100 % heat) only runs while you drive faster
  than `MinSpeedKmh`. Standing still or walking never triggers an ambush.
- **Roll.** When it runs out, one creature is picked among those allowed right now (car speed ≥ its `MinCarSpeedKmh`, Distance Travelled
  between its `MinTravelKm` and `MaxTravelKm`). Each creature's `Chance` is its % to be picked; the rest of 100 % = nothing spawns
  (a table above 100 % is scaled down). `GroupMin..GroupMax` × heat of that creature appear together (capped by `MaxAlive`).
- **Spot.** Ahead of the car, the creature's `DistanceMin..DistanceMax` away, within `SpreadAngle` of the driving direction. It must be
  open terrain: looking straight down the first hit is the ground (not a roof, rock, car or prop), slope ≤ `MaxSlope`, height within
  `MaxHeightDiff` of the car, flat within `FlatTolerance`, at least `StructureBuffer` from camps/wrecks/caves/buildings, and
  `ClearRadius` of free space. Up to 14 spots are tried, then it retries a few times a few seconds later.
- **Emerge.** The creature starts under the ground with its FSMs, colliders and physics off, rises in `RiseSeconds`, then is switched on.
  Spawned creatures are named and registered like the game's own spawns (`RegisterWithGame`), so they are saved and the game's
  far-away cleanup applies. The mod also removes its creatures farther than `DespawnDistance`.
- **Notification.** "Your engine's roar has roused Big Scorpions nearby." top left, in the font and size of the game's new-codex-entry
  notice, red (`NotificationColor`). Text template `NotificationText` with `{plural}`, `{name}`, `{count}`; off with `ShowNotification`.
- **Boss kills** = the game's global `Boss_*` flags (the boss list on the player sheet).
- Respects the game's peaceful mode. Shows up in the Apocasetter Mods menu (optional, no dependency).

## Per-creature settings — `[Creature: <prefab>]`
Created on the first game start for every enemy prefab the game has (traders and the `_BACKUP` / `_Sanity` variants are left out):
`Chance`, `GroupMin`/`GroupMax` (at 100 % heat), `DistanceMin`/`DistanceMax` (m from the car), `MinCarSpeedKmh`,
`MinTravelKm`/`MaxTravelKm` (Distance Travelled window, max 0 = none), `MinBossKills` (0–7: how many of the game's 7 bosses must be dead),
`Name`/`Plural` (for the notification). A creature is only picked when all of its limits are met.

Defaults scale with toughness (prefab health) — tougher creatures come later, after boss kills, in smaller groups, farther away, less often:

| Health | Examples | Min km | Bosses | Group | Distance | Min km/h | Chance* |
|---|---|---|---|---|---|---|---|
| ≤ 15 | rats, small scorpions/spiders, bats | 0 | 0 | 3–5 | 40–80 | 10 | 8 |
| ≤ 35 | big scorpions/spiders, wasps, blast zombies | 5 | 0 | 2–4 | 45–85 | 15 | 7 |
| ≤ 60 | zombie runners, hounds, arachnids | 10 | 0 | 2–3 | 50–90 | 15 | 6 |
| ≤ 100 | nightwalkers, the scrapyard gang | 20 | 1 | 1–2 | 60–100 | 20 | 4 |
| ≤ 300 | Flexa, Gunnar, Skinwal | 30 | 2 | 1–2 | 70–110 | 25 | 3 |
| > 300 | Lanky, Juggernaut | 40 | 3 | 1 | 80–120 | 30 | 2 |
| bosses | Scorpion King … Black Juggernaut | 50–70 | 3–7 | 1 | 90–130 | 30 | 0 |

\* Chance only for creatures in the game's own wild mutant/carnivore spawn lists; the rest start at 0 (enable them by giving them a Chance).
The Burrower is the exception: 25 %, from 0 km, groups of 1–2.

## Test key
`TestKey` (default F8) spawns one ambush immediately (driving: ahead of the car; on foot: ahead of the camera), ignoring speed, heat and
travel limits. Set `TestType` to a prefab name (e.g. `Burrower`, `Nightwalker`) to test one creature. `VerboseLog = true` logs every roll,
the heat and why spots were rejected.

## Build
`MANAGED=<game>/Apocalypter_Data/Managed BEPCORE=<game>/BepInEx/core sh build.sh` (mcs), or `dotnet build` with the csproj
(copies the DLL to `BepInEx\plugins`).
