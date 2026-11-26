// =============================================================================
//  Jellyfish - a pulsing, glowing obstacle that swims in patterns
// -----------------------------------------------------------------------------
//  The bell pulse and tentacle sway are animated on the GPU by the
//  StarlitTides/Jellyfish shader. This script adds the swimming:
//    * Sine   - weaves left and right across the lane
//    * Circle - swims in small loops
//    * Dart   - drifts, then darts sideways in sync with a pulse
//  and syncs its point light and water glow with the pulse.
// =============================================================================
using UnityEngine;

namespace StarlitTides
{
    public class Jellyfish : Obstacle
    {
        public enum SwimPattern { Sine, Circle, Dart }

        [Header("Jellyfish")]
        public float pulseSpeed = 2.4f;          // must match the material's Pulse Speed
        public float swimWidth = 2.5f;
        public float swimFrequency = 0.6f;
        public Light glowLight;
        public float glowLightBase = 1.2f;
        public WaterDisturber waterGlow;

        private SwimPattern _pattern;
        private float _baseX;
        private float _dartTarget;
        private float _nextDart;

        protected override void Awake()
        {
            base.Awake();
            if (glowLight != null) glowLightBase = glowLight.intensity;
        }

        protected override void OnSpawned()
        {
            base.OnSpawned();
            _pattern = (SwimPattern)Random.Range(0, 3);
            _baseX = transform.position.x;
            _dartTarget = _baseX;
            _nextDart = 0f;
            extraDrift = Vector3.zero;
        }

        protected override void Tick(float dt)
        {
            base.Tick(dt);
            float t = Time.time - SpawnTime;
            Vector3 p = transform.position;
            switch (_pattern)
            {
                case SwimPattern.Sine:
                    p.x = _baseX + Mathf.Sin(t * swimFrequency * Mathf.PI * 2f + Seed) * swimWidth;
                    break;
                case SwimPattern.Circle:
                    p.x = _baseX + Mathf.Cos(t * swimFrequency * 3f + Seed) * swimWidth * 0.6f;
                    p.z += Mathf.Sin(t * swimFrequency * 3f + Seed) * swimWidth * 0.6f * dt * 3f;
                    break;
                case SwimPattern.Dart:
                    // pick a new target each pulse, glide toward it
                    if (t >= _nextDart)
                    {
                        _nextDart = t + Mathf.PI * 2f / pulseSpeed;
                        _dartTarget = Mathf.Clamp(_baseX + Random.Range(-swimWidth, swimWidth), -7f, 7f);
                    }
                    p.x = Mathf.MoveTowards(p.x, _dartTarget, dt * 3f);
                    break;
            }
            p.x = Mathf.Clamp(p.x, -8.5f, 8.5f);
            transform.position = p;

            // sync light and water glow with the pulse (same formula as the shader)
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * pulseSpeed);
            if (glowLight != null) glowLight.intensity = Mathf.Lerp(glowLightBase * 0.5f, glowLightBase * 1.3f, pulse);
            if (waterGlow != null) waterGlow.strengthMultiplier = 0.6f + pulse * 0.8f;
        }
    }
}
