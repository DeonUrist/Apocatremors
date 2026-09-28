using System;
using System.Collections.Generic;
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
        public string Key;              // prefab name = config key
        public GameObject Prefab;
        public bool IsBoss, InGameLists;
        public ConfigEntry<float> Chance;
        public float Radius = 0.5f;     // from the prefab's capsule colliders
    }

    // Discovers the enemy prefabs the game has loaded, the Burrower_Effect sand burst, and binds one [Chances] entry per creature.
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
                found.Add(c2);
            }
            found.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase));

            // defaults: worm 30 %, the game's mutant/carnivore lists share ~70 %, everything else (humans, bosses) 0
            int nListed = found.Count(c => c.InGameLists && !c.IsBoss && c.Key != "Burrower");
            float share = nListed > 0 ? Mathf.Clamp(Mathf.Floor(70f / nListed), 1f, 8f) : 0f;

            bool old = Plugin.Cfg.SaveOnConfigSet;
            Plugin.Cfg.SaveOnConfigSet = false;
            Creatures.Clear();
            foreach (var c in found)
            {
                float def = c.Key == "Burrower" ? 30f : (c.IsBoss ? 0f : (c.InGameLists ? share : 0f));
                string desc = "% chance per ambush roll" + (c.IsBoss ? " (boss)" : "") + (c.InGameLists ? " (in the game's wild spawn lists)" : "");
                c.Chance = Plugin.Cfg.Bind("Chances", SafeKey(c.Key), def,
                    new ConfigDescription(desc, new AcceptableValueRange<float>(0f, 100f)));
                Creatures.Add(c);
            }
            Plugin.Cfg.SaveOnConfigSet = old;
            Plugin.Cfg.Save();

            var sb = new StringBuilder();
            sb.Append("Catalog: ").Append(Creatures.Count).Append(" creatures, effect=").Append(Effect != null ? Effect.name : "MISSING")
              .Append(", game lists=").Append(listed.Count).Append('\n');
            foreach (var c in Creatures)
                sb.Append("  ").Append(c.Key).Append(" chance=").Append(c.Chance.Value).Append(c.IsBoss ? " boss" : "")
                  .Append(c.InGameLists ? " listed" : "").Append(" r=").Append(c.Radius.ToString("0.0")).Append('\n');
            Plugin.Log.LogInfo(sb.ToString().TrimEnd());
            LogVanillaSpawners();
        }

        public static float TotalChance()
        {
            float t = 0f;
            foreach (var c in Creatures) t += Mathf.Max(0f, c.Chance.Value);
            return t;
        }

        // One ambush roll: each creature's % is its chance to be picked; the rest of 100 % is "nothing".
        // A table that adds up to more than 100 % is scaled down (then something always spawns).
        public static Creature Roll()
        {
            float total = TotalChance();
            float r = UnityEngine.Random.Range(0f, Mathf.Max(100f, total));
            foreach (var c in Creatures)
            {
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

        private static float MaxAbs(Vector3 v) { return Mathf.Max(Mathf.Abs(v.x), Mathf.Max(Mathf.Abs(v.y), Mathf.Abs(v.z))); }

        private static string SafeKey(string s)
        {
            var sb = new StringBuilder();
            foreach (char ch in s) sb.Append("=\n\t\\\"'[]".IndexOf(ch) >= 0 ? '_' : ch);
            return sb.ToString().Trim();
        }

        // Research aid: how does the game itself spawn the worm and the sanity mutants? (CreateObject params)
        private static void LogVanillaSpawners()
        {
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
