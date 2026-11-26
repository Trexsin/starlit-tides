// =============================================================================
//  LanternFlicker - candle-like flicker for a lantern's light and glow
// -----------------------------------------------------------------------------
//  Uses Perlin noise (smooth randomness), not Random, so the flame breathes
//  instead of strobing. Each lantern gets its own random offset so they never
//  flicker in sync. Optionally pulses the emissive glow of a renderer too.
// =============================================================================
using UnityEngine;

namespace StarlitTides
{
    public class LanternFlicker : MonoBehaviour
    {
        public Light targetLight;
        [Tooltip("Optional: renderer whose emission flickers with the light.")]
        public Renderer glowRenderer;
        [ColorUsage(false, true)] public Color glowColor = new Color(4f, 2.6f, 1.1f);

        [Range(0f, 1f)] public float flickerAmount = 0.25f;
        public float flickerSpeed = 3f;
        [Tooltip("Rare deeper dips, like wind catching the flame.")]
        [Range(0f, 1f)] public float gustAmount = 0.2f;

        private float _baseIntensity;
        private float _seed;
        private MaterialPropertyBlock _mpb;
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");

        private void Awake()
        {
            if (targetLight == null) targetLight = GetComponentInChildren<Light>();
            if (targetLight != null) _baseIntensity = targetLight.intensity;
            _seed = Random.value * 100f;
            _mpb = new MaterialPropertyBlock();
        }

        private void Update()
        {
            float t = Time.time * flickerSpeed + _seed;
            float n = Mathf.PerlinNoise(t, _seed) * 2f - 1f;                 // -1..1, smooth
            float gust = Mathf.Pow(Mathf.PerlinNoise(t * 0.15f, _seed + 7f), 4f) * gustAmount;
            float k = Mathf.Clamp(1f + n * flickerAmount - gust, 0.2f, 1.6f);

            if (targetLight != null) targetLight.intensity = _baseIntensity * k;

            if (glowRenderer != null)
            {
                glowRenderer.GetPropertyBlock(_mpb);
                _mpb.SetColor(EmissionId, glowColor * k);
                glowRenderer.SetPropertyBlock(_mpb);
            }
        }
    }
}
