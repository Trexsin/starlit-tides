// ScoreManager: adds up the score.
//
//   score = distance points + orb points + bonus points
//   - distance: 1 point per metre the tide carries you
//   - orbs:     50 points each, times the combo multiplier (added in Phase 4)
//   - bonuses:  near misses and events (Phase 4)
// The Starburst power-up (Phase 4) doubles everything using powerUpMultiplier.

using System;
using UnityEngine;

namespace StarlitTides
{
    public class ScoreManager : MonoBehaviour
    {
        public static ScoreManager Instance { get; private set; }

        // Sends (new score, points just added).
        public static event Action<int, int> OnScoreChanged;
        // Sends the total number of orbs collected.
        public static event Action<int> OnOrbCollected;

        public int pointsPerOrb = 50;
        public float pointsPerMetre = 1f;

        [Header("Multipliers (set by Phase 4 features)")]
        public float comboMultiplier = 1f;
        public float powerUpMultiplier = 1f;

        public float Distance { get; private set; }
        public int OrbsCollected { get; private set; }
        public int Score { get { return Mathf.FloorToInt(score); } }
        public int BestCombo { get; set; }
        public int NearMisses { get; set; }
        public float TotalMultiplier { get { return comboMultiplier * powerUpMultiplier; } }

        // Kept as a float so fractions of a metre still count.
        float score;
        int lastReportedScore;

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnEnable()
        {
            GameManager.OnRunStarted += ResetScore;
        }

        void OnDisable()
        {
            GameManager.OnRunStarted -= ResetScore;
        }

        public void ResetScore()
        {
            score = 0f;
            Distance = 0f;
            OrbsCollected = 0;
            BestCombo = 0;
            NearMisses = 0;
            lastReportedScore = 0;
            comboMultiplier = 1f;
            powerUpMultiplier = 1f;
            if (OnScoreChanged != null) OnScoreChanged(0, 0);
        }

        void Update()
        {
            if (GameManager.Instance == null || !GameManager.Instance.IsPlaying) return;
            if (OceanController.Instance == null) return;

            // Distance travelled this frame = tide speed * time.
            float metres = OceanController.Instance.tideSpeed * Time.deltaTime;
            Distance += metres;
            score += metres * pointsPerMetre * powerUpMultiplier;

            // Only tell listeners when the whole-number score changes.
            if (Score != lastReportedScore) ReportScore();
        }

        public void AddOrb()
        {
            OrbsCollected++;
            AddPoints(pointsPerOrb * TotalMultiplier);
            if (OnOrbCollected != null) OnOrbCollected(OrbsCollected);
        }

        // Extra points from near misses and events. Starburst doubles these too.
        public void AddBonus(float points)
        {
            AddPoints(points * powerUpMultiplier);
        }

        void AddPoints(float points)
        {
            score += points;
            ReportScore();
        }

        void ReportScore()
        {
            int added = Score - lastReportedScore;
            lastReportedScore = Score;
            if (OnScoreChanged != null) OnScoreChanged(Score, added);
        }
    }
}
