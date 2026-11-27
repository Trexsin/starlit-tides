// PlayArea: the one place that says how wide the play lane is.
//
// The boat, the spawners, the camera, the lane markers and the ocean shader
// all read these numbers, so widening the lane is a single change here.
// (Before Phase 8 the same numbers were copied into five scripts.)
//
//   |<------------- obstacles (HalfWidth + obstacleOverhang) ------------->|
//      |<---------------- boat (HalfWidth) ---------------->|
//        |<------------ orbs (HalfWidth - orbInset) ------------>|
//   buoys at +-EdgeMarkerX, just outside everything
//
// Obstacles spawn a little past the boat's reach, so the lane edges are not
// a free safe zone. Orbs stay a little inside it, so every orb can be reached.
//
// Density: the difficulty numbers in DifficultyManager were tuned for the
// Phase 7 lane. DensityScale says how much wider the lane is now, and the
// spawners spawn that much more often, so the sea is just as busy per metre.

using UnityEngine;

namespace StarlitTides
{
    [DefaultExecutionOrder(-500)]
    public class PlayArea : MonoBehaviour
    {
        public static PlayArea Instance { get; private set; }

        [Header("Lane (world X, metres from the centre)")]
        [Tooltip("How far left/right the boat can steer.")]
        public float laneHalfWidth = 22f;
        [Tooltip("Obstacles spawn this much further out than the boat can go.")]
        public float obstacleOverhang = 2f;
        [Tooltip("Orbs stay this much inside the boat's reach.")]
        public float orbInset = 0.5f;
        [Tooltip("The glowing buoys and reefs that mark the channel edge.")]
        public float edgeMarkerX = 27f;

        [Header("Lane (world Z)")]
        [Tooltip("How far back/forward the boat can move.")]
        public Vector2 laneZ = new Vector2(-4f, 16f);

        [Header("Difficulty")]
        [Tooltip("The obstacle half width the DifficultyManager numbers were tuned for (Phase 7).")]
        public float referenceObstacleHalfWidth = 14f;

        // Defaults used when there is no PlayArea in the scene (old scenes, tests).
        const float DefaultHalfWidth = 22f, DefaultOverhang = 2f, DefaultInset = 0.5f, DefaultMarker = 27f, DefaultReference = 14f;

        public static float HalfWidth { get { return Instance != null ? Instance.laneHalfWidth : DefaultHalfWidth; } }
        public static float ObstacleHalfWidth { get { return HalfWidth + (Instance != null ? Instance.obstacleOverhang : DefaultOverhang); } }
        public static float OrbHalfWidth { get { return HalfWidth - (Instance != null ? Instance.orbInset : DefaultInset); } }
        public static float EdgeMarkerX { get { return Instance != null ? Instance.edgeMarkerX : DefaultMarker; } }
        public static Vector2 LaneZ { get { return Instance != null ? Instance.laneZ : new Vector2(-4f, 16f); } }

        // How many times busier the spawners must be to keep the same density.
        public static float DensityScale
        {
            get
            {
                float reference = Instance != null ? Instance.referenceObstacleHalfWidth : DefaultReference;
                return Mathf.Max(ObstacleHalfWidth / Mathf.Max(reference, 1f), 0.25f);
            }
        }

        static readonly int LaneHalfWidthId = Shader.PropertyToID("_StarlitLaneHalfWidth");

        void Awake()
        {
            Instance = this;
            Shader.SetGlobalFloat(LaneHalfWidthId, laneHalfWidth);
        }

        void OnValidate()
        {
            laneHalfWidth = Mathf.Max(laneHalfWidth, 2f);
            edgeMarkerX = Mathf.Max(edgeMarkerX, laneHalfWidth + obstacleOverhang + 1f);
            if (Application.isPlaying) Shader.SetGlobalFloat(LaneHalfWidthId, laneHalfWidth);
        }

        void OnDestroy()
        {
            if (Instance == this)
            {
                Instance = null;
                Shader.SetGlobalFloat(LaneHalfWidthId, 0f);   // 0 switches the ocean's lane tint off
            }
        }
    }
}
