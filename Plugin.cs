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
        public const string VERSION = "1.0.0";

        internal static ManualLogSource Log;
        internal static ConfigFile Cfg;

        internal static ConfigEntry<bool> Enabled, ShowNotification;
        internal static ConfigEntry<string> NotificationText, NotificationColor;
        internal static ConfigEntry<float> NotificationSeconds;
        internal static ConfigEntry<float> MinSpeedKmh, CooldownMinSeconds, CooldownMaxSeconds;
        internal static ConfigEntry<int> MaxAlive;
        internal static ConfigEntry<bool> RespectPeacefulMode;
        internal static ConfigEntry<float> HeatPer10Km, MaxHeat;
        internal static ConfigEntry<bool> HeatScalesGroup, HeatScalesCooldown;
        internal static ConfigEntry<float> SpreadAngle, MaxSlope, MaxHeightDiff, FlatTolerance, StructureBuffer, ClearRadius;
        internal static ConfigEntry<float> EffectLeadSeconds, RiseSeconds, DespawnDistance;
        internal static ConfigEntry<bool> RegisterWithGame, SurfaceBurst;
        internal static ConfigEntry<Key> TestKey;
        internal static ConfigEntry<string> TestType, Exclude;
        internal static ConfigEntry<bool> VerboseLog;

        private static GameObject _runner;

        private void Awake()
        {
            Log = Logger;
            Cfg = Config;
            Config.SaveOnConfigSet = false;
            BindGlobals();
            DropOrphans(k => !k.Section.StartsWith(Catalog.SectionPrefix));   // settings from older versions; creature sections are bound later
            new Harmony(GUID).Patch(AccessTools.Method(typeof(ConfigFile), "Save"), postfix: new HarmonyMethod(typeof(Plugin), nameof(AfterSave)));
            Config.Save();
            Config.SaveOnConfigSet = true;

            SceneManager.sceneLoaded += (s, m) => { EnsureRunner(); Tremors.ResetForScene(); };
            EnsureRunner();
            Log.LogInfo(NAME + " " + VERSION + " loaded");
        }

        // Bind order = order in the Apocasetter menu and (via AfterSave) in the file; creature sections follow after [Debug].
        private void BindGlobals()
        {
            Config.Bind("General", "Apocasetter", true, "Show this mod in the Apocasetter Mods menu");
            Enabled = Config.Bind("General", "Enabled", true, "Spawn desert ambushes while driving");
            ShowNotification = Config.Bind("General", "ShowNotification", true,
                "Top-left message (codex-entry style, red) when an ambush starts");
            NotificationText = Config.Bind("General", "NotificationText", "Your engine's roar has roused {plural} nearby.",
                "Message text. {plural} = the creature's plural name, {name} = singular name, {count} = how many");
            NotificationColor = Config.Bind("General", "NotificationColor", "#FF3030", "Message colour (HTML hex)");
            NotificationSeconds = Config.Bind("General", "NotificationSeconds", 4f, new ConfigDescription(
                "How long the message stays (s)", new AcceptableValueRange<float>(0.5f, 30f)));

            MinSpeedKmh = Config.Bind("Trigger", "MinSpeedKmh", 15f, new ConfigDescription(
                "The ambush clock only runs while driving at least this fast (km/h). Each creature also has its own MinCarSpeedKmh",
                new AcceptableValueRange<float>(0f, 150f)));
            CooldownMinSeconds = Config.Bind("Trigger", "CooldownMinSeconds", 90f, new ConfigDescription(
                "Shortest driving time between two ambush rolls at heat 100 % (seconds)", new AcceptableValueRange<float>(1f, 3600f)));
            CooldownMaxSeconds = Config.Bind("Trigger", "CooldownMaxSeconds", 240f, new ConfigDescription(
                "Longest driving time between two ambush rolls at heat 100 % (seconds)", new AcceptableValueRange<float>(1f, 3600f)));
            MaxAlive = Config.Bind("Trigger", "MaxAlive", 6, new ConfigDescription(
                "No new ambush while this many spawned creatures are still alive", new AcceptableValueRange<int>(1, 500)));
            RespectPeacefulMode = Config.Bind("Trigger", "RespectPeacefulMode", true, "No ambushes while the game's peaceful mode is on");

            HeatPer10Km = Config.Bind("Heat", "HeatPer10Km", 0.5f, new ConfigDescription(
                "Heat gained per 10 km of the game's Distance Travelled (distance from the starting area). 0 km = no ambushes; " +
                "default: 50 % at 10 km, 100 % at 20 km, +50 % per further 10 km", new AcceptableValueRange<float>(0f, 5f)));
            MaxHeat = Config.Bind("Heat", "MaxHeat", 3f, new ConfigDescription(
                "Upper limit of the heat multiplier (3 = 300 %)", new AcceptableValueRange<float>(0.1f, 20f)));
            HeatScalesGroup = Config.Bind("Heat", "HeatScalesGroup", true, "Group sizes are multiplied by the heat (at least 1)");
            HeatScalesCooldown = Config.Bind("Heat", "HeatScalesCooldown", true,
                "The ambush clock runs heat times as fast (50 % heat = twice the wait, 200 % = half)");

            SpreadAngle = Config.Bind("Placement", "SpreadAngle", 35f, new ConfigDescription(
                "Max angle left/right of the driving direction (degrees)", new AcceptableValueRange<float>(0f, 180f)));
            MaxSlope = Config.Bind("Placement", "MaxSlope", 25f, new ConfigDescription(
                "Steepest ground a creature may emerge from (degrees) - keeps them off mountainsides", new AcceptableValueRange<float>(1f, 89f)));
            MaxHeightDiff = Config.Bind("Placement", "MaxHeightDiff", 8f, new ConfigDescription(
                "Max height difference between the spot and the car (m) - no ridges or ravines", new AcceptableValueRange<float>(0.5f, 100f)));
            FlatTolerance = Config.Bind("Placement", "FlatTolerance", 1.5f, new ConfigDescription(
                "Max ground height variation around the spot (m)", new AcceptableValueRange<float>(0.1f, 20f)));
            StructureBuffer = Config.Bind("Placement", "StructureBuffer", 40f, new ConfigDescription(
                "Min distance from camps, wrecks, caves and buildings (m)", new AcceptableValueRange<float>(0f, 300f)));
            ClearRadius = Config.Bind("Placement", "ClearRadius", 2f, new ConfigDescription(
                "Free space needed around the spot (m); larger creatures get more", new AcceptableValueRange<float>(0.5f, 20f)));

            EffectLeadSeconds = Config.Bind("Emerge", "EffectLeadSeconds", 0.35f, new ConfigDescription(
                "Sand burst starts this long before the creature appears (s)", new AcceptableValueRange<float>(0f, 5f)));
            RiseSeconds = Config.Bind("Emerge", "RiseSeconds", 1.2f, new ConfigDescription(
                "How long the creature takes to rise out of the ground (s)", new AcceptableValueRange<float>(0.05f, 10f)));
            SurfaceBurst = Config.Bind("Emerge", "SurfaceBurst", true, "Second sand burst (and sound) when the creature breaks the surface");
            RegisterWithGame = Config.Bind("Emerge", "RegisterWithGame", true,
                "Register spawned creatures like vanilla spawns (they are saved, and the game's far-away cleanup applies)");
            DespawnDistance = Config.Bind("Emerge", "DespawnDistance", 300f, new ConfigDescription(
                "Remove spawned creatures farther than this from the player (m, 0 = never)", new AcceptableValueRange<float>(0f, 2000f)));

            TestKey = Config.Bind("Debug", "TestKey", Key.None,
                "Spawn one ambush right now, ignoring speed, cooldown, heat and all creature limits (e.g. F8). None = off");
            TestType = Config.Bind("Debug", "TestType", "", "Prefab name the TestKey spawns (e.g. Burrower). Empty = pick by the creatures' chances");
            Exclude = Config.Bind("Debug", "Exclude", "",
                "Comma-separated prefab names never offered as ambush creatures (traders and friendly NPCs are always left out)");
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
