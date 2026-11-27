// DifficultyManager: makes the game harder the longer you survive.
//
// Everything depends on one number, Level:
//   0 at the start of a run, 1 after "rampDuration" seconds (4 minutes).
// Each setting below is a (start, max) pair and we blend between them using Level.
// The ramp curve keeps the first minute relaxed and gets steeper after that.

using UnityEngine;

namespace StarlitTides
{
    public class DifficultyManager : MonoBehaviour
    {
        public static DifficultyManager Instance { get; private set; }

        [Tooltip("Seconds of play until maximum difficulty.")]
        public float rampDuration = 240f;
        [Tooltip("Tide speed in the menu and intro (a calm drifting sea).")]
        public float idleTideSpeed = 3f;

        [Header("Start value (x) -> max value (y)")]
        public Vector2 tideSpeed = new Vector2(7f, 16f);              // metres per second
        public Vector2 drainPerSecond = new Vector2(0.035f, 0.075f);  // fraction of the light meter
        public Vector2 obstacleInterval = new Vector2(1.05f, 0.32f);  // seconds between obstacles (wide lane)
        public Vector2 orbGroupInterval = new Vector2(1.8f, 1.15f);   // seconds between orb groups
        public Vector2 jellyfishChance = new Vector2(0.1f, 0.3f);
        public Vector2 logChance = new Vector2(0.15f, 0.25f);
        public Vector2 creatureChance = new Vector2(0.18f, 0.3f);      // manta rays, turtles, dolphins

        [Header("Ramp shape (x = time 0..1, y = level 0..1)")]
        public AnimationCurve rampCurve = new AnimationCurve(
            new Keyframe(0f, 0f, 0f, 0.6f),
            new Keyframe(0.25f, 0.18f),
            new Keyframe(0.7f, 0.7f),
            new Keyframe(1f, 1f, 0.6f, 0f));

        // 0 = start of a run, 1 = hardest.
        public float Level { get; private set; }

        public float TideSpeed { get { return Mathf.Lerp(tideSpeed.x, tideSpeed.y, Level); } }
        public float DrainPerSecond { get { return Mathf.Lerp(drainPerSecond.x, drainPerSecond.y, Level); } }
        public float ObstacleInterval { get { return Mathf.Lerp(obstacleInterval.x, obstacleInterval.y, Level); } }
        public float OrbGroupInterval { get { return Mathf.Lerp(orbGroupInterval.x, orbGroupInterval.y, Level); } }
        public float JellyfishChance { get { return Mathf.Lerp(jellyfishChance.x, jellyfishChance.y, Level); } }
        public float LogChance { get { return Mathf.Lerp(logChance.x, logChance.y, Level); } }
        public float CreatureChance { get { return Mathf.Lerp(creatureChance.x, creatureChance.y, Level); } }

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            GameManager game = GameManager.Instance;

            // How far through the ramp we are (0..1), shaped by the curve.
            float t = 0f;
            if (game != null) t = Mathf.Clamp01(game.RunTime / rampDuration);
            Level = Mathf.Clamp01(rampCurve.Evaluate(t));

            UpdateTideSpeed(game);
        }

        // The tide speed is the speed the whole world moves toward the boat.
        void UpdateTideSpeed(GameManager game)
        {
            OceanController ocean = OceanController.Instance;
            if (ocean == null) return;

            float target = idleTideSpeed;
            if (game != null && game.State == GameState.Playing) target = TideSpeed;
            if (game != null && game.State == GameState.GameOver) target = idleTideSpeed * 0.5f;

            // Change speed gradually so it never feels like a jolt.
            ocean.tideSpeed = Mathf.MoveTowards(ocean.tideSpeed, target, 4f * Time.deltaTime);
        }
    }
}
