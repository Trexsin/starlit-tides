// =============================================================================
//  HitFeedback - makes crashes FEEL like crashes ("game juice")
// -----------------------------------------------------------------------------
//  On a hit, four things happen in the same instant:
//    1. Screen shake      - a Cinemachine Impulse (the camera has a listener)
//    2. Flash             - a second post-processing Volume (chromatic
//                           aberration, cold flash, dark vignette) whose weight
//                           jumps to 1 and fades back to 0
//    3. Hit-stop          - time nearly freezes for a few hundredths of a second
//    4. (the obstacle spawns a splash + glowing ripple itself)
//  The dash gets a lighter version (small shake + short flash).
// =============================================================================
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
        [Tooltip("Volume with the hit profile (weight is animated 1 -> 0).")]
        public Volume flashVolume;
        public float hitFlashDuration = 0.45f;
        public float dashFlashDuration = 0.2f;
        [Range(0f, 1f)] public float dashFlashWeight = 0.35f;

        [Header("Hit-stop")]
        public float hitStopDuration = 0.06f;
        [Range(0f, 1f)] public float hitStopTimeScale = 0.05f;

        private Coroutine _flash, _stop;

        private void Awake()
        {
            Instance = this;
            if (flashVolume != null) flashVolume.weight = 0f;
        }

        private void OnDestroy() { if (Instance == this) Instance = null; }

        private void OnEnable() { BoatController.OnDash += PlayDash; }
        private void OnDisable() { BoatController.OnDash -= PlayDash; }

        public void PlayHit()
        {
            if (impulseSource != null) impulseSource.GenerateImpulseWithForce(hitShakeForce);
            Flash(1f, hitFlashDuration);
            if (_stop != null) StopCoroutine(_stop);
            _stop = StartCoroutine(HitStop());
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
            if (_flash != null) StopCoroutine(_flash);
            _flash = StartCoroutine(FlashRoutine(weight, duration));
        }

        private IEnumerator FlashRoutine(float weight, float duration)
        {
            float t = 0f;
            while (t < 1f)
            {
                t += Time.unscaledDeltaTime / duration;
                flashVolume.weight = weight * (1f - t) * (1f - t);   // fast attack, smooth decay
                yield return null;
            }
            flashVolume.weight = 0f;
        }

        private IEnumerator HitStop()
        {
            GameManager gm = GameManager.Instance;
            if (gm != null && !gm.IsPlaying) yield break;
            float previous = Time.timeScale;
            Time.timeScale = hitStopTimeScale;
            yield return new WaitForSecondsRealtime(hitStopDuration);
            if (gm == null || gm.IsPlaying) Time.timeScale = previous <= hitStopTimeScale ? 1f : previous;
        }
    }
}
