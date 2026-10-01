# Apocatremors

BepInEx 5 plugin for **Apocalypter**: desert ambushes. While you drive, mutants burst out of the sand ahead of your car —
the game's own sand-worm effect (`Burrower_Effect`: sand burst + sound), then the creature rises out of the ground and its AI takes over.
The farther you get from the starting area, the more often they come and the bigger the groups; tougher creatures only show up far out.

## How it works
- **Heat.** Based on the game's own *Distance Travelled* (km from the starting area, the value the game shows): 0 km = 0 % = no ambushes,
  +50 % per 10 km — 50 % at 10 km, 100 % at 20 km, 150 % at 30 km … up to 300 %.
  Heat multiplies the group size (at least 1) and the speed of the ambush clock (50 % heat = twice the wait, 200 % = half).
- **Clock.** A cooldown (random between `CooldownMinSeconds` and `CooldownMaxSeconds`, at 100 % heat) runs all the time you play,
  on foot too. Once it is out, the ambush waits until you drive faster than 15 km/h — so after a long stretch on foot it comes as soon as
  you drive off. Standing still or walking never triggers one. `SkipChance` % of the rolls spawn nothing and just restart the clock.
- **Roll.** One creature is picked among those allowed right now (car speed ≥ its min speed, Distance Travelled within its window,
  enough bosses dead). Each creature's chance is its % to be picked; the rest of 100 % = nothing spawns (a table above 100 % is scaled
  down). The creature's group size × heat appear together (capped by `MaxAlive`).
- **Spot.** Ahead of the car, the creature's spawn distance away (times `DistanceMultiplier`), within 35° of the driving direction. It must be
  open terrain: looking straight down the first hit is the ground (not a roof, rock, car or prop), slope ≤ 25°, within 8 m of the car's
  height, flat within 1.5 m, at least 40 m from camps/wrecks/caves/buildings, and 2 m of free space. Up to 14 spots are tried, then it
  retries a few times a few seconds later.
- **Emerge.** The creature starts under the ground with its FSMs, colliders and physics off, rises in 1.2 s, then is switched on.
  Spawned creatures are named and registered like the game's own spawns, so they are saved and the game's far-away cleanup applies.
  The mod also removes its creatures farther than `DespawnDistance`.
- **Notification.** "Your engine's roar has roused Big Scorpions nearby." top left, in the font and size of the game's new-codex-entry
  notice, red; off with `ShowNotification`.
- **Boss kills** = the game's global `Boss_*` flags (the boss list on the player sheet).
- Respects the game's peaceful mode.
- **Apocapatrol** (NPC-driven cars, `[Apocapatrol]`, only while that plugin is loaded). Its cars bring enemies of their own, so your
  ambush clock runs `PlayerCooldownMultiplier` times slower (default 1.5) and `PlayerSkipChance` % of your rolls (default 25) spawn nothing
  on top of `SkipChance`. With `[Apocapatrol] Enabled` (default off) the AI cars rouse ambushes too: each moving AI car within 250 m of you
  has its own clock (2 × the normal cooldown) and half of its rolls spawn nothing; creatures emerge ahead of that car under the same rules
  (heat, creature limits, `MaxAlive` shared with your own ambushes) and "The roar of an engine nearby has roused …" is shown.
  A car you took over counts as yours.

## Creatures
Every enemy prefab the game has is known (traders, friendly-until-attacked NPCs and the `_BACKUP` / `_Sanity` variants are left out),
grouped as Mutants, Humans (the scrapyard gang and Flexa), Bosses (the 7) and Other (enemies from other mods). Each has a chance per roll,
a group size (at 100 % heat), a spawn distance window, a minimum car speed, a Distance Travelled window and a number of bosses that must be
dead; a creature is only picked when all of its limits are met. Only mutants spawn: humans, bosses and other enemies have 0 % chance.
(Up to 1.1.1 these were config sections; since 1.2.0 they are fixed in `Catalog.cs`.)

Chances (together they add up to ~106 %, so a roll that isn't skipped always spawns something and the values act as weights):

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

The other rules come from toughness (prefab health): ≤ 15 hp: 40–80 m, 10 km/h · ≤ 35: 45–85 m, 15 km/h · ≤ 60: 50–90 m, 15 km/h ·
≤ 100: 20 km, 1 boss, 1–2, 60–100 m, 20 km/h · ≤ 300: 30 km, 2 bosses, 1–2, 70–110 m, 25 km/h · > 300: 40 km, 3 bosses, 1, 80–120 m, 30 km/h.
Bosses (0 %): one at a time, 90–130 m, 30 km/h, 50–70 km and 3–7 boss kills by toughness.

## Config
`BepInEx\config\com.denis.apocalypter.apocatremors.cfg` (also editable in game through the Apocasetter Mods menu):

- `[General]`: `Enabled`, `ShowNotification`, `CooldownMinSeconds` (90), `CooldownMaxSeconds` (1200), `SkipChance` (25 %),
  `DistanceMultiplier` (1), `DespawnDistance` (300 m), `MaxAlive` (8).
- `[Apocapatrol]`: `Enabled` (AI cars rouse ambushes, off), `PlayerCooldownMultiplier` (1.5), `PlayerSkipChance` (25 %).
- `[Debug]`: `TestKey` (default None, e.g. F8) spawns one ambush immediately — ahead of the car, or ahead of the camera on foot —
  ignoring speed, cooldown, heat and all creature limits (but not `Enabled`); `VerboseLog` logs every ambush roll and spawn decision.

Settings from older versions (the `[Trigger]`/`[Heat]`/`[Placement]`/`[Emerge]` sections and the per-creature sections of 1.x) are removed
from the file automatically; their values are now fixed in the code.

## Install
Copy `Apocatremors.dll` into `BepInEx\plugins\` (BepInEx 5). Apocasetter is optional.

## Build
`MANAGED=<game>/Apocalypter_Data/Managed BEPCORE=<game>/BepInEx/core sh build.sh` (mcs), or `dotnet build` with the csproj
(copies the DLL to `BepInEx\plugins`).
