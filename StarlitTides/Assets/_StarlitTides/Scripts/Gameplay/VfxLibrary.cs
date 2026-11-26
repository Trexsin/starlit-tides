// VfxLibrary: one place that holds all the particle effect prefabs.
//
// Gameplay code just calls:
//   VfxLibrary.Instance.Play(VfxLibrary.Instance.splash, position);
// The effects come from the pool and put themselves back when finished
// (see PooledParticle).

using UnityEngine;

namespace StarlitTides
{
    public class VfxLibrary : MonoBehaviour
    {
        public static VfxLibrary Instance { get; private set; }

        public GameObject orbBurst;
        public GameObject splash;
        public GameObject dashBurst;

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            PoolManager.Prewarm(orbBurst, 8);
            PoolManager.Prewarm(splash, 4);
            PoolManager.Prewarm(dashBurst, 3);
        }

        public GameObject Play(GameObject effectPrefab, Vector3 position)
        {
            if (effectPrefab == null) return null;
            return PoolManager.Spawn(effectPrefab, position, Quaternion.identity);
        }
    }
}
