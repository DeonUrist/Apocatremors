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
- Respects the game's peaceful mode.

## Per-creature settings
One section per enemy prefab the game has (traders, friendly-until-attacked NPCs and the `_BACKUP` / `_Sanity` variants are left out),
grouped as `[Mutants: <prefab>]`, `[Humans: <prefab>]` (the scrapyard gang and Flexa), `[Bosses: <prefab>]` (the 7 bosses) and
`[Other: <prefab>]` (enemies the mod doesn't know, e.g. from other mods — empty in the vanilla game).
Keys: `Chance`, `GroupMin`/`GroupMax` (at 100 % heat), `DistanceMin`/`DistanceMax` (m from the car), `MinCarSpeedKmh`,
`MinTravelKm`/`MaxTravelKm` (Distance Travelled window, max 0 = none), `MinBossKills` (0–7: how many of the game's 7 bosses must be dead),
`Name`/`Plural` (for the notification). A creature is only picked when all of its limits are met; `Chance` 0 = never.
Humans, bosses and other enemies start at 0 %.

Default chances (the rest of each roll is "nothing"; together they add up to ~106 %, so a roll always spawns something and the values act as weights):

| Mutant | Chance | From km | Boss kills | Group |
|---|---|---|---|---|
| Burrower | 12 | 0 | 0 | 1–2 |
| Small Scorpion, Small Spider | 9.44 | 0 | 0 | 3–5 |
| Wasps, Big Scorpion, Big Spider | 8.44 | 5 | 0 | 2–4 |
| Zombie Runner, Grimhound, Wild Hound | 7.44 | 10 | 0 | 2–3 |
| Rat | 6 | 0 | 0 | 3–5 |
| Arachnid | 6 | 10 | 0 | 2–3 |
| Nightwalker | 5.44 | 20 | 1 | 1–2 |
| Teacher | 3 | 25 | 3 | by health |
| Bat | 2 | 0 | 0 | 3–5 |
| Lanky | 2 | 10 | 2 | 1 |
| Skinwal | 2 | 15 | 3 | 1–2 |
| Blast Rat | 1 | 0 | 0 | 3–5 |
| Blast Zombie, Yard Hound, Juggernaut | 0 | 5 / 10 / 40 | 0 / 0 / 3 | |

The other defaults come from toughness (prefab health): ≤ 15 hp: 40–80 m, 10 km/h · ≤ 35: 45–85 m, 15 km/h · ≤ 60: 50–90 m, 15 km/h ·
≤ 100: 20 km, 1 boss, 1–2, 60–100 m, 20 km/h · ≤ 300: 30 km, 2 bosses, 1–2, 70–110 m, 25 km/h · > 300: 40 km, 3 bosses, 1, 80–120 m, 30 km/h.
Bosses (if enabled): one at a time, 90–130 m, 30 km/h, 50–70 km and 3–7 boss kills by toughness.

## Config
`BepInEx\config\com.denis.apocalypter.apocatremors.cfg` (also editable in game through the Apocasetter Mods menu), in this order:
`[General]` (on/off, notification text/colour/duration), `[Trigger]`, `[Heat]`, `[Placement]`, `[Emerge]`, `[Debug]`, then one
section per creature: `[Mutants: …]`, `[Humans: …]`, `[Bosses: …]`, `[Other: …]`. Settings from older versions are removed from the file automatically.

`[Debug]`: `TestKey` (default None, e.g. F8) spawns one ambush immediately — ahead of the car, or ahead of the camera on foot —
ignoring speed, cooldown, heat and all creature limits; `TestType` forces one prefab (e.g. `Burrower`); `Exclude` lists prefab names
never offered; `VerboseLog` logs every ambush roll and spawn decision.

## Install
Copy `Apocatremors.dll` into `BepInEx\plugins\` (BepInEx 5). Apocasetter is optional.

## Build
`MANAGED=<game>/Apocalypter_Data/Managed BEPCORE=<game>/BepInEx/core sh build.sh` (mcs), or `dotnet build` with the csproj
(copies the DLL to `BepInEx\plugins`).
