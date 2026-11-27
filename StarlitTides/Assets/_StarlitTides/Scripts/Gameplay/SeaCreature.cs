// SeaCreature: glowing sea creatures that swim across your path.
//
// They drift toward you with the tide like every obstacle, and swim across
// the lane at the same time, facing the way they swim:
//   MantaGlide  - a big ray gliding across in a lazy S-curve; sometimes it
//                 bursts out of the water in a leap and splashes back down
//   TurtlePaddle- a sea turtle paddling slowly across at the surface
//   DolphinLeap - a dolphin porpoising across in a row of arcing leaps
// Fins, wings and tails are animated in the shader (StarlitSeaCreature.shader).
// While a creature is high in the air it can't hit the boat (its collider is
// switched off), so a leap over you is a beautiful moment, never a cheap hit.

using UnityEngine;

namespace StarlitTides
{
    public class SeaCreature : Obstacle
    {
        public enum Behaviour { MantaGlide, TurtlePaddle, DolphinLeap }

        [Header("Sea creature")]
        public Behaviour behaviour = Behaviour.MantaGlide;
        [Tooltip("Speed across the lane (m/s), picked at random in this range.")]
        public Vector2 crossSpeed = new Vector2(1.2f, 2.2f);
        [Tooltip("How far it weaves forward/back while crossing (metres).")]
        public float weave = 1.5f;
        public float weaveFrequency = 0.35f;
        [Tooltip("Chance that a manta leaps once while crossing.")]
        [Range(0f, 1f)] public float leapChance = 0.4f;
        public float leapHeight = 2.6f;
        public float leapDuration = 1.3f;
        [Tooltip("Above this height (m over the water) the creature can't hit the boat.")]
        public float safeHeight = 1.1f;
        public Light glowLight;
        public Collider hitCollider;

        float direction;          // +1 swims toward +X, -1 toward -X
        float speed;
        float leapStart = -100f;  // time the current leap began
        float nextLeapTime;
        bool leapPlanned;
        bool splashedOut;
        float baseLight;
        float lastHeight;
        Material materialInstance;

        protected override void Awake()
        {
            base.Awake();
            if (hitCollider == null) hitCollider = GetComponent<Collider>();
            if (glowLight != null) baseLight = glowLight.intensity;
        }

        protected override void OnSpawned()
        {
            base.OnSpawned();
            // Swim toward (and past) the middle of the lane, timed so it crosses
            // your path about when the tide brings it to the boat.
            direction = transform.position.x > 0f ? -1f : 1f;
            float tide = OceanController.Instance != null ? Mathf.Max(OceanController.Instance.tideSpeed, 3f) : 7f;
            float secondsToBoat = Mathf.Max(transform.position.z - 2f, 10f) / tide;
            float targetX = direction * Random.Range(0f, 9f);
            speed = Mathf.Clamp(Mathf.Abs(targetX - transform.position.x) / secondsToBoat, crossSpeed.x, crossSpeed.y);
            extraDrift = new Vector3(direction * speed, 0f, 0f);
            transform.rotation = Heading(0f);
            leapStart = -100f;
            splashedOut = false;

            // Offset the fin animation so two creatures never flap in sync.
            Renderer r = visual != null ? visual.GetComponentInChildren<Renderer>() : null;
            if (r != null)
            {
                if (materialInstance == null) materialInstance = r.material;
                materialInstance.SetFloat("_AnimTime", Seed);
            }

            if (behaviour == Behaviour.MantaGlide)
            {
                leapPlanned = Random.value < leapChance;
                nextLeapTime = Time.time + Random.Range(2.5f, 6f);
            }
            else if (behaviour == Behaviour.DolphinLeap)
            {
                leapPlanned = true;
                nextLeapTime = Time.time + Random.Range(0.2f, 1.2f);
            }
            else leapPlanned = false;
        }

        void OnDestroy()
        {
            if (materialInstance != null) Destroy(materialInstance);
        }

        // Facing the swim direction, turned a little by the weave.
        Quaternion Heading(float weaveTurn)
        {
            float yaw = direction > 0f ? 90f : -90f;
            return Quaternion.Euler(0f, yaw + weaveTurn, 0f);
        }

        protected override void Tick(float dt)
        {
            base.Tick(dt);
            float age = Time.time - SpawnTime;
            Vector3 position = transform.position;

            // Weave forward and back while crossing (an S-curve on the water).
            position.z += Mathf.Cos(age * weaveFrequency * Mathf.PI * 2f + Seed) * weave * weaveFrequency * Mathf.PI * 2f * dt;
            float weaveTurn = -direction * Mathf.Cos(age * weaveFrequency * Mathf.PI * 2f + Seed) * 20f * Mathf.Clamp01(weave);

            // ---- leaps ----
            if (leapPlanned && Time.time >= nextLeapTime && Time.time - leapStart > leapDuration)
            {
                leapStart = Time.time;
                splashedOut = false;
                if (behaviour == Behaviour.DolphinLeap) nextLeapTime = Time.time + leapDuration + Random.Range(0.4f, 1.0f);
                else leapPlanned = false;   // a manta leaps once
            }

            float height = 0f, pitch = 0f, roll = 0f;
            float lt = (Time.time - leapStart) / leapDuration;
            if (lt >= 0f && lt <= 1f)
            {
                height = leapHeight * 4f * lt * (1f - lt);                 // a parabola
                pitch = Mathf.Lerp(35f, -35f, lt);                          // nose up, then nose down
                if (behaviour == Behaviour.MantaGlide) roll = Mathf.Sin(lt * Mathf.PI) * 25f * direction;
                if (!splashedOut && lt > 0.05f) { splashedOut = true; Splash(position, 0.8f); }
            }
            else if (lastHeight > 0.3f)
            {
                Splash(position, 1.2f);   // landing back in the water
            }
            lastHeight = height;

            // Turtles bob their heads up gently; everything rolls a little with the swell.
            if (behaviour == Behaviour.TurtlePaddle) pitch += Mathf.Sin(age * 1.7f + Seed) * 4f;

            position.y += height;
            transform.position = position;
            transform.rotation = Heading(weaveTurn) * Quaternion.Euler(-pitch, 0f, roll);

            // High in the air it sails over the boat harmlessly.
            if (hitCollider != null) hitCollider.enabled = height < safeHeight;

            if (glowLight != null)
                glowLight.intensity = baseLight * (0.8f + 0.2f * Mathf.Sin(Time.time * 1.6f + Seed));
        }

        void Splash(Vector3 at, float size)
        {
            Vector3 water = new Vector3(at.x, OceanController.Instance != null ? OceanController.Instance.GetWaterHeight(at) : 0f, at.z);
            if (VfxLibrary.Instance != null) VfxLibrary.Instance.Play(VfxLibrary.Instance.splash, water);
            if (OceanController.Instance != null) OceanController.Instance.AddRipple(water, 2.5f * size, 2f, 1.4f);
        }
    }
}
