// =============================================================================
//  LighthouseBeam - slowly sweeps the lighthouse light across the sea
// -----------------------------------------------------------------------------
//  Rotates the lamp head (which holds the spot light and the beam cone mesh).
//  Exposes the beam direction so Bonus Feature 8 can reveal hidden orbs that
//  the beam passes over.
// =============================================================================
using UnityEngine;

namespace StarlitTides
{
    public class LighthouseBeam : MonoBehaviour
    {
        public static LighthouseBeam Instance { get; private set; }

        [Tooltip("Degrees per second.")]
        public float rotationSpeed = 22f;
        [Tooltip("The rotating lamp head. Defaults to this transform.")]
        public Transform rotatingHead;
        [Tooltip("Spot light inside the lamp (optional, gets a soft pulse).")]
        public Light spotLight;
        public float pulseAmount = 0.12f;

        private float _baseIntensity;

        /// <summary>World-space direction the main beam is pointing (flattened to XZ).</summary>
        public Vector3 BeamDirection
        {
            get
            {
                Vector3 f = (rotatingHead != null ? rotatingHead : transform).forward;
                f.y = 0f;
                return f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
            }
        }

        public Vector3 BeamOrigin => (rotatingHead != null ? rotatingHead : transform).position;

        private void OnEnable()
        {
            Instance = this;
            if (rotatingHead == null) rotatingHead = transform;
            if (spotLight != null) _baseIntensity = spotLight.intensity;
        }

        private void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            rotatingHead.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.World);
            if (spotLight != null)
                spotLight.intensity = _baseIntensity * (1f + Mathf.Sin(Time.time * 1.7f) * pulseAmount);
        }

        /// <summary>True if a world point is inside the beam (used to reveal hidden orbs).</summary>
        public bool IsLit(Vector3 worldPoint, float halfAngleDeg = 8f, float maxDistance = 220f)
        {
            Vector3 to = worldPoint - BeamOrigin;
            to.y = 0f;
            if (to.magnitude > maxDistance) return false;
            // both beams (front and back of the lamp)
            float angle = Vector3.Angle(BeamDirection, to);
            return angle < halfAngleDeg || angle > 180f - halfAngleDeg;
        }
    }
}
