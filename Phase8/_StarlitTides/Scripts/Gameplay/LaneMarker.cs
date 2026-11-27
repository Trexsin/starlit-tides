// LaneMarker: a glowing channel buoy or a small reef that marks the lane edge.
//
// It is pure decoration: it drifts with the tide like everything else, but has
// no collider, so it can never hit the boat (the boat can't reach it anyway).
// The light on top breathes slowly, each marker out of step with the others.

using UnityEngine;

namespace StarlitTides
{
    public class LaneMarker : DriftingObject
    {
        [Tooltip("The glow halo (and lamp) that pulses.")]
        public Transform glow;
        public float pulseSpeed = 1.4f;
        [Range(0f, 1f)] public float pulseAmount = 0.25f;
        [Tooltip("Gentle rocking on the waves (degrees).")]
        public float rockAngle = 6f;

        Vector3 glowScale = Vector3.one;

        void Awake()
        {
            if (glow != null) glowScale = glow.localScale;
        }

        protected override void OnSpawned()
        {
            transform.rotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
        }

        protected override void Tick(float dt)
        {
            float t = Time.time + Seed;
            if (glow != null) glow.localScale = glowScale * (1f + Mathf.Sin(t * pulseSpeed) * pulseAmount);
            if (rockAngle > 0f)
            {
                float yaw = transform.eulerAngles.y;
                transform.rotation = Quaternion.Euler(Mathf.Sin(t * 0.9f) * rockAngle, yaw, Mathf.Cos(t * 0.7f) * rockAngle);
            }
        }
    }
}
