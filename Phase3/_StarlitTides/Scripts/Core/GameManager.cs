// =============================================================================
//  GameManager - the game's state machine
// -----------------------------------------------------------------------------
//      Menu  ->  Intro  ->  Playing  <->  Paused
//                              |
//                              v
//                          GameOver  ->  (Retry reloads the scene)
//
//  Other scripts listen to OnStateChanged / OnRunStarted / OnGameOver instead
//  of checking each other every frame.  In Phase 3 the game starts straight
//  in Playing ("Auto Start Gameplay"); Phases 4-6 add the intro and menus.
// =============================================================================
using System;
using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StarlitTides
{
    public enum GameState { Menu, Intro, Playing, Paused, GameOver }

    [DefaultExecutionOrder(-50)]
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        public static event Action<GameState> OnStateChanged;
        public static event Action OnRunStarted;
        public static event Action OnGameOver;

        [Header("References")]
        public BoatController boat;
        public LightMeter lightMeter;

        [Header("Flow")]
        [Tooltip("Phase 3: skip menu and intro, start playing right away.")]
        public bool autoStartGameplay = true;
        [Tooltip("Seconds of calm before obstacles start (lets the player get their bearings).")]
        public float gracePeriod = 2.5f;
        [Tooltip("Seconds the darkness takes to swallow the boat before the game-over screen.")]
        public float darknessDuration = 2.2f;

        public GameState State { get; private set; } = GameState.Menu;
        /// <summary>Seconds spent in the Playing state this run (drives difficulty).</summary>
        public float RunTime { get; private set; }
        public bool IsPlaying => State == GameState.Playing;
        public bool InGracePeriod => IsPlaying && RunTime < gracePeriod;

        private float _timeScaleBeforePause = 1f;

        private void Awake()
        {
            Instance = this;
            Time.timeScale = 1f;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        private void OnEnable()
        {
            if (lightMeter != null) lightMeter.OnEmpty += HandleLightEmpty;
        }

        private void OnDisable()
        {
            if (lightMeter != null) lightMeter.OnEmpty -= HandleLightEmpty;
        }

        private void Start()
        {
            SetState(GameState.Menu);
            if (autoStartGameplay) StartRun();
        }

        private void Update()
        {
            if (State == GameState.Playing) RunTime += Time.deltaTime;

            StarlitInput input = StarlitInput.Instance;
            if (input == null) return;

            if (input.PausePressed)
            {
                if (State == GameState.Playing) Pause();
                else if (State == GameState.Paused) Resume();
            }
            if (State == GameState.GameOver && input.RestartPressed) Restart();
        }

        // ---------------------------------------------------------------------
        public void SetState(GameState next)
        {
            State = next;
            if (StarlitInput.Instance != null)
                StarlitInput.Instance.gameplayEnabled = next == GameState.Playing;
            OnStateChanged?.Invoke(next);
        }

        /// <summary>Begins a run (called by the intro cinematic, or directly in Phase 3).</summary>
        public void StartRun()
        {
            RunTime = 0f;
            Time.timeScale = 1f;
            if (lightMeter != null) lightMeter.ResetMeter();
            SetState(GameState.Playing);
            OnRunStarted?.Invoke();
        }

        public void Pause()
        {
            if (State != GameState.Playing) return;
            _timeScaleBeforePause = Time.timeScale;
            Time.timeScale = 0f;
            SetState(GameState.Paused);
        }

        public void Resume()
        {
            if (State != GameState.Paused) return;
            Time.timeScale = _timeScaleBeforePause <= 0f ? 1f : _timeScaleBeforePause;
            SetState(GameState.Playing);
        }

        public void Restart()
        {
            Time.timeScale = 1f;
            SceneManager.LoadScene(SceneManager.GetActiveScene().buildIndex >= 0
                ? SceneManager.GetActiveScene().buildIndex
                : 0);
        }

        private void HandleLightEmpty()
        {
            if (State == GameState.Playing) StartCoroutine(DarknessRoutine());
        }

        // The light is out: darkness swallows the boat, then the game is over.
        private IEnumerator DarknessRoutine()
        {
            SetState(GameState.GameOver);
            if (boat != null) boat.Sink(darknessDuration);
            Time.timeScale = 1f;
            yield return new WaitForSecondsRealtime(darknessDuration);
            OnGameOver?.Invoke();
        }
    }
}
