// CameraAnchor: the point the gameplay camera follows instead of the boat.
//
// It copies the boat's forward/back position but only part of its sideways
// position (followX = 0.55). So when you steer to the side, the camera drifts
// only about half way: the boat visibly moves across the screen and you can
// see the glowing buoys that mark the channel edge. With followX = 1 the boat
// would sit dead centre and the lane edges would feel like invisible walls.
//
// It runs after BoatController (execution order) and before Cinemachine
// (which updates in LateUpdate), so the camera never lags a frame behind.

using UnityEngine;

namespace StarlitTides
{
    [DefaultExecutionOrder(100)]
    public class CameraAnchor : MonoBehaviour
    {
        public Transform boat;
        [Tooltip("How much of the boat's sideways movement the camera follows (0..1).")]
        [Range(0f, 1f)] public float followX = 0.55f;
        [Tooltip("How much of the boat's bobbing the camera follows (0..1). Low = steadier view.")]
        [Range(0f, 1f)] public float followY = 0.25f;

        void LateUpdate()
        {
            if (boat == null)
            {
                if (BoatController.Instance == null) return;
                boat = BoatController.Instance.transform;
            }

            Vector3 p = boat.position;
            Vector3 anchor = new Vector3(p.x * followX, p.y * followY, p.z);

            // Phase 9: a light, smooth shake when the boat is fast (SpeedFx).
            if (SpeedFx.Instance != null && Time.timeScale > 0f)
            {
                float amount = SpeedFx.Instance.Shake;
                float t = Time.time * 9f;
                anchor += new Vector3(Mathf.PerlinNoise(t, 0.3f) - 0.5f, Mathf.PerlinNoise(0.7f, t) - 0.5f, 0f) * (2f * amount);
            }
            transform.position = anchor;
        }
    }
}
