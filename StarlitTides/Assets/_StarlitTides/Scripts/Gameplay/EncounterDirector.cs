// EncounterDirector: plans the sea creatures (Phase 9).
//
// Before, every obstacle spawn rolled a dice for "creature", so mantas,
// dolphins, orcas and narwhals piled into the lane at random. Now creatures
// come as ENCOUNTERS, one at a time, chosen from the current zone's list
// (VoyageDirector), with calm gaps between them:
//
//   BowRiders       two dolphins ride beside the boat (harmless company)
//   JellyBloom      a field of moon jellies, all drifting on one current, with gaps
//   LionsMane       one or two big lion's mane jellies drifting through
//   TurtleSurfacing sea turtles crossing below the surface, coming up to breathe
//   DolphinPod      a pod porpoising across the lane in a staggered line
//   MantaGlide      one or two manta rays gliding across (they may breach)
//   OrcaHunt        a pod stalks (fins only), then charges across at burst speed
//   OrcaWaveWash    a pod lines up, dives together, and a wave sweeps the lane
//   HumpbackLunge   a bubble-net ring glows, then a humpback lunges up through it
//   GiantWhale      the giant glowing whale event (WhaleEvent)
//
// The first encounter of a zone is always that zone's signature creature on its
// own (teach), the rest are picked by weight (test). Big encounters thin out
// the filler rocks and logs (DifficultyManager.FillerScale).

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace StarlitTides
{
    public class EncounterDirector : MonoBehaviour
    {
        public static EncounterDirector Instance { get; private set; }

        [Header("Creatures (life-size prefabs)")]
        public GameObject moonJelly;
        public GameObject lionsMane;
        public GameObject turtle;
        public GameObject dolphin;
        public GameObject manta;
        public GameObject orcaMale;
        public GameObject orcaFemale;
        public GameObject humpback;
        public WhaleEvent giantWhale;

        [Header("Placement")]
        public float spawnZ = 112f;
        [Tooltip("Seconds after a zone's rest ends before its first encounter.")]
        public float firstEncounterDelay = 1.5f;

        [Header("Orca wave-wash")]
        public float washSpeed = 7f;
        [Tooltip("Sideways speed the wave gives the boat (it fades out over ~0.5 s: about 6 m of shove).")]
        public float washPush = 12f;

        public bool Busy { get { return running != null; } }
        public EncounterKind? CurrentEncounter { get; private set; }

        Coroutine running;
        float timer;
        int lastStage = -1;
        int encountersThisZone;
        bool giantWhaleUsed;

        void Awake()
        {
            Instance = this;
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        void OnEnable()
        {
            GameManager.OnRunStarted += ResetDirector;
            GameManager.OnStateChanged += HandleState;
        }

        void OnDisable()
        {
            GameManager.OnRunStarted -= ResetDirector;
            GameManager.OnStateChanged -= HandleState;
        }

        void Start()
        {
            Prewarm(moonJelly, 24); Prewarm(lionsMane, 2); Prewarm(turtle, 3); Prewarm(dolphin, 6);
            Prewarm(manta, 2); Prewarm(orcaMale, 2); Prewarm(orcaFemale, 3); Prewarm(humpback, 2);
            // The giant whale only comes when an encounter calls it.
            if (giantWhale != null) giantWhale.autoSchedule = false;
        }

        static void Prewarm(GameObject prefab, int count) { if (prefab != null) PoolManager.Prewarm(prefab, count); }

        void ResetDirector()
        {
            StopEncounter();
            lastStage = -1;
            giantWhaleUsed = false;
        }

        void HandleState(GameState state)
        {
            if (state != GameState.Playing && state != GameState.Paused) StopEncounter();
        }

        void StopEncounter()
        {
            if (running != null) StopCoroutine(running);
            running = null;
            CurrentEncounter = null;
            if (DifficultyManager.Instance != null) DifficultyManager.Instance.FillerScale = 1f;
        }

        void Update()
        {
            VoyageDirector voyage = VoyageDirector.Instance;
            GameManager game = GameManager.Instance;
            if (voyage == null || game == null || !game.IsPlaying) return;

            if (voyage.Stage != lastStage)
            {
                lastStage = voyage.Stage;
                encountersThisZone = 0;
                giantWhaleUsed = false;
                timer = (voyage.Stage == 0 ? game.gracePeriod + 6f : voyage.restDuration) + firstEncounterDelay;
            }

            if (running != null || game.InGracePeriod) return;
            timer -= Time.deltaTime;
            if (timer > 0f) return;

            VoyageDirector.EncounterWeight[] list = voyage.Current.encounters;
            if (list == null || list.Length == 0) { timer = 5f; return; }
            EncounterKind kind = encountersThisZone == 0 ? list[0].kind : Pick(list);
            encountersThisZone++;
            running = StartCoroutine(Run(kind, voyage));
        }

        EncounterKind Pick(VoyageDirector.EncounterWeight[] list)
        {
            float total = 0f;
            foreach (var e in list) if (Allowed(e.kind)) total += e.weight;
            float roll = Random.value * total;
            foreach (var e in list)
            {
                if (!Allowed(e.kind)) continue;
                roll -= e.weight;
                if (roll <= 0f) return e.kind;
            }
            return list[0].kind;
        }

        bool Allowed(EncounterKind kind)
        {
            if (kind == EncounterKind.GiantWhale) return giantWhale != null && !giantWhaleUsed && !giantWhale.IsActive;
            return true;
        }

        IEnumerator Run(EncounterKind kind, VoyageDirector voyage)
        {
            CurrentEncounter = kind;
            DifficultyManager difficulty = DifficultyManager.Instance;
            IEnumerator routine = null;
            float filler = 1f;
            switch (kind)
            {
                case EncounterKind.BowRiders: routine = BowRiders(); break;
                case EncounterKind.JellyBloom: routine = JellyBloom(voyage); filler = 0.6f; break;
                case EncounterKind.LionsMane: routine = LionsManes(voyage); filler = 0.8f; break;
                case EncounterKind.TurtleSurfacing: routine = Turtles(voyage); filler = 0.8f; break;
                case EncounterKind.DolphinPod: routine = DolphinPod(voyage); filler = 0.6f; break;
                case EncounterKind.MantaGlide: routine = Mantas(voyage); filler = 0.8f; break;
                case EncounterKind.OrcaHunt: routine = OrcaHunt(voyage); filler = 0.4f; break;
                case EncounterKind.OrcaWaveWash: routine = OrcaWaveWash(voyage); filler = 0.5f; break;
                case EncounterKind.HumpbackLunge: routine = Humpbacks(voyage); filler = 0.4f; break;
                case EncounterKind.GiantWhale: routine = GiantWhale(); filler = 0.3f; break;
            }
            if (difficulty != null) difficulty.FillerScale = filler;
            if (routine != null) yield return routine;
            if (difficulty != null) difficulty.FillerScale = 1f;
            timer = voyage.EncounterGap * Random.Range(0.85f, 1.2f);
            CurrentEncounter = null;
            running = null;
        }

        // ------------------------------------------------------------------ helpers

        static float BoatX { get { return BoatController.Instance != null ? BoatController.Instance.transform.position.x : 0f; } }
        static float BoatZ { get { return BoatController.Instance != null ? BoatController.Instance.transform.position.z : 0f; } }
        static float Tide { get { return OceanController.Instance != null ? Mathf.Max(3f, OceanController.Instance.tideSpeed) : 8f; } }
        static float Lane { get { return PlayArea.ObstacleHalfWidth; } }
        static float Side { get { return Random.value < 0.5f ? -1f : 1f; } }

        static T Spawn<T>(GameObject prefab, Vector3 position) where T : Component
        {
            if (prefab == null) return null;
            GameObject go = PoolManager.Spawn(prefab, position, Quaternion.identity);
            return go != null ? go.GetComponent<T>() : null;
        }

        // How much of the tide a creature must swim against so that, crossing the
        // lane at 'speed' from 'x', it reaches the lane centre 'targetAhead' metres
        // in front of the boat (the encounter happens where you are, not behind you).
        static float HoldFor(float x, float z, float speed, float targetAhead)
        {
            float seconds = Mathf.Abs(x) / Mathf.Max(0.5f, speed);
            float close = z - (BoatZ + targetAhead);
            return Mathf.Clamp(1f - close / Mathf.Max(1f, Tide * seconds), 0f, 0.95f);
        }

        // ------------------------------------------------------------------ encounters

        IEnumerator BowRiders()
        {
            float side = Side;
            for (int i = 0; i < 2; i++)
            {
                Vector3 p = new Vector3(BoatX + side * (5f + i * 2f), 0f, BoatZ - 12f - i * 3f);
                SeaCreature d = Spawn<SeaCreature>(dolphin, p);
                if (d != null) d.BeginBowRide(i == 0 ? side : -side, Random.Range(8f, 11f), 3f + i * 2.5f);
            }
            yield return new WaitForSeconds(9f);
        }

        IEnumerator JellyBloom(VoyageDirector voyage)
        {
            // One current for the whole bloom, so the field (and its gaps) slide together.
            float drift = Side * Random.Range(0.6f, 1.2f);
            Vector3 current = new Vector3(drift, 0f, 0f);
            int count = Mathf.RoundToInt(Mathf.Lerp(18f, 30f, Mathf.Clamp01(voyage.Progress)));
            float depth = 16f;
            // The field reaches further upstream: it drifts into the lane as it comes.
            float travelTime = (spawnZ - BoatZ) / Tide;
            float upstream = -Mathf.Sign(drift) * Mathf.Abs(drift) * travelTime;
            float minX = -Lane - 2f + Mathf.Min(0f, upstream), maxX = Lane + 2f + Mathf.Max(0f, upstream);
            // One or two clear lanes through the bloom (shifted upstream the same way).
            int gaps = voyage.Progress < 0.6f ? 2 : 1;
            float[] gapX = new float[gaps];
            for (int g = 0; g < gaps; g++) gapX[g] = Random.Range(-Lane * 0.7f, Lane * 0.7f) + upstream;
            var placed = new List<Vector3>();
            int tries = 0;
            while (placed.Count < count && tries++ < count * 12)
            {
                Vector3 p = new Vector3(Random.Range(minX, maxX), 0f, spawnZ + Random.Range(0f, depth));
                bool ok = true;
                foreach (float gx in gapX) if (Mathf.Abs(p.x - gx) < 3.2f) ok = false;
                foreach (Vector3 q in placed) if ((q - p).sqrMagnitude < 2.2f * 2.2f) { ok = false; break; }
                if (!ok) continue;
                placed.Add(p);
                Jellyfish j = Spawn<Jellyfish>(moonJelly, p);
                if (j != null) j.BeginDrift(current);
            }
            yield return new WaitForSeconds(depth / Tide + 3f);
        }

        IEnumerator LionsManes(VoyageDirector voyage)
        {
            int count = voyage.Progress > 0.7f ? 2 : 1;
            float drift = Side * Random.Range(0.3f, 0.8f);
            for (int i = 0; i < count; i++)
            {
                Vector3 p = new Vector3(Random.Range(-Lane * 0.8f, Lane * 0.8f), 0f, spawnZ + i * 18f);
                Jellyfish j = Spawn<Jellyfish>(lionsMane, p);
                if (j != null) j.BeginDrift(new Vector3(drift, 0f, 0f));
            }
            yield return new WaitForSeconds(4f);
        }

        IEnumerator Turtles(VoyageDirector voyage)
        {
            int count = Random.Range(2, 4);
            for (int i = 0; i < count; i++)
            {
                float side = Side;
                Vector3 p = new Vector3(side * Random.Range(Lane * 0.3f, Lane * 0.9f), 0f, spawnZ - 10f + i * 9f);
                SeaCreature t = Spawn<SeaCreature>(turtle, p);
                if (t == null) continue;
                t.BeginCrossing(-side, Random.Range(0.4f, 0.8f));
                t.BeginSurfacing(Random.Range(1f, 4f));
            }
            yield return new WaitForSeconds(5f);
        }

        IEnumerator DolphinPod(VoyageDirector voyage)
        {
            int count = Random.Range(3, Mathf.RoundToInt(Mathf.Lerp(4f, 7f, Mathf.Clamp01(voyage.Progress))));
            float side = Side;
            float speed = Random.Range(7f, 9f);   // porpoising travel speed, ~25-32 km/h
            for (int i = 0; i < count; i++)
            {
                float x = side * (Lane + 8f + i * 1.8f);
                float ahead = 6f + i * 4.5f;
                float z = BoatZ + ahead + Tide * (Mathf.Abs(x) / speed) * 0.7f;
                SeaCreature d = Spawn<SeaCreature>(dolphin, new Vector3(x, 0f, z));
                if (d == null) continue;
                d.BeginCrossing(-side, speed, HoldFor(x, z, speed, ahead));
                d.BeginPorpoising(i * 0.22f + Random.Range(0f, 0.1f), Random.Range(0.3f, 0.5f));
            }
            yield return new WaitForSeconds(6.5f);
        }

        IEnumerator Mantas(VoyageDirector voyage)
        {
            int count = voyage.Progress > 0.6f ? 2 : 1;
            for (int i = 0; i < count; i++)
            {
                float side = Side;
                Spawn<SeaCreature>(manta, new Vector3(side * Random.Range(Lane * 0.5f, Lane), 0f, spawnZ + i * 14f));
            }
            yield return new WaitForSeconds(5f);
        }

        IEnumerator OrcaHunt(VoyageDirector voyage)
        {
            int count = Random.Range(3, voyage.Progress > 0.8f ? 6 : 5);
            float side = Side;
            float charge = Random.Range(11f, 13f);   // burst speed, ~40-47 km/h
            var pod = new List<SeaCreature>();
            for (int i = 0; i < count; i++)
            {
                GameObject prefab = i == 0 ? orcaMale : orcaFemale;
                float x = side * (Lane + 6f + (i % 2) * 2.5f);
                float z = BoatZ + 30f + i * 8f;
                SeaCreature o = Spawn<SeaCreature>(prefab, new Vector3(x, 0f, z));
                if (o == null) continue;
                // Staggered: each orca crosses a little further ahead, a moving fence with gaps.
                o.BeginHunt(-side, 2.6f + i * 0.35f, charge, BoatZ + 3f + i * 7f);
                pod.Add(o);
            }
            if (HudView.Instance != null) HudView.Instance.Popup("Orcas on the hunt!");
            yield return new WaitForSeconds(2.6f + count * 0.35f + 0.7f + (Lane * 2f + 12f) / charge);
        }

        IEnumerator OrcaWaveWash(VoyageDirector voyage)
        {
            float side = Side;
            int count = 3;
            var pod = new List<SeaCreature>();
            for (int i = 0; i < count; i++)
            {
                float x = side * (Lane + 6f);
                float z = BoatZ + 8f + i * 7f;
                SeaCreature o = Spawn<SeaCreature>(i == 1 ? orcaMale : orcaFemale, new Vector3(x, 0f, z));
                if (o == null) continue;
                o.BeginHunt(-side, 999f, 0f, z);   // stalk side by side...
                pod.Add(o);
            }
            if (HudView.Instance != null) HudView.Instance.Popup("The pod lines up...");
            yield return new WaitForSeconds(2.4f);
            foreach (SeaCreature o in pod) if (o != null && o.isActiveAndEnabled) o.BeginDive();   // ...and dives together
            yield return WashWave(-side, side * (Lane + 6f));
        }

        // The wave the diving pod sends across the lane. Its glowing front shoves
        // the boat sideways as it passes (no damage, but it can push you into things).
        IEnumerator WashWave(float dir, float startX)
        {
            float x = startX;
            float endX = -startX;
            bool pushed = false;
            float rippleTimer = 0f, splashTimer = 0f;
            OceanController ocean = OceanController.Instance;
            while (dir > 0f ? x < endX : x > endX)
            {
                float dt = Time.deltaTime;
                float prev = x;
                x += dir * washSpeed * dt;
                float boatZ = BoatZ;

                rippleTimer -= dt;
                if (rippleTimer <= 0f && ocean != null)
                {
                    rippleTimer = 0.09f;
                    for (int k = 0; k < 4; k++)
                    {
                        Vector3 p = new Vector3(x + Random.Range(-0.6f, 0.6f), 0f, boatZ - 8f + k * 13f + Random.Range(-3f, 3f));
                        ocean.AddRipple(p, 3.2f, 2.6f, 0.55f);
                    }
                }
                splashTimer -= dt;
                if (splashTimer <= 0f && VfxLibrary.Instance != null)
                {
                    splashTimer = 0.16f;
                    Vector3 p = new Vector3(x, 0f, boatZ + Random.Range(-6f, 34f));
                    if (ocean != null) p.y = ocean.GetWaterHeight(p);
                    VfxLibrary.Instance.Play(VfxLibrary.Instance.splash, p);
                }

                BoatController boat = BoatController.Instance;
                if (!pushed && boat != null)
                {
                    float bx = boat.transform.position.x;
                    if ((prev - bx) * (x - bx) <= 0f)
                    {
                        pushed = true;
                        if (!boat.IsDashing) boat.Push(new Vector3(dir * washPush, 0f, 0f));   // a dash cuts through the wave
                        if (VfxLibrary.Instance != null) VfxLibrary.Instance.Play(VfxLibrary.Instance.splash, boat.transform.position);
                    }
                }
                yield return null;
            }
        }

        IEnumerator Humpbacks(VoyageDirector voyage)
        {
            int count = voyage.Progress > 0.85f ? 2 : 1;
            for (int i = 0; i < count; i++)
            {
                float ring = 2.4f;
                // Bubble nets go where the boat is heading: that's the danger.
                float x = Mathf.Clamp(BoatX + Random.Range(-7f, 7f), -Lane + 5f, Lane - 5f);
                float z = BoatZ + 8f + Tide * (ring + 0.7f);
                SeaCreature h = Spawn<SeaCreature>(humpback, new Vector3(x, 0f, z));
                if (h != null) h.BeginLunge(ring);
                if (i == 0 && HudView.Instance != null) HudView.Instance.Popup("Bubbles rising... stay out of the ring!");
                yield return new WaitForSeconds(2.2f);
            }
            yield return new WaitForSeconds(3.5f);
        }

        IEnumerator GiantWhale()
        {
            giantWhaleUsed = true;
            if (giantWhale == null) yield break;
            giantWhale.TriggerNow();
            yield return null;
            while (giantWhale.IsActive) yield return null;
        }
    }
}
