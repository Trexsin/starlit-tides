// BONUS FEATURE 11 - Persistent high score and run stats
//
// When a run ends we record its stats and compare them with the saved bests.
// Everything is stored with PlayerPrefs (Unity's small save system), so it
// survives closing the game. The Game Over screen reads LastRun and the Best values.

using System;
using UnityEngine;

namespace StarlitTides
{
    public class HighScoreManager : MonoBehaviour
    {
        public static HighScoreManager Instance { get; private set; }

        // Sent after a run is recorded. true = new high score.
        public static event Action<bool> OnRunRecorded;

        [Serializable]
        public struct RunStats
        {
            public int score;
            public int orbs;
            public int distance;
            public int seconds;
            public int bestCombo;
            public int nearMisses;
        }

        public RunStats LastRun { get; private set; }
        public bool LastRunWasBest { get; private set; }

        public int BestScore { get { return PlayerPrefs.GetInt("ST_BestScore", 0); } }
        public int BestDistance { get { return PlayerPrefs.GetInt("ST_BestDistance", 0); } }
        public int BestCombo { get { return PlayerPrefs.GetInt("ST_BestCombo", 0); } }
        public int TotalRuns { get { return PlayerPrefs.GetInt("ST_TotalRuns", 0); } }
        public int TotalOrbs { get { return PlayerPrefs.GetInt("ST_TotalOrbs", 0); } }

        // The name used for the online leaderboard (remembered between sessions).
        public string PlayerName
        {
            get { return PlayerPrefs.GetString("ST_PlayerName", ""); }
            set { PlayerPrefs.SetString("ST_PlayerName", value); PlayerPrefs.Save(); }
        }

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
            GameManager.OnGameOver += RecordRun;
        }

        void OnDisable()
        {
            GameManager.OnGameOver -= RecordRun;
        }

        void RecordRun()
        {
            ScoreManager score = ScoreManager.Instance;
            if (score == null) return;

            RunStats run = new RunStats();
            run.score = score.Score;
            run.orbs = score.OrbsCollected;
            run.distance = Mathf.FloorToInt(score.Distance);
            run.seconds = GameManager.Instance != null ? Mathf.CeilToInt(GameManager.Instance.RunTime) : 0;
            run.bestCombo = score.BestCombo;
            run.nearMisses = score.NearMisses;
            LastRun = run;

            LastRunWasBest = run.score > BestScore;
            if (LastRunWasBest) PlayerPrefs.SetInt("ST_BestScore", run.score);
            PlayerPrefs.SetInt("ST_BestDistance", Mathf.Max(BestDistance, run.distance));
            PlayerPrefs.SetInt("ST_BestCombo", Mathf.Max(BestCombo, run.bestCombo));
            PlayerPrefs.SetInt("ST_TotalRuns", TotalRuns + 1);
            PlayerPrefs.SetInt("ST_TotalOrbs", TotalOrbs + run.orbs);
            PlayerPrefs.Save();

            if (OnRunRecorded != null) OnRunRecorded(LastRunWasBest);
        }

        // Handy for testing: in the Inspector, click the ⋮ menu of this component > "Reset All High Scores".
        [ContextMenu("Reset All High Scores")]
        public void ResetAll()
        {
            foreach (string key in new[] { "ST_BestScore", "ST_BestDistance", "ST_BestCombo", "ST_TotalRuns", "ST_TotalOrbs" })
                PlayerPrefs.DeleteKey(key);
            PlayerPrefs.Save();
        }
    }
}
