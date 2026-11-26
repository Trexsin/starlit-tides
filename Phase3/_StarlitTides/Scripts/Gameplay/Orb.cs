// =============================================================================
//  Orb - a glowing ball of plankton light
// -----------------------------------------------------------------------------
//  Floats just above the water, spins and pulses. When the boat touches it:
//  restores light, adds score, spawns a burst of sparks and a glowing ripple
//  on the sea, then goes back to the pool.
// =============================================================================
using System;
using UnityEngine;

namespace StarlitTides
{
    public class Orb : DriftingObject
    {
        public static event Action<Orb> OnCollected;

        [Header("Orb")]
        [Tooltip("Light restored (fraction of the meter).")]
        public float lightValue = 0.12f;
        public float spinSpeed = 90f;
        public Transform visual;
        [Tooltip("Gentle pulse of the orb's size.")]
        public float pulseAmount = 0.08f;

        /// <summary>Hidden orbs (Bonus 8) are invisible until the lighthouse beam reveals them.</summary>
        [HideInInspector] public bool isHidden;

        private Vector3 _baseScale = Vector3.one;
        private bool _collected;

        private void Awake()
        {
            if (visual == null && transform.childCount > 0) visual = transform.GetChild(0);
            if (visual != null) _baseScale = visual.localScale;
        }

        protected override void OnSpawned()
        {
            _collected = false;
            isHidden = false;
        }

        protected override void Tick(float dt)
        {
            if (visual == null) return;
            visual.Rotate(0f, spinSpeed * dt, 0f, Space.World);
            float s = 1f + Mathf.Sin((Time.time + Seed) * 4f) * pulseAmount;
            visual.localScale = _baseScale * s;
        }

        public void Collect(BoatController boat)
        {
            if (_collected) return;
            _collected = true;

            if (boat != null && boat.lightMeter != null) boat.lightMeter.Add(lightValue);
            if (ScoreManager.Instance != null) ScoreManager.Instance.AddOrb();

            Vector3 p = transform.position;
            if (VfxLibrary.Instance != null) VfxLibrary.Instance.Play(VfxLibrary.Instance.orbBurst, p);
            if (OceanController.Instance != null) OceanController.Instance.AddRipple(p, 2.2f, 2.2f, 1.4f);

            OnCollected?.Invoke(this);
            Despawn();
        }
    }
}
