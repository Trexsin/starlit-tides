// FramePacer: even motion at the display's refresh rate (Phase 9).
//
// With vSync on, every frame reaches the screen exactly 1/60 s after the last
// one, but the time Unity MEASURES between frames wobbles (15.5 ms, 17.9 ms,
// 16.2 ms... measured on a MacBook Air). Everything in the game moves by that
// measured time, so on screen fast things (rocks, orbs, creatures rushing past
// at up to 18 m/s) jump a little too far one frame and too short the next:
// jerky motion even though the frame rate reads a steady 60.
//
// So while frames are arriving steadily, the game clock advances by exactly one
// refresh interval per frame (Time.captureDeltaTime). If a frame really takes
// longer (a hitch, or the machine can't keep up), it switches back to real time
// at once so the game never runs slow, and locks again after a calm half second.
//
// Real elapsed time for measurements: FramePacer.RealDeltaTime.

using UnityEngine;

namespace StarlitTides
{
    [DefaultExecutionOrder(-1000)]
    public class FramePacer : MonoBehaviour
    {
        public static FramePacer Instance { get; private set; }

        /// <summary>Real seconds since the previous frame (not affected by the pacing).</summary>
        public static float RealDeltaTime { get; private set; } = 1f / 60f;
        /// <summary>True while the game clock is locked to the refresh rate.</summary>
        public static bool Locked { get; private set; }

        [Tooltip("A frame up to this many refresh intervals long still counts as on time.")]
        public float tolerance = 1.35f;
        [Tooltip("Seconds of steady frames before locking (again).")]
        public float relockDelay = 0.5f;

        float lastRealtime = -1f;
        float steadyFor;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            if (Instance != null || Application.isEditor) return;   // the editor's frame timing isn't vSync'd
            var go = new GameObject("Frame Pacer");
            DontDestroyOnLoad(go);
            go.AddComponent<FramePacer>();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) { Instance = null; Time.captureDeltaTime = 0f; Locked = false; }
        }

        void Update()
        {
            float now = Time.realtimeSinceStartup;
            if (lastRealtime < 0f) { lastRealtime = now; return; }
            float real = now - lastRealtime;
            lastRealtime = now;
            RealDeltaTime = real;

            float refresh = (float)Screen.currentResolution.refreshRateRatio.value;
            if (refresh < 30f || refresh > 240f) refresh = 60f;
            float interval = 1f / refresh;
            // Only when the game can actually show every refresh: vSync on, no frame cap below it.
            bool vsynced = QualitySettings.vSyncCount == 1 && (Application.targetFrameRate <= 0 || Application.targetFrameRate >= refresh - 1f);

            bool onTime = real < interval * tolerance && real > interval * 0.5f;
            if (!vsynced || !onTime || !Application.isFocused)
            {
                steadyFor = 0f;
                if (Locked) { Locked = false; Time.captureDeltaTime = 0f; }   // real time, right away
                return;
            }
            steadyFor += real;
            if (!Locked && steadyFor >= relockDelay)
            {
                Locked = true;
                Time.captureDeltaTime = interval;
            }
        }
    }
}
