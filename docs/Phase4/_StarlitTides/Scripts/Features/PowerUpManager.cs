// EXTRA FEATURE 3 - Golden Shooting Star power-up ("Starburst")
//
// Every 40-70 seconds a golden shooting star falls toward the sea ahead of
// you. Its reflection then drifts down the lane. Catch it and Starburst starts:
// - the whole sea lights up for 5 seconds
// - every point you earn is doubled
// Plain white shooting stars also cross the sky now and then, just for beauty.

using System;
using UnityEngine;

namespace StarlitTides
{
    public class PowerUpManager : MonoBehaviour
    {
        public static PowerUpManager Instance { get; private set; }

        public static event Action OnStarburstStarted;
        public static event Action OnStarburstEnded;

        [Header("Prefabs")]
        public GameObject shootingStarPrefab;
        public GameObject starPickupPrefab;
        public GameObject starburstBurstVfx;

        [Header("Golden star")]
        public Vector2 goldenStarInterval = new Vector2(40f, 70f);
        [Tooltip("Seconds of play before the first golden star.")]
        public float firstGoldenStar = 25f;
        public float pickupSpawnZ = 110f;

        [Header("White shooting stars (decoration)")]
        public Vector2 whiteStarInterval = new Vector2(6f, 14f);

        [Header("Starburst")]
        public float starburstDuration = 5f;
        public float scoreMultiplier = 2f;

        public bool StarburstActive { get { return starburstTimer > 0f; } }
        // 1 at the start of Starburst, 0 when it ends (for a UI timer).
        public float StarburstTimeLeft01 { get { return Mathf.Clamp01(starburstTimer / starburstDuration); } }

        float goldenTimer;
        float whiteTimer;
        float starburstTimer;

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
            GameManager.OnRunStarted += HandleRunStarted;
        }

        void OnDisable()
        {
            GameManager.OnRunStarted -= HandleRunStarted;
        }

        void Start()
        {
            PoolManager.Prewarm(shootingStarPrefab, 3);
            whiteTimer = UnityEngine.Random.Range(whiteStarInterval.x, whiteStarInterval.y);
            goldenTimer = firstGoldenStar;
        }

        void HandleRunStarted()
        {
            goldenTimer = firstGoldenStar;
            starburstTimer = 0f;
        }

        void Update()
        {
            // White stars fall in every state, even in menus. They're just pretty.
            whiteTimer -= Time.deltaTime;
            if (whiteTimer <= 0f)
            {
                whiteTimer = UnityEngine.Random.Range(whiteStarInterval.x, whiteStarInterval.y);
                LaunchStar(new Color(0.9f, 0.95f, 1f), false);
            }

            bool playing = GameManager.Instance == null || GameManager.Instance.IsPlaying;
            if (playing)
            {
                goldenTimer -= Time.deltaTime;
                if (goldenTimer <= 0f)
                {
                    goldenTimer = UnityEngine.Random.Range(goldenStarInterval.x, goldenStarInterval.y);
                    LaunchStar(new Color(1f, 0.8f, 0.3f), true);
                }
            }

            UpdateStarburst();
        }

        void LaunchStar(Color color, bool golden)
        {
            if (shootingStarPrefab == null) return;

            // Golden stars fall toward the far end of the lane, so you can see where the reflection will come from.
            // White stars cross a random part of the sky.
            Vector3 from;
            Vector3 to;
            float seconds;
            if (golden)
            {
                from = new Vector3(UnityEngine.Random.Range(-120f, 120f), 160f, 520f);
                to = new Vector3(UnityEngine.Random.Range(-6f, 6f), 8f, pickupSpawnZ + 40f);
                seconds = 1.6f;
            }
            else
            {
                float side = UnityEngine.Random.value < 0.5f ? -1f : 1f;
                from = new Vector3(side * UnityEngine.Random.Range(150f, 400f), UnityEngine.Random.Range(180f, 300f), UnityEngine.Random.Range(400f, 700f));
                to = from + new Vector3(-side * UnityEngine.Random.Range(150f, 300f), -UnityEngine.Random.Range(60f, 120f), UnityEngine.Random.Range(-80f, 80f));
                seconds = UnityEngine.Random.Range(0.7f, 1.2f);
            }

            GameObject obj = PoolManager.Spawn(shootingStarPrefab, from, Quaternion.identity);
            ShootingStar star = obj.GetComponent<ShootingStar>();
            if (golden) star.Launch(from, to, seconds, color, SpawnPickup);
            else star.Launch(from, to, seconds, color, null);
        }

        // The golden star has "landed": its reflection appears at the end of the lane.
        void SpawnPickup()
        {
            if (starPickupPrefab == null) return;
            if (GameManager.Instance != null && !GameManager.Instance.IsPlaying) return;

            float x = UnityEngine.Random.Range(-5f, 5f);
            PoolManager.Spawn(starPickupPrefab, new Vector3(x, 0f, pickupSpawnZ), Quaternion.identity);
        }

        public void StartStarburst(Vector3 position)
        {
            starburstTimer = starburstDuration;
            if (ScoreManager.Instance != null) ScoreManager.Instance.powerUpMultiplier = scoreMultiplier;
            if (VfxLibrary.Instance != null) VfxLibrary.Instance.Play(starburstBurstVfx, position);
            if (HitFeedback.Instance != null) HitFeedback.Instance.Flash(0.25f, 0.4f);
            if (OnStarburstStarted != null) OnStarburstStarted();
        }

        void UpdateStarburst()
        {
            OceanController ocean = OceanController.Instance;
            bool wasActive = StarburstActive;
            if (starburstTimer > 0f) starburstTimer -= Time.deltaTime;

            // The sea glow rises quickly and fades out gently at the end.
            float target = StarburstActive ? 1f : 0f;
            float speed = StarburstActive ? 3f : 1f;
            if (ocean != null) ocean.seaGlowBoost = Mathf.MoveTowards(ocean.seaGlowBoost, target, Time.deltaTime * speed);

            if (wasActive && !StarburstActive)
            {
                if (ScoreManager.Instance != null) ScoreManager.Instance.powerUpMultiplier = 1f;
                if (OnStarburstEnded != null) OnStarburstEnded();
            }
        }
    }
}
