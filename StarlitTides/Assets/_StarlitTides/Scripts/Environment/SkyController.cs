// SkyController: controls the whole night atmosphere from one place.
//
// It feeds the night sky shader and keeps the moonlight, ambient light and fog
// matching it. Other features only change four sliders:
//   sunset    0..1  intro cinematic: sunset turning into night
//   storm     0..1  storm clouds, darker and foggier
//   aurora    0..1  aurora night
//   lightning 0..1  set by TriggerLightning(), fades by itself
// Fog, ambient colours and the moonlight are blended from three presets:
// Night, Storm and Sunset.

using UnityEngine;
using UnityEngine.Rendering;

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
        [Tooltip("The directional light used as the moon (and as the sun in the intro).")]
        public Light moonLight;
        [Tooltip("Where the sun is during the intro sunset.")]
        public Vector3 sunDirection = new Vector3(0.55f, 0.04f, 0.83f);

        [Header("Sliders (animated by Weather and the Intro)")]
        [Range(0f, 1f)] public float sunset = 0f;
        [Range(0f, 1f)] public float storm = 0f;
        [Range(0f, 1f)] public float aurora = 0f;
        [Tooltip("A faint aurora that always shimmers on clear nights (the weather's aurora adds to it).")]
        [Range(0f, 1f)] public float ambientAurora = 0.15f;

        [Header("Atmosphere (StarlitAtmosphere.shader)")]
        [Tooltip("Sea-mist density at sea level.")]
        public float mistDensity = 0.0042f;
        [Tooltip("How fast the mist thins with height (bigger = a lower, flatter mist layer).")]
        public float mistFalloff = 0.07f;
        [Tooltip("No mist closer than this (metres), so the play area stays crisp.")]
        public float mistStart = 26f;
        [Range(0f, 1f)] public float mistMax = 0.72f;
        [Tooltip("Strength of the god rays streaming from the moon.")]
        public float godRays = 0.9f;
        public float godRayThreshold = 0.35f;
        [Range(0f, 1f)] public float lightning = 0f;
        [Tooltip("How fast a lightning flash fades, per second.")]
        public float lightningDecay = 4f;

        [Header("Presets")]
        public AtmospherePreset night = new AtmospherePreset
        {
            fogColor = new Color(0.07f, 0.08f, 0.18f),
            fogDensity = 0.0008f,
            ambientSky = new Color(0.2f, 0.25f, 0.55f),
            ambientEquator = new Color(0.13f, 0.12f, 0.28f),
            ambientGround = new Color(0.03f, 0.03f, 0.05f),
            lightColor = new Color(0.7f, 0.8f, 1.0f),
            lightIntensity = 1.5f
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

        float skyTime;
        Color lastAmbient = Color.clear;
        Quaternion nightRotation;   // the moonlight's direction at night
        bool hasNightRotation;

        void OnEnable()
        {
            Instance = this;
            RememberNightRotation();
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        void RememberNightRotation()
        {
            if (moonLight == null || hasNightRotation) return;
            nightRotation = moonLight.transform.rotation;
            hasNightRotation = true;
        }

        // A lightning flash (0..1). The sky, ambient light and moonlight all flicker.
        public void TriggerLightning(float strength = 1f)
        {
            lightning = Mathf.Max(lightning, Mathf.Clamp01(strength));
        }

        void Update()
        {
            float dt = Application.isPlaying ? Time.deltaTime : 0.016f;
            // Unscaled, so the stars keep twinkling during pause and slow motion.
            skyTime += Application.isPlaying ? Time.unscaledDeltaTime : 0.016f;
            if (lightning > 0f) lightning = Mathf.Max(0f, lightning - lightningDecay * dt);

            // Blend night -> storm, then sunset on top.
            AtmospherePreset look = Blend(night, stormPreset, storm);
            look = Blend(look, sunsetPreset, sunset);

            ApplyFogAndAmbient(look);
            Vector3 moonDirection = ApplyMoonlight(look);
            SendToSkyShader(moonDirection);
            SendToAtmosphere(look);
        }

        void ApplyFogAndAmbient(AtmospherePreset look)
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogColor = look.fogColor;
            RenderSettings.fogDensity = look.fogDensity;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = look.ambientSky + Color.white * (lightning * 0.25f);
            RenderSettings.ambientEquatorColor = look.ambientEquator;
            RenderSettings.ambientGroundColor = look.ambientGround;

            // Unity only picks up new ambient colours after UpdateEnvironment().
            // It's cheap in Trilight mode, but we still only call it when something changed.
            Color ambientNow = RenderSettings.ambientSkyColor + RenderSettings.ambientEquatorColor + RenderSettings.ambientGroundColor;
            bool changed = (ambientNow - lastAmbient).maxColorComponent > 0.002f;
            if (changed || lastAmbient == Color.clear)
            {
                lastAmbient = ambientNow;
                DynamicGI.UpdateEnvironment();
            }
        }

        // Sets the moonlight colour, brightness and direction.
        // Returns the direction TO the moon, which the sky shader needs.
        Vector3 ApplyMoonlight(AtmospherePreset look)
        {
            if (moonLight == null) return new Vector3(0.32f, 0.14f, 0.92f).normalized;

            RememberNightRotation();
            moonLight.color = look.lightColor;
            moonLight.intensity = look.lightIntensity + lightning * 2.5f;

            if (Application.isPlaying)
            {
                // During the sunset the light swings toward the sun.
                Quaternion sunRotation = Quaternion.LookRotation(-sunDirection.normalized, Vector3.up);
                moonLight.transform.rotation = Quaternion.Slerp(nightRotation, sunRotation, sunset);
                return -(nightRotation * Vector3.forward);
            }

            // In the editor, follow the light as you rotate it.
            return -(moonLight.transform.rotation * Vector3.forward);
        }

        // The full-screen atmosphere: haze lit by the sky, moonlight scattered
        // through it, and god rays. Storms make the air thicker and darker.
        void SendToAtmosphere(AtmospherePreset look)
        {
            Color haze = look.fogColor * 0.85f;
            Color moonGlow = look.lightColor * Mathf.Min(look.lightIntensity, 1.6f) * 0.11f * (1f - storm * 0.7f);
            Shader.SetGlobalColor("_AtmoColor", haze);
            Shader.SetGlobalColor("_AtmoMoonColor", moonGlow);
            Shader.SetGlobalVector("_AtmoParams", new Vector4(mistDensity * (1f + storm * 1.5f), mistFalloff, mistStart, mistMax));
            Shader.SetGlobalVector("_AtmoRayParams", new Vector4(godRays * (1f - storm) * (1f - sunset * 0.5f), godRayThreshold, 0.955f, godRays > 0f ? 1f : 0f));
            Shader.SetGlobalFloat("_AtmoBaseHeight", 0f);
        }

        void SendToSkyShader(Vector3 moonDirection)
        {
            Shader.SetGlobalVector("_MoonDirection", moonDirection);
            Shader.SetGlobalVector("_SunDirection", sunDirection.normalized);
            Shader.SetGlobalFloat("_SunsetBlend", sunset);
            Shader.SetGlobalFloat("_StormAmount", storm);
            float clearNight = (1f - storm) * (1f - sunset);
            Shader.SetGlobalFloat("_AuroraAmount", Mathf.Max(aurora, ambientAurora * clearNight));
            Shader.SetGlobalFloat("_LightningFlash", lightning);
            Shader.SetGlobalFloat("_SkyTime", skyTime);
        }

        static AtmospherePreset Blend(AtmospherePreset a, AtmospherePreset b, float t)
        {
            AtmospherePreset result = new AtmospherePreset();
            result.fogColor = Color.Lerp(a.fogColor, b.fogColor, t);
            result.fogDensity = Mathf.Lerp(a.fogDensity, b.fogDensity, t);
            result.ambientSky = Color.Lerp(a.ambientSky, b.ambientSky, t);
            result.ambientEquator = Color.Lerp(a.ambientEquator, b.ambientEquator, t);
            result.ambientGround = Color.Lerp(a.ambientGround, b.ambientGround, t);
            result.lightColor = Color.Lerp(a.lightColor, b.lightColor, t);
            result.lightIntensity = Mathf.Lerp(a.lightIntensity, b.lightIntensity, t);
            return result;
        }
    }
}
