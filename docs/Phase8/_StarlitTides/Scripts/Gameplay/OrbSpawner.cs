// OrbSpawner: places orbs in groups the player can follow.
//
// Orbs come in patterns instead of random dots, so you can "draw" a path
// through them:
//   0 Line    - a straight row
//   1 Arc     - a curve across the lane
//   2 Zigzag  - a weaving trail
//   3 Cluster - a tight bunch
// An orb is skipped if it would land on top of an obstacle.

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
            timer = interval / Mathf.Pow(PlayArea.DensityScale, densityPower) * Random.Range(0.8f, 1.2f);

            SpawnGroup();
        }

        void SpawnGroup()
        {
            int pattern = Random.Range(0, 4);
            int count = Random.Range(4, 8);
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

                PoolManager.Spawn(orbPrefab, position, Quaternion.identity);
            }
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
