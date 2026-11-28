// DifficultyManager: makes the game harder the longer you survive.
//
// Phase 9: with a VoyageDirector in the scene, every number comes from the
// current zone of the voyage (Lantern Bay -> Glow Reef -> ... -> Aurora Deep,
// then faster loops), so the game gets harder in clear steps and each zone has
// its own rules. Level runs 0 -> 1 across the first voyage (the music uses it).
// During a big creature encounter the filler rocks and logs thin out
// (FillerScale), so the creature is the challenge, not the crowd.
//
// Without a VoyageDirector (old scenes) it falls back to the Phase 3 ramp:
// Level 0 -> 1 over "rampDuration" seconds, each setting a (start, max) pair.

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

        [Header("Player speed control (Phase 9)")]
        [Tooltip("Extra sea speed while holding forward (0.35 = +35%).")]
        public float throttleBoost = 0.35f;
        [Tooltip("Less sea speed while holding back (0.2 = -20%).")]
        public float throttleBrake = 0.2f;
        [Tooltip("Extra sea speed right after a forward dash (fades in ~0.7 s).")]
        public float dashSurge = 0.6f;

        float baseTide;

        // 0 = start of a run, 1 = hardest.
        public float Level { get; private set; }

        static VoyageDirector Voyage { get { return VoyageDirector.Instance; } }
        /// <summary>True when the voyage (Phase 9) sets the numbers.</summary>
        public static bool UsesVoyage { get { return VoyageDirector.Instance != null; } }

        /// <summary>1 normally; lower while a big creature encounter is on (fewer rocks and logs).</summary>
        public float FillerScale { get; set; } = 1f;

        public float TideSpeed { get { return Voyage != null ? Voyage.TideSpeed : Mathf.Lerp(tideSpeed.x, tideSpeed.y, Level); } }
        public float DrainPerSecond { get { return Voyage != null ? Voyage.DrainPerSecond : Mathf.Lerp(drainPerSecond.x, drainPerSecond.y, Level); } }
        /// <summary>Seconds between rocks/logs. With the voyage these are real seconds (not scaled by the lane width).</summary>
        public float ObstacleInterval { get { return Voyage != null ? Voyage.FillerInterval / Mathf.Max(0.1f, FillerScale) : Mathf.Lerp(obstacleInterval.x, obstacleInterval.y, Level); } }
        public float OrbGroupInterval { get { return Voyage != null ? Voyage.OrbGroupInterval : Mathf.Lerp(orbGroupInterval.x, orbGroupInterval.y, Level); } }
        public float OrbValue { get { return Voyage != null ? Voyage.OrbValue : 0.12f; } }
        /// <summary>Sideways spacing of chained orbs as a fraction of the boat's reach (voyage).</summary>
        public Vector2 OrbReach { get { return Voyage != null ? Voyage.OrbReach : new Vector2(0.45f, 0.85f); } }
        /// <summary>Seconds between chained orbs (voyage); 0 = the old group patterns.</summary>
        public float OrbGap { get { return Voyage != null ? Voyage.OrbGap : 0f; } }
        public Vector2Int OrbsPerGroup { get { return Voyage != null ? Voyage.Current.orbsPerGroup : new Vector2Int(4, 7); } }
        public float JellyfishChance { get { return Voyage != null ? Voyage.Current.jellyChance : Mathf.Lerp(jellyfishChance.x, jellyfishChance.y, Level); } }
        public float LogChance { get { return Voyage != null ? Voyage.Current.logChance : Mathf.Lerp(logChance.x, logChance.y, Level); } }
        // With the voyage, creatures only come from planned encounters (EncounterDirector).
        public float CreatureChance { get { return Voyage != null ? 0f : Mathf.Lerp(creatureChance.x, creatureChance.y, Level); } }
        /// <summary>No new hazards: the grace period, or the calm rest that opens each zone.</summary>
        public bool Calm { get { GameManager g = GameManager.Instance; return (g != null && g.InGracePeriod) || (Voyage != null && Voyage.InRest); } }

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
            if (Voyage != null)
            {
                Level = Mathf.Clamp01(Voyage.Progress);
            }
            else
            {
                float t = 0f;
                if (game != null) t = Mathf.Clamp01(game.RunTime / rampDuration);
                Level = Mathf.Clamp01(rampCurve.Evaluate(t));
            }

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
            // Gentle: a new zone's extra speed builds up over a few seconds (1.2 m/s per second).
            baseTide = Mathf.MoveTowards(baseTide <= 0f ? ocean.tideSpeed : baseTide, target, 1.2f * Time.deltaTime);

            // The player's throttle: holding forward makes the sea rush past up to 35% faster
            // (you cover more distance, and score it), holding back slows it by up to 20%.
            // A forward dash adds a short 60% surge. Both fade smoothly.
            float mul = 1f;
            BoatController boat = BoatController.Instance;
            if (boat != null && game != null && game.State == GameState.Playing)
                mul = 1f + Mathf.Max(0f, boat.Throttle) * throttleBoost - Mathf.Max(0f, -boat.Throttle) * throttleBrake + boat.Surge * dashSurge;
            ocean.tideSpeed = baseTide * mul;
        }
    }
}
