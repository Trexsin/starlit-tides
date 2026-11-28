// StarPickup: the golden star's reflection, floating on the water.
//
// After a golden shooting star falls, its reflection drifts toward you on the
// tide like an orb. Catch it to start Starburst. It works like an Orb (it
// floats, spins and pulses) but Collect() is replaced so it doesn't count as a
// normal orb or give light.

using UnityEngine;

namespace StarlitTides
{
    public class StarPickup : Orb
    {
        public override void Collect(BoatController boat)
        {
            if (PowerUpManager.Instance != null) PowerUpManager.Instance.StartStarburst(transform.position);
            if (OceanController.Instance != null) OceanController.Instance.AddRipple(transform.position, 4f, 3f, 2f);
            Despawn();
        }
    }
}
