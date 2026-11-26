// CameraDrift: slowly moves an intro camera so each shot feels alive
// (a gentle "dolly" move). It only moves while the intro is playing.

using UnityEngine;

namespace StarlitTides
{
    public class CameraDrift : MonoBehaviour
    {
        [Tooltip("Metres per second, in world space.")]
        public Vector3 velocity = new Vector3(0f, 0f, 2f);
        [Tooltip("Degrees per second to turn left/right.")]
        public float turnSpeed = 0f;

        Vector3 startPosition;
        Quaternion startRotation;
        bool wasPlaying;

        void Awake()
        {
            startPosition = transform.position;
            startRotation = transform.rotation;
        }

        void Update()
        {
            bool playing = IntroCinematic.Instance != null && IntroCinematic.Instance.IsPlaying;

            // Each time the intro starts, go back to the starting spot.
            if (playing && !wasPlaying) transform.SetPositionAndRotation(startPosition, startRotation);
            wasPlaying = playing;
            if (!playing) return;

            transform.position += velocity * Time.deltaTime;
            transform.Rotate(0f, turnSpeed * Time.deltaTime, 0f, Space.World);
        }
    }
}
