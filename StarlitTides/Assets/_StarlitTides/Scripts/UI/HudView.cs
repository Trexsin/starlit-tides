// HudView: the in-game heads-up display.
//
//   top-left   circular light meter (fills and changes colour, pulses when low)
//   top-right  score (counts up smoothly) and best score
//   top-centre combo counter with its timer bar, Starburst timer
//   centre     pop-up messages (near miss, storm, whale...)
//   bottom     weather, dash ready, optional FPS counter

using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StarlitTides
{
    public class HudView : MonoBehaviour
    {
        [Header("Light meter")]
        public LightMeter lightMeter;
        public Image meterFill;
        public Image meterGlow;
        public TextMeshProUGUI meterText;

        [Header("Score")]
        public TextMeshProUGUI scoreText;
        public TextMeshProUGUI bestText;

        [Header("Combo and Starburst")]
        public CanvasGroup comboGroup;
        public TextMeshProUGUI comboText;
        public Image comboBar;
        public CanvasGroup starburstGroup;
        public Image starburstBar;

        [Header("Messages")]
        public TextMeshProUGUI popupText;
        public TextMeshProUGUI infoText;
        public TextMeshProUGUI fpsText;

        [Header("Voyage zone banner (Phase 9)")]
        public TextMeshProUGUI zoneTitle;
        public TextMeshProUGUI zoneSubtitle;
        public float bannerTime = 4.2f;

        public static HudView Instance { get; private set; }

        static readonly Color FullColor = new Color(0.35f, 0.95f, 1f);
        static readonly Color LowColor = new Color(1f, 0.35f, 0.35f);

        float shownScore;
        float popupTime = -10f;
        float fps;
        float bannerStart = -100f;

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnEnable()
        {
            VoyageDirector.OnZoneStarted += HandleZone;
            NearMissDetector.OnNearMiss += HandleNearMiss;
            PowerUpManager.OnStarburstStarted += HandleStarburst;
            WeatherManager.OnWeatherChanged += HandleWeather;
            WhaleEvent.OnWarning += HandleWhale;
            ComboSystem.OnComboEnded += HandleComboLost;
        }

        void OnDisable()
        {
            VoyageDirector.OnZoneStarted -= HandleZone;
            NearMissDetector.OnNearMiss -= HandleNearMiss;
            PowerUpManager.OnStarburstStarted -= HandleStarburst;
            WeatherManager.OnWeatherChanged -= HandleWeather;
            WhaleEvent.OnWarning -= HandleWhale;
            ComboSystem.OnComboEnded -= HandleComboLost;
        }

        void HandleNearMiss(int points) { Popup("NEAR MISS  +" + points); }
        void HandleStarburst() { Popup("<color=#FFD27A>STARBURST</color>  x2"); }
        void HandleWhale() { Popup("Something stirs beneath the tide..."); }
        void HandleComboLost(int combo) { if (combo >= 5) Popup("Combo lost"); }
        void HandleWeather(Weather weather)
        {
            // With the voyage, the zone banner already says what's coming.
            if (VoyageDirector.Instance != null) return;
            if (weather == Weather.Storm) Popup("A storm is rolling in...");
            if (weather == Weather.Aurora) Popup("The aurora awakens");
        }

        void HandleZone(VoyageDirector.Zone zone, int voyage)
        {
            if (zoneTitle == null) return;
            VoyageDirector director = VoyageDirector.Instance;
            int number = director != null ? director.ZoneIndex + 1 : 1;
            zoneTitle.text = (voyage > 1 ? "VOYAGE " + voyage + "  \u2022  " : "") + "ZONE " + number + "\n<size=135%>" + zone.name.ToUpper() + "</size>";
            if (zoneSubtitle != null) zoneSubtitle.text = zone.subtitle;
            bannerStart = Time.unscaledTime;
        }

        public void Popup(string message)
        {
            if (popupText == null) return;
            popupText.text = message;
            popupTime = Time.unscaledTime;
        }

        void Update()
        {
            UpdateLightMeter();
            UpdateScore();
            UpdateComboAndStarburst();
            UpdateMessages();
        }

        void UpdateLightMeter()
        {
            if (lightMeter == null) return;
            float light = Mathf.Clamp01(lightMeter.SmoothValue);
            Color color = Color.Lerp(LowColor, FullColor, Mathf.InverseLerp(0.1f, 0.6f, light));

            meterFill.fillAmount = light;
            meterFill.color = color;
            meterText.text = Mathf.RoundToInt(light * 100f) + "<size=60%>%</size>";

            // The glow behind the ring breathes; faster and stronger when the light is low.
            float danger = 1f - Mathf.Clamp01(light / 0.3f);
            float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * (2f + danger * 8f));
            Color glow = color;
            glow.a = Mathf.Lerp(0.25f, 0.6f, light) + danger * pulse * 0.4f;
            meterGlow.color = glow;
        }

        void UpdateScore()
        {
            ScoreManager score = ScoreManager.Instance;
            if (score == null) return;

            // Count up smoothly instead of jumping.
            shownScore = Mathf.Lerp(shownScore, score.Score, 1f - Mathf.Exp(-10f * Time.unscaledDeltaTime));
            if (Mathf.Abs(score.Score - shownScore) < 1f) shownScore = score.Score;
            scoreText.text = Mathf.RoundToInt(shownScore).ToString("N0");

            HighScoreManager best = HighScoreManager.Instance;
            if (best != null) bestText.text = "BEST  " + Mathf.Max(best.BestScore, score.Score).ToString("N0");
        }

        void UpdateComboAndStarburst()
        {
            ComboSystem combo = ComboSystem.Instance;
            bool showCombo = combo != null && combo.Combo >= 2;
            comboGroup.alpha = Mathf.MoveTowards(comboGroup.alpha, showCombo ? 1f : 0f, Time.unscaledDeltaTime * 4f);
            if (showCombo)
            {
                comboText.text = "x" + combo.Multiplier.ToString("0.0") + "  <size=70%>COMBO " + combo.Combo + "</size>";
                comboBar.fillAmount = combo.TimeLeft01;
            }

            PowerUpManager power = PowerUpManager.Instance;
            bool showStarburst = power != null && power.StarburstActive;
            starburstGroup.alpha = Mathf.MoveTowards(starburstGroup.alpha, showStarburst ? 1f : 0f, Time.unscaledDeltaTime * 4f);
            if (showStarburst) starburstBar.fillAmount = power.StarburstTimeLeft01;
        }

        void UpdateMessages()
        {
            // Pop-ups: fade in fast, hold, fade out.
            float age = Time.unscaledTime - popupTime;
            float alpha = age < 0.15f ? age / 0.15f : 1f - Mathf.Clamp01((age - 1.4f) / 0.5f);
            popupText.alpha = Mathf.Clamp01(alpha);
            popupText.transform.localScale = Vector3.one * (1f + Mathf.Max(0f, 0.15f - age) * 2f);

            string weather = WeatherManager.Instance != null ? WeatherManager.Instance.Current.ToString() : "";
            if (VoyageDirector.Instance != null) weather = VoyageDirector.Instance.Current.name;

            // Zone banner: fades in, holds, fades out.
            if (zoneTitle != null)
            {
                float b = Time.unscaledTime - bannerStart;
                float a = b < 0.4f ? b / 0.4f : 1f - Mathf.Clamp01((b - bannerTime) / 0.8f);
                zoneTitle.alpha = Mathf.Clamp01(a);
                zoneTitle.transform.localScale = Vector3.one * (1f + Mathf.Max(0f, 0.4f - b) * 0.25f);
                if (zoneSubtitle != null) zoneSubtitle.alpha = Mathf.Clamp01(a) * 0.9f;
            }
            BoatController boat = BoatController.Instance;
            string dash = boat != null && boat.DashCooldown01 >= 1f ? "DASH READY" : "dash...";
            infoText.text = weather.ToUpper() + "   •   " + dash + "   •   P  PHOTO";

            bool showFps = SettingsManager.Instance != null && SettingsManager.Instance.ShowFps;
            fpsText.enabled = showFps;
            if (showFps)
            {
                float current = 1f / Mathf.Max(Time.unscaledDeltaTime, 0.0001f);
                fps = Mathf.Lerp(fps, current, 0.05f);
                fpsText.text = Mathf.RoundToInt(fps) + " FPS";
            }
        }
    }
}
