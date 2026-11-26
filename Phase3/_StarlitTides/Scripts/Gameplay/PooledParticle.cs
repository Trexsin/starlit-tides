// =============================================================================
//  PooledParticle - plays a particle effect and returns it to the pool when done
// =============================================================================
using UnityEngine;

namespace StarlitTides
{
    /// <summary>Plays a particle effect on spawn and returns it to the pool when done.</summary>
    public class PooledParticle : MonoBehaviour
    {
        private ParticleSystem[] _systems;
        private float _life;
        private float _timer;

        private void Awake()
        {
            _systems = GetComponentsInChildren<ParticleSystem>(true);
            foreach (ParticleSystem ps in _systems)
            {
                var main = ps.main;
                _life = Mathf.Max(_life, main.duration + main.startLifetime.constantMax);
            }
        }

        private void OnEnable()
        {
            _timer = 0f;
            foreach (ParticleSystem ps in _systems) { ps.Clear(true); ps.Play(true); }
        }

        private void Update()
        {
            _timer += Time.deltaTime;
            if (_timer >= _life) PoolManager.Despawn(gameObject);
        }
    }
}
