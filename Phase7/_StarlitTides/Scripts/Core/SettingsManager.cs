// SettingsManager: loads, saves and applies the player's settings.
//
// Everything is kept in PlayerPrefs so it's remembered between sessions:
//   volumes (master, music, effects), quality level (Low / Medium / High),
//   fullscreen, FPS counter, and whether the intro cinematic plays.
// Quality also changes a few scene settings that URP's quality levels can't
// (grass distance, reflection detail, rain amount, ocean shader detail).

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

        void Awake()
        {
            Instance = this;
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
            if (!Application.isEditor) Screen.fullScreen = Fullscreen;
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
            Screen.fullScreen = value;
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

            // Anti-aliasing: Medium/High use 4x MSAA (set in their URP assets), which keeps
            // edges sharp. Low has no MSAA, so it gets SMAA on the camera instead.
            Camera cam = Camera.main;
            if (cam != null)
            {
                UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
                data.antialiasing = q < 0.25f ? AntialiasingMode.SubpixelMorphologicalAntiAliasing : AntialiasingMode.None;
                data.antialiasingQuality = AntialiasingQuality.High;
            }

            // Contrast-adaptive sharpening after post-processing (StarlitSharpen.shader).
            // Low renders at 85% resolution, so it gets the strongest pass.
            Shader.SetGlobalFloat("_StarlitSharpness", Mathf.Lerp(0.7f, 0.45f, q));

            // Ocean shader: the cheaper version skips some ripple detail and the lantern reflections.
            if (q < 0.25f) Shader.EnableKeyword("STARLIT_QUALITY_LOW");
            else Shader.DisableKeyword("STARLIT_QUALITY_LOW");

            PlayerPrefs.Save();
        }
    }
}
