// RainRipples: little rings on the sea wherever the rain falls (VFX polish pass).
// The WeatherManager turns the rain's emission up and down with the storm; this
// copies a fraction of that rate to the ripple system so the two always match.

using UnityEngine;

namespace StarlitTides
{
    [RequireComponent(typeof(ParticleSystem))]
    public class RainRipples : MonoBehaviour
    {
        public ParticleSystem rain;
        [Tooltip("Ripples per raindrop (most drops land outside the view anyway).")]
        public float ripplesPerDrop = 0.12f;

        ParticleSystem ripples;

        void Awake() { ripples = GetComponent<ParticleSystem>(); }

        void Update()
        {
            if (rain == null) return;
            float rate = rain.emission.rateOverTime.constant * ripplesPerDrop;
            var em = ripples.emission;
            em.rateOverTime = rate;
            if (rate > 0.01f && !ripples.isPlaying) ripples.Play();
            // stay level with the sea even if the camera rises or dips
            Vector3 p = transform.position;
            transform.position = new Vector3(p.x, 0.08f, p.z);
        }
    }
}
