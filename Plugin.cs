using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;

namespace Apocatremors
{
    // Desert ambushes: while the player drives, mutants burst out of the sand ahead of the car
    // (the game's own Burrower_Effect sand burst + sound, then the creature rises out of the ground).
    // Per-creature settings live in one [Creature: <prefab>] section each (bound once the game's prefabs are known).
    [BepInPlugin(GUID, NAME, VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public const string GUID = "com.denis.apocalypter.apocatremors";
        public const string NAME = "Apocatremors";
        public const string VERSION = "0.2.0";

        internal static ManualLogSource Log;
        internal static ConfigFile Cfg;

        // [General]
        internal static ConfigEntry<bool> Enabled, VerboseLog;
        internal static ConfigEntry<Key> TestKey;
        internal static ConfigEntry<string> TestType, Exclude;
        // [Trigger]
        internal static ConfigEntry<float> MinSpeedKmh, CooldownMinSeconds, CooldownMaxSeconds;
        internal static ConfigEntry<int> MaxAlive;
        internal static ConfigEntry<bool> RespectPeacefulMode;
        // [Heat]
        internal static ConfigEntry<float> HeatPer10Km, MaxHeat;
        internal static ConfigEntry<bool> HeatScalesGroup, HeatScalesCooldown;
        // [Placement]
        internal static ConfigEntry<float> SpreadAngle, MaxSlope, MaxHeightDiff, FlatTolerance, StructureBuffer, ClearRadius;
        // [Emerge]
        internal static ConfigEntry<float> EffectLeadSeconds, RiseSeconds, DespawnDistance;
        internal static ConfigEntry<bool> RegisterWithGame, SurfaceBurst;
        // [Notification]
        internal static ConfigEntry<bool> ShowNotification;
        internal static ConfigEntry<string> NotificationText, NotificationColor;
        internal static ConfigEntry<float> NotificationSeconds;

        private static GameObject _runner;

        private void Awake()
        {
            Log = Logger;
            Cfg = Config;
            Config.SaveOnConfigSet = false;

            Config.Bind("General", "Apocasetter", true, "Show this mod in the Apocasetter Mods menu");
            Enabled = Config.Bind("General", "Enabled", true, "Spawn desert ambushes while driving");
            TestKey = Config.Bind("General", "TestKey", Key.F8,
                "Debug: spawn one ambush right now (ignores speed, cooldown, heat and travel limits; on foot it spawns ahead of the camera). None = off");
            TestType = Config.Bind("General", "TestType", "",
                "Debug: prefab name the TestKey spawns (e.g. Burrower). Empty = pick by the creatures' chances");
            Exclude = Config.Bind("General", "Exclude", "Merchant,Mechanic,Organic_Mechanic,Professor,Teacher",
                "Comma-separated prefab names never offered as ambush creatures (traders etc.)");
            VerboseLog = Config.Bind("General", "VerboseLog", false, "Log every roll, the heat and why spawn spots were rejected");

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

            ShowNotification = Config.Bind("Notification", "ShowNotification", true,
                "Top-left message (codex-entry style, red) when an ambush starts");
            NotificationText = Config.Bind("Notification", "NotificationText", "Your engine's roar has roused {plural} nearby.",
                "Message text. {plural} = the creature's plural name, {name} = singular name, {count} = how many");
            NotificationColor = Config.Bind("Notification", "NotificationColor", "#FF3030", "Text colour (HTML hex)");
            NotificationSeconds = Config.Bind("Notification", "NotificationSeconds", 4f, new ConfigDescription(
                "How long the message stays (s; the codex entry uses 4)", new AcceptableValueRange<float>(0.5f, 30f)));

            DropOrphans(k => (k.Section == "Trigger" && (k.Key == "GroupMin" || k.Key == "GroupMax")) ||
                             (k.Section == "Placement" && (k.Key == "DistanceMin" || k.Key == "DistanceMax")));   // 0.1.0 globals, now per creature
            Config.Save();
            Config.SaveOnConfigSet = true;

            SceneManager.sceneLoaded += OnSceneLoaded;
            EnsureRunner("Awake");
            Log.LogInfo(NAME + " " + VERSION + " loaded");
        }

        private static void OnSceneLoaded(Scene s, LoadSceneMode m)
        {
            EnsureRunner("scene " + s.name);
            Tremors.ResetForScene();
        }

        // The game destroys the BepInEx plugin object on scene load; keep our logic on a hidden object it can't find.
        private static void EnsureRunner(string why)
        {
            if (_runner != null) return;
            _runner = new GameObject("Apocatremors.Runner") { hideFlags = HideFlags.HideAndDontSave };
            UnityEngine.Object.DontDestroyOnLoad(_runner);
            _runner.AddComponent<Tremors>();
            Log.LogInfo("Runner created (" + why + ")");
        }

        // ---- orphaned entries (values in the .cfg that no longer have a Bind) - used to migrate 0.1.0 settings

        private static Dictionary<ConfigDefinition, string> Orphans()
        {
            var p = typeof(ConfigFile).GetProperty("OrphanedEntries", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            return p != null ? p.GetValue(Cfg, null) as Dictionary<ConfigDefinition, string> : null;
        }

        internal static string TakeOrphan(string section, string key)
        {
            try
            {
                var o = Orphans();
                if (o == null) return null;
                var def = new ConfigDefinition(section, key);
                string v;
                if (o.TryGetValue(def, out v)) { o.Remove(def); return v; }
            }
            catch (Exception e) { Log.LogWarning("Orphan read failed: " + e.Message); }
            return null;
        }

        internal static void DropOrphans(Func<ConfigDefinition, bool> match)
        {
            try
            {
                var o = Orphans();
                if (o == null) return;
                var gone = new List<ConfigDefinition>();
                foreach (var k in o.Keys) if (match(k)) gone.Add(k);
                foreach (var k in gone) o.Remove(k);
                if (gone.Count > 0) Log.LogInfo("Removed " + gone.Count + " obsolete setting(s) from the config file");
            }
            catch (Exception e) { Log.LogWarning("Orphan cleanup failed: " + e.Message); }
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
