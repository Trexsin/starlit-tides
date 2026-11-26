// GameManager: keeps track of what state the game is in.
//
//   Menu -> Intro -> Playing <-> Paused
//                       |
//                       v
//                   GameOver -> Retry (reloads the scene)
//
// Other scripts listen to the events below instead of checking every frame.
// For now (Phase 3) the game skips the menu and starts playing straight away.

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
        [Tooltip("Skip the menu and intro and start playing right away.")]
        public bool autoStartGameplay = true;
        [Tooltip("Calm seconds at the start of a run: no obstacles, no light drain.")]
        public float gracePeriod = 2.5f;
        [Tooltip("How long the boat takes to sink before the game over screen.")]
        public float darknessDuration = 2.2f;

        public GameState State { get; private set; }

        // Seconds spent playing this run. The difficulty is based on this.
        public float RunTime { get; private set; }

        public bool IsPlaying { get { return State == GameState.Playing; } }
        public bool InGracePeriod { get { return IsPlaying && RunTime < gracePeriod; } }

        float timeScaleBeforePause = 1f;

        void Awake()
        {
            Instance = this;
            Time.timeScale = 1f;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnEnable()
        {
            if (lightMeter != null) lightMeter.OnEmpty += HandleLightEmpty;
        }

        void OnDisable()
        {
            if (lightMeter != null) lightMeter.OnEmpty -= HandleLightEmpty;
        }

        void Start()
        {
            SetState(GameState.Menu);
            if (autoStartGameplay) StartRun();
        }

        void Update()
        {
            if (State == GameState.Playing) RunTime += Time.deltaTime;

            StarlitInput input = StarlitInput.Instance;
            if (input == null) return;

            // Esc toggles pause.
            if (input.PausePressed)
            {
                if (State == GameState.Playing) Pause();
                else if (State == GameState.Paused) Resume();
            }

            // R restarts after a game over.
            if (State == GameState.GameOver && input.RestartPressed) Restart();
        }

        public void SetState(GameState newState)
        {
            State = newState;

            // The player can only steer while playing.
            if (StarlitInput.Instance != null)
                StarlitInput.Instance.gameplayEnabled = newState == GameState.Playing;

            if (OnStateChanged != null) OnStateChanged(newState);
        }

        // Starts a new run. Later the intro cutscene will call this.
        public void StartRun()
        {
            RunTime = 0f;
            Time.timeScale = 1f;
            if (lightMeter != null) lightMeter.ResetMeter();
            SetState(GameState.Playing);
            if (OnRunStarted != null) OnRunStarted();
        }

        public void Pause()
        {
            if (State != GameState.Playing) return;
            timeScaleBeforePause = Time.timeScale;
            Time.timeScale = 0f;
            SetState(GameState.Paused);
        }

        public void Resume()
        {
            if (State != GameState.Paused) return;
            Time.timeScale = timeScaleBeforePause > 0f ? timeScaleBeforePause : 1f;
            SetState(GameState.Playing);
        }

        public void Restart()
        {
            Time.timeScale = 1f;
            int sceneIndex = SceneManager.GetActiveScene().buildIndex;
            if (sceneIndex < 0) sceneIndex = 0;
            SceneManager.LoadScene(sceneIndex);
        }

        void HandleLightEmpty()
        {
            if (State == GameState.Playing) StartCoroutine(DarknessRoutine());
        }

        // The light ran out: the boat sinks into the dark, then the game is over.
        IEnumerator DarknessRoutine()
        {
            SetState(GameState.GameOver);
            if (boat != null) boat.Sink(darknessDuration);
            Time.timeScale = 1f;

            yield return new WaitForSecondsRealtime(darknessDuration);

            if (OnGameOver != null) OnGameOver();
        }
    }
}
