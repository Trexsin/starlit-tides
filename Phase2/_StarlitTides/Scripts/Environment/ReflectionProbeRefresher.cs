// =============================================================================
//  ReflectionProbeRefresher - re-renders the sea's reflection probe
// -----------------------------------------------------------------------------
//  The ocean reflects the sky, the moon and the glowing village through a
//  realtime reflection probe set to "Via Scripting". We render it once at
//  start and again whenever the weather changes (Phase 4 calls Refresh()),
//  so the reflections always match the current sky without re-rendering six
//  cube faces every frame.
// =============================================================================
using UnityEngine;

namespace StarlitTides
{
    [RequireComponent(typeof(ReflectionProbe))]
    public class ReflectionProbeRefresher : MonoBehaviour
    {
        public static ReflectionProbeRefresher Instance { get; private set; }

        [Tooltip("Also refresh every N seconds (0 = only on start and when asked).")]
        public float autoRefreshInterval = 0f;

        private ReflectionProbe _probe;
        private float _timer;

        private void OnEnable()
        {
            Instance = this;
            _probe = GetComponent<ReflectionProbe>();
        }

        private void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        private void Start()
        {
            Refresh();
        }

        private void Update()
        {
            if (autoRefreshInterval <= 0f) return;
            _timer += Time.unscaledDeltaTime;
            if (_timer >= autoRefreshInterval) { _timer = 0f; Refresh(); }
        }

        /// <summary>Re-capture the surroundings (spread over several frames by Unity).</summary>
        public void Refresh()
        {
            if (_probe != null && _probe.mode == UnityEngine.Rendering.ReflectionProbeMode.Realtime)
                _probe.RenderProbe();
        }
    }
}
