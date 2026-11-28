// AmbientWhale: a 16 m humpback whale that breaches far out at sea now and then.
//
// Pure scenery, never an obstacle. Every 20-40 seconds it rises out of the water
// off to one side of the lane, arcs through the air (rolling onto its side the way
// humpbacks do), and crashes back down with a huge splash and ripple. It plays in
// the menu as well as during a run, so the sea always feels alive.

using UnityEngine;

namespace StarlitTides
{
    public class AmbientWhale : MonoBehaviour
    {
        public Transform whale;
        public GameObject splashVfx;
        [Tooltip("Seconds between breaches (random in this range).")]
        public Vector2 interval = new Vector2(20f, 40f);
        public float breachHeight = 16f;   // an 18 m whale clears ~two thirds of its body
        public float breachDuration = 4.2f;
        [Tooltip("How far to the side of the lane it surfaces (metres).")]
        public Vector2 sideDistance = new Vector2(30f, 42f);   // just outside the channel buoys (Phase 8 lane)
        [Tooltip("How far ahead it surfaces (metres).")]
        public Vector2 aheadDistance = new Vector2(38f, 70f);   // closer, so the low camera sees it loom

        float nextBreach;
        float breachStart = -100f;
        Vector3 start, end;
        float side;
        bool splashedOut, splashedIn;

        void Start()
        {
            nextBreach = Time.time + Random.Range(6f, 14f);
            if (whale != null) whale.gameObject.SetActive(false);
        }

        void Update()
        {
            if (whale == null) return;
            if (GameManager.Instance != null && GameManager.Instance.State == GameState.Paused) return;

            if (Time.time >= nextBreach && Time.time - breachStart > breachDuration)
            {
                side = Random.value < 0.5f ? -1f : 1f;
                float x = side * Random.Range(sideDistance.x, sideDistance.y);
                float z = Random.Range(aheadDistance.x, aheadDistance.y);
                start = new Vector3(x + side * 6f, -9f, z + 10f);
                end = new Vector3(x - side * 4f, -9f, z - 12f);
                breachStart = Time.time;
                splashedOut = splashedIn = false;
                nextBreach = Time.time + Random.Range(interval.x, interval.y);
                whale.gameObject.SetActive(true);
            }

            float t = (Time.time - breachStart) / breachDuration;
            if (t < 0f || t > 1f)
            {
                if (whale.gameObject.activeSelf) whale.gameObject.SetActive(false);
                return;
            }

            // The tide carries the whole scene toward the camera, so drift with it.
            float tide = OceanController.Instance != null ? OceanController.Instance.tideSpeed : 0f;
            Vector3 drift = Vector3.back * tide * (Time.time - breachStart) * 0.5f;

            Vector3 position = Vector3.Lerp(start, end, t) + drift;
            position.y = start.y + breachHeight * 4f * t * (1f - t);
            Vector3 ahead = Vector3.Lerp(start, end, t + 0.02f) + drift;
            ahead.y = start.y + breachHeight * 4f * (t + 0.02f) * (1f - t - 0.02f);
            whale.position = position;
            Vector3 dir = ahead - position;
            if (dir.sqrMagnitude > 1e-6f)
                whale.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up) * Quaternion.Euler(0f, 0f, -side * Mathf.Lerp(0f, 110f, t));

            float surface = OceanController.Instance != null ? OceanController.Instance.GetWaterHeight(position) : 0f;
            if (!splashedOut && position.y > surface - 2f)
            {
                splashedOut = true; Splash(position, 0.8f);
                if (AudioManager.Instance != null)
                {
                    AudioManager.Instance.PlayCue(AudioManager.SeaCue.WhaleSong, position, 0.8f);
                    AudioManager.Instance.PlayCue(AudioManager.SeaCue.MediumSplash, position);
                }
            }
            if (splashedOut && !splashedIn && t > 0.62f && position.y < surface + 1f)
            {
                splashedIn = true; Splash(position, 1.6f);
                if (AudioManager.Instance != null) AudioManager.Instance.PlayCue(AudioManager.SeaCue.BigSplash, position);   // the deep whoomp of the landing
            }
        }

        void Splash(Vector3 at, float size)
        {
            Vector3 p = new Vector3(at.x, 0f, at.z);
            if (splashVfx != null && VfxLibrary.Instance != null) VfxLibrary.Instance.Play(splashVfx, p);
            if (OceanController.Instance != null) OceanController.Instance.AddRipple(p, 12f * size, 3f, 3f);
        }
    }
}
