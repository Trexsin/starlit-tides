// OrbSpawner: places orbs in groups the player can follow.
//
// Orbs come in patterns instead of random dots, so you can "draw" a path
// through them:
//   0 Line    - a straight row
//   1 Arc     - a curve across the lane
//   2 Zigzag  - a weaving trail
//   3 Cluster - a tight bunch
// An orb is skipped if it would land on top of an obstacle.
//
// Phase 9: orbs used to offer ~10x the light the meter drains, so nothing could
// go wrong. Now the voyage sets the orb value, group size and spacing per zone:
// early you can miss half of them, late you need nearly all of them. There is
// also a cap on how many orbs can be out at once.
//
// Phase 9, CHAINS: with the voyage, orbs come one at a time in chains of 5-8,
// a fixed gap apart in time (1.3 s early, 1.05 s late), each placed sideways
// from the last at 45-85% of the distance the boat can really steer in that
// gap. So every orb is reachable, but catching every one (the combo, which
// breaks after 2 s without an orb) takes near-perfect, committed steering
// while dodging. A short calm break follows each chain.

using UnityEngine;

namespace StarlitTides
{
    public class OrbSpawner : MonoBehaviour
    {
        public GameObject orbPrefab;
        public float spawnZ = 110f;
        public float orbSpacing = 3.2f;
        [Tooltip("Skip orbs closer than this to an obstacle.")]
        public float obstacleClearance = 2.4f;
        public int prewarmCount = 60;
        [Tooltip("Orb groups scale with the lane width to this power (1 = same density as obstacles).")]
        [Range(0f, 1f)] public float densityPower = 0.75f;

        [Tooltip("Never more orbs than this in the sea at once.")]
        public int maxActiveOrbs = 14;

        [Header("Chains (voyage)")]
        public Vector2Int chainLength = new Vector2Int(5, 8);
        public Vector2 chainBreak = new Vector2(2.6f, 3.6f);
        [Tooltip("Next orb's sideways offset as a fraction of the boat's real reach in the gap.")]
        public Vector2 reachFraction = new Vector2(0.45f, 0.85f);

        float timer;
        int chainLeft;
        float lastX;
        float lastDir = 1f;

        void Start()
        {
            PoolManager.Prewarm(orbPrefab, prewarmCount);
            timer = 0.5f;
        }

        void Update()
        {
            if (GameManager.Instance != null && !GameManager.Instance.IsPlaying) return;
            DifficultyManager difficulty = DifficultyManager.Instance;
            if (difficulty != null && difficulty.OrbGap > 0f) { UpdateChain(difficulty); return; }

            timer -= Time.deltaTime;
            if (timer > 0f) return;

            float interval = 2.2f;
            if (DifficultyManager.Instance != null) interval = DifficultyManager.Instance.OrbGroupInterval;
            // A wider lane gets more orb groups, but a little less than obstacles:
            // groups are long trails, so they already cover more of the sea.
            // (The voyage's numbers are tuned for the wide lane already.)
            float density = DifficultyManager.UsesVoyage ? 1f : Mathf.Pow(PlayArea.DensityScale, densityPower);
            timer = interval / density * Random.Range(0.85f, 1.15f);
            if (ActiveOrbs() >= maxActiveOrbs) return;

            SpawnGroup();
        }

        void SpawnGroup()
        {
            int pattern = Random.Range(0, 4);
            Vector2Int range = DifficultyManager.Instance != null ? DifficultyManager.Instance.OrbsPerGroup : new Vector2Int(4, 7);
            int count = Random.Range(range.x, range.y + 1);
            float value = DifficultyManager.Instance != null ? DifficultyManager.Instance.OrbValue : -1f;
            float halfWidth = PlayArea.OrbHalfWidth;
            float startX = Random.Range(-halfWidth, halfWidth);

            for (int i = 0; i < count; i++)
            {
                float t = 0f;   // 0 for the first orb, 1 for the last
                if (count > 1) t = i / (float)(count - 1);

                float x = startX;
                float z = spawnZ + i * orbSpacing;

                if (pattern == 1)
                {
                    // Arc: smoothly move from startX across toward the other side.
                    float smooth = t * t * (3f - 2f * t);
                    x = Mathf.Lerp(startX, -startX * 0.8f, smooth);
                }
                else if (pattern == 2)
                {
                    // Zigzag: alternate left and right.
                    x = startX + (i % 2 == 0 ? -1.6f : 1.6f);
                }
                else if (pattern == 3)
                {
                    // Cluster: random spots close together.
                    x = startX + Random.Range(-1.4f, 1.4f);
                    z = spawnZ + Random.Range(0f, 4f);
                }

                x = Mathf.Clamp(x, -halfWidth, halfWidth);
                Vector3 position = new Vector3(x, 0f, z);
                if (IsNearObstacle(position)) continue;

                GameObject spawned = PoolManager.Spawn(orbPrefab, position, Quaternion.identity);
                Orb orb = spawned != null ? spawned.GetComponent<Orb>() : null;
                if (orb != null && value > 0f) orb.lightValue = value;
            }
        }

        void UpdateChain(DifficultyManager difficulty)
        {
            timer -= Time.deltaTime;
            if (timer > 0f) return;
            float gap = difficulty.OrbGap;

            if (chainLeft <= 0)
            {
                // a new chain starts somewhere new, after a short calm break
                chainLeft = Random.Range(chainLength.x, chainLength.y + 1);
                lastX = Random.Range(-PlayArea.OrbHalfWidth * 0.6f, PlayArea.OrbHalfWidth * 0.6f);
                lastDir = Random.value < 0.5f ? -1f : 1f;
                SpawnChainOrb(lastX, difficulty.OrbValue);
                chainLeft--;
                timer = gap * Random.Range(0.95f, 1.05f);
                return;
            }

            // How far the boat can really move sideways in the gap: its steering speed
            // (a little faster on fast tides, BoatController), minus ~0.3 s to turn around.
            BoatController boat = BoatController.Instance;
            float tide = OceanController.Instance != null ? OceanController.Instance.tideSpeed : 8f;
            float steer = (boat != null ? boat.lateralSpeed : 15f) * (1f + Mathf.Clamp01((tide - 8f) / 14f) * 0.3f);
            float reach = steer * Mathf.Max(0.2f, gap - 0.3f);
            // Mostly zig-zag (it forces a full reversal each time), sometimes a long run one way.
            if (Random.value < 0.7f) lastDir = -lastDir;
            // (Per zone: orbs close together early, far apart late.)
            Vector2 frac = DifficultyManager.UsesVoyage ? difficulty.OrbReach : reachFraction;
            float x = lastX + lastDir * reach * Random.Range(frac.x, frac.y);
            float half = PlayArea.OrbHalfWidth;
            if (Mathf.Abs(x) > half)
            {
                lastDir = -lastDir;
                x = lastX + lastDir * reach * Random.Range(frac.x, frac.y);
            }
            lastX = Mathf.Clamp(x, -half, half);
            SpawnChainOrb(lastX, difficulty.OrbValue);
            chainLeft--;
            timer = chainLeft > 0 ? gap * Random.Range(0.95f, 1.05f) : Random.Range(chainBreak.x, chainBreak.y);
        }

        void SpawnChainOrb(float x, float value)
        {
            if (ActiveOrbs() >= maxActiveOrbs) return;
            Vector3 position = new Vector3(x, 0f, spawnZ);
            // Never skip a chain orb (that would break the chain): nudge it off an obstacle instead.
            for (int nudge = 0; nudge < 4 && IsNearObstacle(position); nudge++)
                position.z += obstacleClearance + 1.5f;
            GameObject spawned = PoolManager.Spawn(orbPrefab, position, Quaternion.identity);
            Orb orb = spawned != null ? spawned.GetComponent<Orb>() : null;
            if (orb != null && value > 0f) orb.lightValue = value;
        }

        static int ActiveOrbs()
        {
            int n = 0;
            foreach (DriftingObject thing in DriftingObject.Active)
                if (thing is Orb) n++;
            return n;
        }

        bool IsNearObstacle(Vector3 position)
        {
            foreach (DriftingObject thing in DriftingObject.Active)
            {
                Obstacle obstacle = thing as Obstacle;
                if (obstacle == null) continue;

                Vector3 offset = obstacle.transform.position - position;
                offset.y = 0f;
                if (offset.magnitude < obstacle.radius + obstacleClearance) return true;
            }
            return false;
        }
    }
}
