// WaterDisturber: makes the plankton glow around this object.
//
// Put it on anything that touches the sea: the boat, rocks, logs, jellyfish
// and orbs. While it's near the surface, the ocean shader draws a glowing
// ring around it.
// If "leaveTrail" is on (the boat), it also drops little glowing ripples as it
// moves through the water. They drift away with the tide, which makes a wake.

using UnityEngine;

namespace StarlitTides
{
    public class WaterDisturber : MonoBehaviour
    {
        [Tooltip("Size of the glow around the object, in metres.")]
        public float radius = 1.5f;

        [Tooltip("How bright the glow is.")]
        [Range(0f, 3f)] public float strength = 1f;

        [Tooltip("0 = steady glow, 1 = strong pulsing (nice on jellyfish).")]
        [Range(0f, 1f)] public float pulse = 0f;
        public float pulseSpeed = 2f;

        [Tooltip("No glow when the object is further than this above or below the water.")]
        public float maxHeightAboveWater = 1.5f;

        [Header("Wake trail")]
        public bool leaveTrail = false;
        [Tooltip("Distance moved through the water between ripples.")]
        public float trailSpacing = 0.8f;
        public float trailRadius = 1.2f;
        [Range(0f, 3f)] public float trailStrength = 0.8f;
        public float trailLife = 1.8f;
        [Tooltip("Minimum seconds between ripples, so fast dashes don't flood the sea with glow.")]
        public float minTrailInterval = 0.07f;

        // Other scripts can turn the glow up or down (e.g. the jellyfish pulse).
        [HideInInspector] public float strengthMultiplier = 1f;

        Vector3 lastPosition;
        float distanceSinceRipple;
        float lastRippleTime;

        // The glow strength right now, including the pulse.
        public float CurrentStrength
        {
            get
            {
                float wave = Mathf.Sin(Time.time * pulseSpeed) * 0.5f - 0.5f;   // -1..0
                return strength * strengthMultiplier * (1f + wave * pulse);
            }
        }

        void OnEnable()
        {
            lastPosition = transform.position;
            distanceSinceRipple = 0f;
            if (OceanController.Instance != null) OceanController.Instance.Register(this);
        }

        void Start()
        {
            // The ocean might not have existed yet in OnEnable when the scene loads.
            // Registering twice is safe.
            if (OceanController.Instance != null) OceanController.Instance.Register(this);
        }

        void OnDisable()
        {
            if (OceanController.Instance != null) OceanController.Instance.Unregister(this);
        }

        void Update()
        {
            OceanController ocean = OceanController.Instance;
            if (!leaveTrail || ocean == null)
            {
                lastPosition = transform.position;
                return;
            }

            // How far we moved through the WATER: our own movement plus the tide flowing past.
            Vector3 position = transform.position;
            Vector3 moved = position - lastPosition;
            moved.z += ocean.tideSpeed * Time.deltaTime;
            moved.y = 0f;
            distanceSinceRipple += moved.magnitude;
            lastPosition = position;

            bool farEnough = distanceSinceRipple >= trailSpacing;
            bool waitedEnough = Time.time - lastRippleTime >= minTrailInterval;
            if (farEnough && waitedEnough)
            {
                lastRippleTime = Time.time;
                distanceSinceRipple = 0f;

                bool touchingWater = Mathf.Abs(position.y - ocean.GetWaterHeight(position)) < maxHeightAboveWater;
                if (touchingWater) ocean.AddRipple(position, trailRadius, trailStrength * strengthMultiplier, trailLife);
            }
        }
    }
}
