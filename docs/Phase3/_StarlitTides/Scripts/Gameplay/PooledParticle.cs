// PooledParticle: plays a particle effect when it's spawned, then puts itself
// back in the pool once all its particles are gone.

using UnityEngine;

namespace StarlitTides
{
    public class PooledParticle : MonoBehaviour
    {
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
