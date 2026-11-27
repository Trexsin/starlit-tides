// =============================================================================
//  STARLIT TIDES - One-click environment builder (Phase 2)
// -----------------------------------------------------------------------------
//  Menu:  Starlit Tides > 4. Build Environment
//
//  Builds the whole night bay into the OPEN scene:
//    sky + moonlight + fog          terrain (beach, village plateau, lagoon,
//    bioluminescent ocean           channel, headland cliff, forested hills)
//    village: 12 houses with glowing windows, lantern-lit square, well,
//             festoon lights, 5 market stalls with awnings and lanterns
//    wooden pier with lanterns      arched bridge over the lagoon channel
//    lighthouse with sweeping beams forest (pine, round, palm), wind grass
//    3 layers of mountains in fog   fireflies + drifting plankton motes
//    reflection probe               post-processing (bloom, ACES, grading...)
//    camera framed on the bay
//
//  Run it again at any time: it deletes and rebuilds "== STARLIT ENVIRONMENT ==".
//  Generated meshes, textures, materials and terrain data are saved in
//  Assets/_StarlitTides/Generated so you can tweak materials afterwards.
//
//  Want downloaded models instead of procedural ones? Create an
//  "Environment Asset Overrides" asset (Assets > Create > Starlit Tides) and
//  drop prefabs into its lists; the builder uses them automatically.
// =============================================================================
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace StarlitTides.EditorTools
{
    public static class EnvironmentBuilder
    {
        // ------------------------------------------------------------------
        // World layout (matches the map in the Phase 1 guide)
        // ------------------------------------------------------------------
        private const string Gen = "Assets/_StarlitTides/Generated";
        private const string RootName = "== STARLIT ENVIRONMENT ==";

        private static readonly Vector2 SquareCenter = new Vector2(-50f, 32f);
        private const float SquareGroundY = 2.6f;
        private static readonly Vector2 LagoonCenter = new Vector2(-85f, 95f);
        private static readonly Vector2 LagoonRadii = new Vector2(30f, 24f);
        private const float ChannelZ = 95f;
        private const float BridgeX = -40f;
        private static readonly Vector2 PierStart = new Vector2(-26f, 26f);
        private static readonly Vector2 PierEnd = new Vector2(-11f, 46f);
        private const float PierDeckY = 1.45f;
        private static readonly Vector2 LighthouseXZ = new Vector2(-12f, 182f);

        // The terrain is axis-aligned in WORLD space (Unity terrains can't be
        // rotated). Its heights are sampled from the layout map through
        // WorldLayout, so the coast swings round behind the boat.
        private static readonly Vector3 TerrainPos = new Vector3(-800f, -20f, -600f);
        private static readonly Vector3 TerrainSize = new Vector3(1100f, 180f, 1100f);
        private const int HeightRes = 1025;   // ~1 m per sample: crisp beaches and cliffs
        private const int SplatRes = 1024;
        private const int DetailRes = 768;

        // Palette (see Phase 1 guide): navy, purple, teal, neon cyan, soft gold
        private static readonly Color Gold = new Color(1f, 0.78f, 0.45f);

        private static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();
        private static Transform _root;
        private static Terrain _terrain;
        private static EnvironmentAssetOverrides _overrides;
        private static System.Random _rng;
        private static readonly List<Vector2> Paths = new List<Vector2>();   // pairs of points = path segments
        private static Mesh _lanternMesh, _hangingLanternMesh, _windowPaneMesh;

        // ------------------------------------------------------------------
        [MenuItem("Starlit Tides/4. Build Environment", priority = 20)]
        public static void BuildMenu()
        {
            if (!Application.isBatchMode && !EditorUtility.DisplayDialog("Build Starlit Tides Environment",
                    "This builds the whole night bay into the open scene (about 30 seconds).\n\n" +
                    "If '" + RootName + "' already exists it will be deleted and rebuilt.", "Build", "Cancel"))
                return;
            Build();
        }

        public static void Build()
        {
            try
            {
                _rng = new System.Random(20251125);
                Mats.Clear();
                Paths.Clear();
                SetupPaths();
                EnsureFolders();
                _overrides = FindOverrides();

                GameObject old = GameObject.Find(RootName);
                if (old != null) Object.DestroyImmediate(old);
                _root = new GameObject(RootName).transform;

                Progress("Materials", 0.05f);      CreateMaterials();
                Progress("Sky and light", 0.1f);   BuildSkyAndLight();
                Progress("Terrain", 0.15f);        BuildTerrain();
                Progress("Ocean", 0.35f);          BuildOcean();
                Progress("Village", 0.45f);        BuildVillage();
                Progress("Pier", 0.55f);           BuildPier();
                Progress("Bridge", 0.6f);          BuildBridge();
                Progress("Lighthouse", 0.65f);     BuildLighthouse();
                Progress("Forest", 0.7f);          BuildForest();
                Progress("Mountains", 0.8f);       BuildMountains();
                Progress("Ambient VFX", 0.85f);    BuildAmbientVfx();
                Progress("Reflections", 0.9f);     BuildReflectionProbe();
                Progress("Post-processing", 0.93f); BuildPostProcessing();
                Progress("Camera", 0.97f);         SetupCamera();
                Progress("Orientation", 0.98f);    OrientLayout();

                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
                Debug.Log("[Starlit Tides] Environment built. Press Play or look through the Game view.");
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }

        private static void Progress(string step, float t)
        {
            if (!Application.isBatchMode) EditorUtility.DisplayProgressBar("Building Starlit Tides", step + "...", t);
        }

        // ==================================================================
        // HEIGHT FUNCTION - the shape of the whole land and sea floor
        // ==================================================================
        private static float ShoreX(float z) =>
            -24f + 3.5f * Mathf.Sin(z * 0.035f) + 2f * Mathf.Sin(z * 0.011f + 1f) + 18f * Gauss((z - 182f) / 24f);

        /// <summary>Metres inland from the shoreline (negative = in the sea).</summary>
        private static float Inland(float x, float z) => ShoreX(z) - x;

        private static float Height(float x, float z)
        {
            float s = Inland(x, z);
            float y;
            if (s < 0f)
            {
                y = -1.2f + s * 0.55f;                                           // sea floor slopes down
                y = Mathf.Max(y, -13f - Fbm(x * 0.02f, z * 0.02f, 3) * 3f);
            }
            else if (s < 9f)
            {
                y = Mathf.Lerp(-1.2f, 2.0f, Smooth(0f, 9f, s));                    // beach
            }
            else
            {
                y = 2.0f + (Fbm(x * 0.03f, z * 0.03f, 3) - 0.5f) * 1.4f * Smooth(9f, 20f, s);
                y += Smooth(40f, 150f, s) * (10f + 50f * Fbm(x * 0.008f + 3f, z * 0.008f + 7f, 4));       // forest hills
                y += Smooth(200f, 450f, z) * Smooth(-10f, 60f, s) * 70f * Fbm(x * 0.006f, z * 0.006f + 11f, 4); // back hills
            }

            // headland cliff for the lighthouse
            y += 22f * Gauss((z - 182f) / 20f) * Smooth(0f, 5f, s) * Smooth(-200f, -20f, x);

            // flat village square
            float dSq = Vector2.Distance(new Vector2(x, z), SquareCenter);
            y = Mathf.Lerp(y, SquareGroundY, Smooth(34f, 22f, dSq));

            // lagoon basin
            Vector2 e = new Vector2((x - LagoonCenter.x) / LagoonRadii.x, (z - LagoonCenter.y) / LagoonRadii.y);
            float el = e.magnitude;
            if (el < 1.35f) y = Mathf.Lerp(-3.5f, y, Smooth(0.8f, 1.35f, el));

            // channel from the lagoon to the sea (under the bridge)
            float ch = Smooth(10f, 5f, Mathf.Abs(z - ChannelZ)) * Smooth(-70f, -58f, x);
            y = Mathf.Min(y, Mathf.Lerp(y, -2.8f, ch));
            return y;
        }

        private static float Gauss(float t) => Mathf.Exp(-t * t);

        /// <summary>Hermite smoothstep. Works with edge0 > edge1 too (reversed).</summary>
        private static float Smooth(float edge0, float edge1, float x)
        {
            float t = Mathf.Clamp01((x - edge0) / (edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        private static float Fbm(float x, float y, int octaves)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            for (int i = 0; i < octaves; i++)
            {
                sum += Mathf.PerlinNoise(x + 100f, y + 100f) * amp;
                norm += amp;
                x *= 2.03f; y *= 2.03f; amp *= 0.5f;
            }
            return sum / norm;
        }

        private static float Slope(float x, float z)
        {
            const float e = 1.5f;
            float dx = (Height(x + e, z) - Height(x - e, z)) / (2f * e);
            float dz = (Height(x, z + e) - Height(x, z - e)) / (2f * e);
            return Mathf.Atan(Mathf.Sqrt(dx * dx + dz * dz)) * Mathf.Rad2Deg;
        }

        private static void SetupPaths()
        {
            void Seg(float x0, float z0, float x1, float z1) { Paths.Add(new Vector2(x0, z0)); Paths.Add(new Vector2(x1, z1)); }
            Seg(PierStart.x, PierStart.y, SquareCenter.x, SquareCenter.y);   // pier -> square
            Seg(SquareCenter.x, SquareCenter.y, BridgeX, 84f);                // square -> bridge
            Seg(BridgeX, 106f, -30f, 150f);                                    // bridge -> cliff path
            Seg(-30f, 150f, LighthouseXZ.x - 4f, LighthouseXZ.y - 6f);         // cliff path -> lighthouse
            Seg(SquareCenter.x, SquareCenter.y, -95f, 26f);                    // square -> west fields
        }

        private static float DistanceToPath(float x, float z)
        {
            Vector2 p = new Vector2(x, z);
            float best = float.MaxValue;
            for (int i = 0; i < Paths.Count; i += 2)
            {
                Vector2 a = Paths[i], b = Paths[i + 1];
                Vector2 ab = b - a;
                float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / ab.sqrMagnitude);
                best = Mathf.Min(best, Vector2.Distance(p, a + ab * t));
            }
            return best;
        }

        /// <summary>Actual ground height (from the built terrain when available).</summary>
        private static float GroundY(float x, float z)
        {
            if (_terrain != null)
                return _terrain.SampleHeight(WorldLayout.ToWorld(new Vector3(x, 0f, z))) + _terrain.transform.position.y;
            return Height(x, z);
        }

        private static bool InLagoonOrChannel(float x, float z, float margin = 1.3f)
        {
            Vector2 e = new Vector2((x - LagoonCenter.x) / LagoonRadii.x, (z - LagoonCenter.y) / LagoonRadii.y);
            if (e.magnitude < margin) return true;
            return Mathf.Abs(z - ChannelZ) < 12f && x > -72f && Inland(x, z) > -5f;
        }

        // ==================================================================
        // FOLDERS / ASSET HELPERS
        // ==================================================================
        private static void EnsureFolders()
        {
            foreach (string sub in new[] { "Meshes", "Materials", "Textures", "Terrain", "Settings" })
                EnsureFolder(Gen + "/" + sub);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static T SaveAsset<T>(T asset, string relativePath) where T : Object
        {
            string path = Gen + "/" + relativePath;
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static Mesh SaveMesh(Mesh mesh)
        {
            return SaveAsset(mesh, "Meshes/" + mesh.name + ".asset");
        }

        private static EnvironmentAssetOverrides FindOverrides()
        {
            string[] guids = AssetDatabase.FindAssets("t:EnvironmentAssetOverrides");
            if (guids.Length == 0) return null;
            return AssetDatabase.LoadAssetAtPath<EnvironmentAssetOverrides>(AssetDatabase.GUIDToAssetPath(guids[0]));
        }

        private static GameObject Child(string name, Transform parent = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent != null ? parent : _root, false);
            return go;
        }

        private static GameObject MeshObject(string name, Mesh mesh, Material[] materials, Transform parent, Vector3 pos, Quaternion rot, bool isStatic = true, bool castShadows = true)
        {
            GameObject go = Child(name, parent);
            go.transform.SetPositionAndRotation(pos, rot);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = materials;
            mr.shadowCastingMode = castShadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            if (isStatic)
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
            return go;
        }

        private static float R(float a, float b) => a + (float)_rng.NextDouble() * (b - a);

        // ==================================================================
        // TEXTURES (generated, tileable)
        // ==================================================================
        private static float Hash(int x, int y, int seed)
        {
            unchecked
            {
                int h = x * 374761393 + y * 668265263 + seed * 1442695041;
                h = (h ^ (h >> 13)) * 1274126177;
                return ((h ^ (h >> 16)) & 0xFFFFFF) / 16777215f;
            }
        }

        private static float TileNoise(float x, float y, int period, int seed)
        {
            int x0 = Mathf.FloorToInt(x), y0 = Mathf.FloorToInt(y);
            float fx = x - x0, fy = y - y0;
            float u = fx * fx * (3f - 2f * fx), v = fy * fy * (3f - 2f * fy);
            int Wrap(int i) => ((i % period) + period) % period;
            float a = Hash(Wrap(x0), Wrap(y0), seed), b = Hash(Wrap(x0 + 1), Wrap(y0), seed);
            float c = Hash(Wrap(x0), Wrap(y0 + 1), seed), d = Hash(Wrap(x0 + 1), Wrap(y0 + 1), seed);
            return Mathf.Lerp(Mathf.Lerp(a, b, u), Mathf.Lerp(c, d, u), v);
        }

        private static float TileFbm(float u, float v, int seed)
        {
            float sum = 0f, amp = 0.5f, norm = 0f;
            int period = 8;
            for (int o = 0; o < 5; o++)
            {
                sum += TileNoise(u * period, v * period, period, seed + o) * amp;
                norm += amp; amp *= 0.5f; period *= 2;
            }
            return sum / norm;
        }

        private static Texture2D SaveTexture(Texture2D tex, string name, bool alpha, bool repeat = true, bool normalMap = false)
        {
            string rel = "Textures/" + name + ".png";
            string full = Gen + "/" + rel;
            File.WriteAllBytes(full, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(full, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(full);
            imp.wrapMode = repeat ? TextureWrapMode.Repeat : TextureWrapMode.Clamp;
            imp.alphaIsTransparency = alpha;
            imp.mipmapEnabled = true;
            imp.filterMode = FilterMode.Trilinear;
            imp.anisoLevel = 8;                        // sharp at grazing angles (beaches seen from the sea)
            imp.textureCompression = TextureImporterCompression.CompressedHQ;
            if (normalMap) imp.textureType = TextureImporterType.NormalMap;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(full);
        }

        private static Texture2D _lastGroundNormal;

        // A tileable 1024 px ground texture plus a matching normal map made from
        // the same noise (so pebbles, grass tufts and rock cracks catch the moonlight).
        private static Texture2D GroundTexture(string name, Color baseCol, Color speckle, float contrast, int seed, float bumpiness)
        {
            const int size = 1024;
            var height = new float[size * size];
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float u = x / (float)size, v = y / (float)size;
                float n = TileFbm(u, v, seed);
                float fine = TileNoise(u * 64f, v * 64f, 64, seed + 50);
                float grain = TileNoise(u * 256f, v * 256f, 256, seed + 90);
                Color c = baseCol * (1f - contrast * 0.5f + n * contrast);
                c *= 0.9f + grain * 0.2f;
                c = Color.Lerp(c, speckle, Mathf.Clamp01((fine - 0.78f) * 4f));
                c.a = 1f;
                pixels[y * size + x] = c;
                height[y * size + x] = n * 0.6f + fine * 0.3f + grain * 0.15f;
            }
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, true);
            tex.SetPixels32(pixels);
            tex.Apply();

            // Normal map from the height field (central differences, wrapping at the edges).
            var normals = new Color32[size * size];
            float strength = bumpiness * 6f;
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float hl = height[y * size + (x + size - 1) % size], hr = height[y * size + (x + 1) % size];
                float hd = height[((y + size - 1) % size) * size + x], hu = height[((y + 1) % size) * size + x];
                Vector3 nrm = new Vector3((hl - hr) * strength, (hd - hu) * strength, 1f).normalized;
                normals[y * size + x] = new Color(nrm.x * 0.5f + 0.5f, nrm.y * 0.5f + 0.5f, nrm.z * 0.5f + 0.5f, 1f);
            }
            var nTex = new Texture2D(size, size, TextureFormat.RGBA32, true, true);
            nTex.SetPixels32(normals);
            nTex.Apply();
            _lastGroundNormal = SaveTexture(nTex, name + "_N", false, true, true);
            return SaveTexture(tex, name, false);
        }

        private static Texture2D GrassBladeTexture()
        {
            const int w = 128, h = 128;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true);
            var clear = new Color(0.6f, 0.7f, 0.6f, 0f);
            for (int i = 0; i < w * h; i++) tex.SetPixel(i % w, i / w, clear);
            var r = new System.Random(7);
            for (int b = 0; b < 11; b++)
            {
                float baseX = 8 + (float)r.NextDouble() * (w - 16);
                float bend = ((float)r.NextDouble() - 0.5f) * 30f;
                float height = h * (0.55f + (float)r.NextDouble() * 0.45f);
                float width = 4f + (float)r.NextDouble() * 4f;
                for (int y = 0; y < height; y++)
                {
                    float t = y / height;
                    float cx = baseX + bend * t * t;
                    float hw = width * (1f - t);
                    for (int x = Mathf.FloorToInt(cx - hw); x <= Mathf.CeilToInt(cx + hw); x++)
                    {
                        if (x < 0 || x >= w) continue;
                        float edge = 1f - Mathf.Clamp01(Mathf.Abs(x - cx) - hw + 1f);
                        float shade = Mathf.Lerp(0.45f, 1f, t);
                        Color c = new Color(shade * 0.85f, shade, shade * 0.85f, edge);
                        Color old = tex.GetPixel(x, y);
                        tex.SetPixel(x, y, old.a > c.a ? old : c);
                    }
                }
            }
            tex.Apply();
            return SaveTexture(tex, "T_GrassBlades", true, false);
        }

        private static Texture2D SoftDotTexture()
        {
            const int s = 64;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, true);
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float dx = (x + 0.5f) / s * 2f - 1f, dy = (y + 0.5f) / s * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Exp(-d * d * 7f) + Mathf.Exp(-d * d * 60f) * 0.8f;
                a = Mathf.Clamp01(a) * Mathf.Clamp01(1f - d);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            tex.Apply();
            return SaveTexture(tex, "T_SoftDot", true, false);
        }

        private static Texture2D StripeTexture(string name, Color stripe)
        {
            const int s = 64;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, true);
            Color cream = new Color(0.86f, 0.8f, 0.68f);
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
                tex.SetPixel(x, y, ((x / 8) % 2 == 0) ? stripe : cream);
            tex.Apply();
            return SaveTexture(tex, name, false);
        }

        // ==================================================================
        // MATERIALS
        // ==================================================================
        // Phase 8: every solid material uses the stylized shader (StylizedMaterials).
        private static StylizedMaterials.Kind KindOf(string name)
        {
            switch (name)
            {
                case "M_Bark": case "M_PineLeaves": case "M_RoundLeaves": case "M_PalmLeaves":
                    return StylizedMaterials.Kind.Foliage;
                case "M_Mountain1": case "M_Mountain2": case "M_Mountain3":
                    return StylizedMaterials.Kind.Distant;
                case "M_Rock":
                    return StylizedMaterials.Kind.Natural;
                case "M_WindowOff": case "M_Metal": case "M_ClothTeal": case "M_ClothRed": case "M_ClothGold":
                    return StylizedMaterials.Kind.Detail;
                default:
                    return StylizedMaterials.Kind.Prop;
            }
        }

        private static Material Lit(string name, Color color, float smoothness = 0.15f, Texture2D tex = null, bool instancing = false)
        {
            var m = StylizedMaterials.Create(name, color, smoothness, KindOf(name), default, tex, instancing);
            Mats[name] = SaveAsset(m, "Materials/" + name + ".mat");
            return m;
        }

        private static Material Emissive(string name, Color baseCol, Color emission)
        {
            var m = StylizedMaterials.Create(name, baseCol, 0.6f, StylizedMaterials.Kind.Glow, emission);
            Mats[name] = SaveAsset(m, "Materials/" + name + ".mat");
            return m;
        }

        private static Material Custom(string name, string shaderName)
        {
            Shader s = Shader.Find(shaderName);
            if (s == null)
                throw new System.Exception("[Starlit Tides] Shader '" + shaderName + "' not found. Copy the Phase 2 Shaders folder into Assets/_StarlitTides/Shaders first.");
            var m = new Material(s) { name = name };
            Mats[name] = SaveAsset(m, "Materials/" + name + ".mat");
            return m;
        }

        private static Material M(string name) => Mats[name];

        private static void CreateMaterials()
        {
            // walls, trims, roofs
            Lit("M_WallCream", new Color(0.80f, 0.74f, 0.63f));
            Lit("M_WallBlue", new Color(0.56f, 0.64f, 0.70f));
            Lit("M_WallRose", new Color(0.78f, 0.62f, 0.57f));
            Lit("M_WoodTrim", new Color(0.22f, 0.15f, 0.10f), 0.2f);
            Lit("M_RoofTeal", new Color(0.13f, 0.32f, 0.36f), 0.35f);
            Lit("M_RoofTerracotta", new Color(0.55f, 0.24f, 0.17f), 0.25f);
            Lit("M_RoofSlate", new Color(0.18f, 0.2f, 0.29f), 0.4f);
            Lit("M_Stone", new Color(0.36f, 0.35f, 0.39f), 0.1f);
            Lit("M_WoodPlank", new Color(0.37f, 0.27f, 0.19f), 0.15f);
            Lit("M_WoodPost", new Color(0.19f, 0.14f, 0.11f), 0.1f);
            Lit("M_Metal", new Color(0.07f, 0.07f, 0.08f), 0.45f);
            Lit("M_Rock", new Color(0.29f, 0.30f, 0.34f), 0.12f, null, true);

            Lit("M_ClothTeal", Color.white, 0.05f, StripeTexture("T_StripeTeal", new Color(0.08f, 0.42f, 0.48f)));
            Lit("M_ClothRed", Color.white, 0.05f, StripeTexture("T_StripeRed", new Color(0.62f, 0.16f, 0.16f)));
            Lit("M_ClothGold", Color.white, 0.05f, StripeTexture("T_StripeGold", new Color(0.78f, 0.56f, 0.16f)));

            // glowing things (HDR emission values > 1 feed the Bloom)
            Emissive("M_LampGlow", new Color(1f, 0.8f, 0.5f), new Color(2.6f, 1.35f, 0.4f));
            Emissive("M_WindowLit", new Color(0.3f, 0.2f, 0.1f), new Color(2.3f, 1.2f, 0.38f));
            Emissive("M_WindowWarm", new Color(0.3f, 0.2f, 0.1f), new Color(2.2f, 0.85f, 0.25f));
            Lit("M_WindowOff", new Color(0.03f, 0.04f, 0.07f), 0.9f);
            Emissive("M_BulbGlow", new Color(1f, 0.85f, 0.6f), new Color(3.2f, 1.7f, 0.55f));
            Emissive("M_LighthouseGlass", new Color(1f, 0.95f, 0.8f), new Color(9f, 7f, 4f));
            Lit("M_LighthouseWhite", new Color(0.86f, 0.86f, 0.83f), 0.3f);
            Lit("M_LighthouseRed", new Color(0.55f, 0.09f, 0.1f), 0.3f);

            // vegetation (GPU instancing on: hundreds of trees share these)
            Lit("M_Bark", new Color(0.24f, 0.16f, 0.11f), 0.05f, null, true);
            Lit("M_PineLeaves", new Color(0.07f, 0.19f, 0.15f), 0.05f, null, true);
            Lit("M_RoundLeaves", new Color(0.11f, 0.24f, 0.14f), 0.05f, null, true);
            Lit("M_PalmLeaves", new Color(0.14f, 0.32f, 0.17f), 0.1f, null, true);

            // mountains: each layer bluer and lighter -> aerial perspective
            Lit("M_Mountain1", new Color(0.07f, 0.07f, 0.12f), 0.0f);
            Lit("M_Mountain2", new Color(0.09f, 0.09f, 0.16f), 0.0f);
            Lit("M_Mountain3", new Color(0.12f, 0.11f, 0.21f), 0.0f);
            // Phase 8: moonlit snow caps (vertex mask from ProcMesh.MountainRidge),
            // each layer's snow a little bluer and dimmer: aerial perspective
            StylizedMaterials.SetAccent(M("M_Mountain1"), new Color(0.46f, 0.5f, 0.68f));
            StylizedMaterials.SetAccent(M("M_Mountain2"), new Color(0.38f, 0.42f, 0.62f));
            StylizedMaterials.SetAccent(M("M_Mountain3"), new Color(0.32f, 0.35f, 0.56f));
            StylizedMaterials.SetAccent(M("M_Rock"), new Color(0.17f, 0.27f, 0.25f));   // moss on beach rocks
            M("M_Rock").SetShaderPassEnabled("SRPDefaultUnlit", false);   // Step 6: hundreds of small rocks, outlines not worth the draw calls

            // custom shaders
            // Moody night sea: deep navy, teal only in the shallows. Glow accents are
            // mostly cyan, with rare violet blooms and the odd gold sparkle.
            Material ocean = Custom("M_Ocean", "StarlitTides/Ocean");
            ocean.SetColor("_DeepColor", new Color(0.006f, 0.018f, 0.07f));
            ocean.SetColor("_ShallowColor", new Color(0.03f, 0.32f, 0.42f));
            ocean.SetColor("_HorizonColor", new Color(0.07f, 0.08f, 0.2f));
            ocean.SetColor("_BioColor", new Color(0.12f, 2.5f, 3.2f));
            ocean.SetColor("_BioColor2", new Color(1.1f, 0.5f, 3.0f));
            ocean.SetColor("_SparkleColor3", new Color(3.0f, 2.1f, 0.6f));

            // A deep, painterly night: navy zenith, a soft violet horizon with a faint
            // teal glow, a rich Milky Way, a big moon halo and moonlit cloud banks.
            Material sky = Custom("M_NightSky", "Skybox/StarlitNight");
            sky.SetColor("_ZenithColor", new Color(0.008f, 0.012f, 0.05f));
            sky.SetColor("_MidColor", new Color(0.04f, 0.035f, 0.13f));
            sky.SetColor("_HorizonColor", new Color(0.17f, 0.1f, 0.27f));
            sky.SetColor("_HorizonGlow", new Color(0.04f, 0.17f, 0.24f));
            sky.SetFloat("_StarBrightness", 3.0f);
            sky.SetFloat("_GalaxyIntensity", 1.35f);
            sky.SetColor("_GalaxyColorA", new Color(0.36f, 0.22f, 0.6f));
            sky.SetColor("_GalaxyColorB", new Color(0.14f, 0.42f, 0.6f));
            sky.SetColor("_MoonHaloColor", new Color(0.4f, 0.48f, 0.85f));
            sky.SetFloat("_MoonHalo", 1.5f);
            sky.SetFloat("_AuroraDistance", 5.5f);          // the faint aurora sits far away, low on the horizon
            sky.SetFloat("_NightCloudCover", 0.36f);
            Material beam = Custom("M_LighthouseBeam", "StarlitTides/LightBeam");
            beam.SetColor("_Color", new Color(1.6f, 1.25f, 0.7f));
            beam.SetFloat("_Intensity", 0.9f);

            Texture2D dot = SoftDotTexture();
            Material fire = Custom("M_Firefly", "StarlitTides/GlowParticle");
            fire.SetTexture("_MainTex", dot);
            fire.SetColor("_TintColor", new Color(3.2f, 2.6f, 0.8f));
            Material plankton = Custom("M_PlanktonMote", "StarlitTides/GlowParticle");
            plankton.SetTexture("_MainTex", dot);
            plankton.SetColor("_TintColor", new Color(0.4f, 3.0f, 3.6f));
        }

        // ==================================================================
        // SKY, MOONLIGHT, FOG, CONTROLLERS
        // ==================================================================
        private static void BuildSkyAndLight()
        {
            // Re-use the template's directional light if there is one.
            Light moon = null;
            foreach (Light l in Object.FindObjectsByType<Light>())
                if (l.type == LightType.Directional) { moon = l; break; }
            if (moon == null) moon = new GameObject("Moonlight").AddComponent<Light>();
            moon.gameObject.name = "Moonlight";
            moon.type = LightType.Directional;
            // ahead-right, low: moon path across the sea. Phase 8 lowered it from 12 deg
            // to about 8 deg above the horizon so it stays in the higher gameplay framing.
            Vector3 toMoon = new Vector3(0.32f, 0.14f, 0.92f).normalized;
            moon.transform.rotation = Quaternion.LookRotation(-toMoon, Vector3.up);
            moon.color = new Color(0.66f, 0.76f, 1f);
            moon.intensity = 1.5f;
            moon.shadows = LightShadows.Soft;
            moon.shadowStrength = 0.85f;
            moon.lightmapBakeType = LightmapBakeType.Realtime;

            RenderSettings.skybox = M("M_NightSky");
            RenderSettings.sun = moon;
            RenderSettings.defaultReflectionMode = DefaultReflectionMode.Skybox;
            RenderSettings.reflectionIntensity = 1f;

            // Fully dynamic lighting: no baked GI (the sky changes during play).
            Lightmapping.bakedGI = false;
            Lightmapping.realtimeGI = false;

            // Sky fill: the moon is AHEAD of the boat, so everything the camera sees
            // would show its unlit side and read as a flat silhouette. A soft, cool
            // light from behind the camera (the glow of the night sky) models the
            // fronts of the boat, rocks and creatures. No shadows: it's sky light.
            GameObject fillGo = Child("Sky Fill Light");
            fillGo.transform.rotation = Quaternion.LookRotation(new Vector3(-0.25f, -0.55f, 1f).normalized, Vector3.up);
            Light fill = fillGo.AddComponent<Light>();
            fill.type = LightType.Directional;
            fill.color = new Color(0.45f, 0.55f, 1f);
            fill.intensity = 0.4f;
            fill.shadows = LightShadows.None;
            fill.lightmapBakeType = LightmapBakeType.Realtime;

            GameObject controllers = Child("Controllers");
            var sky = controllers.AddComponent<SkyController>();
            sky.moonLight = moon;
            var ocean = controllers.AddComponent<OceanController>();
            ocean.oceanMaterial = M("M_Ocean");
            DynamicGI.UpdateEnvironment();
        }

        // ==================================================================
        // TERRAIN: heights, ground layers, wind-swaying grass
        // ==================================================================
        private static void BuildTerrain()
        {
            var td = new TerrainData();
            td.heightmapResolution = HeightRes;
            td.size = TerrainSize;

            // ---- heights ----
            var heights = new float[HeightRes, HeightRes];
            for (int j = 0; j < HeightRes; j++)
            for (int i = 0; i < HeightRes; i++)
            {
                Vector2 l = WorldLayout.ToLayoutXZ(TerrainPos.x + i / (float)(HeightRes - 1) * TerrainSize.x,
                                                   TerrainPos.z + j / (float)(HeightRes - 1) * TerrainSize.z);
                heights[j, i] = Mathf.Clamp01((Height(l.x, l.y) - TerrainPos.y) / TerrainSize.y);
            }
            td.SetHeights(0, 0, heights);

            // ---- ground layers: sand, grass, rock, path ----
            var layers = new[]
            {
                MakeLayer("TL_Sand", GroundTexture("T_Sand", new Color(0.62f, 0.57f, 0.48f), new Color(0.8f, 0.77f, 0.7f), 0.35f, 1, 0.6f), 6f, 0.05f),
                MakeLayer("TL_Grass", GroundTexture("T_Grass", new Color(0.24f, 0.36f, 0.24f), new Color(0.32f, 0.44f, 0.26f), 0.55f, 2, 0.8f), 8f, 0.0f),
                MakeLayer("TL_Rock", GroundTexture("T_Rock", new Color(0.30f, 0.30f, 0.34f), new Color(0.45f, 0.45f, 0.5f), 0.7f, 3, 1.6f), 10f, 0.1f),
                MakeLayer("TL_Path", GroundTexture("T_Path", new Color(0.42f, 0.34f, 0.27f), new Color(0.52f, 0.48f, 0.45f), 0.5f, 4, 0.9f), 5f, 0.05f),
            };
            td.terrainLayers = layers;
            td.alphamapResolution = SplatRes;
            var alpha = new float[SplatRes, SplatRes, 4];
            for (int j = 0; j < SplatRes; j++)
            for (int i = 0; i < SplatRes; i++)
            {
                Vector2 l = WorldLayout.ToLayoutXZ(TerrainPos.x + (i + 0.5f) / SplatRes * TerrainSize.x,
                                                   TerrainPos.z + (j + 0.5f) / SplatRes * TerrainSize.z);
                float x = l.x, z = l.y;
                float s = Inland(x, z);
                float y = Height(x, z);
                float slope = Slope(x, z);
                float n = Fbm(x * 0.05f, z * 0.05f, 2);

                float sand = 1f - Smooth(7f, 12f + n * 4f, s);
                if (InLagoonOrChannel(x, z, 1.15f)) sand = Mathf.Max(sand, 0.8f);
                float rock = Smooth(30f, 42f, slope);
                float path = Smooth(3.2f, 1.6f, DistanceToPath(x, z) + n * 1.5f) * Smooth(4f, 9f, s);
                float dSq = Vector2.Distance(new Vector2(x, z), SquareCenter);
                path = Mathf.Max(path, Smooth(20f, 17f, dSq + n * 3f));
                if (y < -0.5f) { sand = 1f; rock *= 0.5f; }

                float grass = Mathf.Clamp01(1f - sand - rock - path);
                float sum = sand + grass + rock + path + 1e-5f;
                alpha[j, i, 0] = sand / sum;
                alpha[j, i, 1] = grass / sum;
                alpha[j, i, 2] = rock / sum;
                alpha[j, i, 3] = path / sum;
            }
            td.SetAlphamaps(0, 0, alpha);

            // ---- grass details (waving grass shader, uses terrain wind) ----
            Texture2D grassTex = GrassBladeTexture();
            td.SetDetailScatterMode(DetailScatterMode.InstanceCountMode);
            td.SetDetailResolution(DetailRes, 32);
            td.detailPrototypes = new[]
            {
                new DetailPrototype
                {
                    prototypeTexture = grassTex,
                    renderMode = DetailRenderMode.Grass,
                    usePrototypeMesh = false,
                    healthyColor = new Color(0.40f, 0.62f, 0.42f),
                    dryColor = new Color(0.48f, 0.54f, 0.32f),
                    minWidth = 0.7f, maxWidth = 1.2f,
                    minHeight = 0.45f, maxHeight = 1.0f,
                    noiseSpread = 0.35f
                }
            };
            var detail = new int[DetailRes, DetailRes];
            for (int j = 0; j < DetailRes; j++)
            for (int i = 0; i < DetailRes; i++)
            {
                Vector2 l = WorldLayout.ToLayoutXZ(TerrainPos.x + (i + 0.5f) / DetailRes * TerrainSize.x,
                                                   TerrainPos.z + (j + 0.5f) / DetailRes * TerrainSize.z);
                float x = l.x, z = l.y;
                float s = Inland(x, z);
                if (s < 11f || x < -260f || z > 380f) continue;                     // keep it near the action
                if (InLagoonOrChannel(x, z, 1.25f)) continue;
                if (DistanceToPath(x, z) < 2.5f) continue;
                if (Vector2.Distance(new Vector2(x, z), SquareCenter) < 22f) continue;
                if (Slope(x, z) > 28f) continue;
                float n = Fbm(x * 0.04f + 50f, z * 0.04f, 3);
                detail[j, i] = Mathf.RoundToInt(Mathf.Clamp01((n - 0.3f) * 2.2f) * 10f);
            }
            td.SetDetailLayer(0, 0, 0, detail);
            td.wavingGrassStrength = 0.45f;
            td.wavingGrassSpeed = 0.45f;
            td.wavingGrassAmount = 0.45f;
            td.wavingGrassTint = new Color(0.55f, 0.68f, 0.62f);

            SaveAsset(td, "Terrain/StarlitTerrainData.asset");
            // The splat (ground layer) weights live in textures that must be saved
            // INSIDE the TerrainData asset. Painted before the asset existed, they were
            // never stored, so Unity fell back to "layer 0 everywhere" and every hill
            // rendered as pale sand. Re-apply them now and add them as sub-assets.
            td.SetAlphamaps(0, 0, alpha);
            foreach (Texture2D splat in td.alphamapTextures)
                if (splat != null && !AssetDatabase.Contains(splat)) AssetDatabase.AddObjectToAsset(splat, td);
            EditorUtility.SetDirty(td);

            GameObject go = Terrain.CreateTerrainGameObject(td);
            go.name = "Terrain";
            go.transform.SetParent(_root, false);
            go.transform.position = TerrainPos;
            _terrain = go.GetComponent<Terrain>();
            _terrain.materialTemplate = new Material(Shader.Find("Universal Render Pipeline/Terrain/Lit"));
            _terrain.materialTemplate.EnableKeyword("_NORMALMAP");                       // use the layers' normal maps
            SaveAsset(_terrain.materialTemplate, "Materials/M_Terrain.mat");
            _terrain.heightmapPixelError = 2f;
            _terrain.basemapDistance = 600f;
            _terrain.detailObjectDistance = 110f;
            _terrain.detailObjectDensity = 1f;
            _terrain.drawInstanced = true;
            _terrain.shadowCastingMode = ShadowCastingMode.On;
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
        }

        private static TerrainLayer MakeLayer(string name, Texture2D tex, float tile, float smoothness)
        {
            var layer = new TerrainLayer { diffuseTexture = tex, tileSize = new Vector2(tile, tile), smoothness = smoothness, metallic = 0f };
            layer.normalMapTexture = _lastGroundNormal;
            layer.normalScale = 1f;
            return SaveAsset(layer, "Terrain/" + name + ".terrainlayer");
        }

        // ==================================================================
        // OCEAN
        // ==================================================================
        private static void BuildOcean()
        {
            Mesh grid = SaveMesh(OceanGrid(2400f, 420, 200f));
            GameObject sea = MeshObject("Ocean", grid, new[] { M("M_Ocean") }, _root, Vector3.zero, Quaternion.identity, false, false);
            sea.GetComponent<MeshRenderer>().receiveShadows = false;
            sea.GetComponent<MeshRenderer>().lightProbeUsage = LightProbeUsage.Off;
        }

        // Linear core (1:1 spacing) blended into a power curve, so the play
        // area gets ~1.3 m vertices and the horizon still reaches 2.4 km.
        private static Mesh OceanGrid(float halfExtent, int res, float linearCore)
        {
            Mesh m = ProcMesh.OceanGrid(1f, res, 1f);   // unit grid -1..1
            Vector3[] v = m.vertices;
            const float power = 3f;
            for (int i = 0; i < v.Length; i++)
            {
                float Map(float u) => Mathf.Sign(u) * (linearCore * Mathf.Abs(u) + (halfExtent - linearCore) * Mathf.Pow(Mathf.Abs(u), power));
                v[i] = new Vector3(Map(v[i].x), 0f, Map(v[i].z));
            }
            m.vertices = v;
            m.bounds = new Bounds(Vector3.zero, new Vector3(halfExtent * 2f, 20f, halfExtent * 2f));
            m.name = "OceanGrid";
            return m;
        }

        // ==================================================================
        // VILLAGE: houses, square, well, festoon lights, market, lanterns
        // ==================================================================
        private static void BuildVillage()
        {
            Transform village = Child("Village").transform;
            _lanternMesh = SaveMesh(ProcMesh.LanternPost(3.6f, out _));
            _hangingLanternMesh = SaveMesh(ProcMesh.HangingLantern());
            _windowPaneMesh = SaveMesh(WindowPane());

            // ---- houses around the square, doors facing the centre ----
            string[] walls = { "M_WallCream", "M_WallBlue", "M_WallRose" };
            string[] roofs = { "M_RoofTeal", "M_RoofTerracotta", "M_RoofSlate" };
            float[] angles = { 28f, 52f, 108f, 136f, 164f, 196f, 228f, 262f, 300f, 330f };   // gaps at 0 deg (pier) and ~80 deg (bridge)
            var houseSpots = new List<(Vector2 pos, float yawToFace)>();
            foreach (float a in angles)
            {
                float rad = a * Mathf.Deg2Rad;
                float dist = R(16f, 19f);
                Vector2 p = SquareCenter + new Vector2(Mathf.Cos(rad), Mathf.Sin(rad)) * dist;
                houseSpots.Add((p, YawToward(p, SquareCenter)));
            }
            // two houses by the beach looking out to sea, one up the bridge path
            houseSpots.Add((new Vector2(-36f, 4f), YawToward(new Vector2(-36f, 4f), new Vector2(0f, 10f))));
            houseSpots.Add((new Vector2(-37f, 62f), YawToward(new Vector2(-37f, 62f), new Vector2(0f, 70f))));

            int index = 0;
            foreach (var spot in houseSpots)
            {
                Mesh mesh = SaveMesh(ProcMesh.House(100 + index, out List<WindowSlot> windows, out _));
                float gy = Mathf.Max(GroundY(spot.pos.x, spot.pos.y), SquareGroundY - 0.3f);
                var mats = new[] { M(walls[index % 3]), M("M_WoodTrim"), M(roofs[(index * 2 + 1) % 3]), M("M_Stone") };
                Quaternion rot = Quaternion.Euler(0f, spot.yawToFace + R(-6f, 6f), 0f);
                GameObject house = MeshObject("House_" + (index + 1), mesh, mats, village, new Vector3(spot.pos.x, gy, spot.pos.y), rot);

                // glowing windows (separate objects so Living Village can switch them)
                Transform winRoot = Child("Windows", house.transform).transform;
                int w = 0;
                foreach (WindowSlot slot in windows)
                {
                    float roll = (float)_rng.NextDouble();
                    string mat = roll < 0.55f ? "M_WindowLit" : roll < 0.8f ? "M_WindowWarm" : "M_WindowOff";
                    GameObject pane = MeshObject("Window_" + (++w), _windowPaneMesh, new[] { M(mat) }, winRoot, Vector3.zero, Quaternion.identity, false, false);
                    pane.transform.localPosition = slot.position;
                    pane.transform.localRotation = slot.rotation;
                    pane.transform.localScale = new Vector3(slot.size.x, slot.size.y, 1f);
                    pane.transform.localPosition += Vector3.up * (slot.size.y * 0f);
                }

                // warm light spilling from the front of every other house
                if (index % 2 == 0)
                {
                    Vector3 front = house.transform.TransformPoint(new Vector3(0f, 1.6f, -3.6f));
                    AddPointLight(house.transform, front, new Color(1f, 0.66f, 0.35f), 2.5f, 7f);
                }
                index++;
            }

            // ---- square: well, central festoon pole, lanterns, stalls ----
            Transform square = Child("Village Square", village).transform;
            Vector3 c3 = new Vector3(SquareCenter.x, SquareGroundY, SquareCenter.y);
            BuildWell(square, c3 + new Vector3(3.5f, 0f, -3f));

            var lanternTops = new List<Vector3>();
            for (int i = 0; i < 6; i++)
            {
                float a = (15f + i * 60f) * Mathf.Deg2Rad;
                Vector3 p = c3 + new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * 12.5f;
                PlaceLantern(square, p, YawToward(new Vector2(p.x, p.z), SquareCenter) + 90f);
                lanternTops.Add(p + Vector3.up * 3.55f);
            }
            BuildFestoon(square, c3 + new Vector3(-2f, 0f, 2f), lanternTops);

            // market stalls on the west side of the square, facing the centre
            string[] cloth = { "M_ClothTeal", "M_ClothRed", "M_ClothGold" };
            for (int i = 0; i < 5; i++)
            {
                float a = (135f + i * 22.5f) * Mathf.Deg2Rad;
                Vector2 p = SquareCenter + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * 8.5f;
                Mesh stall = SaveMesh(ProcMesh.MarketStall(300 + i, out Vector3 hook));
                Quaternion rot = Quaternion.Euler(0f, YawToward(p, SquareCenter), 0f);
                Vector3 pos = new Vector3(p.x, SquareGroundY, p.y);
                GameObject go = MeshObject("Market Stall " + (i + 1), stall, new[] { M("M_WoodPlank"), M(cloth[i % 3]) }, square, pos, rot);
                Vector3 hookWorld = go.transform.TransformPoint(hook);
                GameObject lamp = MeshObject("Stall Lantern", _hangingLanternMesh, new[] { M("M_Metal"), M("M_LampGlow") }, go.transform, hookWorld, rot, false);
                Light l = AddPointLight(lamp.transform, hookWorld + Vector3.down * 0.2f, Gold, 1.6f, 5f);
                var flick = lamp.AddComponent<LanternFlicker>();
                flick.targetLight = l;
            }

            // ---- lantern street: pier -> square and square -> bridge ----
            LanternsAlong(village, PierStart, SquareCenter, 9f, 2.4f, 0.12f, 0.85f);
            LanternsAlong(village, SquareCenter, new Vector2(BridgeX, 84f), 10f, 2.4f, 0.25f, 0.9f);

            // a few crates and barrels for life
            for (int i = 0; i < 14; i++)
            {
                Vector2 p = SquareCenter + Random2(R(10f, 14.5f));
                float gy = GroundY(p.x, p.y);
                Mesh crate = SaveMesh(CrateMesh(400 + i));
                MeshObject("Crate", crate, new[] { M("M_WoodPlank") }, village, new Vector3(p.x, gy, p.y), Quaternion.Euler(0f, R(0f, 360f), 0f));
            }
        }

        private static float YawToward(Vector2 from, Vector2 to)
        {
            Vector2 d = to - from;
            // Our house fronts face local -Z, so rotate -Z onto the direction.
            return Mathf.Atan2(-d.x, -d.y) * Mathf.Rad2Deg;
        }

        private static Vector2 Random2(float radius)
        {
            float a = R(0f, Mathf.PI * 2f);
            return new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius;
        }

        private static Mesh WindowPane()
        {
            // unit quad facing -Z, centred, so it can be scaled to any window size
            var mb = new MeshBuilder(1);
            mb.Quad(0, new Vector3(-0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(0.5f, -0.5f, 0), Vector3.back);
            return mb.ToMesh("WindowPane");
        }

        private static Mesh CrateMesh(int seed)
        {
            var r = new System.Random(seed);
            var mb = new MeshBuilder(1);
            float s = 0.5f + (float)r.NextDouble() * 0.35f;
            if (r.NextDouble() < 0.5)
            {
                mb.Box(0, new Vector3(0, s * 0.5f, 0), Vector3.one * s);
                if (r.NextDouble() < 0.5) mb.Box(0, new Vector3(0.1f, s + s * 0.35f, 0.05f), Vector3.one * s * 0.7f, Quaternion.Euler(0, 20, 0));
            }
            else
            {
                mb.Cylinder(0, Vector3.zero, s * 0.42f, s * 0.42f, s * 1.1f, 8, true);   // barrel
            }
            return mb.ToMesh("Crate_" + seed);
        }

        private static Light AddPointLight(Transform parent, Vector3 worldPos, Color color, float intensity, float range)
        {
            var go = new GameObject("Point Light");
            go.transform.SetParent(parent, true);
            go.transform.position = worldPos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color;
            l.intensity = intensity;
            l.range = range;
            l.shadows = LightShadows.None;
            l.lightmapBakeType = LightmapBakeType.Realtime;
            return l;
        }

        private static void PlaceLantern(Transform parent, Vector3 groundPos, float yaw, float scale = 1f)
        {
            Quaternion rot = Quaternion.Euler(0f, yaw, 0f);
            GameObject go = MeshObject("Lantern Post", _lanternMesh, new[] { M("M_Metal"), M("M_LampGlow") }, parent, groundPos, rot, true);
            go.transform.localScale = Vector3.one * scale;
            Vector3 lamp = go.transform.TransformPoint(new Vector3(0.55f, 3.6f - 0.55f, 0f));
            Light l = AddPointLight(go.transform, lamp, Gold, 4f, 9f);
            var f = go.AddComponent<LanternFlicker>();
            f.targetLight = l;
            f.glowRenderer = go.GetComponent<MeshRenderer>();
            f.glowColor = new Color(2.6f, 1.35f, 0.4f);
        }

        private static void LanternsAlong(Transform parent, Vector2 a, Vector2 b, float spacing, float sideOffset, float tStart, float tEnd)
        {
            Vector2 d = (b - a).normalized;
            Vector2 side = new Vector2(-d.y, d.x);
            float len = Vector2.Distance(a, b);
            int n = Mathf.Max(1, Mathf.FloorToInt(len * (tEnd - tStart) / spacing));
            for (int i = 0; i <= n; i++)
            {
                float t = Mathf.Lerp(tStart, tEnd, i / (float)n);
                float sign = (i % 2 == 0) ? 1f : -1f;
                Vector2 p = Vector2.Lerp(a, b, t) + side * sideOffset * sign;
                float yaw = Mathf.Atan2(-side.x * sign, -side.y * sign) * Mathf.Rad2Deg + 90f;
                PlaceLantern(parent, new Vector3(p.x, GroundY(p.x, p.y), p.y), yaw);
            }
        }

        private static void BuildWell(Transform parent, Vector3 pos)
        {
            var mb = new MeshBuilder(3);                       // 0 stone, 1 wood, 2 water
            mb.Cylinder(0, Vector3.zero, 1.25f, 1.2f, 0.9f, 12, false);
            mb.Cylinder(0, Vector3.up * 0.9f, 1.3f, 1.3f, 0.12f, 12, true);
            mb.Cylinder(2, Vector3.up * 0.5f, 1.0f, 1.0f, 0.01f, 12, true);
            mb.Box(1, new Vector3(-1.1f, 1.6f, 0), new Vector3(0.15f, 1.6f, 0.15f));
            mb.Box(1, new Vector3(1.1f, 1.6f, 0), new Vector3(0.15f, 1.6f, 0.15f));
            mb.Beam(1, new Vector3(-1.2f, 2.3f, 0), new Vector3(1.2f, 2.3f, 0), 0.12f, 0.12f);
            mb.Quad(1, new Vector3(-1.5f, 2.35f, -0.9f), new Vector3(-1.5f, 3.0f, 0), new Vector3(1.5f, 3.0f, 0), new Vector3(1.5f, 2.35f, -0.9f), new Vector3(0, 1, -1));
            mb.Quad(1, new Vector3(1.5f, 2.35f, 0.9f), new Vector3(1.5f, 3.0f, 0), new Vector3(-1.5f, 3.0f, 0), new Vector3(-1.5f, 2.35f, 0.9f), new Vector3(0, 1, 1));
            Mesh m = SaveMesh(mb.ToMesh("Well"));
            Material water = Emissive("M_WellWater", new Color(0.02f, 0.08f, 0.1f), new Color(0.1f, 0.9f, 1.1f));
            MeshObject("Well", m, new[] { M("M_Stone"), M("M_WoodTrim"), water }, parent, pos, Quaternion.Euler(0, 30, 0));
        }

        // Strings of glowing bulbs from a central pole to the lantern posts.
        private static void BuildFestoon(Transform parent, Vector3 poleBase, List<Vector3> anchors)
        {
            var mb = new MeshBuilder(2);
            float poleH = 7.5f;
            mb.Cylinder(0, Vector3.zero, 0.16f, 0.12f, poleH, 8);
            Vector3 top = Vector3.up * (poleH - 0.3f);
            foreach (Vector3 anchorWorld in anchors)
            {
                Vector3 anchor = anchorWorld - poleBase;
                float len = Vector3.Distance(top, anchor);
                float sag = len * 0.08f;
                int bulbs = Mathf.FloorToInt(len / 0.9f);
                Vector3 prev = top;
                for (int i = 1; i <= bulbs; i++)
                {
                    float t = i / (float)(bulbs + 1);
                    Vector3 p = Vector3.Lerp(top, anchor, t) + Vector3.down * (sag * 4f * t * (1f - t));
                    mb.Beam(0, prev, p, 0.02f, 0.02f);
                    mb.Box(1, p + Vector3.down * 0.1f, new Vector3(0.12f, 0.16f, 0.12f));
                    prev = p;
                }
                mb.Beam(0, prev, anchor, 0.02f, 0.02f);
            }
            Mesh m = SaveMesh(mb.ToMesh("FestoonLights"));
            MeshObject("Festoon Lights", m, new[] { M("M_WoodPost"), M("M_BulbGlow") }, parent, poleBase, Quaternion.identity, true, false);
            AddPointLight(parent, poleBase + Vector3.up * 6.5f, Gold, 2.2f, 14f);
        }

        // ==================================================================
        // PIER - planks with gaps, stringers, posts into the sea floor
        // ==================================================================
        private static void BuildPier()
        {
            Transform pier = Child("Pier").transform;
            var mb = new MeshBuilder(2);                     // 0 planks, 1 posts
            Vector3 a = new Vector3(PierStart.x, PierDeckY, PierStart.y);
            Vector3 b = new Vector3(PierEnd.x, PierDeckY, PierEnd.y);
            Vector3 dir = (b - a).normalized;
            Vector3 side = Vector3.Cross(Vector3.up, dir).normalized;
            float len = Vector3.Distance(a, b);
            float width = 2.6f;
            Quaternion rot = Quaternion.LookRotation(dir);

            // deck planks
            for (float d = 0f; d < len; d += 0.48f)
            {
                Vector3 c = a + dir * d + Vector3.up * R(-0.015f, 0.015f);
                mb.Box(0, c, new Vector3(width + R(-0.1f, 0.1f), 0.08f, 0.4f), rot * Quaternion.Euler(0f, R(-1.5f, 1.5f), 0f));
            }
            // T-shaped platform at the end
            Vector3 endC = b + dir * 2f;
            for (float d = -2f; d <= 2f; d += 0.48f)
                mb.Box(0, endC + dir * d, new Vector3(7f, 0.08f, 0.4f), rot);
            // stringers
            mb.Beam(1, a - side * 1f + Vector3.down * 0.15f, b + dir * 4f - side * 1f + Vector3.down * 0.15f, 0.14f, 0.2f);
            mb.Beam(1, a + side * 1f + Vector3.down * 0.15f, b + dir * 4f + side * 1f + Vector3.down * 0.15f, 0.14f, 0.2f);
            // posts every 3 m down into the sea floor
            for (float d = 0f; d <= len + 4f; d += 3f)
            {
                foreach (float sgn in new[] { -1f, 1f })
                {
                    float off = d > len ? 3.3f : 1.2f;
                    Vector3 top = a + dir * d + side * (off * sgn);
                    float floor = GroundY(top.x, top.z) - 1f;
                    mb.Cylinder(1, new Vector3(top.x, floor, top.z), 0.16f, 0.14f, PierDeckY + 0.5f - floor, 6);
                }
            }
            // railing on the sea-facing side
            for (float d = 2f; d < len; d += 2f)
            {
                Vector3 p = a + dir * d + side * 1.25f;
                mb.Box(1, p + Vector3.up * 0.5f, new Vector3(0.1f, 1f, 0.1f));
            }
            mb.Beam(1, a + dir * 2f + side * 1.25f + Vector3.up * 1f, a + dir * (len - 0.5f) + side * 1.25f + Vector3.up * 1f, 0.08f, 0.08f);

            Mesh m = SaveMesh(mb.ToMesh("Pier"));
            MeshObject("Pier Deck", m, new[] { M("M_WoodPlank"), M("M_WoodPost") }, pier, Vector3.zero, Quaternion.identity);

            // lanterns along the pier and at its end
            for (float d = 5f; d < len; d += 8f)
                PlaceLantern(pier, a + dir * d - side * 1.2f + Vector3.up * 0.04f, Mathf.Atan2(side.x, side.z) * Mathf.Rad2Deg + 90f, 0.8f);
            PlaceLantern(pier, endC + side * 3.2f, Mathf.Atan2(-side.x, -side.z) * Mathf.Rad2Deg + 90f, 0.8f);
            PlaceLantern(pier, endC - side * 3.2f, Mathf.Atan2(side.x, side.z) * Mathf.Rad2Deg + 90f, 0.8f);

            // moored boats (only if prefabs were provided)
            if (_overrides != null && _overrides.boatPrefabs != null && _overrides.boatPrefabs.Length > 0)
            {
                for (int i = 0; i < 2; i++)
                {
                    GameObject prefab = _overrides.boatPrefabs[i % _overrides.boatPrefabs.Length];
                    if (prefab == null) continue;
                    var boat = (GameObject)PrefabUtility.InstantiatePrefab(prefab, pier);
                    boat.transform.position = a + dir * (8f + i * 9f) + side * (i == 0 ? 3.2f : -3.2f) + Vector3.down * (PierDeckY - 0.1f);
                    boat.transform.rotation = rot;
                }
            }
        }

        // ==================================================================
        // ARCHED BRIDGE over the lagoon channel
        // ==================================================================
        private static void BuildBridge()
        {
            Transform bridge = Child("Bridge").transform;
            float z0 = ChannelZ - 12f, z1 = ChannelZ + 12f;
            float y0 = GroundY(BridgeX, z0) + 0.15f, y1 = GroundY(BridgeX, z1) + 0.15f;
            float rise = 3.4f, width = 3.2f;
            int segs = 18;
            var mb = new MeshBuilder(2);                     // 0 deck/rails (wood), 1 arch (stone)

            Vector3 P(float t) => new Vector3(BridgeX, Mathf.Lerp(y0, y1, t) + rise * Mathf.Sin(Mathf.PI * t), Mathf.Lerp(z0, z1, t));
            for (int i = 0; i < segs; i++)
            {
                Vector3 p0 = P(i / (float)segs), p1 = P((i + 1) / (float)segs);
                mb.Beam(0, p0, p1, width, 0.18f);                                       // deck
                mb.Beam(1, p0 + Vector3.down * 0.55f, p1 + Vector3.down * 0.55f, width - 0.4f, 0.9f);   // stone arch
                foreach (float sgn in new[] { -1f, 1f })
                {
                    Vector3 off = new Vector3(sgn * (width * 0.5f - 0.08f), 0f, 0f);
                    mb.Box(0, p0 + off + Vector3.up * 0.5f, new Vector3(0.12f, 1.0f, 0.12f));      // rail posts
                    mb.Beam(0, p0 + off + Vector3.up * 1.0f, p1 + off + Vector3.up * 1.0f, 0.1f, 0.1f); // hand rail
                    mb.Beam(0, p0 + off + Vector3.up * 0.5f, p1 + off + Vector3.up * 0.5f, 0.06f, 0.06f);
                }
            }
            // stone abutments at both ends
            mb.Box(1, new Vector3(BridgeX, y0 - 1f, z0 - 0.6f), new Vector3(width + 0.6f, 2.4f, 2f));
            mb.Box(1, new Vector3(BridgeX, y1 - 1f, z1 + 0.6f), new Vector3(width + 0.6f, 2.4f, 2f));
            Mesh m = SaveMesh(mb.ToMesh("Bridge"));
            MeshObject("Arched Bridge", m, new[] { M("M_WoodPlank"), M("M_Stone") }, bridge, Vector3.zero, Quaternion.identity);

            foreach (float sgn in new[] { -1f, 1f })
            {
                PlaceLantern(bridge, new Vector3(BridgeX + sgn * 2.2f, y0 - 0.1f, z0 - 1.2f), sgn > 0 ? 0f : 180f, 0.9f);
                PlaceLantern(bridge, new Vector3(BridgeX + sgn * 2.2f, y1 - 0.1f, z1 + 1.2f), sgn > 0 ? 0f : 180f, 0.9f);
            }
        }

        // ==================================================================
        // LIGHTHOUSE with rotating light + two fake-volumetric beams
        // ==================================================================
        private static void BuildLighthouse()
        {
            Transform lh = Child("Lighthouse").transform;
            float gy = GroundY(LighthouseXZ.x, LighthouseXZ.y);
            Vector3 basePos = new Vector3(LighthouseXZ.x, gy, LighthouseXZ.y);
            Mesh tower = SaveMesh(ProcMesh.Lighthouse(out float lampH));
            MeshObject("Lighthouse Tower", tower, new[] { M("M_LighthouseWhite"), M("M_LighthouseRed"), M("M_Metal"), M("M_LighthouseGlass") }, lh, basePos, Quaternion.identity);

            // rotating head: spot light + beam cones (front and back)
            GameObject head = Child("Lamp Head (rotates)", lh);
            head.transform.position = basePos + Vector3.up * lampH;
            head.transform.rotation = Quaternion.Euler(4f, 200f, 0f);   // tilted slightly down toward the sea

            var spotGo = Child("Beam Spot Light", head.transform);
            spotGo.transform.localPosition = Vector3.zero;
            spotGo.transform.localRotation = Quaternion.identity;
            var spot = spotGo.AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.color = new Color(1f, 0.85f, 0.6f);
            spot.intensity = 60f;
            spot.range = 160f;
            spot.spotAngle = 14f;
            spot.innerSpotAngle = 4f;
            spot.shadows = LightShadows.None;

            Mesh cone = SaveMesh(ProcMesh.BeamCone(110f, 11f, 0.5f));
            MeshObject("Beam Front", cone, new[] { M("M_LighthouseBeam") }, head.transform, head.transform.position, head.transform.rotation, false, false);
            GameObject back = MeshObject("Beam Back", cone, new[] { M("M_LighthouseBeam") }, head.transform, head.transform.position, head.transform.rotation * Quaternion.Euler(0f, 180f, 0f), false, false);
            back.transform.localScale = new Vector3(1f, 1f, 0.7f);
            AddPointLight(head.transform, head.transform.position, new Color(1f, 0.85f, 0.6f), 3f, 14f);

            var beam = head.AddComponent<LighthouseBeam>();
            beam.rotatingHead = head.transform;
            beam.spotLight = spot;
            beam.rotationSpeed = 22f;

            // rocks around the cliff base
            for (int i = 0; i < 10; i++)
            {
                float a = R(0f, Mathf.PI * 2f);
                Vector2 p = LighthouseXZ + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * R(14f, 26f);
                if (Inland(p.x, p.y) > 0f) continue;
                Mesh rock = SaveMesh(ProcMesh.Rock(600 + i, R(1.5f, 3.5f)));
                MeshObject("Sea Rock", rock, new[] { M("M_Rock") }, lh, new Vector3(p.x, GroundY(p.x, p.y) + R(0f, 1.5f), p.y), Quaternion.Euler(R(-10, 10), R(0, 360), R(-10, 10)));
            }
        }

        // ==================================================================
        // FOREST - pines on the hills, round trees in the meadows, palms on
        // the beach. Uses override prefabs if provided.
        // ==================================================================
        private static void BuildForest()
        {
            Transform forest = Child("Forest").transform;

            var pines = new List<Mesh>();
            var rounds = new List<Mesh>();
            var palms = new List<Mesh>();
            for (int i = 0; i < 6; i++) pines.Add(SaveMesh(ProcMesh.PineTree(700 + i)));
            for (int i = 0; i < 4; i++) rounds.Add(SaveMesh(ProcMesh.RoundTree(800 + i)));
            for (int i = 0; i < 4; i++) palms.Add(SaveMesh(ProcMesh.PalmTree(900 + i)));

            int placed = 0, attempts = 0;
            while (placed < 650 && attempts < 20000)
            {
                attempts++;
                float x = R(-330f, 10f), z = R(-150f, 420f);
                float s = Inland(x, z);
                if (s < 3f) continue;
                if (InLagoonOrChannel(x, z, 1.25f)) continue;
                if (DistanceToPath(x, z) < 4f) continue;
                if (Vector2.Distance(new Vector2(x, z), SquareCenter) < 30f) continue;
                if (Vector2.Distance(new Vector2(x, z), LighthouseXZ) < 14f) continue;
                if (Mathf.Abs(x - BridgeX) < 8f && Mathf.Abs(z - ChannelZ) < 18f) continue;
                if (Slope(x, z) > 34f) continue;

                float density = Fbm(x * 0.012f + 9f, z * 0.012f, 3);
                bool beach = s < 14f;
                bool hills = s > 55f;
                if (!beach && !hills && density < 0.55f) continue;     // meadows stay open (grassland)
                if (hills && density < 0.38f) continue;
                if (beach && _rng.NextDouble() > 0.25) continue;

                float gy = GroundY(x, z);
                Quaternion rot = Quaternion.Euler(R(-3f, 3f), R(0f, 360f), R(-3f, 3f));
                float scale = R(0.8f, 1.3f);
                GameObject prefab = beach ? Pick(_overrides?.palmTreePrefabs) : hills ? Pick(_overrides?.pineTreePrefabs) : Pick(_overrides?.broadleafTreePrefabs);
                GameObject tree;
                if (prefab != null)
                {
                    tree = (GameObject)PrefabUtility.InstantiatePrefab(prefab, forest);
                    tree.transform.SetPositionAndRotation(new Vector3(x, gy - 0.1f, z), rot);
                }
                else
                {
                    Mesh mesh = beach ? palms[_rng.Next(palms.Count)] : hills ? pines[_rng.Next(pines.Count)] : rounds[_rng.Next(rounds.Count)];
                    Material leaves = beach ? M("M_PalmLeaves") : hills ? M("M_PineLeaves") : M("M_RoundLeaves");
                    tree = MeshObject(beach ? "Palm" : hills ? "Pine" : "Tree", mesh, new[] { M("M_Bark"), leaves }, forest, new Vector3(x, gy - 0.1f, z), rot, false);
                    GameObjectUtility.SetStaticEditorFlags(tree, StaticEditorFlags.OccludeeStatic | StaticEditorFlags.ReflectionProbeStatic);
                }
                tree.transform.localScale = Vector3.one * scale;
                placed++;
            }

            // boulders scattered on the land and a few in the shallows
            for (int i = 0; i < 40; i++)
            {
                float x = R(-200f, -12f), z = R(-80f, 300f);
                if (InLagoonOrChannel(x, z) || DistanceToPath(x, z) < 3f) continue;
                if (Vector2.Distance(new Vector2(x, z), SquareCenter) < 24f) continue;
                if (x > -14f && z < 140f) continue;   // keep the play lane clear
                GameObject prefab = Pick(_overrides?.rockPrefabs);
                Vector3 pos = new Vector3(x, GroundY(x, z) - 0.2f, z);
                if (prefab != null)
                {
                    var r = (GameObject)PrefabUtility.InstantiatePrefab(prefab, forest);
                    r.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, R(0, 360), 0));
                }
                else
                {
                    Mesh rock = SaveMesh(ProcMesh.Rock(1000 + i, R(0.6f, 1.8f)));
                    MeshObject("Boulder", rock, new[] { M("M_Rock") }, forest, pos, Quaternion.Euler(R(-15, 15), R(0, 360), R(-15, 15)));
                }
            }
        }

        private static GameObject Pick(GameObject[] list)
        {
            if (list == null || list.Length == 0) return null;
            GameObject g = list[_rng.Next(list.Length)];
            return g;
        }

        // ==================================================================
        // MOUNTAINS - three ridges, further = lighter/bluer, lost in the fog
        // ==================================================================
        private static void BuildMountains()
        {
            Transform mountains = Child("Mountains").transform;
            float Env(float x) => Mathf.Lerp(0.03f, 1f, Smooth(420f, -200f, x));   // low on the right: moon stays visible
            var layers = new[]
            {
                (z: 640f, h: 150f, depth: 120f, mat: "M_Mountain1", seed: 1),
                (z: 900f, h: 260f, depth: 160f, mat: "M_Mountain2", seed: 2),
                (z: 1250f, h: 380f, depth: 200f, mat: "M_Mountain3", seed: 3),
            };
            foreach (var l in layers)
            {
                Mesh m = SaveMesh(ProcMesh.MountainRidge(l.seed, -2000f, 1300f, l.h, l.depth, 170, Env));
                GameObject go = MeshObject("Mountain Ridge " + l.seed, m, new[] { M(l.mat) }, mountains, new Vector3(0f, 0f, l.z), Quaternion.identity, true, false);
                go.GetComponent<MeshRenderer>().receiveShadows = false;
            }
            // side range to the far left (behind the forest)
            Mesh side = SaveMesh(ProcMesh.MountainRidge(4, -900f, 900f, 200f, 140f, 90, x => 1f));
            GameObject sideGo = MeshObject("Mountain Ridge West", side, new[] { M("M_Mountain2") }, mountains, new Vector3(-700f, 0f, 200f), Quaternion.Euler(0f, -90f, 0f), true, false);
            sideGo.GetComponent<MeshRenderer>().receiveShadows = false;
        }

        // ==================================================================
        // AMBIENT VFX - fireflies over the grass, plankton motes on the tide
        // ==================================================================
        private static void BuildAmbientVfx()
        {
            Transform vfx = Child("Ambient VFX").transform;
            MakeFireflies(vfx, "Fireflies - Meadow", new Vector3(-80f, 4.5f, 30f), new Vector3(70f, 5f, 80f), 30f);
            MakeFireflies(vfx, "Fireflies - Lagoon", new Vector3(-85f, 2.5f, 95f), new Vector3(70f, 4f, 60f), 22f);
            MakeFireflies(vfx, "Fireflies - Village", new Vector3(SquareCenter.x, 4f, SquareCenter.y), new Vector3(40f, 4f, 40f), 10f);

            // glowing plankton motes floating just above the sea, drifting with the tide
            var go = Child("Plankton Motes", vfx);
            go.transform.position = new Vector3(0f, 0.35f, 60f);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.duration = 10f;
            main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(10f, 22f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.05f, 0.14f);
            main.maxParticles = 600;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.6f, 1f, 1f), new Color(0.3f, 0.8f, 1f));
            var em = ps.emission; em.rateOverTime = 30f;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(90f, 0.4f, 140f);
            var vel = ps.velocityOverLifetime; vel.enabled = true; vel.space = ParticleSystemSimulationSpace.World;
            vel.x = new ParticleSystem.MinMaxCurve(-0.1f, 0.1f); vel.y = new ParticleSystem.MinMaxCurve(-0.02f, 0.05f); vel.z = new ParticleSystem.MinMaxCurve(-4.2f, -3.8f);
            var col = ps.colorOverLifetime; col.enabled = true; col.color = BlinkGradient();
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = M("M_PlanktonMote");
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        private static void MakeFireflies(Transform parent, string name, Vector3 center, Vector3 size, float rate)
        {
            var go = Child(name, parent);
            go.transform.position = center;
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = true;
            main.prewarm = true;
            main.startLifetime = new ParticleSystem.MinMaxCurve(5f, 9f);
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.24f);
            main.maxParticles = 400;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(1f, 0.85f, 0.4f), new Color(0.75f, 1f, 0.45f));
            var em = ps.emission; em.rateOverTime = rate;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = size;
            var noise = ps.noise; noise.enabled = true; noise.strength = 0.9f; noise.frequency = 0.25f; noise.scrollSpeed = 0.15f; noise.quality = ParticleSystemNoiseQuality.Medium;
            var col = ps.colorOverLifetime; col.enabled = true; col.color = BlinkGradient();
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = M("M_Firefly");
            r.renderMode = ParticleSystemRenderMode.Billboard;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
        }

        private static ParticleSystem.MinMaxGradient BlinkGradient()
        {
            var g = new Gradient();
            g.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[]
                {
                    new GradientAlphaKey(0f, 0f), new GradientAlphaKey(1f, 0.15f), new GradientAlphaKey(0.25f, 0.35f),
                    new GradientAlphaKey(1f, 0.55f), new GradientAlphaKey(0.4f, 0.8f), new GradientAlphaKey(0f, 1f)
                });
            return new ParticleSystem.MinMaxGradient(g);
        }

        // ==================================================================
        // REFLECTION PROBE (realtime, refreshed by script)
        // ==================================================================
        private static void BuildReflectionProbe()
        {
            GameObject go = Child("Ocean Reflection Probe");
            go.transform.position = new Vector3(-10f, 4f, 40f);
            var probe = go.AddComponent<ReflectionProbe>();
            probe.mode = ReflectionProbeMode.Realtime;
            probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
            probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.AllFacesAtOnce;
            probe.size = new Vector3(600f, 150f, 600f);
            probe.resolution = 256;
            probe.hdr = true;
            probe.farClipPlane = 2500f;
            probe.clearFlags = ReflectionProbeClearFlags.Skybox;
            probe.importance = 1;
            go.AddComponent<ReflectionProbeRefresher>();
            // Render it on the next editor frame (not during the rebuild, when
            // the old, just-deleted probe may still be in the render lists).
            EditorApplication.delayCall += () => { if (probe != null) probe.RenderProbe(); };
        }

        // ==================================================================
        // COLOUR GRADING LUT (Phase 8)
        // ------------------------------------------------------------------
        // A 32x32x32 colour cube stored as a 1024x32 strip: 32 slices side by
        // side, one per blue level; inside a slice x = red, y = green. URP's
        // ColorLookup feeds every pixel's (sRGB) colour through it, so whatever
        // Grade() does to one colour happens to the whole picture, for the cost
        // of one texture lookup.
        // ==================================================================
        private static Texture2D StarlitGradeLut()
        {
            const int n = 32;
            var tex = new Texture2D(n * n, n, TextureFormat.RGBA32, false, true);
            for (int b = 0; b < n; b++)
            for (int g = 0; g < n; g++)
            for (int r = 0; r < n; r++)
            {
                var c = new Color(r / (n - 1f), g / (n - 1f), b / (n - 1f));
                tex.SetPixel(b * n + r, g, Grade(c));
            }
            tex.Apply(false);
            string full = Gen + "/Textures/T_StarlitGradeLUT.png";
            File.WriteAllBytes(full, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            AssetDatabase.ImportAsset(full, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(full);
            imp.sRGBTexture = false;                                   // values are used as-is
            imp.mipmapEnabled = false;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.filterMode = FilterMode.Bilinear;
            imp.textureCompression = TextureImporterCompression.Uncompressed;   // compression would band the colours
            imp.npotScale = TextureImporterNPOTScale.None;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(full);
        }

        // The "Moonlit Storybook" grade, on one sRGB colour.
        private static Color Grade(Color c)
        {
            float lum = c.r * 0.2126f + c.g * 0.7152f + c.b * 0.0722f;
            float shadows = 1f - Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(lum / 0.45f));
            float highlights = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((lum - 0.55f) / 0.45f));
            // shadows toward TEAL (the night grade is already cool and blue, so the
            // LUT adds green, not more blue), highlights toward warm moonlight/lantern gold
            c.r += shadows * -0.006f + highlights * 0.035f;
            c.g += shadows * 0.020f + highlights * 0.012f;
            c.b += shadows * 0.008f + highlights * -0.030f;
            // a little extra saturation in the mid-tones (the glow colours live there)
            float mid = 1f - Mathf.Abs(lum - 0.5f) * 2f;
            float sat = 1f + 0.08f * Mathf.Clamp01(mid);
            c.r = lum + (c.r - lum) * sat; c.g = lum + (c.g - lum) * sat; c.b = lum + (c.b - lum) * sat;
            // gentle S-curve for depth
            c.r = Mathf.Lerp(c.r, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(c.r)), 0.18f);
            c.g = Mathf.Lerp(c.g, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(c.g)), 0.18f);
            c.b = Mathf.Lerp(c.b, Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(c.b)), 0.18f);
            return new Color(Mathf.Clamp01(c.r), Mathf.Clamp01(c.g), Mathf.Clamp01(c.b), 1f);
        }

        // ==================================================================
        // POST-PROCESSING - the "dreamy neon night" look
        // ==================================================================
        private static void BuildPostProcessing()
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            SaveAsset(profile, "Settings/PP_StarlitNight.asset");

            T Add<T>() where T : VolumeComponent
            {
                T c = profile.Add<T>(true);
                c.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(c, profile);
                return c;
            }

            var tone = Add<Tonemapping>();
            tone.mode.Override(TonemappingMode.ACES);

            var bloom = Add<Bloom>();
            bloom.threshold.Override(0.95f);     // highlights bloom into the misty air
            bloom.intensity.Override(1.15f);
            bloom.scatter.Override(0.62f);
            bloom.tint.Override(new Color(0.85f, 0.95f, 1f));
            bloom.highQualityFiltering.Override(true);

            var adj = Add<ColorAdjustments>();
            adj.postExposure.Override(0.35f);
            adj.contrast.Override(22f);         // deeper blacks, punchier highlights
            adj.saturation.Override(14f);           // rich but restrained: the glow provides the colour
            adj.colorFilter.Override(new Color(0.95f, 0.96f, 1f));

            var wb = Add<WhiteBalance>();
            wb.temperature.Override(-9f);
            wb.tint.Override(6f);

            // Phase 8: a colour-grading LUT built in code (StarlitGradeLut) replaces the
            // old split toning. Shadows lean teal-indigo, highlights lean warm, the
            // mid-tones get a little extra saturation, plus a gentle S-curve.
            var lut = Add<ColorLookup>();
            lut.texture.Override(StarlitGradeLut());
            lut.contribution.Override(1f);

            var vig = Add<Vignette>();
            vig.color.Override(new Color(0.02f, 0.03f, 0.08f));
            vig.intensity.Override(0.3f);
            vig.smoothness.Override(0.45f);

            var grain = Add<FilmGrain>();
            grain.type.Override(FilmGrainLookup.Thin1);
            grain.intensity.Override(0f);           // no grain: it reads as blur/noise at 1080p+
            grain.response.Override(0.8f);

            var ca = Add<ChromaticAberration>();
            ca.intensity.Override(0f);              // gameplay spikes this on hits

            var dof = Add<DepthOfField>();
            dof.mode.Override(DepthOfFieldMode.Off); // menus switch this to Bokeh
            dof.focusDistance.Override(12f);
            dof.aperture.Override(4f);
            dof.focalLength.Override(60f);

            EditorUtility.SetDirty(profile);

            // Find the night volume itself (or the template's plain "Global Volume"),
            // or make one. Phase 8 fix: this used to take the FIRST global volume in
            // the scene. On a rebuild that could be the Hit Flash, Light Vision,
            // Photo Mode or menu volume (the search order isn't fixed). That volume
            // was renamed and given the night profile, then destroyed when its own
            // builder rebuilt, leaving the real night volume pointing at a deleted
            // profile: no night grade at all, at random.
            Volume vol = null;
            foreach (Volume v in Object.FindObjectsByType<Volume>())
                if (v.gameObject.name == "Global Volume (Starlit Night)") { vol = v; break; }
            if (vol == null)
                foreach (Volume v in Object.FindObjectsByType<Volume>())
                    if (v.gameObject.name == "Global Volume" && v.isGlobal) { vol = v; break; }
            if (vol == null) vol = Child("Global Volume").AddComponent<Volume>();
            vol.gameObject.name = "Global Volume (Starlit Night)";
            vol.isGlobal = true;
            vol.priority = 0;
            vol.sharedProfile = profile;
        }

        // ==================================================================
        // ORIENTATION - turn the land so the boat sails out to sea (WorldLayout)
        // ==================================================================
        private static void OrientLayout()
        {
            // These follow the gameplay lane / the sky, not the land, so they stay put.
            var keep = new HashSet<string> { "Controllers", "Terrain", "Ocean", "Ocean Reflection Probe", "Play Lane (Phase 3)", "Ambient VFX", "Sky Fill Light" };
            foreach (Transform child in _root)
                if (!keep.Contains(child.name) && child.GetComponent<Volume>() == null)
                    WorldLayout.Apply(child);

            Transform vfx = _root.Find("Ambient VFX");
            if (vfx != null)
                foreach (Transform child in vfx)
                    if (child.name.StartsWith("Fireflies")) WorldLayout.Apply(child);

            // The ocean keeps the lagoon calm: move that zone with the lagoon.
            Vector3 lagoon = WorldLayout.ToWorld(new Vector3(LagoonCenter.x, 0f, LagoonCenter.y));
            float radius = (LagoonRadii.x + LagoonRadii.y) * 0.5f + 4f;
            M("M_Ocean").SetVector("_CalmZone", new Vector4(lagoon.x, lagoon.z, radius, radius));
            EditorUtility.SetDirty(M("M_Ocean"));
        }

        // ==================================================================
        // CAMERA - framed so the village, pier, bridge, lighthouse, moon and
        // mountains are all visible (Phase 3 replaces this with Cinemachine)
        // ==================================================================
        private static void SetupCamera()
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                cam = new GameObject("Main Camera").AddComponent<Camera>();
                cam.gameObject.tag = "MainCamera";
            }
            cam.transform.position = new Vector3(3f, 7.5f, -16f);
            cam.transform.LookAt(new Vector3(-6f, 1.5f, 30f));
            cam.fieldOfView = 55f;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 3000f;
            cam.allowHDR = true;
            cam.clearFlags = CameraClearFlags.Skybox;
            var data = cam.GetUniversalAdditionalCameraData();
            data.renderPostProcessing = true;
            data.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
            data.antialiasingQuality = AntialiasingQuality.High;

            // marker for the gameplay lane (used in Phase 3)
            GameObject lane = Child("Play Lane (Phase 3)");
            lane.transform.position = Vector3.zero;
        }
    }

}
#endif
