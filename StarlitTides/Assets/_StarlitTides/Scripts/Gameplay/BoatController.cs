// BoatController: steers, dashes and floats the paper boat.
//
// The boat stays inside the "play lane" (its size comes from PlayArea).
// It never really travels forward: the tide carries everything else toward it.
//
// - Steering: the velocity eases toward the input, so it feels smooth.
// - Dash (Space): a quick burst of speed, and you can't be hit for a moment.
// - Floating: the height and tilt come from the same wave maths as the ocean
//   shader (OceanController), so the boat rides the waves you can see.
// - Banking: the boat leans into turns like a real boat.
// - Collisions: orbs and obstacles have trigger colliders that call OnTriggerEnter.

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

        [Header("Steering")]
        public float lateralSpeed = 15f;     // left/right (the Phase 8 lane is wider)
        public float forwardSpeed = 7f;      // forward/back
        public float acceleration = 7f;      // how quickly we reach the input speed
        public float edgeSoftness = 1.2f;    // slow down this close to the lane edge
        [Tooltip("Bumping the lane edge faster than this (m/s) makes a ripple.")]
        public float edgeBumpSpeed = 6f;

        [Header("Dash")]
        public float dashSpeed = 27f;
        public float dashDuration = 0.18f;
        [Tooltip("Seconds between dashes. Phase 9: 2.5 (was 1.1): with a dash every second players could dash through everything.")]
        public float dashCooldown = 2.5f;
        [Tooltip("Seconds you can't be hit at the start of a dash. Phase 9: 0.2 (was 0.3).")]
        public float dashInvulnerability = 0.2f;

        [Header("Getting hit")]
        public float hitInvulnerability = 1.0f;
        public Renderer[] blinkRenderers;

        [Header("Floating and leaning")]
        public float floatHeight = 0.02f;
        public float bankAngle = 18f;        // roll when moving sideways
        public float turnAngle = 12f;        // turn the nose when moving sideways
        public float pitchFromSpeed = 4f;
        public float tiltFollow = 6f;        // how fast the boat follows the wave slope

        [Header("References")]
        public LightMeter lightMeter;
        public Transform visual;             // the model that tilts and leans

        public Vector3 Velocity { get { return velocity; } }
        /// <summary>A shove from outside (an orca's wave), fading out over ~0.5 s.</summary>
        public Vector3 ExternalVelocity { get { return externalVelocity; } }
        public bool IsDashing { get { return Time.time < dashEndTime; } }
        /// <summary>-1..1, smoothed: holding forward speeds the whole sea up (DifficultyManager), back slows it.</summary>
        public float Throttle { get; private set; }
        /// <summary>1 right after a forward dash, fading to 0: a short burst of extra speed.</summary>
        public float Surge { get; private set; }
        public bool IsInvulnerable { get { return Time.time < invulnerableUntil; } }
        // 0 right after a dash, 1 when the dash is ready again.
        public float DashCooldown01 { get { return Mathf.Clamp01((Time.time - lastDashTime) / dashCooldown); } }

        Vector3 velocity;
        Vector3 externalVelocity;
        Vector3 dashDirection;
        float dashEndTime = -10f;
        float lastDashTime = -10f;
        float invulnerableUntil;
        Quaternion waveTilt = Quaternion.identity;
        bool sinking;
        float lastEdgeBump = -10f;

        void Awake()
        {
            Instance = this;

            // Kinematic: we move the boat ourselves. The Rigidbody is only
            // there so trigger collisions get reported.
            Rigidbody body = GetComponent<Rigidbody>();
            body.isKinematic = true;

            if (visual == null && transform.childCount > 0) visual = transform.GetChild(0);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            if (sinking) return;

            Vector2 input = Vector2.zero;
            if (StarlitInput.Instance != null) input = StarlitInput.Instance.Move;

            // Throttle: forward/back input eases in and out (the sea speeds up or slows down).
            bool canSteer = GameManager.Instance == null || GameManager.Instance.IsPlaying;
            float wantThrottle = canSteer ? Mathf.Clamp(input.y, -1f, 1f) : 0f;
            Throttle = Mathf.Lerp(Throttle, wantThrottle, 1f - Mathf.Exp(-4f * Time.deltaTime));
            Surge = Mathf.MoveTowards(Surge, 0f, Time.deltaTime * 1.4f);

            TryDash(input);
            Steer(input);
            FloatAndTilt();
            BlinkWhileInvulnerable();
        }

        void TryDash(Vector2 input)
        {
            if (StarlitInput.Instance == null || !StarlitInput.Instance.DashPressed) return;
            if (Time.time - lastDashTime < dashCooldown) return;

            // Dash the way the player is steering: left, right, forward, or diagonally.
            // With no input, dash sideways the way we're already moving (or right if still).
            Vector3 direction = new Vector3(input.x, 0f, input.y);
            if (direction.sqrMagnitude < 0.01f)
            {
                float side = velocity.x < 0f ? -1f : 1f;
                direction = new Vector3(side, 0f, 0f);
            }
            // Snap near-pure directions so W+Space is a clean forward dash and A/D+Space clean sideways.
            if (Mathf.Abs(direction.x) < 0.3f) direction.x = 0f;
            if (Mathf.Abs(direction.z) < 0.3f) direction.z = 0f;
            if (direction.sqrMagnitude < 0.01f) direction = new Vector3(velocity.x < 0f ? -1f : 1f, 0f, 0f);

            dashDirection = direction.normalized;
            // A forward dash is also a burst of speed: the whole sea rushes past faster for a moment.
            if (dashDirection.z > 0.4f) Surge = 1f;
            lastDashTime = Time.time;
            dashEndTime = Time.time + dashDuration;
            invulnerableUntil = Mathf.Max(invulnerableUntil, Time.time + dashInvulnerability);
            if (OnDash != null) OnDash();
        }

        /// <summary>Shoves the boat (Phase 9: the orcas' wave-wash). The push fades out by itself.</summary>
        public void Push(Vector3 push)
        {
            push.y = 0f;
            externalVelocity += push;
        }

        void Steer(Vector2 input)
        {
            float dt = Time.deltaTime;

            // Faster tides (later zones) get a little more steering, so the game stays fair.
            float tide = OceanController.Instance != null ? OceanController.Instance.tideSpeed : 8f;
            float steerBoost = 1f + Mathf.Clamp01((tide - 8f) / 14f) * 0.3f;
            Vector3 targetVelocity = new Vector3(input.x * lateralSpeed * steerBoost, 0f, input.y * forwardSpeed);
            float response = acceleration;
            if (IsDashing)
            {
                targetVelocity = dashDirection * dashSpeed;
                response = 30f;
            }

            // Ease toward the target. Using Exp() makes it feel the same at any frame rate.
            float blend = 1f - Mathf.Exp(-response * dt);
            velocity = Vector3.Lerp(velocity, targetVelocity, blend);

            float halfWidth = PlayArea.HalfWidth;
            Vector2 laneZ = PlayArea.LaneZ;
            float sideSpeed = velocity.x;

            Vector3 position = transform.position + (velocity + externalVelocity) * dt;
            externalVelocity *= Mathf.Exp(-2f * dt);
            position.x = SoftClamp(position.x, -halfWidth, halfWidth, ref velocity.x);
            position.z = SoftClamp(position.z, laneZ.x, laneZ.y, ref velocity.z);
            transform.position = position;

            // Steering hard into the channel edge: a soft bump and a glowing ripple,
            // so the edge feels like a place rather than an invisible wall.
            bool atEdge = Mathf.Abs(position.x) >= halfWidth - 0.05f && Mathf.Sign(sideSpeed) == Mathf.Sign(position.x);
            if (atEdge && Mathf.Abs(sideSpeed) > edgeBumpSpeed && Time.time - lastEdgeBump > 0.6f)
            {
                lastEdgeBump = Time.time;
                velocity.x = -Mathf.Sign(position.x) * 2.5f;
                if (OceanController.Instance != null)
                    OceanController.Instance.AddRipple(position + Vector3.right * Mathf.Sign(position.x) * 0.8f, 1.6f, 0.9f, 1.2f);
            }
        }

        // Keeps a value inside min..max, and slows the velocity down near the edges.
        float SoftClamp(float value, float min, float max, ref float speed)
        {
            if (value < min + edgeSoftness && speed < 0f) speed *= Mathf.Clamp01((value - min) / edgeSoftness);
            if (value > max - edgeSoftness && speed > 0f) speed *= Mathf.Clamp01((max - value) / edgeSoftness);
            return Mathf.Clamp(value, min, max);
        }

        void FloatAndTilt()
        {
            Vector3 position = transform.position;
            Vector3 waterNormal = Vector3.up;

            OceanController ocean = OceanController.Instance;
            if (ocean != null)
            {
                position.y = ocean.GetWaterHeight(position) + floatHeight;
                waterNormal = ocean.GetWaterNormal(position);
            }
            transform.position = position;

            // Tilt with the wave slope (80% of it, so it isn't too wobbly).
            Vector3 up = Vector3.Lerp(Vector3.up, waterNormal, 0.8f);
            Quaternion slope = Quaternion.FromToRotation(Vector3.up, up);
            waveTilt = Quaternion.Slerp(waveTilt, slope, 1f - Mathf.Exp(-tiltFollow * Time.deltaTime));

            // Lean and turn into the movement.
            float roll = -velocity.x / lateralSpeed * bankAngle;
            float yaw = velocity.x / lateralSpeed * turnAngle;
            float pitch = -velocity.z / forwardSpeed * pitchFromSpeed;
            Quaternion lean = Quaternion.Euler(pitch, yaw, roll);

            if (visual != null) visual.rotation = waveTilt * lean;
        }

        // After a crash the boat blinks until it can be hit again.
        void BlinkWhileInvulnerable()
        {
            if (blinkRenderers == null) return;

            bool fromDash = Time.time - lastDashTime <= dashInvulnerability;
            bool blinking = IsInvulnerable && !IsDashing && !fromDash;
            bool visible = !blinking || Mathf.Repeat(Time.time * 12f, 1f) > 0.35f;

            foreach (Renderer r in blinkRenderers)
                if (r != null) r.enabled = visible;
        }

        void OnTriggerEnter(Collider other)
        {
            if (sinking) return;
            if (GameManager.Instance != null && !GameManager.Instance.IsPlaying) return;

            Orb orb = other.GetComponentInParent<Orb>();
            if (orb != null)
            {
                orb.Collect(this);
                return;
            }

            Obstacle obstacle = other.GetComponentInParent<Obstacle>();
            if (obstacle != null) TakeHit(obstacle);
        }

        public void TakeHit(Obstacle obstacle)
        {
            if (IsInvulnerable || obstacle.HasHitBoat) return;

            obstacle.OnBoatHit(this);
            invulnerableUntil = Time.time + hitInvulnerability;

            // Bounce away from the obstacle.
            Vector3 away = transform.position - obstacle.transform.position;
            away.y = 0f;
            velocity = away.normalized * 8f;

            if (lightMeter != null) lightMeter.Damage(obstacle.lightDamage);
            if (OnHit != null) OnHit(obstacle);
        }

        // Game over: the light is gone and the boat slowly sinks.
        public void Sink(float duration)
        {
            if (sinking) return;
            sinking = true;
            StartCoroutine(SinkRoutine(duration));
        }

        IEnumerator SinkRoutine(float duration)
        {
            Vector3 startPosition = transform.position;
            Quaternion startRotation = Quaternion.identity;
            if (visual != null) startRotation = visual.rotation;
            Quaternion endRotation = startRotation * Quaternion.Euler(25f, 0f, 35f);

            float t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / duration;
                float eased = t * t;   // starts slow, speeds up
                transform.position = startPosition + Vector3.down * (1.2f * eased) + Vector3.back * (3f * t);
                if (visual != null) visual.rotation = Quaternion.Slerp(startRotation, endRotation, eased);
                yield return null;
            }
        }
    }
}
