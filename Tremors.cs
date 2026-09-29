using System;
using System.Collections.Generic;
using System.Linq;
using HutongGames.PlayMaker;
using UnityEngine;

namespace Apocatremors
{
    // Per-frame logic: driving detection, heat, cooldown, ambush rolls, spawn-spot search, the emerge animation and cleanup.
    internal class Tremors : MonoBehaviour
    {
        private static Tremors _inst;

        private PlayMakerFSM _menu, _peaceful, _saveLoad;
        private readonly List<PlayMakerFSM> _drive = new List<PlayMakerFSM>();
        private float _nextRefScan, _nextDriveScan, _nextSlowTick;

        private float _cooldown = -1f;       // seconds at heat 100 %
        private float _travelKm, _heat;
        private int _bossKills;
        private PlayMakerFSM _distanceFsm;
        private int _retries;
        private readonly List<GameObject> _alive = new List<GameObject>();
        private readonly List<Emerge> _emerging = new List<Emerge>();

        // structures (camps, wrecks, caves, buildings) = children of the MapMagic tiles' object folders that carry FSMs
        private class Poi { public Transform T; public Vector3 LocalCenter; public Vector3 Size; }
        private readonly Dictionary<int, Poi> _poiInfo = new Dictionary<int, Poi>();   // null value = not a structure
        private readonly List<Poi> _pois = new List<Poi>();
        private float _nextPoiScan;

        private const int MaxTries = 14;

        private void Awake() { _inst = this; }

        internal static void ResetForScene()
        {
            if (_inst == null) return;
            _inst._menu = _inst._peaceful = _inst._saveLoad = null;
            _inst._drive.Clear();
            _inst._alive.Clear();
            _inst._emerging.Clear();
            _inst._poiInfo.Clear();
            _inst._pois.Clear();
            _inst._cooldown = -1f;
            _inst._retries = 0;
            _inst._distanceFsm = null;
            _inst._travelKm = _inst._heat = 0f;
            _inst._bossKills = 0;
            Notice.Reset();
            Catalog.Invalidate();
        }

        private void Update()
        {
            try { Tick(); }
            catch (Exception e) { Plugin.Log.LogError("Tick: " + e); _cooldown = 30f; }
        }

        private void Tick()
        {
            float dt = Time.deltaTime;
            Notice.Tick(dt);
            for (int i = _emerging.Count - 1; i >= 0; i--)
                if (_emerging[i].Step(dt)) _emerging.RemoveAt(i);

            if (Time.unscaledTime >= _nextSlowTick) { _nextSlowTick = Time.unscaledTime + 1f; SlowTick(); }
            if (!InGame()) return;
            if (!Catalog.Built) Catalog.Build();

            GameObject car; Rigidbody carRb;
            CurrentCar(out car, out carRb);

            if (Plugin.Pressed(Plugin.TestKey.Value)) { TestSpawn(car, carRb); return; }
            if (!Plugin.Enabled.Value || car == null || carRb == null) return;
            if (Plugin.RespectPeacefulMode.Value && _peaceful != null && _peaceful.enabled) return;

            float kmh = carRb.velocity.magnitude * 3.6f;
            if (kmh < Plugin.MinSpeedKmh.Value || _heat <= 0f) return;     // standing still or at the start: the clock stops
            if (_cooldown < 0f) ResetCooldown();
            _cooldown -= dt * (Plugin.HeatScalesCooldown.Value ? _heat : 1f);
            if (_cooldown > 0f) return;

            Ambush(car.transform.position, Flat(carRb.velocity, car.transform.forward), null, false, kmh);
        }

        private bool InGame()
        {
            if (Time.unscaledTime >= _nextRefScan && (!Alive(_menu) || !Alive(_saveLoad)))
            {
                _nextRefScan = Time.unscaledTime + 2f;
                var gm = GameObject.Find("__GameManager__");
                if (gm != null)
                    foreach (var f in gm.GetComponents<PlayMakerFSM>())
                    {
                        if (f.FsmName == "Menu") _menu = f;
                        else if (f.FsmName == "PeacefulMode") _peaceful = f;
                    }
                var sl = GameObject.Find("SaveLoadGame");
                if (sl != null) foreach (var f in sl.GetComponents<PlayMakerFSM>()) if (f.FsmName == "SaveLoadGame") _saveLoad = f;
            }
            if (Time.timeScale <= 0f) return false;
            if (!Alive(_menu) || !_menu.Fsm.Initialized || _menu.ActiveStateName != "play") return false;
            if (Alive(_saveLoad) && _saveLoad.Fsm.Initialized && _saveLoad.ActiveStateName != "isPlay") return false;
            return true;
        }

        private void CurrentCar(out GameObject car, out Rigidbody rb)
        {
            car = null; rb = null;
            if (Time.unscaledTime >= _nextDriveScan)
            {
                _nextDriveScan = Time.unscaledTime + 5f;
                _drive.Clear();
                foreach (var f in FindObjectsOfType<PlayMakerFSM>())
                    if (f.FsmName == "Drive" && f.gameObject.name == "DriveTrigger") _drive.Add(f);
            }
            foreach (var f in _drive)
            {
                if (!Alive(f) || !f.enabled || !f.Fsm.Initialized || f.ActiveStateName != "inCar") continue;
                var v = f.FsmVariables.GetFsmGameObject("Car");
                car = v != null && v.Value != null ? v.Value : f.transform.root.gameObject;
                rb = car.GetComponent<Rigidbody>() ?? car.GetComponentInParent<Rigidbody>();
                return;
            }
        }

        private void SlowTick()
        {
            UpdateHeat();
            _alive.RemoveAll(g => g == null);
            float d = Plugin.DespawnDistance.Value;
            if (d <= 0f || _alive.Count == 0) return;
            Vector3 p;
            if (!PlayerPos(out p)) return;
            for (int i = _alive.Count - 1; i >= 0; i--)
            {
                var g = _alive[i];
                if ((g.transform.position - p).sqrMagnitude > d * d) { Destroy(g); _alive.RemoveAt(i); }
            }
        }

        // Heat from the game's "Distance Travelled" (__GameManager__ [DistanceTravelled].distance = km from Starter_Area).
        // Boss kills = the game's global Boss_* flags.
        private void UpdateHeat()
        {
            if (_distanceFsm == null || _distanceFsm.gameObject == null)
            {
                _distanceFsm = null;
                var gm = GameObject.Find("__GameManager__");
                if (gm != null) foreach (var f in gm.GetComponents<PlayMakerFSM>()) if (f.FsmName == "DistanceTravelled") { _distanceFsm = f; break; }
            }
            float km = -1f;
            if (_distanceFsm != null && _distanceFsm.Fsm.Initialized)
            {
                var v = _distanceFsm.FsmVariables.GetFsmFloat("distance");
                if (v != null) km = v.Value;
            }
            if (km < 0f)
            {
                var pl = GameObject.Find("Player"); var st = GameObject.Find("Starter_Area");
                if (pl != null && st != null) km = Vector3.Distance(pl.transform.position, st.transform.position) / 1000f;
            }
            if (km < 0f) return;
            _travelKm = km;
            _bossKills = Catalog.BossKills();
            _heat = Mathf.Clamp(km / 10f * Mathf.Max(0f, Plugin.HeatPer10Km.Value), 0f, Mathf.Max(0.01f, Plugin.MaxHeat.Value));
        }

        private static bool PlayerPos(out Vector3 p)
        {
            var pl = GameObject.Find("Player");
            if (pl != null) { p = pl.transform.position; return true; }
            var cam = Camera.main;
            if (cam != null) { p = cam.transform.position; return true; }
            p = Vector3.zero; return false;
        }

        private void ResetCooldown()
        {
            float a = Mathf.Max(1f, Plugin.CooldownMinSeconds.Value), b = Mathf.Max(a, Plugin.CooldownMaxSeconds.Value);
            _cooldown = UnityEngine.Random.Range(a, b);
            _retries = 0;
            Plugin.Verbose("Next ambush roll in " + _cooldown.ToString("0") + " s at heat 100 % (heat now " + (_heat * 100f).ToString("0") + " %)");
        }

        private void TestSpawn(GameObject car, Rigidbody carRb)
        {
            var forced = Catalog.Find(Plugin.TestType.Value);
            if (car != null && carRb != null) { Ambush(car.transform.position, Flat(carRb.velocity, car.transform.forward), forced, true, carRb.velocity.magnitude * 3.6f); return; }
            var cam = Camera.main;
            if (cam == null) return;
            Vector3 origin = cam.transform.position;
            RaycastHit h;
            if (Physics.Raycast(origin, Vector3.down, out h, 10f, ~0, QueryTriggerInteraction.Ignore)) origin = h.point;
            Ambush(origin, Flat(Vector3.zero, cam.transform.forward), forced, true, 0f);
        }

        private void Ambush(Vector3 origin, Vector3 dir, Creature forced, bool test, float kmh)
        {
            _alive.RemoveAll(g => g == null);
            if (!test && _alive.Count + _emerging.Count >= Plugin.MaxAlive.Value) { _cooldown = 20f; return; }
            float km = _travelKm;
            int bk = _bossKills;
            Func<Creature, bool> allowed = x => x.Allowed(kmh, km, bk);
            if (test) allowed = x => x.Chance.Value > 0f;
            var c = forced;
            if (c == null)
            {
                c = Catalog.Roll(allowed);
                if (c == null && test) c = Catalog.Creatures.Where(x => x.Chance.Value > 0f).OrderBy(x => UnityEngine.Random.value).FirstOrDefault()
                                            ?? Catalog.Creatures.FirstOrDefault(x => !x.IsHuman);
            }
            if (c == null) { Plugin.Verbose("Ambush roll: nothing"); ResetCooldown(); return; }

            float r = Mathf.Max(Plugin.ClearRadius.Value, c.Radius + 0.5f);
            Vector3 ground;
            if (!FindSpot(origin, dir, r, c, out ground))
            {
                Plugin.Verbose("No spawn spot for " + c.Key);
                if (!test && ++_retries < 5) _cooldown = 3f;                // try again a little later
                else if (!test) ResetCooldown();
                return;
            }

            int gMin = Mathf.Max(1, c.GroupMin.Value), gMax = Mathf.Max(gMin, c.GroupMax.Value);
            int baseN = UnityEngine.Random.Range(gMin, gMax + 1);
            int n = baseN;
            if (!test && Plugin.HeatScalesGroup.Value) n = Mathf.Max(1, Mathf.FloorToInt(baseN * _heat + 0.5f));
            if (!test) n = Mathf.Min(n, Mathf.Max(1, Plugin.MaxAlive.Value - _alive.Count - _emerging.Count));
            var spots = new List<Vector3> { ground };
            for (int i = 1; i < n; i++)
                for (int k = 0; k < 8; k++)
                {
                    var off = Quaternion.Euler(0f, UnityEngine.Random.Range(0f, 360f), 0f) * Vector3.forward * UnityEngine.Random.Range(2f * r + 1f, 2f * r + 6f);
                    Vector3 g2;
                    if (ValidSpot(ground + off, origin.y, r, out g2) && spots.All(s => (s - g2).sqrMagnitude > 4f * r * r)) { spots.Add(g2); break; }
                }

            Plugin.Log.LogInfo("Ambush: " + spots.Count + "x " + c.Key + ", heat " + (_heat * 100f).ToString("0") + " %");
            Notice.Show((Plugin.NotificationText.Value ?? "")
                .Replace("{plural}", c.Plural.Value).Replace("{name}", c.Name.Value).Replace("{count}", spots.Count.ToString()));
            float delay = 0f;
            foreach (var s in spots)
            {
                var face = Flat(origin - s, Vector3.forward);
                _emerging.Add(new Emerge(this, c, s, Quaternion.LookRotation(face), delay));
                delay += UnityEngine.Random.Range(0.3f, 0.9f);
            }
            if (!test) ResetCooldown();
        }

        private bool FindSpot(Vector3 origin, Vector3 dir, float r, Creature c, out Vector3 ground)
        {
            float dMin = Mathf.Max(5f, c.DistanceMin.Value), dMax = Mathf.Max(dMin, c.DistanceMax.Value);
            float spread = Mathf.Clamp(Plugin.SpreadAngle.Value, 0f, 180f);
            for (int i = 0; i < MaxTries; i++)
            {
                var d = Quaternion.Euler(0f, UnityEngine.Random.Range(-spread, spread), 0f) * dir;
                if (ValidSpot(origin + d * UnityEngine.Random.Range(dMin, dMax), origin.y, r, out ground)) return true;
            }
            ground = Vector3.zero;
            return false;
        }

        // A spot is good when, looking straight down, the first thing hit is open terrain: not a roof, rock, car or prop,
        // not steep, level with the car, flat around, away from structures and with room for the creature.
        private bool ValidSpot(Vector3 p, float refY, float r, out Vector3 ground)
        {
            ground = p;
            RaycastHit hit;
            if (!GroundHit(p, refY, out hit)) return false;
            ground = hit.point;
            if (Vector3.Angle(hit.normal, Vector3.up) > Plugin.MaxSlope.Value) return false;
            if (Mathf.Abs(hit.point.y - refY) > Plugin.MaxHeightDiff.Value) return false;

            for (int k = 0; k < 4; k++)
            {
                var q = hit.point + Quaternion.Euler(0f, k * 90f + 45f, 0f) * Vector3.forward * (r + 0.5f);
                RaycastHit h2;
                if (!GroundHit(q, refY, out h2) || Mathf.Abs(h2.point.y - hit.point.y) > Plugin.FlatTolerance.Value) return false;
            }
            if (NearStructure(hit.point)) return false;

            var a = hit.point + Vector3.up * (r + 0.3f);
            foreach (var col in Physics.OverlapCapsule(a, a + Vector3.up * 2f, r, ~0, QueryTriggerInteraction.Ignore))
                if (!(col is TerrainCollider)) return false;
            return true;
        }

        // true when the first thing straight below p is terrain
        private static bool GroundHit(Vector3 p, float refY, out RaycastHit hit)
        {
            hit = default(RaycastHit);
            var hits = Physics.RaycastAll(new Vector3(p.x, refY + 200f, p.z), Vector3.down, 500f, ~0, QueryTriggerInteraction.Ignore);
            if (hits == null || hits.Length == 0) return false;
            float best = float.MaxValue;
            foreach (var h in hits) if (h.distance < best) { best = h.distance; hit = h; }
            return hit.collider is TerrainCollider;
        }

        private bool NearStructure(Vector3 p)
        {
            if (Time.unscaledTime >= _nextPoiScan) { _nextPoiScan = Time.unscaledTime + 5f; ScanPois(); }
            float buf = Mathf.Max(0f, Plugin.StructureBuffer.Value);
            foreach (var poi in _pois)
            {
                if (poi.T == null) continue;
                var b = new Bounds(poi.T.position + poi.LocalCenter, poi.Size);
                if (b.SqrDistance(p) < buf * buf) return true;
            }
            return false;
        }

        private void ScanPois()
        {
            _pois.Clear();
            var mm = GameObject.Find("MapMagic");
            if (mm != null)
                foreach (Transform tile in mm.transform)
                    foreach (var folder in new[] { "Objects", "LockedObjects" })
                    {
                        var f = tile.Find(folder);
                        if (f == null) continue;
                        foreach (Transform c in f) AddPoi(c);
                    }
            var start = GameObject.Find("Starter_Area");
            if (start != null) AddPoi(start.transform);
        }

        private void AddPoi(Transform c)
        {
            if (!c.gameObject.activeInHierarchy) return;
            Poi poi;
            int id = c.GetInstanceID();
            if (!_poiInfo.TryGetValue(id, out poi))
            {
                poi = null;
                if (c.GetComponentInChildren<PlayMakerFSM>(true) != null)   // rocks carry no FSM; camps/wrecks/caves do
                {
                    bool any = false; var b = new Bounds(c.position, Vector3.zero);
                    foreach (var col in c.GetComponentsInChildren<Collider>())
                        if (!col.isTrigger) { if (any) b.Encapsulate(col.bounds); else { b = col.bounds; any = true; } }
                    if (!any) foreach (var rd in c.GetComponentsInChildren<Renderer>()) { if (any) b.Encapsulate(rd.bounds); else { b = rd.bounds; any = true; } }
                    if (!any) b = new Bounds(c.position, Vector3.one * 10f);
                    poi = new Poi { T = c, LocalCenter = b.center - c.position, Size = b.size };
                }
                _poiInfo[id] = poi;
            }
            if (poi != null) _pois.Add(poi);
        }

        private class Emerge
        {
            private readonly Tremors _owner;
            private readonly Creature _c;
            private Vector3 _ground;
            private readonly Quaternion _rot;
            private float _wait;
            private GameObject _fx, _go;
            private float _remaining, _speed, _burstAt;
            private bool _burstDone;
            private readonly List<PlayMakerFSM> _fsms = new List<PlayMakerFSM>();
            private readonly List<Collider> _cols = new List<Collider>();
            private Rigidbody _rb;
            private bool _wasKinematic;
            private CollisionDetectionMode _cd;

            public Emerge(Tremors owner, Creature c, Vector3 ground, Quaternion rot, float delay)
            {
                _owner = owner; _c = c; _ground = ground; _rot = rot; _wait = delay;
            }

            // true when finished (or failed)
            public bool Step(float dt)
            {
                try { return DoStep(dt); }
                catch (Exception e) { Plugin.Log.LogError("Emerge " + _c.Key + ": " + e); Release(); return true; }
            }

            private int _stage;        // 0 = group delay, 1 = sand burst lead, 2 = rising
            private float _top;

            private bool DoStep(float dt)
            {
                if (_stage == 0)
                {
                    if (_wait > 0f) { _wait -= dt; return false; }
                    _fx = Burst(_ground);
                    _wait = Mathf.Max(0f, Plugin.EffectLeadSeconds.Value);
                    _stage = 1;
                    return false;
                }
                if (_stage == 1)
                {
                    if (_wait > 0f) { _wait -= dt; return false; }
                    if (_fx != null) _ground = _fx.transform.position;   // follows a floating-origin shift
                    if (!Spawn()) return true;
                    _stage = 2;
                    return false;
                }
                if (_go == null) return true;   // destroyed mid-rise

                float step = Mathf.Min(_speed * dt, _remaining);
                _go.transform.position += Vector3.up * step;
                _remaining -= step;
                if (!_burstDone && _remaining <= _burstAt)
                {
                    _burstDone = true;
                    if (Plugin.SurfaceBurst.Value) Burst(_go.transform.position + Vector3.up * _top);
                }
                if (_remaining > 0.0001f) return false;
                Release();
                if (Plugin.RegisterWithGame.Value) Register(_go);
                _owner._alive.Add(_go);
                return true;
            }

            private static GameObject Burst(Vector3 at)
            {
                if (Catalog.Effect == null) return null;
                var fx = UnityEngine.Object.Instantiate(Catalog.Effect, at, Catalog.Effect.transform.rotation);
                fx.SetActive(true);
                return fx;
            }

            private bool Spawn()
            {
                if (_c.Prefab == null) return false;
                _go = UnityEngine.Object.Instantiate(_c.Prefab, _ground + Vector3.down * 50f, _rot * _c.Prefab.transform.rotation);
                _go.SetActive(true);
                // freeze the creature before its FSMs start: they start on the first enabled Start()
                foreach (var f in _go.GetComponentsInChildren<PlayMakerFSM>(true)) if (f.enabled) { f.enabled = false; _fsms.Add(f); }
                foreach (var col in _go.GetComponentsInChildren<Collider>(true)) if (col.enabled) { col.enabled = false; _cols.Add(col); }
                _rb = _go.GetComponent<Rigidbody>();
                if (_rb != null)
                {
                    _wasKinematic = _rb.isKinematic; _cd = _rb.collisionDetectionMode;
                    if (_cd == CollisionDetectionMode.Continuous || _cd == CollisionDetectionMode.ContinuousDynamic)
                        _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
                    _rb.isKinematic = true;
                }
                Name(_go, _c.Prefab.name);

                float top = 2f, bottom = 0f;
                bool any = false; var b = new Bounds();
                foreach (var rd in _go.GetComponentsInChildren<Renderer>())
                {
                    if (rd is ParticleSystemRenderer) continue;
                    if (any) b.Encapsulate(rd.bounds); else { b = rd.bounds; any = true; }
                }
                if (any) { top = b.max.y - _go.transform.position.y; bottom = b.min.y - _go.transform.position.y; }
                top = Mathf.Max(top, bottom + 0.3f);
                _top = top;

                float startY = _ground.y - top - 0.2f;
                float endY = _ground.y - bottom + 0.05f;
                var pos = _go.transform.position; pos.y = startY; pos.x = _ground.x; pos.z = _ground.z;
                _go.transform.position = pos;
                _remaining = Mathf.Max(0.01f, endY - startY);
                _speed = _remaining / Mathf.Max(0.05f, Plugin.RiseSeconds.Value);
                _burstAt = _remaining - (top + 0.2f);   // the moment the top breaks the surface
                if (_burstAt <= 0f) _burstDone = true;
                return true;
            }

            private void Release()
            {
                foreach (var col in _cols) if (col != null) col.enabled = true;
                _cols.Clear();
                if (_rb != null)
                {
                    _rb.isKinematic = _wasKinematic;
                    if (!_wasKinematic) { _rb.collisionDetectionMode = _cd; _rb.velocity = Vector3.zero; _rb.angularVelocity = Vector3.zero; }
                    _rb = null;
                }
                foreach (var f in _fsms) if (f != null) f.enabled = true;
                _fsms.Clear();
            }
        }

        // the game's spawn recipe: bump itemNameID, name "<prefab>(Clone)<n>" ...
        private static void Name(GameObject go, string prefab)
        {
            int id = -1;
            var counterGo = GameObject.Find("itemNameID");
            if (counterGo != null)
                foreach (var f in counterGo.GetComponents<PlayMakerFSM>())
                    if (f.FsmName == "itemNameID")
                    {
                        var v = f.FsmVariables.GetFsmInt("intName");
                        if (v != null) { v.Value += 1; id = v.Value; }
                        break;
                    }
            go.name = prefab + "(Clone)" + (id >= 0 ? id.ToString() : "");
        }

        // ... and add it to NewGO_ArrayList's items list (saved; the game's far-away cleanup handles it like vanilla enemies)
        private static void Register(GameObject go)
        {
            var reg = GameObject.Find("NewGO_ArrayList");
            if (reg == null) return;
            foreach (var p in reg.GetComponents<PlayMakerArrayListProxy>())
                if ((p.referenceName ?? "").ToLowerInvariant().Contains("item")) { p.arrayList.Add(go); return; }
        }

        private static Vector3 Flat(Vector3 v, Vector3 fallback)
        {
            v.y = 0f;
            if (v.sqrMagnitude < 0.25f) { v = fallback; v.y = 0f; }
            if (v.sqrMagnitude < 1e-4f) v = Vector3.forward;
            return v.normalized;
        }

        private static bool Alive(PlayMakerFSM f) { return f != null && f.gameObject != null; }
    }
}
