// BONUS FEATURE 8 - Living Village (windows)
//
// The village slowly goes to sleep during the night:
// - every couple of seconds a random window switches on or off
// - at the start most windows are lit; the longer you sail, the fewer stay on
// (Lantern flicker, fireflies and the sweeping lighthouse were added in Phase 2.
//  HiddenOrbs.cs adds the lighthouse revealing secret orbs.)

using System.Collections.Generic;
using UnityEngine;

namespace StarlitTides
{
    public class LivingVillage : MonoBehaviour
    {
        [Tooltip("All the window panes in the village (filled in by the builder).")]
        public List<Renderer> windows = new List<Renderer>();
        public Material litMaterial;
        public Material warmMaterial;
        public Material offMaterial;

        [Tooltip("Seconds between window changes (random in this range).")]
        public Vector2 changeInterval = new Vector2(1.2f, 3.5f);
        [Tooltip("Fraction of windows lit at the start, and late at night.")]
        public Vector2 litFraction = new Vector2(0.8f, 0.35f);
        [Tooltip("Seconds of play until the village is mostly asleep.")]
        public float sleepAfter = 240f;

        float timer;

        void Update()
        {
            timer -= Time.deltaTime;
            if (timer > 0f || windows.Count == 0) return;
            timer = Random.Range(changeInterval.x, changeInterval.y);

            // How many windows we want lit right now.
            float runTime = GameManager.Instance != null ? GameManager.Instance.RunTime : 0f;
            float targetFraction = Mathf.Lerp(litFraction.x, litFraction.y, Mathf.Clamp01(runTime / sleepAfter));

            int litCount = 0;
            foreach (Renderer w in windows)
                if (w != null && w.sharedMaterial != offMaterial) litCount++;
            float currentFraction = litCount / (float)windows.Count;

            // Pick a random window and switch it toward the target.
            Renderer window = windows[Random.Range(0, windows.Count)];
            if (window == null) return;

            bool isLit = window.sharedMaterial != offMaterial;
            bool wantMoreLight = currentFraction < targetFraction;
            if (isLit && !wantMoreLight) window.sharedMaterial = offMaterial;
            else if (!isLit && wantMoreLight) window.sharedMaterial = Random.value < 0.6f ? litMaterial : warmMaterial;
        }
    }
}
