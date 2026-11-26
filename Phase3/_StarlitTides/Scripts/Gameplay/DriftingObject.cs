// =============================================================================
//  DriftingObject - base class for everything the tide carries toward you
// -----------------------------------------------------------------------------
//  Orbs, rocks, logs and jellyfish all:
//    * move toward -Z at the current tide speed (+ an optional drift of their own)
//    * float on the waves (OceanController.GetWaterHeight) with a gentle bob
//    * return themselves to the pool once they pass behind the camera
//  Subclasses override OnSpawned() and Tick() for their own behaviour.
// =============================================================================
using System.Collections.Generic;
using UnityEngine;

namespace StarlitTides
{
    public abstract class DriftingObject : MonoBehaviour
    {
        [Header("Drift")]
        [Tooltip("Height above (+) or below (-) the water surface.")]
        public float floatOffset = 0f;
        public float bobAmplitude = 0.1f;
        public float bobSpeed = 1.5f;
        [Tooltip("Extra velocity on top of the tide (world space, m/s).")]
        public Vector3 extraDrift = Vector3.zero;
        [Tooltip("Despawn when Z drops below this (behind the camera).")]
        public float despawnZ = -28f;
        [Tooltip("How much the object follows the wave height (0 = fixed height).")]
        [Range(0f, 1f)] public float followWaves = 1f;

        protected float SpawnTime { get; private set; }
        protected float Seed { get; private set; }

        private static readonly List<DriftingObject> ActiveList = new List<DriftingObject>();
        /// <summary>Every drifting object currently in the world (for spawn spacing, near-miss checks...).</summary>
        public static IReadOnlyList<DriftingObject> Active => ActiveList;

        protected virtual void OnEnable()
        {
            SpawnTime = Time.time;
            Seed = Random.value * 100f;
            ActiveList.Add(this);
            OnSpawned();
        }

        protected virtual void OnDisable()
        {
            ActiveList.Remove(this);
        }

        protected virtual void OnSpawned() { }
        protected virtual void Tick(float dt) { }

        protected virtual void Update()
        {
            float dt = Time.deltaTime;
            OceanController ocean = OceanController.Instance;
            float tide = ocean != null ? ocean.tideSpeed : 6f;

            Vector3 p = transform.position;
            p += (Vector3.back * tide + extraDrift) * dt;
            float water = ocean != null ? ocean.GetWaterHeight(p) : 0f;
            float bob = Mathf.Sin((Time.time + Seed) * bobSpeed) * bobAmplitude;
            p.y = Mathf.Lerp(0f, water, followWaves) + floatOffset + bob;
            transform.position = p;

            Tick(dt);

            if (p.z < despawnZ || Mathf.Abs(p.x) > 45f) Despawn();
        }

        public void Despawn()
        {
            PoolManager.Despawn(gameObject);
        }
    }
}
