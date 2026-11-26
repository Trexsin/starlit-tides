// DebugHUD: simple on-screen info for testing, drawn with Unity's built-in GUI.
//
// No Canvas or fonts needed. It shows the light meter, score, combo,
// Starburst, weather and pop-ups for near misses, plus the intro title,
// pause and game over messages. Phase 6 replaces it with the real HUD.

using UnityEngine;

namespace StarlitTides
{
    public class DebugHUD : MonoBehaviour
    {
        public LightMeter lightMeter;
        public BoatController boat;

        GUIStyle small;
        GUIStyle big;
        GUIStyle title;
        string popupText;
        float popupTime = -10f;

        void OnEnable()
        {
            NearMissDetector.OnNearMiss += ShowNearMiss;
            PowerUpManager.OnStarburstStarted += ShowStarburst;
            WeatherManager.OnWeatherChanged += ShowWeather;
            ComboSystem.OnComboEnded += ShowComboLost;
        }

        void OnDisable()
        {
            NearMissDetector.OnNearMiss -= ShowNearMiss;
            PowerUpManager.OnStarburstStarted -= ShowStarburst;
            WeatherManager.OnWeatherChanged -= ShowWeather;
            ComboSystem.OnComboEnded -= ShowComboLost;
        }

        void ShowNearMiss(int points) { Popup("NEAR MISS  +" + points); }
        void ShowStarburst() { Popup("STARBURST!  x2"); }
        void ShowWeather(Weather weather) { if (weather != Weather.Calm) Popup(weather == Weather.Storm ? "A storm is rolling in..." : "The aurora awakens"); }
        void ShowComboLost(int combo) { if (combo >= 5) Popup("Combo lost (" + combo + ")"); }

        void Popup(string text)
        {
            popupText = text;
            popupTime = Time.unscaledTime;
        }

        void MakeStyles()
        {
            small = new GUIStyle(GUI.skin.label);
            small.fontSize = 18;
            small.normal.textColor = new Color(0.85f, 0.95f, 1f);

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
            GUIStyle centred = new GUIStyle(small);
            centred.alignment = TextAnchor.MiddleCenter;
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
                GUI.Label(new Rect(24, y, 700, 26), boat.DashCooldown01 >= 1f ? "DASH READY [Space]" : "dash recharging...", small);
        }

        void DrawCentreMessages(GameManager game)
        {
            if (Time.unscaledTime - popupTime < 1.6f)
                GUI.Label(new Rect(0, Screen.height * 0.25f, Screen.width, 50), popupText, big);

            if (game == null) return;
            Rect centre = new Rect(0, Screen.height * 0.4f, Screen.width, 60);

            if (game.State == GameState.Paused)
            {
                GUI.Label(centre, "PAUSED - Esc to resume", big);
            }
            else if (game.State == GameState.GameOver)
            {
                GUI.Label(centre, "The tide has gone dark...", big);
                ScoreManager score = ScoreManager.Instance;
                if (score != null)
                {
                    GUIStyle centred = new GUIStyle(small);
                    centred.alignment = TextAnchor.MiddleCenter;
                    GUI.Label(new Rect(0, centre.y + 60, Screen.width, 40), "Score " + score.Score.ToString("N0") + "  -  press R to try again", centred);
                }
            }
            else if (game.InGracePeriod)
            {
                GUI.Label(centre, "Collect the glowing plankton - dodge the dark", big);
            }
        }
    }
}
