// ObstacleSpawner: sends rocks, logs, jellyfish and sea creatures down the lane.
//
// Obstacles come faster as the difficulty goes up, and jellyfish and logs get
// more common. Two rules keep it fair:
//   - obstacles never overlap each other
//   - there's always a gap wide enough for the boat to get through
// Nothing spawns during the calm grace period at the start of a run.

using System.Collections.Generic;
using UnityEngine;

namespace StarlitTides
{
    public class ObstacleSpawner : MonoBehaviour
    {
        public GameObject[] rockPrefabs;
        public GameObject[] logPrefabs;
        public GameObject[] jellyfishPrefabs;
        [Tooltip("Manta rays, sea turtles, dolphins (they swim across the lane).")]
        public GameObject[] creaturePrefabs = new GameObject[0];

        public float spawnZ = 112f;
        [Tooltip("The smallest free gap (metres) that must always stay open.")]
        public float minimumGap = 3.2f;
        public int prewarmEach = 10;

        float timer;

        void Start()
        {
            foreach (GameObject prefab in rockPrefabs) PoolManager.Prewarm(prefab, prewarmEach);
            foreach (GameObject prefab in logPrefabs) PoolManager.Prewarm(prefab, prewarmEach / 2);
            foreach (GameObject prefab in jellyfishPrefabs) PoolManager.Prewarm(prefab, prewarmEach / 2);
            if (creaturePrefabs != null)
                foreach (GameObject prefab in creaturePrefabs) PoolManager.Prewarm(prefab, 2);
            timer = 1f;
        }

        void Update()
        {
            GameManager game = GameManager.Instance;
            if (game != null && (!game.IsPlaying || game.InGracePeriod)) return;

            timer -= Time.deltaTime;
            if (timer > 0f) return;

            DifficultyManager difficulty = DifficultyManager.Instance;
            float interval = 1.4f;
            if (difficulty != null) interval = difficulty.ObstacleInterval;
            // A wider lane needs more obstacles to stay just as busy (see PlayArea).
            timer = interval / PlayArea.DensityScale * Random.Range(0.7f, 1.3f);

            GameObject prefab = ChoosePrefab(difficulty);
            if (prefab == null) return;

            float radius = 1.2f;
            Obstacle prefabObstacle = prefab.GetComponent<Obstacle>();
            if (prefabObstacle != null) radius = prefabObstacle.radius;

            // Sea creatures start near one side of the lane and swim across it.
            bool creature = prefabObstacle is SeaCreature;

            // Try a few random positions and use the first fair one.
            float halfWidth = PlayArea.ObstacleHalfWidth;
            for (int attempt = 0; attempt < 6; attempt++)
            {
                float x = Random.Range(-halfWidth, halfWidth);
                if (creature) x = (Random.value < 0.5f ? -1f : 1f) * Random.Range(halfWidth * 0.45f, halfWidth);
                Vector3 position = new Vector3(x, 0f, spawnZ + Random.Range(0f, 6f));
                if (IsFair(position, radius))
                {
                    PoolManager.Spawn(prefab, position, Quaternion.identity);
                    return;
                }
            }
        }

        GameObject ChoosePrefab(DifficultyManager difficulty)
        {
            float jellyChance = 0.2f;
            float logChance = 0.2f;
            float creatureChance = 0.15f;
            if (difficulty != null)
            {
                jellyChance = difficulty.JellyfishChance;
                logChance = difficulty.LogChance;
                creatureChance = difficulty.CreatureChance;
            }

            float roll = Random.value;
            if (roll < jellyChance && jellyfishPrefabs.Length > 0)
                return jellyfishPrefabs[Random.Range(0, jellyfishPrefabs.Length)];
            if (roll < jellyChance + logChance && logPrefabs.Length > 0)
                return logPrefabs[Random.Range(0, logPrefabs.Length)];
            if (roll < jellyChance + logChance + creatureChance && creaturePrefabs != null && creaturePrefabs.Length > 0)
                return creaturePrefabs[Random.Range(0, creaturePrefabs.Length)];
            if (rockPrefabs.Length > 0)
                return rockPrefabs[Random.Range(0, rockPrefabs.Length)];
            return null;
        }

        // A position is fair if it doesn't overlap another obstacle and the boat
        // can still get past everything in this "row" of the lane.
        bool IsFair(Vector3 position, float radius)
        {
            // Each blocked piece of the row is stored as (from x, to x).
            List<Vector2> blocked = new List<Vector2>();
            blocked.Add(new Vector2(position.x - radius, position.x + radius));

            foreach (DriftingObject thing in DriftingObject.Active)
            {
                Obstacle other = thing as Obstacle;
                if (other == null) continue;

                Vector3 offset = other.transform.position - position;
                bool tooCloseInZ = Mathf.Abs(offset.z) < other.radius + radius + 2f;
                bool tooCloseInX = Mathf.Abs(offset.x) < other.radius + radius + 0.5f;
                if (tooCloseInZ && tooCloseInX) return false;   // they would overlap

                // Obstacles within 8 m ahead/behind count as the same row.
                if (Mathf.Abs(offset.z) < 8f)
                {
                    float otherX = other.transform.position.x;
                    blocked.Add(new Vector2(otherX - other.radius, otherX + other.radius));
                }
            }

            return LargestGap(blocked) >= minimumGap;
        }

        // Walks across the lane from left to right and finds the widest free space.
        float LargestGap(List<Vector2> blocked)
        {
            blocked.Sort((a, b) => a.x.CompareTo(b.x));

            float halfWidth = PlayArea.ObstacleHalfWidth;
            float edge = -halfWidth - 1f;   // right edge of everything blocked so far
            float largest = 0f;
            foreach (Vector2 piece in blocked)
            {
                largest = Mathf.Max(largest, piece.x - edge);
                edge = Mathf.Max(edge, piece.y);
            }
            largest = Mathf.Max(largest, halfWidth + 1f - edge);
            return largest;
        }
    }
}
