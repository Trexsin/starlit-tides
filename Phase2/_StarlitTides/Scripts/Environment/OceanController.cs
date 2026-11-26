// =============================================================================
//  OceanController - drives the StarlitTides/Ocean shader
// -----------------------------------------------------------------------------
//  * Owns the ocean clock and the tide (how far the sea has flowed toward -Z).
//  * Collects every WaterDisturber + short-lived ripples/trail points and sends
//    them to the shader so the plankton glows around them.
//  * Gives gameplay code the exact wave height at any point (GetWaterHeight),
//    using the SAME Gerstner maths as the shader, so the boat, orbs and
//    obstacles bob perfectly in sync with the waves you see.
//  * Exposes WaveScale (weather), TideSpeed (difficulty) and SeaGlowBoost
//    (Starburst power-up) for later phases.
// =============================================================================
using System.Collections.Generic;
using UnityEngine;

namespace StarlitTides
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class OceanController : MonoBehaviour
    {
        public static OceanController Instance { get; private set; }

        public const int MaxDisturbPoints = 32;   // must match the shader array size

        [Tooltip("The ocean material (StarlitTides/Ocean). Wave settings are read from it.")]
        public Material oceanMaterial;

        [Header("Tide")]
        [Tooltip("Speed (m/s) at which the sea flows toward the camera (-Z).")]
        public float tideSpeed = 4f;

        [Header("Weather and power-ups (driven by later phases)")]
        [Tooltip("1 = calm night, about 1.8 = storm.")]
        [Range(0f, 3f)] public float waveScale = 1f;
        [Tooltip("Starburst: 0 = off, 1 = the whole sea glows.")]
        [Range(0f, 1f)] public float seaGlowBoost = 0f;
        [ColorUsage(false, true)] public Color seaGlowColor = new Color(1.6f, 1.3f, 0.5f);

        [Header("Trail ripples")]
        [Tooltip("How long a trail ripple keeps glowing (seconds).")]
        public float defaultRippleLife = 1.6f;

        // ---- shader property IDs (cached for speed) ----
        private static readonly int OceanTimeId    = Shader.PropertyToID("_OceanTime");
        private static readonly int TideOffsetId   = Shader.PropertyToID("_TideOffset");
        private static readonly int WaveScaleId    = Shader.PropertyToID("_WaveScale");
        private static readonly int SeaGlowBoostId = Shader.PropertyToID("_SeaGlowBoost");
        private static readonly int SeaGlowColorId = Shader.PropertyToID("_SeaGlowColor");
        private static readonly int DisturbPtsId   = Shader.PropertyToID("_DisturbPoints");
        private static readonly int DisturbCountId = Shader.PropertyToID("_DisturbCount");
        private static readonly int WaveAId = Shader.PropertyToID("_WaveA");
        private static readonly int WaveBId = Shader.PropertyToID("_WaveB");
        private static readonly int WaveCId = Shader.PropertyToID("_WaveC");
        private static readonly int WaveDId = Shader.PropertyToID("_WaveD");
        private static readonly int FadeStartId = Shader.PropertyToID("_WaveFadeStart");
        private static readonly int FadeEndId   = Shader.PropertyToID("_WaveFadeEnd");
        private static readonly int CalmZoneId  = Shader.PropertyToID("_CalmZone");

        private struct Ripple
        {
            public Vector2 xz;
            public float radius, strength, age, life;
        }

        private readonly List<WaterDisturber> _disturbers = new List<WaterDisturber>();
        private readonly List<Ripple> _ripples = new List<Ripple>();
        private readonly Vector4[] _points = new Vector4[MaxDisturbPoints];

        private float _oceanTime;
        private float _tideOffset;
        private double _lastEditorTime;

        // cached wave parameters (read from the material each frame - cheap)
        private Vector4[] _waves = new Vector4[4];
        private float _fadeStart = 180f, _fadeEnd = 650f;
        private Vector4 _calmZone = new Vector4(-85f, 95f, 34f, 28f);

        public float OceanTime => _oceanTime;
        public float TideOffset => _tideOffset;

        private void OnEnable()
        {
            Instance = this;
#if UNITY_EDITOR
            _lastEditorTime = UnityEditor.EditorApplication.timeSinceStartup;
#endif
        }

        private void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        private void Update()
        {
            float dt = Time.deltaTime;
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                // Animate in the Scene view too, so you can see the sea while you build.
                double now = UnityEditor.EditorApplication.timeSinceStartup;
                dt = Mathf.Min((float)(now - _lastEditorTime), 0.1f);
                _lastEditorTime = now;
            }
#endif
            _oceanTime += dt;
            _tideOffset += tideSpeed * dt;
            // keep the number small so float precision never breaks the shader
            if (_tideOffset > 100000f) _tideOffset -= 100000f;

            ReadWaveSettings();
            UpdateRipples(dt);
            PushToShader();
        }

        private void ReadWaveSettings()
        {
            if (oceanMaterial == null) return;
            _waves[0] = oceanMaterial.GetVector(WaveAId);
            _waves[1] = oceanMaterial.GetVector(WaveBId);
            _waves[2] = oceanMaterial.GetVector(WaveCId);
            _waves[3] = oceanMaterial.GetVector(WaveDId);
            _fadeStart = oceanMaterial.GetFloat(FadeStartId);
            _fadeEnd = oceanMaterial.GetFloat(FadeEndId);
            _calmZone = oceanMaterial.GetVector(CalmZoneId);
        }

        private void UpdateRipples(float dt)
        {
            for (int i = _ripples.Count - 1; i >= 0; i--)
            {
                Ripple r = _ripples[i];
                r.age += dt;
                r.xz.y -= tideSpeed * dt;   // ripples drift away with the tide -> natural wake trail
                if (r.age >= r.life) { _ripples.RemoveAt(i); continue; }
                _ripples[i] = r;
            }
        }

        private void PushToShader()
        {
            int count = 0;

            // 1) live disturbers (boat, obstacles, orbs)
            for (int i = 0; i < _disturbers.Count && count < MaxDisturbPoints; i++)
            {
                WaterDisturber d = _disturbers[i];
                if (d == null || !d.isActiveAndEnabled) continue;
                Vector3 p = d.transform.position;
                if (Mathf.Abs(p.y - GetWaterHeight(p)) > d.maxHeightAboveWater) continue; // not touching water
                _points[count++] = new Vector4(p.x, p.z, d.radius, d.CurrentStrength);
            }

            // 2) ripples, newest first (most visible)
            for (int i = _ripples.Count - 1; i >= 0 && count < MaxDisturbPoints; i--)
            {
                Ripple r = _ripples[i];
                float t = r.age / r.life;
                float fade = (1f - t) * (1f - t);
                float radius = r.radius * (0.6f + t * 1.2f);   // rings expand as they fade
                _points[count++] = new Vector4(r.xz.x, r.xz.y, radius, r.strength * fade);
            }

            for (int i = count; i < MaxDisturbPoints; i++) _points[i] = Vector4.zero;

            Shader.SetGlobalFloat(OceanTimeId, _oceanTime);
            Shader.SetGlobalFloat(TideOffsetId, _tideOffset);
            Shader.SetGlobalFloat(WaveScaleId, waveScale);
            Shader.SetGlobalFloat(SeaGlowBoostId, seaGlowBoost);
            Shader.SetGlobalColor(SeaGlowColorId, seaGlowColor);
            Shader.SetGlobalVectorArray(DisturbPtsId, _points);
            Shader.SetGlobalInt(DisturbCountId, count);
        }

        // ---------------------------------------------------------------------
        // Public API
        // ---------------------------------------------------------------------
        public void Register(WaterDisturber d) { if (!_disturbers.Contains(d)) _disturbers.Add(d); }
        public void Unregister(WaterDisturber d) { _disturbers.Remove(d); }

        /// <summary>Adds a short-lived glowing ripple (splashes, wake, pickups).</summary>
        public void AddRipple(Vector3 worldPos, float radius, float strength, float life = -1f)
        {
            if (_ripples.Count > 96) _ripples.RemoveAt(0);
            _ripples.Add(new Ripple
            {
                xz = new Vector2(worldPos.x, worldPos.z),
                radius = radius,
                strength = strength,
                age = 0f,
                life = life > 0f ? life : defaultRippleLife
            });
        }

        /// <summary>Exact height of the sea surface at a world position (matches the shader).</summary>
        public float GetWaterHeight(Vector3 worldPos)
        {
            // Gerstner waves also move points sideways, so we take one step back
            // to find which undisplaced point ended up here, then sample again.
            Vector3 first = Displacement(worldPos.x, worldPos.z, out _);
            Vector3 second = Displacement(worldPos.x - first.x, worldPos.z - first.z, out _);
            return transform.position.y + second.y;
        }

        /// <summary>Surface normal at a world position (for tilting floating objects).</summary>
        public Vector3 GetWaterNormal(Vector3 worldPos)
        {
            Displacement(worldPos.x, worldPos.z, out Vector3 normal);
            return normal;
        }

        // CPU copy of the vertex shader's wave maths.
        private Vector3 Displacement(float x, float z, out Vector3 normal)
        {
            float amp = WaveAmplitude(x, z);
            float px = x;
            float pz = z + _tideOffset;
            Vector3 tangent = new Vector3(1, 0, 0);
            Vector3 binormal = new Vector3(0, 0, 1);
            Vector3 offset = Vector3.zero;

            for (int i = 0; i < 4; i++)
            {
                Vector4 w = _waves[i];
                if (w.w <= 0.001f) continue;
                float steep = w.z * amp;
                float k = 2f * Mathf.PI / w.w;
                float c = Mathf.Sqrt(9.8f / k);
                Vector2 d = new Vector2(w.x, w.y).normalized;
                float f = k * (d.x * px + d.y * pz - c * _oceanTime);
                float a = steep / k;
                float s = Mathf.Sin(f), co = Mathf.Cos(f);
                tangent  += new Vector3(-d.x * d.x * steep * s, d.x * steep * co, -d.x * d.y * steep * s);
                binormal += new Vector3(-d.x * d.y * steep * s, d.y * steep * co, -d.y * d.y * steep * s);
                offset   += new Vector3(d.x * a * co, a * s, d.y * a * co);
            }
            normal = Vector3.Cross(binormal, tangent).normalized;
            return offset;
        }

        private float WaveAmplitude(float x, float z)
        {
            float dist = Mathf.Sqrt(x * x + z * z);
            float far = 1f - SmoothStep(_fadeStart, _fadeEnd, dist);
            float ex = (x - _calmZone.x) / Mathf.Max(_calmZone.z, 0.001f);
            float ez = (z - _calmZone.y) / Mathf.Max(_calmZone.w, 0.001f);
            float calm = SmoothStep(0.7f, 1.3f, Mathf.Sqrt(ex * ex + ez * ez));
            return far * calm * waveScale;
        }

        private static float SmoothStep(float a, float b, float x)
        {
            float t = Mathf.Clamp01((x - a) / (b - a));
            return t * t * (3f - 2f * t);
        }
    }
}
