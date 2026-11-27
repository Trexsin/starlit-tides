// BONUS FEATURE 8 - Living Village (the lighthouse reveals hidden orbs)
//
// Now and then a "hidden" orb drifts down the lane. You can't see it and you
// can't collect it, until the lighthouse beam sweeps over it. Then it lights
// up with a sparkle and stays visible. Hidden orbs give extra light and points.

using UnityEngine;

namespace StarlitTides
{
    public class HiddenOrbs : MonoBehaviour
    {
        public GameObject orbPrefab;
        public GameObject revealVfx;
        public Vector2 spawnInterval = new Vector2(7f, 13f);
        public float spawnZ = 112f;
        public Vector2 laneX = new Vector2(-12f, 12f);

        [Header("Reward")]
        public float lightValue = 0.2f;
        public int bonusPoints = 100;

        [Header("Beam check")]
        public float beamHalfAngle = 9f;
        public float beamReach = 260f;

        float timer = 5f;

        void OnEnable()
        {
            Orb.OnCollected += HandleCollected;
        }

        void OnDisable()
        {
            Orb.OnCollected -= HandleCollected;
        }

        void Update()
        {
            GameManager game = GameManager.Instance;
            if (game == null || !game.IsPlaying) return;

            timer -= Time.deltaTime;
            if (timer <= 0f)
            {
                timer = Random.Range(spawnInterval.x, spawnInterval.y);
                SpawnHiddenOrb();
            }

            RevealOrbsInBeam();
        }

        void SpawnHiddenOrb()
        {
            if (orbPrefab == null) return;
            Vector3 position = new Vector3(Random.Range(laneX.x, laneX.y), 0f, spawnZ);
            Orb orb = PoolManager.Spawn(orbPrefab, position, Quaternion.identity).GetComponent<Orb>();
            orb.SetHidden(true);
            orb.lightValue = lightValue;
        }

        void RevealOrbsInBeam()
        {
            LighthouseBeam beam = LighthouseBeam.Instance;
            if (beam == null) return;

            foreach (DriftingObject thing in DriftingObject.Active)
            {
                Orb orb = thing as Orb;
                if (orb == null || !orb.isHidden) continue;
                if (!beam.IsLit(orb.transform.position, beamHalfAngle, beamReach)) continue;

                orb.SetHidden(false);
                if (VfxLibrary.Instance != null) VfxLibrary.Instance.Play(revealVfx, orb.transform.position);
                if (OceanController.Instance != null) OceanController.Instance.AddRipple(orb.transform.position, 2.5f, 2f, 1.2f);
            }
        }

        // A revealed orb is worth extra points on top of the normal orb points.
        void HandleCollected(Orb orb)
        {
            if (orb.IsSecret && ScoreManager.Instance != null)
                ScoreManager.Instance.AddBonus(bonusPoints);
        }
    }
}
