// StarlitDiagnostics: a self-running test for the real player (Phase 9).
//
// Off unless the game is launched with   -starlitDiag
//   open "StarlitTides.app" --args -starlitDiag
// It then writes to Player.log:
//   1. what screen size the game really gets in each fullscreen mode (the size
//      asked for, the safe area, the display, the window), to track down black
//      borders around the picture
//   2. the frame rate in every zone of the voyage (average, median and the
//      slowest 1% of frames), at the current quality and resolution
// and quits. It never writes the player's settings.

using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Playables;

namespace StarlitTides
{
    public class StarlitDiagnostics : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-starlitDiag") < 0) return;
            Application.runInBackground = true;   // finish even if another window is put in front
            var go = new GameObject("Starlit Diagnostics");
            DontDestroyOnLoad(go);
            go.AddComponent<StarlitDiagnostics>();
        }

        static void Log(string s) { Debug.Log("[Starlit Diag] " + s); }

        static string DisplayState(string label)
        {
            var b = new StringBuilder(label + ": ");
            b.Append("screen " + Screen.width + "x" + Screen.height + " mode " + Screen.fullScreenMode);
            Rect safe = Screen.safeArea;
            b.Append(" | safeArea " + safe.x + "," + safe.y + " " + safe.width + "x" + safe.height);
            b.Append(" | display system " + Display.main.systemWidth + "x" + Display.main.systemHeight + " rendering " + Display.main.renderingWidth + "x" + Display.main.renderingHeight);
            b.Append(" | currentRes " + Screen.currentResolution.width + "x" + Screen.currentResolution.height);
            DisplayInfo info = Screen.mainWindowDisplayInfo;
            b.Append(" | displayInfo " + info.width + "x" + info.height + " workArea " + info.workArea.width + "x" + info.workArea.height + "@" + info.workArea.x + "," + info.workArea.y);
            b.Append(" | windowPos " + Screen.mainWindowPosition);
            Resolution[] all = Screen.resolutions;
            if (all.Length > 0) b.Append(" | resolutions " + all.Length + ", largest " + all[all.Length - 1].width + "x" + all[all.Length - 1].height);
            return b.ToString();
        }

        IEnumerator Start()
        {
            // Let the first-launch auto-detect finish first (it measures the frame rate too).
            yield return new WaitForSecondsRealtime(3f);
            while (DisplayManager.Instance != null && DisplayManager.Instance.Detecting) yield return new WaitForSecondsRealtime(0.5f);
            Log(DisplayState("start"));
            Log("focused " + Application.isFocused + ", quality " + QualitySettings.names[QualitySettings.GetQualityLevel()] + ", resolution " + (DisplayManager.Instance != null ? DisplayManager.Instance.ResolutionLabel() : "?") + ", vSync " + QualitySettings.vSyncCount + ", targetFps " + Application.targetFrameRate);

            bool costOnly = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-starlitCost") >= 0;
            bool zonesOnly = System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-starlitZones") >= 0;
            if (!costOnly && !zonesOnly) {
            // ---- 1. fullscreen modes ----
            Vector2Int native = DisplayManager.NativeSize();
            Screen.SetResolution(native.x, native.y, FullScreenMode.MaximizedWindow);
            yield return new WaitForSecondsRealtime(3f);
            Log(DisplayState("MaximizedWindow " + native.x + "x" + native.y));
            Screen.SetResolution(native.x, native.y, FullScreenMode.FullScreenWindow);
            yield return new WaitForSecondsRealtime(3f);
            Log(DisplayState("FullScreenWindow " + native.x + "x" + native.y));
            Rect safe = Screen.safeArea;
            if (safe.height > 0 && (int)safe.height != native.y)
            {
                Screen.SetResolution((int)safe.width, (int)safe.height, FullScreenMode.FullScreenWindow);
                yield return new WaitForSecondsRealtime(3f);
                Log(DisplayState("FullScreenWindow safeArea " + (int)safe.width + "x" + (int)safe.height));
            }
            if (DisplayManager.Instance != null) DisplayManager.Instance.SetFullscreen(true);
            yield return new WaitForSecondsRealtime(3f);
            Log(DisplayState("game's own fullscreen"));
            }

            // ---- 2. frame rate through the voyage ----
            UIManager ui = FindAnyObjectByType<UIManager>();
            if (ui != null) ui.Play();
            yield return new WaitForSecondsRealtime(2f);
            foreach (PlayableDirector d in FindObjectsByType<PlayableDirector>()) if (d.state == PlayState.Playing) d.Stop();
            float wait = 0f;
            while (GameManager.Instance != null && !GameManager.Instance.IsPlaying && wait < 10f) { wait += 0.5f; yield return new WaitForSecondsRealtime(0.5f); }
            if (GameManager.Instance != null && !GameManager.Instance.IsPlaying) GameManager.Instance.StartRun();

            VoyageDirector voyage = VoyageDirector.Instance;
            int zones = voyage != null ? voyage.zones.Length : 1;
            for (int z = costOnly ? zones - 1 : 0; z < zones; z++)
            {
                if (zonesOnly && voyage != null && z > 0)
                {
                    // A REAL transition: skip to 8 s before the border and sail across it.
                    typeof(VoyageDirector).GetProperty("ZoneTime").SetValue(voyage, voyage.Current.duration - 8f);
                    StartCoroutine(TraceTransition(16f, "zone " + (z + 1)));
                    yield return Measure(16f, "zone " + (z + 1) + " TRANSITION");
                }
                else if (voyage != null && z > 0) voyage.DebugJumpToZone(z);
                yield return new WaitForSecondsRealtime(zonesOnly ? 0f : 3f);   // let the zone's look and first encounter arrive
                yield return Measure(zonesOnly ? 8f : 4f, "zone " + (z + 1) + " " + (voyage != null ? voyage.zones[z].name : ""));
            }

            if (zonesOnly) { Log("done"); yield return new WaitForSecondsRealtime(0.5f); Application.Quit(); yield break; }
            // ---- 3. what costs the most: uncapped (vSync off), High, native, Aurora Deep ----
            // Each feature is measured against a fresh baseline right before it (the
            // GPU clock wanders), and twice, to see through the noise.
            int savedVSync = QualitySettings.vSyncCount, savedTarget = Application.targetFrameRate;
            QualitySettings.SetQualityLevel(QualitySettings.names.Length - 1, true);
            var asset = QualitySettings.renderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
            if (asset != null) asset.renderScale = 1f;
            var tests = new List<KeyValuePair<string, System.Action<bool>>>();
            OceanController ocean = OceanController.Instance;
            Renderer oceanRenderer = null;
            if (ocean != null && ocean.oceanMaterial != null)
                foreach (Renderer r in FindObjectsByType<Renderer>()) if (r.sharedMaterial == ocean.oceanMaterial) { oceanRenderer = r; break; }
            if (oceanRenderer != null) tests.Add(new KeyValuePair<string, System.Action<bool>>("ocean", on => oceanRenderer.enabled = on));
            if (ocean != null && ocean.oceanMaterial != null)
                tests.Add(new KeyValuePair<string, System.Action<bool>>("ocean high detail (LOW keyword on)", on => { if (on) Shader.DisableKeyword("STARLIT_QUALITY_LOW"); else Shader.EnableKeyword("STARLIT_QUALITY_LOW"); }));
            if (asset != null)
                foreach (var f in Features(asset)) { var ff = f; tests.Add(new KeyValuePair<string, System.Action<bool>>(f.name, on => ff.SetActive(on))); }
            var camData = Camera.main != null ? Camera.main.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>() : null;
            if (camData != null) tests.Add(new KeyValuePair<string, System.Action<bool>>("post-processing", on => camData.renderPostProcessing = on));
            tests.Add(new KeyValuePair<string, System.Action<bool>>("sea scenery", on => { foreach (DriftingObject d in DriftingObject.Active) if (d is SeaDecor) foreach (Renderer r in d.GetComponentsInChildren<Renderer>()) r.enabled = on; }));
            tests.Add(new KeyValuePair<string, System.Action<bool>>("lane objects (orbs, rocks, creatures)", on => { foreach (DriftingObject d in DriftingObject.Active) if (!(d is SeaDecor)) foreach (Renderer r in d.GetComponentsInChildren<Renderer>()) r.enabled = on; }));
            tests.Add(new KeyValuePair<string, System.Action<bool>>("particles", on => { foreach (ParticleSystemRenderer r in FindObjectsByType<ParticleSystemRenderer>()) r.enabled = on; }));
            tests.Add(new KeyValuePair<string, System.Action<bool>>("sky", on => { if (Camera.main != null) Camera.main.clearFlags = on ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor; }));
            GameManager gm = GameManager.Instance;
            for (int round = 0; round < (costOnly ? 1 : 2); round++)
                foreach (var t in tests)
                {
                    QualitySettings.vSyncCount = 0; Application.targetFrameRate = -1;
                    if (gm != null) gm.Pause();   // freeze the scene so both readings see the same thing
                    yield return new WaitForSecondsRealtime(0.6f);
                    float baseFps = 0f, offFps = 0f;
                    yield return MeasureAvg(3f, v => baseFps = v);
                    t.Value(false);
                    yield return new WaitForSecondsRealtime(0.6f);
                    yield return MeasureAvg(3f, v => offFps = v);
                    t.Value(true);
                    float ms = 1000f / Mathf.Max(1f, baseFps) - 1000f / Mathf.Max(1f, offFps);
                    Log("COST " + t.Key + ": " + ms.ToString("0.0") + " ms (with " + baseFps.ToString("0.0") + " fps, without " + offFps.ToString("0.0") + " fps)");
                }
            if (gm != null) gm.Resume();
            QualitySettings.vSyncCount = savedVSync; Application.targetFrameRate = savedTarget;
            if (DisplayManager.Instance != null) DisplayManager.Instance.ApplyRenderScale();

            Log("done");
            yield return new WaitForSecondsRealtime(0.5f);
            Application.Quit();
        }

        // Every half second: how the zone change is going (all values should glide, not jump).
        IEnumerator TraceTransition(float seconds, string label)
        {
            float end = Time.realtimeSinceStartup + seconds;
            var line = new StringBuilder(label + " glide:");
            while (Time.realtimeSinceStartup < end)
            {
                VoyageDirector v = VoyageDirector.Instance;
                SkyController sky = SkyController.Instance;
                float tide = OceanController.Instance != null ? OceanController.Instance.tideSpeed : 0f;
                line.Append(" [" + (v != null ? v.Current.name.Split(' ')[0] : "?") + " tide " + tide.ToString("0.0")
                    + " fov+" + (SpeedFx.Instance != null ? SpeedFx.Instance.FovBoost : 0f).ToString("0.0")
                    + " sunset " + (sky != null ? sky.sunset : 0f).ToString("0.00")
                    + " storm " + (sky != null ? sky.storm : 0f).ToString("0.00")
                    + " aurora " + (sky != null ? sky.aurora : 0f).ToString("0.00") + "]");
                yield return new WaitForSecondsRealtime(1f);
            }
            Log(line.ToString());
        }

        static IEnumerator MeasureAvg(float seconds, System.Action<float> done)
        {
            int frames = 0;
            float start = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - start < seconds) { yield return null; frames++; }
            done(frames / Mathf.Max(0.001f, Time.realtimeSinceStartup - start));
        }

        IEnumerator Measure(float seconds, string label)
        {
            var frames = new List<float>();
            var gameSteps = new List<float>();
            double cpuSum = 0, gpuSum = 0, mainSum = 0; int timed = 0;
            var spikes = new StringBuilder();
            int spikeCount = 0;
            float start = Time.realtimeSinceStartup;
            float end = start + seconds;
            var timings = new FrameTiming[1];
            while (Time.realtimeSinceStartup < end)
            {
                KeepAlive();
                float before = Time.realtimeSinceStartup;
                yield return null;
                float dt = Time.realtimeSinceStartup - before;   // real time (FramePacer may lock the game clock)
                frames.Add(dt);
                gameSteps.Add(Time.deltaTime);
                FrameTimingManager.CaptureFrameTimings();
                if (FrameTimingManager.GetLatestTimings(1, timings) > 0 && timings[0].gpuFrameTime > 0)
                {
                    cpuSum += timings[0].cpuFrameTime; gpuSum += timings[0].gpuFrameTime; mainSum += timings[0].cpuMainThreadFrameTime; timed++;
                }
                if (dt > 0.024f)
                {
                    spikeCount++;
                    if (spikeCount <= 12)
                        spikes.Append(" [" + (Time.realtimeSinceStartup - start).ToString("0.0") + "s " + (dt * 1000f).ToString("0") + "ms gpu "
                            + timings[0].gpuFrameTime.ToString("0.0") + " main " + timings[0].cpuMainThreadFrameTime.ToString("0.0")
                            + " " + (EncounterDirector.Instance != null && EncounterDirector.Instance.CurrentEncounter.HasValue ? EncounterDirector.Instance.CurrentEncounter.Value.ToString() : "-") + "]");
                }
            }
            if (timed > 0)
                Log(label + " timing: cpu frame " + (cpuSum / timed).ToString("0.0") + " ms, main thread " + (mainSum / timed).ToString("0.0") + " ms, gpu " + (gpuSum / timed).ToString("0.0")
                    + " ms; " + spikeCount + " frames over 24 ms" + spikes);
            else Log(label + ": " + spikeCount + " frames over 24 ms (no GPU timing available)" + spikes);
            // How even the game's own time steps are (what decides smooth motion on screen).
            float stepMean = 0f; foreach (float g in gameSteps) stepMean += g; stepMean /= Mathf.Max(1, gameSteps.Count);
            float stepVar = 0f; foreach (float g in gameSteps) stepVar += (g - stepMean) * (g - stepMean);
            float stepJitter = Mathf.Sqrt(stepVar / Mathf.Max(1, gameSteps.Count)) * 1000f;
            float realMean = 0f; foreach (float f in frames) realMean += f; realMean /= Mathf.Max(1, frames.Count);
            float realVar = 0f; foreach (float f in frames) realVar += (f - realMean) * (f - realMean);
            Log(label + " pacing: game step jitter " + stepJitter.ToString("0.00") + " ms, real frame jitter " + (Mathf.Sqrt(realVar / Mathf.Max(1, frames.Count)) * 1000f).ToString("0.00")
                + " ms, clock locked " + FramePacer.Locked);
            frames.Sort();
            float sum = 0f; foreach (float f in frames) sum += f;
            float avg = frames.Count / Mathf.Max(0.001f, sum);
            float median = 1f / frames[frames.Count / 2];
            float low1 = 1f / frames[Mathf.Min(frames.Count - 1, (int)(frames.Count * 0.99f))];
            int creatures = 0, total = 0;
            foreach (DriftingObject d in DriftingObject.Active) { total++; if (d is SeaCreature || d is Jellyfish) creatures++; }
            Log(label + ": avg " + avg.ToString("0.0") + " fps, median " + median.ToString("0.0") + ", 1% low " + low1.ToString("0.0")
                + " (" + frames.Count + " frames, " + total + " drifting objects, " + creatures + " creatures)");
        }

        static List<UnityEngine.Rendering.Universal.ScriptableRendererFeature> Features(UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset asset)
        {
            var list = new List<UnityEngine.Rendering.Universal.ScriptableRendererFeature>();
            var field = typeof(UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset).GetField("m_RendererDataList", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            var datas = field != null ? field.GetValue(asset) as UnityEngine.Rendering.Universal.ScriptableRendererData[] : null;
            if (datas != null) foreach (var d in datas) if (d != null) foreach (var f in d.rendererFeatures) if (f != null && !list.Contains(f)) list.Add(f);
            return list;
        }

        // Keep the light up so the run lasts through every zone.
        static void KeepAlive()
        {
            BoatController boat = BoatController.Instance;
            if (boat != null && boat.lightMeter != null && boat.lightMeter.Value < 0.5f) boat.lightMeter.Add(0.5f);
        }
    }
}
