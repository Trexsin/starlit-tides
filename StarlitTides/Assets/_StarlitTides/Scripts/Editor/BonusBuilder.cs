// BonusBuilder: adds Bonus Features 7-11 to the open scene with one click.
// Menu: Starlit Tides > 7. Build Bonus Features
//
// Run steps 4, 5 and 6 first. It creates:
//   7  the glowing whale (procedural mesh + shader), its waves and splash
//   8  window lights that switch on/off + hidden orbs the lighthouse reveals
//   9  Photo Mode (free camera + depth of field/filter volume)
//   10 AudioManager (all sounds generated in code)
//   11 HighScoreManager + LeaderboardClient (Game Over stats and online top 10)
// Running it again rebuilds everything inside "== STARLIT BONUS ==".
// Extra menu item for demos: Starlit Tides > Test: Trigger Whale Now (in Play mode).

#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace StarlitTides.EditorTools
{
    public static class BonusBuilder
    {
        const string RootName = "== STARLIT BONUS ==";
        const string GeneratedFolder = "Assets/_StarlitTides/Generated/Bonus";
        const string PrefabFolder = "Assets/_StarlitTides/Prefabs/Gameplay";
        const string VfxFolder = "Assets/_StarlitTides/Prefabs/VFX";

        [MenuItem("Starlit Tides/7. Build Bonus Features", priority = 23)]
        public static void BuildMenu()
        {
            if (Object.FindAnyObjectByType<WeatherManager>() == null)
            {
                EditorUtility.DisplayDialog("Starlit Tides", "Build the features first (Starlit Tides > 6. Build Features).", "OK");
                return;
            }
            bool ok = Application.isBatchMode || EditorUtility.DisplayDialog("Build Starlit Tides Bonus Features",
                "Adds Bonus Features 7-11 (whale, living village, photo mode, audio, high scores).\n\n" +
                "'" + RootName + "' will be rebuilt if it already exists.", "Build", "Cancel");
            if (ok) Build();
        }

        [MenuItem("Starlit Tides/Test: Trigger Whale Now", priority = 40)]
        public static void TriggerWhale()
        {
            if (!Application.isPlaying || WhaleEvent.Instance == null)
            {
                EditorUtility.DisplayDialog("Starlit Tides", "Press Play first, then use this while the game is running.", "OK");
                return;
            }
            WhaleEvent.Instance.TriggerNow();
        }

        public static void Build()
        {
            MakeFolder(GeneratedFolder);
            GameObject old = GameObject.Find(RootName);
            if (old != null) Object.DestroyImmediate(old);
            GameObject root = new GameObject(RootName);

            // ---- 7: whale ----
            GameObject bigSplash = SavePrefab(MakeBigSplash(), VfxFolder + "/VFX_WhaleSplash.prefab");
            GameObject wave = SavePrefab(MakeWaveSegment(), PrefabFolder + "/WhaleWave.prefab");
            GameObject whaleEventObject = Child("Whale Event (Bonus 7)", root.transform);
            WhaleEvent whaleEvent = whaleEventObject.AddComponent<WhaleEvent>();
            whaleEvent.whale = MakeWhale(whaleEventObject.transform).transform;
            whaleEvent.waveSegmentPrefab = wave;
            whaleEvent.bigSplashVfx = bigSplash;
            CinemachineImpulseSource rumble = whaleEventObject.AddComponent<CinemachineImpulseSource>();
            rumble.ImpulseDefinition.ImpulseShape = CinemachineImpulseDefinition.ImpulseShapes.Rumble;
            rumble.ImpulseDefinition.ImpulseType = CinemachineImpulseDefinition.ImpulseTypes.Uniform;
            rumble.ImpulseDefinition.ImpulseDuration = 0.6f;
            rumble.DefaultVelocity = new Vector3(0.3f, -0.5f, 0f);
            whaleEvent.rumble = rumble;

            // ---- 8: living village + hidden orbs ----
            MakeLivingVillage(root.transform);
            HiddenOrbs hidden = Child("Hidden Orbs (Bonus 8)", root.transform).AddComponent<HiddenOrbs>();
            OrbSpawner orbSpawner = Object.FindAnyObjectByType<OrbSpawner>();
            hidden.orbPrefab = orbSpawner != null ? orbSpawner.orbPrefab : null;
            hidden.revealVfx = AssetDatabase.LoadAssetAtPath<GameObject>(VfxFolder + "/VFX_NearMiss.prefab");

            // ---- 9: photo mode ----
            MakePhotoMode(root.transform);

            // ---- 10: audio ----
            Child("Audio (Bonus 10)", root.transform).AddComponent<AudioManager>();

            // ---- 11: high scores + online leaderboard ----
            GameObject scores = Child("High Scores (Bonus 11)", root.transform);
            scores.AddComponent<HighScoreManager>();
            if (Object.FindAnyObjectByType<LeaderboardClient>() == null) scores.AddComponent<LeaderboardClient>();

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[Starlit Tides] Bonus features built. In Play mode try P for photo mode, or Starlit Tides > Test: Trigger Whale Now.");
        }

        // ==============================================================
        // Helpers
        // ==============================================================
        static void MakeFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path).Replace('\\', '/');
            MakeFolder(parent);
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

        static GameObject Child(string name, Transform parent)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        static Material ShaderMaterial(string name, string shader)
        {
            Shader s = Shader.Find(shader);
            if (s == null) throw new System.Exception("[Starlit Tides] Shader '" + shader + "' not found - copy the Phase 5 Shaders folder first.");
            Material m = new Material(s);
            m.name = name;
            return SaveAsset(m, GeneratedFolder + "/" + name + ".mat");
        }

        static T AddOverride<T>(VolumeProfile profile) where T : VolumeComponent
        {
            T component = profile.Add<T>(true);
            component.name = typeof(T).Name;
            AssetDatabase.AddObjectToAsset(component, profile);
            return component;
        }

        // ==============================================================
        // Bonus 7: whale mesh (a "loft": rings of different sizes along the body)
        // ==============================================================
        static Mesh WhaleMesh()
        {
            const int rings = 30, sides = 20;
            const float length = 20f;
            List<Vector3> verts = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> tris = new List<int>();

            for (int r = 0; r <= rings; r++)
            {
                float t = r / (float)rings;                  // 0 = tail, 1 = head
                float z = -length * 0.5f + t * length;
                float bulge = Mathf.Sin(Mathf.PI * Mathf.Pow(t, 0.75f));    // fat in the middle, round head
                // Thin tail stock that grows into the body over the first 45% (a smoothstep curve).
                float grow = Mathf.Clamp01(t / 0.45f);
                grow = grow * grow * (3f - 2f * grow);
                float tailTaper = Mathf.Lerp(0.12f, 1f, grow);
                float radius = Mathf.Max(2.8f * bulge * tailTaper, 0.02f);
                for (int s = 0; s <= sides; s++)
                {
                    float a = s / (float)sides * Mathf.PI * 2f;
                    float x = Mathf.Cos(a) * radius * 1.1f;
                    float y = Mathf.Sin(a) * radius * 0.85f;
                    if (y < 0f) y *= 0.8f;                    // flatter belly
                    verts.Add(new Vector3(x, y, z));
                    uvs.Add(new Vector2(s / (float)sides, t));
                }
            }
            for (int r = 0; r < rings; r++)
            {
                for (int s = 0; s < sides; s++)
                {
                    int a = r * (sides + 1) + s;
                    int b = a + sides + 1;
                    tris.Add(a); tris.Add(a + 1); tris.Add(b);
                    tris.Add(a + 1); tris.Add(b + 1); tris.Add(b);
                }
            }

            Mesh body = new Mesh();
            body.SetVertices(verts);
            body.SetUVs(0, uvs);
            body.SetTriangles(tris, 0);
            body.RecalculateNormals();

            // Tail flukes and side fins: flat, two-sided triangles.
            MeshBuilder fins = new MeshBuilder(1);
            float tailZ = -length * 0.5f;
            Vector2 tailUV = new Vector2(0.5f, 0f), finUV = new Vector2(0.5f, 0.62f);
            foreach (float side in new[] { -1f, 1f })
            {
                Vector3 root = new Vector3(0f, 0f, tailZ + 0.6f);
                Vector3 tip = new Vector3(side * 4.5f, 0.2f, tailZ - 1.6f);
                Vector3 notch = new Vector3(side * 0.8f, 0f, tailZ - 0.6f);
                fins.Triangle(0, root, tip, notch, tailUV, tailUV, tailUV, Vector3.up);
                fins.Triangle(0, root, notch, tip, tailUV, tailUV, tailUV, Vector3.down);

                float finZ = tailZ + 0.62f * length;
                Vector3 a = new Vector3(side * 2.2f, -0.6f, finZ + 0.8f);
                Vector3 b = new Vector3(side * 2.1f, -0.6f, finZ - 0.8f);
                Vector3 c = new Vector3(side * 5.2f, -2f, finZ - 2.2f);
                fins.Triangle(0, a, b, c, finUV, finUV, finUV, Vector3.up);
                fins.Triangle(0, a, c, b, finUV, finUV, finUV, Vector3.down);
            }
            Mesh finMesh = fins.ToMesh("WhaleFins");

            Mesh whale = new Mesh();
            whale.name = "GlowingWhale";
            whale.CombineMeshes(new[]
            {
                new CombineInstance { mesh = body, transform = Matrix4x4.identity },
                new CombineInstance { mesh = finMesh, transform = Matrix4x4.identity }
            }, true, true);
            whale.RecalculateBounds();
            return SaveAsset(whale, GeneratedFolder + "/GlowingWhale.asset");
        }

        static GameObject MakeWhale(Transform parent)
        {
            GameObject whale = Child("Glowing Whale", parent);
            whale.transform.localScale = Vector3.one * 1.5f;   // a 30 m giant
            whale.AddComponent<MeshFilter>().sharedMesh = WhaleMesh();
            MeshRenderer renderer = whale.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = ShaderMaterial("M_Whale", "StarlitTides/Whale");
            renderer.shadowCastingMode = ShadowCastingMode.Off;

            WaterDisturber glow = whale.AddComponent<WaterDisturber>();
            glow.radius = 7f;
            glow.strength = 1.6f;
            glow.maxHeightAboveWater = 8f;

            Light light = Child("Whale Glow Light", whale.transform).AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(0.4f, 0.9f, 1f);
            light.intensity = 4f;
            light.range = 22f;
            light.shadows = LightShadows.None;

            whale.SetActive(false);
            return whale;
        }

        // A curved, glowing wave crest (half a tube lying across the lane).
        static Mesh CrestMesh(float width, float height, float depth)
        {
            const int across = 10, over = 12;
            List<Vector3> verts = new List<Vector3>();
            List<Vector2> uvs = new List<Vector2>();
            List<int> tris = new List<int>();
            for (int i = 0; i <= across; i++)
            {
                for (int j = 0; j <= over; j++)
                {
                    float u = i / (float)across, v = j / (float)over;
                    float angle = v * Mathf.PI;
                    verts.Add(new Vector3(-width * 0.5f + u * width, Mathf.Sin(angle) * height, Mathf.Cos(angle) * depth));
                    uvs.Add(new Vector2(u, v));
                }
            }
            for (int i = 0; i < across; i++)
            {
                for (int j = 0; j < over; j++)
                {
                    int a = i * (over + 1) + j, b = a + over + 1;
                    tris.Add(a); tris.Add(b); tris.Add(a + 1);
                    tris.Add(a + 1); tris.Add(b); tris.Add(b + 1);
                }
            }
            Mesh mesh = new Mesh();
            mesh.name = "WhaleWaveCrest";
            mesh.SetVertices(verts);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return SaveAsset(mesh, GeneratedFolder + "/WhaleWaveCrest.asset");
        }

        static GameObject MakeWaveSegment()
        {
            GameObject root = new GameObject("WhaleWave");
            BoxCollider box = root.AddComponent<BoxCollider>();
            box.size = new Vector3(3.8f, 1.6f, 1.6f);
            box.center = new Vector3(0f, 0.6f, 0f);
            box.isTrigger = true;

            Transform visual = Child("Visual", root.transform).transform;
            GameObject crest = Child("Crest", visual);
            crest.AddComponent<MeshFilter>().sharedMesh = CrestMesh(4.2f, 1.4f, 0.9f);
            MeshRenderer renderer = crest.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = ShaderMaterial("M_WaveCrest", "StarlitTides/WaveCrest");
            renderer.shadowCastingMode = ShadowCastingMode.Off;

            WhaleWave wave = root.AddComponent<WhaleWave>();
            wave.type = ObstacleType.Rock;
            wave.radius = 2f;
            wave.lightDamage = 0.15f;
            wave.visual = visual;
            wave.floatOffset = -0.1f;
            wave.bobAmplitude = 0.05f;

            WaterDisturber glow = root.AddComponent<WaterDisturber>();
            glow.radius = 2.6f;
            glow.strength = 1.1f;
            return root;
        }

        // VFX polish pass: built by VfxKit (spray column, crown, mist, foam and glow rings, plankton spread).
        static GameObject MakeBigSplash() { return VfxKit.WhaleSplash(); }

        // ==============================================================
        // Bonus 8: living village
        // ==============================================================
        static void MakeLivingVillage(Transform root)
        {
            LivingVillage village = Child("Living Village (Bonus 8)", root).AddComponent<LivingVillage>();
            const string materials = "Assets/_StarlitTides/Generated/Materials/";
            village.litMaterial = AssetDatabase.LoadAssetAtPath<Material>(materials + "M_WindowLit.mat");
            village.warmMaterial = AssetDatabase.LoadAssetAtPath<Material>(materials + "M_WindowWarm.mat");
            village.offMaterial = AssetDatabase.LoadAssetAtPath<Material>(materials + "M_WindowOff.mat");

            // Every window pane the environment builder made is called "Window_<number>".
            foreach (MeshRenderer renderer in Object.FindObjectsByType<MeshRenderer>())
                if (renderer.gameObject.name.StartsWith("Window_")) village.windows.Add(renderer);
        }

        // ==============================================================
        // Bonus 9: photo mode camera and volume
        // ==============================================================
        static void MakePhotoMode(Transform root)
        {
            GameObject photoObject = Child("Photo Mode (Bonus 9)", root);
            PhotoMode photo = photoObject.AddComponent<PhotoMode>();

            CinemachineCamera cam = Child("CM Photo Camera", photoObject.transform).AddComponent<CinemachineCamera>();
            cam.Priority.Enabled = true;
            cam.Priority.Value = 0;
            LensSettings lens = LensSettings.Default;
            lens.FieldOfView = 45f;
            lens.FarClipPlane = 3000f;
            cam.Lens = lens;
            photo.photoCamera = cam;

            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
            SaveAsset(profile, GeneratedFolder + "/PP_PhotoMode.asset");
            DepthOfField dof = AddOverride<DepthOfField>(profile);
            dof.mode.Override(DepthOfFieldMode.Bokeh);
            dof.focusDistance.Override(10f);
            dof.aperture.Override(4f);
            dof.focalLength.Override(70f);
            ColorAdjustments colour = AddOverride<ColorAdjustments>(profile);
            colour.colorFilter.Override(Color.white);
            colour.saturation.Override(0f);
            colour.contrast.Override(0f);
            colour.postExposure.Override(0f);
            AddOverride<Vignette>(profile).intensity.Override(0.25f);
            AddOverride<FilmGrain>(profile).intensity.Override(0f);
            EditorUtility.SetDirty(profile);

            Volume volume = photoObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 20f;
            volume.weight = 0f;
            volume.sharedProfile = profile;
            photo.photoVolume = volume;

            DebugHUD hud = Object.FindAnyObjectByType<DebugHUD>();
            photo.hideWhileActive = hud != null ? new Behaviour[] { hud } : new Behaviour[0];
        }
    }
}
#endif
