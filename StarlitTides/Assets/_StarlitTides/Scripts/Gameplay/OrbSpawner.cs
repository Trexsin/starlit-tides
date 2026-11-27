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

        float timer;

        void Start()
        {
            PoolManager.Prewarm(orbPrefab, prewarmCount);
            timer = 0.5f;
        }

        void Update()
        {
            if (GameManager.Instance != null && !GameManager.Instance.IsPlaying) return;

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
