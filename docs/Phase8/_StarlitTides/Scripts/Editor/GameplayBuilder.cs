// =============================================================================
//  STARLIT TIDES - One-click gameplay setup (Phase 3)
// -----------------------------------------------------------------------------
//  Menu:  Starlit Tides > 5. Build Gameplay
//
//  Creates (and saves as prefabs in Assets/_StarlitTides/Prefabs):
//    * the glowing PAPER BOAT (procedural folded-paper mesh, inner candle,
//      point light, wake particles, plankton glow, light meter)
//    * the PLANKTON ORB (crystal core + camera-facing halo)
//    * obstacles: 3 ROCKS, a drifting LOG, a pulsing JELLYFISH
//    * VFX: orb burst, splash, dash burst
//  and adds to the open scene ("== STARLIT GAMEPLAY =="):
//    managers (input, game, difficulty, score, VFX, hit feedback, debug HUD),
//    the two spawners, the boat, a Cinemachine follow camera with impulse
//    shake, and the hit-flash post-processing volume.
//
//  Run Phase 2's "4. Build Environment" first (the ocean is needed).
//  Running this again rebuilds everything inside "== STARLIT GAMEPLAY ==".
// =============================================================================
#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace StarlitTides.EditorTools
{
    public static class GameplayBuilder
    {
        private const string RootName = "== STARLIT GAMEPLAY ==";
        private const string Gen = "Assets/_StarlitTides/Generated/Gameplay";
        private const string PrefabDir = "Assets/_StarlitTides/Prefabs/Gameplay";
        private const string VfxDir = "Assets/_StarlitTides/Prefabs/VFX";

        private static Texture2D _softDot;
        private static Material _glowCyan, _glowGold, _glowWhite;
        private static Mesh _haloQuad;
        private static Material _reefRock, _reefLichen, _reefGlow, _obstacleRock, _rockLichen;

        [MenuItem("Starlit Tides/5. Build Gameplay", priority = 21)]
        public static void BuildMenu()
        {
            if (Object.FindAnyObjectByType<OceanController>() == null)
            {
                EditorUtility.DisplayDialog("Starlit Tides", "Build the environment first (Starlit Tides > 4. Build Environment).", "OK");
                return;
            }
            if (!Application.isBatchMode && !EditorUtility.DisplayDialog("Build Starlit Tides Gameplay",
                    "Creates the boat, orbs, obstacles, effects, managers and the gameplay camera.\n\n" +
                    "'" + RootName + "' will be rebuilt if it already exists.", "Build", "Cancel"))
                return;
            Build();
        }

        public static void Build()
        {
            EnsureFolder(Gen); EnsureFolder(PrefabDir); EnsureFolder(VfxDir);
            GameObject old = GameObject.Find(RootName);
            if (old != null) Object.DestroyImmediate(old);
            Transform root = new GameObject(RootName).transform;

            _softDot = SoftDot();
            _haloQuad = SaveAsset(Quad(), "HaloQuad.asset");
            _reefRock = _reefLichen = _reefGlow = _obstacleRock = _rockLichen = null;
            _glowCyan = GlowMaterial("M_GlowCyan", new Color(0.5f, 3.2f, 3.8f));
            _glowGold = GlowMaterial("M_GlowGold", new Color(3.5f, 2.2f, 0.8f));
            _glowWhite = GlowMaterial("M_GlowWhite", new Color(2.2f, 2.8f, 3.2f));

            // ---- prefabs ----
            GameObject orbBurst = SavePrefab(BuildOrbBurst(), VfxDir + "/VFX_OrbBurst.prefab");
            GameObject splash = SavePrefab(BuildSplash(), VfxDir + "/VFX_Splash.prefab");
            GameObject dashBurst = SavePrefab(BuildDashBurst(), VfxDir + "/VFX_DashBurst.prefab");
            GameObject hitBurst = SavePrefab(VfxKit.Hit(), VfxDir + "/VFX_Hit.prefab");   // obstacle hits: splash + coral danger sparks
            GameObject orb = SavePrefab(BuildOrb(), PrefabDir + "/Orb.prefab");
            var rocks = new List<GameObject>();
            for (int i = 0; i < 3; i++) rocks.Add(SavePrefab(BuildRock(i), PrefabDir + "/Rock_" + (i + 1) + ".prefab"));
            GameObject log = SavePrefab(BuildLog(), PrefabDir + "/Log.prefab");
            GameObject jelly = SavePrefab(BuildJellyfish(), PrefabDir + "/Jellyfish.prefab");
            GameObject boatPrefab = SavePrefab(BuildBoat(), PrefabDir + "/PaperBoat.prefab");

            // ---- scene objects ----
            GameObject boat = (GameObject)PrefabUtility.InstantiatePrefab(boatPrefab);
            boat.transform.SetParent(root, false);
            boat.transform.position = new Vector3(0f, 0f, 2f);
            var meter = boat.GetComponent<LightMeter>();
            var boatCtrl = boat.GetComponent<BoatController>();

            Transform managers = new GameObject("Managers").transform;
            managers.SetParent(root, false);
            managers.gameObject.AddComponent<PlayArea>();   // lane size: read by the boat, spawners, camera, markers, ocean
            managers.gameObject.AddComponent<StarlitInput>();
            var gm = managers.gameObject.AddComponent<GameManager>();
            gm.boat = boatCtrl;
            gm.lightMeter = meter;
            managers.gameObject.AddComponent<DifficultyManager>();
            managers.gameObject.AddComponent<ScoreManager>();
            var vfx = managers.gameObject.AddComponent<VfxLibrary>();
            vfx.orbBurst = orbBurst; vfx.splash = splash; vfx.dashBurst = dashBurst; vfx.hit = hitBurst;
            var hud = managers.gameObject.AddComponent<DebugHUD>();
            hud.lightMeter = meter; hud.boat = boatCtrl;

            // hit feedback: impulse source + flash volume
            var feedbackGo = new GameObject("Hit Feedback");
            feedbackGo.transform.SetParent(managers, false);
            var impulse = feedbackGo.AddComponent<CinemachineImpulseSource>();
            impulse.ImpulseDefinition.ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Bump;
            impulse.ImpulseDefinition.ImpulseType = CinemachineImpulseDefinition.ImpulseTypes.Uniform;
            impulse.ImpulseDefinition.ImpulseDuration = 0.3f;
            impulse.DefaultVelocity = new Vector3(0.35f, -0.6f, 0.1f);
            var feedback = feedbackGo.AddComponent<HitFeedback>();
            feedback.impulseSource = impulse;
            feedback.flashVolume = BuildFlashVolume(root);

            // spawners
            Transform spawners = new GameObject("Spawners").transform;
            spawners.SetParent(root, false);
            var orbSpawner = spawners.gameObject.AddComponent<OrbSpawner>();
            orbSpawner.orbPrefab = orb;
            var obstacleSpawner = spawners.gameObject.AddComponent<ObstacleSpawner>();
            obstacleSpawner.rockPrefabs = rocks.ToArray();
            obstacleSpawner.logPrefabs = new[] { log };
            obstacleSpawner.jellyfishPrefabs = new[] { jelly };

            // channel edge: glowing buoys and little reefs just outside the lane (Phase 8)
            var markers = spawners.gameObject.AddComponent<LaneMarkers>();
            markers.buoyPrefabs = new[] { SavePrefab(BuildBuoy(), PrefabDir + "/ChannelBuoy.prefab") };
            markers.reefPrefabs = new[]
            {
                SavePrefab(BuildReef(0), PrefabDir + "/ChannelReef_1.prefab"),
                SavePrefab(BuildReef(1), PrefabDir + "/ChannelReef_2.prefab"),
            };

            BuildCameraRig(root, boat.transform);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Selection.activeGameObject = boat;
            Debug.Log("[Starlit Tides] Gameplay built. Press Play!  WASD/Arrows steer, Space dashes, Esc pauses.");
        }

        // ==================================================================
        // HELPERS
        // ==================================================================
        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        private static T SaveAsset<T>(T asset, string name) where T : Object
        {
            string path = Gen + "/" + name;
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(asset, path);
            return asset;
        }

        private static GameObject SavePrefab(GameObject temp, string path)
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(temp, path);
            Object.DestroyImmediate(temp);
            return prefab;
        }

        // Phase 8: stylized shader for every solid material. Emissive ones glow,
        // the rest are props with an outline (StylizedMaterials).
        private static Material Lit(string name, Color color, float smoothness, Color emission = default)
        {
            StylizedMaterials.Kind kind = emission != default ? StylizedMaterials.Kind.Glow
                                        : name == "M_ObstacleRock" || name == "M_ReefRock" || name.StartsWith("M_Log") ? StylizedMaterials.Kind.Natural
                                        : StylizedMaterials.Kind.Prop;
            var m = StylizedMaterials.Create(name, color, smoothness, kind, emission);
            return SaveAsset(m, name + ".mat");
        }

        private static Material Custom(string name, string shader)
        {
            Shader s = Shader.Find(shader);
            if (s == null) throw new System.Exception("[Starlit Tides] Shader '" + shader + "' not found - copy the Phase 3 Shaders folder first.");
            var m = new Material(s) { name = name };
            return SaveAsset(m, name + ".mat");
        }

        private static Material GlowMaterial(string name, Color tint)
        {
            Material m = Custom(name, "StarlitTides/GlowParticle");
            m.SetTexture("_MainTex", _softDot);
            m.SetColor("_TintColor", tint);
            EditorAssetUtil.Persist(m);   // write now, so a later import can't drop the texture
            return m;
        }

        private static Texture2D SoftDot()
        {
            const string path = Gen + "/T_GlowDot.png";
            const int s = 64;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, true);
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float dx = (x + 0.5f) / s * 2f - 1f, dy = (y + 0.5f) / s * 2f - 1f;
                float d = Mathf.Sqrt(dx * dx + dy * dy);
                float a = Mathf.Clamp01(Mathf.Exp(-d * d * 6f) + Mathf.Exp(-d * d * 50f) * 0.7f) * Mathf.Clamp01(1f - d);
                tex.SetPixel(x, y, new Color(1f, 1f, 1f, a));
            }
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            // synchronous import, never null: a pending import here once saved the glow
            // materials with no texture, and every halo drew as a white square
            return EditorAssetUtil.ImportTexture(path, imp =>
            {
                imp.alphaIsTransparency = true;
                imp.wrapMode = TextureWrapMode.Clamp;
            });
        }

        private static GameObject MeshChild(string name, Transform parent, Mesh mesh, params Material[] mats)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            var mr = go.AddComponent<MeshRenderer>();
            mr.sharedMaterials = mats;
            return go;
        }

        private static Light AddLight(Transform parent, Vector3 localPos, Color color, float intensity, float range)
        {
            var go = new GameObject("Light");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            var l = go.AddComponent<Light>();
            l.type = LightType.Point;
            l.color = color; l.intensity = intensity; l.range = range;
            l.shadows = LightShadows.None;
            return l;
        }

        private static Mesh Quad()
        {
            var mb = new MeshBuilder(1);
            mb.Quad(0, new Vector3(-0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(0.5f, -0.5f, 0), Vector3.back);
            return mb.ToMesh("HaloQuad");
        }

        // ==================================================================
        // PAPER BOAT
        // ==================================================================
        private static Mesh PaperBoatMesh()
        {
            // A classic folded paper boat, ~2 m long, pointing toward +Z.
            // Every panel is subdivided and gently crumpled (edges stay fixed so the
            // panels still meet), and the facets stay flat-shaded like folded paper.
            var mb = new MeshBuilder(1);
            Vector3 kBR = new Vector3(0.24f, 0f, 0.55f), kBL = new Vector3(-0.24f, 0f, 0.55f);     // keel corners (bow side)
            Vector3 kSR = new Vector3(0.24f, 0f, -0.55f), kSL = new Vector3(-0.24f, 0f, -0.55f);   // keel corners (stern side)
            Vector3 rBR = new Vector3(0.38f, 0.42f, 0.52f), rBL = new Vector3(-0.38f, 0.42f, 0.52f); // rim
            Vector3 rSR = new Vector3(0.38f, 0.42f, -0.52f), rSL = new Vector3(-0.38f, 0.42f, -0.52f);
            Vector3 bow = new Vector3(0f, 0.52f, 1.08f), stern = new Vector3(0f, 0.52f, -1.08f);
            Vector3 sailTop = new Vector3(0f, 1.18f, 0f);

            float Crumple(Vector3 p) =>
                (Mathf.PerlinNoise(p.x * 7.3f + p.z * 2.1f + 11f, p.y * 7.9f + 3f) - 0.5f) * 0.035f;

            void Tri(Vector3 a, Vector3 b, Vector3 c, Vector2 ua, Vector2 ub, Vector2 uc)
            {
                Vector3 n = Vector3.Cross(b - a, c - a);
                mb.Triangle(0, a, b, c, ua, ub, uc, n);
                mb.Triangle(0, a, c, b, ua, uc, ub, -n);   // paper is two-sided
            }

            // A triangle split into n*n small facets, displaced along its normal.
            void Sheet(Vector3 a, Vector3 b, Vector3 c, int n)
            {
                Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
                Vector3 P(int i, int j)
                {
                    float u = i / (float)n, v = j / (float)n, w = 1f - u - v;
                    Vector3 p = a * w + b * u + c * v;
                    float edge = Mathf.Clamp01(Mathf.Min(u, Mathf.Min(v, w)) * 3f);   // 0 on the edges
                    return p + normal * Crumple(p) * edge;
                }
                Vector2 UV(Vector3 p) => new Vector2(p.x + p.z * 0.6f, p.y + p.z * 0.4f);
                for (int i = 0; i < n; i++)
                for (int j = 0; j < n - i; j++)
                {
                    Vector3 p0 = P(i, j), p1 = P(i + 1, j), p2 = P(i, j + 1);
                    Tri(p0, p1, p2, UV(p0), UV(p1), UV(p2));
                    if (i + j < n - 1)
                    {
                        Vector3 p3 = P(i + 1, j + 1);
                        Tri(p1, p3, p2, UV(p1), UV(p3), UV(p2));
                    }
                }
            }
            void Panel(Vector3 a, Vector3 b, Vector3 c, Vector3 d, int n) { Sheet(a, b, c, n); Sheet(a, c, d, n); }

            Panel(kSR, rSR, rBR, kBR, 6);          // right side
            Panel(kBL, rBL, rSL, kSL, 6);          // left side
            Panel(kSL, kSR, kBR, kBL, 4);          // bottom
            Sheet(kBR, rBR, bow, 4); Sheet(kBL, bow, rBL, 4); Sheet(kBR, bow, kBL, 3);       // bow
            Sheet(kSR, stern, rSR, 4); Sheet(kSL, rSL, stern, 4); Sheet(kSR, kSL, stern, 3); // stern
            // The tall central "sail": two sheets leaning together, each folded down the middle.
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 a = new Vector3(side * 0.05f, 0.42f, -0.52f), b = new Vector3(side * 0.05f, 0.42f, 0.52f);
                Vector3 mid = new Vector3(side * 0.09f, 0.78f, 0f);
                Sheet(a, mid, sailTop, 4); Sheet(mid, b, sailTop, 4); Sheet(a, b, mid, 4);
            }
            // Small side flaps of the fold, and the rim's folded-over lip.
            Sheet(rSR, new Vector3(0.05f, 0.42f, -0.52f), new Vector3(0.11f, 0.64f, -0.34f), 3);
            Sheet(rBL, new Vector3(-0.05f, 0.42f, 0.52f), new Vector3(-0.11f, 0.64f, 0.34f), 3);
            foreach (float side in new[] { -1f, 1f })
                Panel(new Vector3(side * 0.38f, 0.42f, -0.52f), new Vector3(side * 0.38f, 0.42f, 0.52f),
                      new Vector3(side * 0.31f, 0.36f, 0.5f), new Vector3(side * 0.31f, 0.36f, -0.5f), 3);
            return mb.ToMesh("PaperBoat");
        }

        // Off-white paper: soft mottling, fine fibres and faint fold creases.
        private static Texture2D PaperTexture()
        {
            const string path = Gen + "/T_Paper.png";
            const int s = 512;
            var tex = new Texture2D(s, s, TextureFormat.RGBA32, true);
            var px = new Color32[s * s];
            var rng = new System.Random(5);
            float[] fibre = new float[s * s];
            for (int f = 0; f < 900; f++)
            {
                float x = (float)rng.NextDouble() * s, y = (float)rng.NextDouble() * s;
                float ang = (float)rng.NextDouble() * Mathf.PI, len = 6f + (float)rng.NextDouble() * 18f;
                float dark = 0.03f + (float)rng.NextDouble() * 0.05f;
                for (int k = 0; k < len; k++)
                {
                    int xi = ((int)(x + Mathf.Cos(ang) * k) % s + s) % s, yi = ((int)(y + Mathf.Sin(ang) * k) % s + s) % s;
                    fibre[yi * s + xi] = Mathf.Max(fibre[yi * s + xi], dark);
                }
            }
            for (int y = 0; y < s; y++)
            for (int x = 0; x < s; x++)
            {
                float u = x / (float)s, v = y / (float)s;
                float mottle = Mathf.PerlinNoise(u * 6f + 3f, v * 6f + 7f) * 0.06f + Mathf.PerlinNoise(u * 40f, v * 40f) * 0.03f;
                float crease = Mathf.Exp(-Mathf.Pow((Mathf.Repeat(u + v * 0.5f, 0.5f) - 0.25f) / 0.004f, 2f)) * 0.08f
                             + Mathf.Exp(-Mathf.Pow((Mathf.Repeat(v * 1.3f, 0.33f) - 0.16f) / 0.003f, 2f)) * 0.05f;
                float k = 1f - mottle - fibre[y * s + x] - crease;
                px[y * s + x] = new Color(0.93f * k, 0.9f * k, 0.83f * k, 1f);
            }
            tex.SetPixels32(px);
            tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            Object.DestroyImmediate(tex);
            return EditorAssetUtil.ImportTexture(path, imp =>
            {
                imp.wrapMode = TextureWrapMode.Repeat;
                imp.anisoLevel = 8;
                imp.filterMode = FilterMode.Trilinear;
            });
        }

        private static GameObject BuildBoat()
        {
            var root = new GameObject("PaperBoat");
            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            var col = root.AddComponent<CapsuleCollider>();
            col.direction = 2; col.radius = 0.6f; col.height = 2.8f; col.center = new Vector3(0f, 0.45f, 0f);
            col.isTrigger = true;

            Transform visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            Material paper = Lit("M_Paper", Color.white, 0.18f, new Color(0.5f, 0.36f, 0.19f));
            paper.SetTexture("_BaseMap", PaperTexture());
            paper.SetTextureScale("_BaseMap", new Vector2(1.6f, 1.6f));
            EditorUtility.SetDirty(paper);
            GameObject hull = MeshChild("Paper Hull", visual, SaveAsset(PaperBoatMesh(), "PaperBoat.asset"), paper);
            hull.transform.localScale = Vector3.one * 1.45f;

            // inner candle glow + halo
            var mbCore = new MeshBuilder(1);
            ProcMesh.Icosphere(mbCore, 0, Vector3.zero, 0.13f, 1, 3, 0.1f);
            Material candle = Lit("M_BoatCandle", new Color(1f, 0.85f, 0.6f), 0.5f, new Color(6f, 3.8f, 1.6f));
            GameObject core = MeshChild("Candle", visual, SaveAsset(mbCore.ToMesh("BoatCandle"), "BoatCandle.asset"), candle);
            core.transform.localPosition = new Vector3(0f, 0.75f, 0.35f);
            GameObject halo = MeshChild("Halo", visual, _haloQuad, _glowGold);
            halo.transform.localPosition = new Vector3(0f, 0.9f, 0.2f);
            halo.transform.localScale = Vector3.one * 1.6f;
            halo.AddComponent<FaceCamera>();
            halo.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            // Raised and set back so it lights the sea around the boat without blowing out the paper.
            Light light = AddLight(root.transform, new Vector3(0f, 2.6f, -1.1f), new Color(1f, 0.82f, 0.56f), 5f, 20f);

            var disturber = root.AddComponent<WaterDisturber>();
            disturber.radius = 1.9f; disturber.strength = 0.6f;
            disturber.leaveTrail = true; disturber.trailSpacing = 0.7f; disturber.trailRadius = 1.2f; disturber.trailStrength = 0.55f; disturber.trailLife = 1.8f;

            var meter = root.AddComponent<LightMeter>();
            meter.boatLight = light;
            meter.glowRenderer = hull.GetComponent<MeshRenderer>();
            meter.waterGlow = disturber;

            var boat = root.AddComponent<BoatController>();
            boat.visual = visual;
            boat.lightMeter = meter;
            boat.blinkRenderers = new Renderer[] { hull.GetComponent<MeshRenderer>(), core.GetComponent<MeshRenderer>() };

            // wake particles at the stern
            var wakeGo = new GameObject("Wake");
            wakeGo.transform.SetParent(root.transform, false);
            wakeGo.transform.localPosition = new Vector3(0f, 0.05f, -1.4f);
            var ps = wakeGo.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.3f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.6f, 1f, 1f), new Color(1f, 0.9f, 0.6f));
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 400;
            var shape = ps.shape; shape.shapeType = ParticleSystemShapeType.Box; shape.scale = new Vector3(0.6f, 0.05f, 0.3f);
            var colOverLife = ps.colorOverLifetime; colOverLife.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(new Color(0.4f, 0.9f, 1f), 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            colOverLife.color = g;
            var sizeOverLife = ps.sizeOverLifetime; sizeOverLife.enabled = true;
            sizeOverLife.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.Linear(0f, 1f, 1f, 0.2f));
            var pr = wakeGo.GetComponent<ParticleSystemRenderer>();
            pr.sharedMaterial = _glowCyan;
            pr.shadowCastingMode = ShadowCastingMode.Off;
            VfxKit.StyleWake(ps);   // twinkling star sparks instead of round dots
            var wake = wakeGo.AddComponent<BoatWake>();
            wake.boat = boat;

            return root;
        }

        // ==================================================================
        // ORB
        // ==================================================================
        private static GameObject BuildOrb()
        {
            var root = new GameObject("Orb");
            var col = root.AddComponent<SphereCollider>();
            col.radius = 0.75f; col.isTrigger = true;

            Transform visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            var mb = new MeshBuilder(1);
            ProcMesh.Icosphere(mb, 0, Vector3.zero, 0.34f, 1, 11, 0.08f);
            Mesh mesh = SaveAsset(mb.ToMesh("OrbCrystal"), "OrbCrystal.asset");

            // Use the Shader Graph from the Phase 3 lesson if it exists, otherwise a Lit fallback.
            Shader sg = Shader.Find("Shader Graphs/SG_OrbGlow");
            Material orbMat = sg != null ? SaveAsset(new Material(sg) { name = "M_Orb" }, "M_Orb.mat")
                                         : Lit("M_Orb", new Color(0.2f, 0.7f, 0.8f), 0.9f, new Color(0.6f, 3.4f, 4.0f));
            GameObject core = MeshChild("Crystal", visual, mesh, orbMat);
            core.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            GameObject halo = MeshChild("Halo", visual, _haloQuad, _glowCyan);
            halo.transform.localScale = Vector3.one * 1.5f;
            halo.AddComponent<FaceCamera>();
            halo.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            var orb = root.AddComponent<Orb>();
            orb.visual = visual;
            orb.floatOffset = 0.55f; orb.bobAmplitude = 0.15f; orb.bobSpeed = 2.2f;

            var d = root.AddComponent<WaterDisturber>();
            d.radius = 1.1f; d.strength = 0.8f; d.pulse = 0.4f; d.pulseSpeed = 4f; d.maxHeightAboveWater = 1.5f;
            return root;
        }

        // ==================================================================
        // CHANNEL MARKERS (Phase 8): decoration along the lane edges
        // ==================================================================
        // A rounded channel buoy: a half-sunk float, a moon-white band, a thin
        // mast and a breathing cyan lamp with a soft halo. No collider.
        private static GameObject BuildBuoy()
        {
            var root = new GameObject("ChannelBuoy");
            Transform visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            visual.localScale = Vector3.one * 1.3f;   // a touch bigger so it reads from the camera

            var mb = new MeshBuilder(2);
            mb.SmoothGroup = 1;   // Phase 8: round, smooth float; crisp caps
            mb.Cylinder(0, new Vector3(0f, -0.55f, 0f), 0.42f, 0.62f, 0.45f, 20, false, true);     // underwater skirt
            mb.Cylinder(1, new Vector3(0f, -0.10f, 0f), 0.62f, 0.62f, 0.22f, 20, false, false);    // white band
            mb.Cylinder(0, new Vector3(0f, 0.12f, 0f), 0.62f, 0.30f, 0.42f, 20, true, false);      // shoulder
            mb.SmoothGroup = 2;
            mb.Cylinder(0, new Vector3(0f, 0.50f, 0f), 0.07f, 0.06f, 1.25f, 10, true, false);      // mast
            mb.SmoothGroup = 3;
            mb.Cylinder(1, new Vector3(0f, 1.70f, 0f), 0.20f, 0.20f, 0.06f, 14, true, true);       // lamp cap
            Mesh body = SaveAsset(mb.ToMesh("ChannelBuoy"), "ChannelBuoy.asset");
            Material bodyMat = Lit("M_BuoyBody", new Color(0.13f, 0.17f, 0.30f), 0.55f);
            Material bandMat = Lit("M_BuoyBand", new Color(0.72f, 0.78f, 0.90f), 0.4f);
            MeshChild("Body", visual, body, bodyMat, bandMat);

            Transform glow = new GameObject("Glow").transform;
            glow.SetParent(visual, false);
            glow.localPosition = new Vector3(0f, 1.52f, 0f);
            var lampMb = new MeshBuilder(1);
            ProcMesh.Icosphere(lampMb, 0, Vector3.zero, 0.16f, 2, 5, 0f);
            Mesh lampMesh = SaveAsset(lampMb.ToMesh("BuoyLamp"), "BuoyLamp.asset");
            Material lampMat = Lit("M_BuoyLamp", new Color(0.3f, 0.9f, 0.85f), 0.8f, new Color(0.5f, 3.2f, 2.8f));
            GameObject lamp = MeshChild("Lamp", glow, lampMesh, lampMat);
            lamp.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            GameObject halo = MeshChild("Halo", glow, _haloQuad, _glowCyan);
            halo.transform.localScale = Vector3.one * 2.4f;
            halo.AddComponent<FaceCamera>();
            halo.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            var marker = root.AddComponent<LaneMarker>();
            marker.glow = glow;
            marker.floatOffset = -0.05f; marker.bobAmplitude = 0.08f; marker.bobSpeed = 1.3f;
            marker.followWaves = 1f; marker.rockAngle = 7f;
            return root;
        }

        // A little reef: a few half-sunk rocks crusted with glowing cyan lichen.
        private static GameObject BuildReef(int variant)
        {
            var root = new GameObject("ChannelReef_" + (variant + 1));
            Transform visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);

            // shared by both reef variants, made fresh on each build
            if (_reefRock == null)
            {
                _reefRock = Lit("M_ReefRock", new Color(0.17f, 0.22f, 0.30f), 0.35f);
                StylizedMaterials.SetAccent(_reefRock, new Color(0.14f, 0.3f, 0.3f));
            }
            if (_reefLichen == null) _reefLichen = Lit("M_ReefLichen", new Color(0.25f, 0.8f, 0.75f), 0.6f, new Color(0.3f, 2.2f, 2.0f));
            if (_reefGlow == null) _reefGlow = GlowMaterial("M_GlowReef", new Color(0.2f, 1.1f, 1.25f));
            Material rockMat = _reefRock, lichenMat = _reefLichen;

            var rng = new System.Random(4100 + variant * 31);
            float R(float a, float b) => a + (float)rng.NextDouble() * (b - a);
            int rocks = variant == 0 ? 3 : 4;
            var lichen = new MeshBuilder(1);
            for (int i = 0; i < rocks; i++)
            {
                float radius = i == 0 ? R(0.9f, 1.2f) : R(0.45f, 0.8f);
                Mesh rock = SaveAsset(ProcMesh.Rock(4200 + variant * 10 + i, radius, 0.4f, new Vector3(1.2f, R(0.8f, 1.2f), 1.1f), 3), "ReefRock_" + variant + "_" + i + ".asset");
                float a = i * 2.4f + R(0f, 0.6f);
                Vector3 pos = i == 0 ? Vector3.zero : new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * R(1.0f, 1.6f);
                GameObject g = MeshChild("Rock_" + i, visual, rock, rockMat);
                g.transform.localPosition = pos + Vector3.down * radius * 0.35f;
                g.transform.localRotation = Quaternion.Euler(0f, R(0f, 360f), 0f);

                // glowing lichen dots on the top half of each rock
                int dots = 5 + rng.Next(4);
                for (int d = 0; d < dots; d++)
                {
                    float az = R(0f, Mathf.PI * 2f), el = R(0.25f, 1.2f);
                    Vector3 dir = new Vector3(Mathf.Cos(az) * Mathf.Cos(el), Mathf.Sin(el), Mathf.Sin(az) * Mathf.Cos(el));
                    Vector3 c = g.transform.localPosition + Vector3.Scale(dir, new Vector3(1.15f, 0.9f, 1.05f)) * radius * 0.92f;
                    ProcMesh.Icosphere(lichen, 0, c, R(0.05f, 0.11f), 1, 4300 + d, 0.1f);
                }
            }
            Mesh lichenMesh = SaveAsset(lichen.ToMesh("ReefLichen_" + variant), "ReefLichen_" + variant + ".asset");
            GameObject lg = MeshChild("Lichen", visual, lichenMesh, lichenMat);
            lg.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            Transform glow = new GameObject("Glow").transform;
            glow.SetParent(visual, false);
            glow.localPosition = new Vector3(0f, 0.3f, 0f);
            GameObject halo = MeshChild("Halo", glow, _haloQuad, _reefGlow);
            halo.transform.localScale = Vector3.one * 4.2f;
            halo.AddComponent<FaceCamera>();
            halo.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;

            var marker = root.AddComponent<LaneMarker>();
            marker.glow = glow;
            marker.pulseSpeed = 0.8f; marker.pulseAmount = 0.15f;
            marker.floatOffset = -0.15f; marker.bobAmplitude = 0.02f; marker.followWaves = 0.25f; marker.rockAngle = 0f;
            return root;
        }

        // ==================================================================
        // OBSTACLES
        // ==================================================================
        private static GameObject BuildRock(int index)
        {
            var root = new GameObject("Rock_" + (index + 1));
            Transform visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            Mesh mesh = SaveAsset(ProcMesh.Rock(2000 + index * 7, 1.25f, 0.42f, new Vector3(1.25f, 1.05f + index * 0.12f, 1.15f), 4), "ObstacleRock_" + index + ".asset");
            if (_obstacleRock == null)
            {
                _obstacleRock = Lit("M_ObstacleRock", new Color(0.26f, 0.27f, 0.32f), 0.25f);   // shared by the 3 rocks
                StylizedMaterials.SetAccent(_obstacleRock, new Color(0.16f, 0.25f, 0.26f));      // moss on the tops (ProcMesh.Rock mask)
                _rockLichen = Lit("M_RockLichen", new Color(0.9f, 0.45f, 0.4f), 0.5f, new Color(1.5f, 0.42f, 0.36f));
            }
            Material mat = _obstacleRock;
            MeshChild("Mesh", visual, mesh, mat);

            // Phase 8: a few warm coral lichen dots on the upper half. In the art
            // direction warm coral/pink means "danger", cyan means "collect".
            {
                var lichen = new MeshBuilder(1);
                Vector3[] rv = mesh.vertices, rn = mesh.normals;
                var rng = new System.Random(3100 + index);
                for (int tries = 0, placed = 0; tries < 400 && placed < 9; tries++)
                {
                    int k = rng.Next(rv.Length);
                    if (rn[k].y < 0.35f || rv[k].y < 0.05f) continue;
                    lichen.SmoothGroup = 1 + placed;
                    ProcMesh.Icosphere(lichen, 0, rv[k] + rn[k] * 0.02f, 0.05f + (float)rng.NextDouble() * 0.06f, 1, 900 + placed, 0.1f);
                    placed++;
                }
                Mesh lichenMesh = SaveAsset(lichen.ToMesh("RockLichen_" + index), "RockLichen_" + index + ".asset");
                GameObject lg = MeshChild("Lichen", visual, lichenMesh, _rockLichen);
                lg.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            }

            var col = root.AddComponent<SphereCollider>();
            col.radius = 1.15f; col.center = new Vector3(0f, 0.3f, 0f); col.isTrigger = true;

            var ob = root.AddComponent<Obstacle>();
            ob.type = ObstacleType.Rock; ob.radius = 1.3f; ob.lightDamage = 0.25f; ob.visual = visual;
            ob.floatOffset = -0.45f; ob.bobAmplitude = 0.04f; ob.followWaves = 0.35f; ob.spinSpeed = 0f;

            var d = root.AddComponent<WaterDisturber>();
            d.radius = 2.3f; d.strength = 0.85f;   // the plankton outlines the rock: readable at night
            return root;
        }

        private static GameObject BuildLog()
        {
            var root = new GameObject("Log");
            Transform visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);

            var mb = new MeshBuilder(2);    // 0 bark, 1 cut wood
            // Phase 8: a lofted trunk (20 sides x 10 rings) with bark grooves, a gentle
            // bulge and smooth shading; crisp cut ends; smooth branch stubs.
            mb.Transform = Matrix4x4.TRS(new Vector3(-2.25f, 0f, 0f), Quaternion.Euler(0f, 0f, -90f), Vector3.one);
            const int sides = 20, rings = 10;
            const float length = 4.5f;
            float LogRadius(float t, float a) => Mathf.Lerp(0.38f, 0.33f, t) * (1f + 0.06f * Mathf.Sin(t * Mathf.PI))
                                                 * (1f + 0.035f * Mathf.Sin(a * 7f + t * 2f) + 0.02f * Mathf.Sin(a * 13f));
            Vector3 LogPoint(float t, float a) => new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * LogRadius(t, a) + Vector3.up * (t * length);
            mb.SmoothGroup = 1;
            for (int r = 0; r < rings; r++)
            for (int k = 0; k < sides; k++)
            {
                float t0 = r / (float)rings, t1 = (r + 1) / (float)rings;
                float a0 = k / (float)sides * Mathf.PI * 2f, a1 = (k + 1) / (float)sides * Mathf.PI * 2f;
                Vector3 outward = new Vector3(Mathf.Cos((a0 + a1) * 0.5f), 0f, Mathf.Sin((a0 + a1) * 0.5f));
                mb.Quad(0, LogPoint(t0, a0), LogPoint(t1, a0), LogPoint(t1, a1), LogPoint(t0, a1), outward);
            }
            mb.SmoothGroup = 0;
            for (int k = 0; k < sides; k++)   // cut ends (end grain)
            {
                float a0 = k / (float)sides * Mathf.PI * 2f, a1 = (k + 1) / (float)sides * Mathf.PI * 2f;
                mb.Triangle(1, Vector3.zero, LogPoint(0f, a0), LogPoint(0f, a1), Vector3.down);
                mb.Triangle(1, Vector3.up * length, LogPoint(1f, a0), LogPoint(1f, a1), Vector3.up);
            }
            mb.SmoothGroup = 2;
            mb.Transform = Matrix4x4.TRS(new Vector3(0.6f, 0.2f, 0f), Quaternion.Euler(0f, 0f, -35f), Vector3.one);
            mb.Cylinder(0, Vector3.zero, 0.12f, 0.06f, 0.9f, 10, true);
            mb.SmoothGroup = 3;
            mb.Transform = Matrix4x4.TRS(new Vector3(-1.1f, 0.1f, 0.15f), Quaternion.Euler(40f, 0f, 20f), Vector3.one);
            mb.Cylinder(0, Vector3.zero, 0.1f, 0.05f, 0.7f, 10, true);
            mb.SmoothGroup = 0;
            Mesh mesh = SaveAsset(mb.ToMesh("DriftLog"), "DriftLog.asset");
            Material bark = Lit("M_LogBark", new Color(0.23f, 0.16f, 0.11f), 0.15f);
            Material wood = Lit("M_LogCut", new Color(0.55f, 0.42f, 0.28f), 0.1f);
            MeshChild("Mesh", visual, mesh, bark, wood);

            var col = root.AddComponent<CapsuleCollider>();
            col.direction = 0; col.radius = 0.45f; col.height = 4.6f; col.isTrigger = true;

            var ob = root.AddComponent<Obstacle>();
            ob.type = ObstacleType.Log; ob.radius = 2.0f; ob.lightDamage = 0.2f; ob.visual = visual;
            ob.floatOffset = -0.12f; ob.bobAmplitude = 0.06f; ob.followWaves = 1f;
            ob.rollSpeed = 35f; ob.sidewaysDrift = new Vector2(0.6f, 1.8f);

            var d = root.AddComponent<WaterDisturber>();
            d.radius = 2.6f; d.strength = 0.7f;
            return root;
        }

        private static Mesh JellyMesh()
        {
            // Smooth bell (dome with a wavy rim) + ribbon tentacles hanging below y = 0.
            var verts = new List<Vector3>(); var norms = new List<Vector3>(); var tris = new List<int>();
            int rings = 14, segs = 32;   // Phase 8: smoother bell silhouette (was 7 x 20)
            float r = 0.6f, h = 0.8f;
            for (int i = 0; i <= rings; i++)
            {
                float v = i / (float)rings;                        // 0 top -> 1 rim
                float ang = v * Mathf.PI * 0.5f;
                for (int j = 0; j <= segs; j++)
                {
                    float a = j / (float)segs * Mathf.PI * 2f;
                    float frill = i == rings ? 1f + 0.08f * Mathf.Sin(a * 10f) : 1f;
                    float rr = Mathf.Sin(ang) * r * frill;
                    float y = Mathf.Cos(ang) * h;
                    Vector3 p = new Vector3(Mathf.Cos(a) * rr, Mathf.Max(y, 0.001f), Mathf.Sin(a) * rr);
                    verts.Add(p);
                    norms.Add(new Vector3(p.x / (r * r), p.y / (h * h), p.z / (r * r)).normalized);
                }
            }
            for (int i = 0; i < rings; i++)
            for (int j = 0; j < segs; j++)
            {
                int a = i * (segs + 1) + j, b = a + segs + 1;
                tris.Add(a); tris.Add(a + 1); tris.Add(b);
                tris.Add(a + 1); tris.Add(b + 1); tris.Add(b);
            }
            // tentacles: thin vertical ribbons around the rim + 4 frilly oral arms in the middle
            void Ribbon(float x, float z, float width, float length, int parts, float twist)
            {
                int start = verts.Count;
                Vector3 side = new Vector3(-z, 0f, x).normalized * width * 0.5f;
                if (side.sqrMagnitude < 1e-6f) side = new Vector3(width * 0.5f, 0, 0);
                for (int k = 0; k <= parts; k++)
                {
                    float t = k / (float)parts;
                    float y = -0.02f - t * length;
                    Vector3 c = new Vector3(x, y, z) * 1f;
                    Vector3 s = Quaternion.Euler(0f, twist * t, 0f) * side * (1f - t * 0.7f);
                    Vector3 n = new Vector3(x, 0f, z).normalized; if (n == Vector3.zero) n = Vector3.forward;
                    verts.Add(c - s); norms.Add(n);
                    verts.Add(c + s); norms.Add(n);
                }
                for (int k = 0; k < parts; k++)
                {
                    int a = start + k * 2;
                    tris.Add(a); tris.Add(a + 2); tris.Add(a + 1);
                    tris.Add(a + 1); tris.Add(a + 2); tris.Add(a + 3);
                }
            }
            for (int i = 0; i < 10; i++)
            {
                float a = i / 10f * Mathf.PI * 2f;
                Ribbon(Mathf.Cos(a) * r * 0.85f, Mathf.Sin(a) * r * 0.85f, 0.05f, 1.4f + (i % 3) * 0.3f, 8, 90f);
            }
            for (int i = 0; i < 4; i++)
            {
                float a = i / 4f * Mathf.PI * 2f + 0.4f;
                Ribbon(Mathf.Cos(a) * 0.12f, Mathf.Sin(a) * 0.12f, 0.16f, 1.0f, 8, 200f);
            }
            var mesh = new Mesh { name = "Jellyfish" };
            mesh.SetVertices(verts); mesh.SetNormals(norms); mesh.SetTriangles(tris, 0);
            mesh.RecalculateBounds();
            mesh.bounds = new Bounds(new Vector3(0f, -0.5f, 0f), new Vector3(2.5f, 3.5f, 2.5f));  // room for sway
            return mesh;
        }

        private static GameObject BuildJellyfish()
        {
            var root = new GameObject("Jellyfish");
            Transform visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            visual.localScale = Vector3.one * 1.3f;
            Material mat = Custom("M_Jellyfish", "StarlitTides/Jellyfish");
            GameObject body = MeshChild("Body", visual, SaveAsset(JellyMesh(), "Jellyfish.asset"), mat);
            body.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
            GameObject halo = MeshChild("Halo", visual, _haloQuad,
                GlowMaterial("M_GlowPink", new Color(2.2f, 0.5f, 2.4f)));
            halo.transform.localPosition = new Vector3(0f, 0.35f, 0f);
            halo.transform.localScale = Vector3.one * 2.4f;
            halo.AddComponent<FaceCamera>();

            var col = root.AddComponent<SphereCollider>();
            col.radius = 0.85f; col.center = new Vector3(0f, -0.1f, 0f); col.isTrigger = true;

            Light l = AddLight(root.transform, new Vector3(0f, 0.3f, 0f), new Color(1f, 0.45f, 0.95f), 2.2f, 6f);

            var d = root.AddComponent<WaterDisturber>();
            d.radius = 2.0f; d.strength = 0.9f; d.maxHeightAboveWater = 2.5f;

            var j = root.AddComponent<Jellyfish>();
            j.type = ObstacleType.Jellyfish; j.radius = 1.0f; j.lightDamage = 0.3f; j.visual = visual;
            j.floatOffset = 1.0f; j.bobAmplitude = 0.25f; j.bobSpeed = 2.4f; j.followWaves = 0.6f;
            j.glowLight = l; j.waterGlow = d; j.pulseSpeed = 2.4f;
            return root;
        }

        // ==================================================================
        // VFX
        // ==================================================================
        private static ParticleSystem NewSystem(string name, Transform parent, Material mat)
        {
            var go = new GameObject(name);
            if (parent != null) go.transform.SetParent(parent, false);
            var ps = go.AddComponent<ParticleSystem>();
            var main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var em = ps.emission; em.rateOverTime = 0f;
            var r = go.GetComponent<ParticleSystemRenderer>();
            r.sharedMaterial = mat;
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return ps;
        }

        private static Gradient Fade(Color a, Color b)
        {
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(a, 0f), new GradientColorKey(b, 1f) },
                      new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.8f, 0.4f), new GradientAlphaKey(0f, 1f) });
            return g;
        }

        // VFX polish pass: the effects are built by VfxKit (layered, shared textures + shader).
        private static GameObject BuildOrbBurst() { return VfxKit.OrbBurst(); }

        private static GameObject BuildSplash() { return VfxKit.Splash(); }

        private static GameObject BuildDashBurst() { return VfxKit.DashBurst(); }

        // ==================================================================
        // HIT FLASH VOLUME + CAMERA
        // ==================================================================
        private static Volume BuildFlashVolume(Transform root)
        {
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            SaveAsset(profile, "PP_HitFlash.asset");
            T Add<T>() where T : VolumeComponent
            {
                T c = profile.Add<T>(true);
                c.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(c, profile);
                return c;
            }
            Add<ChromaticAberration>().intensity.Override(1f);
            var vig = Add<Vignette>(); vig.intensity.Override(0.5f); vig.color.Override(new Color(0.12f, 0f, 0.18f)); vig.smoothness.Override(0.6f);
            var adj = Add<ColorAdjustments>(); adj.postExposure.Override(0.7f); adj.saturation.Override(-35f);
            Add<LensDistortion>().intensity.Override(-0.25f);
            EditorUtility.SetDirty(profile);

            var go = new GameObject("Hit Flash Volume");
            go.transform.SetParent(root, false);
            var vol = go.AddComponent<Volume>();
            vol.isGlobal = true; vol.priority = 10f; vol.weight = 0f; vol.sharedProfile = profile;
            return vol;
        }

        private static void BuildCameraRig(Transform root, Transform boat)
        {
            Camera cam = Camera.main;
            if (cam == null)
            {
                cam = new GameObject("Main Camera").AddComponent<Camera>();
                cam.tag = "MainCamera";
            }
            if (cam.GetComponent<CinemachineBrain>() == null) cam.gameObject.AddComponent<CinemachineBrain>();

            // Phase 8 framing. The camera follows an anchor that copies only about
            // half of the boat's sideways movement (CameraAnchor), so the boat moves
            // across the screen and the channel edges stay in view. It sits higher
            // and uses a narrower lens than before (48 deg instead of 58 deg), so the
            // wide lane fills the picture instead of a thin strip in the middle.
            var anchorGo = new GameObject("Camera Anchor");
            anchorGo.transform.SetParent(root, false);
            anchorGo.transform.position = new Vector3(boat.position.x * 0.55f, 0f, boat.position.z);
            var anchor = anchorGo.AddComponent<CameraAnchor>();
            anchor.boat = boat;
            anchor.followX = 0.8f;   // the camera is close now: follow more, or the boat leaves the frame at the lane edge

            var look = new GameObject("Camera Look Target").transform;
            look.SetParent(anchorGo.transform, false);
            look.localPosition = new Vector3(0f, 0.8f, 16f);

            // Low and close (player feedback): 6.5 m up, 12.5 m behind, ~11 deg pitch.
            // Creatures and obstacles loom; the boat sits ~80% down the screen and the
            // moon stays in the sky (~14% from the top).
            Vector3 offset = new Vector3(0f, 6.5f, -12.5f);
            var go = new GameObject("CM Gameplay Camera");
            go.transform.SetParent(root, false);
            go.transform.position = anchorGo.transform.position + offset;
            var cm = go.AddComponent<CinemachineCamera>();
            cm.Follow = anchorGo.transform;
            cm.LookAt = look;
            cm.Priority.Enabled = true;
            cm.Priority.Value = 10;
            var lens = LensSettings.Default;
            lens.FieldOfView = 52f; lens.NearClipPlane = 0.3f; lens.FarClipPlane = 3000f;
            cm.Lens = lens;
            var fit = go.AddComponent<CameraAspectFit>();   // same horizontal view on 16:9, 16:10 and 21:9
            fit.referenceVerticalFov = 52f;

            var follow = go.AddComponent<CinemachineFollow>();
            follow.FollowOffset = offset;
            follow.TrackerSettings.BindingMode = BindingMode.WorldSpace;
            follow.TrackerSettings.PositionDamping = new Vector3(0.9f, 0.6f, 0.9f);

            var composer = go.AddComponent<CinemachineRotationComposer>();
            composer.Damping = new Vector2(1.2f, 0.9f);

            go.AddComponent<CinemachineImpulseListener>();
        }
    }
}
#endif
