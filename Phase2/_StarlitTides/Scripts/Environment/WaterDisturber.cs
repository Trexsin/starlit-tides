// =============================================================================
//  WaterDisturber - makes the plankton glow around this object
// -----------------------------------------------------------------------------
//  Add to anything that touches the sea: the boat, rocks, logs, jellyfish,
//  orbs. While it is near the surface the ocean shader draws a glowing core
//  and ring around it. If it moves relative to the water (like the boat) it
//  also drops trail ripples that drift away on the tide: a glowing wake.
// =============================================================================
using UnityEngine;

namespace StarlitTides
{
    public class WaterDisturber : MonoBehaviour
    {
        [Tooltip("Radius of the glow around the object (metres).")]
        public float radius = 1.5f;

        [Tooltip("Glow brightness.")]
        [Range(0f, 3f)] public float strength = 1f;

        [Tooltip("Gentle pulsing of the glow (0 = steady). Nice on jellyfish.")]
        [Range(0f, 1f)] public float pulse = 0f;
        public float pulseSpeed = 2f;

        [Tooltip("Ignored when further than this above or below the water.")]
        public float maxHeightAboveWater = 1.5f;

        [Header("Wake trail")]
        public bool leaveTrail = false;
        [Tooltip("Distance travelled through the water between trail ripples.")]
        public float trailSpacing = 0.8f;
        public float trailRadius = 1.2f;
        [Range(0f, 3f)] public float trailStrength = 0.8f;
        public float trailLife = 1.8f;
        [Tooltip("Never drop ripples faster than this (seconds) - keeps fast dashes from flooding the sea with glow.")]
        public float minTrailInterval = 0.07f;

        /// <summary>Extra multiplier other scripts can use (e.g. combo makes the wake brighter).</summary>
        [HideInInspector] public float strengthMultiplier = 1f;

        private Vector3 _lastPos;
        private float _distanceAccumulator;
        private float _lastRippleTime;

        public float CurrentStrength =>
            strength * strengthMultiplier * (1f + (Mathf.Sin(Time.time * pulseSpeed) * 0.5f - 0.5f) * pulse);

        private void OnEnable()
        {
            _lastPos = transform.position;
            _distanceAccumulator = 0f;
            if (OceanController.Instance != null) OceanController.Instance.Register(this);
        }

        private void Start()
        {
            // OceanController may enable after us on scene load; register again (no duplicates).
            if (OceanController.Instance != null) OceanController.Instance.Register(this);
        }

        private void OnDisable()
        {
            if (OceanController.Instance != null) OceanController.Instance.Unregister(this);
        }

        private void Update()
        {
            OceanController ocean = OceanController.Instance;
            if (!leaveTrail || ocean == null) { _lastPos = transform.position; return; }

            // Movement relative to the WATER = our movement + the tide flowing past us.
            Vector3 pos = transform.position;
            Vector3 delta = pos - _lastPos;
            delta.z += ocean.tideSpeed * Time.deltaTime;
            delta.y = 0f;
            _distanceAccumulator += delta.magnitude;
            _lastPos = pos;

            if (_distanceAccumulator >= trailSpacing && Time.time - _lastRippleTime >= minTrailInterval)
            {
                _lastRippleTime = Time.time;
                _distanceAccumulator = 0f;
                if (Mathf.Abs(pos.y - ocean.GetWaterHeight(pos)) < maxHeightAboveWater)
                    ocean.AddRipple(pos, trailRadius, trailStrength * strengthMultiplier, trailLife);
            }
        }
    }
}
