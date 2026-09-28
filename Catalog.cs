using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using BepInEx.Configuration;
using HutongGames.PlayMaker;
using HutongGames.PlayMaker.Actions;
using UnityEngine;

namespace Apocatremors
{
    internal class Creature
    {
        public string Key;              // prefab name
        public GameObject Prefab;
        public bool IsBoss, InGameLists;
        public float Radius = 0.5f;     // from the prefab's capsule colliders
        public float Health;            // prefab Health FSM "Health" (difficulty proxy for the defaults)

        // [Creature: <Key>]
        public ConfigEntry<float> Chance, DistanceMin, DistanceMax, MinCarSpeedKmh, MinTravelKm, MaxTravelKm;
        public ConfigEntry<int> GroupMin, GroupMax;
        public ConfigEntry<string> Name, Plural;

        public bool Allowed(float kmh, float travelKm)
        {
            if (Chance.Value <= 0f) return false;
            if (kmh < MinCarSpeedKmh.Value) return false;
            if (travelKm < MinTravelKm.Value) return false;
            if (MaxTravelKm.Value > 0f && travelKm > MaxTravelKm.Value) return false;
            return true;
        }
    }

    // Discovers the enemy prefabs the game has loaded, the Burrower_Effect sand burst, and binds one [Creature: X] section per creature.
    internal static class Catalog
    {
        public static readonly List<Creature> Creatures = new List<Creature>();
        public static GameObject Effect;
        public static bool Built;

        private static readonly string[] Suffixes = { "_BACKUP", "_Sanity", "_Dead", "_cooked", "_Effect" };

        public static void Invalidate() { Built = false; }

        public static void Build()
        {
            Built = true;
            var byGo = new Dictionary<GameObject, List<PlayMakerFSM>>();
            foreach (var f in Resources.FindObjectsOfTypeAll<PlayMakerFSM>())
            {
                if (f == null || f.gameObject == null) continue;
                var go = f.gameObject;
                if (go.scene.IsValid()) continue;              // assets only
                if (go.transform.parent != null) continue;     // root prefabs only
                List<PlayMakerFSM> l;
                if (!byGo.TryGetValue(go, out l)) { l = new List<PlayMakerFSM>(); byGo[go] = l; }
                l.Add(f);
            }

            // the game's own creature lists (EnemySpawn_Wild_V2 has EnemySpawnMutant + EnemySpawnCarnivore)
            var listed = new HashSet<string>();
            foreach (var p in Resources.FindObjectsOfTypeAll<PlayMakerArrayListProxy>())
            {
                if (p == null || p.gameObject == null || p.gameObject.scene.IsValid()) continue;
                string rn = p.referenceName ?? "";
                if (rn != "EnemySpawnMutant" && rn != "EnemySpawnCarnivore") continue;
                if (p.preFillGameObjectList == null) continue;
                foreach (var g in p.preFillGameObjectList) if (g != null) listed.Add(g.name);
            }

            var exclude = new HashSet<string>((Plugin.Exclude.Value ?? "").Split(',').Select(s => s.Trim()).Where(s => s.Length > 0),
                StringComparer.OrdinalIgnoreCase);

            var found = new List<Creature>();
            Effect = null;
            foreach (var kv in byGo)
            {
                var go = kv.Key;
                var names = new HashSet<string>(kv.Value.Select(f => f.FsmName));
                if (go.name == "Burrower_Effect" && names.Contains("DestroySelf")) { Effect = go; continue; }
                if (!names.Contains("Health") || !(names.Contains("Attack") || names.Contains("Detection"))) continue;
                if (go.GetComponent<Rigidbody>() == null) continue;
                if (Suffixes.Any(s => go.name.EndsWith(s, StringComparison.OrdinalIgnoreCase))) continue;
                if (exclude.Contains(go.name)) continue;
                if (found.Any(c => c.Key == go.name)) continue;
                var c2 = new Creature { Key = go.name, Prefab = go, IsBoss = names.Contains("BossUI"), InGameLists = listed.Contains(go.name) };
                foreach (var cap in go.GetComponents<CapsuleCollider>())
                    if (!cap.isTrigger) c2.Radius = Mathf.Max(c2.Radius, cap.radius * MaxAbs(go.transform.localScale));
                foreach (var f in kv.Value)
                    if (f.FsmName == "Health")
                    {
                        try { var h = f.FsmVariables.GetFsmFloat("Health"); if (h != null) c2.Health = h.Value; } catch (Exception) { }
                        break;
                    }
                found.Add(c2);
            }
            found.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase));

            BindAll(found);

            var sb = new StringBuilder();
            sb.Append("Catalog: ").Append(Creatures.Count).Append(" creatures, effect=").Append(Effect != null ? Effect.name : "MISSING")
              .Append(", game lists=").Append(listed.Count).Append('\n');
            foreach (var c in Creatures)
                sb.Append("  ").Append(c.Key).Append(" hp=").Append(c.Health.ToString("0.#")).Append(" chance=").Append(c.Chance.Value)
                  .Append(" group=").Append(c.GroupMin.Value).Append('-').Append(c.GroupMax.Value)
                  .Append(" km>=").Append(c.MinTravelKm.Value).Append(c.MaxTravelKm.Value > 0f ? " km<=" + c.MaxTravelKm.Value : "")
                  .Append(c.IsBoss ? " boss" : "").Append(c.InGameLists ? " listed" : "").Append(" \"").Append(c.Plural.Value).Append('"').Append('\n');
            Plugin.Log.LogInfo(sb.ToString().TrimEnd());
            LogVanillaSpawners();
        }

        // ------------------------------------------------------------------ per-creature config

        public static string Section(Creature c) { return "Creature: " + SafeKey(c.Key); }

        private static void BindAll(List<Creature> found)
        {
            // difficulty rank by prefab health among the non-boss creatures (0 = weakest, 1 = strongest)
            var ranked = found.Where(c => !c.IsBoss && c.Key != "Burrower").OrderBy(c => c.Health).ThenBy(c => c.Key).ToList();
            var rank = new Dictionary<Creature, float>();
            for (int i = 0; i < ranked.Count; i++) rank[ranked[i]] = ranked.Count > 1 ? i / (float)(ranked.Count - 1) : 0f;

            int nListed = found.Count(c => c.InGameLists && !c.IsBoss && c.Key != "Burrower");
            float share = nListed > 0 ? Mathf.Clamp(Mathf.Floor(70f / nListed), 1f, 8f) : 0f;

            bool old = Plugin.Cfg.SaveOnConfigSet;
            Plugin.Cfg.SaveOnConfigSet = false;
            Creatures.Clear();
            foreach (var c in found)
            {
                string s = Section(c);
                float p; rank.TryGetValue(c, out p);
                bool worm = c.Key == "Burrower";

                // defaults: worm 30 %, the game's wild mutant/carnivore lists share ~70 %, everything else (humans, bosses) 0;
                // tougher creatures need more distance travelled and come in smaller groups
                float chance = worm ? 30f : (c.IsBoss ? 0f : (c.InGameLists ? share : 0f));
                float migrated;
                string old01 = Plugin.TakeOrphan("Chances", SafeKey(c.Key));
                if (old01 != null && float.TryParse(old01, NumberStyles.Float, CultureInfo.InvariantCulture, out migrated)) chance = Mathf.Clamp(migrated, 0f, 100f);
                float km = worm ? 0f : (c.IsBoss ? 40f : Mathf.Round(p * 6f) * 5f);          // 0..30 km in 5 km steps
                int gMin = worm ? 1 : (c.IsBoss ? 1 : (p < 0.34f ? 2 : 1));
                int gMax = worm ? 2 : (c.IsBoss ? 1 : (p < 0.34f ? 4 : (p < 0.67f ? 3 : 2)));
                string name = Prettify(c.Key);

                c.Chance = Plugin.Cfg.Bind(s, "Chance", chance, new ConfigDescription(
                    "% chance to be picked per ambush roll (the rest of 100 % = nothing)" + (c.IsBoss ? " - boss" : "") +
                    (c.InGameLists ? " - in the game's wild spawn lists" : ""), new AcceptableValueRange<float>(0f, 100f)));
                c.GroupMin = Plugin.Cfg.Bind(s, "GroupMin", gMin, new ConfigDescription(
                    "Smallest group at heat 100 %", new AcceptableValueRange<int>(1, 50)));
                c.GroupMax = Plugin.Cfg.Bind(s, "GroupMax", gMax, new ConfigDescription(
                    "Largest group at heat 100 %", new AcceptableValueRange<int>(1, 50)));
                c.DistanceMin = Plugin.Cfg.Bind(s, "DistanceMin", 50f, new ConfigDescription(
                    "Nearest spawn distance from the car (m)", new AcceptableValueRange<float>(5f, 500f)));
                c.DistanceMax = Plugin.Cfg.Bind(s, "DistanceMax", 90f, new ConfigDescription(
                    "Farthest spawn distance from the car (m)", new AcceptableValueRange<float>(5f, 500f)));
                c.MinCarSpeedKmh = Plugin.Cfg.Bind(s, "MinCarSpeedKmh", 15f, new ConfigDescription(
                    "Only picked while the car is at least this fast (km/h)", new AcceptableValueRange<float>(0f, 150f)));
                c.MinTravelKm = Plugin.Cfg.Bind(s, "MinTravelKm", km, new ConfigDescription(
                    "Only picked once the game's Distance Travelled is at least this far (km)", new AcceptableValueRange<float>(0f, 200f)));
                c.MaxTravelKm = Plugin.Cfg.Bind(s, "MaxTravelKm", 0f, new ConfigDescription(
                    "No longer picked beyond this Distance Travelled (km, 0 = no limit)", new AcceptableValueRange<float>(0f, 200f)));
                c.Name = Plugin.Cfg.Bind(s, "Name", name, "Name used in the notification ({name})");
                c.Plural = Plugin.Cfg.Bind(s, "Plural", Pluralize(name), "Plural name used in the notification ({plural})");
                Creatures.Add(c);
            }
            Plugin.DropOrphans(k => k.Section == "Chances");   // 0.1.0 table, migrated into the creature sections above
            Plugin.Cfg.SaveOnConfigSet = old;
            Plugin.Cfg.Save();
        }

        public static float TotalChance(Func<Creature, bool> allowed)
        {
            float t = 0f;
            foreach (var c in Creatures) if (allowed(c)) t += Mathf.Max(0f, c.Chance.Value);
            return t;
        }

        // One ambush roll among the allowed creatures: each creature's % is its chance to be picked; the rest of 100 % is "nothing".
        // A table that adds up to more than 100 % is scaled down (then something always spawns).
        public static Creature Roll(Func<Creature, bool> allowed)
        {
            float total = TotalChance(allowed);
            float r = UnityEngine.Random.Range(0f, Mathf.Max(100f, total));
            foreach (var c in Creatures)
            {
                if (!allowed(c)) continue;
                float w = Mathf.Max(0f, c.Chance.Value);
                if (w <= 0f) continue;
                if (r < w) return c;
                r -= w;
            }
            return null;
        }

        public static Creature Find(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;
            return Creatures.FirstOrDefault(c => string.Equals(c.Key, key.Trim(), StringComparison.OrdinalIgnoreCase));
        }

        // ------------------------------------------------------------------ names

        // "Scorpion_Big" -> "Big Scorpion", "Zombie_Runner" -> "Zombie Runner", "Terror_of_the_Night" -> "Terror of the Night"
        internal static string Prettify(string key)
        {
            var words = key.Replace('_', ' ').Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();
            if (words.Count == 2 && (words[1] == "Big" || words[1] == "Small")) words = new List<string> { words[1], words[0] };
            return string.Join(" ", words.ToArray());
        }

        // English plural of the head noun: "Big Scorpion" -> "Big Scorpions", "Terror of the Night" -> "Terrors of the Night",
        // "Blast Zombie" -> "Blast Zombies", "Wasps" stays
        internal static string Pluralize(string name)
        {
            if (string.IsNullOrEmpty(name)) return name;
            int of = name.IndexOf(" of ", StringComparison.Ordinal);
            if (of > 0) return Pluralize(name.Substring(0, of)) + name.Substring(of);
            int sp = name.LastIndexOf(' ');
            string head = sp >= 0 ? name.Substring(sp + 1) : name, pre = sp >= 0 ? name.Substring(0, sp + 1) : "";
            return pre + PluralWord(head);
        }

        private static string PluralWord(string w)
        {
            if (w.Length < 2) return w + "s";
            string l = w.ToLowerInvariant();
            if (l.EndsWith("s") && !l.EndsWith("ss")) return w;                      // already plural (Wasps)
            if (l.EndsWith("ss") || l.EndsWith("x") || l.EndsWith("z") || l.EndsWith("ch") || l.EndsWith("sh")) return w + "es";
            if (l.EndsWith("y") && "aeiou".IndexOf(l[l.Length - 2]) < 0) return w.Substring(0, w.Length - 1) + "ies";
            if (l == "wolf") return w.Substring(0, w.Length - 1) + "ves";
            return w + "s";
        }

        private static float MaxAbs(Vector3 v) { return Mathf.Max(Mathf.Abs(v.x), Mathf.Max(Mathf.Abs(v.y), Mathf.Abs(v.z))); }

        internal static string SafeKey(string s)
        {
            var sb = new StringBuilder();
            foreach (char ch in s) sb.Append("=\n\t\\\"'[]".IndexOf(ch) >= 0 ? '_' : ch);
            return sb.ToString().Trim();
        }

        // Research aid: how does the game itself spawn the worm and the sanity mutants? (CreateObject params)
        private static void LogVanillaSpawners()
        {
            if (!Plugin.VerboseLog.Value) return;
            try
            {
                foreach (var f in Resources.FindObjectsOfTypeAll<PlayMakerFSM>())
                {
                    if (f == null || f.gameObject == null) continue;
                    bool worm = f.gameObject.name == "EnemySpawn_Burrower" && !f.gameObject.scene.IsValid() && f.FsmName == "ItemSpawner";
                    bool sanity = f.gameObject.name == "__GameManager__" && f.FsmName == "SanityMutantSpawn";
                    if (!worm && !sanity) continue;
                    foreach (var st in f.FsmStates)
                    {
                        if (st == null || !st.Name.StartsWith("spawn")) continue;
                        var acts = st.Actions;
                        if (acts == null || acts.Length == 0) { st.LoadActions(); acts = st.Actions; }
                        if (acts == null) continue;
                        foreach (var a in acts)
                        {
                            var co = a as CreateObject;
                            if (co == null) continue;
                            Plugin.Log.LogInfo(string.Format("[vanilla] {0}[{1}] {2}: CreateObject go={3} spawnPoint={4} pos={5} rot={6} parent={7}",
                                f.gameObject.name, f.FsmName, st.Name, Describe(co.gameObject), Describe(co.spawnPoint),
                                co.position != null ? (co.position.IsNone ? "none" : co.position.Value.ToString()) : "null",
                                co.rotation != null ? (co.rotation.IsNone ? "none" : co.rotation.Value.ToString()) : "null",
                                Describe(co.parent)));
                        }
                    }
                    if (worm) break;
                }
            }
            catch (Exception e) { Plugin.Log.LogWarning("Vanilla spawner dump failed: " + e.Message); }
        }

        private static string Describe(FsmGameObject v)
        {
            if (v == null) return "null";
            if (v.IsNone) return "none";
            string n = v.Value != null ? v.Value.name : "null";
            return string.IsNullOrEmpty(v.Name) ? n : "{" + v.Name + "}" + n;
        }
    }
}
