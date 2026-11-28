// DisplayManager: screen mode, resolution and the first-launch auto-detect.
//
// Resolution (Phase 9: a real list you pick from a dropdown):
//   The Settings dropdown lists real sizes: the display's native size first, then
//   common widths (2560, 1920, 1600, 1280...) at the display's own shape, so the
//   picture is never stretched.
//   - Fullscreen: the screen keeps running at its native size, so text and the HUD
//     stay razor sharp, and the 3D world is rendered at the chosen size (URP render
//     scale = chosen width / screen width), upscaled with AMD FSR 1.
//   - Windowed: the window itself takes the chosen size.
//
// The first launch (and the AUTO-DETECT button) measures the frame rate and picks
// the biggest resolution that holds ~60 fps:
//   1. fullscreen at the display's native size,
//   2. measure the real frame rate for a few seconds,
//   3. too slow? pick the size that should reach the target (frame time is roughly
//      proportional to the number of pixels), measure again, and if even ~60% of the
//      native width isn't enough, drop the quality level one step,
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

        const string WidthKey = "ST_ResolutionWidth";          // the chosen width (0 = native)
        const string OldScaleKey = "ST_RenderScaleIndex";       // Phase 8 step 9 (percent steps)
        const string DetectedKey = "ST_DisplayAutoDetected";   // bump the value to re-run auto-detect for everyone
        const int DetectVersion = 7;   // 7: shaders ~30% cheaper, pick again; 6: rejects readings from a hidden window; 5: steadier auto-detect that starts from High; 4: the real below-the-notch fullscreen size (3: resolutions became a real list)

        /// <summary>The sizes in the dropdown, biggest (native) first.</summary>
        public List<Vector2Int> Options { get; private set; }
        public int ScaleIndex { get; private set; }
        public bool Detecting { get; private set; }
        /// <summary>A short line for the Settings screen ("Auto-picked ...", "Testing ...").</summary>
        public string Status { get; private set; }
        /// <summary>The chosen size as a fraction of the native width (1 = native).</summary>
        public float RenderScale { get { return Fraction(ScaleIndex); } }
        public Vector2Int Chosen { get { return Options[Mathf.Clamp(ScaleIndex, 0, Options.Count - 1)]; } }

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
            BuildOptions();
            int width = PlayerPrefs.GetInt(WidthKey, 0);
            if (!PlayerPrefs.HasKey(WidthKey) && PlayerPrefs.HasKey(OldScaleKey))
            {
                // Carry over the Phase 8 percentage setting.
                float[] oldScales = { 1f, 0.9f, 0.8f, 0.7f, 0.6f, 0.5f };
                width = Mathf.RoundToInt(NativeSize().x * oldScales[Mathf.Clamp(PlayerPrefs.GetInt(OldScaleKey, 0), 0, 5)]);
            }
            ScaleIndex = width <= 0 ? 0 : NearestIndex(width);
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
            WatchFrameRate();
            // The window was resized, or the fullscreen switch finished: refresh the labels.
            if (Screen.width != lastWidth || Screen.height != lastHeight)
            {
                lastWidth = Screen.width;
                lastHeight = Screen.height;
                // (The native size is known for sure only once the screen switch is done.)
                Vector2Int keep = Chosen;
                BuildOptions();
                ScaleIndex = NearestIndex(keep.x);
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
            Vector2Int native = NativeSize();
            bool wrongSize = Screen.width < native.x * 0.97f;   // compare widths: a notch can trim the height
            if (Screen.fullScreenMode != FullScreenMode.FullScreenWindow || wrongSize)
                Screen.SetResolution(native.x, native.y, FullScreenMode.FullScreenWindow);
        }

        /// <summary>Fullscreen at native size, or a window at the chosen resolution.</summary>
        public void SetFullscreen(bool fullscreen)
        {
            if (Application.isEditor) return;
            Vector2Int native = NativeSize();
            if (fullscreen)
                Screen.SetResolution(native.x, native.y, FullScreenMode.FullScreenWindow);
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
            Options = new List<Vector2Int> { native };
            foreach (int w in CommonWidths)
            {
                if (w >= native.x * 0.97f || w < native.x * 0.34f) continue;
                int h = Mathf.RoundToInt(w * aspect / 2f) * 2;
                Options.Add(new Vector2Int(w, h));
            }
        }

        int NearestIndex(int width)
        {
            int best = 0;
            for (int i = 1; i < Options.Count; i++)
                if (Mathf.Abs(Options[i].x - width) < Mathf.Abs(Options[best].x - width)) best = i;
            return best;
        }

        float Fraction(int index)
        {
            Vector2Int native = Options[0];
            return Mathf.Clamp(Options[Mathf.Clamp(index, 0, Options.Count - 1)].x / (float)Mathf.Max(1, native.x), 0.25f, 1f);
        }

        /// <summary>Picks a resolution from the dropdown (0 = native).</summary>
        public void SetResolution(int index)
        {
            SetScaleIndex(Mathf.Clamp(index, 0, Options.Count - 1));
            if (!Detecting) Status = "";
            // Windowed: the window itself takes the chosen size.
            if (!Application.isEditor && Screen.fullScreenMode == FullScreenMode.Windowed) ApplyWindowSize();
            if (Changed != null) Changed();
        }

        /// <summary>Moves one resolution step up (+1 = bigger) or down (-1).</summary>
        public void StepResolution(int direction)
        {
            SetResolution(Mathf.Clamp(ScaleIndex - direction, 0, Options.Count - 1));
        }

        void SetScaleIndex(int index)
        {
            ScaleIndex = index;
            PlayerPrefs.SetInt(WidthKey, index == 0 ? 0 : Options[index].x);
            PlayerPrefs.Save();
            ApplyRenderScale();
        }

        void ApplyWindowSize()
        {
            Vector2Int size = Chosen;
            Vector2Int native = NativeSize();
            // A window as big as the screen doesn't fit with its title bar: use 85%.
            if (size.x > native.x * 0.9f) size = new Vector2Int(Mathf.RoundToInt(native.x * 0.85f), Mathf.RoundToInt(native.y * 0.85f));
            Screen.SetResolution(size.x, size.y, FullScreenMode.Windowed);
        }

        /// <summary>The label for one dropdown entry.</summary>
        public string OptionLabel(int index)
        {
            Vector2Int o = Options[Mathf.Clamp(index, 0, Options.Count - 1)];
            return o.x + " x " + o.y + (index == 0 ? "  (native)" : "");
        }

        /// <summary>"1920 x 1248" for the Settings screen.</summary>
        public string ResolutionLabel()
        {
            Vector2Int size = Chosen;
            return size.x + " x " + size.y;
        }

        /// <summary>The small line under the resolution: what it means, or the auto-detect result.</summary>
        public string DetailLabel()
        {
            if (!string.IsNullOrEmpty(Status)) return Status;
            bool windowed = Screen.fullScreenMode == FullScreenMode.Windowed;
            if (windowed) return "Windowed: the window is " + Screen.width + " x " + Screen.height;
            return ScaleIndex == 0
                ? "Native: every pixel of your " + Options[0].x + " x " + Options[0].y + " screen"
                : "3D rendered at " + ResolutionLabel() + ", upscaled with AMD FSR (menus stay sharp)";
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
            // Fullscreen renders the 3D at the chosen width; a window already has that size.
            bool windowed = !Application.isEditor && Screen.fullScreenMode == FullScreenMode.Windowed;
            float scale = windowed ? 1f : Mathf.Clamp(Chosen.x / (float)Mathf.Max(1, Screen.width), 0.25f, 1f);
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
            int startScale = ScaleIndex;
            invalidReading = false;

            // Start from the best: fullscreen, native, the highest quality.
            if (!Application.isEditor)
            {
                if (settings != null && !settings.Fullscreen) settings.SetFullscreen(true);
                else SetFullscreen(true);
            }
            if (settings != null) while (settings.QualityLevel < QualitySettings.names.Length - 1) settings.StepQuality(1);
            SetScaleIndex(0);

            // The goal is a steady 60 fps (or the display's rate if lower), with ~12%
            // headroom: gameplay gets busier than the menu this is measured in.
            int savedVSync = QualitySettings.vSyncCount;
            int savedTarget = Application.targetFrameRate;
            float target = Mathf.Min(60f, RefreshRate()) * 1.12f;
            int softest = 0;   // the smallest size we accept before lowering the quality
            while (softest < Options.Count - 1 && Fraction(softest + 1) >= 0.68f) softest++;
            float fps = 0f;
            bool done = false;

            for (int guard = 0; guard < 8 && !done; guard++)
            {
                // 1. how fast is this quality at the native size?
                SetScaleIndex(0);
                yield return MeasureSettled(target, result => fps = result);
                if (invalidReading) break;
                if (fps >= target) break;

                // 2. frame time grows with the number of pixels (scale squared), so this
                //    size should reach the target; verify it, and one size lower if needed
                float wanted = Mathf.Sqrt(fps / target);
                int pick = 0;
                while (pick < Options.Count - 1 && Fraction(pick) > wanted + 0.001f) pick++;
                for (int tries = 0; tries < 2 && pick <= softest; tries++, pick++)
                {
                    SetScaleIndex(pick);
                    yield return MeasureSettled(target, result => fps = result);
                    if (invalidReading) break;
                    if (fps >= target * 0.97f) { done = true; break; }
                }
                if (done || invalidReading) break;

                // 3. still too slow at a sharp size: one quality level down, and again
                if (settings != null && settings.QualityLevel > 0) { settings.StepQuality(-1); continue; }

                // Lowest quality already: accept a softer picture.
                for (int i = softest + 1; i < Options.Count; i++)
                {
                    SetScaleIndex(i);
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
                if (settings != null) while (settings.QualityLevel != startQuality) settings.StepQuality(settings.QualityLevel < startQuality ? 1 : -1);
                SetScaleIndex(startScale);
                Detecting = false;
                detectRoutine = null;
                Debug.Log("[Starlit Tides] Auto-detect: the window wasn't visible, nothing measured. It will try again next launch.");
                SetStatus("Couldn't measure (the game window wasn't visible). Press Auto-detect to try again.");
                yield break;
            }
            PlayerPrefs.SetInt(DetectedKey, DetectVersion);
            PlayerPrefs.Save();
            Detecting = false;
            detectRoutine = null;
            SetStatus("Auto-picked for this device: " + ResolutionLabel() + ", "
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
            if (Detecting || game == null || game.State != GameState.Playing || Time.timeScale < 0.9f) { missFrames = windowFrames = 0; missWindowStart = Time.unscaledTime; return; }
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
            if (!struggling || Time.unscaledTime - lastDrop < 12f || ScaleIndex >= Options.Count - 1 || Fraction(ScaleIndex + 1) < 0.5f) return;
            lastDrop = Time.unscaledTime;
            SetScaleIndex(ScaleIndex + 1);
            Debug.Log("[Starlit Tides] Frame rate dropping: resolution lowered to " + ResolutionLabel());
            SetStatus("Lowered to " + ResolutionLabel() + " automatically to keep the game smooth");
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
