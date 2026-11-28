// ReflectionProbeRefresher: updates the reflections on the sea.
//
// The sea reflects the sky, moon and village lights using a reflection probe.
// Rendering a probe is expensive (6 camera renders), so instead of every frame
// we render it once at the start and again when the weather changes
// (Phase 4 calls Refresh()).

using UnityEngine;
using UnityEngine.Rendering;

namespace StarlitTides
{
    [RequireComponent(typeof(ReflectionProbe))]
    public class ReflectionProbeRefresher : MonoBehaviour
    {
        public static ReflectionProbeRefresher Instance { get; private set; }

        [Tooltip("Also refresh every this many seconds. 0 = only when asked.")]
        public float autoRefreshInterval = 0f;

        ReflectionProbe probe;
        float timer;

        void OnEnable()
        {
            Instance = this;
            probe = GetComponent<ReflectionProbe>();
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        void Start()
        {
            Refresh();
        }

        void Update()
        {
            if (autoRefreshInterval <= 0f) return;

            timer += Time.unscaledDeltaTime;
            if (timer >= autoRefreshInterval)
            {
                timer = 0f;
                Refresh();
            }
        }

        public void Refresh()
        {
            if (probe == null || probe.mode != ReflectionProbeMode.Realtime) return;
            // During play, spread the six cube faces over several frames: rendering
            // them all at once caused a visible hitch (Phase 9 frame-time diagnostics).
            // The menu and loading still render them at once (nothing moves yet).
            GameManager game = GameManager.Instance;
            bool playing = Application.isPlaying && game != null && game.State == GameState.Playing;
            probe.timeSlicingMode = playing ? ReflectionProbeTimeSlicingMode.IndividualFaces : ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
            probe.RenderProbe();
        }
    }
}
