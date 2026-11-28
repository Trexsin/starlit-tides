// SettingsManager: loads, saves and applies the player's settings.
//
// Everything is kept in PlayerPrefs so it's remembered between sessions:
//   volumes (master, music, effects), quality level (Low / Medium / High),
//   fullscreen, FPS counter, and whether the intro cinematic plays.
// Quality also changes a few scene settings that URP's quality levels can't
// (grass distance, rain amount, ocean shader detail, bloom, god rays).
// Screen mode and the two resolutions live in DisplayManager (same object).
//
// Phase 9: every graphics and power choice is the player's (Settings screen):
//   anti-aliasing, shadows, reflections, colour depth, frame rate, the battery
//   saver, 30 fps in menus and the automatic resolution drop. The defaults are the
//   balanced values; changing the quality preset resets shadows and reflections
//   to that preset's own values, which can then be changed one by one.

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

        // ---- Phase 9: graphics and power options ----
        public static readonly string[] AntiAliasingNames = { "Off", "SMAA", "MSAA 2x", "MSAA 4x" };
        public static readonly string[] ShadowNames = { "Off", "Low", "Medium", "High", "Ultra" };
        public static readonly string[] ReflectionNames = { "Low", "High", "Ultra" };
        public static readonly string[] ColorDepthNames = { "Standard", "High (less banding)" };
        public static readonly string[] FrameRateNames = { "30 fps", "60 fps", "Match display", "Unlimited" };
        public const int FrameRate30 = 0, FrameRate60 = 1, FrameRateMatch = 2, FrameRateUnlimited = 3;

        /// <summary>0 Off, 1 SMAA, 2 MSAA 2x, 3 MSAA 4x.</summary>
        public int AntiAliasing { get; private set; }
        /// <summary>0 Off, 1 Low, 2 Medium, 3 High, 4 Ultra.</summary>
        public int Shadows { get; private set; }
        /// <summary>0 Low (128 px), 1 High (256 px), 2 Ultra (512 px) sea reflections.</summary>
        public int Reflections { get; private set; }
        /// <summary>0 Standard (32-bit HDR), 1 High (64-bit HDR).</summary>
        public int ColorDepth { get; private set; }
        /// <summary>FrameRate30 / FrameRate60 / FrameRateMatch / FrameRateUnlimited.</summary>
        public int FrameRate { get; private set; }
        /// <summary>Menus, pause and game over at 30 fps.</summary>
        public bool MenuLowFps { get; private set; }
        /// <summary>While unplugged: 30 fps and fewer effects (never a lower resolution). Off by default.</summary>
        public bool BatterySaver { get; private set; }
        /// <summary>Lower the 3D resolution by itself if the frame rate keeps dropping.</summary>
        public bool AdaptiveResolution { get; private set; }

        /// <summary>The shadows and reflections a quality preset comes with.</summary>
        public static int PresetShadows(int level) { return Mathf.Clamp(level + 1, 1, 3); }
        public static int PresetReflections(int level) { return level <= 0 ? 0 : 1; }

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
            AntiAliasing = Mathf.Clamp(PlayerPrefs.GetInt("ST_AntiAliasing", 1), 0, AntiAliasingNames.Length - 1);
            Shadows = Mathf.Clamp(PlayerPrefs.GetInt("ST_Shadows", PresetShadows(QualityLevel)), 0, ShadowNames.Length - 1);
            Reflections = Mathf.Clamp(PlayerPrefs.GetInt("ST_Reflections", PresetReflections(QualityLevel)), 0, ReflectionNames.Length - 1);
            ColorDepth = Mathf.Clamp(PlayerPrefs.GetInt("ST_ColorDepth", 0), 0, ColorDepthNames.Length - 1);
            FrameRate = Mathf.Clamp(PlayerPrefs.GetInt("ST_FrameRate", FrameRateMatch), 0, FrameRateNames.Length - 1);
            MenuLowFps = PlayerPrefs.GetInt("ST_MenuLowFps", 1) == 1;
            BatterySaver = PlayerPrefs.GetInt("ST_BatterySaver", 0) == 1;   // off unless the player turns it on
            AdaptiveResolution = PlayerPrefs.GetInt("ST_AdaptiveResolution", 1) == 1;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (Application.isEditor) RestoreAssets();
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
        // The preset brings its own shadows and reflections (changeable afterwards).
        public void StepQuality(int direction)
        {
            int count = QualitySettings.names.Length;
            QualityLevel = (QualityLevel + direction + count) % count;
            PlayerPrefs.SetInt("ST_Quality", QualityLevel);
            Shadows = PresetShadows(QualityLevel);
            Reflections = PresetReflections(QualityLevel);
            PlayerPrefs.SetInt("ST_Shadows", Shadows);
            PlayerPrefs.SetInt("ST_Reflections", Reflections);
            ApplyQuality();
        }

        /// <summary>Sets the quality preset directly (with its shadows and reflections).</summary>
        public void SetQuality(int level)
        {
            int count = QualitySettings.names.Length;
            int target = Mathf.Clamp(level, 0, count - 1);
            if (target == QualityLevel) return;
            StepQuality(target - QualityLevel);
        }

        public void SetAntiAliasing(int value)
        {
            AntiAliasing = Mathf.Clamp(value, 0, AntiAliasingNames.Length - 1);
            PlayerPrefs.SetInt("ST_AntiAliasing", AntiAliasing);
            ApplyQuality();
        }

        public void SetShadows(int value)
        {
            Shadows = Mathf.Clamp(value, 0, ShadowNames.Length - 1);
            PlayerPrefs.SetInt("ST_Shadows", Shadows);
            ApplyQuality();
        }

        public void SetReflections(int value)
        {
            Reflections = Mathf.Clamp(value, 0, ReflectionNames.Length - 1);
            PlayerPrefs.SetInt("ST_Reflections", Reflections);
            ApplyQuality();
        }

        public void SetColorDepth(int value)
        {
            ColorDepth = Mathf.Clamp(value, 0, ColorDepthNames.Length - 1);
            PlayerPrefs.SetInt("ST_ColorDepth", ColorDepth);
            ApplyQuality();
        }

        public void SetFrameRate(int value)
        {
            FrameRate = Mathf.Clamp(value, 0, FrameRateNames.Length - 1);
            PlayerPrefs.SetInt("ST_FrameRate", FrameRate);
            PlayerPrefs.Save();
            PowerManager.Reapply();
        }

        public void SetMenuLowFps(bool value)
        {
            MenuLowFps = value;
            PlayerPrefs.SetInt("ST_MenuLowFps", value ? 1 : 0);
            PlayerPrefs.Save();
            PowerManager.Reapply();
        }

        public void SetBatterySaver(bool value)
        {
            BatterySaver = value;
            PlayerPrefs.SetInt("ST_BatterySaver", value ? 1 : 0);
            PlayerPrefs.Save();
            PowerManager.Reapply();
        }

        public void SetAdaptiveResolution(bool value)
        {
            AdaptiveResolution = value;
            PlayerPrefs.SetInt("ST_AdaptiveResolution", value ? 1 : 0);
            PlayerPrefs.Save();
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

        /// <summary>Re-applies the current quality (the battery saver switching on or off).</summary>
        public void ReapplyQuality() { ApplyQuality(); }

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

            // Sea reflections (the player's choice): a smaller probe texture is much cheaper.
            ReflectionProbe probe = FindAnyObjectByType<ReflectionProbe>();
            if (probe != null)
            {
                int size = Reflections <= 0 ? 128 : Reflections == 1 ? 256 : 512;
                if (probe.resolution != size)
                {
                    probe.resolution = size;
                    if (ReflectionProbeRefresher.Instance != null) ReflectionProbeRefresher.Instance.Refresh();
                }
            }

            // Shadows, MSAA and colour depth live on the quality level's URP asset.
            ApplyPipelineOptions();

            // Rain: fewer drops on lower settings.
            WeatherManager weather = WeatherManager.Instance;
            if (weather != null) weather.maxRainRate = Mathf.Lerp(500f, 1000f, q);   // Phase 9: 1000 on High (was 1400)

            // Anti-aliasing (the player's choice). SMAA is a cheap post pass on the camera;
            // MSAA is set on the URP asset (ApplyPipelineOptions) and costs more memory.
            Camera cam = Camera.main;
            if (cam != null)
            {
                UniversalAdditionalCameraData data = cam.GetUniversalAdditionalCameraData();
                data.antialiasing = AntiAliasing == 1 ? AntialiasingMode.SubpixelMorphologicalAntiAliasing : AntialiasingMode.None;
                data.antialiasingQuality = q < 0.25f ? AntialiasingQuality.Medium : AntialiasingQuality.High;
                cam.allowMSAA = AntiAliasing >= 2;
                data.renderShadows = Shadows > 0;
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

            // Phase 9: bloom was the biggest post-processing cost at native resolution
            // (~3 ms on a MacBook Air). High keeps the smooth high-quality filter; Medium
            // drops it; Low also blurs at quarter size (soft glows hide the difference).
            ApplyBloomQuality(QualityLevel, maxLevel);

            // Phase 8: moon god rays (a 28-step screen-space march) on High only.
            SkyController sky = FindAnyObjectByType<SkyController>();
            // (Off in the battery saver: they are a 16-step screen-space march.)
            if (sky != null) sky.godRayQuality = q > 0.75f && !PowerManager.OnBattery ? 1f : 0f;
            if (PowerManager.Instance != null) PowerManager.Reapply();   // the level's vSync setting, then the frame rate
            // (The sea field reads the quality itself: AmbientSeaField, 45% .. 100% density.)

            PlayerPrefs.Save();
        }

        // ---------------------------------------------------------------- URP asset options

        struct AssetState { public int msaa, shadowRes, cascades; public float distance; public HDRColorBufferPrecision precision; }

        // The URP assets as saved in the project, restored when play mode ends so the
        // editor never saves a runtime choice into them.
        readonly System.Collections.Generic.Dictionary<UniversalRenderPipelineAsset, AssetState> originals =
            new System.Collections.Generic.Dictionary<UniversalRenderPipelineAsset, AssetState>();

        void ApplyPipelineOptions()
        {
            UniversalRenderPipelineAsset asset = QualitySettings.renderPipeline as UniversalRenderPipelineAsset;
            if (asset == null) asset = UnityEngine.Rendering.GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if (asset == null) return;
            if (!originals.ContainsKey(asset))
                originals[asset] = new AssetState
                {
                    msaa = asset.msaaSampleCount, shadowRes = asset.mainLightShadowmapResolution, cascades = asset.shadowCascadeCount,
                    distance = asset.shadowDistance, precision = asset.hdrColorBufferPrecision
                };

            asset.msaaSampleCount = AntiAliasing == 3 ? 4 : AntiAliasing == 2 ? 2 : 1;
            asset.hdrColorBufferPrecision = ColorDepth == 1 ? HDRColorBufferPrecision._64Bits : HDRColorBufferPrecision._32Bits;

            // Off / Low / Medium / High / Ultra: shadow map size, cascades and how far shadows reach.
            int[] res = { 512, 512, 1024, 2048, 4096 };
            int[] cascades = { 1, 1, 1, 2, 4 };
            float[] distance = { 0f, 35f, 50f, 70f, 100f };
            asset.mainLightShadowmapResolution = res[Shadows];
            asset.shadowCascadeCount = cascades[Shadows];
            asset.shadowDistance = distance[Shadows];
        }

        void RestoreAssets()
        {
            foreach (var pair in originals)
            {
                UniversalRenderPipelineAsset a = pair.Key;
                if (a == null) continue;
                a.msaaSampleCount = pair.Value.msaa;
                a.mainLightShadowmapResolution = pair.Value.shadowRes;
                a.shadowCascadeCount = pair.Value.cascades;
                a.shadowDistance = pair.Value.distance;
                a.hdrColorBufferPrecision = pair.Value.precision;
            }
            originals.Clear();
        }

        UnityEngine.Rendering.Volume nightVolume;

        void ApplyBloomQuality(int level, int maxLevel)
        {
            if (nightVolume == null)
            {
                // The night look's volume, found by name (other global volumes are flashes and overlays).
                foreach (UnityEngine.Rendering.Volume v in FindObjectsByType<UnityEngine.Rendering.Volume>())
                    if (v.name == "Global Volume (Starlit Night)" || v.name == "Global Volume") { nightVolume = v; break; }
            }
            if (nightVolume == null || nightVolume.sharedProfile == null) return;
            // .profile makes a runtime copy, so the saved asset is never changed.
            if (nightVolume.profile.TryGet(out UnityEngine.Rendering.Universal.Bloom bloom))
            {
                bloom.highQualityFiltering.Override(level >= maxLevel);
                bloom.downscale.Override(level == 0 ? UnityEngine.Rendering.Universal.BloomDownscaleMode.Quarter : UnityEngine.Rendering.Universal.BloomDownscaleMode.Half);
            }
        }
    }
}
