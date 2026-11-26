// WhaleWave: one piece of a glowing wave pushed out by the whale.
//
// It's an Obstacle (crash into it and you lose light), but it rolls toward you
// faster than the tide and rises up and settles as it travels.

using UnityEngine;

namespace StarlitTides
{
    public class WhaleWave : Obstacle
    {
        [Tooltip("How much faster than the tide the wave rolls toward you.")]
        public float surgeSpeed = 6f;
        public float lifeTime = 9f;

        Vector3 baseScale = Vector3.one;

        protected override void Awake()
        {
            base.Awake();
            if (visual != null) baseScale = visual.localScale;
        }

        protected override void OnSpawned()
        {
            base.OnSpawned();
            transform.rotation = Quaternion.identity;   // always faces down the lane
            extraDrift = new Vector3(0f, 0f, -surgeSpeed);
        }

        protected override void Tick(float dt)
        {
            base.Tick(dt);

            // Rise up quickly, then slowly shrink away.
            float age = Time.time - SpawnTime;
            float grow = Mathf.Clamp01(age / 0.6f);
            float fade = 1f - Mathf.Clamp01((age - lifeTime * 0.7f) / (lifeTime * 0.3f));
            if (visual != null) visual.localScale = new Vector3(baseScale.x, baseScale.y * grow * fade, baseScale.z);

            if (age > lifeTime) Despawn();
        }
    }
}
