// DriftingObject: the base class for everything the tide carries toward you
// (orbs, rocks, logs, jellyfish).
//
// Every frame it:
//   1. moves toward the camera (-Z) at the tide speed, plus its own drift
//   2. floats on the waves with a small bob
//   3. goes back to the pool once it has passed behind the camera
// Child classes add their own behaviour in OnSpawned() and Tick().

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
        [Tooltip("Extra movement on top of the tide, in m/s.")]
        public Vector3 extraDrift = Vector3.zero;
        [Tooltip("Remove the object when it goes behind this Z.")]
        public float despawnZ = -28f;
        [Tooltip("1 = ride the waves fully, 0 = stay at a fixed height.")]
        [Range(0f, 1f)] public float followWaves = 1f;

        protected float SpawnTime { get; private set; }
        // A random number per object, so they don't all bob in sync.
        protected float Seed { get; private set; }

        // Every drifting object currently in the world. The spawners use this to
        // avoid putting things on top of each other.
        static List<DriftingObject> activeObjects = new List<DriftingObject>();
        public static IReadOnlyList<DriftingObject> Active { get { return activeObjects; } }

        protected virtual void OnEnable()
        {
            SpawnTime = Time.time;
            Seed = Random.value * 100f;
            activeObjects.Add(this);
            OnSpawned();
        }

        protected virtual void OnDisable()
        {
            activeObjects.Remove(this);
        }

        // Called every time the object comes out of the pool.
        protected virtual void OnSpawned() { }

        // Called every frame after moving.
        protected virtual void Tick(float dt) { }

        protected virtual void Update()
        {
            float dt = Time.deltaTime;
            OceanController ocean = OceanController.Instance;

            float tideSpeed = 6f;
            if (ocean != null) tideSpeed = ocean.tideSpeed;

            // 1. Drift with the tide.
            Vector3 position = transform.position;
            position += (Vector3.back * tideSpeed + extraDrift) * dt;

            // 2. Float on the water and bob gently.
            float waterHeight = 0f;
            if (ocean != null) waterHeight = ocean.GetWaterHeight(position);
            float bob = Mathf.Sin((Time.time + Seed) * bobSpeed) * bobAmplitude;
            position.y = waterHeight * followWaves + floatOffset + bob;

            transform.position = position;
            Tick(dt);

            // 3. Gone past the camera (or far off to the side)? Back to the pool.
            if (position.z < despawnZ || Mathf.Abs(position.x) > 45f) Despawn();
        }

        public void Despawn()
        {
            PoolManager.Despawn(gameObject);
        }
    }
}
