// Orb: a glowing ball of plankton light that refills the boat's light.
//
// It floats just above the water, spins and pulses. When the boat touches it:
// light comes back, points are added, sparks burst out, a glowing ripple
// spreads on the sea, and the orb goes back to the pool.

using System;
using UnityEngine;

namespace StarlitTides
{
    public class Orb : DriftingObject
    {
        public static event Action<Orb> OnCollected;

        [Header("Orb")]
        [Tooltip("How much light it gives back (0.12 = 12% of the meter).")]
        public float lightValue = 0.12f;
        public float spinSpeed = 90f;
        public Transform visual;
        public float pulseAmount = 0.08f;

        // Hidden orbs (Phase 5) stay invisible until the lighthouse beam finds them.
        [HideInInspector] public bool isHidden;

        Vector3 baseScale = Vector3.one;
        bool collected;

        void Awake()
        {
            if (visual == null && transform.childCount > 0) visual = transform.GetChild(0);
            if (visual != null) baseScale = visual.localScale;
        }

        protected override void OnSpawned()
        {
            collected = false;
            isHidden = false;
        }

        protected override void Tick(float dt)
        {
            if (visual == null) return;

            visual.Rotate(0f, spinSpeed * dt, 0f, Space.World);
            float pulse = 1f + Mathf.Sin((Time.time + Seed) * 4f) * pulseAmount;
            visual.localScale = baseScale * pulse;
        }

        public void Collect(BoatController boat)
        {
            if (collected) return;
            collected = true;

            if (boat != null && boat.lightMeter != null) boat.lightMeter.Add(lightValue);
            if (ScoreManager.Instance != null) ScoreManager.Instance.AddOrb();

            Vector3 position = transform.position;
            if (VfxLibrary.Instance != null) VfxLibrary.Instance.Play(VfxLibrary.Instance.orbBurst, position);
            if (OceanController.Instance != null) OceanController.Instance.AddRipple(position, 2.2f, 2.2f, 1.4f);

            if (OnCollected != null) OnCollected(this);
            Despawn();
        }
    }
}
