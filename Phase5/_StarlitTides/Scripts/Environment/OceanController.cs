// OceanController: drives the ocean shader and answers "how high is the water here?".
//
// What it does every frame:
// - Advances the ocean clock and the tide (how far the sea has flowed toward the camera).
// - Collects the glowing spots from every WaterDisturber and every ripple and
//   sends them to the shader as one list.
// - Copies the wave settings from the ocean material, so GetWaterHeight() uses
//   exactly the same Gerstner wave maths as the shader. That's why the boat,
//   orbs and rocks bob in sync with the waves you see.
// Other phases change: tideSpeed (difficulty), waveScale (storms) and
// seaGlowBoost (Starburst power-up).

using System.Collections.Generic;
using UnityEngine;

namespace StarlitTides
{
    [ExecuteAlways]          // also runs in the editor, so the sea animates while you work
    [DisallowMultipleComponent]
    public class OceanController : MonoBehaviour
    {
        public static OceanController Instance { get; private set; }

        // Must match the array size in StarlitOcean.shader.
        public const int MaxDisturbPoints = 32;

        [Tooltip("The ocean material. The wave settings are read from it.")]
        public Material oceanMaterial;

        [Header("Tide")]
        [Tooltip("How fast the sea flows toward the camera, in m/s.")]
        public float tideSpeed = 4f;

        [Header("Weather and power-ups")]
        [Tooltip("1 = calm night, about 1.8 = storm.")]
        [Range(0f, 3f)] public float waveScale = 1f;
        [Tooltip("Extra swell on top of waveScale (the whale's splash). 1 = waves twice as big.")]
        [Range(0f, 3f)] public float swell = 0f;
        [Tooltip("Starburst: 0 = off, 1 = the whole sea glows.")]
        [Range(0f, 1f)] public float seaGlowBoost = 0f;
        [ColorUsage(false, true)] public Color seaGlowColor = new Color(1.1f, 0.6f, 0.12f);
        [Tooltip("Overall plankton glow. The intro animates it 0 -> 1 as night falls.")]
        [Range(0f, 1f)] public float nightGlow = 1f;

        [Header("Ripples")]
        [Tooltip("How long a ripple glows, in seconds.")]
        public float defaultRippleLife = 1.6f;

        // A short-lived glowing ring on the water (wakes, splashes, pickups).
        struct Ripple
        {
            public Vector2 position;   // world X and Z
            public float radius;
            public float strength;
            public float age;
            public float life;
        }

        List<WaterDisturber> disturbers = new List<WaterDisturber>();
        List<Ripple> ripples = new List<Ripple>();
        Vector4[] glowPoints = new Vector4[MaxDisturbPoints];

        float oceanTime;
        float tideOffset;
        double lastEditorTime;

        // Copies of the material's wave settings (each: direction x, direction z, steepness, wavelength).
        Vector4[] waves = new Vector4[4];
        float waveFadeStart = 180f;
        float waveFadeEnd = 650f;
        Vector4 calmZone = new Vector4(-85f, 95f, 34f, 28f);   // the lagoon: centre x, z and size x, z

        public float OceanTime { get { return oceanTime; } }
        public float TideOffset { get { return tideOffset; } }

        void OnEnable()
        {
            Instance = this;
#if UNITY_EDITOR
            lastEditorTime = UnityEditor.EditorApplication.timeSinceStartup;
#endif
        }

        void OnDisable()
        {
            if (Instance == this) Instance = null;
        }

        void Update()
        {
            float dt = GetDeltaTime();
            oceanTime += dt;
            tideOffset += tideSpeed * dt;
            // Keep the number small, otherwise float precision breaks the waves after a long time.
            if (tideOffset > 100000f) tideOffset -= 100000f;

            ReadWaveSettings();
            UpdateRipples(dt);
            SendToShader();
        }

        // In Play mode use Time.deltaTime. In the editor use the editor clock instead.
        float GetDeltaTime()
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                double now = UnityEditor.EditorApplication.timeSinceStartup;
                float dt = Mathf.Min((float)(now - lastEditorTime), 0.1f);
                lastEditorTime = now;
                return dt;
            }
#endif
            return Time.deltaTime;
        }

        void ReadWaveSettings()
        {
            if (oceanMaterial == null) return;
            waves[0] = oceanMaterial.GetVector("_WaveA");
            waves[1] = oceanMaterial.GetVector("_WaveB");
            waves[2] = oceanMaterial.GetVector("_WaveC");
            waves[3] = oceanMaterial.GetVector("_WaveD");
            waveFadeStart = oceanMaterial.GetFloat("_WaveFadeStart");
            waveFadeEnd = oceanMaterial.GetFloat("_WaveFadeEnd");
            calmZone = oceanMaterial.GetVector("_CalmZone");
        }

        void UpdateRipples(float dt)
        {
            // Go backwards so we can remove items while looping.
            for (int i = ripples.Count - 1; i >= 0; i--)
            {
                Ripple r = ripples[i];
                r.age += dt;
                r.position.y -= tideSpeed * dt;   // ripples float away on the tide
                if (r.age >= r.life) ripples.RemoveAt(i);
                else ripples[i] = r;
            }
        }

        void SendToShader()
        {
            int count = 0;

            // First the objects touching the water (boat, rocks, orbs...).
            foreach (WaterDisturber d in disturbers)
            {
                if (count >= MaxDisturbPoints) break;
                if (d == null || !d.isActiveAndEnabled) continue;

                Vector3 p = d.transform.position;
                bool touchingWater = Mathf.Abs(p.y - GetWaterHeight(p)) <= d.maxHeightAboveWater;
                if (!touchingWater) continue;

                glowPoints[count] = new Vector4(p.x, p.z, d.radius, d.CurrentStrength);
                count++;
            }

            // Then the ripples, newest first (they're the brightest).
            for (int i = ripples.Count - 1; i >= 0 && count < MaxDisturbPoints; i--)
            {
                Ripple r = ripples[i];
                float t = r.age / r.life;           // 0 = new, 1 = gone
                float fade = (1f - t) * (1f - t);
                float radius = r.radius * (0.6f + t * 1.2f);   // the ring grows as it fades
                glowPoints[count] = new Vector4(r.position.x, r.position.y, radius, r.strength * fade);
                count++;
            }

            // Clear the unused slots.
            for (int i = count; i < MaxDisturbPoints; i++) glowPoints[i] = Vector4.zero;

            Shader.SetGlobalFloat("_OceanTime", oceanTime);
            Shader.SetGlobalFloat("_TideOffset", tideOffset);
            Shader.SetGlobalFloat("_WaveScale", waveScale * (1f + swell));
            Shader.SetGlobalFloat("_SeaGlowBoost", seaGlowBoost);
            Shader.SetGlobalColor("_SeaGlowColor", seaGlowColor);
            Shader.SetGlobalFloat("_SeaNightGlow", nightGlow);
            Shader.SetGlobalVectorArray("_DisturbPoints", glowPoints);
            Shader.SetGlobalInt("_DisturbCount", count);
        }

        public void Register(WaterDisturber d)
        {
            if (!disturbers.Contains(d)) disturbers.Add(d);
        }

        public void Unregister(WaterDisturber d)
        {
            disturbers.Remove(d);
        }

        // Adds a short glowing ripple (splashes, the boat's wake, orb pickups).
        public void AddRipple(Vector3 worldPosition, float radius, float strength, float life = -1f)
        {
            if (ripples.Count > 96) ripples.RemoveAt(0);   // forget the oldest

            Ripple r = new Ripple();
            r.position = new Vector2(worldPosition.x, worldPosition.z);
            r.radius = radius;
            r.strength = strength;
            r.age = 0f;
            r.life = life > 0f ? life : defaultRippleLife;
            ripples.Add(r);
        }

        // Height of the sea surface at this point (same result as the shader).
        public float GetWaterHeight(Vector3 worldPosition)
        {
            // Gerstner waves also push the water sideways, so the water that ends
            // up here started a little to the side. One correction step is enough.
            Vector3 normal;
            Vector3 first = WaveOffset(worldPosition.x, worldPosition.z, out normal);
            Vector3 corrected = WaveOffset(worldPosition.x - first.x, worldPosition.z - first.z, out normal);
            return transform.position.y + corrected.y;
        }

        // Which way the water surface faces at this point (for tilting floating things).
        public Vector3 GetWaterNormal(Vector3 worldPosition)
        {
            Vector3 normal;
            WaveOffset(worldPosition.x, worldPosition.z, out normal);
            return normal;
        }

        // The same 4 Gerstner waves as the vertex shader (see StarlitOcean.shader).
        Vector3 WaveOffset(float x, float z, out Vector3 normal)
        {
            float amplitude = WaveAmplitude(x, z);
            float sampleZ = z + tideOffset;   // the pattern flows with the tide

            Vector3 tangent = new Vector3(1f, 0f, 0f);
            Vector3 binormal = new Vector3(0f, 0f, 1f);
            Vector3 offset = Vector3.zero;

            for (int i = 0; i < 4; i++)
            {
                Vector4 wave = waves[i];
                if (wave.w <= 0.001f) continue;   // no wavelength = no wave

                float steepness = wave.z * amplitude;
                float k = 2f * Mathf.PI / wave.w;          // wave number
                float speed = Mathf.Sqrt(9.8f / k);       // real deep-water wave speed
                Vector2 dir = new Vector2(wave.x, wave.y).normalized;
                float f = k * (dir.x * x + dir.y * sampleZ - speed * oceanTime);
                float a = steepness / k;                   // wave height
                float sin = Mathf.Sin(f);
                float cos = Mathf.Cos(f);

                offset += new Vector3(dir.x * a * cos, a * sin, dir.y * a * cos);
                tangent += new Vector3(-dir.x * dir.x * steepness * sin, dir.x * steepness * cos, -dir.x * dir.y * steepness * sin);
                binormal += new Vector3(-dir.x * dir.y * steepness * sin, dir.y * steepness * cos, -dir.y * dir.y * steepness * sin);
            }

            normal = Vector3.Cross(binormal, tangent).normalized;
            return offset;
        }

        // Waves fade out far away and inside the calm lagoon (same as the shader).
        float WaveAmplitude(float x, float z)
        {
            float distance = Mathf.Sqrt(x * x + z * z);
            float farFade = 1f - SmoothStep(waveFadeStart, waveFadeEnd, distance);

            float lagoonX = (x - calmZone.x) / Mathf.Max(calmZone.z, 0.001f);
            float lagoonZ = (z - calmZone.y) / Mathf.Max(calmZone.w, 0.001f);
            float lagoonDistance = Mathf.Sqrt(lagoonX * lagoonX + lagoonZ * lagoonZ);   // < 1 means inside
            float lagoonFade = SmoothStep(0.7f, 1.3f, lagoonDistance);

            return farFade * lagoonFade * waveScale * (1f + swell);
        }

        // Same as HLSL's smoothstep().
        static float SmoothStep(float from, float to, float x)
        {
            float t = Mathf.Clamp01((x - from) / (to - from));
            return t * t * (3f - 2f * t);
        }
    }
}
