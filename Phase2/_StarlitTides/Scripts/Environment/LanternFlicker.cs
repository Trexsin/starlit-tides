// LanternFlicker: makes a lantern flicker like a candle flame.
//
// Perlin noise gives smooth random values, so the flame "breathes" instead of
// strobing. Every lantern starts at a random point in the noise, so they
// never flicker together. Now and then a "gust" makes a deeper dip.

using UnityEngine;

namespace StarlitTides
{
    public class LanternFlicker : MonoBehaviour
    {
        public Light targetLight;
        [Tooltip("Optional: a renderer whose glow flickers with the light.")]
        public Renderer glowRenderer;
        [ColorUsage(false, true)] public Color glowColor = new Color(4f, 2.6f, 1.1f);

        [Range(0f, 1f)] public float flickerAmount = 0.25f;
        public float flickerSpeed = 3f;
        [Tooltip("Occasional deeper dips, like wind catching the flame.")]
        [Range(0f, 1f)] public float gustAmount = 0.2f;

        float baseIntensity;
        float seed;
        MaterialPropertyBlock propertyBlock;
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        void Awake()
        {
            if (targetLight == null) targetLight = GetComponentInChildren<Light>();
            if (targetLight != null) baseIntensity = targetLight.intensity;
            seed = Random.value * 100f;
            propertyBlock = new MaterialPropertyBlock();
        }

        void Update()
        {
            float t = Time.time * flickerSpeed + seed;

            float noise = Mathf.PerlinNoise(t, seed) * 2f - 1f;                       // -1..1
            float gust = Mathf.Pow(Mathf.PerlinNoise(t * 0.15f, seed + 7f), 4f) * gustAmount;
            float brightness = Mathf.Clamp(1f + noise * flickerAmount - gust, 0.2f, 1.6f);

            if (targetLight != null) targetLight.intensity = baseIntensity * brightness;

            if (glowRenderer != null)
            {
                glowRenderer.GetPropertyBlock(propertyBlock);
                propertyBlock.SetColor(EmissionColorId, glowColor * brightness);
                glowRenderer.SetPropertyBlock(propertyBlock);
            }
        }
    }
}
