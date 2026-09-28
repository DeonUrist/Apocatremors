using System;
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
    [BepInPlugin(GUID, NAME, VERSION)]
    public class Plugin : BaseUnityPlugin
    {
        public const string GUID = "com.denis.apocalypter.apocatremors";
        public const string NAME = "Apocatremors";
        public const string VERSION = "0.1.0";

        internal static ManualLogSource Log;
        internal static ConfigFile Cfg;

        // [General]
        internal static ConfigEntry<bool> Enabled, VerboseLog;
        internal static ConfigEntry<Key> TestKey;
        internal static ConfigEntry<string> TestType, Exclude;
        // [Trigger]
        internal static ConfigEntry<float> MinSpeedKmh, CooldownMinSeconds, CooldownMaxSeconds;
        internal static ConfigEntry<int> MaxAlive, GroupMin, GroupMax;
        internal static ConfigEntry<bool> RespectPeacefulMode;
        // [Placement]
        internal static ConfigEntry<float> DistanceMin, DistanceMax, SpreadAngle, MaxSlope, MaxHeightDiff, FlatTolerance,
            StructureBuffer, ClearRadius;
        // [Emerge]
        internal static ConfigEntry<float> EffectLeadSeconds, RiseSeconds, DespawnDistance;
        internal static ConfigEntry<bool> RegisterWithGame, SurfaceBurst;

        private static GameObject _runner;

        private void Awake()
        {
            Log = Logger;
            Cfg = Config;

            Config.Bind("General", "Apocasetter", true, "Show this mod in the Apocasetter Mods menu");
            Enabled = Config.Bind("General", "Enabled", true, "Spawn desert ambushes while driving");
            TestKey = Config.Bind("General", "TestKey", Key.F8,
                "Debug: spawn one ambush right now (ignores speed/cooldown; on foot it spawns ahead of the camera). None = off");
            TestType = Config.Bind("General", "TestType", "",
                "Debug: prefab name the TestKey spawns (e.g. Burrower). Empty = pick by the [Chances] table");
            Exclude = Config.Bind("General", "Exclude", "Merchant,Mechanic,Organic_Mechanic,Professor,Teacher",
                "Comma-separated prefab names never offered as ambush creatures (traders etc.)");
            VerboseLog = Config.Bind("General", "VerboseLog", false, "Log every roll and rejected spawn spot");

            MinSpeedKmh = Config.Bind("Trigger", "MinSpeedKmh", 15f, "The cooldown only runs while driving at least this fast (km/h)");
            CooldownMinSeconds = Config.Bind("Trigger", "CooldownMinSeconds", 90f, "Shortest driving time between two ambush rolls (seconds)");
            CooldownMaxSeconds = Config.Bind("Trigger", "CooldownMaxSeconds", 240f, "Longest driving time between two ambush rolls (seconds)");
            MaxAlive = Config.Bind("Trigger", "MaxAlive", 6, "No new ambush while this many spawned creatures are still alive");
            GroupMin = Config.Bind("Trigger", "GroupMin", 1, "Smallest number of creatures per ambush");
            GroupMax = Config.Bind("Trigger", "GroupMax", 2, "Largest number of creatures per ambush");
            RespectPeacefulMode = Config.Bind("Trigger", "RespectPeacefulMode", true, "No ambushes while the game's peaceful mode is on");

            DistanceMin = Config.Bind("Placement", "DistanceMin", 50f, "Nearest spawn distance from the car (m)");
            DistanceMax = Config.Bind("Placement", "DistanceMax", 90f, "Farthest spawn distance from the car (m)");
            SpreadAngle = Config.Bind("Placement", "SpreadAngle", 35f, "Max angle left/right of the driving direction (degrees)");
            MaxSlope = Config.Bind("Placement", "MaxSlope", 25f, "Steepest ground a creature may emerge from (degrees) - keeps them off mountainsides");
            MaxHeightDiff = Config.Bind("Placement", "MaxHeightDiff", 8f, "Max height difference between the spot and the car (m) - no ridges or ravines");
            FlatTolerance = Config.Bind("Placement", "FlatTolerance", 1.5f, "Max ground height variation around the spot (m)");
            StructureBuffer = Config.Bind("Placement", "StructureBuffer", 40f, "Min distance from camps, wrecks, caves and buildings (m)");
            ClearRadius = Config.Bind("Placement", "ClearRadius", 2f, "Free space needed around the spot (m); larger creatures get more");

            EffectLeadSeconds = Config.Bind("Emerge", "EffectLeadSeconds", 0.35f, "Sand burst starts this long before the creature appears (s)");
            RiseSeconds = Config.Bind("Emerge", "RiseSeconds", 1.2f, "How long the creature takes to rise out of the ground (s)");
            SurfaceBurst = Config.Bind("Emerge", "SurfaceBurst", true, "Second sand burst (and sound) when the creature breaks the surface");
            RegisterWithGame = Config.Bind("Emerge", "RegisterWithGame", true,
                "Register spawned creatures like vanilla spawns (they are saved, and the game's far-away cleanup applies)");
            DespawnDistance = Config.Bind("Emerge", "DespawnDistance", 300f, "Remove spawned creatures farther than this from the player (m, 0 = never)");

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
