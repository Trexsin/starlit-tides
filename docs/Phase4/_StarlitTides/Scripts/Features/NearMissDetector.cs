// EXTRA FEATURE 5 - Near-Miss Bonus
//
// Slip past an obstacle very closely without touching it and you get:
// - bonus points (times your combo and Starburst multipliers)
// - a short slow-motion moment
// - a burst of sparkles around the boat
// Each obstacle remembers how close the boat came (Obstacle.ClosestApproach).
// Once it has passed behind the boat we check that distance.

using System;
using System.Collections;
using UnityEngine;

namespace StarlitTides
{
    public class NearMissDetector : MonoBehaviour
    {
        public static event Action<int> OnNearMiss;   // sends the points awarded

        [Tooltip("Pass closer than this (metres, from the obstacle's edge) for a near miss.")]
        public float nearMissDistance = 1.6f;
        [Tooltip("Closer than this means the boat went THROUGH it (e.g. while dashing), which doesn't count.")]
        public float minimumDistance = 0.5f;
        public int bonusPoints = 150;
        public GameObject sparkleVfx;

        [Header("Slow motion")]
        [Range(0.05f, 1f)] public float slowTimeScale = 0.35f;
        [Tooltip("Real seconds the slow motion lasts.")]
        public float slowDuration = 0.6f;
        [Tooltip("Real seconds before slow motion can happen again.")]
        public float slowCooldown = 2f;

        float lastSlowTime = -10f;
        Coroutine slowRoutine;

        void Update()
        {
            GameManager game = GameManager.Instance;
            BoatController boat = BoatController.Instance;
            if (game == null || !game.IsPlaying || boat == null) return;

            float boatZ = boat.transform.position.z;

            foreach (DriftingObject thing in DriftingObject.Active)
            {
                Obstacle obstacle = thing as Obstacle;
                if (obstacle == null) continue;
                if (obstacle.HasHitBoat || obstacle.NearMissAwarded) continue;

                // Only judge it once it's fully behind the boat.
                bool hasPassed = obstacle.transform.position.z < boatZ - obstacle.radius - 1f;
                if (!hasPassed) continue;

                obstacle.NearMissAwarded = true;   // judged, whatever the result
                float distance = obstacle.ClosestApproach;
                if (distance < nearMissDistance && distance >= minimumDistance) Award(boat);
            }
        }

        void Award(BoatController boat)
        {
            float multiplier = 1f;
            if (ScoreManager.Instance != null)
            {
                multiplier = ScoreManager.Instance.comboMultiplier;
                ScoreManager.Instance.AddBonus(bonusPoints * multiplier);
                ScoreManager.Instance.NearMisses++;
            }

            if (VfxLibrary.Instance != null) VfxLibrary.Instance.Play(sparkleVfx, boat.transform.position + Vector3.up * 0.6f);
            if (OnNearMiss != null) OnNearMiss(Mathf.RoundToInt(bonusPoints * multiplier));

            if (Time.unscaledTime - lastSlowTime > slowCooldown)
            {
                lastSlowTime = Time.unscaledTime;
                if (slowRoutine != null) StopCoroutine(slowRoutine);
                slowRoutine = StartCoroutine(SlowMotion());
            }
        }

        IEnumerator SlowMotion()
        {
            // Ease into slow motion, hold it, then ease back to normal speed.
            float rampTime = slowDuration * 0.25f;
            yield return ChangeTimeScale(1f, slowTimeScale, rampTime);
            yield return new WaitForSecondsRealtime(slowDuration * 0.5f);
            yield return ChangeTimeScale(slowTimeScale, 1f, rampTime);
        }

        IEnumerator ChangeTimeScale(float from, float to, float seconds)
        {
            float t = 0f;
            while (t < 1f)
            {
                // Stop if the game got paused or ended meanwhile.
                if (GameManager.Instance == null || !GameManager.Instance.IsPlaying) yield break;

                t += Time.unscaledDeltaTime / seconds;
                Time.timeScale = Mathf.Lerp(from, to, t);
                yield return null;
            }
        }
    }
}
