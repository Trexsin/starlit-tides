// GameOverView: the end-of-run screen (Bonus 11 with the real UI).
// Shows the run's stats next to your bests, "NEW HIGH SCORE!", a name box to
// send your score to the global leaderboard, and the world's top 10.

using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StarlitTides
{
    public class GameOverView : MonoBehaviour
    {
        public TextMeshProUGUI newBestText;
        public TextMeshProUGUI statsText;
        public TMP_InputField nameInput;
        public Button submitButton;
        public TextMeshProUGUI statusText;
        public TextMeshProUGUI topScoresText;

        bool submitted;
        bool submitting;

        void Awake()
        {
            submitButton.onClick.AddListener(Submit);
        }

        void Update()
        {
            // While typing a name, keys like R and P must not restart or open photo mode.
            StarlitInput.TextFieldFocused = nameInput.isFocused;
            submitButton.interactable = !submitted && !submitting && nameInput.text.Trim().Length > 0;
        }

        void OnDisable()
        {
            StarlitInput.TextFieldFocused = false;
        }

        // Called by the UIManager when the screen opens.
        public void Show()
        {
            HighScoreManager scores = HighScoreManager.Instance;
            if (scores == null) return;
            HighScoreManager.RunStats run = scores.LastRun;

            newBestText.gameObject.SetActive(scores.LastRunWasBest);

            StringBuilder s = new StringBuilder();
            // <pos=..> lines the numbers up in a column.
            s.AppendLine("<color=#FFD27A>SCORE</color><pos=55%>" + run.score.ToString("N0") + "   <size=75%><color=#8FA3C7>best " + scores.BestScore.ToString("N0") + "</color></size>");
            s.AppendLine("<color=#7FE8FF>ORBS</color><pos=55%>" + run.orbs);
            s.AppendLine("<color=#7FE8FF>DISTANCE</color><pos=55%>" + run.distance.ToString("N0") + " m");
            s.AppendLine("<color=#7FE8FF>TIME</color><pos=55%>" + (run.seconds / 60) + ":" + (run.seconds % 60).ToString("00"));
            s.AppendLine("<color=#7FE8FF>BEST COMBO</color><pos=55%>" + run.bestCombo);
            s.AppendLine("<color=#7FE8FF>NEAR MISSES</color><pos=55%>" + run.nearMisses);
            statsText.text = s.ToString();

            submitted = false;
            submitting = false;
            nameInput.text = scores.PlayerName;
            statusText.text = "";
            topScoresText.text = "Loading...";
            RefreshTopScores();
        }

        void RefreshTopScores()
        {
            LeaderboardClient client = LeaderboardClient.Instance;
            if (client == null)
            {
                topScoresText.text = "(leaderboard not set up)";
                return;
            }
            client.FetchTop(10, ShowTopScores);
        }

        void ShowTopScores(bool ok, List<LeaderboardClient.Entry> entries, string error)
        {
            if (!ok)
            {
                topScoresText.text = "<color=#8FA3C7>" + error + "</color>";
                return;
            }
            if (entries.Count == 0)
            {
                topScoresText.text = "<color=#8FA3C7>Be the first on the board!</color>";
                return;
            }
            StringBuilder s = new StringBuilder();
            foreach (LeaderboardClient.Entry e in entries)
                s.AppendLine("<color=#8FA3C7>" + e.rank + ".</color>  " + e.name + "<pos=75%>" + e.score.ToString("N0"));
            topScoresText.text = s.ToString();
        }

        void Submit()
        {
            LeaderboardClient client = LeaderboardClient.Instance;
            if (client == null || submitted || submitting) return;

            string playerName = nameInput.text.Trim();
            if (HighScoreManager.Instance != null) HighScoreManager.Instance.PlayerName = playerName;

            submitting = true;
            statusText.text = "Sending...";
            client.SubmitScore(playerName, (ok, rank, error) =>
            {
                submitting = false;
                submitted = ok;
                statusText.text = ok ? "You're <color=#FFD27A>#" + rank + "</color> in the world!" : "<color=#FF8A8A>" + error + "</color>";
                if (ok) RefreshTopScores();
            });
        }
    }
}
