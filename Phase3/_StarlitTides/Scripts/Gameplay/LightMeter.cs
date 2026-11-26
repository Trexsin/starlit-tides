// =============================================================================
//  LightMeter - the boat's life: a light that slowly fades
// -----------------------------------------------------------------------------
//  Value goes from 1 (full glow) to 0 (darkness = game over).
//    * drains every second (rate from DifficultyManager)
//    * each plankton orb restores some light          -> Add()
//    * hitting an obstacle drains a big chunk         -> Damage()
//
//  EXTRA FEATURE 1 - Dynamic Light Meter (groundwork):
//  the meter is not just a UI number. It physically drives
//    - the boat's point light RANGE and INTENSITY (how far you can see)
//    - the paper boat's emissive glow
//    - the plankton glow around the boat on the water (WaterDisturber)
//  so a dying light visibly shrinks the world around you.  Phase 4 adds the
//  darkness closing in on the screen.
// =============================================================================
using System;
using UnityEngine;

namespace StarlitTides
{
    public class LightMeter : MonoBehaviour
    {
        public event Action<float> OnChanged;   // new value 0..1
        public event Action OnEmpty;
        public event Action<float> OnDamaged;   // amount
        public event Action<float> OnRestored;  // amount

        [Header("Meter")]
        [Range(0f, 1f)] public float startValue = 1f;
        [Tooltip("Used when there is no DifficultyManager in the scene.")]
        public float fallbackDrainPerSecond = 0.04f;
        [Tooltip("Values above 1 are allowed briefly as 'overcharge' (capped here).")]
        public float maxValue = 1f;

        [Header("Visuals driven by the meter")]
        public Light boatLight;
        public Vector2 lightRange = new Vector2(3f, 14f);       // at empty -> at full
        public Vector2 lightIntensity = new Vector2(0.4f, 3.5f);
        public Renderer glowRenderer;
        [ColorUsage(false, true)] public Color glowColor = new Color(3.2f, 2.3f, 1.2f);
        public Vector2 glowIntensity = new Vector2(0.08f, 0.45f);
        public WaterDisturber waterGlow;
        public Vector2 waterGlowStrength = new Vector2(0.15f, 0.7f);
        [Tooltip("Below this value the light flickers as a warning.")]
        [Range(0f, 1f)] public float lowWarning = 0.25f;

        public float Value { get; private set; }
        public bool IsEmpty => Value <= 0f;
        /// <summary>Lerped display value (smooth for UI bars).</summary>
        public float SmoothValue { get; private set; }

        private MaterialPropertyBlock _mpb;
        private static readonly int EmissionId = Shader.PropertyToID("_EmissionColor");
        private bool _emptyFired;
        private float _flash;   // brief brightening when an orb is collected

        private void Awake()
        {
            _mpb = new MaterialPropertyBlock();
            Value = SmoothValue = startValue;
        }

        public void ResetMeter()
        {
            Value = SmoothValue = startValue;
            _emptyFired = false;
            OnChanged?.Invoke(Value);
        }

        private void Update()
        {
            GameManager gm = GameManager.Instance;
            bool playing = gm == null || gm.IsPlaying;

            if (playing && !(gm != null && gm.InGracePeriod))
            {
                float drain = DifficultyManager.Instance != null ? DifficultyManager.Instance.DrainPerSecond : fallbackDrainPerSecond;
                SetValue(Value - drain * Time.deltaTime);
            }

            SmoothValue = Mathf.MoveTowards(SmoothValue, Value, Time.unscaledDeltaTime * 1.5f);
            _flash = Mathf.MoveTowards(_flash, 0f, Time.deltaTime * 3f);
            ApplyVisuals();
        }

        public void Add(float amount)
        {
            if (IsEmpty && _emptyFired) return;   // too late, the light is gone
            SetValue(Value + amount);
            _flash = 1f;
            OnRestored?.Invoke(amount);
        }

        public void Damage(float amount)
        {
            SetValue(Value - amount);
            OnDamaged?.Invoke(amount);
        }

        private void SetValue(float v)
        {
            float old = Value;
            Value = Mathf.Clamp(v, 0f, maxValue);
            if (!Mathf.Approximately(old, Value)) OnChanged?.Invoke(Value);
            if (Value <= 0f && !_emptyFired)
            {
                _emptyFired = true;
                OnEmpty?.Invoke();
            }
        }

        private void ApplyVisuals()
        {
            // ease-out curve: the light stays strong for a while, then collapses quickly at the end
            float v = Mathf.Clamp01(Value);
            float shaped = 1f - (1f - v) * (1f - v);

            // low-light warning flicker
            float flicker = 1f;
            if (v < lowWarning && v > 0f)
            {
                float danger = 1f - v / lowWarning;
                flicker = 1f - danger * 0.45f * Mathf.Abs(Mathf.Sin(Time.time * (6f + danger * 10f)));
            }

            float boost = 1f + _flash * 0.6f;

            if (boatLight != null)
            {
                boatLight.range = Mathf.Lerp(lightRange.x, lightRange.y, shaped) * (1f + _flash * 0.25f);
                boatLight.intensity = Mathf.Lerp(lightIntensity.x, lightIntensity.y, shaped) * flicker * boost;
                boatLight.enabled = Value > 0f || SmoothValue > 0.01f;
            }

            if (glowRenderer != null)
            {
                glowRenderer.GetPropertyBlock(_mpb);
                _mpb.SetColor(EmissionId, glowColor * Mathf.Lerp(glowIntensity.x, glowIntensity.y, shaped) * flicker * boost);
                glowRenderer.SetPropertyBlock(_mpb);
            }

            if (waterGlow != null)
                waterGlow.strength = Mathf.Lerp(waterGlowStrength.x, waterGlowStrength.y, shaped) * boost;
        }
    }
}
