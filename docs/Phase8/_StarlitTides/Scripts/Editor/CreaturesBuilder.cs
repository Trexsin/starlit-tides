// CreaturesBuilder: makes the glowing sea-creature obstacles and adds them to
// the obstacle spawner.
// Menu: Starlit Tides > 10. Build Sea Creatures   (run 5. Build Gameplay first)
//       Starlit Tides > Rebuild Everything         (steps 4-8, 10 and 11 in one go)
//
// Three creatures, all procedural meshes built here (no imported models):
//   Manta ray   - a lofted wing surface (top + belly), cephalic fins, whip tail
//   Sea turtle  - domed shell with glowing scute seams, head, four flippers
//   Dolphin     - a lofted body with dorsal fin, pectoral fins and flukes
// Every vertex gets animation masks in its colour (see StarlitSeaCreature.shader):
//   R = fin weight, G = body wave, B = glow mask, A = fin phase.

#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace StarlitTides.EditorTools
{
    public static class CreaturesBuilder
    {
        const string Gen = "Assets/_StarlitTides/Generated/Creatures";
        const string PrefabDir = "Assets/_StarlitTides/Prefabs/Gameplay";

        [MenuItem("Starlit Tides/10. Build Sea Creatures", priority = 25)]
        public static void BuildMenu()
        {
            if (Object.FindAnyObjectByType<ObstacleSpawner>() == null)
            {
                EditorUtility.DisplayDialog("Starlit Tides", "Build the gameplay first (Starlit Tides > 5. Build Gameplay).", "OK");
                return;
            }
            Build();
        }

        [MenuItem("Starlit Tides/Rebuild Everything", priority = 30)]
        public static void RebuildEverything()
        {
            if (!Application.isBatchMode && !EditorUtility.DisplayDialog("Rebuild Starlit Tides",
                    "Rebuilds the environment, gameplay, features, bonus features, UI, sea creatures and the sea field (steps 4-8, 10 and 11).\n\nThis takes a minute or two.",
                    "Rebuild", "Cancel"))
                return;
            if (!UIBuilder.EnsureTextMeshPro()) return;
            EnvironmentBuilder.Build();
            GameplayBuilder.Build();
            FeaturesBuilder.Build();
            BonusBuilder.Build();
            UIBuilder.Build();
            Build();
            SeaFieldBuilder.Build();   // Phase 8: scenery beyond the lane + horizon islands
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            AssetDatabase.SaveAssets();
            EditorAssetUtil.CheckGeneratedAssets();   // catches materials saved without their textures
            Debug.Log("[Starlit Tides] Everything rebuilt and the scene saved.");
        }

        public static void Build()
        {
            EnsureFolder(Gen);
            EnsureFolder(PrefabDir);

            GameObject manta = SavePrefab(BuildManta(), PrefabDir + "/MantaRay.prefab");
            GameObject turtle = SavePrefab(BuildTurtle(), PrefabDir + "/SeaTurtle.prefab");
            GameObject dolphin = SavePrefab(BuildDolphin(), PrefabDir + "/Dolphin.prefab");
            GameObject orca = SavePrefab(BuildOrca(), PrefabDir + "/Orca.prefab");
            GameObject narwhal = SavePrefab(BuildNarwhal(), PrefabDir + "/Narwhal.prefab");
            GameObject nautilus = SavePrefab(BuildNautilus(), PrefabDir + "/Nautilus.prefab");

            ObstacleSpawner spawner = Object.FindAnyObjectByType<ObstacleSpawner>();
            if (spawner != null)
            {
                spawner.creaturePrefabs = new[] { manta, turtle, dolphin, orca, narwhal, nautilus };
                EditorUtility.SetDirty(spawner);
            }

            // Scenery: a humpback whale that breaches far out at sea now and then.
            const string ambientName = "Ambient Humpback (Phase 7)";
            GameObject oldAmbient = GameObject.Find(ambientName);
            if (oldAmbient != null) Object.DestroyImmediate(oldAmbient);
            GameObject ambient = new GameObject(ambientName);
            AmbientWhale breacher = ambient.AddComponent<AmbientWhale>();
            breacher.whale = BuildHumpback(ambient.transform).transform;
            breacher.splashVfx = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/_StarlitTides/Prefabs/VFX/VFX_WhaleSplash.prefab");

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[Starlit Tides] Sea creatures built: manta ray, sea turtle, dolphin, orca, narwhal, nautilus + a breaching humpback.");
        }

        // ==============================================================
        // Mesh building helper: smooth parts with per-vertex masks
        // ==============================================================
        class CreatureMesh
        {
            public readonly List<Vector3> verts = new List<Vector3>();
            public readonly List<Color> colors = new List<Color>();
            public readonly List<Vector2> uvs = new List<Vector2>();
            public readonly List<int> tris = new List<int>();

            public int Add(Vector3 p, Color c, Vector2 uv)
            {
                verts.Add(p); colors.Add(c); uvs.Add(uv);
                return verts.Count - 1;
            }

            // Makes every triangle added since 'firstTri' face away from 'centre'.
            public void Orient(int firstTri, Vector3 centre)
            {
                int good = 0, bad = 0;
                for (int i = firstTri; i < tris.Count; i += 3)
                {
                    Vector3 a = verts[tris[i]], b = verts[tris[i + 1]], c = verts[tris[i + 2]];
                    float d = Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c) / 3f - centre);
                    if (d >= 0f) good++; else bad++;
                }
                if (bad <= good) return;
                for (int i = firstTri; i < tris.Count; i += 3)
                {
                    int t = tris[i + 1]; tris[i + 1] = tris[i + 2]; tris[i + 2] = t;
                }
            }

            // A (nu x nv) grid of vertices from f(u, v), u and v in 0..1.
            public void Grid(int nu, int nv, System.Func<float, float, (Vector3 p, Color c, Vector2 uv)> f, Vector3 centre)
            {
                int start = verts.Count, firstTri = tris.Count;
                for (int j = 0; j <= nv; j++)
                for (int i = 0; i <= nu; i++)
                {
                    var v = f(i / (float)nu, j / (float)nv);
                    Add(v.p, v.c, v.uv);
                }
                for (int j = 0; j < nv; j++)
                for (int i = 0; i < nu; i++)
                {
                    int a = start + j * (nu + 1) + i, b = a + 1, c = a + nu + 1, d = c + 1;
                    tris.Add(a); tris.Add(c); tris.Add(b);
                    tris.Add(b); tris.Add(c); tris.Add(d);
                }
                Orient(firstTri, centre);
            }

            // A smooth ellipsoid (optionally only part of it), rotated and placed.
            public void Ellipsoid(Vector3 centre, Vector3 radii, Quaternion rotation, int rings, int sides,
                                  System.Func<Vector3, Color> color, float latFrom = -90f, float latTo = 90f,
                                  System.Func<Vector3, Vector2> uv = null)
            {
                Grid(sides, rings, (u, v) =>
                {
                    float lat = Mathf.Lerp(latFrom, latTo, v) * Mathf.Deg2Rad;
                    float lon = u * Mathf.PI * 2f;
                    Vector3 local = new Vector3(Mathf.Cos(lat) * Mathf.Cos(lon) * radii.x, Mathf.Sin(lat) * radii.y, Mathf.Cos(lat) * Mathf.Sin(lon) * radii.z);
                    Vector3 p = centre + rotation * local;
                    Vector2 t = uv != null ? uv(p) : new Vector2(u, v);
                    return (p, color(p), t);
                }, centre);
            }

            // A thin fin: a triangle (rootA, rootB, tip) given some thickness.
            public void Fin(Vector3 rootA, Vector3 rootB, Vector3 tip, Vector3 thicknessAxis, float thickness, System.Func<Vector3, Color> color, Vector2 uv)
            {
                int firstTri = tris.Count;
                Vector3 off = thicknessAxis.normalized * thickness * 0.5f;
                Vector3[] p = { rootA + off, rootB + off, tip + off * 0.2f, rootA - off, rootB - off, tip - off * 0.2f };
                int[] idx = new int[6];
                for (int i = 0; i < 6; i++) idx[i] = Add(p[i], color(p[i]), uv);
                int[] faces = { 0, 1, 2, 3, 5, 4, 0, 3, 1, 1, 3, 4, 1, 4, 2, 2, 4, 5, 2, 5, 0, 0, 5, 3 };
                foreach (int f in faces) tris.Add(idx[f]);
                // Orient each face on its own (a fin is too thin for one centre).
                Vector3 centre = (rootA + rootB + tip) / 3f;
                for (int i = firstTri; i < tris.Count; i += 3)
                {
                    Vector3 a = verts[tris[i]], b = verts[tris[i + 1]], c = verts[tris[i + 2]];
                    if (Vector3.Dot(Vector3.Cross(b - a, c - a), (a + b + c) / 3f - centre) < 0f)
                    { int t = tris[i + 1]; tris[i + 1] = tris[i + 2]; tris[i + 2] = t; }
                }
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name };
                if (verts.Count > 65000) mesh.indexFormat = IndexFormat.UInt32;
                mesh.SetVertices(verts);
                mesh.SetColors(colors);
                mesh.SetUVs(0, uvs);
                mesh.SetTriangles(tris, 0);
                mesh.RecalculateNormals();
                SmoothSeams(mesh);
                mesh.RecalculateBounds();
                // Room for the fin and tail animation done in the shader.
                Bounds b = mesh.bounds; b.Expand(1.2f); mesh.bounds = b;
                return mesh;
            }

            // Grid seams (where a loop closes) have duplicate vertices with different
            // normals; average them when they point roughly the same way.
            static void SmoothSeams(Mesh mesh)
            {
                Vector3[] v = mesh.vertices, n = mesh.normals;
                var groups = new Dictionary<Vector3Int, List<int>>();
                for (int i = 0; i < v.Length; i++)
                {
                    Vector3Int key = Vector3Int.RoundToInt(v[i] * 2000f);
                    if (!groups.TryGetValue(key, out var list)) groups[key] = list = new List<int>();
                    list.Add(i);
                }
                var result = (Vector3[])n.Clone();
                foreach (var list in groups.Values)
                {
                    if (list.Count < 2) continue;
                    foreach (int i in list)
                    {
                        Vector3 sum = Vector3.zero;
                        foreach (int j in list) if (Vector3.Dot(n[i], n[j]) > 0.2f) sum += n[j];
                        if (sum.sqrMagnitude > 1e-8f) result[i] = sum.normalized;
                    }
                }
                mesh.normals = result;
            }
        }

        static Color Mask(float fin, float wave, float glow, float phase = 0f)
        {
            return new Color(Mathf.Clamp01(fin), Mathf.Clamp01(wave), Mathf.Clamp01(glow), phase);
        }

        // ==============================================================
        // Manta ray (head toward +Z)
        // ==============================================================
        static Mesh MantaMesh()
        {
            const float length = 2.3f, halfSpan = 2.3f, thickness = 0.3f;
            var m = new CreatureMesh();

            // Max(0, ...): sin(PI) is a tiny negative number, and Pow of a negative is NaN.
            float Width(float v) => halfSpan * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Pow(v, 0.68f))), 0.8f)
                                    + 0.32f * (1f - v) * Mathf.Clamp01(v / 0.04f);
            Vector3 Surface(float u, float v, bool top)
            {
                float au = Mathf.Abs(u);
                float w = Width(v);
                float x = u * w;
                // Wings sweep back toward the tips and curl down a little.
                float z = length * (0.5f - v) - Mathf.Pow(au, 1.7f) * w * 0.42f;
                float body = Mathf.Pow(Mathf.Clamp01(1f - u * u), 1.6f) * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Clamp01(v * 1.04f))), 0.6f);
                float y = (top ? thickness : -thickness * 0.55f) * body - 0.14f * au * au * au;
                return new Vector3(x, y, z);
            }

            foreach (bool top in new[] { true, false })
            {
                m.Grid(44, 30, (s, v) =>
                {
                    float u = s * 2f - 1f;
                    Vector3 p = Surface(u, v, top);
                    Color c = Mask(Mathf.Pow(Mathf.Abs(u), 1.4f), 0f, top ? 1f : 0.3f);
                    return (p, c, new Vector2(s, v));
                }, new Vector3(0f, top ? -1f : 1f, 0f));
            }

            // Cephalic fins: two curled lobes either side of the mouth.
            foreach (float side in new[] { -1f, 1f })
            {
                m.Ellipsoid(new Vector3(side * 0.3f, -0.02f, length * 0.5f + 0.1f), new Vector3(0.07f, 0.035f, 0.26f),
                    Quaternion.Euler(0f, -side * 14f, side * 30f), 8, 10, p => Mask(0.05f, 0f, 0.4f));
            }

            // Whip tail: a thin tapering tube that waves sideways (G mask).
            const int segs = 14, sides = 6;
            const float tailLength = 1.9f;
            int start = m.verts.Count, firstTri = m.tris.Count;
            for (int i = 0; i <= segs; i++)
            {
                float t = i / (float)segs;
                float r = Mathf.Lerp(0.05f, 0.006f, t);
                float z = -length * 0.5f + 0.15f - t * tailLength;
                for (int k = 0; k <= sides; k++)
                {
                    float a = k / (float)sides * Mathf.PI * 2f;
                    m.Add(new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, z), Mask(0f, 0.3f + t * 0.7f, 0.6f), new Vector2(0.5f, 1f));
                }
            }
            for (int i = 0; i < segs; i++)
            for (int k = 0; k < sides; k++)
            {
                int a = start + i * (sides + 1) + k, b = a + 1, c = a + sides + 1, d = c + 1;
                m.tris.Add(a); m.tris.Add(c); m.tris.Add(b);
                m.tris.Add(b); m.tris.Add(c); m.tris.Add(d);
            }
            // Orient each tail ring segment outward from the tail's axis.
            for (int i = firstTri; i < m.tris.Count; i += 3)
            {
                Vector3 a = m.verts[m.tris[i]], b = m.verts[m.tris[i + 1]], c = m.verts[m.tris[i + 2]];
                Vector3 centroid = (a + b + c) / 3f;
                Vector3 axis = new Vector3(0f, 0f, centroid.z);
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), centroid - axis) < 0f)
                { int tmp = m.tris[i + 1]; m.tris[i + 1] = m.tris[i + 2]; m.tris[i + 2] = tmp; }
            }

            return SaveAsset(m.ToMesh("MantaRay"), Gen + "/MantaRay.asset");
        }

        // Shared set-up for every creature obstacle. Sizes are in metres at real-world
        // scale (the paper boat is ~3 m): a 7 m manta, 4 m dolphins, a 7 m orca...
        static SeaCreature Setup(GameObject root, Transform visual, Collider col, SeaCreature.Behaviour behaviour,
                                 float radius, float damage, float floatOffset, Vector2 crossSpeed,
                                 Color lightColor, float lightIntensity, float lightRange, float glowRadius)
        {
            col.isTrigger = true;
            var c = root.AddComponent<SeaCreature>();
            c.type = ObstacleType.SeaCreature; c.behaviour = behaviour;
            c.radius = radius; c.lightDamage = damage; c.visual = visual; c.hitCollider = col;
            c.floatOffset = floatOffset; c.bobAmplitude = 0.05f; c.bobSpeed = 1.4f; c.followWaves = 0.8f;
            c.crossSpeed = crossSpeed; c.leapChance = 0f;
            c.glowLight = AddLight(root.transform, new Vector3(0f, 1f, 0f), lightColor, lightIntensity, lightRange);
            var d = root.AddComponent<WaterDisturber>();
            d.radius = glowRadius; d.strength = 0.9f; d.maxHeightAboveWater = 2.5f;
            return c;
        }

        static void Stylize(Material m, float iridescence, float toon = 0.6f)
        {
            m.SetFloat("_Iridescence", iridescence);
            m.SetFloat("_Toon", toon);
        }

        // ==============================================================
        // Manta ray: 8 m wingspan, deep indigo with magenta spots
        // ==============================================================
        static GameObject BuildManta()
        {
            Material mat = CreatureMaterial("M_MantaRay", new Color(0.06f, 0.03f, 0.18f), new Color(0.78f, 0.74f, 0.9f),
                new Color(2.6f, 0.45f, 3.4f), new Color(0.25f, 1.6f, 2.6f));
            mat.SetFloat("_SpotStrength", 1.8f);
            mat.SetVector("_SpotScale", new Vector4(16f, 9f, 0f, 0f));
            mat.SetFloat("_SpotDensity", 0.55f);
            mat.SetFloat("_EdgeGlow", 1.6f);
            mat.SetFloat("_FinSpeed", 1.5f);
            mat.SetFloat("_FinLift", 0.55f);
            mat.SetFloat("_FinSweep", 0.08f);
            mat.SetFloat("_FinLag", 0.6f);
            mat.SetFloat("_SwimAmount", 0.3f);
            mat.SetFloat("_SwimFreq", 1.6f);
            mat.SetFloat("_SwimSpeed", 4f);
            mat.SetVector("_SwimAxis", new Vector4(1f, 0f, 0f, 0f));
            Stylize(mat, 0.3f);

            const float scale = 1.75f;   // an 8 m wingspan: the top end of a giant oceanic manta
            GameObject root = Creature("MantaRay", MantaMesh(), mat, scale, out Transform visual);
            var col = root.AddComponent<BoxCollider>();
            col.size = new Vector3(3.8f, 0.9f, 2.0f) * scale; col.center = new Vector3(0f, 0.15f, 0f);
            var c = Setup(root, visual, col, SeaCreature.Behaviour.MantaGlide, 3.4f, 0.25f, 0.04f, new Vector2(0.4f, 2.6f),
                new Color(0.9f, 0.45f, 1f), 2.8f, 10f, 4.4f);
            c.weave = 1.6f; c.weaveFrequency = 0.18f;
            c.leapChance = 0.45f; c.leapHeight = 4.2f; c.leapDuration = 1.8f; c.safeHeight = 1.8f;
            return root;
        }

        // ==============================================================
        // Sea turtle (head toward +Z)
        // ==============================================================
        static Mesh TurtleMesh()
        {
            var m = new CreatureMesh();
            // Shell dome: scute seams use a flat top-down UV.
            System.Func<Vector3, Vector2> shellUV = p => new Vector2(p.x / 1.6f + 0.5f, p.z / 2.0f + 0.5f);
            m.Ellipsoid(new Vector3(0f, 0.04f, 0f), new Vector3(0.76f, 0.4f, 1.0f), Quaternion.identity, 14, 28,
                p => Mask(0f, 0f, 1f), 0f, 90f, shellUV);
            // Plastron (belly plate): a flatter lower half.
            m.Ellipsoid(new Vector3(0f, 0.04f, 0f), new Vector3(0.74f, 0.15f, 0.97f), Quaternion.identity, 6, 28,
                p => Mask(0f, 0f, 0f), -90f, 0f);
            // Neck and head.
            m.Ellipsoid(new Vector3(0f, 0.0f, 0.92f), new Vector3(0.17f, 0.13f, 0.26f), Quaternion.Euler(-8f, 0f, 0f), 8, 12, p => Mask(0f, 0f, 0.35f));
            m.Ellipsoid(new Vector3(0f, 0.04f, 1.17f), new Vector3(0.2f, 0.16f, 0.27f), Quaternion.Euler(-12f, 0f, 0f), 10, 14, p => Mask(0f, 0f, 0.5f));
            // Flippers: long swept front paddles and small rear ones. They alternate (phase A).
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 front = new Vector3(side * 0.98f, -0.02f, 0.42f);
                m.Ellipsoid(front, new Vector3(0.82f, 0.05f, 0.21f), Quaternion.Euler(0f, side * 28f, -side * 6f), 6, 16,
                    p => Mask((Mathf.Abs(p.x) - 0.5f) / 1.0f, 0f, 0.55f, 0f));
                Vector3 rear = new Vector3(side * 0.62f, -0.05f, -0.86f);
                m.Ellipsoid(rear, new Vector3(0.34f, 0.04f, 0.17f), Quaternion.Euler(0f, -side * 38f, 0f), 6, 12,
                    p => Mask((Mathf.Abs(p.x) - 0.35f) / 0.5f, 0f, 0.4f, 0.5f));
            }
            // Short tail.
            m.Ellipsoid(new Vector3(0f, -0.02f, -1.02f), new Vector3(0.08f, 0.06f, 0.18f), Quaternion.identity, 6, 8, p => Mask(0f, 0.4f, 0f));
            return SaveAsset(m.ToMesh("SeaTurtle"), Gen + "/SeaTurtle.asset");
        }

        static GameObject BuildTurtle()
        {
            Material mat = CreatureMaterial("M_SeaTurtle", new Color(0.03f, 0.15f, 0.11f), new Color(0.75f, 0.62f, 0.36f),
                new Color(1.2f, 3.0f, 0.9f), new Color(0.3f, 1.6f, 1.1f));
            mat.SetFloat("_SeamStrength", 2.0f);
            mat.SetFloat("_SeamScale", 4.6f);
            mat.SetFloat("_SpotStrength", 1.0f);
            mat.SetVector("_SpotScale", new Vector4(22f, 22f, 0f, 0f));
            mat.SetFloat("_SpotDensity", 0.3f);
            mat.SetFloat("_Gloss", 140f);
            mat.SetFloat("_SpecStrength", 1.6f);
            mat.SetFloat("_FinSpeed", 1.6f);
            mat.SetFloat("_FinLift", 0.32f);
            mat.SetFloat("_FinSweep", 0.32f);
            mat.SetFloat("_FinLag", 0.6f);
            mat.SetFloat("_SwimAmount", 0.04f);
            Stylize(mat, 0.45f);

            const float scale = 1.3f;   // a 2.8 m leatherback-sized turtle
            GameObject root = Creature("SeaTurtle", TurtleMesh(), mat, scale, out Transform visual);
            var col = root.AddComponent<SphereCollider>();
            col.radius = 1.05f * scale; col.center = new Vector3(0f, 0.1f, 0f);
            var c = Setup(root, visual, col, SeaCreature.Behaviour.TurtlePaddle, 1.7f, 0.25f, -0.24f, new Vector2(0.3f, 1.5f),
                new Color(0.5f, 1f, 0.6f), 1.8f, 5.5f, 2.4f);
            c.weave = 0.6f; c.weaveFrequency = 0.18f;
            return root;
        }

        // ==============================================================
        // Cetaceans: one lofted-body generator for the dolphin, orca,
        // narwhal and humpback whale (head toward +Z, sizes in metres)
        // ==============================================================
        struct CetaceanShape
        {
            public string name;
            public float length, radius;
            public float beak;          // 0 = round head, 0.12 = dolphin beak
            public float dorsalHeight, dorsalZ;
            public float pectoralLength, flukeSpan;
            public float tuskLength;    // narwhal
        }

        static Mesh CetaceanMesh(CetaceanShape sh)
        {
            float L = sh.length, R = sh.radius;
            var m = new CreatureMesh();
            float Radius(float t)   // t: 0 = tail, 1 = snout
            {
                // A smaller exponent rounds the head off (orca, narwhal, humpback).
                float body = Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * Mathf.Pow(t, 0.62f))), sh.beak > 0f ? 0.9f : 0.42f);
                float grow = Mathf.Clamp01(t / 0.38f); grow = grow * grow * (3f - 2f * grow);
                float r = R * body * Mathf.Lerp(0.16f, 1f, grow);
                if (sh.beak > 0f && t > 1f - sh.beak) r *= Mathf.Lerp(1f, 0.55f, (t - (1f - sh.beak)) / sh.beak);
                return Mathf.Max(r, 0.012f);
            }
            m.Grid(24, 48, (s, t) =>
            {
                float a = s * Mathf.PI * 2f;
                float r = Radius(t);
                float z = -L * 0.5f + t * L;
                float y = Mathf.Sin(a) * r * 1.02f;
                if (y < 0f) y *= 0.92f;
                if (sh.beak > 0f) y += R * 0.14f * Mathf.Exp(-Mathf.Pow((t - 0.84f) / 0.05f, 2f)) * Mathf.Max(0f, Mathf.Sin(a));   // melon
                return (new Vector3(Mathf.Cos(a) * r * 0.9f, y, z), Mask(0f, 1f - t, 1f), new Vector2(s, t));
            }, Vector3.zero);
            int bodyTris = m.tris.Count;
            for (int i = 0; i < bodyTris; i += 3)
            {
                Vector3 a = m.verts[m.tris[i]], b = m.verts[m.tris[i + 1]], c = m.verts[m.tris[i + 2]];
                Vector3 centroid = (a + b + c) / 3f;
                if (Vector3.Dot(Vector3.Cross(b - a, c - a), centroid - new Vector3(0f, 0f, centroid.z)) < 0f)
                { int tmp = m.tris[i + 1]; m.tris[i + 1] = m.tris[i + 2]; m.tris[i + 2] = tmp; }
            }

            Vector2 finUV = new Vector2(0.25f, 0.55f);
            float th = L * 0.018f;
            if (sh.dorsalHeight > 0f)
                m.Fin(new Vector3(0f, R * 0.88f, L * (sh.dorsalZ + 0.08f)), new Vector3(0f, R * 0.72f, L * (sh.dorsalZ - 0.12f)),
                      new Vector3(0f, R * 0.85f + sh.dorsalHeight, L * (sh.dorsalZ - 0.17f)), Vector3.right, th, p => Mask(0f, 0.35f, 0.6f), finUV);
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 rootA = new Vector3(side * R * 0.66f, -R * 0.4f, L * 0.22f), rootB = new Vector3(side * R * 0.66f, -R * 0.4f, L * 0.12f);
                Vector3 tip = new Vector3(side * (R * 0.66f + sh.pectoralLength), -R * 0.4f - sh.pectoralLength * 0.45f, L * 0.05f);
                m.Fin(rootA, rootB, tip, Vector3.up, th, p => Mask(Mathf.Abs(p.x) > R ? 0.25f : 0f, 0.2f, 0.5f), finUV);
                m.Fin(new Vector3(0f, 0f, -L * 0.46f), new Vector3(0f, 0f, -L * 0.51f), new Vector3(side * sh.flukeSpan * 0.5f, 0f, -L * 0.5f - sh.flukeSpan * 0.36f),
                      Vector3.up, th, p => Mask(0f, 1f, 0.6f), finUV);
            }

            if (sh.tuskLength > 0f)
            {
                // Narwhal tusk: a long spiral cone. UV.x winds round it, so the stripe
                // pattern becomes glowing spiral grooves.
                const int segs = 24, sides = 8;
                int start = m.verts.Count;
                Vector3 baseP = new Vector3(0f, R * 0.08f, L * 0.5f - R * 0.1f);
                for (int i = 0; i <= segs; i++)
                {
                    float t = i / (float)segs;
                    float r = Mathf.Lerp(L * 0.02f, L * 0.002f, t);
                    for (int k = 0; k <= sides; k++)
                    {
                        float a = k / (float)sides * Mathf.PI * 2f;
                        Vector3 p = baseP + new Vector3(Mathf.Cos(a) * r, Mathf.Sin(a) * r, t * sh.tuskLength);
                        m.Add(p, Mask(0f, 0f, 1f), new Vector2(k / (float)sides + t * 7f, 0.5f));
                    }
                }
                int firstTri = m.tris.Count;
                for (int i = 0; i < segs; i++)
                for (int k = 0; k < sides; k++)
                {
                    int a = start + i * (sides + 1) + k, b = a + 1, c = a + sides + 1, d = c + 1;
                    m.tris.Add(a); m.tris.Add(c); m.tris.Add(b);
                    m.tris.Add(b); m.tris.Add(c); m.tris.Add(d);
                }
                for (int i = firstTri; i < m.tris.Count; i += 3)
                {
                    Vector3 a = m.verts[m.tris[i]], b = m.verts[m.tris[i + 1]], c = m.verts[m.tris[i + 2]];
                    Vector3 centroid = (a + b + c) / 3f;
                    Vector3 axis = new Vector3(baseP.x, baseP.y, centroid.z);
                    if (Vector3.Dot(Vector3.Cross(b - a, c - a), centroid - axis) < 0f)
                    { int tmp = m.tris[i + 1]; m.tris[i + 1] = m.tris[i + 2]; m.tris[i + 2] = tmp; }
                }
            }
            return SaveAsset(m.ToMesh(sh.name), Gen + "/" + sh.name + ".asset");
        }

        static void SwimMaterial(Material mat, float swimAmount, float swimFreq, float swimSpeed)
        {
            mat.SetFloat("_FinSpeed", 2.4f);
            mat.SetFloat("_FinLift", 0.08f);
            mat.SetFloat("_FinSweep", 0.03f);
            mat.SetFloat("_SwimAmount", swimAmount);
            mat.SetFloat("_SwimFreq", swimFreq);
            mat.SetFloat("_SwimSpeed", swimSpeed);
            mat.SetVector("_SwimAxis", new Vector4(0f, 1f, 0f, 0f));   // flukes beat up and down
        }

        // Dolphin: 4 m, ocean blue with racing cyan flank lines
        static GameObject BuildDolphin()
        {
            Material mat = CreatureMaterial("M_Dolphin", new Color(0.04f, 0.13f, 0.32f), new Color(0.82f, 0.86f, 0.92f),
                new Color(0.3f, 2.4f, 3.6f), new Color(0.3f, 1.2f, 2.4f));
            mat.SetFloat("_StripeStrength", 2.2f);
            mat.SetFloat("_StripeWidth", 0.03f);
            mat.SetFloat("_SpotStrength", 0.8f);
            mat.SetVector("_SpotScale", new Vector4(18f, 30f, 0f, 0f));
            mat.SetFloat("_SpotDensity", 0.25f);
            mat.SetFloat("_Gloss", 200f);
            mat.SetFloat("_SpecStrength", 2.2f);
            SwimMaterial(mat, 0.22f, 1.5f, 7f);
            Stylize(mat, 0.6f);

            var shape = new CetaceanShape { name = "Dolphin", length = 4f, radius = 0.55f, beak = 0.12f, dorsalHeight = 0.55f, dorsalZ = 0.04f, pectoralLength = 0.6f, flukeSpan = 1.1f };
            GameObject root = Creature("Dolphin", CetaceanMesh(shape), mat, 1f, out Transform visual);
            var col = root.AddComponent<CapsuleCollider>();
            col.direction = 2; col.radius = 0.6f; col.height = 3.8f;
            var c = Setup(root, visual, col, SeaCreature.Behaviour.DolphinLeap, 1.8f, 0.2f, -0.4f, new Vector2(0.6f, 3f),
                new Color(0.45f, 0.75f, 1f), 1.8f, 6f, 2.2f);
            c.weave = 0.4f; c.weaveFrequency = 0.3f;
            c.leapHeight = 2.7f; c.leapDuration = 1.3f; c.safeHeight = 1.4f;
            return root;
        }

        // Orca (killer whale): 9 m bull, glossy black and white with violet glow lines
        static GameObject BuildOrca()
        {
            Material mat = CreatureMaterial("M_Orca", new Color(0.012f, 0.012f, 0.025f), new Color(0.9f, 0.92f, 0.96f),
                new Color(1.6f, 0.6f, 3.6f), new Color(0.6f, 0.4f, 2.2f));
            mat.SetFloat("_BellyLow", -0.12f);
            mat.SetFloat("_BellyHigh", 0.02f);       // a crisp black/white line, not a blend
            mat.SetFloat("_StripeStrength", 1.6f);
            mat.SetFloat("_StripeWidth", 0.02f);
            mat.SetFloat("_SpotStrength", 0.6f);
            mat.SetVector("_SpotScale", new Vector4(20f, 40f, 0f, 0f));
            mat.SetFloat("_SpotDensity", 0.2f);
            mat.SetFloat("_Gloss", 260f);
            mat.SetFloat("_SpecStrength", 2.6f);
            SwimMaterial(mat, 0.35f, 0.9f, 5f);
            Stylize(mat, 0.5f, 0.7f);

            var shape = new CetaceanShape { name = "Orca", length = 7f, radius = 1.05f, beak = 0f, dorsalHeight = 1.7f, dorsalZ = 0.02f, pectoralLength = 1.3f, flukeSpan = 2.2f };
            // Real-world scale: a big adult bull orca, ~9 m (the mesh is 7 m, x1.3).
            const float orcaScale = 1.3f;
            GameObject root = Creature("Orca", CetaceanMesh(shape), mat, orcaScale, out Transform visual);
            var col = root.AddComponent<CapsuleCollider>();
            col.direction = 2; col.radius = 1.0f * orcaScale; col.height = 6.6f * orcaScale;
            var c = Setup(root, visual, col, SeaCreature.Behaviour.DolphinLeap, 3.2f * orcaScale, 0.3f, -0.75f * orcaScale, new Vector2(0.5f, 2.6f),
                new Color(0.7f, 0.5f, 1f), 2.6f, 11f, 3.6f * orcaScale);
            c.weave = 0.3f; c.weaveFrequency = 0.2f;
            c.leapHeight = 4.4f; c.leapDuration = 2.1f; c.safeHeight = 2.5f;
            return root;
        }

        // Narwhal: 5.5 m body with a 2.9 m glowing golden spiral tusk
        static GameObject BuildNarwhal()
        {
            Material mat = CreatureMaterial("M_Narwhal", new Color(0.16f, 0.2f, 0.3f), new Color(0.88f, 0.9f, 0.95f),
                new Color(3.4f, 2.3f, 0.6f), new Color(0.4f, 1.4f, 2.4f));
            mat.SetFloat("_StripeStrength", 2.4f);    // spiral grooves on the tusk
            mat.SetFloat("_StripeWidth", 0.06f);
            mat.SetFloat("_SpotStrength", 1.4f);
            mat.SetVector("_SpotScale", new Vector4(24f, 34f, 0f, 0f));
            mat.SetFloat("_SpotDensity", 0.5f);
            mat.SetFloat("_Gloss", 180f);
            SwimMaterial(mat, 0.22f, 1.2f, 5.5f);
            Stylize(mat, 0.8f);

            var shape = new CetaceanShape { name = "Narwhal", length = 4.5f, radius = 0.72f, beak = 0f, dorsalHeight = 0.08f, dorsalZ = 0.02f, pectoralLength = 0.55f, flukeSpan = 1.3f, tuskLength = 2.4f };
            // Real-world scale: a large adult, 5.5 m body with a ~2.9 m tusk (the mesh is 4.5 m, x1.22).
            const float narScale = 1.22f;
            GameObject root = Creature("Narwhal", CetaceanMesh(shape), mat, narScale, out Transform visual);
            var col = root.AddComponent<CapsuleCollider>();
            col.direction = 2; col.radius = 0.75f * narScale; col.height = 6.2f * narScale; col.center = new Vector3(0f, 0f, 1.1f * narScale);
            var c = Setup(root, visual, col, SeaCreature.Behaviour.MantaGlide, 2.4f * narScale, 0.25f, -0.32f * narScale, new Vector2(0.5f, 2.2f),
                new Color(1f, 0.85f, 0.45f), 2.4f, 8f, 2.6f * narScale);
            c.weave = 0.8f; c.weaveFrequency = 0.16f;
            return root;
        }

        // Humpback whale: 18 m, long pectoral fins. Scenery: it breaches out at sea.
        static GameObject BuildHumpback(Transform parent)
        {
            Material mat = CreatureMaterial("M_Humpback", new Color(0.05f, 0.07f, 0.14f), new Color(0.82f, 0.85f, 0.9f),
                new Color(0.4f, 2.4f, 3.4f), new Color(0.3f, 1.3f, 2.2f));
            mat.SetFloat("_SpotStrength", 1.4f);
            mat.SetVector("_SpotScale", new Vector4(26f, 60f, 0f, 0f));
            mat.SetFloat("_SpotDensity", 0.4f);
            mat.SetFloat("_StripeStrength", 0.8f);
            mat.SetFloat("_StripeWidth", 0.02f);
            SwimMaterial(mat, 0.6f, 0.4f, 2.5f);
            mat.SetFloat("_FinLift", 0.6f);
            mat.SetFloat("_FinSpeed", 1.2f);
            Stylize(mat, 0.4f);

            var shape = new CetaceanShape { name = "Humpback", length = 16f, radius = 2.1f, beak = 0f, dorsalHeight = 0.5f, dorsalZ = -0.12f, pectoralLength = 4.8f, flukeSpan = 5.2f };
            // Real-world scale: a big adult humpback, ~18 m (the mesh is 16 m, x1.125).
            GameObject whale = Creature("Humpback Whale", CetaceanMesh(shape), mat, 1.125f, out Transform visual);
            whale.transform.SetParent(parent, false);
            AddLight(whale.transform, new Vector3(0f, 2f, 0f), new Color(0.4f, 0.8f, 1f), 3f, 26f);
            var d = whale.AddComponent<WaterDisturber>();
            d.radius = 7f; d.strength = 1.2f; d.maxHeightAboveWater = 11f;
            whale.SetActive(false);
            return whale;
        }

        // ==============================================================
        // Nautilus: a glowing, tiger-striped spiral shell (1.9 m across)
        // ==============================================================
        static Mesh NautilusMesh()
        {
            var m = new CreatureMesh();
            // A logarithmic spiral tube: radius grows as e^(b*theta). The shell stands
            // upright (spiral in the Y-Z plane), the opening facing +Z.
            const int segs = 90, sides = 18;
            const float turns = 2.1f, a0 = 0.06f, b = 0.18f;
            float thetaMax = turns * Mathf.PI * 2f;
            int start = m.verts.Count;
            for (int i = 0; i <= segs; i++)
            {
                float t = i / (float)segs;
                float theta = t * thetaMax;
                float r = a0 * Mathf.Exp(b * theta);
                Vector3 centre = new Vector3(0f, Mathf.Sin(theta) * r, -Mathf.Cos(theta) * r);
                Vector3 radial = new Vector3(0f, Mathf.Sin(theta), -Mathf.Cos(theta));
                float tube = r * 0.62f;
                for (int k = 0; k <= sides; k++)
                {
                    float a = k / (float)sides * Mathf.PI * 2f;
                    Vector3 p = centre + (radial * Mathf.Cos(a) + Vector3.right * Mathf.Sin(a) * 0.82f) * tube;
                    // UV.x = position along the spiral: the stripe pattern makes tiger bands.
                    m.Add(p, Mask(0f, 0f, 1f), new Vector2(theta / Mathf.PI * 1.3f, k / (float)sides));
                }
            }
            int firstTri = m.tris.Count;
            for (int i = 0; i < segs; i++)
            for (int k = 0; k < sides; k++)
            {
                int a = start + i * (sides + 1) + k, b2 = a + 1, c = a + sides + 1, d = c + 1;
                m.tris.Add(a); m.tris.Add(c); m.tris.Add(b2);
                m.tris.Add(b2); m.tris.Add(c); m.tris.Add(d);
            }
            // Orient each quad away from its own ring centre.
            for (int i = firstTri; i < m.tris.Count; i += 3)
            {
                int ring = (m.tris[i] - start) / (sides + 1);
                float theta = ring / (float)segs * thetaMax;
                float r = a0 * Mathf.Exp(b * theta);
                Vector3 centre = new Vector3(0f, Mathf.Sin(theta) * r, -Mathf.Cos(theta) * r);
                Vector3 pa = m.verts[m.tris[i]], pb = m.verts[m.tris[i + 1]], pc = m.verts[m.tris[i + 2]];
                if (Vector3.Dot(Vector3.Cross(pb - pa, pc - pa), (pa + pb + pc) / 3f - centre) < 0f)
                { int tmp = m.tris[i + 1]; m.tris[i + 1] = m.tris[i + 2]; m.tris[i + 2] = tmp; }
            }
            // A soft body and a fringe of glowing tentacles peeking out of the opening.
            float rEnd = a0 * Mathf.Exp(b * thetaMax);
            Vector3 mouth = new Vector3(0f, Mathf.Sin(thetaMax) * rEnd, -Mathf.Cos(thetaMax) * rEnd);
            m.Ellipsoid(mouth + new Vector3(0f, 0f, 0.1f), new Vector3(rEnd * 0.45f, rEnd * 0.45f, rEnd * 0.3f), Quaternion.identity, 8, 12, p => Mask(0f, 0f, 0.5f));
            for (int i = 0; i < 14; i++)
            {
                float ang = i / 14f * Mathf.PI * 2f;
                Vector3 baseP = mouth + new Vector3(Mathf.Cos(ang) * rEnd * 0.35f, Mathf.Sin(ang) * rEnd * 0.35f, 0.15f);
                m.Ellipsoid(baseP + new Vector3(0f, 0f, 0.22f), new Vector3(0.025f, 0.025f, 0.26f), Quaternion.Euler(Mathf.Sin(ang) * 25f, Mathf.Cos(ang) * 25f, 0f), 4, 6,
                    p => Mask(0.6f, 0f, 1f, i / 14f));
            }
            return SaveAsset(m.ToMesh("Nautilus"), Gen + "/Nautilus.asset");
        }

        static GameObject BuildNautilus()
        {
            Material mat = CreatureMaterial("M_Nautilus", new Color(0.92f, 0.82f, 0.66f), new Color(0.95f, 0.9f, 0.82f),
                new Color(3.6f, 1.1f, 0.35f), new Color(1.6f, 0.9f, 0.4f));
            mat.SetFloat("_StripeStrength", 2.6f);   // glowing tiger bands
            mat.SetFloat("_StripeWidth", 0.09f);
            mat.SetFloat("_SpotStrength", 0f);
            mat.SetFloat("_Gloss", 320f);
            mat.SetFloat("_SpecStrength", 2.4f);     // polished nacre
            mat.SetFloat("_FinSpeed", 2.2f);
            mat.SetFloat("_FinLift", 0.05f);
            mat.SetFloat("_FinSweep", 0.06f);
            mat.SetFloat("_SwimAmount", 0f);
            mat.SetFloat("_PulseSpeed", 1.1f);
            Stylize(mat, 1.3f, 0.4f);

            const float scale = 1.9f;   // a giant, ~3 m glowing nautilus
            GameObject root = Creature("Nautilus", NautilusMesh(), mat, scale, out Transform visual);
            visual.localPosition = new Vector3(0f, 0.35f, 0f);
            var col = root.AddComponent<SphereCollider>();
            col.radius = 1.5f; col.center = new Vector3(0f, 0.7f, 0f);
            var c = Setup(root, visual, col, SeaCreature.Behaviour.TurtlePaddle, 2.0f, 0.25f, -0.45f, new Vector2(0.3f, 1.3f),
                new Color(1f, 0.6f, 0.3f), 2.2f, 6f, 2.2f);
            c.bobAmplitude = 0.18f; c.bobSpeed = 1.1f; c.weave = 0.5f; c.weaveFrequency = 0.2f;
            return root;
        }

        // ==============================================================
        // Shared helpers
        // ==============================================================
        static GameObject Creature(string name, Mesh mesh, Material mat, float scale, out Transform visual)
        {
            GameObject root = new GameObject(name);
            visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            visual.localScale = Vector3.one * scale;
            GameObject body = new GameObject("Body");
            body.transform.SetParent(visual, false);
            body.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer r = body.AddComponent<MeshRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.lightProbeUsage = LightProbeUsage.BlendProbes;
            return root;
        }

        static Material CreatureMaterial(string name, Color back, Color belly, Color glow, Color rim)
        {
            Shader shader = Shader.Find("StarlitTides/SeaCreature");
            if (shader == null) throw new System.Exception("[Starlit Tides] Shader 'StarlitTides/SeaCreature' not found.");
            Material m = new Material(shader) { name = name };
            m.SetColor("_BackColor", back);
            m.SetColor("_BellyColor", belly);
            m.SetColor("_GlowColor", glow);
            m.SetColor("_RimColor", rim);
            m.enableInstancing = true;
            return SaveAsset(m, Gen + "/" + name + ".mat");
        }

        static Light AddLight(Transform parent, Vector3 localPos, Color color, float intensity, float range)
        {
            var go = new GameObject("Glow Light");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color; l.intensity = intensity; l.range = range;
            l.shadows = LightShadows.None;
            return l;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        static T SaveAsset<T>(T asset, string path) where T : Object
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static GameObject SavePrefab(GameObject temp, string path)
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temp, path);
            Object.DestroyImmediate(temp);
            return prefab;
        }
    }
}
#endif
