// Jellyfish: a glowing obstacle that pulses and swims in patterns.
//
// The pulsing bell and swaying tentacles are done in the shader
// (StarlitJellyfish.shader). This script only moves it around:
//   Sine   - weaves left and right
//   Circle - swims in small loops
//   Dart   - picks a new spot every pulse and glides to it
//   Drift  - (Phase 9) drifts with the current like real jellyfish do: a weak
//            pulse forward, carried sideways by the water. A bloom is many
//            jellies sharing one current, so the whole field slides together.
// It also makes its light and water glow pulse along with the bell.

using UnityEngine;

namespace StarlitTides
{
    public class Jellyfish : Obstacle
    {
        public enum SwimPattern { Sine, Circle, Dart, Drift }

        [Header("Jellyfish")]
        public float pulseSpeed = 2.4f;   // keep the same as the material's Pulse Speed
        public float swimWidth = 2.5f;
        public float swimFrequency = 0.6f;
        public Light glowLight;
        public float glowLightBase = 1.2f;
        public WaterDisturber waterGlow;

        SwimPattern pattern;
        float startX;
        float dartTargetX;
        float nextDartTime;

        protected override void Awake()
        {
            base.Awake();
            if (glowLight != null) glowLightBase = glowLight.intensity;
        }

        protected override void OnSpawned()
        {
            base.OnSpawned();
            pattern = (SwimPattern)Random.Range(0, 3);
            startX = transform.position.x;
            dartTargetX = startX;
            nextDartTime = 0f;
            extraDrift = Vector3.zero;
        }

        /// <summary>Drift with a sea current (m/s), as part of a bloom.</summary>
        public void BeginDrift(Vector3 current)
        {
            pattern = SwimPattern.Drift;
            current.y = 0f;
            extraDrift = current;
        }

        protected override void Tick(float dt)
        {
            base.Tick(dt);

            float age = Time.time - SpawnTime;
            Vector3 position = transform.position;

            if (pattern == SwimPattern.Sine)
            {
                position.x = startX + Mathf.Sin(age * swimFrequency * Mathf.PI * 2f + Seed) * swimWidth;
            }
            else if (pattern == SwimPattern.Circle)
            {
                float angle = age * swimFrequency * 3f + Seed;
                position.x = startX + Mathf.Cos(angle) * swimWidth * 0.6f;
                position.z += Mathf.Sin(angle) * swimWidth * 0.6f * dt * 3f;
            }
            else if (pattern == SwimPattern.Drift)
            {
                // Each bell pulse gives a little push "up-current" (they swim against it).
                float pulsePush = Mathf.Max(0f, Mathf.Sin(age * pulseSpeed + Seed));
                position.z += pulsePush * 0.25f * dt;
            }
            else // Dart
            {
                if (age >= nextDartTime)
                {
                    nextDartTime = age + Mathf.PI * 2f / pulseSpeed;   // once per pulse
                    dartTargetX = Mathf.Clamp(startX + Random.Range(-swimWidth, swimWidth), -PlayArea.ObstacleHalfWidth, PlayArea.ObstacleHalfWidth);
                }
                position.x = Mathf.MoveTowards(position.x, dartTargetX, dt * 3f);
            }

            if (pattern != SwimPattern.Drift)
                position.x = Mathf.Clamp(position.x, -PlayArea.ObstacleHalfWidth, PlayArea.ObstacleHalfWidth);
            transform.position = position;

            // Pulse the light and water glow (0..1, same timing as the shader).
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.time * pulseSpeed);
            if (glowLight != null) glowLight.intensity = Mathf.Lerp(glowLightBase * 0.5f, glowLightBase * 1.3f, pulse);
            if (waterGlow != null) waterGlow.strengthMultiplier = 0.6f + pulse * 0.8f;
        }
    }
}
