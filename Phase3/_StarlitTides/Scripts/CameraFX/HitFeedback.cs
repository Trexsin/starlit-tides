// HitFeedback: makes crashes feel like crashes.
//
// When the boat hits something, three things happen at once:
//   1. Screen shake - a Cinemachine Impulse (the camera has a listener)
//   2. Flash        - a second post-processing Volume (colour fringes, dark
//                     edges) jumps to full strength and fades out
//   3. Hit-stop     - time almost freezes for a split second
// (The obstacle adds its own splash and ripple.)
// Dashing gets a smaller version of the shake and flash.

using System.Collections;
using Unity.Cinemachine;
using UnityEngine;
using UnityEngine.Rendering;

namespace StarlitTides
{
    public class HitFeedback : MonoBehaviour
    {
        public static HitFeedback Instance { get; private set; }

        [Header("Shake")]
        public CinemachineImpulseSource impulseSource;
        public float hitShakeForce = 1.0f;
        public float dashShakeForce = 0.25f;

        [Header("Flash")]
        [Tooltip("Volume with the hit effect. Its weight goes 1 -> 0.")]
        public Volume flashVolume;
        public float hitFlashDuration = 0.45f;
        public float dashFlashDuration = 0.2f;
        [Range(0f, 1f)] public float dashFlashWeight = 0.35f;

        [Header("Hit-stop")]
        public float hitStopDuration = 0.06f;
        [Range(0f, 1f)] public float hitStopTimeScale = 0.05f;

        Coroutine flashRoutine;
        Coroutine hitStopRoutine;

        void Awake()
        {
            Instance = this;
            if (flashVolume != null) flashVolume.weight = 0f;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnEnable()
        {
            BoatController.OnDash += PlayDash;
        }

        void OnDisable()
        {
            BoatController.OnDash -= PlayDash;
        }

        public void PlayHit()
        {
            if (impulseSource != null) impulseSource.GenerateImpulseWithForce(hitShakeForce);
            Flash(1f, hitFlashDuration);

            if (hitStopRoutine != null) StopCoroutine(hitStopRoutine);
            hitStopRoutine = StartCoroutine(HitStop());
        }

        public void PlayDash()
        {
            if (impulseSource != null) impulseSource.GenerateImpulseWithForce(dashShakeForce);
            Flash(dashFlashWeight, dashFlashDuration);

            if (VfxLibrary.Instance != null && BoatController.Instance != null)
                VfxLibrary.Instance.Play(VfxLibrary.Instance.dashBurst, BoatController.Instance.transform.position);
        }

        public void Flash(float weight, float duration)
        {
            if (flashVolume == null) return;
            if (flashRoutine != null) StopCoroutine(flashRoutine);
            flashRoutine = StartCoroutine(FlashFade(weight, duration));
        }

        IEnumerator FlashFade(float weight, float duration)
        {
            float t = 0f;
            while (t < 1f)
            {
                // Uses real time so it still fades during hit-stop and pause.
                t += Time.unscaledDeltaTime / duration;
                float fade = (1f - t) * (1f - t);   // drops fast, then eases out
                flashVolume.weight = weight * fade;
                yield return null;
            }
            flashVolume.weight = 0f;
        }

        IEnumerator HitStop()
        {
            GameManager game = GameManager.Instance;
            if (game != null && !game.IsPlaying) yield break;

            float normalTimeScale = Time.timeScale;
            Time.timeScale = hitStopTimeScale;

            yield return new WaitForSecondsRealtime(hitStopDuration);

            // Put time back, unless the game was paused or ended meanwhile.
            if (game == null || game.IsPlaying)
                Time.timeScale = normalTimeScale > hitStopTimeScale ? normalTimeScale : 1f;
        }
    }
}
