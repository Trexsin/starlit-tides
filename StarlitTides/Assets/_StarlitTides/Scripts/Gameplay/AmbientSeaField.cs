// AmbientSeaField: fills the sea beyond the play lane with scenery (Phase 8, Step 4).
//
// The camera sees ~200 m of sea across, but the lane is only ~50 m wide, so
// before this the sides of the screen were empty water. Scenery is spawned in
// BANDS by distance from the lane centre, each with its own spacing:
//
//   near  27-50 m   glowing algae, paper lanterns, small rocks   (both sides)
//   mid   52-90 m   sea stacks, rock arches, fishing boats        (right: open sea)
//                   boats, algae, lanterns                        (left: toward the coast)
//   far   95-140 m  big sea stacks, whale spouts
//
// Positions lean toward the outer edge of each band (density rises toward the
// screen edges). Nothing spawns over land or shallows (the coast is on the left).
// Like LaneMarkers it runs all the time (menu, intro, play), and the whole
// field is laid out at once at the start.
//
// Every ~45 s of play a SET PIECE floats past on one side (a ring of lanterns,
// a glowing coral arch). The density follows the quality preset.
//
// Phase 9: each zone of the voyage has its own SCENERY SET (coral and glowing
// algae on the reef, sea stacks in the sunset channel, icebergs in the orca
// strait...). VoyageDirector calls SetScenerySet(); the old scenery simply
// drifts away behind the boat while the new set appears on the horizon.

using System;
using UnityEngine;
using Random = UnityEngine.Random;

namespace StarlitTides
{
    public class AmbientSeaField : MonoBehaviour
    {
        [Serializable]
        public class Band
        {
            public string name = "Band";
            public GameObject[] prefabs;
            [Tooltip("Distance from the lane centre (|x|), metres.")]
            public Vector2 distance = new Vector2(27f, 50f);
            [Tooltip("Metres along the lane between spawns on each side (at full density).")]
            public float spacing = 16f;
            public bool left = true, right = true;
            public float spawnZ = 190f;
            [HideInInspector] public float travelled;
        }

        [Serializable]
        public class ScenerySet
        {
            public string name = "Set";
            public Band[] bands = new Band[0];
        }

        public static AmbientSeaField Instance { get; private set; }

        [Tooltip("The default scenery (Lantern Bay, the menu and the intro).")]
        public Band[] bands = new Band[0];
        [Tooltip("Phase 9: scenery per voyage zone. Index 0 = the default bands above.")]
        public ScenerySet[] zoneSets = new ScenerySet[0];
        public float firstZ = -25f;

        [Header("Set pieces (during play)")]
        public GameObject[] setPieces = new GameObject[0];
        public Vector2 setPieceInterval = new Vector2(38f, 55f);
        public Vector2 setPieceDistance = new Vector2(44f, 62f);
        public float setPieceZ = 200f;

        [Header("Density")]
        [Tooltip("Scenery density on the lowest and highest quality preset.")]
        public Vector2 densityLowHigh = new Vector2(0.45f, 1f);

        float density = 1f;
        float setPieceTimer;
        int activeSet = 0;
        int previousSet = -1;      // the set being faded out (-1 = none)
        float setBlend = 1f;       // 0 = all previous set, 1 = all active set
        [Tooltip("Seconds the old zone's scenery takes to give way to the new zone's.")]
        public float sceneryCrossfade = 18f;

        Band[] BandsOf(int set)
        {
            if (set > 0 && zoneSets != null && set < zoneSets.Length && zoneSets[set] != null)
                return zoneSets[set].bands;
            return bands;
        }

        Band[] ActiveBands { get { return BandsOf(activeSet); } }

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Switches the scenery to a zone's set (0 or less = the default bands).</summary>
        public void SetScenerySet(int index)
        {
            index = Mathf.Max(0, index);
            if (index == activeSet) return;
            // Crossfade: both sets keep spawning for a while, the old one less and less.
            previousSet = activeSet;
            activeSet = index;
            setBlend = 0f;
            foreach (Band b in ActiveBands) b.travelled = 0f;
        }

        void Start()
        {
            density = CurrentDensity();
            foreach (Band b in bands)
            {
                if (b.prefabs == null || b.prefabs.Length == 0) continue;
                int sides = (b.left ? 1 : 0) + (b.right ? 1 : 0);
                int each = Mathf.CeilToInt((b.spawnZ - firstZ) / Spacing(b) * sides / b.prefabs.Length) + 2;
                foreach (GameObject p in b.prefabs) PoolManager.Prewarm(p, each);
                for (float z = firstZ; z <= b.spawnZ; z += Spacing(b)) SpawnRow(b, z);
            }
            foreach (GameObject p in setPieces) PoolManager.Prewarm(p, 1);
            // The zone sets only start spawning when their zone comes, so a few each is plenty.
            if (zoneSets != null)
                foreach (ScenerySet set in zoneSets)
                    if (set != null && set.bands != null)
                        foreach (Band b in set.bands)
                            if (b.prefabs != null)
                                foreach (GameObject p in b.prefabs) if (p != null) PoolManager.Prewarm(p, 3);
            setPieceTimer = Random.Range(setPieceInterval.x * 0.5f, setPieceInterval.y);
        }

        float CurrentDensity()
        {
            SettingsManager s = SettingsManager.Instance;
            if (s == null) return densityLowHigh.y;
            int maxLevel = Mathf.Max(1, QualitySettings.names.Length - 1);
            return Mathf.Lerp(densityLowHigh.x, densityLowHigh.y, s.QualityLevel / (float)maxLevel);
        }

        float Spacing(Band b) { return b.spacing / Mathf.Max(density, 0.1f); }

        void Update()
        {
            density = CurrentDensity();   // follows the quality setting live (spacing adapts from the next spawn)
            float tide = OceanController.Instance != null ? OceanController.Instance.tideSpeed : 6f;
            float step = tide * Time.deltaTime;

            if (previousSet >= 0)
            {
                setBlend = Mathf.MoveTowards(setBlend, 1f, Time.deltaTime / Mathf.Max(0.1f, sceneryCrossfade));
                if (setBlend >= 1f) previousSet = -1;
            }
            Advance(ActiveBands, step, setBlend);
            if (previousSet >= 0 && previousSet != activeSet) Advance(BandsOf(previousSet), step, 1f - setBlend);

            GameManager game = GameManager.Instance;
            // Back on the menu: the bay's own scenery again.
            if (game != null && game.State == GameState.Menu && activeSet != 0) SetScenerySet(0);
            if (setPieces.Length > 0 && game != null && game.IsPlaying)
            {
                setPieceTimer -= Time.deltaTime;
                if (setPieceTimer <= 0f)
                {
                    setPieceTimer = Random.Range(setPieceInterval.x, setPieceInterval.y);
                    float side = Random.value < 0.5f ? -1f : 1f;
                    Vector3 p = new Vector3(side * Random.Range(setPieceDistance.x, setPieceDistance.y), 0f, setPieceZ);
                    if (!SeaDecor.IsOpenWater(p, -1.5f)) p.x = Mathf.Abs(p.x);   // the coast is on the left: use the open-sea side
                    PoolManager.Spawn(setPieces[Random.Range(0, setPieces.Length)], p, Quaternion.identity);
                }
            }
        }

        // Moves a set's bands along; each row spawns with probability 'chance' (the crossfade).
        void Advance(Band[] set, float step, float chance)
        {
            foreach (Band b in set)
            {
                if (b.prefabs == null || b.prefabs.Length == 0) continue;
                b.travelled += step;
                float spacing = Spacing(b);
                while (b.travelled >= spacing)
                {
                    b.travelled -= spacing;
                    if (Random.value <= chance) SpawnRow(b, b.spawnZ - b.travelled);
                }
            }
        }

        void SpawnRow(Band b, float z)
        {
            // the two sides are offset by half a spacing so they don't line up
            if (b.left) SpawnOne(b, -1f, z);
            if (b.right) SpawnOne(b, 1f, z + Spacing(b) * 0.5f);
        }

        void SpawnOne(Band b, float side, float z)
        {
            float u = 1f - (1f - Random.value) * (1f - Random.value);   // leans toward the outer edge
            float x = side * Mathf.Lerp(b.distance.x, b.distance.y, u);
            Vector3 p = new Vector3(x, 0f, z + Random.Range(-0.35f, 0.35f) * Spacing(b));
            if (!SeaDecor.IsOpenWater(p, -1.5f)) return;
            GameObject prefab = b.prefabs[Random.Range(0, b.prefabs.Length)];
            DriftingObject d = prefab.GetComponent<DriftingObject>();
            if (d != null) p.y = d.floatOffset;
            PoolManager.Spawn(prefab, p, Quaternion.identity);
        }
    }
}
