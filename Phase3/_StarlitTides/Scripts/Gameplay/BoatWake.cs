// BoatWake: glowing particles that stream away behind the boat.
//
// The boat barely moves (the world moves instead), so a normal trail would just
// pile up under it. Instead the particles get the tide's speed toward the
// camera, so they flow away like a wake on moving water.
// Steering faster makes more particles. The combo system (Phase 4) raises
// comboBoost for a longer, brighter wake.

using UnityEngine;

namespace StarlitTides
{
    [RequireComponent(typeof(ParticleSystem))]
    public class BoatWake : MonoBehaviour
    {
        public BoatController boat;
        public float baseRate = 25f;
        public float ratePerSpeed = 6f;
        [Tooltip("1 = normal. Higher = longer and brighter (set by the combo system).")]
        public float comboBoost = 1f;

        ParticleSystem particles;

        void Awake()
        {
            particles = GetComponent<ParticleSystem>();
        }

        void Update()
        {
            float tideSpeed = 5f;
            if (OceanController.Instance != null) tideSpeed = OceanController.Instance.tideSpeed;

            // Particles move with the water: toward the camera at the tide speed.
            ParticleSystem.VelocityOverLifetimeModule velocity = particles.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
            velocity.y = new ParticleSystem.MinMaxCurve(0f, 0.15f);
            velocity.z = new ParticleSystem.MinMaxCurve(-tideSpeed * 1.05f, -tideSpeed * 0.95f);

            // More particles when moving fast, none after game over.
            float boatSpeed = 0f;
            if (boat != null) boatSpeed = boat.Velocity.magnitude;
            bool gameOver = GameManager.Instance != null && GameManager.Instance.State == GameState.GameOver;

            ParticleSystem.EmissionModule emission = particles.emission;
            emission.rateOverTime = gameOver ? 0f : (baseRate + boatSpeed * ratePerSpeed) * comboBoost;

            ParticleSystem.MainModule main = particles.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f * comboBoost, 1.4f * comboBoost);
            main.startSizeMultiplier = 0.18f * Mathf.Sqrt(comboBoost);
        }
    }
}
