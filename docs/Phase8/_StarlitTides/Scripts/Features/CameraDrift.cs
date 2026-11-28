// CameraDrift: slowly moves an intro camera so each shot feels alive
// (a gentle "dolly" move). It only moves while the intro is playing.
//
// Phase 8 fix: each shot now drifts from ITS OWN start time on the Timeline
// (startTime). Before, every shot camera started drifting the moment the intro
// began, so a late shot had already travelled a long way before it went live.
// The last shot ("The sea awakens", live at 16.5 s) sank 6.6 m and moved 50 m
// forward: it played inside the waves, in front of the boat, showing only water.

using UnityEngine;

namespace StarlitTides
{
    public class CameraDrift : MonoBehaviour
    {
        [Tooltip("Metres per second, in world space.")]
        public Vector3 velocity = new Vector3(0f, 0f, 2f);
        [Tooltip("Degrees per second to turn left/right.")]
        public float turnSpeed = 0f;
        [Tooltip("Seconds into the intro Timeline when this shot starts. The drift begins here.")]
        public float startTime = 0f;

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
            IntroCinematic intro = IntroCinematic.Instance;
            bool playing = intro != null && intro.IsPlaying;

            // Each time the intro starts, go back to the starting spot.
            if (playing && !wasPlaying) transform.SetPositionAndRotation(startPosition, startRotation);
            wasPlaying = playing;
            if (!playing) return;

            // Time since THIS shot began (0 until it begins), from the Timeline clock.
            float timelineTime = intro.director != null ? (float)intro.director.time : 0f;
            float elapsed = Mathf.Max(0f, timelineTime - startTime);
            transform.SetPositionAndRotation(startPosition + velocity * elapsed,
                                             Quaternion.Euler(0f, turnSpeed * elapsed, 0f) * startRotation);
        }
    }
}
