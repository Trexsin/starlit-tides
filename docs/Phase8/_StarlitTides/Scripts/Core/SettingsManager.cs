// SettingsManager: loads, saves and applies the player's settings.
//
// Everything is kept in PlayerPrefs so it's remembered between sessions:
//   volumes (master, music, effects), quality level (Low / Medium / High),
//   fullscreen, FPS counter, and whether the intro cinematic plays.
// Quality also changes a few scene settings that URP's quality levels can't
// (grass distance, reflection detail, rain amount, ocean shader detail).
// Screen mode and render resolution live in DisplayManager (same object).

using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace StarlitTides
{
    [DefaultExecutionOrder(-60)]
    public class SettingsManager : MonoBehaviour
    {
        public static SettingsManager Instance { get; private set; }

        public float MasterVolume { get; private set; }
        public float MusicVolume { get; private set; }
        public float SfxVolume { get; private set; }
        public int QualityLevel { get; private set; }
        public bool Fullscreen { get; private set; }
        public bool ShowFps { get; private set; }
        public bool PlayIntro { get; private set; }

        public string QualityName { get { return QualitySettings.names[QualityLevel]; } }
        /// <summary>The sharpening strength for the quality level; DisplayManager adds more when upscaling.</summary>
        public float BaseSharpness { get; private set; }

        void Awake()
        {
            Instance = this;
            // Resolution / screen mode (added after the UI was built, so add it if missing).
            if (GetComponent<DisplayManager>() == null) gameObject.AddComponent<DisplayManager>();
            MasterVolume = PlayerPrefs.GetFloat("ST_MasterVolume", 0.9f);
            MusicVolume = PlayerPrefs.GetFloat("ST_MusicVolume", 0.55f);
            SfxVolume = PlayerPrefs.GetFloat("ST_SfxVolume", 0.8f);
            // Settings v2 (the sharper renderer): start everyone on High once.
            if (PlayerPrefs.GetInt("ST_SettingsVersion", 1) < 2)
            {
                PlayerPrefs.DeleteKey("ST_Quality");
                PlayerPrefs.SetInt("ST_SettingsVersion", 2);
            }
            QualityLevel = Mathf.Clamp(PlayerPrefs.GetInt("ST_Quality", QualitySettings.names.Length - 1), 0, QualitySettings.names.Length - 1);
            Fullscreen = PlayerPrefs.GetInt("ST_Fullscreen", 1) == 1;
            ShowFps = PlayerPrefs.GetInt("ST_ShowFps", 0) == 1;
            PlayIntro = PlayerPrefs.GetInt("ST_PlayIntro", 1) == 1;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            ApplyAll();
        }

        public void ApplyAll()
        {
            AudioListener.volume = MasterVolume;
            ApplyAudio();
            ApplyQuality();
            // (Fullscreen and the native size are applied by DisplayManager.Start.)
        }

        void ApplyAudio()
        {
            AudioManager audio = AudioManager.Instance;
            if (audio == null) return;
            audio.musicVolume = MusicVolume;
            audio.sfxVolume = SfxVolume;
        }

        public void SetMasterVolume(float value)
        {
            MasterVolume = value;
            AudioListener.volume = value;
            PlayerPrefs.SetFloat("ST_MasterVolume", value);
        }

        public void SetMusicVolume(float value)
        {
            MusicVolume = value;
            PlayerPrefs.SetFloat("ST_MusicVolume", value);
            ApplyAudio();
        }

        public void SetSfxVolume(float value)
        {
            SfxVolume = value;
            PlayerPrefs.SetFloat("ST_SfxVolume", value);
            ApplyAudio();
        }

        // Moves the quality one step up (+1) or down (-1), wrapping around.
        public void StepQuality(int direction)
        {
            int count = QualitySettings.names.Length;
            QualityLevel = (QualityLevel + direction + count) % count;
            PlayerPrefs.SetInt("ST_Quality", QualityLevel);
            ApplyQuality();
        }

        public void SetFullscreen(bool value)
        {
            Fullscreen = value;
            PlayerPrefs.SetInt("ST_Fullscreen", value ? 1 : 0);
            // Fullscreen at the display's native size, or a 75% window (not Unity's
            // last saved size, which could be tiny or stretched).
            if (DisplayManager.Instance != null) DisplayManager.Instance.SetFullscreen(value);
            else Screen.fullScreen = value;
        }

        public void SetShowFps(bool value)
        {
            ShowFps = value;
            PlayerPrefs.SetInt("ST_ShowFps", value ? 1 : 0);
        }

        public void SetPlayIntro(bool value)
        {
            PlayIntro = value;
            PlayerPrefs.SetInt("ST_PlayIntro", value ? 1 : 0);
        }

        // Applies the URP quality level, then the extra scene tweaks for it.
        // 0 = lowest. The project's levels are Low, Medium, High.
        void ApplyQuality()
        {
            QualitySettings.SetQualityLevel(QualityLevel, true);
            int maxLevel = Mathf.Max(1, QualitySettings.names.Length - 1);
            float q = QualityLevel / (float)maxLevel;   // 0 on Low, 1 on High

            // Grass: how far it's drawn and how thick it is.
            foreach (Terrain terrain in Terrain.activeTerrains)
            {
                terrain.detailObjectDistance = Mathf.Lerp(55f, 120f, q);
                terrain.detailObjectDensity = Mathf.Lerp(0.45f, 1f, q);
            }

            // Sea reflections: a smaller probe texture is much cheaper to render.
            ReflectionProbe probe = FindAnyObjectByType<ReflectionProbe>();
            if (probe != null)
            {
                probe.resolution = q < 0.25f ? 128 : q < 0.75f ? 256 : 512;
                if (ReflectionProbeRefresher.Instance != null) ReflectionProbeRefresher.Instance.Refresh();
            }

            // Rain: fewer drops on lower settings.
            WeatherManager weather = WeatherManager.Instance;
            if (weather != null) weather.maxRainRate = Mathf.Lerp(500f, 1400f, q);

            // Anti-aliasing: High uses 4x MSAA and Medium 2x (set in their URP assets), which
            // keeps edges sharp. Low has no MSAA, so it gets SMAA on the camera instead.
            Camera cam = Camera.main;
            if (cam != null)
            {
                UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
                data.antialiasing = q < 0.25f ? AntialiasingMode.SubpixelMorphologicalAntiAliasing : AntialiasingMode.None;
                data.antialiasingQuality = AntialiasingQuality.High;
            }

            // Contrast-adaptive sharpening after post-processing (StarlitSharpen.shader).
            // Low gets the strongest pass. DisplayManager adds more when the 3D is
            // rendered below the screen's resolution, and sets URP's render scale on
            // this level's asset (each quality level has its own URP asset).
            BaseSharpness = Mathf.Lerp(0.7f, 0.45f, q);
            if (DisplayManager.Instance != null) DisplayManager.Instance.ApplyRenderScale();
            else Shader.SetGlobalFloat("_StarlitSharpness", BaseSharpness);

            // STARLIT_QUALITY_LOW (Low only) switches off, in the shaders: ocean ripple
            // detail, lantern reflections, the boat's glowing wake and the star glints
            // (StarlitOcean), and the outline pass and paint noise (StarlitStylized).
            if (q < 0.25f) Shader.EnableKeyword("STARLIT_QUALITY_LOW");
            else Shader.DisableKeyword("STARLIT_QUALITY_LOW");

            // Phase 8: moon god rays (a 28-step screen-space march) on High only.
            SkyController sky = FindAnyObjectByType<SkyController>();
            if (sky != null) sky.godRayQuality = q > 0.75f ? 1f : 0f;
            // (The sea field reads the quality itself: AmbientSeaField, 45% .. 100% density.)

            PlayerPrefs.Save();
        }
    }
}
