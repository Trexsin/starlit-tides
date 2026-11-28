// EXTRA FEATURE 2 - Combo System
//
// Collect orbs quickly one after another to build a combo.
// - Each orb within "comboWindow" seconds of the last one adds 1 to the combo.
// - Every 5 in a row raises the score multiplier by 0.5 (x1, x1.5, x2 ... up to x6).
// - The boat's glowing wake gets longer and brighter as the combo grows.
// - Waiting too long, or crashing, ends the combo.

using System;
using UnityEngine;

namespace StarlitTides
{
    public class ComboSystem : MonoBehaviour
    {
        public static ComboSystem Instance { get; private set; }

        // Sends (combo count, multiplier).
        public static event Action<int, float> OnComboChanged;
        // Sends the combo count that was lost.
        public static event Action<int> OnComboEnded;

        [Tooltip("Seconds you have to grab the next orb before the combo ends.")]
        public float comboWindow = 2.6f;
        public int orbsPerStep = 5;
        public float multiplierPerStep = 0.5f;
        public float maxMultiplier = 6f;

        [Header("Light trail")]
        public BoatWake boatWake;
        public WaterDisturber boatGlow;
        [Tooltip("How much longer/brighter the wake gets per orb in the combo.")]
        public float trailGrowthPerOrb = 0.06f;
        public float maxTrailBoost = 3f;

        public int Combo { get; private set; }
        public float Multiplier { get; private set; }
        // 1 right after an orb, 0 when the combo is about to end (for a UI timer bar).
        public float TimeLeft01 { get { return Combo > 0 ? Mathf.Clamp01(timer / comboWindow) : 0f; } }

        float timer;
        float baseTrailLife = -1f;

        void Awake()
        {
            Instance = this;
            Multiplier = 1f;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnEnable()
        {
            Orb.OnCollected += HandleOrb;
            BoatController.OnHit += HandleHit;
            GameManager.OnRunStarted += ResetCombo;
        }

        void OnDisable()
        {
            Orb.OnCollected -= HandleOrb;
            BoatController.OnHit -= HandleHit;
            GameManager.OnRunStarted -= ResetCombo;
        }

        void Update()
        {
            if (Combo > 0)
            {
                timer -= Time.deltaTime;
                if (timer <= 0f) EndCombo();
            }
            UpdateTrail();
        }

        void HandleOrb(Orb orb)
        {
            Combo++;
            timer = comboWindow;

            int steps = Combo / orbsPerStep;
            Multiplier = Mathf.Min(1f + steps * multiplierPerStep, maxMultiplier);

            if (ScoreManager.Instance != null)
            {
                ScoreManager.Instance.comboMultiplier = Multiplier;
                ScoreManager.Instance.BestCombo = Mathf.Max(ScoreManager.Instance.BestCombo, Combo);
            }
            if (OnComboChanged != null) OnComboChanged(Combo, Multiplier);
        }

        void HandleHit(Obstacle obstacle)
        {
            EndCombo();
        }

        void EndCombo()
        {
            if (Combo == 0) return;
            int lost = Combo;
            ResetCombo();
            if (OnComboEnded != null) OnComboEnded(lost);
        }

        void ResetCombo()
        {
            Combo = 0;
            timer = 0f;
            Multiplier = 1f;
            if (ScoreManager.Instance != null) ScoreManager.Instance.comboMultiplier = 1f;
            if (OnComboChanged != null) OnComboChanged(0, 1f);
        }

        // The wake and water glow grow with the combo.
        void UpdateTrail()
        {
            float target = Mathf.Min(1f + Combo * trailGrowthPerOrb, maxTrailBoost);

            if (boatWake != null)
                boatWake.comboBoost = Mathf.MoveTowards(boatWake.comboBoost, target, Time.deltaTime * 2f);

            if (boatGlow != null)
            {
                if (baseTrailLife < 0f) baseTrailLife = boatGlow.trailLife;
                float boost = boatWake != null ? boatWake.comboBoost : target;
                boatGlow.trailLife = baseTrailLife * boost;
                boatGlow.strengthMultiplier = Mathf.Lerp(1f, 1.8f, (boost - 1f) / (maxTrailBoost - 1f));
            }
        }
    }
}
