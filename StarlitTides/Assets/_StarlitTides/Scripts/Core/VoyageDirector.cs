// VoyageDirector: a run is a journey out to sea through six zones (Phase 9).
//
//   1 Lantern Bay     moonlit bay, rocks and logs, friendly bow-riding dolphins
//   2 Glow Reef       emerald glowing sea, moon-jelly blooms, sea turtles
//   3 Sunset Channel  sunset, dolphin pods porpoising, manta rays
//   4 Orca Strait     icy sea under the aurora, orca hunts and wave-washing
//   5 Storm Reach     storm, humpback bubble-net lunges, lion's mane jellies
//   6 Aurora Deep     everything, mixed
//   then Voyage 2, 3... (zones 2-6 again, faster each loop)
//
// Each zone sets:
//   - the gameplay numbers (tide speed, light drain, orb value and spacing,
//     filler rocks/logs) that DifficultyManager hands to the spawners
//   - which creature encounters may happen (EncounterDirector)
//   - the look: fog, ambient light, moonlight, sunset/aurora/storm, the sea's
//     colours, and the scenery set (AmbientSeaField)
// A short REST opens every zone (no new hazards) with a banner naming it.
// After Lantern Bay the coast drifts away behind the boat and sinks below the
// horizon, so the bay really is left behind. Everything resets on the menu.

using System;
using System.Collections.Generic;
using UnityEngine;

namespace StarlitTides
{
    public enum EncounterKind
    {
        BowRiders, JellyBloom, LionsMane, TurtleSurfacing, DolphinPod, MantaGlide,
        OrcaHunt, OrcaWaveWash, HumpbackLunge, GiantWhale
    }

    [DefaultExecutionOrder(-40)]
    public class VoyageDirector : MonoBehaviour
    {
        public static VoyageDirector Instance { get; private set; }

        /// <summary>Raised when a new zone begins (the zone, and the voyage number from 1).</summary>
        public static event Action<Zone, int> OnZoneStarted;

        [Serializable]
        public class EncounterWeight
        {
            public EncounterKind kind;
            public float weight = 1f;
            public EncounterWeight() { }
            public EncounterWeight(EncounterKind k, float w) { kind = k; weight = w; }
        }

        [Serializable]
        public class Zone
        {
            public string name = "Zone";
            [Tooltip("Shown under the zone name on the banner.")]
            public string subtitle = "";
            public float duration = 50f;

            [Header("Gameplay")]
            public float tideSpeed = 8f;
            public float drainPerSecond = 0.04f;
            [Tooltip("Light each orb gives back (0.1 = 10% of the meter).")]
            public float orbValue = 0.1f;
            public float orbGroupInterval = 4.5f;
            public Vector2Int orbsPerGroup = new Vector2Int(4, 5);
            [Tooltip("Seconds between filler rocks/logs.")]
            public float fillerInterval = 1f;
            [Range(0f, 1f)] public float logChance = 0.25f;
            [Tooltip("Chance a filler spawn is a single drifting jellyfish.")]
            [Range(0f, 1f)] public float jellyChance = 0f;
            [Tooltip("Creature encounters that can happen here. The FIRST one opens the zone (teach, then test).")]
            public EncounterWeight[] encounters = new EncounterWeight[0];
            [Tooltip("Seconds of calm between two encounters.")]
            public float encounterGap = 6f;
            public bool hiddenOrbs;

            [Header("Look")]
            [Tooltip("Off = the bay's own night look and sea colours.")]
            public bool customLook;
            public SkyController.AtmospherePreset atmosphere;
            public Weather weather = Weather.Calm;
            [Range(0f, 1f)] public float sunset;
            [Range(0f, 1f)] public float ambientAurora = 0.15f;
            public Color seaShallow, seaDeep, seaHorizon, seaNear;
            [ColorUsage(false, true)] public Color seaFoam, seaBio, seaBio2;
            [Tooltip("Index of the scenery set in AmbientSeaField.zoneSets (-1 = the default bands).")]
            public int scenerySet = -1;
        }

        public Zone[] zones = DefaultZones();
        [Tooltip("Calm seconds at the start of each zone (after the first).")]
        public float restDuration = 5f;
        [Tooltip("Zone the voyage loops back to after the last one.")]
        public int loopToZone = 1;
        public float loopTideBonus = 2f;
        public float maxTide = 24f;
        [Tooltip("Seconds the look takes to blend into the next zone.")]
        public float lookBlendTime = 14f;
        [Tooltip("Seconds before the zone border that the sky, sea and weather start turning into the next zone's, so you sail INTO it.")]
        public float transitionLead = 6f;

        [Header("Coast departure")]
        [Tooltip("Land objects (children of the environment root) that fall behind after the first zone.")]
        public string[] coastObjects = { "Terrain", "Village", "Pier", "Bridge", "Lighthouse", "Forest", "Mountains" };
        public string environmentRoot = "== STARLIT ENVIRONMENT ==";
        [Tooltip("The land sinks this many metres per metre it falls behind (it drops below the horizon).")]
        public float coastSinkRate = 0.035f;

        public int Stage { get; private set; }            // zones crossed this run (0 = Lantern Bay)
        public int ZoneIndex { get; private set; }        // index into zones
        public int Voyage { get { return Stage < zones.Length ? 1 : 2 + (Stage - zones.Length) / Mathf.Max(1, zones.Length - loopToZone); } }
        public float ZoneTime { get; private set; }
        public Zone Current { get { return zones[ZoneIndex]; } }
        public bool InRest { get { return Stage > 0 && ZoneTime < restDuration; } }
        public bool Active { get { GameManager g = GameManager.Instance; return g != null && g.State == GameState.Playing; } }
        /// <summary>0 at the start of a run, 1 at Aurora Deep, above 1 in later voyages.</summary>
        public float Progress { get { return (Stage + Mathf.Clamp01(ZoneTime / Mathf.Max(1f, Current.duration))) / Mathf.Max(1f, zones.Length - 1f); } }

        // Loop bonus: each voyage after the first is faster and denser.
        int Loop { get { return Mathf.Max(0, Voyage - 1); } }
        public float TideSpeed { get { return Mathf.Min(maxTide, Current.tideSpeed + Loop * loopTideBonus); } }
        public float DrainPerSecond { get { return Current.drainPerSecond + Loop * 0.006f; } }
        public float OrbValue { get { return Mathf.Max(0.05f, Current.orbValue - Loop * 0.005f); } }
        public float OrbGroupInterval { get { return Current.orbGroupInterval; } }
        public float FillerInterval { get { return Mathf.Max(0.42f, Current.fillerInterval - Loop * 0.06f); } }
        public float EncounterGap { get { return Mathf.Max(2.5f, Current.encounterGap - Loop * 0.6f); } }

        // ---- look state ----
        Zone lookFrom, lookTo;
        float lookT = 1f;
        SkyController.AtmospherePreset blendedAtmosphere;

        // The sea material's own colours (restored when play ends, so the editor never saves runtime values).
        static readonly int ShallowId = Shader.PropertyToID("_ShallowColor"), DeepId = Shader.PropertyToID("_DeepColor"),
            HorizonId = Shader.PropertyToID("_HorizonColor"), NearId = Shader.PropertyToID("_NearColor"),
            FoamId = Shader.PropertyToID("_FoamColor"), BioId = Shader.PropertyToID("_BioColor"), Bio2Id = Shader.PropertyToID("_BioColor2"),
            CalmZoneId = Shader.PropertyToID("_CalmZone");
        Material sea;
        Color[] seaOriginal;
        Vector4 calmZoneOriginal;

        // ---- coast ----
        readonly List<Transform> coast = new List<Transform>();
        readonly List<Vector3> coastStart = new List<Vector3>();
        float coastBehind;      // metres the land has fallen behind
        bool coastLeaving, coastGone;
        float probeTimer;

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            RestoreSea();
            ResetCoast();
        }

        void OnEnable()
        {
            GameManager.OnRunStarted += HandleRunStarted;
            GameManager.OnStateChanged += HandleStateChanged;
        }

        void OnDisable()
        {
            GameManager.OnRunStarted -= HandleRunStarted;
            GameManager.OnStateChanged -= HandleStateChanged;
        }

        void Start()
        {
            OceanController ocean = OceanController.Instance;
            if (ocean != null && ocean.oceanMaterial != null)
            {
                sea = ocean.oceanMaterial;
                seaOriginal = new[] { sea.GetColor(ShallowId), sea.GetColor(DeepId), sea.GetColor(HorizonId), sea.GetColor(NearId),
                                      sea.GetColor(FoamId), sea.GetColor(BioId), sea.GetColor(Bio2Id) };
                if (sea.HasProperty(CalmZoneId)) calmZoneOriginal = sea.GetVector(CalmZoneId);
            }
            FindCoast();
            lookFrom = lookTo = zones[0];
        }

        void FindCoast()
        {
            coast.Clear(); coastStart.Clear();
            GameObject root = GameObject.Find(environmentRoot);
            if (root == null) return;
            foreach (string name in coastObjects)
            {
                Transform t = root.transform.Find(name);
                if (t == null) continue;
                coast.Add(t); coastStart.Add(t.position);
            }
            Transform vfx = root.transform.Find("Ambient VFX");
            if (vfx != null)
                foreach (Transform child in vfx)
                    if (child.name.StartsWith("Fireflies")) { coast.Add(child); coastStart.Add(child.position); }
        }

        // ------------------------------------------------------------------ run flow

        void HandleRunStarted()
        {
            Stage = 0; ZoneIndex = 0; ZoneTime = 0f;
            lookFrom = lookTo = zones[0]; lookT = 1f;
            ResetCoast();
            AnnounceZone();
        }

        void HandleStateChanged(GameState state)
        {
            // Back on the menu (or the intro): the bay again, as it was.
            if (state == GameState.Menu || state == GameState.Intro)
            {
                Stage = 0; ZoneIndex = 0; ZoneTime = 0f;
                lookFrom = lookTo = zones[0]; lookT = 1f;
                ResetCoast();
            }
        }

        void Update()
        {
            GameManager game = GameManager.Instance;
            bool playing = game != null && game.State == GameState.Playing;

            if (playing)
            {
                ZoneTime += Time.deltaTime;
                // Start turning the look toward the next zone a little before the border.
                Zone upcoming = zones[NextIndex()];
                if (ZoneTime >= Current.duration - transitionLead && lookTo != upcoming) BeginLook(upcoming);
                if (ZoneTime >= Current.duration) NextZone();
            }

            UpdateLook(game, playing);
            UpdateCoast(playing);
        }

        int NextIndex()
        {
            int next = ZoneIndex + 1;
            return next >= zones.Length ? Mathf.Clamp(loopToZone, 0, zones.Length - 1) : next;
        }

        // Starts a smooth blend from whatever is on screen now to a zone's look.
        void BeginLook(Zone target)
        {
            lookFrom = BlendSnapshot();
            lookTo = target;
            lookT = 0f;
            // New scenery starts appearing on the horizon now and crossfades with the old.
            AmbientSeaField field = AmbientSeaField.Instance;
            if (field != null) field.SetScenerySet(target.scenerySet);
        }

        void NextZone()
        {
            Stage++;
            int next = NextIndex();
            ZoneIndex = next;
            if (lookTo != zones[ZoneIndex]) BeginLook(zones[ZoneIndex]);   // (normally already blending since transitionLead)
            ZoneTime = 0f;
            if (Stage == 1) coastLeaving = true;   // leaving the bay
            AnnounceZone();
        }

        void AnnounceZone()
        {
            AmbientSeaField field = AmbientSeaField.Instance;
            if (field != null) field.SetScenerySet(Current.scenerySet);
            if (OnZoneStarted != null) OnZoneStarted(Current, Voyage);
        }

        /// <summary>Jumps straight to a zone (testing and demos).</summary>
        public void DebugJumpToZone(int index)
        {
            index = Mathf.Clamp(index, 0, zones.Length - 1);
            Stage = index; ZoneIndex = index; ZoneTime = restDuration;
            lookFrom = lookTo = zones[index]; lookT = 1f;
            if (index > 0) { coastLeaving = true; coastBehind = Mathf.Max(coastBehind, 2000f); }
            AnnounceZone();
        }

        // ------------------------------------------------------------------ look

        // A copy of the current blended look, so a zone change mid-blend never jumps.
        Zone BlendSnapshot()
        {
            Zone z = new Zone();
            z.customLook = true;
            z.atmosphere = blendedAtmosphere;
            float t = Smooth(lookT);
            z.weather = lookTo.weather;
            z.sunset = Mathf.Lerp(lookFrom.sunset, lookTo.sunset, t);
            z.ambientAurora = Mathf.Lerp(lookFrom.ambientAurora, lookTo.ambientAurora, t);
            Color[] a = SeaColors(lookFrom), b = SeaColors(lookTo);
            z.seaShallow = Color.Lerp(a[0], b[0], t); z.seaDeep = Color.Lerp(a[1], b[1], t);
            z.seaHorizon = Color.Lerp(a[2], b[2], t); z.seaNear = Color.Lerp(a[3], b[3], t);
            z.seaFoam = Color.Lerp(a[4], b[4], t); z.seaBio = Color.Lerp(a[5], b[5], t); z.seaBio2 = Color.Lerp(a[6], b[6], t);
            return z;
        }

        static float Smooth(float t) { t = Mathf.Clamp01(t); return t * t * (3f - 2f * t); }

        Color[] SeaColors(Zone z)
        {
            if (z == null || !z.customLook)
                return seaOriginal ?? new[] { Color.black, Color.black, Color.black, Color.black, Color.white, Color.white, Color.white };
            return new[] { z.seaShallow, z.seaDeep, z.seaHorizon, z.seaNear, z.seaFoam, z.seaBio, z.seaBio2 };
        }

        SkyController.AtmospherePreset Atmosphere(Zone z, SkyController sky)
        {
            return z != null && z.customLook ? z.atmosphere : sky.night;
        }

        void UpdateLook(GameManager game, bool playing)
        {
            SkyController sky = SkyController.Instance;
            if (sky == null) return;
            lookT = Mathf.Min(1f, lookT + Time.deltaTime / Mathf.Max(0.1f, lookBlendTime));
            float t = Smooth(lookT);

            blendedAtmosphere = SkyController.Lerp(Atmosphere(lookFrom, sky), Atmosphere(lookTo, sky), t);
            bool inMenu = game == null || game.State == GameState.Menu || game.State == GameState.Intro;
            sky.zoneLook = blendedAtmosphere;
            sky.zoneLookWeight = Mathf.MoveTowards(sky.zoneLookWeight, inMenu ? 0f : 1f, Time.deltaTime * 0.5f);

            if (playing)
            {
                // (The intro animates the sunset itself, so only drive it during play.)
                sky.sunset = Mathf.MoveTowards(sky.sunset, Mathf.Lerp(lookFrom.sunset, lookTo.sunset, t), Time.deltaTime * 0.12f);
                sky.ambientAurora = Mathf.Lerp(lookFrom.ambientAurora, lookTo.ambientAurora, t);
                WeatherManager weather = WeatherManager.Instance;
                // The weather turns together with the sky (toward the zone being blended in).
                if (weather != null) { weather.forceWeather = true; weather.forcedWeather = lookTo.weather; }
            }
            else if (inMenu)
            {
                WeatherManager weather = WeatherManager.Instance;
                if (weather != null) weather.forceWeather = false;
                sky.ambientAurora = Mathf.MoveTowards(sky.ambientAurora, zones[0].ambientAurora, Time.deltaTime * 0.3f);
                if (game != null && game.State == GameState.Menu) sky.sunset = Mathf.MoveTowards(sky.sunset, 0f, Time.deltaTime * 0.3f);
            }

            if (sea != null && seaOriginal != null)
            {
                Color[] a = SeaColors(lookFrom), b = SeaColors(lookTo);
                float w = sky.zoneLookWeight;
                sea.SetColor(ShallowId, Color.Lerp(seaOriginal[0], Color.Lerp(a[0], b[0], t), w));
                sea.SetColor(DeepId, Color.Lerp(seaOriginal[1], Color.Lerp(a[1], b[1], t), w));
                sea.SetColor(HorizonId, Color.Lerp(seaOriginal[2], Color.Lerp(a[2], b[2], t), w));
                sea.SetColor(NearId, Color.Lerp(seaOriginal[3], Color.Lerp(a[3], b[3], t), w));
                sea.SetColor(FoamId, Color.Lerp(seaOriginal[4], Color.Lerp(a[4], b[4], t), w));
                sea.SetColor(BioId, Color.Lerp(seaOriginal[5], Color.Lerp(a[5], b[5], t), w));
                sea.SetColor(Bio2Id, Color.Lerp(seaOriginal[6], Color.Lerp(a[6], b[6], t), w));
            }
        }

        void RestoreSea()
        {
            if (sea == null || seaOriginal == null) return;
            sea.SetColor(ShallowId, seaOriginal[0]); sea.SetColor(DeepId, seaOriginal[1]);
            sea.SetColor(HorizonId, seaOriginal[2]); sea.SetColor(NearId, seaOriginal[3]);
            sea.SetColor(FoamId, seaOriginal[4]); sea.SetColor(BioId, seaOriginal[5]); sea.SetColor(Bio2Id, seaOriginal[6]);
            if (sea.HasProperty(CalmZoneId)) sea.SetVector(CalmZoneId, calmZoneOriginal);
        }

        // ------------------------------------------------------------------ coast

        // The land doesn't move, the boat does: so once we leave the bay the coast
        // falls behind at the tide speed, and sinks as it nears the horizon.
        void UpdateCoast(bool playing)
        {
            if (!coastLeaving || coastGone || coast.Count == 0) return;
            float tide = OceanController.Instance != null ? OceanController.Instance.tideSpeed : 8f;
            if (playing) coastBehind += tide * Time.deltaTime;
            ApplyCoast();

            probeTimer -= Time.deltaTime;
            if (probeTimer <= 0f)
            {
                probeTimer = 15f;   // (each refresh is time-sliced, but still not free)
                if (ReflectionProbeRefresher.Instance != null) ReflectionProbeRefresher.Instance.Refresh();
            }

            if (coastBehind > 1600f)
            {
                coastGone = true;
                foreach (Transform t in coast) if (t != null) t.gameObject.SetActive(false);
                if (ReflectionProbeRefresher.Instance != null) ReflectionProbeRefresher.Instance.Refresh();
            }
        }

        void ApplyCoast()
        {
            float sink = Mathf.Max(0f, coastBehind - 120f) * coastSinkRate;
            Vector3 offset = new Vector3(0f, -sink, -coastBehind);
            for (int i = 0; i < coast.Count; i++)
                if (coast[i] != null) coast[i].position = coastStart[i] + offset;
            if (sea != null && sea.HasProperty(CalmZoneId))
                sea.SetVector(CalmZoneId, new Vector4(calmZoneOriginal.x, calmZoneOriginal.y - coastBehind, calmZoneOriginal.z, calmZoneOriginal.w));
        }

        void ResetCoast()
        {
            bool moved = coastBehind > 0f || coastGone;
            coastBehind = 0f; coastLeaving = false; coastGone = false;
            for (int i = 0; i < coast.Count; i++)
            {
                if (coast[i] == null) continue;
                coast[i].gameObject.SetActive(true);
                coast[i].position = coastStart[i];
            }
            if (sea != null && sea.HasProperty(CalmZoneId)) sea.SetVector(CalmZoneId, calmZoneOriginal);
            if (moved && ReflectionProbeRefresher.Instance != null) ReflectionProbeRefresher.Instance.Refresh();
        }

        // ------------------------------------------------------------------ the voyage

        static SkyController.AtmospherePreset Atmo(Color fog, float density, Color sky, Color equator, Color ground, Color light, float intensity)
        {
            return new SkyController.AtmospherePreset
            {
                fogColor = fog, fogDensity = density, ambientSky = sky, ambientEquator = equator,
                ambientGround = ground, lightColor = light, lightIntensity = intensity
            };
        }

        static EncounterWeight E(EncounterKind k, float w) { return new EncounterWeight(k, w); }

        /// <summary>The six zones. Numbers are explained in Phase9/Phase9_Plan_Voyage.md.</summary>
        public static Zone[] DefaultZones()
        {
            return new[]
            {
                new Zone
                {
                    name = "Lantern Bay", subtitle = "Find your way out of the harbour", duration = 45f,
                    tideSpeed = 8f, drainPerSecond = 0.04f, orbValue = 0.1f, orbGroupInterval = 4.5f, orbsPerGroup = new Vector2Int(4, 5),
                    fillerInterval = 1.0f, logChance = 0.3f, jellyChance = 0f,
                    encounters = new[] { E(EncounterKind.BowRiders, 1f) }, encounterGap = 14f, hiddenOrbs = true,
                    customLook = false, weather = Weather.Calm, sunset = 0f, ambientAurora = 0.15f, scenerySet = 0,
                },
                new Zone
                {
                    name = "Glow Reef", subtitle = "Jellyfish blooms drift with the current", duration = 50f,
                    tideSpeed = 10f, drainPerSecond = 0.045f, orbValue = 0.095f, orbGroupInterval = 4.2f, orbsPerGroup = new Vector2Int(3, 5),
                    fillerInterval = 0.95f, logChance = 0.15f, jellyChance = 0.12f,
                    encounters = new[] { E(EncounterKind.JellyBloom, 3f), E(EncounterKind.TurtleSurfacing, 2f) }, encounterGap = 7f,
                    customLook = true, weather = Weather.Calm, sunset = 0f, ambientAurora = 0.55f, scenerySet = 1,
                    atmosphere = Atmo(new Color(0.03f, 0.13f, 0.15f), 0.0009f, new Color(0.12f, 0.42f, 0.46f), new Color(0.08f, 0.26f, 0.3f),
                                      new Color(0.02f, 0.05f, 0.05f), new Color(0.6f, 1f, 0.92f), 1.45f),
                    seaShallow = new Color(0.02f, 0.55f, 0.5f), seaDeep = new Color(0f, 0.06f, 0.08f), seaHorizon = new Color(0.04f, 0.2f, 0.22f),
                    seaNear = new Color(0.02f, 0.46f, 0.42f), seaFoam = new Color(0.5f, 1.1f, 0.85f),
                    seaBio = new Color(0.2f, 3.3f, 1.8f), seaBio2 = new Color(3.0f, 0.6f, 2.2f),
                },
                new Zone
                {
                    name = "Sunset Channel", subtitle = "Dolphin pods race across the channel", duration = 50f,
                    tideSpeed = 12f, drainPerSecond = 0.05f, orbValue = 0.09f, orbGroupInterval = 4.0f, orbsPerGroup = new Vector2Int(3, 5),
                    fillerInterval = 0.9f, logChance = 0.3f, jellyChance = 0.05f,
                    encounters = new[] { E(EncounterKind.DolphinPod, 3f), E(EncounterKind.MantaGlide, 2f), E(EncounterKind.BowRiders, 0.7f), E(EncounterKind.JellyBloom, 0.8f) },
                    encounterGap = 6f,
                    customLook = true, weather = Weather.Calm, sunset = 0.8f, ambientAurora = 0f, scenerySet = 2,
                    atmosphere = Atmo(new Color(0.22f, 0.1f, 0.24f), 0.0009f, new Color(0.35f, 0.22f, 0.5f), new Color(0.5f, 0.24f, 0.32f),
                                      new Color(0.08f, 0.04f, 0.06f), new Color(1f, 0.6f, 0.45f), 1.4f),
                    seaShallow = new Color(0.24f, 0.09f, 0.34f), seaDeep = new Color(0.05f, 0.012f, 0.09f), seaHorizon = new Color(0.42f, 0.15f, 0.28f),
                    seaNear = new Color(0.17f, 0.07f, 0.28f), seaFoam = new Color(1.0f, 0.7f, 0.58f),
                    seaBio = new Color(3.4f, 1.3f, 0.4f), seaBio2 = new Color(3.2f, 0.35f, 2.0f),
                },
                new Zone
                {
                    name = "Orca Strait", subtitle = "A pod of orcas is hunting", duration = 55f,
                    tideSpeed = 14f, drainPerSecond = 0.055f, orbValue = 0.08f, orbGroupInterval = 3.8f, orbsPerGroup = new Vector2Int(3, 4),
                    fillerInterval = 0.85f, logChance = 0.1f, jellyChance = 0.04f,
                    encounters = new[] { E(EncounterKind.OrcaHunt, 3f), E(EncounterKind.OrcaWaveWash, 2f), E(EncounterKind.DolphinPod, 1f) },
                    encounterGap = 6f,
                    customLook = true, weather = Weather.Aurora, sunset = 0f, ambientAurora = 0.8f, scenerySet = 3,
                    atmosphere = Atmo(new Color(0.06f, 0.08f, 0.2f), 0.0008f, new Color(0.3f, 0.36f, 0.72f), new Color(0.18f, 0.2f, 0.42f),
                                      new Color(0.03f, 0.04f, 0.07f), new Color(0.75f, 0.86f, 1.1f), 1.7f),
                    seaShallow = new Color(0.15f, 0.46f, 0.66f), seaDeep = new Color(0.01f, 0.03f, 0.1f), seaHorizon = new Color(0.12f, 0.15f, 0.36f),
                    seaNear = new Color(0.1f, 0.4f, 0.6f), seaFoam = new Color(0.85f, 0.97f, 1.15f),
                    seaBio = new Color(0.6f, 1.9f, 3.5f), seaBio2 = new Color(1.9f, 0.6f, 3.5f),
                },
                new Zone
                {
                    name = "Storm Reach", subtitle = "Humpbacks feed beneath the storm", duration = 55f,
                    tideSpeed = 16f, drainPerSecond = 0.06f, orbValue = 0.075f, orbGroupInterval = 3.6f, orbsPerGroup = new Vector2Int(3, 4),
                    fillerInterval = 0.75f, logChance = 0.25f, jellyChance = 0.06f,
                    encounters = new[] { E(EncounterKind.HumpbackLunge, 3f), E(EncounterKind.LionsMane, 2f), E(EncounterKind.GiantWhale, 1f), E(EncounterKind.OrcaHunt, 0.8f) },
                    encounterGap = 5f,
                    customLook = true, weather = Weather.Storm, sunset = 0f, ambientAurora = 0f, scenerySet = 4,
                    atmosphere = Atmo(new Color(0.05f, 0.06f, 0.1f), 0.0012f, new Color(0.1f, 0.13f, 0.24f), new Color(0.07f, 0.08f, 0.14f),
                                      new Color(0.02f, 0.02f, 0.03f), new Color(0.6f, 0.7f, 0.95f), 0.9f),
                    seaShallow = new Color(0.08f, 0.3f, 0.42f), seaDeep = new Color(0.01f, 0.025f, 0.06f), seaHorizon = new Color(0.1f, 0.12f, 0.22f),
                    seaNear = new Color(0.06f, 0.27f, 0.38f), seaFoam = new Color(0.75f, 0.92f, 1.2f),
                    seaBio = new Color(0.5f, 2.2f, 4.2f), seaBio2 = new Color(3.0f, 0.6f, 4.0f),
                },
                new Zone
                {
                    name = "Aurora Deep", subtitle = "Everything that lives in the sea, under the lights", duration = 60f,
                    tideSpeed = 18f, drainPerSecond = 0.065f, orbValue = 0.07f, orbGroupInterval = 3.4f, orbsPerGroup = new Vector2Int(3, 4),
                    fillerInterval = 0.7f, logChance = 0.2f, jellyChance = 0.06f,
                    encounters = new[]
                    {
                        E(EncounterKind.OrcaHunt, 2f), E(EncounterKind.HumpbackLunge, 2f), E(EncounterKind.DolphinPod, 2f), E(EncounterKind.JellyBloom, 1.5f),
                        E(EncounterKind.OrcaWaveWash, 1.5f), E(EncounterKind.MantaGlide, 1f), E(EncounterKind.LionsMane, 1f), E(EncounterKind.TurtleSurfacing, 1f),
                        E(EncounterKind.GiantWhale, 0.6f),
                    },
                    encounterGap = 4f,
                    customLook = true, weather = Weather.Aurora, sunset = 0f, ambientAurora = 1f, scenerySet = 5,
                    atmosphere = Atmo(new Color(0.05f, 0.08f, 0.17f), 0.0008f, new Color(0.26f, 0.42f, 0.58f), new Color(0.2f, 0.18f, 0.4f),
                                      new Color(0.03f, 0.05f, 0.06f), new Color(0.75f, 1f, 0.95f), 1.6f),
                    seaShallow = new Color(0.1f, 0.5f, 0.46f), seaDeep = new Color(0.01f, 0.03f, 0.08f), seaHorizon = new Color(0.16f, 0.12f, 0.32f),
                    seaNear = new Color(0.06f, 0.42f, 0.4f), seaFoam = new Color(0.75f, 1.1f, 0.95f),
                    seaBio = new Color(0.3f, 3.5f, 1.6f), seaBio2 = new Color(3.5f, 0.5f, 2.7f),
                },
            };
        }
    }
}
