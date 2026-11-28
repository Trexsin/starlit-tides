// Obstacle: rocks and drifting logs. (Jellyfish builds on this class.)
//
// It drifts with the tide like everything else, and can also:
//   - rollSpeed:     roll around (logs)
//   - spinSpeed:     slowly turn on the water
//   - sidewaysDrift: slide across the lane (logs)
// Hitting one costs a lot of light. BoatController.TakeHit() calls OnBoatHit(),
// which adds the splash, ripple, screen shake and flash.
// It also remembers how close the boat came, for the near-miss bonus (Phase 4).

using UnityEngine;

namespace StarlitTides
{
    public enum ObstacleType { Rock, Log, Jellyfish }

    public class Obstacle : DriftingObject
    {
        [Header("Obstacle")]
        public ObstacleType type = ObstacleType.Rock;
        [Tooltip("Light lost when the boat hits this.")]
        public float lightDamage = 0.25f;
        [Tooltip("Rough size, used for spawn spacing and near misses.")]
        public float radius = 1.2f;
        public Transform visual;

        [Header("Movement")]
        public float rollSpeed = 0f;
        public float spinSpeed = 0f;
        [Tooltip("Random sideways speed range (logs). The direction is picked at spawn.")]
        public Vector2 sidewaysDrift = Vector2.zero;

        public bool HasHitBoat { get; private set; }
        // Closest the boat has come, from its centre to our edge.
        public float ClosestApproach { get; private set; }
        public bool NearMissAwarded { get; set; }

        protected virtual void Awake()
        {
            if (visual == null && transform.childCount > 0) visual = transform.GetChild(0);
        }

        protected override void OnSpawned()
        {
            HasHitBoat = false;
            NearMissAwarded = false;
            ClosestApproach = float.MaxValue;

            // Random facing. Logs only turn a little so they lie across the lane.
            float angle = Random.Range(0f, 360f);
            if (type == ObstacleType.Log) angle = Random.Range(-35f, 35f);
            transform.rotation = Quaternion.Euler(0f, angle, 0f);

            if (sidewaysDrift != Vector2.zero)
            {
                // Drift toward the middle of the lane so logs cross your path.
                float speed = Random.Range(sidewaysDrift.x, sidewaysDrift.y);
                float direction = transform.position.x > 0f ? -1f : 1f;
                extraDrift = new Vector3(speed * direction, 0f, 0f);
            }
        }

        protected override void Tick(float dt)
        {
            if (visual != null && rollSpeed != 0f) visual.Rotate(rollSpeed * dt, 0f, 0f, Space.Self);
            if (spinSpeed != 0f) transform.Rotate(0f, spinSpeed * dt, 0f, Space.World);

            // Track how close the boat gets (ignoring height).
            BoatController boat = BoatController.Instance;
            if (boat != null)
            {
                Vector3 offset = boat.transform.position - transform.position;
                offset.y = 0f;
                ClosestApproach = Mathf.Min(ClosestApproach, offset.magnitude - radius);
            }
        }

        // Called by the boat when it crashes into this obstacle.
        public virtual void OnBoatHit(BoatController boat)
        {
            HasHitBoat = true;

            Vector3 hitPoint = Vector3.Lerp(transform.position, boat.transform.position, 0.5f);
            if (VfxLibrary.Instance != null) VfxLibrary.Instance.Play(VfxLibrary.Instance.splash, hitPoint);
            if (OceanController.Instance != null) OceanController.Instance.AddRipple(hitPoint, 3.5f, 2.5f, 1.8f);
            if (HitFeedback.Instance != null) HitFeedback.Instance.PlayHit();
        }
    }
}
