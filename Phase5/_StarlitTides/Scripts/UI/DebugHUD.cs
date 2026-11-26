// DebugHUD: simple on-screen info for testing, drawn with Unity's built-in GUI.
//
// No Canvas or fonts needed. It shows the light meter, score, combo,
// Starburst, weather, pop-ups (near miss, whale...), the intro title, pause,
// and the Game Over screen with run stats, high score and the online
// leaderboard (Bonus 11). Phase 6 replaces it with the real HUD.

using System.Collections.Generic;
using UnityEngine;

namespace StarlitTides
{
    public class DebugHUD : MonoBehaviour
    {
        public LightMeter lightMeter;
        public BoatController boat;

        GUIStyle small;
        GUIStyle centred;
        GUIStyle big;
        GUIStyle title;
        string popupText;
        float popupTime = -10f;

        // Game over / leaderboard state
        bool runRecorded;
        string playerName = "";
        string leaderboardStatus = "";
        bool submitted;
        bool submitting;
        List<LeaderboardClient.Entry> topScores = new List<LeaderboardClient.Entry>();

        void OnEnable()
        {
            NearMissDetector.OnNearMiss += ShowNearMiss;
            PowerUpManager.OnStarburstStarted += ShowStarburst;
            WeatherManager.OnWeatherChanged += ShowWeather;
            ComboSystem.OnComboEnded += ShowComboLost;
            WhaleEvent.OnWarning += ShowWhaleWarning;
            HighScoreManager.OnRunRecorded += HandleRunRecorded;
        }

        void OnDisable()
        {
            NearMissDetector.OnNearMiss -= ShowNearMiss;
            PowerUpManager.OnStarburstStarted -= ShowStarburst;
            WeatherManager.OnWeatherChanged -= ShowWeather;
            ComboSystem.OnComboEnded -= ShowComboLost;
            WhaleEvent.OnWarning -= ShowWhaleWarning;
            HighScoreManager.OnRunRecorded -= HandleRunRecorded;
            StarlitInput.TextFieldFocused = false;
        }

        void ShowNearMiss(int points) { Popup("NEAR MISS  +" + points); }
        void ShowStarburst() { Popup("STARBURST!  x2"); }
        void ShowWeather(Weather weather) { if (weather != Weather.Calm) Popup(weather == Weather.Storm ? "A storm is rolling in..." : "The aurora awakens"); }
        void ShowComboLost(int combo) { if (combo >= 5) Popup("Combo lost (" + combo + ")"); }
        void ShowWhaleWarning() { Popup("Something stirs beneath the tide..."); }

        void Popup(string text)
        {
            popupText = text;
            popupTime = Time.unscaledTime;
        }

        // The run is over and saved: get ready to show the stats and the online top 10.
        void HandleRunRecorded(bool newBest)
        {
            runRecorded = true;
            submitted = false;
            leaderboardStatus = "";
            if (HighScoreManager.Instance != null) playerName = HighScoreManager.Instance.PlayerName;
            RefreshTopScores();
        }

        void RefreshTopScores()
        {
            LeaderboardClient client = LeaderboardClient.Instance;
            if (client == null) return;
            client.FetchTop(10, (ok, entries, error) =>
            {
                if (ok) topScores = entries;
                else leaderboardStatus = error;
            });
        }

        void Submit()
        {
            LeaderboardClient client = LeaderboardClient.Instance;
            if (client == null || submitting || submitted) return;
            if (HighScoreManager.Instance != null) HighScoreManager.Instance.PlayerName = playerName;

            submitting = true;
            leaderboardStatus = "Sending...";
            client.SubmitScore(playerName, (ok, rank, error) =>
            {
                submitting = false;
                submitted = ok;
                leaderboardStatus = ok ? "You're #" + rank + " in the world!" : error;
                if (ok) RefreshTopScores();
            });
        }

        void MakeStyles()
        {
            small = new GUIStyle(GUI.skin.label);
            small.fontSize = 18;
            small.normal.textColor = new Color(0.85f, 0.95f, 1f);

            centred = new GUIStyle(small);
            centred.alignment = TextAnchor.MiddleCenter;

            big = new GUIStyle(small);
            big.fontSize = 32;
            big.alignment = TextAnchor.MiddleCenter;

            title = new GUIStyle(big);
            title.fontSize = 64;
            title.normal.textColor = new Color(1f, 0.9f, 0.7f);
        }

        void OnGUI()
        {
            if (small == null) MakeStyles();

            GameManager game = GameManager.Instance;
            if (game != null && game.State == GameState.Intro)
            {
                DrawIntro();
                return;
            }

            if (game != null && game.State == GameState.GameOver && runRecorded)
            {
                DrawGameOver();
                return;
            }
            StarlitInput.TextFieldFocused = false;

            DrawLightBar();
            DrawStats();
            DrawCentreMessages(game);
        }

        void DrawIntro()
        {
            float fade = Mathf.Clamp01(Mathf.Sin(Mathf.Clamp01(Time.timeSinceLevelLoad / 8f) * Mathf.PI) * 2f);
            Color old = GUI.color;
            GUI.color = new Color(1f, 1f, 1f, fade);
            GUI.Label(new Rect(0, Screen.height * 0.2f, Screen.width, 80), "STARLIT TIDES", title);
            GUI.color = old;
            GUI.Label(new Rect(0, Screen.height - 60, Screen.width, 40), "Space / Enter to skip", centred);
        }

        void DrawLightBar()
        {
            float light = lightMeter != null ? lightMeter.SmoothValue : 0f;
            Rect bar = new Rect(24, 24, 320, 18);

            GUI.color = new Color(0f, 0f, 0f, 0.5f);
            GUI.DrawTexture(bar, Texture2D.whiteTexture);
            GUI.color = Color.Lerp(new Color(1f, 0.3f, 0.3f), new Color(0.3f, 0.95f, 1f), light);
            GUI.DrawTexture(new Rect(bar.x, bar.y, bar.width * Mathf.Clamp01(light), bar.height), Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUI.Label(new Rect(24, 44, 600, 26), "LIGHT " + Mathf.RoundToInt(light * 100) + "%", small);
        }

        void DrawStats()
        {
            float y = 70;
            ScoreManager score = ScoreManager.Instance;
            if (score != null)
            {
                GUI.Label(new Rect(24, y, 700, 26), "SCORE " + score.Score.ToString("N0") + "    ORBS " + score.OrbsCollected + "    " + Mathf.RoundToInt(score.Distance) + " m", small);
                y += 26;
                GUI.Label(new Rect(24, y, 700, 26), "MULTIPLIER x" + score.TotalMultiplier.ToString("0.0") + "    NEAR MISSES " + score.NearMisses, small);
                y += 26;
            }

            ComboSystem combo = ComboSystem.Instance;
            if (combo != null && combo.Combo > 0)
            {
                GUI.Label(new Rect(24, y, 700, 26), "COMBO " + combo.Combo + "  (x" + combo.Multiplier.ToString("0.0") + ")", small);
                GUI.color = new Color(1f, 0.85f, 0.4f);
                GUI.DrawTexture(new Rect(24, y + 24, 160 * combo.TimeLeft01, 4), Texture2D.whiteTexture);
                GUI.color = Color.white;
                y += 32;
            }

            PowerUpManager power = PowerUpManager.Instance;
            if (power != null && power.StarburstActive)
            {
                GUI.Label(new Rect(24, y, 700, 26), "STARBURST  x2", small);
                GUI.color = new Color(1f, 0.8f, 0.3f);
                GUI.DrawTexture(new Rect(24, y + 24, 160 * power.StarburstTimeLeft01, 4), Texture2D.whiteTexture);
                GUI.color = Color.white;
                y += 32;
            }

            WeatherManager weather = WeatherManager.Instance;
            if (weather != null) GUI.Label(new Rect(24, y, 700, 26), "WEATHER " + weather.Current, small);
            y += 26;

            if (boat != null)
                GUI.Label(new Rect(24, y, 700, 26), boat.DashCooldown01 >= 1f ? "DASH READY [Space]   PHOTO MODE [P]" : "dash recharging...", small);

            HighScoreManager best = HighScoreManager.Instance;
            if (best != null)
                GUI.Label(new Rect(Screen.width - 260, 24, 240, 26), "BEST " + best.BestScore.ToString("N0"), small);
        }

        void DrawCentreMessages(GameManager game)
        {
            if (Time.unscaledTime - popupTime < 1.8f)
                GUI.Label(new Rect(0, Screen.height * 0.25f, Screen.width, 50), popupText, big);

            if (game == null) return;
            Rect centre = new Rect(0, Screen.height * 0.4f, Screen.width, 60);

            if (game.State == GameState.Paused)
                GUI.Label(centre, "PAUSED - Esc to resume", big);
            else if (game.State == GameState.GameOver)
                GUI.Label(centre, "The tide has gone dark...", big);
            else if (game.InGracePeriod)
                GUI.Label(centre, "Collect the glowing plankton - dodge the dark", big);
        }

        // ------------------------------------------------------------------
        // Bonus 11: end-of-run stats + online leaderboard
        // ------------------------------------------------------------------
        void DrawGameOver()
        {
            HighScoreManager scores = HighScoreManager.Instance;
            if (scores == null) return;
            HighScoreManager.RunStats run = scores.LastRun;

            float width = 760, height = 520;
            Rect panel = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            GUI.color = new Color(0.02f, 0.03f, 0.08f, 0.85f);
            GUI.DrawTexture(panel, Texture2D.whiteTexture);
            GUI.color = Color.white;

            GUILayout.BeginArea(new Rect(panel.x + 30, panel.y + 20, width - 60, height - 40));
            GUILayout.Label("The tide has gone dark...", big);
            if (scores.LastRunWasBest) GUILayout.Label("NEW HIGH SCORE!", centred);
            GUILayout.Space(10);

            GUILayout.BeginHorizontal();

            // Left column: this run.
            GUILayout.BeginVertical(GUILayout.Width(330));
            GUILayout.Label("Score        " + run.score.ToString("N0") + "   (best " + scores.BestScore.ToString("N0") + ")", small);
            GUILayout.Label("Orbs         " + run.orbs, small);
            GUILayout.Label("Distance     " + run.distance.ToString("N0") + " m", small);
            GUILayout.Label("Time         " + (run.seconds / 60) + ":" + (run.seconds % 60).ToString("00"), small);
            GUILayout.Label("Best combo   " + run.bestCombo, small);
            GUILayout.Label("Near misses  " + run.nearMisses, small);
            GUILayout.Label("Runs played  " + scores.TotalRuns, small);
            GUILayout.Space(14);

            GUILayout.Label("Your name for the global leaderboard:", small);
            GUI.SetNextControlName("NameField");
            playerName = GUILayout.TextField(playerName, 16, GUILayout.Width(300));
            StarlitInput.TextFieldFocused = GUI.GetNameOfFocusedControl() == "NameField";

            GUI.enabled = !submitted && !submitting && playerName.Trim().Length > 0;
            if (GUILayout.Button(submitted ? "Submitted" : "Submit score", GUILayout.Width(300))) Submit();
            GUI.enabled = true;
            GUILayout.Label(leaderboardStatus, small);
            GUILayout.EndVertical();

            // Right column: the world's top 10.
            GUILayout.BeginVertical();
            GUILayout.Label("WORLD TOP 10", small);
            if (topScores.Count == 0) GUILayout.Label("(loading or empty)", small);
            foreach (LeaderboardClient.Entry e in topScores)
                GUILayout.Label(e.rank + ".  " + e.name + "   " + e.score.ToString("N0"), small);
            GUILayout.EndVertical();

            GUILayout.EndHorizontal();
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("Try again  (R)")) GameManager.Instance.Restart();
            GUILayout.EndArea();
        }
    }
}
