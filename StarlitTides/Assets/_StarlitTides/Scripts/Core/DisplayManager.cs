// DisplayManager: screen mode, render resolution and the first-launch auto-detect.
//
// How resolution works here (the way modern games do it):
//   - The window or screen always runs at its real pixel size. In fullscreen that
//     is the display's native size (e.g. 2940 x 1846 on a 13" MacBook Air), so
//     text, the HUD and the UI are always razor sharp.
//   - "Resolution" in Settings is the size the 3D world is rendered at: a
//     percentage of the screen (100%, 90% ... 50%), set as URP's render scale.
//     Below 100% the picture is upscaled with AMD FSR 1 (edge-aware upscaling plus
//     sharpening), which looks much better than the OS stretching a small window.
//
// Before this, the game kept whatever resolution Unity had saved last time
// (1280 x 720 on the dev Mac), so a 2940-pixel-wide screen showed a stretched
// 720p picture: blurry. And native Retina at High was too heavy to hold 60 fps:
// choppy. So on the first launch (and from the AUTO-DETECT button) this:
//   1. switches to fullscreen at the display's native size,
//   2. measures the real frame rate for a few seconds at 100%,
//   3. if it's below the target (the display's refresh rate, max 60 Hz), picks
//      the highest resolution that should reach it (frame time is roughly
//      proportional to the number of pixels), measures again, and if even 60%
//      isn't enough, drops the quality level one step,
//   4. saves the result.

using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StarlitTides
{
    [DefaultExecutionOrder(-55)]
    public class DisplayManager : MonoBehaviour
    {
        public static DisplayManager Instance { get; private set; }

        /// <summary>The render-resolution steps, as a fraction of the screen's pixels.</summary>
        public static readonly float[] Scales = { 1f, 0.9f, 0.8f, 0.7f, 0.6f, 0.5f };

        const string ScaleKey = "ST_RenderScaleIndex";
        const string DetectedKey = "ST_DisplayAutoDetected";   // bump the value to re-run auto-detect for everyone
        const int DetectVersion = 2;   // 2: v1 measured the stale 1280 x 720 size

        public int ScaleIndex { get; private set; }
        public bool Detecting { get; private set; }
        /// <summary>A short line for the Settings screen ("Auto-picked ...", "Testing ...").</summary>
        public string Status { get; private set; }
        public float RenderScale { get { return Scales[ScaleIndex]; } }

        /// <summary>Raised whenever the resolution, mode or status text changes.</summary>
        public event System.Action Changed;

        // The URP assets' render scales as saved in the project, restored when
        // play mode ends so the editor never saves a runtime value into them.
        readonly Dictionary<UniversalRenderPipelineAsset, float> originalScales = new Dictionary<UniversalRenderPipelineAsset, float>();
        readonly Dictionary<UniversalRenderPipelineAsset, UpscalingFilterSelection> originalFilters = new Dictionary<UniversalRenderPipelineAsset, UpscalingFilterSelection>();
        int lastWidth, lastHeight;
        Coroutine detectRoutine;

        void Awake()
        {
            Instance = this;
            ScaleIndex = Mathf.Clamp(PlayerPrefs.GetInt(ScaleKey, 0), 0, Scales.Length - 1);
            Status = "";
        }

        void Start()
        {
            Debug.Log("[Starlit Tides] Display: screen " + Screen.width + "x" + Screen.height + " (" + Screen.fullScreenMode + "), desktop "
                      + Screen.currentResolution.width + "x" + Screen.currentResolution.height + " @ " + RefreshRate().ToString("0") + " Hz"
                      + ", display " + Display.main.systemWidth + "x" + Display.main.systemHeight);

            bool firstLaunch = PlayerPrefs.GetInt(DetectedKey, 0) < DetectVersion;
            bool fullscreen = SettingsManager.Instance == null || SettingsManager.Instance.Fullscreen;
            // Start in the mode Settings says (Unity would otherwise restore whatever
            // mode and size it saved last, e.g. a stretched 1280 x 720).
            if (!Application.isEditor)
            {
                if (fullscreen || firstLaunch) GoFullscreenNative();
                else if (Screen.fullScreenMode != FullScreenMode.Windowed) SetFullscreen(false);
            }
            ApplyRenderScale();

            // Auto-detect needs the real player; in the editor the Game view sets the size.
            if (firstLaunch && !Application.isEditor) AutoDetect();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
            if (Application.isEditor) RestoreAssets();
        }

        void Update()
        {
            // The window was resized, or the fullscreen switch finished: refresh the labels.
            if (Screen.width != lastWidth || Screen.height != lastHeight)
            {
                lastWidth = Screen.width;
                lastHeight = Screen.height;
                if (Changed != null) Changed();
            }
        }

        // ---------------------------------------------------------------- screen mode

        /// <summary>
        /// Fullscreen always runs at the display's own pixel size. (Unity otherwise keeps
        /// the last saved size and stretches it, which is what made the game blurry.)
        /// </summary>
        void GoFullscreenNative()
        {
            Vector2Int native = NativeSize();
            bool wrongSize = Screen.width < native.x * 0.97f;   // compare widths: a notch can trim the height
            if (Screen.fullScreenMode != FullScreenMode.FullScreenWindow || wrongSize)
                Screen.SetResolution(native.x, native.y, FullScreenMode.FullScreenWindow);
        }

        /// <summary>Fullscreen at native size, or a window at 75% of the desktop.</summary>
        public void SetFullscreen(bool fullscreen)
        {
            if (Application.isEditor) return;
            Vector2Int native = NativeSize();
            if (fullscreen)
                Screen.SetResolution(native.x, native.y, FullScreenMode.FullScreenWindow);
            else
                Screen.SetResolution(Mathf.RoundToInt(native.x * 0.75f), Mathf.RoundToInt(native.y * 0.75f), FullScreenMode.Windowed);
        }

        /// <summary>
        /// The display's real pixel size (2940 x 1912 on a 13" MacBook Air at its default
        /// scaling). Not Screen.currentResolution: in fullscreen that reports the game's
        /// current mode (it said 1280 x 720 on the dev Mac), not the display.
        /// </summary>
        public static Vector2Int NativeSize()
        {
            int w = Display.main.systemWidth, h = Display.main.systemHeight;
            if (w <= 0 || h <= 0) { w = Screen.currentResolution.width; h = Screen.currentResolution.height; }
            return new Vector2Int(w, h);
        }

        // ---------------------------------------------------------------- resolution

        /// <summary>Moves one resolution step up (+1 = higher) or down (-1).</summary>
        public void StepResolution(int direction)
        {
            // Index 0 is the highest, so "up" means a lower index. No wrapping: the
            // ends of the list are the ends.
            SetScaleIndex(Mathf.Clamp(ScaleIndex - direction, 0, Scales.Length - 1));
            if (!Detecting) Status = "";
            if (Changed != null) Changed();
        }

        void SetScaleIndex(int index)
        {
            ScaleIndex = index;
            PlayerPrefs.SetInt(ScaleKey, index);
            PlayerPrefs.Save();
            ApplyRenderScale();
        }

        /// <summary>The 3D render size for a step, from the current screen size.</summary>
        public static Vector2Int RenderSize(int index)
        {
            float s = Scales[Mathf.Clamp(index, 0, Scales.Length - 1)];
            return new Vector2Int(Mathf.RoundToInt(Screen.width * s), Mathf.RoundToInt(Screen.height * s));
        }

        /// <summary>"2205 x 1385" for the Settings screen.</summary>
        public string ResolutionLabel()
        {
            Vector2Int size = RenderSize(ScaleIndex);
            return size.x + " x " + size.y;
        }

        /// <summary>The small line under the resolution: what it means, or the auto-detect result.</summary>
        public string DetailLabel()
        {
            if (!string.IsNullOrEmpty(Status)) return Status;
            string screen = Screen.width + " x " + Screen.height;
            return ScaleIndex == 0
                ? "Native: every pixel of the " + screen + " screen"
                : Mathf.RoundToInt(RenderScale * 100f) + "% of " + screen + ", upscaled with AMD FSR";
        }

        /// <summary>
        /// Sets URP's render scale on the active quality level's asset. Called again by
        /// SettingsManager whenever the quality level (and so the asset) changes.
        /// </summary>
        public void ApplyRenderScale()
        {
            UniversalRenderPipelineAsset asset = QualitySettings.renderPipeline as UniversalRenderPipelineAsset;
            if (asset == null) asset = GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
            if (asset == null) return;
            if (!originalScales.ContainsKey(asset))
            {
                originalScales[asset] = asset.renderScale;
                originalFilters[asset] = asset.upscalingFilter;
            }
            float scale = RenderScale;
            asset.renderScale = scale;
            // FSR 1 when upscaling: edge-adaptive upscale + sharpening (cheap, works on Metal).
            asset.upscalingFilter = scale < 0.999f ? UpscalingFilterSelection.FSR : UpscalingFilterSelection.Auto;
            asset.fsrOverrideSharpness = true;
            asset.fsrSharpness = 0.9f;
            // A touch more of our own sharpening pass the further the picture is upscaled.
            float qualitySharpness = SettingsManager.Instance != null ? SettingsManager.Instance.BaseSharpness : 0.45f;
            Shader.SetGlobalFloat("_StarlitSharpness", Mathf.Clamp(qualitySharpness + (1f - scale) * 0.4f, 0f, 0.9f));
            if (Changed != null) Changed();
        }

        void RestoreAssets()
        {
            foreach (KeyValuePair<UniversalRenderPipelineAsset, float> pair in originalScales)
            {
                if (pair.Key == null) continue;
                pair.Key.renderScale = pair.Value;
                pair.Key.upscalingFilter = originalFilters[pair.Key];
            }
            originalScales.Clear();
            originalFilters.Clear();
        }

        // ---------------------------------------------------------------- auto-detect

        public void AutoDetect()
        {
            if (detectRoutine != null) StopCoroutine(detectRoutine);
            detectRoutine = StartCoroutine(AutoDetectRoutine());
        }

        static float RefreshRate()
        {
            double hz = Screen.currentResolution.refreshRateRatio.value;
            return hz > 1.0 ? (float)hz : 60f;
        }

        IEnumerator AutoDetectRoutine()
        {
            Detecting = true;
            SettingsManager settings = SettingsManager.Instance;

            // Start from the best: fullscreen, native, 100%.
            if (!Application.isEditor)
            {
                if (settings != null && !settings.Fullscreen) settings.SetFullscreen(true);
                else SetFullscreen(true);
            }
            SetScaleIndex(0);

            // The goal is a steady 60 fps (or the display's rate if lower). The quality
            // levels use vSync, which caps the frame rate at the refresh rate and would
            // hide how much headroom there is, so vSync is off while measuring. Ask for
            // 10% headroom: the menu view is a little lighter than a busy run.
            int savedVSync = QualitySettings.vSyncCount;
            int savedTarget = Application.targetFrameRate;
            float target = Mathf.Min(60f, RefreshRate()) * 1.1f;
            int softest = System.Array.IndexOf(Scales, 0.6f);   // below 60% the picture gets soft
            float fps = 0f;

            for (int attempt = 0; attempt < 6; attempt++)
            {
                // (A percentage: the pixel size isn't known until the screen switch finishes.)
                SetStatus("Finding the best resolution: testing " + Mathf.RoundToInt(RenderScale * 100f) + "% on " + (settings != null ? settings.QualityName : "") + "...");
                QualitySettings.vSyncCount = 0;          // (re-set each pass: StepQuality restores the level's vSync)
                Application.targetFrameRate = -1;
                // Let the new size settle (shaders, the screen switch, the reflection probe).
                yield return new WaitForSecondsRealtime(attempt == 0 ? 2.5f : 1.5f);
                yield return Measure(2.5f, result => fps = result);
                Debug.Log("[Starlit Tides] Auto-detect: " + ResolutionLabel() + " (" + Mathf.RoundToInt(RenderScale * 100f) + "%) on "
                          + (settings != null ? settings.QualityName : "?") + ": " + fps.ToString("0.0") + " fps uncapped (want " + target.ToString("0") + ")");
                if (fps >= target) break;   // fast enough: done

                // Frame time grows roughly with the number of pixels (scale squared), so the
                // scale that should reach the target is the current one times sqrt(fps / target).
                // This can also go UP after a quality drop freed some time.
                float wanted = RenderScale * Mathf.Sqrt(fps / target);
                int next = 0;
                while (next < Scales.Length - 1 && Scales[next] > wanted + 0.001f) next++;

                if (next > softest && settings != null && settings.QualityLevel > 0)
                {
                    // Would need a very soft picture: lower the quality level instead, and
                    // test again from 100%.
                    settings.StepQuality(-1);
                    SetScaleIndex(0);
                    continue;
                }
                if (next == ScaleIndex) break;   // already the right step (or nothing lower left)
                SetScaleIndex(next);
            }

            QualitySettings.vSyncCount = savedVSync;
            Application.targetFrameRate = savedTarget;
            PlayerPrefs.SetInt(DetectedKey, DetectVersion);
            PlayerPrefs.Save();
            Detecting = false;
            detectRoutine = null;
            SetStatus("Auto-picked for this device: " + Mathf.RoundToInt(RenderScale * 100f) + "%, "
                      + (settings != null ? settings.QualityName : "") + ", " + Mathf.RoundToInt(Mathf.Min(fps, RefreshRate())) + " fps");
        }

        void SetStatus(string text)
        {
            Status = text;
            if (Changed != null) Changed();
        }

        // Average frame rate over a window of real time (total frames / total time,
        // so a single hitch doesn't dominate the way a min would). The game pauses
        // while it isn't the front app; that gap would read as one very slow frame,
        // so the count starts again after any pause or while unfocused.
        static IEnumerator Measure(float seconds, System.Action<float> done)
        {
            int frames = 0;
            float start = Time.realtimeSinceStartup;
            float last = start;
            while (Time.realtimeSinceStartup - start < seconds)
            {
                yield return null;
                float now = Time.realtimeSinceStartup;
                if (!Application.isFocused || now - last > 0.5f) { frames = 0; start = now; }
                else frames++;
                last = now;
            }
            done(frames / Mathf.Max(0.001f, Time.realtimeSinceStartup - start));
        }
    }
}
