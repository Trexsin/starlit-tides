// SpeedFx: makes you FEEL the boat's speed (Phase 9).
//
// Research on racing games says speed is sold by the camera and by things
// rushing past close by, more than by the raw number. This drives:
//   - Speed01: 0 at the calm starting tide, 1 at the fastest voyage tide
//   - the camera's field of view (CameraAspectFit reads FovBoost): wider when faster
//   - a punch (Kick) on every dash and on entering a new zone (a "tide surge")
//   - water streaks: glowing streaks low over the sea that rush past at tide speed
//   - bow spray: droplets thrown up at the boat's bow, more the faster you go
//   - air lines: faint speed lines at the screen edges (only when fast, or dashing)
//   - a light camera shake when fast (CameraAnchor reads Shake)
//   - a short chromatic-aberration / lens pulse on the Speed volume
// All the particle systems are built by Phase9Builder; any of them may be missing.

using UnityEngine;
using UnityEngine.Rendering;

namespace StarlitTides
{
    public class SpeedFx : MonoBehaviour
    {
        public static SpeedFx Instance { get; private set; }

        [Header("Speed range (tide m/s)")]
        public float slowTide = 7f;
        public float fastTide = 22f;

        [Header("Camera")]
        [Tooltip("Extra vertical FOV at full speed (degrees).")]
        public float maxFovBoost = 9f;
        public float kickFov = 7f;
        public float maxShake = 0.025f;

        [Header("Effects (built by Phase9Builder)")]
        public ParticleSystem waterStreaks;
        public ParticleSystem bowSpray;
        public ParticleSystem airLines;
        public Volume speedVolume;

        [Header("Rates")]
        public Vector2 streakRate = new Vector2(20f, 90f);
        public Vector2 sprayRate = new Vector2(12f, 70f);
        public float airLineRate = 70f;

        public float Speed01 { get; private set; }
        /// <summary>The smoothed punch (eases in and out, never snaps).</summary>
        public float Kick { get; private set; }
        float kickTarget;
        /// <summary>Degrees added to the gameplay camera's FOV.</summary>
        public float FovBoost { get { return Speed01 * maxFovBoost + Kick * kickFov; } }
        /// <summary>Metres of camera shake right now.</summary>
        public float Shake { get { return (Mathf.Max(0f, Speed01 - 0.35f) / 0.65f) * maxShake + Kick * 0.015f; } }

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnEnable()
        {
            BoatController.OnDash += HandleDash;
            VoyageDirector.OnZoneStarted += HandleZone;
        }

        void OnDisable()
        {
            BoatController.OnDash -= HandleDash;
            VoyageDirector.OnZoneStarted -= HandleZone;
        }

        void HandleDash() { AddKick(0.8f); }

        void HandleZone(VoyageDirector.Zone zone, int voyage)
        {
            if (VoyageDirector.Instance != null && VoyageDirector.Instance.Stage == 0) return;   // not on the very first zone
            AddKick(0.3f);    // a gentle surge (a strong punch made the zone change feel abrupt)
            if (airLines != null) airLines.Emit(12);
        }

        public void AddKick(float amount) { kickTarget = Mathf.Max(kickTarget, amount); }

        void Update()
        {
            float dt = Time.deltaTime;
            GameManager game = GameManager.Instance;
            bool playing = game != null && game.IsPlaying;
            float tide = OceanController.Instance != null ? OceanController.Instance.tideSpeed : slowTide;
            float target = playing ? Mathf.Clamp01((tide - slowTide) / Mathf.Max(1f, fastTide - slowTide)) : 0f;
            Speed01 = Mathf.MoveTowards(Speed01, target, dt * 0.5f);
            // The target fades out; the kick follows it smoothly (in over ~0.2 s, out over ~1 s).
            kickTarget = Mathf.MoveTowards(kickTarget, 0f, dt * 0.9f);
            Kick = Mathf.Lerp(Kick, kickTarget, 1f - Mathf.Exp(-(kickTarget > Kick ? 10f : 4f) * dt));

            bool dashing = BoatController.Instance != null && BoatController.Instance.IsDashing;
            float speedNow = playing ? tide : tide * 0.6f;

            if (waterStreaks != null)
            {
                ParticleSystem.EmissionModule e = waterStreaks.emission;
                e.rateOverTime = playing ? Mathf.Lerp(streakRate.x, streakRate.y, Speed01) : streakRate.x * 0.3f;
                ParticleSystem.VelocityOverLifetimeModule v = waterStreaks.velocityOverLifetime;
                v.z = new ParticleSystem.MinMaxCurve(-speedNow * 1.05f, -speedNow * 0.95f);
            }

            if (bowSpray != null)
            {
                ParticleSystem.EmissionModule e = bowSpray.emission;
                e.rateOverTime = playing ? Mathf.Lerp(sprayRate.x, sprayRate.y, Speed01) + (dashing ? 60f : 0f) : 4f;
                ParticleSystem.VelocityOverLifetimeModule v = bowSpray.velocityOverLifetime;
                v.z = new ParticleSystem.MinMaxCurve(-speedNow * 0.9f, -speedNow * 0.6f);
            }

            if (airLines != null)
            {
                float lines = Mathf.Clamp01((Speed01 - 0.4f) / 0.6f) * airLineRate + (dashing ? airLineRate * 1.4f : 0f);
                ParticleSystem.EmissionModule e = airLines.emission;
                e.rateOverTime = playing ? lines : 0f;
            }

            if (speedVolume != null)
                speedVolume.weight = Mathf.Clamp01(Speed01 * 0.3f + Kick * 0.5f);
        }
    }
}
