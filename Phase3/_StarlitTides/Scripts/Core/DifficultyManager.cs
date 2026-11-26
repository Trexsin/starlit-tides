// =============================================================================
//  DifficultyManager - the game slowly gets faster, denser and hungrier
// -----------------------------------------------------------------------------
//  Everything is a function of one number, Level (0 at the start of a run,
//  1 after 'rampDuration' seconds). Each value has an AnimationCurve you can
//  reshape in the Inspector; the defaults ease in, so the first minute is
//  relaxed and minutes 2-4 get intense.
//
//      Tide speed (world speed)   7  -> 16 m/s
//      Light drain per second     3.5% -> 7.5%
//      Seconds between obstacles  1.7 -> 0.5
//      Seconds between orb groups 2.4 -> 1.6
//      Jellyfish / log chance     rises with Level
// =============================================================================
using UnityEngine;

namespace StarlitTides
{
    public class DifficultyManager : MonoBehaviour
    {
        public static DifficultyManager Instance { get; private set; }

        [Tooltip("Seconds of play until maximum difficulty.")]
        public float rampDuration = 240f;
        [Tooltip("Tide speed used in the menu / intro (calm drifting sea).")]
        public float idleTideSpeed = 3f;

        [Header("Ranges (start -> max)")]
        public Vector2 tideSpeed = new Vector2(7f, 16f);
        public Vector2 drainPerSecond = new Vector2(0.035f, 0.075f);
        public Vector2 obstacleInterval = new Vector2(1.7f, 0.5f);
        public Vector2 orbGroupInterval = new Vector2(2.4f, 1.6f);
        public Vector2 jellyfishChance = new Vector2(0.1f, 0.4f);
        public Vector2 logChance = new Vector2(0.15f, 0.35f);

        [Header("Shape of the ramp (x = time 0..1, y = level 0..1)")]
        public AnimationCurve rampCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 0.6f), new Keyframe(0.25f, 0.18f), new Keyframe(0.7f, 0.7f), new Keyframe(1f, 1f, 0.6f, 0f));

        /// <summary>0 = start of a run, 1 = maximum difficulty.</summary>
        public float Level { get; private set; }

        public float TideSpeed => Mathf.Lerp(tideSpeed.x, tideSpeed.y, Level);
        public float DrainPerSecond => Mathf.Lerp(drainPerSecond.x, drainPerSecond.y, Level);
        public float ObstacleInterval => Mathf.Lerp(obstacleInterval.x, obstacleInterval.y, Level);
        public float OrbGroupInterval => Mathf.Lerp(orbGroupInterval.x, orbGroupInterval.y, Level);
        public float JellyfishChance => Mathf.Lerp(jellyfishChance.x, jellyfishChance.y, Level);
        public float LogChance => Mathf.Lerp(logChance.x, logChance.y, Level);

        private void Awake() { Instance = this; }
        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void Update()
        {
            GameManager gm = GameManager.Instance;
            OceanController ocean = OceanController.Instance;

            float t = gm != null ? Mathf.Clamp01(gm.RunTime / rampDuration) : 0f;
            Level = Mathf.Clamp01(rampCurve.Evaluate(t));

            if (ocean == null) return;
            float target;
            if (gm != null && gm.State == GameState.Playing) target = TideSpeed;
            else if (gm != null && gm.State == GameState.GameOver) target = idleTideSpeed * 0.5f;
            else target = idleTideSpeed;
            // ease the world speed so changes never feel like a jolt
            ocean.tideSpeed = Mathf.MoveTowards(ocean.tideSpeed, target, 4f * Time.deltaTime);
        }
    }
}
