using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Apocatremors
{
    // Desert ambushes: while the player drives, mutants burst out of the sand ahead of the car.
    [BepInPlugin(GUID, NAME, VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public const string GUID = "com.denis.apocalypter.apocatremors";
        public const string NAME = "Apocatremors";
        public const string VERSION = "1.2.0";

        internal static ManualLogSource Log;
        internal static ConfigFile Cfg;

        // [General] / [Apocapatrol] / [Debug]
        internal static ConfigEntry<bool> Enabled, ShowNotification;
        internal static ConfigEntry<float> CooldownMinSeconds, CooldownMaxSeconds, SkipChance, DistanceMultiplier, DespawnDistance;
        internal static ConfigEntry<int> MaxAlive;
        internal static ConfigEntry<bool> AiCars;
        internal static ConfigEntry<float> PlayerCooldownMultiplier, PlayerSkipChance;
        internal static ConfigEntry<Key> TestKey;
        internal static ConfigEntry<bool> VerboseLog;

        // Fixed tuning (was configurable before 1.2.0; the values are the former defaults as tuned in play)
        internal const string NotificationText = "Your engine's roar has roused {plural} nearby.";
        internal const string NotificationColor = "#FF3030";
        internal const float NotificationSeconds = 4f;
        internal const float MinSpeedKmh = 15f;                 // the ambush roll waits until the car is at least this fast (km/h)
        internal const bool RespectPeacefulMode = true;
        internal const float HeatPer10Km = 0.5f, MaxHeat = 3f;  // 50 % heat at 10 km, 100 % at 20 km, ... up to 300 %
        internal const bool HeatScalesGroup = true, HeatScalesCooldown = true;
        internal const float AiCooldownMultiplier = 2f, AiSkipChance = 50f, AiMaxPlayerDistance = 250f;
        internal const string AiNotificationText = "The roar of an engine nearby has roused {plural}.";
        internal const float SpreadAngle = 35f, MaxSlope = 25f, MaxHeightDiff = 8f, FlatTolerance = 1.5f, StructureBuffer = 40f, ClearRadius = 2f;
        internal const float EffectLeadSeconds = 0.35f, RiseSeconds = 1.2f;
        internal const bool SurfaceBurst = true, RegisterWithGame = true;
        internal const string TestType = "";                     // prefab the TestKey spawns; empty = roll by chance
        internal static readonly string[] Exclude = new string[0];   // prefab names never offered (traders/friendly NPCs are always left out)

        private static GameObject _runner;

        private void Awake()
        {
            Log = Logger;
            Cfg = Config;
            Config.SaveOnConfigSet = false;
            BindGlobals();
            DropOrphans(k => true);   // settings from older versions (incl. the per-creature sections of 1.x)
            new Harmony(GUID).Patch(AccessTools.Method(typeof(ConfigFile), "Save"), postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterSave)));
            Config.Save();
            Config.SaveOnConfigSet = true;

            SceneManager.sceneLoaded += (s, m) => { EnsureRunner(); Tremors.ResetForScene(); };
            EnsureRunner();
            Log.LogInfo(NAME + " " + VERSION + " loaded");
        }

        // Bind order = order in the Apocasetter menu and (via AfterSave) in the file.
        private void BindGlobals()
        {
            Config.Bind("General", "Apocasetter", true, "Show this mod in the Apocasetter Mods menu");
            Enabled = Config.Bind("General", "Enabled", true, "Spawn desert ambushes while driving");
            ShowNotification = Config.Bind("General", "ShowNotification", true,
                "Top-left message (codex-entry style, red) when an ambush starts");
            CooldownMinSeconds = Config.Bind("General", "CooldownMinSeconds", 90f, new ConfigDescription(
                "Shortest time between two ambush rolls at heat 100 % (seconds). The clock also runs on foot; the roll waits until you drive " +
                "at least 15 km/h", new AcceptableValueRange<float>(1f, 3600f)));
            CooldownMaxSeconds = Config.Bind("General", "CooldownMaxSeconds", 1200f, new ConfigDescription(
                "Longest time between two ambush rolls at heat 100 % (seconds)", new AcceptableValueRange<float>(1f, 3600f)));
            SkipChance = Config.Bind("General", "SkipChance", 25f, new ConfigDescription(
                "Percentage of ambush rolls that spawn nothing (the clock just restarts)", new AcceptableValueRange<float>(0f, 100f)));
            DistanceMultiplier = Config.Bind("General", "DistanceMultiplier", 1f, new ConfigDescription(
                "Multiplies every creature's spawn distance from the car (0.1 = ten times closer, 10 = ten times farther)",
                new AcceptableValueRange<float>(0.1f, 10f)));
            DespawnDistance = Config.Bind("General", "DespawnDistance", 300f, new ConfigDescription(
                "Remove spawned creatures farther than this from the player (m, 0 = never)", new AcceptableValueRange<float>(0f, 2000f)));
            MaxAlive = Config.Bind("General", "MaxAlive", 8, new ConfigDescription(
                "No new ambush while this many spawned creatures are still alive", new AcceptableValueRange<int>(1, 500)));

            // Everything in [Apocapatrol] only takes effect while the Apocapatrol plugin is loaded.
            AiCars = Config.Bind("Apocapatrol", "Enabled", false,
                "Apocapatrol's NPC-driven cars rouse ambushes too: creatures emerge ahead of a moving AI car near you, with the same rules " +
                "as for you (heat, creature limits, MaxAlive; their clock runs 2x slower and half their rolls spawn nothing). Only while Apocapatrol is loaded");
            PlayerCooldownMultiplier = Config.Bind("Apocapatrol", "PlayerCooldownMultiplier", 1.5f, new ConfigDescription(
                "While Apocapatrol is loaded, your own ambush clock is this many times longer (its cars bring enemies of their own)",
                new AcceptableValueRange<float>(0.1f, 20f)));
            PlayerSkipChance = Config.Bind("Apocapatrol", "PlayerSkipChance", 25f, new ConfigDescription(
                "While Apocapatrol is loaded, percentage of your ambush rolls that spawn nothing (on top of SkipChance)", new AcceptableValueRange<float>(0f, 100f)));

            TestKey = Config.Bind("Debug", "TestKey", Key.None,
                "Spawn one ambush right now, ignoring speed, cooldown, heat and all creature limits (e.g. F8); needs Enabled. None = off");
            VerboseLog = Config.Bind("Debug", "VerboseLog", false, "Log every ambush roll and spawn decision");
        }

        private static void EnsureRunner()
        {
            // the game destroys the plugin's GameObject on scene load; the logic lives on a hidden object it can't find
            if (_runner != null) return;
            _runner = new GameObject("Apocatremors.Runner") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(_runner);
            _runner.AddComponent<Tremors>();
        }

        // BepInEx keeps values it has no Bind for ("orphans") and writes them back forever; drop the ones that no longer exist.
        internal static void DropOrphans(Func<ConfigDefinition, bool> match)
        {
            try
            {
                var p = typeof(ConfigFile).GetProperty("OrphanedEntries", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                var orphans = p != null ? p.GetValue(Cfg, null) as Dictionary<ConfigDefinition, string> : null;
                if (orphans == null) return;
                foreach (var k in orphans.Keys.Where(match).ToList()) orphans.Remove(k);
            }
            catch (Exception e) { Log.LogWarning("Config cleanup failed: " + e.Message); }
        }

        // BepInEx writes sections alphabetically; rewrite our file with the sections in bind order.
        private static bool _saving;

        private static void AfterSave(ConfigFile __instance)
        {
            if (_saving || __instance != Cfg) return;
            _saving = true;
            try { SortSections(__instance); }
            catch (Exception e) { Log.LogWarning("Config section order: " + e.Message); }
            finally { _saving = false; }
        }

        private static void SortSections(ConfigFile cfg)
        {
            string path = cfg.ConfigFilePath;
            if (!File.Exists(path)) return;
            string text = File.ReadAllText(path);
            var lines = text.Replace("\r\n", "\n").Split('\n');

            var header = new List<string>();
            var blocks = new List<KeyValuePair<string, List<string>>>();
            foreach (var line in lines)
            {
                string t = line.Trim();
                if (t.StartsWith("[") && t.EndsWith("]")) { blocks.Add(new KeyValuePair<string, List<string>>(t.Substring(1, t.Length - 2), new List<string> { line })); continue; }
                if (blocks.Count == 0) header.Add(line); else blocks[blocks.Count - 1].Value.Add(line);
            }

            var order = new List<string>();
            foreach (var k in cfg.Keys) if (!order.Contains(k.Section)) order.Add(k.Section);
            var sorted = blocks.OrderBy(b => { int i = order.IndexOf(b.Key); return i < 0 ? int.MaxValue : i; }).ToList();   // stable

            var sb = new StringBuilder();
            foreach (var l in header) sb.Append(l).Append('\n');
            foreach (var b in sorted) foreach (var l in b.Value) sb.Append(l).Append('\n');
            string result = sb.ToString().TrimEnd('\n') + "\n";
            if (text.Contains("\r\n")) result = result.Replace("\n", "\r\n");
            if (result != text) File.WriteAllText(path, result, new UTF8Encoding(false));
        }

        internal static void Verbose(string msg)
        {
            if (VerboseLog.Value) Log.LogInfo(msg);
        }

        internal static bool Pressed(Key key)
        {
            if (key == Key.None) return false;
            var kb = Keyboard.current;
            if (kb == null) return false;
            try { return kb[key].wasPressedThisFrame; }
            catch (Exception) { return false; }
        }
    }
}
