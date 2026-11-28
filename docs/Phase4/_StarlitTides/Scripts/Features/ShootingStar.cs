// ShootingStar: a bright streak that flies across the sky.
//
// It moves from "start" to "end" over "duration" seconds and leaves a glowing
// trail (a TrailRenderer). The PowerUpManager launches white ones now and then
// just for atmosphere, and a golden one before each Starburst power-up.

using System;
using UnityEngine;

namespace StarlitTides
{
    public class ShootingStar : MonoBehaviour
    {
        public TrailRenderer trail;

        Vector3 start;
        Vector3 end;
        float duration;
        float timer;
        Action onFinished;

        // Starts the flight. onFinished is called when it reaches the end (can be null).
        public void Launch(Vector3 from, Vector3 to, float seconds, Color color, Action finished)
        {
            start = from;
            end = to;
            duration = seconds;
            timer = 0f;
            onFinished = finished;

            transform.position = from;
            if (trail != null)
            {
                trail.Clear();
                trail.startColor = color;
                trail.endColor = new Color(color.r, color.g, color.b, 0f);
            }
        }

        void Update()
        {
            timer += Time.deltaTime;
            float t = Mathf.Clamp01(timer / duration);
            transform.position = Vector3.Lerp(start, end, t);

            // Wait a little after arriving so the trail can fade out.
            if (timer >= duration + 0.8f)
            {
                Action callback = onFinished;
                onFinished = null;
                PoolManager.Despawn(gameObject);
                if (callback != null) callback();
            }
        }
    }
}
