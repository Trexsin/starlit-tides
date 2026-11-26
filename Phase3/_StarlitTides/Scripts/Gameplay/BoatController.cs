// =============================================================================
//  BoatController - steering, dashing and floating the paper boat
// -----------------------------------------------------------------------------
//  The boat lives in a fixed "play lane" near the world origin (see the Phase 1
//  design decision): it moves left/right and a little forward/back, while the
//  tide carries the world toward it.
//
//  * Smooth steering: velocity eases toward the input (acceleration + drag)
//  * Dash (Space): a short burst of speed with a brief moment of invincibility
//  * Floating: height and tilt come from OceanController.GetWaterHeight/Normal,
//    the same maths as the ocean shader, so it rides the visible waves
//  * Banking: the boat leans and turns into its movement like a real boat
//  * Collisions: trigger colliders on orbs and obstacles call back into here
// =============================================================================
using System;
using System.Collections;
using UnityEngine;

namespace StarlitTides
{
    [RequireComponent(typeof(Rigidbody))]
    public class BoatController : MonoBehaviour
    {
        public static BoatController Instance { get; private set; }

        public static event Action OnDash;
        public static event Action<Obstacle> OnHit;

        [Header("Play lane (world X / Z limits)")]
        public Vector2 laneX = new Vector2(-7f, 7f);
        public Vector2 laneZ = new Vector2(-3f, 12f);

        [Header("Steering")]
        public float lateralSpeed = 9f;
        public float forwardSpeed = 6f;
        public float acceleration = 7f;      // how quickly velocity reaches the input
        public float edgeSoftness = 1.2f;    // slows down near the lane edges

        [Header("Dash")]
        public float dashSpeed = 24f;
        public float dashDuration = 0.18f;
        public float dashCooldown = 1.1f;
        public float dashInvulnerability = 0.3f;

        [Header("Getting hit")]
        public float hitInvulnerability = 1.0f;
        public Renderer[] blinkRenderers;

        [Header("Floating and banking")]
        public float floatHeight = 0.02f;
        public float bankAngle = 18f;        // roll when moving sideways
        public float turnAngle = 12f;        // yaw when moving sideways
        public float pitchFromSpeed = 4f;
        public float tiltFollow = 6f;        // how quickly the boat follows the wave slope

        [Header("References")]
        public LightMeter lightMeter;
        public Transform visual;              // the mesh child that tilts and bobs

        public Vector3 Velocity => _velocity;
        public bool IsDashing => Time.time < _dashEnd;
        public bool IsInvulnerable => Time.time < _invulnerableUntil;
        public float DashCooldown01 => Mathf.Clamp01((Time.time - _lastDash) / dashCooldown);

        private Vector3 _velocity;
        private Vector3 _dashDir;
        private float _dashEnd = -10f, _lastDash = -10f, _invulnerableUntil;
        private Quaternion _tilt = Quaternion.identity;
        private bool _sinking;

        private void Awake()
        {
            Instance = this;
            var rb = GetComponent<Rigidbody>();
            rb.isKinematic = true;             // we move it ourselves; it only reports triggers
            rb.interpolation = RigidbodyInterpolation.None;
            if (visual == null && transform.childCount > 0) visual = transform.GetChild(0);
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            if (_sinking) return;
            float dt = Time.deltaTime;
            Vector2 input = StarlitInput.Instance != null ? StarlitInput.Instance.Move : Vector2.zero;

            // ---- dash ----
            if (StarlitInput.Instance != null && StarlitInput.Instance.DashPressed && Time.time - _lastDash >= dashCooldown)
            {
                Vector3 dir = new Vector3(input.x, 0f, input.y);
                if (dir.sqrMagnitude < 0.01f) dir = new Vector3(Mathf.Sign(_velocity.x == 0 ? 1 : _velocity.x), 0, 0.3f);
                _dashDir = dir.normalized;
                _lastDash = Time.time;
                _dashEnd = Time.time + dashDuration;
                _invulnerableUntil = Mathf.Max(_invulnerableUntil, Time.time + dashInvulnerability);
                OnDash?.Invoke();
            }

            // ---- steering ----
            Vector3 target = new Vector3(input.x * lateralSpeed, 0f, input.y * forwardSpeed);
            if (IsDashing) target = _dashDir * dashSpeed;
            float k = 1f - Mathf.Exp(-(IsDashing ? 30f : acceleration) * dt);   // frame-rate independent easing
            _velocity = Vector3.Lerp(_velocity, target, k);

            Vector3 pos = transform.position + _velocity * dt;

            // soft lane edges: slow down and stop at the limits
            pos.x = SoftClamp(pos.x, laneX.x, laneX.y, ref _velocity.x);
            pos.z = SoftClamp(pos.z, laneZ.x, laneZ.y, ref _velocity.z);

            // ---- float on the waves ----
            OceanController ocean = OceanController.Instance;
            Vector3 waterNormal = Vector3.up;
            if (ocean != null)
            {
                pos.y = ocean.GetWaterHeight(pos) + floatHeight;
                waterNormal = ocean.GetWaterNormal(pos);
            }
            transform.position = pos;

            // ---- tilt: follow the wave slope + bank into turns ----
            Quaternion slope = Quaternion.FromToRotation(Vector3.up, Vector3.Lerp(Vector3.up, waterNormal, 0.8f));
            _tilt = Quaternion.Slerp(_tilt, slope, 1f - Mathf.Exp(-tiltFollow * dt));
            float roll = -_velocity.x / lateralSpeed * bankAngle;
            float yaw = _velocity.x / lateralSpeed * turnAngle;
            float pitch = -_velocity.z / forwardSpeed * pitchFromSpeed;
            Quaternion bank = Quaternion.Euler(pitch, yaw, roll);
            if (visual != null) visual.rotation = _tilt * bank;

            // ---- blink while invulnerable after a hit ----
            bool blinking = Time.time < _invulnerableUntil && !IsDashing && Time.time - _lastDash > dashInvulnerability;
            if (blinkRenderers != null)
                foreach (Renderer r in blinkRenderers)
                    if (r != null) r.enabled = !blinking || Mathf.Repeat(Time.time * 12f, 1f) > 0.35f;
        }

        private float SoftClamp(float value, float min, float max, ref float velocity)
        {
            if (value < min + edgeSoftness && velocity < 0f) velocity *= Mathf.Clamp01((value - min) / edgeSoftness);
            if (value > max - edgeSoftness && velocity > 0f) velocity *= Mathf.Clamp01((max - value) / edgeSoftness);
            return Mathf.Clamp(value, min, max);
        }

        // ---------------------------------------------------------------------
        // Collisions (trigger colliders on orbs and obstacles)
        // ---------------------------------------------------------------------
        private void OnTriggerEnter(Collider other)
        {
            if (_sinking) return;
            if (GameManager.Instance != null && !GameManager.Instance.IsPlaying) return;

            Orb orb = other.GetComponentInParent<Orb>();
            if (orb != null) { orb.Collect(this); return; }

            Obstacle obstacle = other.GetComponentInParent<Obstacle>();
            if (obstacle != null) TakeHit(obstacle);
        }

        public void TakeHit(Obstacle obstacle)
        {
            if (IsInvulnerable || obstacle.HasHitBoat) return;
            obstacle.OnBoatHit(this);
            _invulnerableUntil = Time.time + hitInvulnerability;
            // knock the boat away from the obstacle
            Vector3 away = transform.position - obstacle.transform.position;
            away.y = 0f;
            _velocity = away.normalized * 8f;
            if (lightMeter != null) lightMeter.Damage(obstacle.lightDamage);
            OnHit?.Invoke(obstacle);
        }

        /// <summary>Game over: the boat's light dies and it slowly sinks into the dark water.</summary>
        public void Sink(float duration)
        {
            if (_sinking) return;
            _sinking = true;
            StartCoroutine(SinkRoutine(duration));
        }

        private IEnumerator SinkRoutine(float duration)
        {
            Vector3 start = transform.position;
            Quaternion startRot = visual != null ? visual.rotation : Quaternion.identity;
            Quaternion endRot = startRot * Quaternion.Euler(25f, 0f, 35f);
            float t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / duration;
                float e = t * t;
                transform.position = start + Vector3.down * (1.2f * e) + Vector3.back * (3f * t);
                if (visual != null) visual.rotation = Quaternion.Slerp(startRot, endRot, e);
                yield return null;
            }
        }
    }
}
