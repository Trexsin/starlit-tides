// EXTRA FEATURE 4 - Dynamic Weather
//
// The weather changes as your score rises:
//   Calm night  ->  Storm  ->  Aurora night
// Storm: heavy clouds, rain, lightning, rougher waves and thicker fog.
//        Every raindrop that hits the sea makes the plankton spark.
// Aurora: the storm clears and glowing curtains of light fill the sky.
// Changes blend over several seconds, so the sky never "snaps".

using System;
using UnityEngine;

namespace StarlitTides
{
    public enum Weather { Calm, Storm, Aurora }

    public class WeatherManager : MonoBehaviour
    {
        public static WeatherManager Instance { get; private set; }

        public static event Action<Weather> OnWeatherChanged;

        [Header("Score needed for each weather")]
        public int stormAtScore = 3000;
        public int auroraAtScore = 7500;

        [Header("Blending")]
        [Tooltip("Seconds to blend from one weather to the next.")]
        public float transitionTime = 8f;

        [Header("Storm")]
        public ParticleSystem rain;
        public float maxRainRate = 1400f;
        public float stormWaveScale = 1.7f;
        public Vector2 lightningInterval = new Vector2(3f, 8f);
        [Tooltip("Glowing sparks per second where rain hits the sea.")]
        public float rainSparksPerSecond = 5f;

        [Header("Debug: force a weather (leave on Auto to follow the score)")]
        public bool forceWeather = false;
        public Weather forcedWeather = Weather.Calm;

        public Weather Current { get; private set; }

        // 0..1 amounts that ease toward their targets.
        float storm;
        float aurora;
        float lightningTimer;
        float sparkTimer;
        float refreshTimer = -1f;

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
            GameManager.OnRunStarted += HandleRunStarted;
        }

        void OnDisable()
        {
            GameManager.OnRunStarted -= HandleRunStarted;
        }

        void HandleRunStarted()
        {
            SetWeather(Weather.Calm);
        }

        void Update()
        {
            ChooseWeather();
            BlendWeather();
            UpdateRain();
            UpdateLightning();
            RefreshReflectionsWhenSettled();
        }

        void ChooseWeather()
        {
            Weather wanted = Current;
            if (forceWeather)
            {
                wanted = forcedWeather;
            }
            else if (GameManager.Instance != null && GameManager.Instance.IsPlaying && ScoreManager.Instance != null)
            {
                int score = ScoreManager.Instance.Score;
                if (score >= auroraAtScore) wanted = Weather.Aurora;
                else if (score >= stormAtScore) wanted = Weather.Storm;
                else wanted = Weather.Calm;
            }

            if (wanted != Current) SetWeather(wanted);
        }

        void SetWeather(Weather weather)
        {
            if (weather == Current) return;
            Current = weather;
            refreshTimer = transitionTime;   // update reflections once the sky has changed
            if (OnWeatherChanged != null) OnWeatherChanged(weather);
        }

        void BlendWeather()
        {
            float stormTarget = Current == Weather.Storm ? 1f : 0f;
            float auroraTarget = Current == Weather.Aurora ? 1f : 0f;
            float step = Time.deltaTime / transitionTime;
            storm = Mathf.MoveTowards(storm, stormTarget, step);
            aurora = Mathf.MoveTowards(aurora, auroraTarget, step);

            SkyController sky = SkyController.Instance;
            if (sky != null)
            {
                sky.storm = storm;
                sky.aurora = aurora;
            }

            OceanController ocean = OceanController.Instance;
            if (ocean != null) ocean.waveScale = Mathf.Lerp(1f, stormWaveScale, storm);
        }

        void UpdateRain()
        {
            if (rain != null)
            {
                ParticleSystem.EmissionModule emission = rain.emission;
                emission.rateOverTime = maxRainRate * storm * storm;
                if (storm > 0.01f && !rain.isPlaying) rain.Play();
            }

            // Bioluminescent rain: drops hitting the sea make little glowing ripples.
            OceanController ocean = OceanController.Instance;
            if (ocean == null || storm < 0.2f) return;

            sparkTimer -= Time.deltaTime * rainSparksPerSecond * storm;
            while (sparkTimer <= 0f)
            {
                sparkTimer += 1f;
                Vector3 spot = new Vector3(UnityEngine.Random.Range(-14f, 14f), 0f, UnityEngine.Random.Range(0f, 45f));
                ocean.AddRipple(spot, UnityEngine.Random.Range(0.4f, 0.9f), 1.2f, 0.8f);
            }
        }

        void UpdateLightning()
        {
            if (storm < 0.5f) return;

            lightningTimer -= Time.deltaTime;
            if (lightningTimer > 0f) return;
            lightningTimer = UnityEngine.Random.Range(lightningInterval.x, lightningInterval.y);

            if (SkyController.Instance != null)
                SkyController.Instance.TriggerLightning(UnityEngine.Random.Range(0.6f, 1f));
        }

        // Re-render the sea's reflections once the new weather has fully blended in.
        void RefreshReflectionsWhenSettled()
        {
            if (refreshTimer < 0f) return;
            refreshTimer -= Time.deltaTime;
            if (refreshTimer < 0f && ReflectionProbeRefresher.Instance != null)
                ReflectionProbeRefresher.Instance.Refresh();
        }
    }
}
