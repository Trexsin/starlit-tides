// SeaDecor: scenery that floats past beyond the play lane (Phase 8, Step 4).
//
// Glowing algae, paper lanterns, rocks, sea stacks, arches, fishing boats and
// whale spouts. Like everything else it drifts toward the camera with the tide,
// so the sea seems to slide past the boat. It has no collider: decoration only.
//
// Extras, all optional:
//   glow       - a child that breathes (scale pulse)
//   rockAngle  - gentle rocking on the waves (boats, lanterns)
//   spout      - a particle system that bursts every few seconds (whale spouts)
//   scaleRange - a random size each time it comes out of the pool
// It also removes itself when the tide carries it over land or shallow water
// (the coast lies off to the left of the lane).

using UnityEngine;

namespace StarlitTides
{
    public class SeaDecor : DriftingObject
    {
        [Header("Scenery")]
        public Transform glow;
        public float pulseSpeed = 1.2f;
        [Range(0f, 1f)] public float pulseAmount = 0.15f;
        [Tooltip("Rocking on the waves (degrees). 0 for rocks.")]
        public float rockAngle = 0f;
        [Tooltip("Random uniform scale range applied on spawn.")]
        public Vector2 scaleRange = Vector2.one;
        [Tooltip("Random yaw on spawn.")]
        public bool randomYaw = true;

        [Header("Spout (optional)")]
        public ParticleSystem spout;
        public Vector2 spoutInterval = new Vector2(5f, 11f);

        [Header("Coast")]
        [Tooltip("Despawn if the ground under it is higher than this (metres, water = 0).")]
        public float maxGroundHeight = -1.2f;

        Vector3 glowScale = Vector3.one;
        Vector3 baseScale = Vector3.one;
        float yaw;
        float nextSpout;
        float nextLandCheck;

        void Awake()
        {
            if (glow != null) glowScale = glow.localScale;
            baseScale = transform.localScale;
        }

        protected override void OnSpawned()
        {
            yaw = randomYaw ? Random.Range(0f, 360f) : transform.eulerAngles.y;
            transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            transform.localScale = baseScale * Random.Range(scaleRange.x, scaleRange.y);
            nextSpout = Time.time + Random.Range(1f, spoutInterval.y);
            nextLandCheck = Time.time + Random.Range(0f, 0.5f);
        }

        /// <summary>True if this spot is open water (no terrain above maxGroundHeight).</summary>
        public static bool IsOpenWater(Vector3 position, float maxGround)
        {
            Terrain t = Terrain.activeTerrain;
            if (t == null) return true;
            Vector3 local = position - t.transform.position;
            Vector3 size = t.terrainData.size;
            if (local.x < 0f || local.z < 0f || local.x > size.x || local.z > size.z) return true;
            return t.SampleHeight(position) + t.transform.position.y < maxGround;
        }

        protected override void Tick(float dt)
        {
            float t = Time.time + Seed;
            if (glow != null && pulseAmount > 0f)
                glow.localScale = glowScale * (1f + Mathf.Sin(t * pulseSpeed) * pulseAmount);
            if (rockAngle > 0f)
                transform.rotation = Quaternion.Euler(Mathf.Sin(t * 0.8f) * rockAngle, yaw, Mathf.Cos(t * 0.63f) * rockAngle);

            if (spout != null && Time.time >= nextSpout)
            {
                spout.Play(true);
                nextSpout = Time.time + Random.Range(spoutInterval.x, spoutInterval.y);
            }

            if (Time.time >= nextLandCheck)
            {
                nextLandCheck = Time.time + 0.5f;
                if (!IsOpenWater(transform.position, maxGroundHeight)) Despawn();
            }
        }
    }
}
