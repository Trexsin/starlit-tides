// =============================================================================
//  Obstacle - rocks and drifting logs (jellyfish extend this class)
// -----------------------------------------------------------------------------
//  Drifts with the tide. Optional motion for variety:
//    * rollSpeed    - logs roll around their length
//    * spinSpeed    - slow turning on the water
//    * extraDrift   - logs slide across the lane
//  Hitting one drains a big chunk of light (BoatController.TakeHit calls
//  OnBoatHit), with a splash, a glowing ripple, screen shake and a flash.
//  Each obstacle also records how close the boat came (Phase 4 near-miss).
// =============================================================================
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
        [Tooltip("Approximate radius, used for spawn spacing and near-miss distance.")]
        public float radius = 1.2f;
        public Transform visual;

        [Header("Motion")]
        public float rollSpeed = 0f;
        public float spinSpeed = 0f;
        [Tooltip("Random sideways drift speed range (logs). Direction chosen at spawn.")]
        public Vector2 sidewaysDrift = Vector2.zero;

        public bool HasHitBoat { get; private set; }
        /// <summary>Closest distance (XZ) the boat has come, edge to edge. Used for near misses.</summary>
        public float ClosestApproach { get; private set; }
        public bool NearMissAwarded { get; set; }

        private Quaternion _baseRotation;

        protected virtual void Awake()
        {
            if (visual == null && transform.childCount > 0) visual = transform.GetChild(0);
        }

        protected override void OnSpawned()
        {
            HasHitBoat = false;
            NearMissAwarded = false;
            ClosestApproach = float.MaxValue;
            _baseRotation = Quaternion.Euler(0f, Random.Range(0f, 360f), 0f);
            if (type == ObstacleType.Log) _baseRotation = Quaternion.Euler(0f, Random.Range(-35f, 35f), 0f);
            transform.rotation = _baseRotation;
            if (sidewaysDrift != Vector2.zero)
            {
                float speed = Random.Range(sidewaysDrift.x, sidewaysDrift.y);
                // drift toward the centre of the lane so logs cross your path
                float dir = transform.position.x > 0f ? -1f : 1f;
                extraDrift = new Vector3(speed * dir, 0f, 0f);
            }
        }

        protected override void Tick(float dt)
        {
            if (visual != null && rollSpeed != 0f) visual.Rotate(rollSpeed * dt, 0f, 0f, Space.Self);
            if (spinSpeed != 0f) transform.Rotate(0f, spinSpeed * dt, 0f, Space.World);

            BoatController boat = BoatController.Instance;
            if (boat != null)
            {
                Vector3 d = boat.transform.position - transform.position;
                d.y = 0f;
                ClosestApproach = Mathf.Min(ClosestApproach, d.magnitude - radius);
            }
        }

        /// <summary>Called by the boat when it crashes into this obstacle.</summary>
        public virtual void OnBoatHit(BoatController boat)
        {
            HasHitBoat = true;
            Vector3 p = Vector3.Lerp(transform.position, boat.transform.position, 0.5f);
            if (VfxLibrary.Instance != null) VfxLibrary.Instance.Play(VfxLibrary.Instance.splash, p);
            if (OceanController.Instance != null) OceanController.Instance.AddRipple(p, 3.5f, 2.5f, 1.8f);
            if (HitFeedback.Instance != null) HitFeedback.Instance.PlayHit();
        }
    }
}
