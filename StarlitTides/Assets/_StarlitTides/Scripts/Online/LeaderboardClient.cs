// LeaderboardClient: sends scores to the online leaderboard and gets the top scores.
//
// The game never talks to the database directly. The database password would
// end up inside the .exe where anyone could find it. Instead it calls a small
// web API on Vercel, and only that API knows the password:
//
//   Unity  --HTTPS-->  Vercel (/api/scores)  -->  Neon Postgres database
//
// Usage (for example on the Game Over screen):
//   LeaderboardClient.Instance.SubmitScore("Mira", OnSubmitted);
//   LeaderboardClient.Instance.FetchTop(10, OnTopScores);
//
// Requests run in the background (coroutines). If there's no internet the
// game keeps working and only the online board is skipped.

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

        [Tooltip("The Vercel website address, without a / at the end.")]
        public string baseUrl = "https://starlit-tides.vercel.app";
        public int timeoutSeconds = 8;
        public string gameVersion = "1.0";

        // One row of the leaderboard. The names match the JSON from the API.
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

        // JsonUtility can't read a bare list, so the API wraps it: { "scores": [ ... ] }
        [Serializable] class EntryList { public Entry[] scores; }
        [Serializable] class SubmitBody { public string name; public int score; public int orbs; public int distance; public int duration; public string version; }
        [Serializable] class SubmitReply { public int rank; public int score; }
        [Serializable] class ErrorReply { public string error; }

        public bool IsConfigured { get { return !string.IsNullOrEmpty(baseUrl); } }

        void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        // Sends the run that just ended (reads it from ScoreManager).
        // onDone gets: success, rank, error message.
        public void SubmitScore(string playerName, Action<bool, int, string> onDone)
        {
            ScoreManager scores = ScoreManager.Instance;
            if (scores == null)
            {
                if (onDone != null) onDone(false, 0, "No score to submit.");
                return;
            }

            float seconds = 1f;
            if (GameManager.Instance != null) seconds = GameManager.Instance.RunTime;

            SubmitScore(playerName, scores.Score, scores.OrbsCollected,
                        Mathf.FloorToInt(scores.Distance), Mathf.Max(1, Mathf.CeilToInt(seconds)), onDone);
        }

        public void SubmitScore(string playerName, int score, int orbs, int distance, int durationSeconds, Action<bool, int, string> onDone)
        {
            if (!IsConfigured)
            {
                if (onDone != null) onDone(false, 0, "Leaderboard address not set.");
                return;
            }

            SubmitBody body = new SubmitBody();
            body.name = playerName;
            body.score = score;
            body.orbs = orbs;
            body.distance = distance;
            body.duration = durationSeconds;
            body.version = gameVersion;

            StartCoroutine(PostScore(JsonUtility.ToJson(body), onDone));
        }

        // Downloads the best scores. onDone gets: success, list of entries, error message.
        public void FetchTop(int limit, Action<bool, List<Entry>, string> onDone)
        {
            if (!IsConfigured)
            {
                if (onDone != null) onDone(false, new List<Entry>(), "Leaderboard address not set.");
                return;
            }
            StartCoroutine(GetTopScores(Mathf.Clamp(limit, 1, 50), onDone));
        }

        IEnumerator GetTopScores(int limit, Action<bool, List<Entry>, string> onDone)
        {
            using (UnityWebRequest request = UnityWebRequest.Get(baseUrl + "/api/scores?limit=" + limit))
            {
                request.timeout = timeoutSeconds;
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    if (onDone != null) onDone(false, new List<Entry>(), ReadError(request));
                    yield break;
                }

                EntryList list = JsonUtility.FromJson<EntryList>(request.downloadHandler.text);
                List<Entry> entries = new List<Entry>();
                if (list != null && list.scores != null) entries.AddRange(list.scores);
                if (onDone != null) onDone(true, entries, null);
            }
        }

        IEnumerator PostScore(string json, Action<bool, int, string> onDone)
        {
            using (UnityWebRequest request = new UnityWebRequest(baseUrl + "/api/scores", UnityWebRequest.kHttpVerbPOST))
            {
                request.uploadHandler = new UploadHandlerRaw(Encoding.UTF8.GetBytes(json));
                request.downloadHandler = new DownloadHandlerBuffer();
                request.SetRequestHeader("Content-Type", "application/json");
                request.timeout = timeoutSeconds;
                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    if (onDone != null) onDone(false, 0, ReadError(request));
                    yield break;
                }

                SubmitReply reply = JsonUtility.FromJson<SubmitReply>(request.downloadHandler.text);
                int rank = reply != null ? reply.rank : 0;
                if (onDone != null) onDone(true, rank, null);
            }
        }

        // Uses the API's own message when there is one (like "Name must be 1-16 letters...").
        static string ReadError(UnityWebRequest request)
        {
            string text = null;
            if (request.downloadHandler != null) text = request.downloadHandler.text;

            if (!string.IsNullOrEmpty(text))
            {
                try
                {
                    ErrorReply reply = JsonUtility.FromJson<ErrorReply>(text);
                    if (reply != null && !string.IsNullOrEmpty(reply.error)) return reply.error;
                }
                catch (ArgumentException)
                {
                    // The reply wasn't JSON. Fall through to the generic message.
                }
            }

            if (request.result == UnityWebRequest.Result.ConnectionError) return "No connection to the leaderboard.";
            return request.error;
        }
    }
}
