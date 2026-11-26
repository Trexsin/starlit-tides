// =============================================================================
//  OrbSpawner - places plankton orbs in readable, fun patterns
// -----------------------------------------------------------------------------
//  Instead of random dots, orbs come in groups the player can "draw" a line
//  through (this is what makes collecting feel satisfying):
//    Line     - a straight row down the lane
//    Arc      - a curve from one side toward the other
//    Zigzag   - a weaving trail
//    Cluster  - a tight bunch (risky if near an obstacle)
//  Orbs never spawn on top of an obstacle.
// =============================================================================
using UnityEngine;

namespace StarlitTides
{
    public class OrbSpawner : MonoBehaviour
    {
        public GameObject orbPrefab;
        public float spawnZ = 110f;
        public Vector2 laneX = new Vector2(-6.5f, 6.5f);
        public float orbSpacing = 3.2f;
        [Tooltip("Orbs closer than this to an obstacle are skipped.")]
        public float obstacleClearance = 2.4f;
        public int prewarmCount = 40;

        private float _timer;

        private void Start()
        {
            PoolManager.Prewarm(orbPrefab, prewarmCount);
            _timer = 0.5f;
        }

        private void Update()
        {
            GameManager gm = GameManager.Instance;
            if (gm != null && !gm.IsPlaying) return;

            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            float interval = DifficultyManager.Instance != null ? DifficultyManager.Instance.OrbGroupInterval : 2.2f;
            _timer = interval * Random.Range(0.8f, 1.2f);
            SpawnGroup();
        }

        private void SpawnGroup()
        {
            int pattern = Random.Range(0, 4);
            int count = Random.Range(4, 8);
            float startX = Random.Range(laneX.x, laneX.y);

            for (int i = 0; i < count; i++)
            {
                float t = count > 1 ? i / (float)(count - 1) : 0f;
                float x = startX;
                float z = spawnZ + i * orbSpacing;
                switch (pattern)
                {
                    case 0: break;                                                             // line
                    case 1: x = Mathf.Lerp(startX, -startX * 0.8f, t * t * (3f - 2f * t)); break; // arc across
                    case 2: x = startX + (i % 2 == 0 ? -1.6f : 1.6f); break;                    // zigzag
                    case 3:                                                                    // cluster
                        x = startX + Random.Range(-1.4f, 1.4f);
                        z = spawnZ + Random.Range(0f, 4f);
                        break;
                }
                x = Mathf.Clamp(x, laneX.x, laneX.y);
                Vector3 pos = new Vector3(x, 0f, z);
                if (NearObstacle(pos)) continue;
                PoolManager.Spawn(orbPrefab, pos, Quaternion.identity);
            }
        }

        private bool NearObstacle(Vector3 pos)
        {
            foreach (DriftingObject d in DriftingObject.Active)
            {
                if (!(d is Obstacle o)) continue;
                Vector3 diff = o.transform.position - pos;
                diff.y = 0f;
                if (diff.magnitude < o.radius + obstacleClearance) return true;
            }
            return false;
        }
    }
}
