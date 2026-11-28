// LaneMarkers: lines the play lane with glowing buoys and little reefs.
//
// A row of markers floats just outside the lane on both sides (at
// PlayArea.EdgeMarkerX), so the player can see where the channel ends. Buoys
// and reefs alternate every "spacing" metres, with a little random jitter so
// the line looks natural, not like a fence.
//
// The markers drift with the tide the whole time (menu, intro and play), so
// the channel is always there. At the start the whole line is laid out at
// once; after that a new pair is added each time the tide has carried the
// last one "spacing" metres closer.

using UnityEngine;

namespace StarlitTides
{
    public class LaneMarkers : MonoBehaviour
    {
        public GameObject[] buoyPrefabs;
        public GameObject[] reefPrefabs;
        [Tooltip("Metres between markers along the lane.")]
        public float spacing = 16f;
        [Tooltip("Random sideways/forward jitter (metres).")]
        public Vector2 jitter = new Vector2(1.6f, 4f);
        public float spawnZ = 125f;
        public float firstZ = -26f;

        float distanceSinceLast;
        int index;

        void Start()
        {
            int perSide = Mathf.CeilToInt((spawnZ - firstZ) / spacing) + 2;
            foreach (GameObject p in buoyPrefabs) PoolManager.Prewarm(p, perSide);
            foreach (GameObject p in reefPrefabs) PoolManager.Prewarm(p, perSide);

            for (float z = firstZ; z <= spawnZ; z += spacing) SpawnPair(z);
        }

        void Update()
        {
            float tide = OceanController.Instance != null ? OceanController.Instance.tideSpeed : 6f;
            distanceSinceLast += tide * Time.deltaTime;
            while (distanceSinceLast >= spacing)
            {
                distanceSinceLast -= spacing;
                SpawnPair(spawnZ - distanceSinceLast);
            }
        }

        void SpawnPair(float z)
        {
            bool buoy = index % 2 == 0;
            index++;
            Spawn(buoy ? buoyPrefabs : reefPrefabs, -1f, z);
            Spawn(buoy ? reefPrefabs : buoyPrefabs, 1f, z + spacing * 0.5f);
        }

        void Spawn(GameObject[] prefabs, float side, float z)
        {
            if (prefabs == null || prefabs.Length == 0) return;
            GameObject prefab = prefabs[Random.Range(0, prefabs.Length)];
            float x = side * (PlayArea.EdgeMarkerX + Random.Range(-jitter.x, jitter.x));
            Vector3 position = new Vector3(x, 0f, z + Random.Range(-jitter.y, jitter.y));
            PoolManager.Spawn(prefab, position, Quaternion.identity);
        }
    }
}
