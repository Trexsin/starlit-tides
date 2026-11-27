// BONUS FEATURE 7 - The Glowing Whale
//
// Every couple of minutes a giant bioluminescent whale leaps out of the sea:
//   1. Warning  - the sea ahead glows brighter and brighter, the camera rumbles
//   2. Breach   - the whale arcs out of the water with a huge glowing splash
//   3. Waves    - when it crashes back down, rows of glowing waves roll toward
//                 you. Each row has one gap. Find it, or dash through!
// The whale moves along a simple arc (a parabola). Its waves are Obstacles,
// so they use the same collisions, near misses and pooling as rocks.

using System;
using System.Collections;
using Unity.Cinemachine;
using UnityEngine;

namespace StarlitTides
{
    public class WhaleEvent : MonoBehaviour
    {
        public static WhaleEvent Instance { get; private set; }

        public static event Action OnWarning;
        public static event Action OnBreach;
        public static event Action OnSplashDown;

        [Header("References")]
        public Transform whale;
        public GameObject waveSegmentPrefab;
        public GameObject bigSplashVfx;
        public CinemachineImpulseSource rumble;

        [Header("Timing (seconds of play)")]
        public float firstWhaleAt = 55f;
        public Vector2 interval = new Vector2(80f, 110f);

        [Header("Breach")]
        public float warningTime = 2.5f;
        public float breachTime = 4.5f;
        public float breachHeight = 28f;

        [Header("Waves")]
        public int waveRows = 3;
        public float rowSpacing = 14f;
        public float segmentWidth = 4f;
        [Tooltip("Extra swell added to the ocean (1 = waves twice as big) right after the splash.")]
        public float swell = 1.2f;

        public bool IsActive { get; private set; }

        float nextWhaleTime;

        void Awake()
        {
            Instance = this;
            if (whale != null) whale.gameObject.SetActive(false);
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnEnable()
        {
            GameManager.OnRunStarted += ResetTimer;
        }

        void OnDisable()
        {
            GameManager.OnRunStarted -= ResetTimer;
        }

        void ResetTimer()
        {
            nextWhaleTime = firstWhaleAt;
        }

        void Update()
        {
            GameManager game = GameManager.Instance;
            if (game == null || !game.IsPlaying || IsActive) return;
            if (game.RunTime >= nextWhaleTime)
            {
                nextWhaleTime = game.RunTime + UnityEngine.Random.Range(interval.x, interval.y);
                StartCoroutine(WhaleRoutine());
            }
        }

        // Starts the event right now (handy for testing and demos).
        public void TriggerNow()
        {
            if (!IsActive) StartCoroutine(WhaleRoutine());
        }

        IEnumerator WhaleRoutine()
        {
            IsActive = true;
            OceanController ocean = OceanController.Instance;

            // The whale leaps sideways across the lane ahead of you, so you see its whole body.
            float side = UnityEngine.Random.value < 0.5f ? -1f : 1f;
            Vector3 start = new Vector3(side * 24f, -16f, 58f);
            Vector3 end = new Vector3(-side * 6f, -16f, 44f);
            Vector3 surface = Vector3.Lerp(start, end, 0.15f);
            surface.y = 0f;

            // ---- 1. Warning: the sea glows ahead, the camera rumbles ----
            if (OnWarning != null) OnWarning();
            float t = 0f;
            while (t < warningTime)
            {
                t += Time.deltaTime;
                if (ocean != null && Mathf.Repeat(t, 0.3f) < Time.deltaTime)
                    ocean.AddRipple(surface, 4f + t * 3f, 1.5f + t, 1.2f);
                if (rumble != null && Mathf.Repeat(t, 0.6f) < Time.deltaTime)
                    rumble.GenerateImpulseWithForce(0.15f + t * 0.1f);
                yield return null;
            }

            // ---- 2. Breach: follow a parabola out of the water and back ----
            if (OnBreach != null) OnBreach();
            whale.gameObject.SetActive(true);
            bool splashedOut = false;
            bool splashedIn = false;
            t = 0f;
            Vector3 previous = start;
            while (t < 1f)
            {
                t += Time.deltaTime / breachTime;
                Vector3 position = Vector3.Lerp(start, end, t);
                position.y = start.y + breachHeight * 4f * t * (1f - t);   // the arc (a parabola)
                whale.position = position;

                // Face the direction of travel, with a slow barrel roll.
                Vector3 velocity = position - previous;
                if (velocity.sqrMagnitude > 0.0001f)
                    whale.rotation = Quaternion.LookRotation(velocity.normalized, Vector3.up) * Quaternion.Euler(0f, 0f, t * 60f * side);
                previous = position;

                if (!splashedOut && position.y > -1f)
                {
                    splashedOut = true;
                    Splash(new Vector3(position.x, 0f, position.z), 1f);
                }
                if (splashedOut && !splashedIn && t > 0.6f && position.y < 0.5f)
                {
                    splashedIn = true;
                    Splash(new Vector3(position.x, 0f, position.z), 1.6f);
                    if (OnSplashDown != null) OnSplashDown();
                    SpawnWaves(position.z);
                    StartCoroutine(Swell());
                }
                yield return null;
            }

            whale.gameObject.SetActive(false);
            IsActive = false;
        }

        void Splash(Vector3 position, float size)
        {
            if (VfxLibrary.Instance != null) VfxLibrary.Instance.Play(bigSplashVfx, position);
            if (OceanController.Instance != null) OceanController.Instance.AddRipple(position, 10f * size, 3f, 2.5f);
            if (rumble != null) rumble.GenerateImpulseWithForce(0.6f * size);
        }

        // Rows of glowing waves across the lane, each with one random gap.
        void SpawnWaves(float startZ)
        {
            if (waveSegmentPrefab == null) return;
            float laneWidth = PlayArea.ObstacleHalfWidth * 2f;
            int segments = Mathf.RoundToInt(laneWidth / segmentWidth);

            // Each row's gap is close to the last one, so the boat can always
            // reach it in time even in the wide Phase 8 lane.
            int gap = UnityEngine.Random.Range(segments / 4, segments - segments / 4);
            for (int row = 0; row < waveRows; row++)
            {
                if (row > 0) gap = Mathf.Clamp(gap + UnityEngine.Random.Range(-3, 4), 0, segments - 1);
                float z = startZ - 4f - row * rowSpacing;
                for (int i = 0; i < segments; i++)
                {
                    if (i == gap) continue;
                    float x = -laneWidth * 0.5f + segmentWidth * (i + 0.5f);
                    PoolManager.Spawn(waveSegmentPrefab, new Vector3(x, 0f, z), Quaternion.identity);
                }
            }
        }

        // The whole sea swells up after the splash, then calms down again.
        IEnumerator Swell()
        {
            OceanController ocean = OceanController.Instance;
            if (ocean == null) yield break;

            float t = 0f;
            while (t < 1f)
            {
                t += Time.deltaTime / 7f;
                ocean.swell = swell * Mathf.Sin(t * Mathf.PI) * (1f - t * 0.5f);
                yield return null;
            }
            ocean.swell = 0f;
        }
    }
}
