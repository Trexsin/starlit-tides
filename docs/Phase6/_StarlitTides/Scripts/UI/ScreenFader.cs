// ScreenFader: smooth fades to and from black between screens.
// A full-screen black image on top of the UI; we only change its alpha.

using System.Collections;
using UnityEngine;

namespace StarlitTides
{
    [RequireComponent(typeof(CanvasGroup))]
    public class ScreenFader : MonoBehaviour
    {
        CanvasGroup group;

        void Awake()
        {
            group = GetComponent<CanvasGroup>();
            group.alpha = 1f;   // start black, the UIManager fades in
            group.blocksRaycasts = false;
        }

        public IEnumerator FadeOut(float seconds) { return Fade(1f, seconds); }
        public IEnumerator FadeIn(float seconds) { return Fade(0f, seconds); }

        IEnumerator Fade(float target, float seconds)
        {
            float start = group.alpha;
            float t = 0f;
            while (t < 1f)
            {
                // Real time, so fades work while the game is paused.
                t += Time.unscaledDeltaTime / Mathf.Max(seconds, 0.01f);
                float eased = t * t * (3f - 2f * t);
                group.alpha = Mathf.Lerp(start, target, eased);
                yield return null;
            }
            group.alpha = target;
        }
    }
}
