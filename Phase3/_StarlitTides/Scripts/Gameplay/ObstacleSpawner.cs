// =============================================================================
//  ObstacleSpawner - rocks, drifting logs and jellyfish
// -----------------------------------------------------------------------------
//  Spawns faster as the difficulty rises; jellyfish and logs become more
//  common over time. Simple fairness rules:
//    * obstacles never overlap each other
//    * at least one gap wide enough for the boat always remains in each "row"
//    * nothing spawns during the first seconds of a run (grace period)
// =============================================================================
using UnityEngine;

namespace StarlitTides
{
    public class ObstacleSpawner : MonoBehaviour
    {
        public GameObject[] rockPrefabs;
        public GameObject[] logPrefabs;
        public GameObject[] jellyfishPrefabs;

        public float spawnZ = 112f;
        public Vector2 laneX = new Vector2(-7.5f, 7.5f);
        [Tooltip("Minimum free gap (metres) that must stay open across the lane.")]
        public float minimumGap = 3.2f;
        public int prewarmEach = 6;

        private float _timer;

        private void Start()
        {
            foreach (var p in rockPrefabs) PoolManager.Prewarm(p, prewarmEach);
            foreach (var p in logPrefabs) PoolManager.Prewarm(p, prewarmEach / 2);
            foreach (var p in jellyfishPrefabs) PoolManager.Prewarm(p, prewarmEach / 2);
            _timer = 1f;
        }

        private void Update()
        {
            GameManager gm = GameManager.Instance;
            if (gm != null && (!gm.IsPlaying || gm.InGracePeriod)) return;

            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            DifficultyManager diff = DifficultyManager.Instance;
            _timer = (diff != null ? diff.ObstacleInterval : 1.4f) * Random.Range(0.7f, 1.3f);

            GameObject prefab = ChoosePrefab(diff);
            if (prefab == null) return;
            float radius = prefab.GetComponent<Obstacle>() != null ? prefab.GetComponent<Obstacle>().radius : 1.2f;

            // try a few positions, keep the first fair one
            for (int attempt = 0; attempt < 6; attempt++)
            {
                float x = Random.Range(laneX.x, laneX.y);
                Vector3 pos = new Vector3(x, 0f, spawnZ + Random.Range(0f, 6f));
                if (IsFair(pos, radius))
                {
                    PoolManager.Spawn(prefab, pos, Quaternion.identity);
                    return;
                }
            }
        }

        private GameObject ChoosePrefab(DifficultyManager diff)
        {
            float jelly = diff != null ? diff.JellyfishChance : 0.2f;
            float log = diff != null ? diff.LogChance : 0.2f;
            float r = Random.value;
            if (r < jelly && jellyfishPrefabs.Length > 0) return jellyfishPrefabs[Random.Range(0, jellyfishPrefabs.Length)];
            if (r < jelly + log && logPrefabs.Length > 0) return logPrefabs[Random.Range(0, logPrefabs.Length)];
            return rockPrefabs.Length > 0 ? rockPrefabs[Random.Range(0, rockPrefabs.Length)] : null;
        }

        // Fair = doesn't overlap another obstacle, and the obstacles in this
        // "row" (similar Z) still leave a gap the boat can pass through.
        private bool IsFair(Vector3 pos, float radius)
        {
            float blockedFrom = pos.x - radius, blockedTo = pos.x + radius;
            var intervals = new System.Collections.Generic.List<Vector2> { new Vector2(blockedFrom, blockedTo) };
            foreach (DriftingObject d in DriftingObject.Active)
            {
                if (!(d is Obstacle o)) continue;
                Vector3 diff = o.transform.position - pos;
                if (Mathf.Abs(diff.z) < o.radius + radius + 2f && Mathf.Abs(diff.x) < o.radius + radius + 0.5f) return false; // overlap
                if (Mathf.Abs(diff.z) < 8f)
                    intervals.Add(new Vector2(o.transform.position.x - o.radius, o.transform.position.x + o.radius));
            }
            // largest free gap across the lane
            intervals.Sort((a, b) => a.x.CompareTo(b.x));
            float cursor = laneX.x - 1f, bestGap = 0f;
            foreach (Vector2 iv in intervals)
            {
                bestGap = Mathf.Max(bestGap, iv.x - cursor);
                cursor = Mathf.Max(cursor, iv.y);
            }
            bestGap = Mathf.Max(bestGap, laneX.y + 1f - cursor);
            return bestGap >= minimumGap;
        }
    }
}
