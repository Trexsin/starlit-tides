// =============================================================================
//  ScoreManager - distance survived + orbs collected, with multipliers
// -----------------------------------------------------------------------------
//  Score = distance points + orb points + bonus points
//    distance : 1 point per metre the tide carries you
//    orbs     : 50 points each x combo multiplier (Phase 4, Feature 2)
//    bonuses  : near misses etc. (Phase 4, Feature 5)
//  The Starburst power-up (Feature 3) doubles everything via powerUpMultiplier.
// =============================================================================
using System;
using UnityEngine;

namespace StarlitTides
{
    public class ScoreManager : MonoBehaviour
    {
        public static ScoreManager Instance { get; private set; }

        /// <summary>(newScore, pointsJustAdded)</summary>
        public static event Action<int, int> OnScoreChanged;
        public static event Action<int> OnOrbCollected;   // total orbs

        public int pointsPerOrb = 50;
        public float pointsPerMetre = 1f;

        [Header("Multipliers (set by Combo / Starburst in Phase 4)")]
        public float comboMultiplier = 1f;
        public float powerUpMultiplier = 1f;

        public float Distance { get; private set; }
        public int OrbsCollected { get; private set; }
        public int Score => Mathf.FloorToInt(_score);
        public int BestCombo { get; set; }
        public int NearMisses { get; set; }
        public float TotalMultiplier => comboMultiplier * powerUpMultiplier;

        private float _score;
        private int _lastReported;

        private void Awake() { Instance = this; }
        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void OnEnable() { GameManager.OnRunStarted += ResetScore; }
        private void OnDisable() { GameManager.OnRunStarted -= ResetScore; }

        public void ResetScore()
        {
            _score = 0f; Distance = 0f; OrbsCollected = 0; BestCombo = 0; NearMisses = 0; _lastReported = 0;
            comboMultiplier = 1f; powerUpMultiplier = 1f;
            OnScoreChanged?.Invoke(0, 0);
        }

        private void Update()
        {
            if (GameManager.Instance == null || !GameManager.Instance.IsPlaying) return;
            OceanController ocean = OceanController.Instance;
            if (ocean == null) return;

            float metres = ocean.tideSpeed * Time.deltaTime;
            Distance += metres;
            _score += metres * pointsPerMetre * powerUpMultiplier;
            if (Score != _lastReported) { OnScoreChanged?.Invoke(Score, Score - _lastReported); _lastReported = Score; }
        }

        public void AddOrb()
        {
            OrbsCollected++;
            AddPoints(pointsPerOrb * TotalMultiplier);
            OnOrbCollected?.Invoke(OrbsCollected);
        }

        /// <summary>Extra points (near miss, events). Starburst doubles these too.</summary>
        public void AddBonus(float points)
        {
            AddPoints(points * powerUpMultiplier);
        }

        private void AddPoints(float points)
        {
            _score += points;
            OnScoreChanged?.Invoke(Score, Score - _lastReported);
            _lastReported = Score;
        }
    }
}
