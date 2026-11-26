// =============================================================================
//  LeaderboardClient - talks to the Starlit Tides leaderboard on Vercel
// -----------------------------------------------------------------------------
//  The game never touches the database directly (that would put the database
//  password inside the .exe). Instead it calls a tiny web API:
//
//      Unity  --HTTPS-->  Vercel function (/api/scores)  -->  Neon Postgres
//
//  Usage (e.g. from the Game Over screen, Bonus 11):
//      LeaderboardClient.Instance.SubmitScore("Mira", (ok, rank, error) => { ... });
//      LeaderboardClient.Instance.FetchTop(10, (ok, entries, error) => { ... });
//
//  Everything is asynchronous (coroutines) and fails gracefully: with no
//  internet the game keeps working and only the online board is skipped.
// =============================================================================
using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace StarlitTides
{
    public class LeaderboardClient : MonoBehaviour
    {
        public static LeaderboardClient Instance { get; private set; }

        [Tooltip("Your Vercel deployment, e.g. https://starlit-tides.vercel.app (no trailing slash).")]
        public string baseUrl = "https://starlit-tides.vercel.app";
        public int timeoutSeconds = 8;
        public string gameVersion = "1.0";

        [Serializable]
        public class Entry
        {
            public int rank;
            public string name;
            public int score;
            public int orbs;
            public int distance;
            public string created_at;
        }

        [Serializable] private class EntryList { public Entry[] scores; }
        [Serializable] private class SubmitBody { public string name; public int score; public int orbs; public int distance; public int duration; public string version; }
        [Serializable] private class SubmitReply { public long id; public int rank; public int score; public string error; }
        [Serializable] private class ErrorReply { public string error; }

        public bool IsConfigured => !string.IsNullOrEmpty(baseUrl) && !baseUrl.Contains("YOUR-PROJECT");

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        /// <summary>Submits the current run from ScoreManager. Callback: (success, rank, errorMessage).</summary>
        public void SubmitScore(string playerName, Action<bool, int, string> onDone)
        {
            ScoreManager sm = ScoreManager.Instance;
            GameManager gm = GameManager.Instance;
            if (sm == null) { onDone?.Invoke(false, 0, "No score to submit."); return; }
            SubmitScore(playerName, sm.Score, sm.OrbsCollected, Mathf.FloorToInt(sm.Distance),
                        Mathf.Max(1, Mathf.CeilToInt(gm != null ? gm.RunTime : 1f)), onDone);
        }

        public void SubmitScore(string playerName, int score, int orbs, int distance, int durationSeconds, Action<bool, int, string> onDone)
        {
            if (!IsConfigured) { onDone?.Invoke(false, 0, "Leaderboard URL not set."); return; }
            var body = new SubmitBody
            {
                name = playerName, score = score, orbs = orbs, distance = distance,
                duration = durationSeconds, version = gameVersion
            };
            StartCoroutine(PostRoutine(JsonUtility.ToJson(body), onDone));
        }

        /// <summary>Downloads the top scores. Callback: (success, entries, errorMessage).</summary>
        public void FetchTop(int limit, Action<bool, List<Entry>, string> onDone)
        {
            if (!IsConfigured) { onDone?.Invoke(false, new List<Entry>(), "Leaderboard URL not set."); return; }
            StartCoroutine(GetRoutine(Mathf.Clamp(limit, 1, 50), onDone));
        }

        private IEnumerator GetRoutine(int limit, Action<bool, List<Entry>, string> onDone)
        {
            using (UnityWebRequest req = UnityWebRequest.Get($"{baseUrl}/api/scores?limit={limit}"))
            {
                req.timeout = timeoutSeconds;
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    onDone?.Invoke(false, new List<Entry>(), ReadError(req));
                    yield break;
                }
                EntryList list = JsonUtility.FromJson<EntryList>(req.downloadHandler.text);
                onDone?.Invoke(true, new List<Entry>(list?.scores ?? Array.Empty<Entry>()), null);
            }
        }

        private IEnumerator PostRoutine(string json, Action<bool, int, string> onDone)
        {
            using (var req = new UnityWebRequest($"{baseUrl}/api/scores", UnityWebRequest.kHttpVerbPOST))
            {
                req.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                req.downloadHandler = new DownloadHandlerBuffer();
                req.SetRequestHeader("Content-Type", "application/json");
                req.timeout = timeoutSeconds;
                yield return req.SendWebRequest();

                if (req.result != UnityWebRequest.Result.Success)
                {
                    onDone?.Invoke(false, 0, ReadError(req));
                    yield break;
                }
                SubmitReply reply = JsonUtility.FromJson<SubmitReply>(req.downloadHandler.text);
                onDone?.Invoke(true, reply != null ? reply.rank : 0, null);
            }
        }

        // Prefer the API's own error message ("Name must be 1-16 letters...").
        private static string ReadError(UnityWebRequest req)
        {
            string text = req.downloadHandler != null ? req.downloadHandler.text : null;
            if (!string.IsNullOrEmpty(text))
            {
                try
                {
                    ErrorReply e = JsonUtility.FromJson<ErrorReply>(text);
                    if (e != null && !string.IsNullOrEmpty(e.error)) return e.error;
                }
                catch (ArgumentException) { /* not JSON */ }
            }
            return req.result == UnityWebRequest.Result.ConnectionError ? "No connection to the leaderboard." : req.error;
        }
    }
}
