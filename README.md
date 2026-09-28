# Apocatremors

BepInEx 5 plugin for **Apocalypter**: desert ambushes. While you drive, mutants burst out of the sand ahead of your car —
the game's own sand-worm effect (`Burrower_Effect`: sand burst + sound), then the creature rises out of the ground and its AI takes over.

## How it works
- A cooldown (random between `CooldownMinSeconds` and `CooldownMaxSeconds`) only runs while you drive faster than `MinSpeedKmh`.
  Standing still or walking never triggers an ambush.
- When it runs out, one roll picks a creature from `[Chances]` (each value = % chance; the rest of 100 % = nothing spawns;
  a table above 100 % is scaled down). `GroupMin..GroupMax` of that creature appear together.
- The spot is ahead of the car, `DistanceMin..DistanceMax` away, within `SpreadAngle` of the driving direction. It must be open terrain:
  looking straight down the first hit is the ground (not a roof, rock, car or prop), slope ≤ `MaxSlope`, height within
  `MaxHeightDiff` of the car, flat within `FlatTolerance`, at least `StructureBuffer` from camps/wrecks/caves/buildings, and
  `ClearRadius` of free space. Up to 14 spots are tried, then it retries a few times a few seconds later.
- The creature starts under the ground with its FSMs, colliders and physics off, rises in `RiseSeconds`, then is switched on.
- Spawned creatures are named and registered like the game's own spawns (`RegisterWithGame`), so they are saved and the
  game's far-away cleanup applies. The mod also removes its creatures farther than `DespawnDistance`.
- `[Chances]` is filled on the first game start with every enemy prefab the game has (traders, `_BACKUP` and `_Sanity`
  variants are left out). Defaults: Burrower 30 %, creatures from the game's wild mutant/carnivore spawn lists share ~70 %,
  humans and bosses 0 %.
- Respects the game's peaceful mode. Shows up in the Apocasetter Mods menu (optional, no dependency).

## Test key
`TestKey` (default F8) spawns one ambush immediately (driving: ahead of the car; on foot: ahead of the camera). Set `TestType`
to a prefab name (e.g. `Burrower`, `Nightwalker`) to test one creature. `VerboseLog = true` logs every roll and why spots were rejected.

## Build
`MANAGED=<game>/Apocalypter_Data/Managed BEPCORE=<game>/BepInEx/core sh build.sh` (mcs), or `dotnet build` with the csproj
(copies the DLL to `BepInEx\plugins`).
