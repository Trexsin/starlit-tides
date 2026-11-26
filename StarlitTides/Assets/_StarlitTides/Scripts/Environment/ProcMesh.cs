// =============================================================================
//  ProcMesh - procedural low-poly geometry for Starlit Tides
// -----------------------------------------------------------------------------
//  A tiny mesh-building toolkit plus generators for everything the world needs
//  when no downloaded model is assigned: houses, market stalls, lantern posts,
//  pine / round / palm trees, rocks, the lighthouse, mountain ridges, the
//  ocean grid and the light-beam cone.
//
//  Style: FLAT SHADED (every triangle has its own vertices + face normal).
//  This gives the faceted, hand-crafted "low-poly" look that reads clearly at
//  night under coloured lights.
//
//  All generators are deterministic: same seed -> same mesh.
// =============================================================================
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace StarlitTides
{
    /// <summary>Accumulates flat-shaded triangles into several sub-meshes (one per material).</summary>
    public class MeshBuilder
    {
        private readonly List<Vector3> _verts = new List<Vector3>();
        private readonly List<Vector3> _normals = new List<Vector3>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private readonly List<List<int>> _subs = new List<List<int>>();

        public Matrix4x4 Transform = Matrix4x4.identity;   // applied to every point added

        public MeshBuilder(int subMeshCount = 1)
        {
            for (int i = 0; i < subMeshCount; i++) _subs.Add(new List<int>());
        }

        public int VertexCount => _verts.Count;

        /// <summary>Adds a triangle. If 'outward' is non-zero the winding is fixed so the face points that way.</summary>
        public void Triangle(int sub, Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc, Vector3 outward = default)
        {
            a = Transform.MultiplyPoint3x4(a);
            b = Transform.MultiplyPoint3x4(b);
            c = Transform.MultiplyPoint3x4(c);
            if (outward != Vector3.zero) outward = Transform.MultiplyVector(outward);

            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-12f) return;            // degenerate
            if (outward != Vector3.zero && Vector3.Dot(n, outward) < 0f)
            {
                (b, c) = (c, b);
                (ub, uc) = (uc, ub);
                n = -n;
            }
            n.Normalize();
            int i = _verts.Count;
            _verts.Add(a); _verts.Add(b); _verts.Add(c);
            _normals.Add(n); _normals.Add(n); _normals.Add(n);
            _uvs.Add(ua); _uvs.Add(ub); _uvs.Add(uc);
            List<int> t = _subs[sub];
            t.Add(i); t.Add(i + 1); t.Add(i + 2);
        }

        public void Triangle(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 outward = default)
        {
            Triangle(sub, a, b, c, new Vector2(a.x + a.z, a.y), new Vector2(b.x + b.z, b.y), new Vector2(c.x + c.z, c.y), outward);
        }

        /// <summary>Quad a-b-c-d (in order around the edge).</summary>
        public void Quad(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward = default, float uvW = 1f, float uvH = 1f)
        {
            if (outward == Vector3.zero) outward = Vector3.Cross(b - a, c - a);
            Triangle(sub, a, b, c, new Vector2(0, 0), new Vector2(0, uvH), new Vector2(uvW, uvH), outward);
            Triangle(sub, a, c, d, new Vector2(0, 0), new Vector2(uvW, uvH), new Vector2(uvW, 0), outward);
        }

        /// <summary>Double-sided quad (cloth, leaves, thin roofs).</summary>
        public void QuadTwoSided(int sub, Vector3 a, Vector3 b, Vector3 c, Vector3 d, float uvW = 1f, float uvH = 1f)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            Quad(sub, a, b, c, d, n, uvW, uvH);
            Quad(sub, a, d, c, b, -n, uvW, uvH);
        }

        /// <summary>Axis-aligned box (before Transform) centred on 'center'.</summary>
        public void Box(int sub, Vector3 center, Vector3 size)
        {
            Box(sub, center, size, Quaternion.identity);
        }

        public void Box(int sub, Vector3 center, Vector3 size, Quaternion rotation)
        {
            Vector3 h = size * 0.5f;
            Vector3 P(float x, float y, float z) => center + rotation * new Vector3(x * h.x, y * h.y, z * h.z);
            Vector3 R(Vector3 v) => rotation * v;
            Quad(sub, P(1, -1, -1), P(1, 1, -1), P(1, 1, 1), P(1, -1, 1), R(Vector3.right), size.z, size.y);
            Quad(sub, P(-1, -1, 1), P(-1, 1, 1), P(-1, 1, -1), P(-1, -1, -1), R(Vector3.left), size.z, size.y);
            Quad(sub, P(-1, 1, -1), P(-1, 1, 1), P(1, 1, 1), P(1, 1, -1), R(Vector3.up), size.x, size.z);
            Quad(sub, P(-1, -1, 1), P(-1, -1, -1), P(1, -1, -1), P(1, -1, 1), R(Vector3.down), size.x, size.z);
            Quad(sub, P(1, -1, 1), P(1, 1, 1), P(-1, 1, 1), P(-1, -1, 1), R(Vector3.forward), size.x, size.y);
            Quad(sub, P(-1, -1, -1), P(-1, 1, -1), P(1, 1, -1), P(1, -1, -1), R(Vector3.back), size.x, size.y);
        }

        /// <summary>Box stretched between two points (beams, planks, rails).</summary>
        public void Beam(int sub, Vector3 from, Vector3 to, float width, float height)
        {
            Vector3 dir = to - from;
            float len = dir.magnitude;
            if (len < 1e-4f) return;
            Quaternion rot = Quaternion.LookRotation(dir / len, Mathf.Abs(Vector3.Dot(dir / len, Vector3.up)) > 0.99f ? Vector3.forward : Vector3.up);
            Box(sub, (from + to) * 0.5f, new Vector3(width, height, len), rot);
        }

        /// <summary>Faceted cylinder / cone frustum along +Y from 'basePos'.</summary>
        public void Cylinder(int sub, Vector3 basePos, float radiusBottom, float radiusTop, float height, int segments, bool capTop = true, bool capBottom = false, float twistDeg = 0f)
        {
            Vector3 top = basePos + Vector3.up * height;
            for (int i = 0; i < segments; i++)
            {
                float a0 = (i / (float)segments) * Mathf.PI * 2f;
                float a1 = ((i + 1) / (float)segments) * Mathf.PI * 2f;
                float tw = twistDeg * Mathf.Deg2Rad;
                Vector3 b0 = basePos + new Vector3(Mathf.Cos(a0), 0, Mathf.Sin(a0)) * radiusBottom;
                Vector3 b1 = basePos + new Vector3(Mathf.Cos(a1), 0, Mathf.Sin(a1)) * radiusBottom;
                Vector3 t0 = top + new Vector3(Mathf.Cos(a0 + tw), 0, Mathf.Sin(a0 + tw)) * radiusTop;
                Vector3 t1 = top + new Vector3(Mathf.Cos(a1 + tw), 0, Mathf.Sin(a1 + tw)) * radiusTop;
                Vector3 outward = new Vector3(Mathf.Cos((a0 + a1) * 0.5f), 0, Mathf.Sin((a0 + a1) * 0.5f));
                if (radiusTop > 0.0001f) Quad(sub, b0, t0, t1, b1, outward, 1f, height);
                else Triangle(sub, b0, top, b1, outward + Vector3.up * 0.2f);
                if (capTop && radiusTop > 0.0001f) Triangle(sub, top, t0, t1, Vector3.up);
                if (capBottom) Triangle(sub, basePos, b0, b1, Vector3.down);
            }
        }

        public Mesh ToMesh(string name)
        {
            var mesh = new Mesh { name = name };
            if (_verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(_verts);
            mesh.SetNormals(_normals);
            mesh.SetUVs(0, _uvs);
            mesh.subMeshCount = _subs.Count;
            for (int i = 0; i < _subs.Count; i++) mesh.SetTriangles(_subs[i], i, false);
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();
            return mesh;
        }
    }

    /// <summary>Where a window was placed on a generated house (local space).</summary>
    public struct WindowSlot
    {
        public Vector3 position;
        public Quaternion rotation;   // window quad faces along rotation * Vector3.forward
        public Vector2 size;
    }

    public static class ProcMesh
    {
        // Sub-mesh indices used by the generators (the builder assigns materials in this order)
        public const int HouseWall = 0, HouseTrim = 1, HouseRoof = 2, HouseStone = 3;
        public const int StallWood = 0, StallCloth = 1;
        public const int LampMetal = 0, LampGlow = 1;
        public const int TreeBark = 0, TreeLeaves = 1;
        public const int LhWhite = 0, LhRed = 1, LhMetal = 2, LhGlass = 3;

        // ---------------------------------------------------------------------
        // House: stone base, walls with timber frame, gable roof with
        // overhang, chimney and door. Door faces local -Z (toward the viewer).
        // ---------------------------------------------------------------------
        public static Mesh House(int seed, out List<WindowSlot> windows, out float footprintRadius)
        {
            var rng = new System.Random(seed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);

            float w = R(4.6f, 6.6f);         // width (X)
            float d = R(4.0f, 5.4f);         // depth (Z)
            int floors = rng.NextDouble() < 0.45 ? 2 : 1;
            float floorH = R(2.6f, 3.0f);
            float h = floorH * floors;
            float roofH = R(1.8f, 2.8f);
            float over = 0.45f;
            float baseH = 0.6f;

            var mb = new MeshBuilder(4);
            windows = new List<WindowSlot>();
            footprintRadius = Mathf.Sqrt(w * w + d * d) * 0.5f + over;

            // stone foundation (goes 1.5 m into the ground for uneven terrain)
            mb.Box(HouseStone, new Vector3(0, baseH * 0.5f - 0.75f, 0), new Vector3(w + 0.3f, baseH + 1.5f, d + 0.3f));
            // walls
            mb.Box(HouseWall, new Vector3(0, baseH + h * 0.5f, 0), new Vector3(w, h, d));

            // timber frame: corner posts + floor beams (cheap detail that reads well)
            float t = 0.22f;
            foreach (var c in new[] { new Vector2(-1, -1), new Vector2(1, -1), new Vector2(-1, 1), new Vector2(1, 1) })
                mb.Box(HouseTrim, new Vector3(c.x * (w * 0.5f), baseH + h * 0.5f, c.y * (d * 0.5f)), new Vector3(t, h, t));
            for (int f = 0; f <= floors; f++)
            {
                float y = baseH + f * floorH;
                mb.Box(HouseTrim, new Vector3(0, y, -d * 0.5f), new Vector3(w + t, t, t));
                mb.Box(HouseTrim, new Vector3(0, y, d * 0.5f), new Vector3(w + t, t, t));
                mb.Box(HouseTrim, new Vector3(-w * 0.5f, y, 0), new Vector3(t, t, d + t));
                mb.Box(HouseTrim, new Vector3(w * 0.5f, y, 0), new Vector3(t, t, d + t));
            }

            // gable roof, ridge along X
            float eaveY = baseH + h;
            float ridgeY = eaveY + roofH;
            float hx = w * 0.5f + over, hz = d * 0.5f + over;
            float thick = 0.18f;
            Vector3 rA = new Vector3(-hx, ridgeY, 0), rB = new Vector3(hx, ridgeY, 0);
            Vector3 fA = new Vector3(-hx, eaveY - 0.25f, -hz), fB = new Vector3(hx, eaveY - 0.25f, -hz);
            Vector3 bA = new Vector3(-hx, eaveY - 0.25f, hz), bB = new Vector3(hx, eaveY - 0.25f, hz);
            mb.Quad(HouseRoof, fA, rA, rB, fB, new Vector3(0, 1, -1), w, roofH);        // front slope
            mb.Quad(HouseRoof, bB, rB, rA, bA, new Vector3(0, 1, 1), w, roofH);         // back slope
            Vector3 down = Vector3.down * thick;
            mb.Quad(HouseRoof, fA + down, fB + down, rB + down, rA + down, new Vector3(0, -1, 1));  // undersides
            mb.Quad(HouseRoof, bB + down, bA + down, rA + down, rB + down, new Vector3(0, -1, -1));
            mb.Quad(HouseRoof, fA, fB, fB + down, fA + down, Vector3.back);              // eave edges
            mb.Quad(HouseRoof, bB, bA, bA + down, bB + down, Vector3.forward);
            // gable triangles (wall colour)
            mb.Triangle(HouseWall, new Vector3(-w * 0.5f, eaveY, -d * 0.5f), new Vector3(-w * 0.5f, ridgeY - 0.1f, 0), new Vector3(-w * 0.5f, eaveY, d * 0.5f), Vector3.left);
            mb.Triangle(HouseWall, new Vector3(w * 0.5f, eaveY, -d * 0.5f), new Vector3(w * 0.5f, ridgeY - 0.1f, 0), new Vector3(w * 0.5f, eaveY, d * 0.5f), Vector3.right);

            // chimney
            float cx = R(-w * 0.3f, w * 0.3f);
            mb.Box(HouseStone, new Vector3(cx, ridgeY - 0.3f, d * 0.22f), new Vector3(0.6f, roofH * 0.9f + 0.8f, 0.6f));

            // door on the front (-Z)
            float doorX = R(-w * 0.25f, w * 0.25f);
            mb.Box(HouseTrim, new Vector3(doorX, baseH + 1.05f, -d * 0.5f - 0.04f), new Vector3(1.0f, 2.1f, 0.1f));
            mb.Box(HouseStone, new Vector3(doorX, baseH * 0.5f - 0.05f, -d * 0.5f - 0.45f), new Vector3(1.4f, baseH, 0.7f)); // step

            // windows: front (avoid the door), back, sides
            Vector2 win = new Vector2(0.85f, 1.05f);
            for (int f = 0; f < floors; f++)
            {
                float y = baseH + f * floorH + floorH * 0.55f;
                int perSide = w > 5.6f ? 2 : 1;
                for (int i = 0; i < perSide; i++)
                {
                    float x = perSide == 1 ? (doorX > 0 ? -w * 0.25f : w * 0.25f) : (i == 0 ? -w * 0.3f : w * 0.3f);
                    if (f == 0 && Mathf.Abs(x - doorX) < 1.1f) x = doorX > 0 ? doorX - 1.6f : doorX + 1.6f;
                    if (Mathf.Abs(x) > w * 0.5f - 0.6f) continue;
                    AddWindow(mb, windows, new Vector3(x, y, -d * 0.5f), Quaternion.identity, win);
                    AddWindow(mb, windows, new Vector3(-x, y, d * 0.5f), Quaternion.Euler(0, 180, 0), win);
                }
                AddWindow(mb, windows, new Vector3(-w * 0.5f, y, 0), Quaternion.Euler(0, 90, 0), win);
                AddWindow(mb, windows, new Vector3(w * 0.5f, y, 0), Quaternion.Euler(0, -90, 0), win);
            }

            return mb.ToMesh("House_" + seed);
        }

        // Window frame + sill go into the house mesh; the glowing pane is a
        // separate quad (so Living Village can switch windows on and off).
        private static void AddWindow(MeshBuilder mb, List<WindowSlot> list, Vector3 wallPos, Quaternion rot, Vector2 size)
        {
            Vector3 outDir = rot * Vector3.back;   // window faces outward along local -Z
            Vector3 right = rot * Vector3.right;
            float fw = 0.09f;
            Vector3 c = wallPos + outDir * 0.05f;
            Quaternion frameRot = Quaternion.LookRotation(outDir);
            mb.Box(HouseTrim, c + Vector3.up * (size.y * 0.5f), new Vector3(size.x + fw * 2, fw, fw), frameRot);                       // lintel
            mb.Box(HouseTrim, c - Vector3.up * (size.y * 0.5f) + outDir * 0.06f, new Vector3(size.x + fw * 4, fw * 1.4f, fw * 3), frameRot); // sill
            mb.Box(HouseTrim, c + right * (size.x * 0.5f), new Vector3(fw, size.y, fw), frameRot);
            mb.Box(HouseTrim, c - right * (size.x * 0.5f), new Vector3(fw, size.y, fw), frameRot);
            mb.Box(HouseTrim, c + outDir * 0.03f, new Vector3(fw * 0.6f, size.y, fw * 0.6f), frameRot);   // mullion
            list.Add(new WindowSlot { position = wallPos + outDir * 0.02f, rotation = Quaternion.LookRotation(-outDir), size = size });
        }

        // ---------------------------------------------------------------------
        // Market stall: four posts, counter, sloped striped awning with a
        // scalloped front edge. Front faces -Z. Returns the lantern hook point.
        // ---------------------------------------------------------------------
        public static Mesh MarketStall(int seed, out Vector3 lanternHook)
        {
            var rng = new System.Random(seed);
            float w = 2.8f + (float)rng.NextDouble() * 0.8f, d = 1.8f, hFront = 2.5f, hBack = 2.9f;
            var mb = new MeshBuilder(2);
            float p = 0.12f;
            mb.Box(StallWood, new Vector3(-w / 2, hFront / 2, -d / 2), new Vector3(p, hFront, p));
            mb.Box(StallWood, new Vector3(w / 2, hFront / 2, -d / 2), new Vector3(p, hFront, p));
            mb.Box(StallWood, new Vector3(-w / 2, hBack / 2, d / 2), new Vector3(p, hBack, p));
            mb.Box(StallWood, new Vector3(w / 2, hBack / 2, d / 2), new Vector3(p, hBack, p));
            mb.Box(StallWood, new Vector3(0, 0.95f, -d / 2 + 0.25f), new Vector3(w, 0.1f, 0.6f));     // counter top
            mb.Box(StallWood, new Vector3(0, 0.47f, -d / 2 + 0.05f), new Vector3(w, 0.9f, 0.06f));    // counter front
            mb.Box(StallWood, new Vector3(0, 1.4f, d / 2), new Vector3(w, 2.0f, 0.05f));              // back board
            // goods: little crates and baskets on the counter
            int n = 3 + rng.Next(3);
            for (int i = 0; i < n; i++)
            {
                float x = -w / 2 + 0.4f + i * (w - 0.8f) / Mathf.Max(1, n - 1);
                float s = 0.25f + (float)rng.NextDouble() * 0.15f;
                mb.Box(StallCloth, new Vector3(x, 1.0f + s / 2, -d / 2 + 0.25f), new Vector3(s * 1.3f, s, s), Quaternion.Euler(0, (float)rng.NextDouble() * 30f - 15f, 0));
            }
            // awning
            Vector3 a = new Vector3(-w / 2 - 0.2f, hFront + 0.05f, -d / 2 - 0.5f);
            Vector3 b = new Vector3(-w / 2 - 0.2f, hBack + 0.1f, d / 2 + 0.1f);
            Vector3 c = new Vector3(w / 2 + 0.2f, hBack + 0.1f, d / 2 + 0.1f);
            Vector3 e = new Vector3(w / 2 + 0.2f, hFront + 0.05f, -d / 2 - 0.5f);
            mb.QuadTwoSided(StallCloth, a, b, c, e, 1f, 1f);
            // scalloped valance along the front
            int scallops = 6;
            for (int i = 0; i < scallops; i++)
            {
                float x0 = Mathf.Lerp(a.x, e.x, i / (float)scallops);
                float x1 = Mathf.Lerp(a.x, e.x, (i + 1) / (float)scallops);
                Vector3 t0 = new Vector3(x0, a.y, a.z), t1 = new Vector3(x1, a.y, a.z);
                Vector3 m = new Vector3((x0 + x1) * 0.5f, a.y - 0.28f, a.z);
                mb.Triangle(StallCloth, t0, t1, m, Vector3.back);
                mb.Triangle(StallCloth, t0, m, t1, Vector3.forward);
            }
            lanternHook = new Vector3(0, hFront - 0.35f, -d / 2 + 0.1f);
            return mb.ToMesh("Stall_" + seed);
        }

        // ---------------------------------------------------------------------
        // Lantern post with a curled arm and a hanging lamp box.
        // Returns the lamp centre (for the point light).
        // ---------------------------------------------------------------------
        public static Mesh LanternPost(float height, out Vector3 lampCenter)
        {
            var mb = new MeshBuilder(2);
            mb.Cylinder(LampMetal, Vector3.zero, 0.16f, 0.12f, 0.35f, 6);                 // base
            mb.Cylinder(LampMetal, Vector3.up * 0.35f, 0.07f, 0.06f, height - 0.35f, 6);   // pole
            mb.Beam(LampMetal, new Vector3(0, height - 0.15f, 0), new Vector3(0.55f, height - 0.05f, 0), 0.06f, 0.06f); // arm
            Vector3 lamp = new Vector3(0.55f, height - 0.55f, 0);
            mb.Box(LampMetal, lamp + Vector3.up * 0.27f, new Vector3(0.36f, 0.06f, 0.36f));                         // lamp cap
            mb.Cylinder(LampMetal, lamp + Vector3.up * 0.3f, 0.2f, 0.0f, 0.18f, 4, false);                         // lamp roof
            mb.Box(LampGlow, lamp, new Vector3(0.26f, 0.42f, 0.26f));                                              // glowing glass
            mb.Box(LampMetal, lamp - Vector3.up * 0.24f, new Vector3(0.32f, 0.05f, 0.32f));                         // lamp base
            lampCenter = lamp;
            return mb.ToMesh("LanternPost");
        }

        /// <summary>Small hanging lantern (for stalls and the pier).</summary>
        public static Mesh HangingLantern()
        {
            var mb = new MeshBuilder(2);
            mb.Beam(LampMetal, new Vector3(0, 0.35f, 0), new Vector3(0, 0.12f, 0), 0.02f, 0.02f);
            mb.Cylinder(LampMetal, new Vector3(0, 0.1f, 0), 0.13f, 0.0f, 0.1f, 6, false);
            mb.Cylinder(LampGlow, new Vector3(0, -0.15f, 0), 0.1f, 0.11f, 0.25f, 6, true, true);
            return mb.ToMesh("HangingLantern");
        }

        // ---------------------------------------------------------------------
        // Trees
        // ---------------------------------------------------------------------
        public static Mesh PineTree(int seed)
        {
            var rng = new System.Random(seed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var mb = new MeshBuilder(2);
            float height = R(7f, 12f);
            mb.Cylinder(TreeBark, Vector3.down * 0.5f, 0.32f, 0.18f, height * 0.4f + 0.5f, 6);
            int tiers = 4;
            float y = height * 0.22f;
            float radius = height * 0.28f;
            for (int i = 0; i < tiers; i++)
            {
                float tierH = height * (0.42f - i * 0.05f);
                mb.Cylinder(TreeLeaves, new Vector3(R(-0.1f, 0.1f), y, R(-0.1f, 0.1f)), radius, 0f, tierH, 7, false, true, R(0, 25));
                y += tierH * 0.48f;
                radius *= 0.74f;
            }
            return mb.ToMesh("Pine_" + seed);
        }

        public static Mesh RoundTree(int seed)
        {
            var rng = new System.Random(seed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var mb = new MeshBuilder(2);
            float trunkH = R(2.2f, 3.4f);
            mb.Cylinder(TreeBark, Vector3.down * 0.4f, 0.28f, 0.2f, trunkH + 0.6f, 6);
            int blobs = 3;
            for (int i = 0; i < blobs; i++)
            {
                Vector3 c = new Vector3(R(-0.9f, 0.9f), trunkH + 1.4f + R(0f, 1.2f), R(-0.9f, 0.9f));
                float r = R(1.5f, 2.3f);
                Icosphere(mb, TreeLeaves, c, r, 1, seed * 7 + i, 0.18f);
            }
            return mb.ToMesh("RoundTree_" + seed);
        }

        public static Mesh PalmTree(int seed)
        {
            var rng = new System.Random(seed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var mb = new MeshBuilder(2);
            float height = R(5.5f, 8.5f);
            float lean = R(0.8f, 2.2f);
            int segs = 7;
            Vector3 prev = Vector3.down * 0.4f;
            for (int i = 1; i <= segs; i++)
            {
                float t = i / (float)segs;
                Vector3 p = new Vector3(lean * t * t, height * t, 0);
                float r = Mathf.Lerp(0.26f, 0.16f, t);
                // trunk ring segments slightly wider at each joint (palm look)
                AddTube(mb, TreeBark, prev, p, r * 1.12f, r, 6);
                prev = p;
            }
            Vector3 top = prev;
            int leaves = 8;
            for (int i = 0; i < leaves; i++)
            {
                float ang = i / (float)leaves * Mathf.PI * 2f + R(0, 0.3f);
                Vector3 dir = new Vector3(Mathf.Cos(ang), 0, Mathf.Sin(ang));
                Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
                float len = R(2.8f, 3.8f);
                int parts = 5;
                for (int j = 0; j < parts; j++)
                {
                    float t0 = j / (float)parts, t1 = (j + 1) / (float)parts;
                    Vector3 a0 = top + dir * len * t0 + Vector3.up * (0.6f * t0 - 1.9f * t0 * t0);
                    Vector3 a1 = top + dir * len * t1 + Vector3.up * (0.6f * t1 - 1.9f * t1 * t1);
                    float w0 = Mathf.Sin(Mathf.PI * Mathf.Lerp(0.15f, 1f, t0)) * 0.55f;
                    float w1 = Mathf.Sin(Mathf.PI * Mathf.Lerp(0.15f, 1f, t1)) * 0.55f;
                    // slight V shape: edges droop below the spine
                    mb.QuadTwoSided(TreeLeaves, a0 - side * w0 + Vector3.down * w0 * 0.3f, a0, a1, a1 - side * w1 + Vector3.down * w1 * 0.3f);
                    mb.QuadTwoSided(TreeLeaves, a0, a0 + side * w0 + Vector3.down * w0 * 0.3f, a1 + side * w1 + Vector3.down * w1 * 0.3f, a1);
                }
            }
            // coconuts
            for (int i = 0; i < 3; i++)
                Icosphere(mb, TreeBark, top + new Vector3(R(-0.3f, 0.3f), -0.3f, R(-0.3f, 0.3f)), 0.18f, 0, seed + i, 0f);
            return mb.ToMesh("Palm_" + seed);
        }

        private static void AddTube(MeshBuilder mb, int sub, Vector3 a, Vector3 b, float ra, float rb, int segs)
        {
            Vector3 axis = (b - a).normalized;
            Vector3 u = Vector3.Cross(axis, Vector3.forward).normalized;
            if (u.sqrMagnitude < 0.01f) u = Vector3.Cross(axis, Vector3.right).normalized;
            Vector3 v = Vector3.Cross(axis, u);
            for (int i = 0; i < segs; i++)
            {
                float a0 = i / (float)segs * Mathf.PI * 2f, a1 = (i + 1) / (float)segs * Mathf.PI * 2f;
                Vector3 d0 = u * Mathf.Cos(a0) + v * Mathf.Sin(a0);
                Vector3 d1 = u * Mathf.Cos(a1) + v * Mathf.Sin(a1);
                mb.Quad(sub, a + d0 * ra, b + d0 * rb, b + d1 * rb, a + d1 * ra, (d0 + d1));
            }
        }

        // ---------------------------------------------------------------------
        // Icosphere with noise displacement - rocks and leaf blobs.
        // ---------------------------------------------------------------------
        public static void Icosphere(MeshBuilder mb, int sub, Vector3 center, float radius, int subdivisions, int seed, float roughness, Vector3 scale = default)
        {
            if (scale == default) scale = Vector3.one;
            float t = (1f + Mathf.Sqrt(5f)) / 2f;
            var verts = new List<Vector3>
            {
                new Vector3(-1, t, 0), new Vector3(1, t, 0), new Vector3(-1, -t, 0), new Vector3(1, -t, 0),
                new Vector3(0, -1, t), new Vector3(0, 1, t), new Vector3(0, -1, -t), new Vector3(0, 1, -t),
                new Vector3(t, 0, -1), new Vector3(t, 0, 1), new Vector3(-t, 0, -1), new Vector3(-t, 0, 1)
            };
            var faces = new List<int[]>
            {
                new[]{0,11,5}, new[]{0,5,1}, new[]{0,1,7}, new[]{0,7,10}, new[]{0,10,11},
                new[]{1,5,9}, new[]{5,11,4}, new[]{11,10,2}, new[]{10,7,6}, new[]{7,1,8},
                new[]{3,9,4}, new[]{3,4,2}, new[]{3,2,6}, new[]{3,6,8}, new[]{3,8,9},
                new[]{4,9,5}, new[]{2,4,11}, new[]{6,2,10}, new[]{8,6,7}, new[]{9,8,1}
            };
            for (int i = 0; i < verts.Count; i++) verts[i] = verts[i].normalized;

            for (int s = 0; s < subdivisions; s++)
            {
                var cache = new Dictionary<long, int>();
                int Mid(int a, int b)
                {
                    long key = a < b ? ((long)a << 32) | (uint)b : ((long)b << 32) | (uint)a;
                    if (cache.TryGetValue(key, out int idx)) return idx;
                    verts.Add(((verts[a] + verts[b]) * 0.5f).normalized);
                    cache[key] = verts.Count - 1;
                    return verts.Count - 1;
                }
                var next = new List<int[]>();
                foreach (var f in faces)
                {
                    int a = Mid(f[0], f[1]), b = Mid(f[1], f[2]), c = Mid(f[2], f[0]);
                    next.Add(new[] { f[0], a, c }); next.Add(new[] { f[1], b, a });
                    next.Add(new[] { f[2], c, b }); next.Add(new[] { a, b, c });
                }
                faces = next;
            }

            // displace each vertex along its direction by smooth noise
            float ox = seed * 13.37f, oy = seed * 7.13f;
            var displaced = new Vector3[verts.Count];
            for (int i = 0; i < verts.Count; i++)
            {
                Vector3 v = verts[i];
                float n = Mathf.PerlinNoise(v.x * 1.7f + ox, v.y * 1.7f + v.z * 0.9f + oy) * 2f - 1f;
                float r = radius * (1f + n * roughness);
                displaced[i] = center + Vector3.Scale(v * r, scale);
            }
            foreach (var f in faces)
            {
                Vector3 a = displaced[f[0]], b = displaced[f[1]], c = displaced[f[2]];
                mb.Triangle(sub, a, b, c, ((a + b + c) / 3f) - center);
            }
        }

        public static Mesh Rock(int seed, float radius = 1f, float roughness = 0.35f, Vector3 scale = default)
        {
            var mb = new MeshBuilder(1);
            Icosphere(mb, 0, Vector3.zero, radius, 1, seed, roughness, scale == default ? new Vector3(1.2f, 0.75f, 1f) : scale);
            return mb.ToMesh("Rock_" + seed);
        }

        // ---------------------------------------------------------------------
        // Lighthouse: striped tapered tower, gallery, glass lamp room, cap.
        // Returns the lamp height (for the rotating light).
        // ---------------------------------------------------------------------
        public static Mesh Lighthouse(out float lampHeight)
        {
            var mb = new MeshBuilder(4);
            int seg = 14;
            float towerH = 16f, r0 = 2.7f, r1 = 1.8f;
            int stripes = 6;
            mb.Cylinder(LhMetal, Vector3.down * 2f, r0 + 0.6f, r0 + 0.4f, 2.6f, seg);             // rock-anchored base
            for (int i = 0; i < stripes; i++)
            {
                float y0 = 0.6f + towerH * i / stripes, y1 = 0.6f + towerH * (i + 1) / stripes;
                float ra = Mathf.Lerp(r0, r1, (float)i / stripes), rb = Mathf.Lerp(r0, r1, (float)(i + 1) / stripes);
                mb.Cylinder(i % 2 == 0 ? LhWhite : LhRed, Vector3.up * y0, ra, rb, y1 - y0, seg, false);
            }
            float top = 0.6f + towerH;
            mb.Cylinder(LhMetal, Vector3.up * top, r1 + 0.9f, r1 + 0.9f, 0.3f, seg, true, true);      // gallery floor
            for (int i = 0; i < 16; i++)                                                               // railing posts
            {
                float a = i / 16f * Mathf.PI * 2f;
                Vector3 p = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * (r1 + 0.8f) + Vector3.up * (top + 0.3f);
                mb.Box(LhMetal, p + Vector3.up * 0.5f, new Vector3(0.06f, 1f, 0.06f));
            }
            mb.Cylinder(LhMetal, Vector3.up * (top + 1.25f), r1 + 0.82f, r1 + 0.82f, 0.08f, seg, true, true); // rail ring
            mb.Cylinder(LhGlass, Vector3.up * (top + 0.3f), 1.35f, 1.35f, 2.2f, 10, false);           // lamp room glass
            mb.Cylinder(LhMetal, Vector3.up * (top + 2.5f), 1.7f, 0.15f, 1.7f, 10, false, true);      // cap
            mb.Cylinder(LhMetal, Vector3.up * (top + 4.1f), 0.18f, 0.0f, 0.8f, 6, false);             // spire
            // door
            mb.Box(LhMetal, new Vector3(0, 1.6f, -r0 + 0.1f), new Vector3(1.0f, 2.0f, 0.3f));
            lampHeight = top + 1.4f;
            return mb.ToMesh("Lighthouse");
        }

        // ---------------------------------------------------------------------
        // Open cone along +Z: apex at origin, wide end at 'length'.
        // UV.y = 0 at the apex, 1 at the end (what the beam shader expects).
        // ---------------------------------------------------------------------
        public static Mesh BeamCone(float length, float endRadius, float startRadius = 0.25f, int segments = 24)
        {
            var verts = new List<Vector3>();
            var normals = new List<Vector3>();
            var uvs = new List<Vector2>();
            var tris = new List<int>();
            for (int i = 0; i <= segments; i++)
            {
                float a = i / (float)segments * Mathf.PI * 2f;
                Vector3 dir = new Vector3(Mathf.Cos(a), Mathf.Sin(a), 0);
                verts.Add(dir * startRadius);
                verts.Add(dir * endRadius + Vector3.forward * length);
                Vector3 n = (dir - Vector3.forward * ((endRadius - startRadius) / length)).normalized;
                normals.Add(n); normals.Add(n);
                uvs.Add(new Vector2(i / (float)segments, 0)); uvs.Add(new Vector2(i / (float)segments, 1));
            }
            for (int i = 0; i < segments; i++)
            {
                int k = i * 2;
                tris.Add(k); tris.Add(k + 1); tris.Add(k + 3);
                tris.Add(k); tris.Add(k + 3); tris.Add(k + 2);
            }
            var mesh = new Mesh { name = "BeamCone" };
            mesh.SetVertices(verts); mesh.SetNormals(normals); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            return mesh;
        }

        // ---------------------------------------------------------------------
        // Ocean grid: dense near the play area, coarse far away (vertices are
        // spaced with a power curve). One mesh covers the whole horizon.
        // ---------------------------------------------------------------------
        public static Mesh OceanGrid(float halfExtent, int resolution, float power)
        {
            int n = resolution + 1;
            var verts = new Vector3[n * n];
            for (int z = 0; z < n; z++)
            for (int x = 0; x < n; x++)
            {
                float u = x / (float)resolution * 2f - 1f;
                float v = z / (float)resolution * 2f - 1f;
                float px = Mathf.Sign(u) * Mathf.Pow(Mathf.Abs(u), power) * halfExtent;
                float pz = Mathf.Sign(v) * Mathf.Pow(Mathf.Abs(v), power) * halfExtent;
                verts[z * n + x] = new Vector3(px, 0, pz);
            }
            var tris = new int[resolution * resolution * 6];
            int t = 0;
            for (int z = 0; z < resolution; z++)
            for (int x = 0; x < resolution; x++)
            {
                int i = z * n + x;
                tris[t++] = i; tris[t++] = i + n; tris[t++] = i + n + 1;
                tris[t++] = i; tris[t++] = i + n + 1; tris[t++] = i + 1;
            }
            var mesh = new Mesh { name = "OceanGrid", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = verts;
            mesh.triangles = tris;
            var normals = new Vector3[verts.Length];
            for (int i = 0; i < normals.Length; i++) normals[i] = Vector3.up;
            mesh.normals = normals;
            // Waves move vertices up/down in the shader: give generous bounds so it is never culled.
            mesh.bounds = new Bounds(Vector3.zero, new Vector3(halfExtent * 2f, 20f, halfExtent * 2f));
            return mesh;
        }

        // ---------------------------------------------------------------------
        // Mountain ridge: a long faceted wall of peaks along X, facing -Z.
        // 'envelope' scales height along X (lets us keep the moon side open).
        // ---------------------------------------------------------------------
        public static Mesh MountainRidge(int seed, float xMin, float xMax, float peakHeight, float depth, int points, System.Func<float, float> envelope)
        {
            var mb = new MeshBuilder(1);
            float ox = seed * 31.7f;
            Vector3[] mid = new Vector3[points + 1];
            Vector3[] top = new Vector3[points + 1];
            Vector3[] bas = new Vector3[points + 1];
            for (int i = 0; i <= points; i++)
            {
                float x = Mathf.Lerp(xMin, xMax, i / (float)points);
                // ridged noise: sharp peaks instead of round hills
                float n = 0f, amp = 1f, freq = 0.0035f, norm = 0f;
                for (int o = 0; o < 4; o++)
                {
                    float r = 1f - Mathf.Abs(Mathf.PerlinNoise(x * freq + ox, o * 11.3f + seed) * 2f - 1f);
                    n += r * r * amp; norm += amp; amp *= 0.5f; freq *= 2.1f;
                }
                n /= norm;
                float h = Mathf.Max(peakHeight * n * envelope(x), 4f);
                float jitter = (Mathf.PerlinNoise(x * 0.05f, seed) - 0.5f) * depth * 0.3f;
                bas[i] = new Vector3(x, -20f, 0f);
                mid[i] = new Vector3(x, h * 0.5f, depth * 0.35f + jitter);
                top[i] = new Vector3(x, h, depth * 0.7f);
            }
            for (int i = 0; i < points; i++)
            {
                mb.Quad(0, bas[i], mid[i], mid[i + 1], bas[i + 1], Vector3.back + Vector3.up * 0.3f);
                mb.Quad(0, mid[i], top[i], top[i + 1], mid[i + 1], Vector3.back + Vector3.up);
            }
            return mb.ToMesh("Mountains_" + seed);
        }
    }
}
