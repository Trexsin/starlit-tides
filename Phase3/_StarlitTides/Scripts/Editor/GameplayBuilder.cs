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
            _glowCyan = GlowMaterial("M_GlowCyan", new Color(0.5f, 3.2f, 3.8f));
            _glowGold = GlowMaterial("M_GlowGold", new Color(3.5f, 2.2f, 0.8f));
            _glowWhite = GlowMaterial("M_GlowWhite", new Color(2.2f, 2.8f, 3.2f));

            // ---- prefabs ----
            GameObject orbBurst = SavePrefab(BuildOrbBurst(), VfxDir + "/VFX_OrbBurst.prefab");
            GameObject splash = SavePrefab(BuildSplash(), VfxDir + "/VFX_Splash.prefab");
            GameObject dashBurst = SavePrefab(BuildDashBurst(), VfxDir + "/VFX_DashBurst.prefab");
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
            managers.gameObject.AddComponent<StarlitInput>();
            var gm = managers.gameObject.AddComponent<GameManager>();
            gm.boat = boatCtrl;
            gm.lightMeter = meter;
            managers.gameObject.AddComponent<DifficultyManager>();
            managers.gameObject.AddComponent<ScoreManager>();
            var vfx = managers.gameObject.AddComponent<VfxLibrary>();
            vfx.orbBurst = orbBurst; vfx.splash = splash; vfx.dashBurst = dashBurst;
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

        private static Material Lit(string name, Color color, float smoothness, Color emission = default)
        {
            var m = new Material(Shader.Find("Universal Render Pipeline/Lit")) { name = name };
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            if (emission != default)
            {
                m.EnableKeyword("_EMISSION");
                m.SetColor("_EmissionColor", emission);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            m.enableInstancing = true;
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
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.alphaIsTransparency = true;
            imp.wrapMode = TextureWrapMode.Clamp;
            imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
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
            var mb = new MeshBuilder(1);
            Vector3 kBR = new Vector3(0.24f, 0f, 0.55f), kBL = new Vector3(-0.24f, 0f, 0.55f);     // keel corners (bow side)
            Vector3 kSR = new Vector3(0.24f, 0f, -0.55f), kSL = new Vector3(-0.24f, 0f, -0.55f);   // keel corners (stern side)
            Vector3 rBR = new Vector3(0.36f, 0.42f, 0.5f), rBL = new Vector3(-0.36f, 0.42f, 0.5f); // rim
            Vector3 rSR = new Vector3(0.36f, 0.42f, -0.5f), rSL = new Vector3(-0.36f, 0.42f, -0.5f);
            Vector3 bow = new Vector3(0f, 0.5f, 1.05f), stern = new Vector3(0f, 0.5f, -1.05f);

            void Tri(Vector3 a, Vector3 b, Vector3 c)
            {
                Vector3 n = Vector3.Cross(b - a, c - a);
                mb.Triangle(0, a, b, c, n);
                mb.Triangle(0, a, c, b, -n);   // paper is two-sided
            }
            void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d) { Tri(a, b, c); Tri(a, c, d); }

            Quad(kSR, rSR, rBR, kBR);           // right side
            Quad(kBL, rBL, rSL, kSL);           // left side
            Quad(kSL, kSR, kBR, kBL);           // bottom
            Tri(kBR, rBR, bow); Tri(kBL, bow, rBL); Tri(kBR, bow, kBL);         // bow
            Tri(kSR, stern, rSR); Tri(kSL, rSL, stern); Tri(kSR, kSL, stern);   // stern
            // the tall central "sail" fold: two sheets leaning together
            Tri(new Vector3(0.05f, 0.42f, -0.5f), new Vector3(0.05f, 0.42f, 0.5f), new Vector3(0f, 1.15f, 0f));
            Tri(new Vector3(-0.05f, 0.42f, -0.5f), new Vector3(-0.05f, 0.42f, 0.5f), new Vector3(0f, 1.15f, 0f));
            // small side flaps of the fold
            Tri(rSR, new Vector3(0.05f, 0.42f, -0.5f), new Vector3(0.1f, 0.62f, -0.35f));
            Tri(rBL, new Vector3(-0.05f, 0.42f, 0.5f), new Vector3(-0.1f, 0.62f, 0.35f));
            return mb.ToMesh("PaperBoat");
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
            Material paper = Lit("M_Paper", new Color(0.96f, 0.93f, 0.86f), 0.25f, new Color(3.2f, 2.3f, 1.2f));
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

            Light light = AddLight(root.transform, new Vector3(0f, 1.8f, -0.4f), new Color(1f, 0.86f, 0.62f), 3.5f, 14f);

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
        // OBSTACLES
        // ==================================================================
        private static GameObject BuildRock(int index)
        {
            var root = new GameObject("Rock_" + (index + 1));
            Transform visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);
            Mesh mesh = SaveAsset(ProcMesh.Rock(2000 + index * 7, 1.25f, 0.42f, new Vector3(1.25f, 1.05f + index * 0.12f, 1.15f)), "ObstacleRock_" + index + ".asset");
            Material mat = AssetDatabase.LoadAssetAtPath<Material>(Gen + "/M_ObstacleRock.mat") ?? Lit("M_ObstacleRock", new Color(0.26f, 0.27f, 0.32f), 0.25f);
            MeshChild("Mesh", visual, mesh, mat);

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
            mb.Transform = Matrix4x4.TRS(new Vector3(-2.25f, 0f, 0f), Quaternion.Euler(0f, 0f, -90f), Vector3.one);
            mb.Cylinder(0, Vector3.zero, 0.38f, 0.33f, 4.5f, 9, false, false, 6f);
            mb.Cylinder(1, Vector3.zero, 0.38f, 0.38f, 0.001f, 9, false, true);
            mb.Cylinder(1, Vector3.up * 4.5f, 0.33f, 0.33f, 0.001f, 9, true, false);
            mb.Transform = Matrix4x4.identity;
            mb.Transform = Matrix4x4.TRS(new Vector3(0.6f, 0.2f, 0f), Quaternion.Euler(0f, 0f, -35f), Vector3.one);
            mb.Cylinder(0, Vector3.zero, 0.12f, 0.06f, 0.9f, 6, true);
            mb.Transform = Matrix4x4.TRS(new Vector3(-1.1f, 0.1f, 0.15f), Quaternion.Euler(40f, 0f, 20f), Vector3.one);
            mb.Cylinder(0, Vector3.zero, 0.1f, 0.05f, 0.7f, 6, true);
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
            int rings = 7, segs = 20;
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

        private static GameObject BuildOrbBurst()
        {
            ParticleSystem sparks = NewSystem("VFX_OrbBurst", null, _glowCyan);
            var main = sparks.main;
            main.duration = 0.3f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.45f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(2.5f, 7f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.25f);
            main.gravityModifier = 0.25f;
            var em = sparks.emission; em.SetBursts(new[] { new ParticleSystem.Burst(0f, 36) });
            var sh = sparks.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.25f;
            var col = sparks.colorOverLifetime; col.enabled = true; col.color = Fade(Color.white, new Color(0.3f, 0.9f, 1f));
            var drag = sparks.limitVelocityOverLifetime; drag.enabled = true; drag.drag = 2.5f; drag.limit = 50f;
            var r = sparks.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.06f; r.lengthScale = 1.5f;

            ParticleSystem flash = NewSystem("Flash", sparks.transform, _glowWhite);
            var fm = flash.main;
            fm.duration = 0.2f; fm.startLifetime = 0.35f; fm.startSpeed = 0f; fm.startSize = 2.5f;
            var fem = flash.emission; fem.SetBursts(new[] { new ParticleSystem.Burst(0f, 1) });
            var fsz = flash.sizeOverLifetime; fsz.enabled = true; fsz.size = new ParticleSystem.MinMaxCurve(1f, AnimationCurve.EaseInOut(0f, 0.4f, 1f, 1.6f));
            var fcol = flash.colorOverLifetime; fcol.enabled = true; fcol.color = Fade(Color.white, Color.white);
            var fsh = flash.shape; fsh.enabled = false;

            sparks.gameObject.AddComponent<PooledParticle>();
            return sparks.gameObject;
        }

        private static GameObject BuildSplash()
        {
            ParticleSystem drops = NewSystem("VFX_Splash", null, _glowWhite);
            var main = drops.main;
            main.duration = 0.3f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.6f, 1.1f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(4f, 9f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.1f, 0.28f);
            main.gravityModifier = 1.6f;
            var em = drops.emission; em.SetBursts(new[] { new ParticleSystem.Burst(0f, 50) });
            var sh = drops.shape; sh.shapeType = ParticleSystemShapeType.Cone; sh.angle = 35f; sh.radius = 0.6f;
            sh.rotation = new Vector3(-90f, 0f, 0f);
            var col = drops.colorOverLifetime; col.enabled = true; col.color = Fade(Color.white, new Color(0.3f, 0.8f, 1f));
            var r = drops.GetComponent<ParticleSystemRenderer>(); r.renderMode = ParticleSystemRenderMode.Stretch; r.velocityScale = 0.05f; r.lengthScale = 1.2f;
            drops.gameObject.AddComponent<PooledParticle>();
            return drops.gameObject;
        }

        private static GameObject BuildDashBurst()
        {
            ParticleSystem ps = NewSystem("VFX_DashBurst", null, _glowGold);
            var main = ps.main;
            main.duration = 0.2f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(0.25f, 0.5f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(3f, 6f);
            main.startSize = new ParticleSystem.MinMaxCurve(0.08f, 0.18f);
            var em = ps.emission; em.SetBursts(new[] { new ParticleSystem.Burst(0f, 24) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Circle; sh.radius = 0.5f; sh.rotation = new Vector3(90f, 0f, 0f);
            var col = ps.colorOverLifetime; col.enabled = true; col.color = Fade(new Color(1f, 0.95f, 0.7f), new Color(1f, 0.6f, 0.2f));
            ps.gameObject.AddComponent<PooledParticle>();
            return ps.gameObject;
        }

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

            // look target: ahead-left of the boat, so the village and lighthouse stay in frame
            var look = new GameObject("Camera Look Target").transform;
            look.SetParent(boat, false);
            look.localPosition = new Vector3(-3.5f, 1f, 22f);

            var go = new GameObject("CM Gameplay Camera");
            go.transform.SetParent(root, false);
            go.transform.position = boat.position + new Vector3(2f, 5.2f, -11f);
            var cm = go.AddComponent<CinemachineCamera>();
            cm.Follow = boat;
            cm.LookAt = look;
            cm.Priority.Enabled = true;
            cm.Priority.Value = 10;
            var lens = LensSettings.Default;
            lens.FieldOfView = 54f; lens.NearClipPlane = 0.3f; lens.FarClipPlane = 3000f;
            cm.Lens = lens;

            var follow = go.AddComponent<CinemachineFollow>();
            follow.FollowOffset = new Vector3(2f, 5.2f, -11f);
            follow.TrackerSettings.BindingMode = BindingMode.WorldSpace;
            follow.TrackerSettings.PositionDamping = new Vector3(0.9f, 0.6f, 0.9f);

            var composer = go.AddComponent<CinemachineRotationComposer>();
            composer.Damping = new Vector2(1.2f, 0.9f);

            go.AddComponent<CinemachineImpulseListener>();
        }
    }
}
#endif
