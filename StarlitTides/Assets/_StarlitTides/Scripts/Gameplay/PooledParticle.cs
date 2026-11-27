// PooledParticle: plays a particle effect when it's spawned, then puts itself
// back in the pool once all its particles are gone.

using UnityEngine;

namespace StarlitTides
{
    public class PooledParticle : MonoBehaviour
    {
        [Tooltip("Children placed on the water surface (at the real wave height) before playing: shockwave rings, foam rings.")]
        public Transform[] snapToWater = new Transform[0];
        public float waterOffset = 0.12f;

        ParticleSystem[] systems;
        float lifetime;   // how long the longest particle can live
        float timer;

        void Awake()
        {
            systems = GetComponentsInChildren<ParticleSystem>(true);
            foreach (ParticleSystem ps in systems)
            {
                float total = ps.main.duration + ps.main.startLifetime.constantMax;
                lifetime = Mathf.Max(lifetime, total);
            }
        }

        void OnEnable()
        {
            timer = 0f;
            OceanController ocean = OceanController.Instance;
            if (ocean != null && snapToWater != null)
                foreach (Transform t in snapToWater)
                {
                    if (t == null) continue;
                    Vector3 p = t.position;
                    p.y = ocean.GetWaterHeight(p) + waterOffset;
                    t.position = p;
                }
            foreach (ParticleSystem ps in systems)
            {
                ps.Clear(true);
                ps.Play(true);
            }
        }

        void Update()
        {
            timer += Time.deltaTime;
            if (timer >= lifetime) PoolManager.Despawn(gameObject);
        }
    }
}
