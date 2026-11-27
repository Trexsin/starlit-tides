// =============================================================================
//  STARLIT TIDES - Phase 8, Step 4: the sea beyond the lane
// -----------------------------------------------------------------------------
//  Menu:  Starlit Tides > 11. Build Sea Field   (also part of Rebuild Everything)
//  Run 5. Build Gameplay first (it makes the glow texture and halo quad).
//
//  Makes scenery prefabs (all procedural, no imported models) and adds
//  "== STARLIT SEA FIELD ==" to the scene:
//    * AmbientSeaField spawner with three bands of drifting scenery:
//        glowing algae blooms, floating paper lanterns, rock clusters,
//        sea stacks with little pines on top, a natural rock arch,
//        a fishing boat with a lantern, a whale surfacing with a spout
//    * set pieces: a ring of lanterns, a glowing coral arch
//    * static HORIZON ISLANDS (silhouettes 550-900 m out) with a far
//      lighthouse light and two ship lights
//  Running it again rebuilds everything inside "== STARLIT SEA FIELD ==".
// =============================================================================
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
    public static class SeaFieldBuilder
    {
        const string RootName = "== STARLIT SEA FIELD ==";
        const string Gen = "Assets/_StarlitTides/Generated/SeaField";
        const string PrefabDir = "Assets/_StarlitTides/Prefabs/SeaField";
        const string GameplayGen = "Assets/_StarlitTides/Generated/Gameplay";

        static Mesh _quad, _lanternMesh;
        static Texture2D _dot;
        static Material _rock, _leaves, _bark, _wood, _woodDark, _lanternPaper, _lanternCap, _whaleSkin, _island;
        static Material _glowMint, _glowCyan, _glowWarm, _glowViolet, _glowSpray, _glowWarmSmall;

        [MenuItem("Starlit Tides/11. Build Sea Field", priority = 26)]
        public static void BuildMenu()
        {
            if (Object.FindAnyObjectByType<ObstacleSpawner>() == null)
            {
                EditorUtility.DisplayDialog("Starlit Tides", "Build the gameplay first (Starlit Tides > 5. Build Gameplay).", "OK");
                return;
            }
            Build();
        }

        public static void Build()
        {
            EnsureFolder(Gen); EnsureFolder(PrefabDir);
            GameObject old = GameObject.Find(RootName);
            if (old != null) Object.DestroyImmediate(old);
            Transform root = new GameObject(RootName).transform;

            _quad = AssetDatabase.LoadAssetAtPath<Mesh>(GameplayGen + "/HaloQuad.asset");
            _dot = EditorAssetUtil.LoadTexture(GameplayGen + "/T_GlowDot.png");
            CreateMaterials();
            _lanternMesh = SaveMesh(LanternMesh(), "PaperLantern.asset");   // shared by every lantern prefab (save once)

            // ---- drifting scenery ----
            GameObject algaeMint = Save(BuildAlgae("AlgaeBloom_Mint", _glowMint, 11));
            GameObject algaeCyan = Save(BuildAlgae("AlgaeBloom_Cyan", _glowCyan, 23));
            GameObject lanterns = Save(BuildLanternCluster("LanternCluster", 4, 1.6f, 31));
            GameObject rocks = Save(BuildRockCluster("RockCluster", 41));
            GameObject stackA = Save(BuildSeaStack("SeaStack_A", 51, 4f, 14f, 3));
            GameObject stackB = Save(BuildSeaStack("SeaStack_B", 52, 5.5f, 19f, 5));
            GameObject stackC = Save(BuildSeaStack("SeaStack_C", 53, 3.2f, 10f, 2));
            GameObject arch = Save(BuildArch("RockArch", 61, null));
            GameObject boat = Save(BuildFishingBoat("FishingBoat", 71));
            GameObject whale = Save(BuildWhaleSpout("WhaleSpout", 81));
            GameObject stackBig = Save(BuildSeaStack("SeaStack_Giant", 54, 9f, 34f, 7));

            // ---- set pieces ----
            GameObject ring = Save(BuildLanternCluster("SetPiece_LanternRing", 12, 6f, 91, true));
            GameObject glowArch = Save(BuildArch("SetPiece_CoralArch", 62, _glowViolet));

            var go = new GameObject("Ambient Sea Field");
            go.transform.SetParent(root, false);
            var field = go.AddComponent<AmbientSeaField>();
            field.bands = new[]
            {
                new AmbientSeaField.Band { name = "Near (both sides)", prefabs = new[] { algaeMint, algaeCyan, lanterns, rocks, algaeMint },
                                           distance = new Vector2(28f, 50f), spacing = 15f, spawnZ = 190f },
                // (fewer stacks than first tried: too many read as a fence of pillars)
                new AmbientSeaField.Band { name = "Mid (open sea, right)", prefabs = new[] { stackA, stackC, arch, boat, boat, lanterns },
                                           distance = new Vector2(55f, 92f), spacing = 26f, left = false, spawnZ = 230f },
                new AmbientSeaField.Band { name = "Mid (toward the coast, left)", prefabs = new[] { boat, algaeCyan, lanterns, rocks },
                                           distance = new Vector2(55f, 92f), spacing = 30f, right = false, spawnZ = 230f },
                new AmbientSeaField.Band { name = "Far (open sea, right)", prefabs = new[] { stackBig, whale, whale, stackB },
                                           distance = new Vector2(98f, 145f), spacing = 44f, left = false, spawnZ = 280f },
                // Left = toward the coast: no tall stacks here, they would stand in front
                // of the lighthouse cliff and the village in the menu view.
                new AmbientSeaField.Band { name = "Far (toward the coast, left)", prefabs = new[] { whale, boat },
                                           distance = new Vector2(98f, 145f), spacing = 52f, right = false, spawnZ = 280f },
            };
            field.setPieces = new[] { ring, glowArch };

            BuildHorizon(root);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[Starlit Tides] Sea field built: drifting scenery in 4 bands, 2 set pieces and the horizon islands.");
        }

        // ==================================================================
        // MATERIALS
        // ==================================================================
        static void CreateMaterials()
        {
            _rock = SaveMat(StylizedMaterials.Create("M_SeaRock", new Color(0.2f, 0.21f, 0.27f), 0.3f, StylizedMaterials.Kind.Natural));
            StylizedMaterials.SetAccent(_rock, new Color(0.15f, 0.26f, 0.25f));                     // moss on top
            _leaves = SaveMat(StylizedMaterials.Create("M_StackPine", new Color(0.06f, 0.17f, 0.14f), 0.05f, StylizedMaterials.Kind.Foliage));
            _bark = SaveMat(StylizedMaterials.Create("M_StackBark", new Color(0.2f, 0.14f, 0.1f), 0.05f, StylizedMaterials.Kind.Foliage));
            _wood = SaveMat(StylizedMaterials.Create("M_BoatWood", new Color(0.42f, 0.27f, 0.18f), 0.2f, StylizedMaterials.Kind.Prop));
            _woodDark = SaveMat(StylizedMaterials.Create("M_BoatWoodDark", new Color(0.16f, 0.11f, 0.08f), 0.1f, StylizedMaterials.Kind.Detail));
            _lanternPaper = SaveMat(StylizedMaterials.Create("M_LanternPaper", new Color(1f, 0.72f, 0.42f), 0.3f, StylizedMaterials.Kind.Glow, new Color(2.6f, 1.25f, 0.42f)));
            _lanternCap = SaveMat(StylizedMaterials.Create("M_LanternCap", new Color(0.14f, 0.09f, 0.07f), 0.2f, StylizedMaterials.Kind.Detail));
            _whaleSkin = SaveMat(StylizedMaterials.Create("M_WhaleBack", new Color(0.1f, 0.12f, 0.2f), 0.7f, StylizedMaterials.Kind.Natural));
            _island = SaveMat(StylizedMaterials.Create("M_HorizonIsland", new Color(0.05f, 0.05f, 0.1f), 0f, StylizedMaterials.Kind.Distant));

            // additive glows (StarlitTides/GlowParticle + soft dot)
            _glowMint = Glow("M_GlowAlgaeMint", new Color(0.12f, 0.75f, 0.5f));
            _glowCyan = Glow("M_GlowAlgaeCyan", new Color(0.1f, 0.6f, 0.85f));
            _glowWarm = Glow("M_GlowLantern", new Color(1.6f, 0.75f, 0.25f));
            _glowWarmSmall = Glow("M_GlowShipLight", new Color(2.4f, 1.3f, 0.5f));
            _glowViolet = Glow("M_GlowCoral", new Color(1.1f, 0.5f, 2.0f));
            _glowSpray = Glow("M_GlowSpray", new Color(0.55f, 0.85f, 1.1f));
        }

        static Material Glow(string name, Color tint)
        {
            var m = new Material(Shader.Find("StarlitTides/GlowParticle")) { name = name };
            m.SetTexture("_MainTex", _dot);
            m.SetColor("_TintColor", tint);
            Material saved = SaveMat(m);
            EditorAssetUtil.Persist(saved);
            return saved;
        }

        // ==================================================================
        // SCENERY PREFABS
        // ==================================================================

        // A bloom of glowing algae lying on the water: a few overlapping soft
        // glow patches (flat, additive) and a scatter of small floating sparks.
        static GameObject BuildAlgae(string name, Material glow, int seed)
        {
            var rng = new System.Random(seed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var root = new GameObject(name);
            Transform g = Child("Glow", root.transform);
            for (int i = 0; i < 4; i++)
            {
                GameObject patch = MeshChild("Patch_" + i, g, _quad, glow);
                patch.transform.localPosition = new Vector3(R(-3f, 3f), 0.05f + i * 0.01f, R(-3f, 3f));
                patch.transform.localRotation = Quaternion.Euler(90f, R(0f, 360f), 0f);
                patch.transform.localScale = new Vector3(R(5f, 9f), R(3.5f, 6f), 1f);
                NoShadows(patch);
            }
            for (int i = 0; i < 8; i++)
            {
                GameObject spark = MeshChild("Spark_" + i, g, _quad, glow);
                spark.transform.localPosition = new Vector3(R(-4f, 4f), R(0.2f, 0.7f), R(-4f, 4f));
                spark.transform.localScale = Vector3.one * R(0.25f, 0.5f);
                spark.AddComponent<FaceCamera>();
                NoShadows(spark);
            }
            var d = root.AddComponent<SeaDecor>();
            d.glow = g; d.pulseSpeed = 0.7f; d.pulseAmount = 0.12f;
            d.floatOffset = 0f; d.followWaves = 1f; d.bobAmplitude = 0.05f;
            d.scaleRange = new Vector2(0.8f, 1.4f);
            Finish(d);
            return root;
        }

        // Paper lanterns floating on the water: warm, rare points of light.
        static GameObject BuildLanternCluster(string name, int count, float spread, int seed, bool ring = false)
        {
            var rng = new System.Random(seed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var root = new GameObject(name);
            Mesh body = _lanternMesh;
            Transform g = Child("Glow", root.transform);
            for (int i = 0; i < count; i++)
            {
                Vector3 p;
                if (ring) { float a = i / (float)count * Mathf.PI * 2f; p = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * spread; }
                else p = new Vector3(R(-spread, spread), 0f, R(-spread, spread));
                GameObject lantern = MeshChild("Lantern_" + i, root.transform, body, _lanternPaper, _lanternCap);
                lantern.transform.localPosition = p + Vector3.up * R(0.05f, 0.15f);
                lantern.transform.localRotation = Quaternion.Euler(R(-6f, 6f), R(0f, 360f), R(-6f, 6f));
                float s = R(0.85f, 1.2f);
                lantern.transform.localScale = Vector3.one * s;
                NoShadows(lantern);
                GameObject halo = MeshChild("Halo_" + i, g, _quad, _glowWarm);
                halo.transform.localPosition = p + Vector3.up * 0.35f * s;
                halo.transform.localScale = Vector3.one * 1.8f * s;
                halo.AddComponent<FaceCamera>();
                NoShadows(halo);
                // a warm glow pool on the water under each lantern
                GameObject pool = MeshChild("Pool_" + i, g, _quad, _glowWarm);
                pool.transform.localPosition = p + Vector3.up * 0.04f;
                pool.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                pool.transform.localScale = Vector3.one * 2.6f * s;
                NoShadows(pool);
            }
            if (ring)
            {
                GameObject centre = MeshChild("CentreBloom", g, _quad, _glowMint);
                centre.transform.localPosition = Vector3.up * 0.03f;
                centre.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                centre.transform.localScale = Vector3.one * spread * 1.6f;
                NoShadows(centre);
            }
            var d = root.AddComponent<SeaDecor>();
            d.glow = g; d.pulseSpeed = 1.6f; d.pulseAmount = 0.08f;
            d.floatOffset = 0f; d.followWaves = 1f; d.bobAmplitude = 0.07f; d.bobSpeed = 1.2f;
            d.rockAngle = 4f;
            Finish(d, ring ? 220f : 200f);
            return root;
        }

        static Mesh LanternMesh()
        {
            var mb = new MeshBuilder(2);
            mb.SmoothGroup = 1;
            float[] r = { 0.13f, 0.21f, 0.24f, 0.24f, 0.21f, 0.13f };
            float[] y = { 0f, 0.08f, 0.18f, 0.3f, 0.4f, 0.48f };
            for (int i = 0; i < r.Length - 1; i++)
                mb.Cylinder(0, Vector3.up * y[i], r[i], r[i + 1], y[i + 1] - y[i], 14, false);
            mb.SmoothGroup = 0;
            mb.Cylinder(1, Vector3.down * 0.04f, 0.14f, 0.14f, 0.05f, 14, true, true);   // base ring
            mb.Cylinder(1, Vector3.up * 0.47f, 0.14f, 0.1f, 0.05f, 14, true, false);     // top ring
            return mb.ToMesh("PaperLantern");
        }

        // A few half-sunk mossy rocks: no glow (scenery, not an obstacle).
        static GameObject BuildRockCluster(string name, int seed)
        {
            var rng = new System.Random(seed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var root = new GameObject(name);
            for (int i = 0; i < 4; i++)
            {
                float radius = i == 0 ? 1.8f : R(0.7f, 1.3f);
                Mesh m = SaveMesh(ProcMesh.Rock(seed * 10 + i, radius, 0.42f, new Vector3(1.2f, R(0.7f, 1.1f), 1.1f), 3), name + "_Rock" + i + ".asset");
                GameObject r = MeshChild("Rock_" + i, root.transform, m, _rock);
                float a = i * 2.2f;
                r.transform.localPosition = (i == 0 ? Vector3.zero : new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * R(1.8f, 2.8f)) + Vector3.down * radius * 0.35f;
                r.transform.localRotation = Quaternion.Euler(0f, R(0f, 360f), 0f);
                NoShadows(r);
            }
            var d = root.AddComponent<SeaDecor>();
            d.floatOffset = -0.2f; d.followWaves = 0.2f; d.bobAmplitude = 0.02f;
            d.scaleRange = new Vector2(0.8f, 1.3f);
            Finish(d);
            return root;
        }

        // A tall sea stack: a stretched rock pillar, a smaller rock on top,
        // and a few little pines clinging to the summit.
        static GameObject BuildSeaStack(string name, int seed, float radius, float height, int pines)
        {
            var rng = new System.Random(seed);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            var root = new GameObject(name);
            Mesh pillar = SaveMesh(ProcMesh.Rock(seed * 7, 1f, 0.32f, new Vector3(radius, height * 0.5f, radius * R(0.8f, 1.1f)), 4), name + "_Pillar.asset");
            GameObject p = MeshChild("Pillar", root.transform, pillar, _rock);
            p.transform.localPosition = Vector3.up * (height * 0.35f);
            NoShadows(p);
            Mesh capMesh = SaveMesh(ProcMesh.Rock(seed * 7 + 1, 1f, 0.38f, new Vector3(radius * 0.75f, radius * 0.45f, radius * 0.7f), 3), name + "_Cap.asset");
            GameObject cap = MeshChild("Cap", root.transform, capMesh, _rock);
            float top = height * 0.35f + height * 0.42f;
            cap.transform.localPosition = new Vector3(R(-0.3f, 0.3f) * radius, top, R(-0.3f, 0.3f) * radius);
            NoShadows(cap);
            for (int i = 0; i < pines; i++)
            {
                Mesh pine = SaveMesh(ProcMesh.PineTree(seed * 13 + i), name + "_Pine" + i + ".asset");
                GameObject t = MeshChild("Pine_" + i, root.transform, pine, _bark, _leaves);
                float a = R(0f, Mathf.PI * 2f), rr = R(0f, radius * 0.45f);
                t.transform.localPosition = cap.transform.localPosition + new Vector3(Mathf.Cos(a) * rr, radius * 0.3f, Mathf.Sin(a) * rr);
                t.transform.localScale = Vector3.one * R(0.3f, 0.55f) * Mathf.Clamp(radius / 4f, 0.8f, 1.6f);
                t.transform.localRotation = Quaternion.Euler(R(-6f, 6f), R(0f, 360f), R(-6f, 6f));
                NoShadows(t);
            }
            var d = root.AddComponent<SeaDecor>();
            d.floatOffset = -2.5f; d.followWaves = 0f; d.bobAmplitude = 0f;
            d.scaleRange = new Vector2(0.85f, 1.2f);
            d.maxGroundHeight = -3f;
            Finish(d, 260f);
            return root;
        }

        // A natural rock arch: a cross-section swept along a half-ellipse, its
        // legs going down under the water, displaced by noise so it looks carved.
        // With a glow material it becomes the "coral arch" set piece.
        static GameObject BuildArch(string name, int seed, Material glowDots)
        {
            var root = new GameObject(name);
            Mesh m = SaveMesh(ArchMesh(seed), name + "_Mesh.asset");
            GameObject a = MeshChild("Arch", root.transform, m, _rock);
            NoShadows(a);
            Transform g = null;
            if (glowDots != null)
            {
                g = Child("Glow", root.transform);
                var rng = new System.Random(seed);
                Vector3[] v = m.vertices, n = m.normals;
                for (int i = 0, placed = 0; i < 800 && placed < 26; i++)
                {
                    int k = rng.Next(v.Length);
                    if (v[k].y < 0.5f || n[k].y < -0.2f) continue;
                    GameObject dot = MeshChild("Coral_" + placed, g, _quad, glowDots);
                    dot.transform.localPosition = v[k] + n[k] * 0.25f;
                    dot.transform.localScale = Vector3.one * (0.8f + (float)rng.NextDouble() * 1.4f);
                    dot.AddComponent<FaceCamera>();
                    NoShadows(dot);
                    placed++;
                }
            }
            var d = root.AddComponent<SeaDecor>();
            d.glow = g; d.pulseSpeed = 0.9f; d.pulseAmount = 0.2f;
            d.floatOffset = -0.5f; d.followWaves = 0f; d.bobAmplitude = 0f;
            d.scaleRange = new Vector2(0.9f, 1.25f);
            d.maxGroundHeight = -3f;
            Finish(d, 260f);
            return root;
        }

        static Mesh ArchMesh(int seed)
        {
            const int along = 40, around = 16;
            const float span = 8f, rise = 9f, legDepth = 4f;
            var verts = new List<Vector3>();
            var colors = new List<Color>();
            var tris = new List<int>();
            float ox = seed * 3.7f;
            for (int i = 0; i <= along; i++)
            {
                float t = i / (float)along;
                // path: down leg (t 0..0.15), arch (0.15..0.85), down leg (0.85..1)
                Vector3 c, tangent;
                if (t < 0.15f) { float k = t / 0.15f; c = new Vector3(-span, Mathf.Lerp(-legDepth, 0f, k), 0f); tangent = Vector3.up; }
                else if (t > 0.85f) { float k = (t - 0.85f) / 0.15f; c = new Vector3(span, Mathf.Lerp(0f, -legDepth, k), 0f); tangent = Vector3.down; }
                else
                {
                    float a = Mathf.Lerp(Mathf.PI, 0f, (t - 0.15f) / 0.7f);
                    c = new Vector3(Mathf.Cos(a) * span, Mathf.Sin(a) * rise, 0f);
                    tangent = new Vector3(-Mathf.Sin(a) * span, Mathf.Cos(a) * rise, 0f).normalized * -1f;
                }
                Vector3 side = Vector3.forward;
                Vector3 up = Vector3.Cross(side, tangent).normalized;
                // thicker legs, thinner crown
                float crown = Mathf.Sin(Mathf.Clamp01((t - 0.15f) / 0.7f) * Mathf.PI);
                float thick = Mathf.Lerp(2.4f, 1.3f, crown);
                for (int j = 0; j <= around; j++)
                {
                    float b = j / (float)around * Mathf.PI * 2f;
                    Vector3 dir = up * Mathf.Cos(b) + side * Mathf.Sin(b) * 1.25f;
                    Vector3 p = c + dir * thick;
                    float n = Mathf.PerlinNoise(p.x * 0.35f + ox, p.y * 0.35f + p.z * 0.2f) - 0.5f;
                    float n2 = Mathf.PerlinNoise(p.x * 1.1f + ox * 2f, p.y * 1.1f - p.z * 0.6f) - 0.5f;
                    p += dir.normalized * (n * 1.1f + n2 * 0.35f);
                    verts.Add(p);
                    colors.Add(new Color(Mathf.Clamp01(dir.y * 1.4f - 0.2f + n), 0f, 0f, 1f));   // moss on the upper surface
                }
            }
            for (int i = 0; i < along; i++)
            for (int j = 0; j < around; j++)
            {
                int a = i * (around + 1) + j, b = a + around + 1;
                tris.Add(a); tris.Add(b); tris.Add(a + 1);
                tris.Add(a + 1); tris.Add(b); tris.Add(b + 1);
            }
            var mesh = new Mesh { name = "RockArch_" + seed };
            mesh.SetVertices(verts); mesh.SetColors(colors); mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            // make sure faces point outward (away from the path)
            Vector3[] nrm = mesh.normals;
            int outward = 0;
            for (int j = 0; j <= around; j++)
            {
                int k = (along / 2) * (around + 1) + j;
                if (Vector3.Dot(nrm[k], verts[k] - new Vector3(0f, rise, 0f)) > 0f) outward++;
            }
            if (outward < around / 2) { tris.Reverse(); mesh.SetTriangles(tris, 0); mesh.RecalculateNormals(); }
            mesh.RecalculateBounds(); mesh.RecalculateTangents();
            MeshBuilder.BakeSmoothNormals(mesh);
            return mesh;
        }

        // A small wooden fishing boat with a warm lantern on a pole.
        static GameObject BuildFishingBoat(string name, int seed)
        {
            var root = new GameObject(name);
            Mesh hull = SaveMesh(HullMesh(), "FishingBoatHull.asset");
            GameObject h = MeshChild("Hull", root.transform, hull, _wood, _woodDark);
            NoShadows(h);
            var mb = new MeshBuilder(1);
            mb.SmoothGroup = 1;
            mb.Cylinder(0, new Vector3(0f, 0.3f, 0.6f), 0.06f, 0.05f, 2.6f, 10, true);          // mast
            mb.SmoothGroup = 2;
            mb.Cylinder(0, new Vector3(0f, 0.3f, -1.7f), 0.04f, 0.035f, 1.5f, 8, true);          // lantern pole
            mb.SmoothGroup = 0;
            mb.ChamferBox(0, new Vector3(0f, 0.32f, -0.2f), new Vector3(1.1f, 0.08f, 0.35f), 0.02f); // thwart (seat)
            GameObject spars = MeshChild("Spars", root.transform, SaveMesh(mb.ToMesh("FishingBoatSpars"), "FishingBoatSpars.asset"), _woodDark);
            NoShadows(spars);
            GameObject lantern = MeshChild("Lantern", root.transform, _lanternMesh, _lanternPaper, _lanternCap);
            lantern.transform.localPosition = new Vector3(0f, 1.62f, -1.7f);
            lantern.transform.localScale = Vector3.one * 0.7f;
            NoShadows(lantern);
            Transform g = Child("Glow", root.transform);
            GameObject halo = MeshChild("Halo", g, _quad, _glowWarm);
            halo.transform.localPosition = new Vector3(0f, 1.8f, -1.7f);
            halo.transform.localScale = Vector3.one * 2.4f;
            halo.AddComponent<FaceCamera>();
            NoShadows(halo);
            GameObject pool = MeshChild("Reflection", g, _quad, _glowWarm);
            pool.transform.localPosition = new Vector3(0f, 0.03f, -2.1f);
            pool.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            pool.transform.localScale = new Vector3(1.6f, 3.6f, 1f);
            NoShadows(pool);
            var d = root.AddComponent<SeaDecor>();
            d.glow = g; d.pulseSpeed = 2.3f; d.pulseAmount = 0.06f;
            d.floatOffset = -0.15f; d.followWaves = 1f; d.bobAmplitude = 0.08f; d.bobSpeed = 1.1f;
            d.rockAngle = 6f;
            Finish(d, 230f);
            return root;
        }

        // Lofted hull: U-shaped sections along the length, a raised sheer at bow
        // and stern, closed by a dark inner floor at the gunwale.
        static Mesh HullMesh()
        {
            const int along = 18, across = 12;
            const float length = 4.6f, beam = 0.75f, depth = 0.55f;
            var mb = new MeshBuilder(2);
            Vector3 P(float u, float v)
            {
                float z = Mathf.Lerp(-length * 0.5f, length * 0.5f, u);
                float w = beam * Mathf.Pow(Mathf.Max(0f, Mathf.Sin(Mathf.PI * u)), 0.55f);          // pointed ends
                float sheer = 0.35f + 0.25f * Mathf.Pow(Mathf.Abs(u - 0.5f) * 2f, 2f);             // rises at bow and stern
                float a = Mathf.Lerp(-Mathf.PI * 0.5f, Mathf.PI * 0.5f, v);                      // around the U
                float x = Mathf.Sin(a) * w;
                float y = sheer - Mathf.Cos(a) * depth * Mathf.Lerp(0.35f, 1f, Mathf.Sin(Mathf.PI * u));
                return new Vector3(x, y, z);
            }
            mb.SmoothGroup = 1;
            for (int i = 0; i < along; i++)
            for (int j = 0; j < across; j++)
            {
                float u0 = i / (float)along, u1 = (i + 1) / (float)along, v0 = j / (float)across, v1 = (j + 1) / (float)across;
                Vector3 c = (P(u0, v0) + P(u1, v1)) * 0.5f;
                Vector3 outward = c - new Vector3(0f, 0.6f, c.z);
                mb.Quad(0, P(u0, v0), P(u1, v0), P(u1, v1), P(u0, v1), outward);
            }
            mb.SmoothGroup = 0;
            for (int i = 0; i < along; i++)   // dark inner floor at the gunwale
            {
                float u0 = i / (float)along, u1 = (i + 1) / (float)along;
                Vector3 l0 = P(u0, 0f), r0 = P(u0, 1f), l1 = P(u1, 0f), r1 = P(u1, 1f);
                Vector3 drop = Vector3.down * 0.12f;
                mb.Quad(1, l0 + drop, l1 + drop, r1 + drop, r0 + drop, Vector3.up);
            }
            return mb.ToMesh("FishingBoatHull");
        }

        // A whale's back breaking the surface far out, with a misty spout now and then.
        static GameObject BuildWhaleSpout(string name, int seed)
        {
            var root = new GameObject(name);
            var mb = new MeshBuilder(1);
            mb.SmoothGroup = 1;
            ProcMesh.Icosphere(mb, 0, Vector3.zero, 1f, 2, seed, 0.03f, new Vector3(2.4f, 0.7f, 7f));
            mb.SmoothGroup = 2;
            mb.Transform = Matrix4x4.TRS(new Vector3(0f, 0.55f, -2.6f), Quaternion.Euler(-20f, 0f, 0f), Vector3.one);
            mb.Cylinder(0, Vector3.zero, 0.5f, 0.05f, 0.8f, 10, false);                               // small dorsal hump
            GameObject back = MeshChild("Back", root.transform, SaveMesh(mb.ToMesh("WhaleBack"), "WhaleBack.asset"), _whaleSkin);
            NoShadows(back);

            var psGo = new GameObject("Spout");
            psGo.transform.SetParent(root.transform, false);
            psGo.transform.localPosition = new Vector3(0f, 0.6f, 2.2f);
            var ps = psGo.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false; main.loop = false; main.duration = 0.6f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(1.2f, 2f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(5f, 8f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.6f, 1.4f);
            main.gravityModifier = 0.55f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 80;
            var em = ps.emission; em.rateOverTime = 0f; em.SetBursts(new[] { new ParticleSystem.Burst(0f, 30), new ParticleSystem.Burst(0.15f, 20) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 9f; sh.radius = 0.15f; sh.rotation = new Vector3(-90f, 0f, 0f);
            var col = ps.colorOverLifetime; col.enabled = true;
            var grad = new Gradient();
            grad.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.6f, 0.8f, 1f), 1f) },
                         new[] { new GradientAlphaKey(0.9f, 0f), new GradientAlphaKey(0.6f, 0.4f), new GradientAlphaKey(0f, 1f) });
            col.color = grad;
            var sz = ps.sizeOverLifetime; sz.enabled = true; sz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 0.6f, 1f, 1.8f));
            var pr = psGo.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = _glowSpray; pr.shadowCastingMode = ShadowCastingMode.Off;
            VfxKit.StyleSpout(ps);   // VFX polish pass: soft rising mist

            var d = root.AddComponent<SeaDecor>();
            d.spout = ps; d.spoutInterval = new Vector2(4f, 9f);
            d.floatOffset = -0.35f; d.followWaves = 0.6f; d.bobAmplitude = 0.15f; d.bobSpeed = 0.4f;
            d.extraDrift = new Vector3(0f, 0f, 1.5f);   // swims slowly against the tide
            Finish(d, 260f);
            return root;
        }

        // ==================================================================
        // HORIZON: static island silhouettes, a far lighthouse, ship lights
        // ==================================================================
        static void BuildHorizon(Transform root)
        {
            Transform horizon = Child("Horizon Islands", root);
            // (azimuth degrees from +Z, distance, width, peak). The moon sits ~19 deg
            // to the right: keep 10-30 deg clear so its glitter path stays open.
            var islands = new[]
            {
                (az: -6f, dist: 780f, width: 260f, peak: 36f, seed: 201),
                (az: 38f, dist: 640f, width: 190f, peak: 24f, seed: 202),
                (az: 54f, dist: 860f, width: 360f, peak: 58f, seed: 203),
                (az: 71f, dist: 580f, width: 150f, peak: 18f, seed: 204),
                (az: -2f, dist: 1150f, width: 420f, peak: 70f, seed: 205),
            };
            Vector3 lighthousePos = Vector3.zero;
            foreach (var isl in islands)
            {
                float w = isl.width;
                Mesh m = SaveMesh(ProcMesh.MountainRidge(isl.seed, -w * 0.5f, w * 0.5f, isl.peak, 70f, Mathf.RoundToInt(w / 6f),
                                  x => Mathf.Pow(Mathf.Max(0f, Mathf.Cos(x / (w * 0.5f) * Mathf.PI * 0.5f)), 0.6f)), "Island_" + isl.seed + ".asset");
                Vector3 dir = Quaternion.Euler(0f, isl.az, 0f) * Vector3.forward;
                var go = MeshChild("Island " + isl.seed, horizon, m, _island);
                go.transform.position = dir * isl.dist;
                go.transform.rotation = Quaternion.LookRotation(dir);
                NoShadows(go);
                go.GetComponent<MeshRenderer>().receiveShadows = false;
                if (isl.seed == 203) lighthousePos = go.transform.position + Vector3.up * (isl.peak * 0.55f) + Quaternion.LookRotation(dir) * new Vector3(-w * 0.28f, 0f, 0f);
            }
            // far lighthouse: a steady warm star on the big island
            var far = MeshChild("Far Lighthouse Light", horizon, _quad, _glowWarmSmall);
            far.transform.position = lighthousePos;
            far.transform.localScale = Vector3.one * 9f;
            far.AddComponent<FaceCamera>();
            NoShadows(far);
            // two ship lights on the horizon
            foreach (var (az, dist) in new[] { (44f, 520f), (-14f, 680f) })
            {
                var ship = MeshChild("Ship Light", horizon, _quad, _glowWarmSmall);
                ship.transform.position = Quaternion.Euler(0f, az, 0f) * Vector3.forward * dist + Vector3.up * 2.5f;
                ship.transform.localScale = Vector3.one * 5f;
                ship.AddComponent<FaceCamera>();
                NoShadows(ship);
            }
        }

        // ==================================================================
        // HELPERS
        // ==================================================================
        static void Finish(SeaDecor d, float despawnX = 200f)
        {
            d.despawnX = despawnX;
            d.despawnZ = -40f;
        }

        static GameObject Save(GameObject temp)
        {
            string path = PrefabDir + "/" + temp.name + ".prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temp, path);
            Object.DestroyImmediate(temp);
            return prefab;
        }

        static Material SaveMat(Material m) { return SaveAsset(m, m.name + ".mat"); }
        static Mesh SaveMesh(Mesh m, string file) { return SaveAsset(m, file); }

        static T SaveAsset<T>(T asset, string file) where T : Object
        {
            string path = Gen + "/" + file;
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        static Transform Child(string name, Transform parent)
        {
            var t = new GameObject(name).transform;
            t.SetParent(parent, false);
            return t;
        }

        static GameObject MeshChild(string name, Transform parent, Mesh mesh, params Material[] mats)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            go.AddComponent<MeshRenderer>().sharedMaterials = mats;
            return go;
        }

        static void NoShadows(GameObject go)
        {
            var r = go.GetComponent<Renderer>();
            if (r != null) r.shadowCastingMode = ShadowCastingMode.Off;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }
    }
}
#endif
