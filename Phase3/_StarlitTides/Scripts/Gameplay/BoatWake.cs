// =============================================================================
//  BoatWake - glowing wake particles streaming behind the boat
// -----------------------------------------------------------------------------
//  The boat stays near the origin while the world moves, so a normal trail
//  would just pile up under it. Instead we emit particles at the stern and
//  give them the tide's velocity (toward -Z), so they stream away exactly like
//  a wake on moving water. Faster steering = more particles.
//  Phase 4 (Combo, Feature 2) scales 'comboBoost' to make the wake longer and
//  brighter.
// =============================================================================
using UnityEngine;

namespace StarlitTides
{
    [RequireComponent(typeof(ParticleSystem))]
    public class BoatWake : MonoBehaviour
    {
        public BoatController boat;
        public float baseRate = 25f;
        public float ratePerSpeed = 6f;
        [Tooltip("Set by the combo system: 1 = normal, higher = longer and brighter wake.")]
        public float comboBoost = 1f;

        private ParticleSystem _ps;

        private void Awake()
        {
            _ps = GetComponent<ParticleSystem>();
        }

        private void Update()
        {
            OceanController ocean = OceanController.Instance;
            float tide = ocean != null ? ocean.tideSpeed : 5f;

            var vel = _ps.velocityOverLifetime;
            vel.enabled = true;
            vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.6f, 0.6f);
            vel.y = new ParticleSystem.MinMaxCurve(0f, 0.15f);
            vel.z = new ParticleSystem.MinMaxCurve(-tide * 1.05f, -tide * 0.95f);

            float speed = boat != null ? boat.Velocity.magnitude : 0f;
            var emission = _ps.emission;
            bool alive = GameManager.Instance == null || GameManager.Instance.State != GameState.GameOver;
            emission.rateOverTime = alive ? (baseRate + speed * ratePerSpeed) * comboBoost : 0f;

            var main = _ps.main;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.8f * comboBoost, 1.4f * comboBoost);
            main.startSizeMultiplier = 0.18f * Mathf.Sqrt(comboBoost);
        }
    }
}
