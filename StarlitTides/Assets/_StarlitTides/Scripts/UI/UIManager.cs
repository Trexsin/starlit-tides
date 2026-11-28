// UIManager: decides which screen is showing and moves between them.
//
//   Main Menu -> (fade) -> Intro cinematic -> Playing (HUD)
//        |                                      |   Esc
//        +-> How to Play / Settings             +-> Pause -> Settings / Main Menu
//                                               |   light runs out
//                                               +-> Game Over -> Retry / Main Menu
//
// Retry reloads the scene and skips straight back into a new run (no menu,
// no intro). Every change of screen goes through a short fade.

using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace StarlitTides
{
    public class UIManager : MonoBehaviour
    {
        public enum Screen { None, Menu, HowToPlay, Settings, Pause, GameOver }

        // Set before reloading the scene, so a retry skips the menu.
        static bool retryRequested;
        // The intro plays once per session (unless turned off in Settings).
        static bool introPlayedThisSession;

        [Header("Screens")]
        public CanvasGroup menuScreen;
        public CanvasGroup howToPlayScreen;
        public CanvasGroup settingsScreen;
        public CanvasGroup pauseScreen;
        public CanvasGroup gameOverScreen;
        public Canvas hudCanvas;
        public ScreenFader fader;
        public GameOverView gameOverView;
        public SettingsView settingsView;

        [Header("First button selected on each screen (keyboard / gamepad)")]
        public Button menuFirst;
        public Button howToPlayFirst;
        public Button settingsFirst;
        public Button pauseFirst;
        public Button gameOverFirst;

        public Screen Current { get; private set; }
        Screen settingsReturnTo = Screen.Menu;
        bool busy;

        void OnEnable()
        {
            GameManager.OnStateChanged += HandleStateChanged;
            HighScoreManager.OnRunRecorded += HandleRunRecorded;
        }

        void OnDisable()
        {
            GameManager.OnStateChanged -= HandleStateChanged;
            HighScoreManager.OnRunRecorded -= HandleRunRecorded;
        }

        void Start()
        {
            if (retryRequested)
            {
                retryRequested = false;
                Show(Screen.None);
                if (GameManager.Instance != null) GameManager.Instance.StartRun();
                StartCoroutine(fader.FadeIn(0.6f));
            }
            else
            {
                Show(Screen.Menu);
                StartCoroutine(fader.FadeIn(1.5f));
            }
        }

        void Update()
        {
            GameManager game = GameManager.Instance;
            bool playing = game != null && game.State == GameState.Playing;
            hudCanvas.enabled = playing && !PhotoMode.IsActive;

            // Esc / gamepad Start goes "back" from the sub-screens.
            StarlitInput input = StarlitInput.Instance;
            if (input != null && input.PausePressed && !busy)
            {
                if (Current == Screen.HowToPlay) Show(Screen.Menu);
                else if (Current == Screen.Settings) Show(settingsReturnTo);
            }
        }

        // ------------------------------------------------------------------
        // Showing screens
        // ------------------------------------------------------------------
        public void Show(Screen screen)
        {
            Current = screen;
            SetVisible(menuScreen, screen == Screen.Menu);
            SetVisible(howToPlayScreen, screen == Screen.HowToPlay);
            SetVisible(settingsScreen, screen == Screen.Settings);
            SetVisible(pauseScreen, screen == Screen.Pause);
            SetVisible(gameOverScreen, screen == Screen.GameOver);

            if (screen == Screen.Settings && settingsView != null) settingsView.Open();   // (unapplied changes from last time are dropped)
            if (screen == Screen.GameOver && gameOverView != null) gameOverView.Show();

            // Give keyboard / gamepad navigation a starting point.
            Button first = null;
            if (screen == Screen.Menu) first = menuFirst;
            else if (screen == Screen.HowToPlay) first = howToPlayFirst;
            else if (screen == Screen.Settings) first = settingsFirst;
            else if (screen == Screen.Pause) first = pauseFirst;
            else if (screen == Screen.GameOver) first = gameOverFirst;
            if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(first != null ? first.gameObject : null);
        }

        static void SetVisible(CanvasGroup group, bool visible)
        {
            if (group == null) return;
            group.alpha = visible ? 1f : 0f;
            group.interactable = visible;
            group.blocksRaycasts = visible;
        }

        void HandleStateChanged(GameState state)
        {
            if (state == GameState.Paused) Show(Screen.Pause);
            // A run is under way: no menu screen may stay on top of it, however the run started.
            else if (state == GameState.Playing && Current != Screen.None) Show(Screen.None);
        }

        // The run has been saved: now show the Game Over screen.
        void HandleRunRecorded(bool newBest)
        {
            Show(Screen.GameOver);
        }

        // ------------------------------------------------------------------
        // Button actions (hooked up by the UI builder)
        // ------------------------------------------------------------------
        public void Play()
        {
            if (!busy) StartCoroutine(PlayRoutine());
        }

        IEnumerator PlayRoutine()
        {
            busy = true;
            yield return fader.FadeOut(0.6f);
            Show(Screen.None);

            bool playIntro = SettingsManager.Instance == null || SettingsManager.Instance.PlayIntro;
            if (playIntro && !introPlayedThisSession && IntroCinematic.Instance != null)
            {
                introPlayedThisSession = true;
                IntroCinematic.Instance.Play();
            }
            else if (GameManager.Instance != null)
            {
                GameManager.Instance.StartRun();
            }

            yield return fader.FadeIn(0.9f);
            busy = false;
        }

        public void OpenHowToPlay() { Show(Screen.HowToPlay); }
        public void BackToMenu() { Show(Screen.Menu); }

        public void OpenSettings()
        {
            settingsReturnTo = Current == Screen.Pause ? Screen.Pause : Screen.Menu;
            Show(Screen.Settings);
        }

        public void CloseSettings() { Show(settingsReturnTo); }

        public void Resume()
        {
            if (GameManager.Instance != null) GameManager.Instance.Resume();
        }

        public void Retry()
        {
            if (!busy) StartCoroutine(ReloadRoutine(true));
        }

        public void MainMenu()
        {
            if (!busy) StartCoroutine(ReloadRoutine(false));
        }

        IEnumerator ReloadRoutine(bool retry)
        {
            busy = true;
            Time.timeScale = 1f;
            yield return fader.FadeOut(0.5f);
            retryRequested = retry;
            GameManager.Instance.Restart();
        }

        public void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
