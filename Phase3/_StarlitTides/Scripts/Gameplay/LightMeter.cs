// LightMeter: the boat's life, a light that slowly fades.
//
// Value goes from 1 (full glow) to 0 (darkness, game over).
// - It drains a little every second (the rate comes from DifficultyManager).
// - Each orb gives some light back: Add().
// - Each crash takes a big chunk: Damage().
//
// Extra Feature 1 (Dynamic Light Meter): the meter isn't just a number on screen.
// It controls the boat's point light (range and brightness), the paper's glow
// and the plankton glow under the boat. When the light gets low you really
// can see less of the sea. Below 25% it flickers as a warning.

using System;
using UnityEngine;

namespace StarlitTides
{
    public class LightMeter : MonoBehaviour
    {
        public event Action<float> OnChanged;    // sends the new value (0..1)
        public event Action OnEmpty;
        public event Action<float> OnDamaged;    // sends the amount lost
        public event Action<float> OnRestored;   // sends the amount gained

        [Header("Meter")]
        [Range(0f, 1f)] public float startValue = 1f;
        [Tooltip("Drain rate if there is no DifficultyManager in the scene.")]
        public float fallbackDrainPerSecond = 0.04f;
        public float maxValue = 1f;

        [Header("What the meter controls (x = when empty, y = when full)")]
        public Light boatLight;
        public Vector2 lightRange = new Vector2(3f, 14f);
        public Vector2 lightIntensity = new Vector2(0.4f, 3.5f);
        public Renderer glowRenderer;
        [ColorUsage(false, true)] public Color glowColor = new Color(3.2f, 2.3f, 1.2f);
        public Vector2 glowIntensity = new Vector2(0.08f, 0.45f);
        public WaterDisturber waterGlow;
        public Vector2 waterGlowStrength = new Vector2(0.15f, 0.7f);
        [Tooltip("Below this the light flickers as a warning.")]
        [Range(0f, 1f)] public float lowWarning = 0.25f;

        public float Value { get; private set; }
        public bool IsEmpty { get { return Value <= 0f; } }
        // A smoothed copy of Value, nice for UI bars.
        public float SmoothValue { get; private set; }

        MaterialPropertyBlock propertyBlock;
        static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        bool emptyEventSent;
        float pickupFlash;   // quick brightening when an orb is collected

        void Awake()
        {
            propertyBlock = new MaterialPropertyBlock();
            Value = startValue;
            SmoothValue = startValue;
        }

        public void ResetMeter()
        {
            Value = startValue;
            SmoothValue = startValue;
            emptyEventSent = false;
            if (OnChanged != null) OnChanged(Value);
        }

        void Update()
        {
            GameManager game = GameManager.Instance;
            bool playing = game == null || game.IsPlaying;
            bool inGracePeriod = game != null && game.InGracePeriod;

            if (playing && !inGracePeriod)
            {
                float drain = fallbackDrainPerSecond;
                if (DifficultyManager.Instance != null) drain = DifficultyManager.Instance.DrainPerSecond;
                SetValue(Value - drain * Time.deltaTime);
            }

            SmoothValue = Mathf.MoveTowards(SmoothValue, Value, Time.unscaledDeltaTime * 1.5f);
            pickupFlash = Mathf.MoveTowards(pickupFlash, 0f, Time.deltaTime * 3f);
            UpdateVisuals();
        }

        public void Add(float amount)
        {
            if (emptyEventSent) return;   // too late, the light is already gone
            SetValue(Value + amount);
            pickupFlash = 1f;
            if (OnRestored != null) OnRestored(amount);
        }

        public void Damage(float amount)
        {
            SetValue(Value - amount);
            if (OnDamaged != null) OnDamaged(amount);
        }

        void SetValue(float newValue)
        {
            float oldValue = Value;
            Value = Mathf.Clamp(newValue, 0f, maxValue);

            if (Value != oldValue && OnChanged != null) OnChanged(Value);

            if (Value <= 0f && !emptyEventSent)
            {
                emptyEventSent = true;
                if (OnEmpty != null) OnEmpty();
            }
        }

        void UpdateVisuals()
        {
            float v = Mathf.Clamp01(Value);

            // Ease-out curve: the light stays strong for a while, then drops fast near the end.
            float strength = 1f - (1f - v) * (1f - v);

            // Flicker when the light is low. The lower it is, the faster and deeper the flicker.
            float flicker = 1f;
            if (v > 0f && v < lowWarning)
            {
                float danger = 1f - v / lowWarning;
                float wave = Mathf.Abs(Mathf.Sin(Time.time * (6f + danger * 10f)));
                flicker = 1f - danger * 0.45f * wave;
            }

            float boost = 1f + pickupFlash * 0.6f;

            if (boatLight != null)
            {
                boatLight.range = Mathf.Lerp(lightRange.x, lightRange.y, strength) * (1f + pickupFlash * 0.25f);
                boatLight.intensity = Mathf.Lerp(lightIntensity.x, lightIntensity.y, strength) * flicker * boost;
                boatLight.enabled = Value > 0f || SmoothValue > 0.01f;
            }

            if (glowRenderer != null)
            {
                // A property block changes the glow on this boat only, not on the shared material.
                Color emission = glowColor * Mathf.Lerp(glowIntensity.x, glowIntensity.y, strength) * flicker * boost;
                glowRenderer.GetPropertyBlock(propertyBlock);
                propertyBlock.SetColor(EmissionColorId, emission);
                glowRenderer.SetPropertyBlock(propertyBlock);
            }

            if (waterGlow != null)
                waterGlow.strength = Mathf.Lerp(waterGlowStrength.x, waterGlowStrength.y, strength) * boost;
        }
    }
}
