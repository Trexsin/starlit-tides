// DisplayManager: screen mode, resolution and the first-launch auto-detect.
//
// Two resolutions, both the player's choice (Phase 9):
//   DISPLAY RESOLUTION: the size of the whole picture (menus, HUD and 3D). The list
//     is the display's native size (the sharpest) and common widths below it
//     (2560, 1920, 1600...) at the display's own shape, so it's never stretched.
//     The default is native: anything smaller is stretched by the OS and looks soft
//     (players asked for a sharp picture). Smaller sizes run cooler; in a window,
//     the window takes this size.
//   3D RESOLUTION: the 3D world is drawn at 100% .. 50% of the display resolution
//     and upscaled with AMD FSR 1 (the menus stay sharp).
//
// The first launch (and the AUTO-DETECT button) measures the frame rate and picks
// the biggest 3D resolution that holds ~60 fps:
//   1. fullscreen, 100%, the highest quality,
//   2. measure the real frame rate for a few seconds,
//   3. too slow? pick the percentage that should reach the target (frame time is
//      roughly proportional to the number of pixels), measure again, and if even
//      ~70% isn't enough, drop the quality level one step,
//   4. save the result.

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

        /// <summary>Common widths offered under the native size (at the display's own shape).</summary>
        static readonly int[] CommonWidths = { 3840, 3200, 2880, 2560, 2304, 2048, 1920, 1680, 1600, 1440, 1280, 1024 };

        /// <summary>The 3D resolution choices, as a fraction of the display resolution.</summary>
        public static readonly float[] RenderFractions = { 1f, 0.9f, 0.8f, 0.7f, 0.6f, 0.5f };

        const string DisplayKey = "ST_DisplayWidth";            // the chosen display width (0 = native)
        const string RenderKey = "ST_RenderPercentIndex";       // index into RenderFractions
        const string DetectedKey = "ST_DisplayAutoDetected";   // bump the value to re-run auto-detect for everyone
        const int DetectVersion = 9;   // 9: display and 3D resolution became two settings; 8: output capped at 1920 px (cooler, less memory); 7: shaders ~30% cheaper, pick again; 6: rejects readings from a hidden window; 5: steadier auto-detect that starts from High; 4: the real below-the-notch fullscreen size (3: resolutions became a real list)

        /// <summary>The display resolutions in the dropdown, biggest (native) first.</summary>
        public List<Vector2Int> DisplayOptions { get; private set; }
        public int DisplayIndex { get; private set; }
        /// <summary>Index into RenderFractions (0 = 100%).</summary>
        public int RenderIndex { get; private set; }
        public bool Detecting { get; private set; }
        /// <summary>A short line for the Settings screen ("Auto-picked ...", "Testing ...").</summary>
        public string Status { get; private set; }
        /// <summary>The 3D resolution as a fraction of the display resolution (1 = 100%).</summary>
        public float RenderScale { get { return Fraction(RenderIndex); } }
        /// <summary>The chosen display resolution (before the battery saver's cap).</summary>
        public Vector2Int ChosenDisplay { get { return DisplayOptions[Mathf.Clamp(DisplayIndex, 0, DisplayOptions.Count - 1)]; } }
        /// <summary>The size the 3D world is drawn at.</summary>
        public Vector2Int Chosen
        {
            get
            {
                Vector2Int o = OutputSize();
                return new Vector2Int(Mathf.RoundToInt(o.x * RenderScale), Mathf.RoundToInt(o.y * RenderScale));
            }
        }

        /// <summary>Raised whenever the resolution, mode or status text changes.</summary>
        public event System.Action Changed;

        // The URP assets' render scales as saved in the project, restored when
        // play mode ends so the editor never saves a runtime value into them.
        readonly Dictionary<UniversalRenderPipelineAsset, float> originalScales = new Dictionary<UniversalRenderPipelineAsset, float>();
        readonly Dictionary<UniversalRenderPipelineAsset, UpscalingFilterSelection> originalFilters = new Dictionary<UniversalRenderPipelineAsset, UpscalingFilterSelection>();
        int lastWidth, lastHeight;
        Coroutine detectRoutine;

        [Tooltip("Default display width (px) when the player hasn't chosen one. 0 = the display's native size (the sharpest).")]
        public int defaultDisplayWidth = 0;

        void Awake()
        {
            Instance = this;
            BuildOptions();
            // (The Phase 9 single-resolution keys are replaced; auto-detect runs again, DetectVersion 9.)
            PlayerPrefs.DeleteKey("ST_ResolutionWidth");
            PlayerPrefs.DeleteKey("ST_RenderScaleIndex");
            int width = PlayerPrefs.GetInt(DisplayKey, -1);
            DisplayIndex = width < 0 ? DefaultDisplayIndex() : width == 0 ? 0 : NearestIndex(width);
            RenderIndex = Mathf.Clamp(PlayerPrefs.GetInt(RenderKey, 0), 0, RenderFractions.Length - 1);
            Status = "";
        }

        int DefaultDisplayIndex()
        {
            if (defaultDisplayWidth <= 0 || DisplayOptions[0].x <= defaultDisplayWidth) return 0;
            return NearestIndex(defaultDisplayWidth);
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
            WatchFrameRate();
            // The window was resized, or the fullscreen switch finished: refresh the labels.
            if (Screen.width != lastWidth || Screen.height != lastHeight)
            {
                lastWidth = Screen.width;
                lastHeight = Screen.height;
                // (The native size is known for sure only once the screen switch is done.)
                bool native = DisplayIndex == 0;
                Vector2Int keep = ChosenDisplay;
                BuildOptions();
                // Nothing chosen yet: the default, worked out again now the real screen size is known.
                if (!PlayerPrefs.HasKey(DisplayKey)) DisplayIndex = DefaultDisplayIndex();
                else DisplayIndex = native ? 0 : NearestIndex(keep.x);
                ApplyRenderScale();
            }
        }

        // ---------------------------------------------------------------- screen mode

        /// <summary>
        /// Fullscreen always runs at the display's own pixel size. (Unity otherwise keeps
        /// the last saved size and stretches it, which is what made the game blurry.)
        /// </summary>
        void GoFullscreenNative()
        {
            Vector2Int output = OutputSize();
            bool wrongSize = Mathf.Abs(Screen.width - output.x) > output.x * 0.03f;   // compare widths: a notch can trim the height
            if (Screen.fullScreenMode != FullScreenMode.FullScreenWindow || wrongSize)
                Screen.SetResolution(output.x, output.y, FullScreenMode.FullScreenWindow);
        }

        /// <summary>Re-applies the fullscreen output size (the battery saver switching on or off).</summary>
        public void RefreshOutput()
        {
            if (Application.isEditor || Screen.fullScreenMode == FullScreenMode.Windowed) return;
            GoFullscreenNative();
        }

        /// <summary>
        /// The size the game draws its whole picture at: the chosen display resolution.
        /// (Nothing else ever lowers it: the battery saver used to cap it at 1440 px,
        /// which made the picture blurry while Settings still showed the player's choice.)
        /// (Measured on a MacBook Air: the panel's full 2940 x 1846 runs post-processing
        /// and menus at 5.4 megapixels a frame, so 1920 px is much cooler. That trade is
        /// the player's: the default is native, the 3D resolution keeps 60 fps.)
        /// </summary>
        public static Vector2Int OutputSize()
        {
            Vector2Int native = NativeSize();
            if (Instance == null || Instance.DisplayOptions == null) return native;
            return Instance.ChosenDisplay;
        }

        /// <summary>Fullscreen at native size, or a window at the chosen resolution.</summary>
        public void SetFullscreen(bool fullscreen)
        {
            if (Application.isEditor) return;
            Vector2Int output = OutputSize();
            if (fullscreen)
                Screen.SetResolution(output.x, output.y, FullScreenMode.FullScreenWindow);
            else
                ApplyWindowSize();
        }

        /// <summary>
        /// The biggest picture a fullscreen game can really have on this display.
        /// On a MacBook with a camera notch that is NOT the panel size: the panel is
        /// 2940 x 1912, but macOS gives fullscreen apps the area below the notch,
        /// 2940 x 1846. Asking for 1912 made Unity squeeze the taller picture into the
        /// shorter space with black bars down both sides (Phase 9 diagnostics), so we
        /// use the largest size Unity lists (Screen.resolutions), which is the
        /// below-the-notch size on a Mac and the monitor's size on Windows.
        /// (Not Screen.currentResolution: in fullscreen it reports the game's own mode.)
        /// </summary>
        public static Vector2Int NativeSize()
        {
            int w = 0, h = 0;
            foreach (Resolution r in Screen.resolutions)
                if ((long)r.width * r.height > (long)w * h) { w = r.width; h = r.height; }
            if (w <= 0 || h <= 0)
            {
                DisplayInfo info = Screen.mainWindowDisplayInfo;
                w = info.width; h = info.height;
            }
            if (w <= 0 || h <= 0) { w = Display.main.systemWidth; h = Display.main.systemHeight; }
            if (w <= 0 || h <= 0) { w = Screen.currentResolution.width; h = Screen.currentResolution.height; }
            return new Vector2Int(w, h);
        }

        // ---------------------------------------------------------------- resolution

        /// <summary>Native size first, then the common widths below it at the same shape.</summary>
        void BuildOptions()
        {
            Vector2Int native = NativeSize();
            float aspect = native.y / (float)Mathf.Max(1, native.x);
            DisplayOptions = new List<Vector2Int> { native };
            foreach (int w in CommonWidths)
            {
                if (w >= native.x * 0.97f || w < native.x * 0.34f) continue;
                int h = Mathf.RoundToInt(w * aspect / 2f) * 2;
                DisplayOptions.Add(new Vector2Int(w, h));
            }
        }

        int NearestIndex(int width)
        {
            int best = 0;
            for (int i = 1; i < DisplayOptions.Count; i++)
                if (Mathf.Abs(DisplayOptions[i].x - width) < Mathf.Abs(DisplayOptions[best].x - width)) best = i;
            return best;
        }

        static float Fraction(int index)
        {
            return RenderFractions[Mathf.Clamp(index, 0, RenderFractions.Length - 1)];
        }

        /// <summary>Picks the display resolution (0 = native, the sharpest).</summary>
        public void SetDisplayResolution(int index)
        {
            DisplayIndex = Mathf.Clamp(index, 0, DisplayOptions.Count - 1);
            PlayerPrefs.SetInt(DisplayKey, DisplayIndex == 0 ? 0 : DisplayOptions[DisplayIndex].x);
            PlayerPrefs.Save();
            if (!Detecting) Status = "";
            if (!Application.isEditor)
            {
                if (Screen.fullScreenMode == FullScreenMode.Windowed) ApplyWindowSize();
                else GoFullscreenNative();
            }
            ApplyRenderScale();
        }

        /// <summary>Picks the 3D resolution (0 = 100% of the display resolution).</summary>
        public void SetRenderResolution(int index)
        {
            SetRenderIndex(Mathf.Clamp(index, 0, RenderFractions.Length - 1));
            if (!Detecting) Status = "";
            if (Changed != null) Changed();
        }

        void SetRenderIndex(int index)
        {
            RenderIndex = index;
            PlayerPrefs.SetInt(RenderKey, index);
            PlayerPrefs.Save();
            ApplyRenderScale();
        }

        void ApplyWindowSize()
        {
            Vector2Int size = ChosenDisplay;
            Vector2Int native = NativeSize();
            // A window as big as the screen doesn't fit with its title bar: use 85%.
            if (size.x > native.x * 0.9f) size = new Vector2Int(Mathf.RoundToInt(native.x * 0.85f), Mathf.RoundToInt(native.y * 0.85f));
            Screen.SetResolution(size.x, size.y, FullScreenMode.Windowed);
        }

        /// <summary>The label for one display resolution entry.</summary>
        public string DisplayOptionLabel(int index)
        {
            Vector2Int o = DisplayOptions[Mathf.Clamp(index, 0, DisplayOptions.Count - 1)];
            string note = index == 0 ? "  (native)" : index == DefaultDisplayIndex() ? "  (default)" : "";
            return o.x + " x " + o.y + note;
        }

        /// <summary>The label for one 3D resolution entry ("80%").</summary>
        public static string RenderOptionLabel(int index)
        {
            return Mathf.RoundToInt(Fraction(index) * 100f) + "%" + (index == 0 ? "  (full)" : "");
        }

        /// <summary>"1920 x 1248" (the size the 3D is drawn at).</summary>
        public string ResolutionLabel()
        {
            Vector2Int size = Chosen;
            return size.x + " x " + size.y;
        }

        /// <summary>The small line under the resolutions: what they mean, or the auto-detect result.</summary>
        public string DetailLabel()
        {
            if (!string.IsNullOrEmpty(Status)) return Status;
            string battery = PowerManager.OnBattery ? "   \u2022   Battery saver on: 30 fps" : "";
            if (RenderIndex == 0) return "3D drawn at the full " + ResolutionLabel() + battery;
            return "3D drawn at " + ResolutionLabel() + ", upscaled with AMD FSR (menus stay sharp)" + battery;
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
            // Fullscreen: the 3D at the chosen percentage of the display resolution (the
            // screen buffer may be bigger, e.g. a Mac keeps it at the panel size).
            // A window already has the display resolution's size.
            bool windowed = Application.isEditor || Screen.fullScreenMode == FullScreenMode.Windowed;
            float scale = windowed ? RenderScale : Mathf.Clamp(Chosen.x / (float)Mathf.Max(1, Screen.width), 0.25f, 1f);
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

        // Phase 9 rewrite. Measured on a MacBook Air (M4): with vSync off, Metal frame
        // times swing between ~9 ms and ~100 ms (the CPU runs ahead, then waits for a
        // free drawable), so one short measurement is noisy. The old routine once
        // measured a SMALLER size as slower and fell all the way to a blurry 57%.
        // Now: start from the best quality, measure longer, re-measure before
        // trusting a bad result, and prefer a sharp picture (>= ~70% of the width)
        // over High quality.
        IEnumerator AutoDetectRoutine()
        {
            Detecting = true;
            SettingsManager settings = SettingsManager.Instance;
            int startQuality = settings != null ? settings.QualityLevel : 0;
            int startShadows = settings != null ? settings.Shadows : 0;
            int startReflections = settings != null ? settings.Reflections : 0;
            int startScale = RenderIndex;
            invalidReading = false;

            // Start from the best: fullscreen, 100% 3D resolution, the highest quality
            // (at the player's display resolution).
            if (!Application.isEditor)
            {
                if (settings != null && !settings.Fullscreen) settings.SetFullscreen(true);
                else SetFullscreen(true);
            }
            if (settings != null) while (settings.QualityLevel < QualitySettings.names.Length - 1) settings.StepQuality(1);
            SetRenderIndex(0);

            // The goal is a steady 60 fps (or the display's rate if lower), with ~12%
            // headroom: gameplay gets busier than the menu this is measured in.
            int savedVSync = QualitySettings.vSyncCount;
            int savedTarget = Application.targetFrameRate;
            float target = Mathf.Min(60f, RefreshRate()) * 1.12f;
            int softest = 0;   // the smallest size we accept before lowering the quality
            while (softest < RenderFractions.Length - 1 && Fraction(softest + 1) >= 0.68f) softest++;
            float fps = 0f;
            bool done = false;

            for (int guard = 0; guard < 8 && !done; guard++)
            {
                // 1. how fast is this quality at 100%?
                SetRenderIndex(0);
                yield return MeasureSettled(target, result => fps = result);
                if (invalidReading) break;
                if (fps >= target) break;

                // 2. frame time grows with the number of pixels (scale squared), so this
                //    size should reach the target; verify it, and one size lower if needed
                float wanted = Mathf.Sqrt(fps / target);
                int pick = 0;
                while (pick < RenderFractions.Length - 1 && Fraction(pick) > wanted + 0.001f) pick++;
                for (int tries = 0; tries < 2 && pick <= softest; tries++, pick++)
                {
                    SetRenderIndex(pick);
                    yield return MeasureSettled(target, result => fps = result);
                    if (invalidReading) break;
                    if (fps >= target * 0.97f) { done = true; break; }
                }
                if (done || invalidReading) break;

                // 3. still too slow at a sharp size: one quality level down, and again
                if (settings != null && settings.QualityLevel > 0) { settings.StepQuality(-1); continue; }

                // Lowest quality already: accept a softer picture.
                for (int i = softest + 1; i < RenderFractions.Length; i++)
                {
                    SetRenderIndex(i);
                    yield return MeasureSettled(target, result => fps = result);
                    if (invalidReading || fps >= target * 0.97f) break;
                }
                break;
            }

            QualitySettings.vSyncCount = savedVSync;
            Application.targetFrameRate = savedTarget;
            if (invalidReading)
            {
                // The window wasn't on screen (hidden behind another app, another Space,
                // or the display asleep): macOS then skips drawing and the frame rate
                // reads in the thousands. Keep the old settings and try again next launch.
                if (settings != null)
                {
                    while (settings.QualityLevel != startQuality) settings.StepQuality(settings.QualityLevel < startQuality ? 1 : -1);
                    settings.SetShadows(startShadows);
                    settings.SetReflections(startReflections);
                }
                SetRenderIndex(startScale);
                Detecting = false;
                detectRoutine = null;
                Debug.Log("[Starlit Tides] Auto-detect: the window wasn't visible, nothing measured. It will try again next launch.");
                PowerManager.Reapply();
            SetStatus("Couldn't measure (the game window wasn't visible). Press Auto-detect to try again.");
                yield break;
            }
            PlayerPrefs.SetInt(DetectedKey, DetectVersion);
            PlayerPrefs.Save();
            Detecting = false;
            detectRoutine = null;
            PowerManager.Reapply();
            SetStatus("Auto-picked for this device: " + RenderOptionLabel(RenderIndex).Split(' ')[0] + " 3D, "
                      + (settings != null ? settings.QualityName : "") + ", " + Mathf.RoundToInt(Mathf.Min(fps, RefreshRate())) + " fps");
        }

        // Lets a new size or quality settle, measures for 4 s with vSync off, and if that
        // reads below the target measures once more and keeps the better reading.
        bool invalidReading;

        IEnumerator MeasureSettled(float target, System.Action<float> done)
        {
            SettingsManager settings = SettingsManager.Instance;
            SetStatus("Finding the best settings: testing " + ResolutionLabel() + " on " + (settings != null ? settings.QualityName : "") + "...");
            float best = 0f;
            for (int pass = 0; pass < 2; pass++)
            {
                QualitySettings.vSyncCount = 0;          // (re-set each pass: StepQuality restores the level's vSync)
                Application.targetFrameRate = -1;
                yield return new WaitForSecondsRealtime(pass == 0 ? 2f : 0.5f);
                float fps = 0f;
                yield return Measure(4f, result => fps = result);
                // No game draws this scene at 4x the display rate: a reading like that
                // means nothing was drawn. Wait and re-measure a few times before giving up.
                float impossible = Mathf.Max(300f, RefreshRate() * 4f);
                for (int retry = 0; fps > impossible && retry < 4; retry++)
                {
                    Debug.Log("[Starlit Tides] Auto-detect: " + fps.ToString("0") + " fps is impossible (window not visible?), waiting...");
                    yield return new WaitForSecondsRealtime(3f);
                    QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
                    yield return Measure(4f, result => fps = result);
                }
                if (fps > impossible) { invalidReading = true; done(0f); yield break; }
                best = Mathf.Max(best, fps);
                Debug.Log("[Starlit Tides] Auto-detect: " + ResolutionLabel() + " (" + Mathf.RoundToInt(RenderScale * 100f) + "%) on "
                          + (settings != null ? settings.QualityName : "?") + ": " + fps.ToString("0.0") + " fps uncapped (want " + target.ToString("0") + ")");
                if (best >= target) break;
            }
            done(best);
        }

        // ---------------------------------------------------------------- safety net

        // During play: if the game keeps missing frames (more than 4% of frames take
        // over 1.6x the display's frame time, for 5 s in a row), step the resolution
        // down one size and say so in Settings. Never below ~50%, at most every 12 s.
        float missWindowStart, lastDrop = -100f;
        int missFrames, windowFrames;

        void WatchFrameRate()
        {
            GameManager game = GameManager.Instance;
            bool allowed = SettingsManager.Instance == null || SettingsManager.Instance.AdaptiveResolution;
            if (!allowed || Detecting || game == null || game.State != GameState.Playing || Time.timeScale < 0.9f) { missFrames = windowFrames = 0; missWindowStart = Time.unscaledTime; return; }
            float budget = 1f / Mathf.Max(30f, Mathf.Min(60f, RefreshRate()));
            // Real time between frames (FramePacer may lock the game clock to the refresh rate).
            float dt = FramePacer.Instance != null ? FramePacer.RealDeltaTime : Time.unscaledDeltaTime;
            if (dt > 0.5f) return;   // a pause or alt-tab, not a slow frame
            windowFrames++;
            if (dt > budget * 1.6f) missFrames++;
            if (Time.unscaledTime - missWindowStart < 5f) return;

            bool struggling = windowFrames > 0 && missFrames > windowFrames * 0.04f;
            missFrames = windowFrames = 0;
            missWindowStart = Time.unscaledTime;
            if (!struggling || Time.unscaledTime - lastDrop < 12f || RenderIndex >= RenderFractions.Length - 1 || Fraction(RenderIndex + 1) < 0.5f) return;
            lastDrop = Time.unscaledTime;
            SetRenderIndex(RenderIndex + 1);
            Debug.Log("[Starlit Tides] Frame rate dropping: 3D resolution lowered to " + RenderOptionLabel(RenderIndex));
            SetStatus("3D resolution lowered to " + RenderOptionLabel(RenderIndex).Split(' ')[0] + " to keep the game smooth (switch off in Settings)");
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
