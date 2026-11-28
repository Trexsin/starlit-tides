// BoatSplash: the boat's wake and splashes, modelled on how real boats do it.
//
// Research (see Phase9/Phase9_Plan_Voyage.md):
//   - A moving boat leaves a KELVIN WAKE: a V of waves whose arms make ~19.5 deg
//     (arcsin 1/3) with its path. The divergent waves pile up along the arms, and
//     curved TRANSVERSE crests fill the inside of the V.
//   - Behind the stern the hull and propeller churn a band of turbulent FOAM.
//   - Games lay wakes down per metre travelled, not per second, so the trail
//     stays even at any speed (the Blood Stone team's approach).
//
// So the wake is made of continuous FOAM RIBBONS lying on the water (one mesh,
// drawn with StarlitWake.shader, which breaks them into soft, ageing foam):
//   - two arm ribbons: points leave the bow sides moving away from the boat's
//     path at speed * tan(19.5 deg), so the ribbons draw the arms of the V
//   - a wide churned band behind the stern
//   - a curved crest every few metres, stretching between the arms as it ages
// Every point drifts back with the water and rides the waves; ribbons widen
// and fade with age. Steering hard throws spray off the outside of the turn.

using System.Collections.Generic;
using UnityEngine;

namespace StarlitTides
{
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class BoatSplash : MonoBehaviour
    {
        public BoatController boat;
        [Tooltip("Spray thrown off the outside of hard turns.")]
        public ParticleSystem turnSpray;

        [Header("Kelvin wake")]
        [Tooltip("Half-angle of the V (degrees). Kelvin's classic value is 19.47.")]
        public float kelvinAngle = 19.47f;
        [Tooltip("Metres the boat moves through the water between ribbon points.")]
        public float spacing = 0.5f;
        public float bowOffset = 1.2f, sternOffset = -1.15f, halfBeam = 0.45f;
        [Tooltip("Speed through the water (m/s) at which the wake is at full strength.")]
        public float fullSpeed = 16f;

        [Header("Ribbons: width at birth -> at death (m), life (s), opacity")]
        // (Kept small and short: a wake that filled the bottom of the screen distracted from play.)
        public Vector2 armWidth = new Vector2(0.22f, 0.8f);
        public float armLife = 1.3f, armOpacity = 0.6f;
        public Vector2 bandWidth = new Vector2(0.5f, 1.4f);
        public float bandLife = 1.4f, bandOpacity = 0.45f;
        public Vector2 crestWidth = new Vector2(0.2f, 0.5f);
        public float crestLife = 1.0f, crestOpacity = 0.25f;
        [Tooltip("Metres travelled between transverse crests inside the V (0 = none).")]
        public float crestSpacing = 0f;

        [Header("Turn spray")]
        public float sprayFromSideSpeed = 6f;
        public float sprayRate = 160f;

        struct Point { public Vector3 pos, vel; public float born, dist; }

        class Ribbon
        {
            public readonly List<Point> points = new List<Point>();
            public Vector2 width; public float life, opacity, seed;
            public bool closed;     // crests are emitted all at once and then only age
        }

        readonly Ribbon leftArm = new Ribbon(), rightArm = new Ribbon(), band = new Ribbon();
        readonly List<Ribbon> crests = new List<Ribbon>();
        readonly Stack<Ribbon> crestPool = new Stack<Ribbon>();

        Mesh mesh;
        readonly List<Vector3> verts = new List<Vector3>();
        readonly List<Color> colors = new List<Color>();
        readonly List<Vector2> uv0 = new List<Vector2>(), uv1 = new List<Vector2>();
        readonly List<int> tris = new List<int>();

        float travelled, crestTravelled, totalDistance, sprayDebt, strength = 1f;

        void Awake()
        {
            mesh = new Mesh { name = "Boat Wake" };
            mesh.MarkDynamic();
            GetComponent<MeshFilter>().sharedMesh = mesh;
            leftArm.width = rightArm.width = armWidth; leftArm.life = rightArm.life = armLife; leftArm.opacity = rightArm.opacity = armOpacity;
            leftArm.seed = 0.3f; rightArm.seed = 7.1f;
            band.width = bandWidth; band.life = bandLife; band.opacity = bandOpacity; band.seed = 3.7f;
            // The wake lives in world space: keep this object at the origin, unrotated.
            transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            transform.localScale = Vector3.one;
        }

        void OnDestroy()
        {
            if (mesh != null) Destroy(mesh);
        }

        void Update()
        {
            if (boat == null) { boat = BoatController.Instance; if (boat == null) return; }
            float dt = Time.deltaTime;
            float now = Time.time;
            GameManager game = GameManager.Instance;
            bool alive = game == null || game.State != GameState.GameOver;

            float tide = OceanController.Instance != null ? OceanController.Instance.tideSpeed : 6f;
            Vector3 v = boat.Velocity + boat.ExternalVelocity;
            Vector3 waterVel = new Vector3(0f, 0f, -tide);
            // Relative to the water (which flows toward -Z), the boat moves along this path.
            Vector3 throughWater = new Vector3(v.x, 0f, tide + v.z);
            float speed = throughWater.magnitude;

            if (alive && speed > 0.5f && dt > 0f)
            {
                Vector3 dir = throughWater / speed;
                Vector3 perp = new Vector3(dir.z, 0f, -dir.x);
                strength = Mathf.Clamp01(speed / fullSpeed) * 0.5f + 0.5f;
                float armSpeed = speed * Mathf.Tan(kelvinAngle * Mathf.Deg2Rad);
                Vector3 pos = boat.transform.position;
                Vector3 bow = pos + dir * bowOffset;
                Vector3 stern = pos + dir * sternOffset;

                travelled += speed * dt;
                crestTravelled += speed * dt;
                int guard = 0;
                while (travelled >= spacing && guard++ < 12)
                {
                    travelled -= spacing;
                    totalDistance += spacing;
                    float back = travelled / speed;   // how long ago this point should have been laid
                    Add(leftArm, bow - perp * halfBeam, waterVel - perp * armSpeed, now - back, back);
                    Add(rightArm, bow + perp * halfBeam, waterVel + perp * armSpeed, now - back, back);
                    Add(band, stern, waterVel, now - back, back);
                }

                // A transverse crest: a curved line across the inside of the V, bowed
                // back in the middle, whose ends spread with the arms as it ages.
                if (crestSpacing > 0f && crestTravelled >= crestSpacing)
                {
                    crestTravelled = 0f;
                    Ribbon c = crestPool.Count > 0 ? crestPool.Pop() : new Ribbon();
                    c.points.Clear();
                    c.width = crestWidth; c.life = crestLife; c.opacity = crestOpacity; c.closed = true; c.seed = Random.value * 10f;
                    Vector3 origin = pos + dir * (sternOffset - 0.4f);
                    const int n = 9;
                    for (int i = 0; i < n; i++)
                    {
                        float s = i / (n - 1f) * 2f - 1f;   // -1 .. 1 across
                        Vector3 p = origin + perp * (s * (halfBeam + 0.6f)) - dir * (s * s * 0.9f);
                        Vector3 vel = waterVel + perp * (s * armSpeed * 0.92f) - dir * (s * s * 0.6f);
                        c.points.Add(new Point { pos = p, vel = vel, born = now, dist = s * 3f });
                    }
                    crests.Add(c);
                }

                UpdateTurnSpray(v, pos, dir, waterVel, dt);
            }
            else
            {
                // No new foam: break the ribbons so they don't join across a gap.
                travelled = 0f;
            }

            Age(leftArm, dt, now); Age(rightArm, dt, now); Age(band, dt, now);
            for (int i = crests.Count - 1; i >= 0; i--)
            {
                Age(crests[i], dt, now);
                if (crests[i].points.Count == 0) { crestPool.Push(crests[i]); crests.RemoveAt(i); }
            }
            BuildMesh(now);
        }

        void Add(Ribbon r, Vector3 position, Vector3 velocity, float born, float back)
        {
            r.points.Add(new Point { pos = position + velocity * back, vel = velocity, born = born, dist = totalDistance });
        }

        // Moves every point with its velocity, puts it on the waves, drops the old ones.
        void Age(Ribbon r, float dt, float now)
        {
            OceanController ocean = OceanController.Instance;
            int dead = 0;
            for (int i = 0; i < r.points.Count; i++)
            {
                Point p = r.points[i];
                if (now - p.born > r.life) { dead = i + 1; continue; }
                p.pos += p.vel * dt;
                p.pos.y = (ocean != null ? ocean.GetWaterHeight(p.pos) : 0f) + 0.1f;
                r.points[i] = p;
            }
            if (dead > 0) r.points.RemoveRange(0, Mathf.Min(dead, r.points.Count));
        }

        void BuildMesh(float now)
        {
            verts.Clear(); colors.Clear(); uv0.Clear(); uv1.Clear(); tris.Clear();
            Strip(leftArm, now); Strip(rightArm, now); Strip(band, now);
            foreach (Ribbon c in crests) Strip(c, now);
            mesh.Clear();
            mesh.SetVertices(verts);
            mesh.SetColors(colors);
            mesh.SetUVs(0, uv0);
            mesh.SetUVs(1, uv1);
            mesh.SetTriangles(tris, 0);
            mesh.bounds = new Bounds(boat != null ? boat.transform.position : Vector3.zero, new Vector3(120f, 20f, 120f));
        }

        // A flat strip through the ribbon's points: wider and fainter with age,
        // fading in at the emitter end so it never starts with a hard edge.
        void Strip(Ribbon r, float now)
        {
            int count = r.points.Count;
            if (count < 2) return;
            int start = verts.Count;
            for (int i = 0; i < count; i++)
            {
                Point p = r.points[i];
                Vector3 a = r.points[Mathf.Max(0, i - 1)].pos, b = r.points[Mathf.Min(count - 1, i + 1)].pos;
                Vector3 tangent = b - a; tangent.y = 0f;
                if (tangent.sqrMagnitude < 1e-6f) tangent = Vector3.forward;
                Vector3 side = new Vector3(tangent.z, 0f, -tangent.x).normalized;
                float age = Mathf.Clamp01((now - p.born) / r.life);
                float w = Mathf.Lerp(r.width.x, r.width.y, Mathf.Sqrt(age)) * 0.5f;
                float fade = (1f - age) * (1f - age);
                // soften both ends of an open ribbon (the newest point is still being laid)
                float endFade = r.closed ? Mathf.Min(1f, Mathf.Min(i, count - 1 - i) / 1.5f + 0.15f) : Mathf.Min(1f, (count - 1 - i) / 2f + 0.2f);
                Color col = new Color(1f, 1f, 1f, fade * endFade * r.opacity * strength);
                verts.Add(p.pos - side * w); verts.Add(p.pos + side * w);
                colors.Add(col); colors.Add(col);
                uv0.Add(new Vector2(p.dist, 0f)); uv0.Add(new Vector2(p.dist, 1f));
                uv1.Add(new Vector2(age, r.seed)); uv1.Add(new Vector2(age, r.seed));
            }
            for (int i = 0; i < count - 1; i++)
            {
                int k = start + i * 2;
                tris.Add(k); tris.Add(k + 2); tris.Add(k + 1);
                tris.Add(k + 1); tris.Add(k + 2); tris.Add(k + 3);
            }
        }

        void UpdateTurnSpray(Vector3 v, Vector3 pos, Vector3 dir, Vector3 waterVel, float dt)
        {
            if (turnSpray == null || Mathf.Abs(v.x) <= sprayFromSideSpeed) return;
            float side = Mathf.Sign(v.x);
            sprayDebt += (Mathf.Abs(v.x) - sprayFromSideSpeed) / sprayFromSideSpeed * sprayRate * dt;
            while (sprayDebt >= 1f)
            {
                sprayDebt -= 1f;
                var e = new ParticleSystem.EmitParams();
                e.position = pos + Vector3.right * side * 0.5f + dir * Random.Range(-0.6f, 0.8f) + Vector3.up * 0.1f;
                e.velocity = waterVel * 0.6f + new Vector3(side * Random.Range(2f, 4.5f), Random.Range(2f, 4f), Random.Range(-1f, 1f));
                e.startSize = Random.Range(0.05f, 0.14f);
                e.startLifetime = Random.Range(0.4f, 0.75f);
                e.applyShapeToPosition = false;
                turnSpray.Emit(e, 1);
            }
        }
    }
}
