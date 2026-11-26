// =============================================================================
//  SkyController - one place that controls the whole night atmosphere
// -----------------------------------------------------------------------------
//  Feeds the Skybox/StarlitNight shader and keeps the moonlight, ambient
//  light and fog consistent with it.  Later phases only change four numbers:
//      sunset    0..1   (intro cinematic: sunset -> night)
//      storm     0..1   (weather: storm clouds, darker, rougher)
//      aurora    0..1   (weather: aurora night)
//      lightning 0..1   (set by TriggerLightning, decays automatically)
//  Everything else (fog colour, ambient colours, light intensity) is blended
//  from three presets: Night, Storm and Sunset.
// =============================================================================
using UnityEngine;

namespace StarlitTides
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public class SkyController : MonoBehaviour
    {
        public static SkyController Instance { get; private set; }

        [System.Serializable]
        public struct AtmospherePreset
        {
            public Color fogColor;
            public float fogDensity;
            [ColorUsage(false, true)] public Color ambientSky;
            [ColorUsage(false, true)] public Color ambientEquator;
            [ColorUsage(false, true)] public Color ambientGround;
            public Color lightColor;
            public float lightIntensity;
        }

        [Header("References")]
        [Tooltip("Directional light that acts as the moon (and the sun during the intro).")]
        public Light moonLight;
        [Tooltip("Optional: direction of the setting sun used during the intro sunset.")]
        public Vector3 sunDirection = new Vector3(0.55f, 0.04f, 0.83f);

        [Header("State (animated by Weather / Intro)")]
        [Range(0f, 1f)] public float sunset = 0f;
        [Range(0f, 1f)] public float storm = 0f;
        [Range(0f, 1f)] public float aurora = 0f;
        [Range(0f, 1f)] public float lightning = 0f;
        [Tooltip("How fast a lightning flash fades (per second).")]
        public float lightningDecay = 4f;

        [Header("Presets")]
        public AtmospherePreset night = new AtmospherePreset
        {
            fogColor = new Color(0.075f, 0.075f, 0.17f),
            fogDensity = 0.0012f,
            ambientSky = new Color(0.24f, 0.28f, 0.55f),
            ambientEquator = new Color(0.19f, 0.16f, 0.38f),
            ambientGround = new Color(0.05f, 0.05f, 0.08f),
            lightColor = new Color(0.66f, 0.76f, 1.0f),
            lightIntensity = 0.8f
        };
        public AtmospherePreset stormPreset = new AtmospherePreset
        {
            fogColor = new Color(0.05f, 0.055f, 0.08f),
            fogDensity = 0.0028f,
            ambientSky = new Color(0.06f, 0.07f, 0.11f),
            ambientEquator = new Color(0.05f, 0.05f, 0.08f),
            ambientGround = new Color(0.02f, 0.02f, 0.03f),
            lightColor = new Color(0.55f, 0.62f, 0.8f),
            lightIntensity = 0.15f
        };
        public AtmospherePreset sunsetPreset = new AtmospherePreset
        {
            fogColor = new Color(0.55f, 0.36f, 0.38f),
            fogDensity = 0.0010f,
            ambientSky = new Color(0.45f, 0.42f, 0.62f),
            ambientEquator = new Color(0.7f, 0.42f, 0.35f),
            ambientGround = new Color(0.12f, 0.08f, 0.08f),
            lightColor = new Color(1.0f, 0.62f, 0.38f),
            lightIntensity = 1.4f
        };

        private static readonly int MoonDirId   = Shader.PropertyToID("_MoonDirection");
        private static readonly int SunDirId    = Shader.PropertyToID("_SunDirection");
        private static readonly int SunsetId    = Shader.PropertyToID("_SunsetBlend");
        private static readonly int StormId     = Shader.PropertyToID("_StormAmount");
        private static readonly int AuroraId    = Shader.PropertyToID("_AuroraAmount");
        private static readonly int LightningId = Shader.PropertyToID("_LightningFlash");
        private static readonly int SkyTimeId   = Shader.PropertyToID("_SkyTime");

        private float _skyTime;
        private Color _lastAmbient = Color.clear;
        private Quaternion _nightRotation;
        private bool _hasNightRotation;

        private void OnEnable()
        {
            Instance = this;
            if (moonLight != null) { _nightRotation = moonLight.transform.rotation; _hasNightRotation = true; }
        }

        private void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Flash of lightning (0..1 strength). The sky and the moonlight flicker.</summary>
        public void TriggerLightning(float strength = 1f)
        {
            lightning = Mathf.Max(lightning, Mathf.Clamp01(strength));
        }

        private void Update()
        {
            float dt = Application.isPlaying ? Time.deltaTime : 0.016f;
            _skyTime += Application.isPlaying ? Time.unscaledDeltaTime : 0.016f;
            if (lightning > 0f) lightning = Mathf.Max(0f, lightning - lightningDecay * dt);

            // ---- blend the presets: night -> storm, then -> sunset on top ----
            AtmospherePreset a = Lerp(night, stormPreset, storm);
            a = Lerp(a, sunsetPreset, sunset);

            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = a.fogColor;
            RenderSettings.fogDensity = a.fogDensity;
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = a.ambientSky + Color.white * (lightning * 0.25f);
            RenderSettings.ambientEquatorColor = a.ambientEquator;
            RenderSettings.ambientGroundColor = a.ambientGround;
            // The ambient light probe must be recomputed when ambient colours change
            // (cheap for Trilight mode), otherwise objects keep the old ambient light.
            Color ambientKey = RenderSettings.ambientSkyColor + RenderSettings.ambientEquatorColor * 0.37f + RenderSettings.ambientGroundColor * 0.11f;
            if ((ambientKey - _lastAmbient).maxColorComponent > 0.002f || _lastAmbient == Color.clear)
            {
                _lastAmbient = ambientKey;
                DynamicGI.UpdateEnvironment();
            }

            Vector3 moonDir = new Vector3(0.32f, 0.21f, 0.92f).normalized;
            if (moonLight != null)
            {
                moonLight.color = a.lightColor;
                moonLight.intensity = a.lightIntensity + lightning * 2.5f;

                // During the sunset the light comes from the sun; at night from the moon.
                if (!_hasNightRotation) { _nightRotation = moonLight.transform.rotation; _hasNightRotation = true; }
                if (Application.isPlaying && sunset > 0.001f)
                {
                    Quaternion sunRot = Quaternion.LookRotation(-sunDirection.normalized, Vector3.up);
                    moonLight.transform.rotation = Quaternion.Slerp(_nightRotation, sunRot, sunset);
                }
                else if (Application.isPlaying)
                {
                    moonLight.transform.rotation = _nightRotation;
                }
                moonDir = -((Application.isPlaying ? _nightRotation : moonLight.transform.rotation) * Vector3.forward);
            }

            Shader.SetGlobalVector(MoonDirId, moonDir);
            Shader.SetGlobalVector(SunDirId, sunDirection.normalized);
            Shader.SetGlobalFloat(SunsetId, sunset);
            Shader.SetGlobalFloat(StormId, storm);
            Shader.SetGlobalFloat(AuroraId, aurora);
            Shader.SetGlobalFloat(LightningId, lightning);
            Shader.SetGlobalFloat(SkyTimeId, _skyTime);
        }

        private static AtmospherePreset Lerp(AtmospherePreset x, AtmospherePreset y, float t)
        {
            return new AtmospherePreset
            {
                fogColor = Color.Lerp(x.fogColor, y.fogColor, t),
                fogDensity = Mathf.Lerp(x.fogDensity, y.fogDensity, t),
                ambientSky = Color.Lerp(x.ambientSky, y.ambientSky, t),
                ambientEquator = Color.Lerp(x.ambientEquator, y.ambientEquator, t),
                ambientGround = Color.Lerp(x.ambientGround, y.ambientGround, t),
                lightColor = Color.Lerp(x.lightColor, y.lightColor, t),
                lightIntensity = Mathf.Lerp(x.lightIntensity, y.lightIntensity, t)
            };
        }
    }
}
