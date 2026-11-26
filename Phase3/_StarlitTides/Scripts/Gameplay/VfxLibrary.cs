// =============================================================================
//  VfxLibrary - one place that knows every pooled particle effect
// -----------------------------------------------------------------------------
//  Gameplay code calls  VfxLibrary.Instance.Play(VfxLibrary.Instance.splash, pos)
//  and never needs its own prefab references. Effects are pooled and return
//  themselves when finished (PooledParticle).
// =============================================================================
using UnityEngine;

namespace StarlitTides
{
    public class VfxLibrary : MonoBehaviour
    {
        public static VfxLibrary Instance { get; private set; }

        [Header("Phase 3")]
        public GameObject orbBurst;
        public GameObject splash;
        public GameObject dashBurst;

        private void Awake() { Instance = this; }
        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Start()
        {
            PoolManager.Prewarm(orbBurst, 8);
            PoolManager.Prewarm(splash, 4);
            PoolManager.Prewarm(dashBurst, 3);
        }

        public GameObject Play(GameObject prefab, Vector3 position, Quaternion? rotation = null)
        {
            if (prefab == null) return null;
            return PoolManager.Spawn(prefab, position, rotation ?? Quaternion.identity);
        }
    }

}
