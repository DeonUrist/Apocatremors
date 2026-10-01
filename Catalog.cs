using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using HutongGames.PlayMaker;
using UnityEngine;

namespace Apocatremors
{
    internal class Creature
    {
        public string Key;              // prefab name
        public GameObject Prefab;
        public string Group;            // Mutants, Humans, Bosses or Other = config section prefix
        public float Radius = 0.5f;     // from the prefab's capsule colliders
        public float Health;            // prefab Health FSM "Health" (difficulty proxy for the defaults)

        // Spawn rules (fixed since 1.2.0; per-creature config sections before): % chance per roll (0 = never), distance window from the car (m),
        // min car speed (km/h), Distance Travelled window (km, max 0 = none), bosses that must be dead, group size at heat 100 %, names.
        public float Chance, DistanceMin, DistanceMax, MinCarSpeedKmh, MinTravelKm, MaxTravelKm;
        public int GroupMin, GroupMax, MinBossKills;
        public string Name, Plural;

        public bool Allowed(float kmh, float travelKm, int bossKills)
        {
            if (bossKills < MinBossKills) return false;
            if (Chance <= 0f) return false;
            if (kmh < MinCarSpeedKmh) return false;
            if (travelKm < MinTravelKm) return false;
            if (MaxTravelKm > 0f && travelKm > MaxTravelKm) return false;
            return true;
        }
    }

    // Discovers the enemy prefabs the game has loaded, the Burrower_Effect sand burst, and assigns each creature its spawn rules.
    internal static class Catalog
    {
        public static readonly List<Creature> Creatures = new List<Creature>();
        public static GameObject Effect;
        public static bool Built;

        private static readonly string[] Suffixes = { "_BACKUP", "_Sanity", "_Dead", "_cooked", "_Effect" };

        public const string Mutants = "Mutants", Humans = "Humans", Bosses = "Bosses", Other = "Other";   // section order
        private static readonly string[] Groups = { Mutants, Humans, Bosses, Other };

        // The game's own enemies; anything else (e.g. from other mods) goes to [Other: ...] with 0 % chance.
        private static readonly string[] VanillaMutants = { "Arachnid", "Bat", "Blast Rat", "Blast Zombie", "Burrower", "Grimhound", "Juggernaut",
            "Lanky", "Nightwalker", "Rat", "Scorpion_Big", "Scorpion_Small", "Skinwal", "Spider_Big", "Spider_Small", "Teacher", "Wasps",
            "Wild Hound", "Yard Hound", "Zombie_Runner" };
        private static readonly string[] VanillaHumans = { "Boltjaw", "Flexa", "Lugnut", "Scraffa", "Scrud", "Spanna", "Sprokka" };
        private static readonly string[] VanillaBosses = { "Alpha_Nightwalker", "Black_Juggernaut", "Buzzgut", "Duke_Ironjaw", "Professor",
            "Scorpion_King", "Terror_of_the_Night" };

        // Default chances of the mutants that spawn out of the box (all others 0).
        private static readonly Dictionary<string, float> DefaultChance = new Dictionary<string, float>
        {
            { "Burrower", 12f },
            { "Scorpion_Small", 9.44f }, { "Spider_Small", 9.44f },
            { "Wasps", 8.44f }, { "Scorpion_Big", 8.44f }, { "Spider_Big", 8.44f },
            { "Zombie_Runner", 7.44f }, { "Grimhound", 7.44f }, { "Wild Hound", 7.44f },
            { "Nightwalker", 5.44f },
            { "Rat", 6f }, { "Arachnid", 6f }, { "Teacher", 3f }, { "Bat", 2f }, { "Lanky", 2f }, { "Skinwal", 2f }, { "Blast Rat", 1f },
        };

        // Overrides of the health-tier defaults: { MinTravelKm, MinBossKills }.
        private static readonly Dictionary<string, int[]> Unlock = new Dictionary<string, int[]>
        {
            { "Burrower", new[] { 0, 0 } }, { "Lanky", new[] { 10, 2 } }, { "Skinwal", new[] { 15, 3 } }, { "Teacher", new[] { 25, 3 } },
        };

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

            var exclude = new HashSet<string>(Plugin.Exclude, StringComparer.OrdinalIgnoreCase);

            var found = new List<Creature>();
            Effect = null;
            foreach (var kv in byGo)
            {
                var go = kv.Key;
                var names = new HashSet<string>(kv.Value.Select(f => f.FsmName));
                if (go.name == "Burrower_Effect" && names.Contains("DestroySelf")) { Effect = go; continue; }
                if (!names.Contains("Health") || !(names.Contains("Attack") || names.Contains("Detection"))) continue;
                if (go.GetComponent<Rigidbody>() == null) continue;
                if (names.Contains("PlayerIsEnemy")) continue;                   // traders and Coyotes NPCs: friendly until attacked
                if (Suffixes.Any(s => go.name.EndsWith(s, StringComparison.OrdinalIgnoreCase))) continue;
                if (exclude.Contains(go.name)) continue;
                if (found.Any(c => c.Key == go.name)) continue;
                var c2 = new Creature { Key = go.name, Prefab = go };
                c2.Group = VanillaMutants.Contains(go.name) ? Mutants : VanillaHumans.Contains(go.name) ? Humans
                         : VanillaBosses.Contains(go.name) ? Bosses : Other;
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
            found.Sort((a, b) => a.Group != b.Group ? Array.IndexOf(Groups, a.Group) - Array.IndexOf(Groups, b.Group)
                                                    : string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase));

            Assign(found);
            Plugin.Log.LogInfo("Creatures: " + string.Join(", ", Groups.Select(g => Creatures.Count(c => c.Group == g) + " " + g.ToLowerInvariant()).ToArray()) +
                (Effect == null ? " (sand burst effect not found)" : ""));
            Plugin.Verbose("Other: " + string.Join(", ", Creatures.Where(c => c.Group == Other).Select(c => c.Key).ToArray()));
        }

        // ------------------------------------------------------------------ per-creature rules

        // Default difficulty tiers by prefab health (Health FSM "Health"): tougher creatures need more distance travelled and
        // more bosses killed, come in smaller groups, farther away, and only at higher car speed.
        private class Tier
        {
            public float MaxHp, Km, DMin, DMax, Speed; public int Bosses, GMin, GMax;
            public Tier(float maxHp, float km, int bosses, int gMin, int gMax, float dMin, float dMax, float speed)
            { MaxHp = maxHp; Km = km; Bosses = bosses; GMin = gMin; GMax = gMax; DMin = dMin; DMax = dMax; Speed = speed; }
        }

        private static readonly Tier[] Tiers =
        {
            //        max hp        km  bosses group  distance   km/h
            new Tier(15f,            0f, 0, 3, 5,  40f,  80f, 10f),   // rats, small scorpions/spiders, bats
            new Tier(35f,            5f, 0, 2, 4,  45f,  85f, 15f),   // big scorpions/spiders, wasps, blast zombies
            new Tier(60f,           10f, 0, 2, 3,  50f,  90f, 15f),   // runners, hounds, arachnids
            new Tier(100f,          20f, 1, 1, 2,  60f, 100f, 20f),   // nightwalkers, scrapyard gang
            new Tier(300f,          30f, 2, 1, 2,  70f, 110f, 25f),   // flexa, skinwal
            new Tier(float.MaxValue, 40f, 3, 1, 1, 80f, 120f, 30f),   // lanky, juggernaut
        };

        private static void Assign(List<Creature> found)
        {
            var bosses = found.Where(c => c.Group == Bosses).OrderBy(c => c.Health).ThenBy(c => c.Key).ToList();

            Creatures.Clear();
            foreach (var c in found)
            {
                var t = Tiers.First(x => c.Health <= x.MaxHp);
                float chance = 0f, km = t.Km, dMin = t.DMin, dMax = t.DMax, speed = t.Speed;
                int bossKills = t.Bosses, gMin = t.GMin, gMax = t.GMax;
                if (c.Group == Mutants) DefaultChance.TryGetValue(c.Key, out chance);
                int[] u;
                if (Unlock.TryGetValue(c.Key, out u)) { km = u[0]; bossKills = u[1]; }
                if (c.Key == "Burrower") { gMin = 1; gMax = 2; }
                if (c.Group == Bosses)
                {
                    // bosses: off by default; if enabled, only far out and after most of the other bosses are dead
                    int i = bosses.IndexOf(c), n = bosses.Count;
                    bossKills = 3 + (n > 1 ? Mathf.RoundToInt(4f * i / (n - 1)) : 4);        // weakest boss 3 kills ... strongest 7
                    km = 50f + 10f * Mathf.Floor(4f * i / Mathf.Max(1, n - 1)) / 2f;          // 50..70 km
                    gMin = gMax = 1; dMin = 90f; dMax = 130f; speed = 30f;
                }
                string name = Prettify(c.Key);

                c.Chance = chance; c.GroupMin = gMin; c.GroupMax = gMax; c.DistanceMin = dMin; c.DistanceMax = dMax;
                c.MinCarSpeedKmh = speed; c.MinTravelKm = km; c.MaxTravelKm = 0f; c.MinBossKills = bossKills;
                c.Name = name; c.Plural = Pluralize(name);
                Creatures.Add(c);
            }
        }

        // Killed bosses = true global bools Boss_* (Boss_Alpha_Nightwalker, Boss_Black_Juggernaut, Boss_Buzzgut, Boss_Collage_Teacher,
        // Boss_Duke_Ironjaw, Boss_Scorpion_King, Boss_Terror_of_the_Night) - the flags the player sheet's boss list reads.
        public static int BossKills()
        {
            int n = 0;
            try
            {
                foreach (var b in FsmVariables.GlobalVariables.BoolVariables)
                    if (b != null && b.Name != null && b.Name.StartsWith("Boss_") && b.Value) n++;
            }
            catch (Exception) { }
            return n;
        }

        public static float TotalChance(Func<Creature, bool> allowed)
        {
            float t = 0f;
            foreach (var c in Creatures) if (allowed(c)) t += Mathf.Max(0f, c.Chance);
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
                float w = Mathf.Max(0f, c.Chance);
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
            return w + "s";
        }

        private static float MaxAbs(Vector3 v) { return Mathf.Max(Mathf.Abs(v.x), Mathf.Max(Mathf.Abs(v.y), Mathf.Abs(v.z))); }
    }
}
