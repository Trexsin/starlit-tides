// EXTRA FEATURE 1 - Dynamic Light Meter (vision)
//
// Phase 3 already made the boat's light shrink as the meter drains.
// This adds the second half: as your light fades, darkness closes in on the
// screen. A post-processing Volume with a heavy dark vignette and lower
// exposure fades in below 50% light. Near empty it "breathes" like a dying
// flame. Each orb you collect pushes the darkness back.

using UnityEngine;
using UnityEngine.Rendering;

namespace StarlitTides
{
    public class LightVision : MonoBehaviour
    {
        public LightMeter lightMeter;
        [Tooltip("Volume with the darkness effect. Its weight is set from the light meter.")]
        public Volume darknessVolume;

        [Tooltip("Darkness starts creeping in below this light value.")]
        [Range(0f, 1f)] public float startBelow = 0.5f;
        [Tooltip("How strong the darkness is when the light is empty.")]
        [Range(0f, 1f)] public float maxWeight = 1f;

        float currentWeight;

        void Update()
        {
            if (lightMeter == null || darknessVolume == null) return;

            // 0 when the light is above startBelow, 1 when it's empty.
            float light = lightMeter.SmoothValue;
            float darkness = 1f - Mathf.Clamp01(light / startBelow);
            darkness = darkness * darkness;   // gentle at first, strong near the end

            // Near empty, the darkness pulses slowly like a dying flame.
            if (darkness > 0.6f)
                darkness *= 0.9f + 0.1f * Mathf.Sin(Time.unscaledTime * 5f);

            // Ease toward the target so orb pickups push the dark back smoothly.
            float target = darkness * maxWeight;
            currentWeight = Mathf.MoveTowards(currentWeight, target, Time.unscaledDeltaTime * 1.5f);
            darknessVolume.weight = currentWeight;
        }
    }
}
