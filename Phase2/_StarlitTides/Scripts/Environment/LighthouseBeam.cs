// LighthouseBeam: slowly turns the lighthouse lamp so its beams sweep the sea.
//
// It also answers "is this point inside the beam?" (IsLit), which Phase 5
// uses to reveal hidden orbs when the light passes over them.

using UnityEngine;

namespace StarlitTides
{
    public class LighthouseBeam : MonoBehaviour
    {
        public static LighthouseBeam Instance { get; private set; }

        [Tooltip("Degrees per second.")]
        public float rotationSpeed = 22f;
        [Tooltip("The part that turns. Uses this object if empty.")]
        public Transform rotatingHead;
        [Tooltip("Optional spot light that gently pulses.")]
        public Light spotLight;
        public float pulseAmount = 0.12f;

        float baseIntensity;

        // The direction the front beam points, flattened onto the sea.
        public Vector3 BeamDirection
        {
            get
            {
                Vector3 forward = Head.forward;
                forward.y = 0f;
                if (forward.sqrMagnitude < 0.0001f) return Vector3.forward;
                return forward.normalized;
            }
        }

        public Vector3 BeamOrigin { get { return Head.position; } }

        Transform Head { get { return rotatingHead != null ? rotatingHead : transform; } }

        void OnEnable()
        {
            Instance = this;
            if (rotatingHead == null) rotatingHead = transform;
            if (spotLight != null) baseIntensity = spotLight.intensity;
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            rotatingHead.Rotate(Vector3.up, rotationSpeed * Time.deltaTime, Space.World);

            if (spotLight != null)
                spotLight.intensity = baseIntensity * (1f + Mathf.Sin(Time.time * 1.7f) * pulseAmount);
        }

        // True if the point is inside either beam (front or back) and not too far away.
        public bool IsLit(Vector3 worldPoint, float halfAngleDegrees = 8f, float maxDistance = 220f)
        {
            Vector3 toPoint = worldPoint - BeamOrigin;
            toPoint.y = 0f;
            if (toPoint.magnitude > maxDistance) return false;

            float angle = Vector3.Angle(BeamDirection, toPoint);
            bool inFrontBeam = angle < halfAngleDegrees;
            bool inBackBeam = angle > 180f - halfAngleDegrees;
            return inFrontBeam || inBackBeam;
        }
    }
}
