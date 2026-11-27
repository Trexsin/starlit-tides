// FeaturesBuilder: adds Extra Features 1-6 to the open scene with one click.
// Menu: Starlit Tides > 6. Build Features
//
// Run "4. Build Environment" and "5. Build Gameplay" first.
// It creates (prefabs and assets are saved under Assets/_StarlitTides):
//   - Darkness Volume              (Feature 1: darkness closes in as the light fades)
//   - ComboSystem                  (Feature 2)
//   - Shooting star + golden star  (Feature 3: Starburst)
//   - Rain + WeatherManager        (Feature 4: calm -> storm -> aurora)
//   - Near-miss sparkle            (Feature 5)
//   - Intro Timeline + 5 cameras   (Feature 6: sunset -> night flythrough)
// Running it again rebuilds everything inside "== STARLIT FEATURES ==".

#if UNITY_EDITOR
using System.IO;
using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Playables;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Timeline;

namespace StarlitTides.EditorTools
{
    public static class FeaturesBuilder
    {
        const string RootName = "== STARLIT FEATURES ==";
        const string RainName = "Rain (Weather)";
        const string GeneratedFolder = "Assets/_StarlitTides/Generated/Features";
        const string PrefabFolder = "Assets/_StarlitTides/Prefabs/Gameplay";
        const string VfxFolder = "Assets/_StarlitTides/Prefabs/VFX";
        const string TimelineFolder = "Assets/_StarlitTides/Timeline";

        static Texture2D glowDot;

        [MenuItem("Starlit Tides/6. Build Features", priority = 22)]
        public static void BuildMenu()
        {
            if (Object.FindAnyObjectByType<GameManager>() == null)
            {
                EditorUtility.DisplayDialog("Starlit Tides", "Build the gameplay first (Starlit Tides > 5. Build Gameplay).", "OK");
                return;
            }
            bool ok = Application.isBatchMode || EditorUtility.DisplayDialog("Build Starlit Tides Features",
                "Adds Extra Features 1-6 (darkness, combo, Starburst, weather, near miss, intro cinematic).\n\n" +
                "'" + RootName + "' will be rebuilt if it already exists.", "Build", "Cancel");
            if (ok) Build();
        }

        public static void Build()
        {
            MakeFolder(GeneratedFolder);
            MakeFolder(PrefabFolder);
            MakeFolder(VfxFolder);
            MakeFolder(TimelineFolder);

            glowDot = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/_StarlitTides/Generated/Gameplay/T_GlowDot.png");
            if (glowDot == null) throw new System.Exception("[Starlit Tides] Run '5. Build Gameplay' first (the glow texture is missing).");

            // Remove anything from a previous build.
            GameObject old = GameObject.Find(RootName);
            if (old != null) Object.DestroyImmediate(old);
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                Transform oldRain = mainCamera.transform.Find(RainName);
                if (oldRain != null) Object.DestroyImmediate(oldRain.gameObject);
            }

            GameObject root = new GameObject(RootName);
            GameManager game = Object.FindAnyObjectByType<GameManager>();
            BoatController boat = Object.FindAnyObjectByType<BoatController>();
            LightMeter meter = boat.GetComponent<LightMeter>();

            // ---- Feature 1: darkness closing in ----
            GameObject visionObject = Child("Light Vision (Feature 1)", root.transform);
            LightVision vision = visionObject.AddComponent<LightVision>();
            vision.lightMeter = meter;
            vision.darknessVolume = MakeDarknessVolume(visionObject.transform);

            // ---- Feature 2: combo ----
            ComboSystem combo = Child("Combo System (Feature 2)", root.transform).AddComponent<ComboSystem>();
            combo.boatWake = boat.GetComponentInChildren<BoatWake>();
            combo.boatGlow = boat.GetComponent<WaterDisturber>();

            // ---- Feature 3: shooting stars and Starburst ----
            PowerUpManager power = Child("Power Ups (Feature 3)", root.transform).AddComponent<PowerUpManager>();
            power.shootingStarPrefab = SavePrefab(MakeShootingStar(), PrefabFolder + "/ShootingStar.prefab");
            power.starPickupPrefab = SavePrefab(MakeStarPickup(), PrefabFolder + "/GoldenStarPickup.prefab");
            power.starburstBurstVfx = SavePrefab(MakeBurst("VFX_Starburst", new Color(3.5f, 2.6f, 0.9f), 80, 9f, 1.2f), VfxFolder + "/VFX_Starburst.prefab");

            // ---- Feature 4: weather ----
            WeatherManager weather = Child("Weather (Feature 4)", root.transform).AddComponent<WeatherManager>();
            if (mainCamera != null) weather.rain = MakeRain(mainCamera.transform);

            // ---- Feature 5: near miss ----
            NearMissDetector nearMiss = Child("Near Miss (Feature 5)", root.transform).AddComponent<NearMissDetector>();
            nearMiss.sparkleVfx = SavePrefab(MakeBurst("VFX_NearMiss", new Color(1.5f, 3f, 3.5f), 40, 5f, 0.7f), VfxFolder + "/VFX_NearMiss.prefab");

            // ---- Feature 6: intro cinematic ----
            MakeIntro(root.transform, mainCamera);
            game.autoStartGameplay = false;   // the intro starts the run now
            EditorUtility.SetDirty(game);

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[Starlit Tides] Features built. Press Play: the intro plays first (Space to skip).");
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

        static Material GlowMaterial(string name, Color tint)
        {
            Material m = new Material(Shader.Find("StarlitTides/GlowParticle"));
            m.name = name;
            m.SetTexture("_MainTex", glowDot);
            m.SetColor("_TintColor", tint);
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
        // Feature 1: a Volume that darkens the screen edges and exposure
        // ==============================================================
        static Volume MakeDarknessVolume(Transform parent)
        {
            VolumeProfile profile = ScriptableObject.CreateInstance<VolumeProfile>();
            SaveAsset(profile, GeneratedFolder + "/PP_Darkness.asset");

            Vignette vignette = AddOverride<Vignette>(profile);
            vignette.intensity.Override(0.62f);
            vignette.smoothness.Override(0.9f);
            vignette.color.Override(new Color(0f, 0f, 0.02f));

            ColorAdjustments adjust = AddOverride<ColorAdjustments>(profile);
            adjust.postExposure.Override(-1.3f);
            adjust.saturation.Override(-35f);
            EditorUtility.SetDirty(profile);

            Volume volume = parent.gameObject.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 5f;
            volume.weight = 0f;
            volume.sharedProfile = profile;
            return volume;
        }

        // ==============================================================
        // Feature 3: shooting star, golden star pickup, bursts
        // ==============================================================
        static GameObject MakeShootingStar()
        {
            GameObject star = new GameObject("ShootingStar");
            TrailRenderer trail = star.AddComponent<TrailRenderer>();
            trail.time = 0.55f;
            trail.widthCurve = new AnimationCurve(new Keyframe(0f, 2.2f), new Keyframe(1f, 0f));
            trail.minVertexDistance = 2f;
            trail.sharedMaterial = GlowMaterial("M_ShootingStarTrail", new Color(2.5f, 2.5f, 2.5f));
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;

            // A bright head on the front of the trail.
            GameObject head = GameObject.CreatePrimitive(PrimitiveType.Quad);
            head.name = "Head";
            Object.DestroyImmediate(head.GetComponent<Collider>());
            head.transform.SetParent(star.transform, false);
            head.transform.localScale = Vector3.one * 9f;
            head.GetComponent<MeshRenderer>().sharedMaterial = GlowMaterial("M_ShootingStarHead", new Color(4f, 3.6f, 3f));
            head.AddComponent<FaceCamera>();

            ShootingStar script = star.AddComponent<ShootingStar>();
            script.trail = trail;
            return star;
        }

        static GameObject MakeStarPickup()
        {
            GameObject root = new GameObject("GoldenStarPickup");
            SphereCollider collider = root.AddComponent<SphereCollider>();
            collider.radius = 1.1f;
            collider.isTrigger = true;

            Transform visual = new GameObject("Visual").transform;
            visual.SetParent(root.transform, false);

            // A spiky golden star: an icosphere with big noise looks like a sparkly crystal.
            MeshBuilder mb = new MeshBuilder(1);
            ProcMesh.Icosphere(mb, 0, Vector3.zero, 0.45f, 1, 77, 0.45f);
            Mesh mesh = SaveAsset(mb.ToMesh("GoldenStar"), GeneratedFolder + "/GoldenStar.asset");

            // Use the Shader Graph from the Phase 4 lesson if it exists, otherwise a glowing Lit material.
            Material material;
            Shader graph = Shader.Find("Shader Graphs/SG_GoldenStar");
            if (graph != null)
            {
                material = new Material(graph);
            }
            else
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                material.SetColor("_BaseColor", new Color(1f, 0.8f, 0.35f));
                material.SetFloat("_Smoothness", 0.9f);
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", new Color(5f, 3.4f, 1.1f));
            }
            material.name = "M_GoldenStar";
            SaveAsset(material, GeneratedFolder + "/M_GoldenStar.mat");

            GameObject crystal = new GameObject("Crystal");
            crystal.transform.SetParent(visual, false);
            crystal.AddComponent<MeshFilter>().sharedMesh = mesh;
            crystal.AddComponent<MeshRenderer>().sharedMaterial = material;

            GameObject halo = GameObject.CreatePrimitive(PrimitiveType.Quad);
            halo.name = "Halo";
            Object.DestroyImmediate(halo.GetComponent<Collider>());
            halo.transform.SetParent(visual, false);
            halo.transform.localScale = Vector3.one * 3.2f;
            halo.GetComponent<MeshRenderer>().sharedMaterial = GlowMaterial("M_GlowGoldStrong", new Color(4f, 2.8f, 0.9f));
            halo.AddComponent<FaceCamera>();

            StarPickup pickup = root.AddComponent<StarPickup>();
            pickup.visual = visual;
            pickup.floatOffset = 0.8f;
            pickup.bobAmplitude = 0.25f;
            pickup.bobSpeed = 2.5f;
            pickup.spinSpeed = 140f;
            pickup.pulseAmount = 0.15f;

            WaterDisturber glow = root.AddComponent<WaterDisturber>();
            glow.radius = 2.2f;
            glow.strength = 1.4f;
            glow.pulse = 0.5f;
            glow.pulseSpeed = 5f;
            glow.maxHeightAboveWater = 2f;
            return root;
        }

        // A round burst of glowing sparks (used for Starburst and near misses).
        static GameObject MakeBurst(string name, Color color, int count, float speed, float life)
        {
            GameObject go = new GameObject(name);
            ParticleSystem ps = go.AddComponent<ParticleSystem>();

            ParticleSystem.MainModule main = ps.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 0.3f;
            main.startLifetime = new ParticleSystem.MinMaxCurve(life * 0.6f, life);
            main.startSpeed = new ParticleSystem.MinMaxCurve(speed * 0.4f, speed);
            main.startSize = new ParticleSystem.MinMaxCurve(0.12f, 0.3f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;
            emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)count) });

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Sphere;
            shape.radius = 0.4f;

            ParticleSystem.LimitVelocityOverLifetimeModule drag = ps.limitVelocityOverLifetime;
            drag.enabled = true;
            drag.drag = 2f;
            drag.limit = 100f;

            ParticleSystem.ColorOverLifetimeModule fade = ps.colorOverLifetime;
            fade.enabled = true;
            Gradient gradient = new Gradient();
            gradient.SetKeys(
                new[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
            fade.color = gradient;

            ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = GlowMaterial("M_" + name, color);
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.05f;
            renderer.lengthScale = 1.5f;
            renderer.shadowCastingMode = ShadowCastingMode.Off;

            go.AddComponent<PooledParticle>();
            return go;
        }

        // ==============================================================
        // Feature 4: rain that follows the camera
        // ==============================================================
        static ParticleSystem MakeRain(Transform cameraTransform)
        {
            GameObject go = new GameObject(RainName);
            go.transform.SetParent(cameraTransform, false);
            go.transform.localPosition = new Vector3(0f, 12f, 18f);
            go.transform.localRotation = Quaternion.identity;

            ParticleSystem ps = go.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = ps.main;
            main.loop = true;
            main.playOnAwake = true;
            main.startLifetime = 1.2f;
            main.startSpeed = 0f;
            main.startSize = new ParticleSystem.MinMaxCurve(0.03f, 0.06f);
            main.startColor = new Color(0.6f, 0.75f, 1f, 0.35f);
            main.maxParticles = 4000;
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            ParticleSystem.EmissionModule emission = ps.emission;
            emission.rateOverTime = 0f;   // the WeatherManager turns it up during storms

            ParticleSystem.ShapeModule shape = ps.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(60f, 1f, 60f);

            ParticleSystem.VelocityOverLifetimeModule velocity = ps.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = new ParticleSystem.MinMaxCurve(-2f, -1f);
            velocity.y = new ParticleSystem.MinMaxCurve(-26f, -22f);
            velocity.z = new ParticleSystem.MinMaxCurve(-5f, -3f);

            ParticleSystemRenderer renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.sharedMaterial = GlowMaterial("M_Rain", new Color(0.35f, 0.45f, 0.6f));
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.04f;
            renderer.lengthScale = 2f;
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            return ps;
        }

        // ==============================================================
        // Feature 6: the intro Timeline
        // ==============================================================
        struct Shot
        {
            public string name;
            public Vector3 position;
            public Vector3 lookAt;
            public Vector3 drift;
            public double start;
            public double duration;
        }

        static void MakeIntro(Transform root, Camera mainCamera)
        {
            CinemachineBrain brain = mainCamera.GetComponent<CinemachineBrain>();
            brain.DefaultBlend = new CinemachineBlendDefinition(CinemachineBlendDefinition.Styles.EaseInOut, 2.5f);
            EditorUtility.SetDirty(brain);

            // The SkyController and OceanController live on the "Controllers" object.
            // The Animation track needs an Animator there to animate their sliders.
            SkyController sky = Object.FindAnyObjectByType<SkyController>();
            Animator animator = sky.GetComponent<Animator>();
            if (animator == null) animator = sky.gameObject.AddComponent<Animator>();

            GameObject introObject = Child("Intro Cinematic (Feature 6)", root);
            PlayableDirector director = introObject.AddComponent<PlayableDirector>();
            director.playOnAwake = false;
            director.extrapolationMode = DirectorWrapMode.None;
            IntroCinematic intro = introObject.AddComponent<IntroCinematic>();
            intro.director = director;

            // Five shots. Each overlaps the next by 1 second, which Cinemachine turns into a smooth blend.
            Shot[] shots =
            {
                new Shot { name = "1 Sunset over the mountains", position = new Vector3(90f, 45f, -60f), lookAt = new Vector3(-150f, 60f, 650f), drift = new Vector3(-4f, 0f, 6f), start = 0, duration = 5 },
                new Shot { name = "2 The village", position = new Vector3(-24f, 12f, 6f), lookAt = new Vector3(-50f, 4f, 34f), drift = new Vector3(1.5f, -0.3f, 2.5f), start = 4, duration = 5.5 },
                new Shot { name = "3 Bridge and lagoon", position = new Vector3(-12f, 9f, 72f), lookAt = new Vector3(-48f, 2f, 98f), drift = new Vector3(-2f, 0f, 3f), start = 8.5, duration = 5 },
                new Shot { name = "4 Forest and lighthouse", position = new Vector3(-55f, 30f, 120f), lookAt = new Vector3(-12f, 26f, 182f), drift = new Vector3(3f, -0.5f, 2f), start = 12.5, duration = 5 },
                new Shot { name = "5 The sea awakens", position = new Vector3(8f, 9f, -24f), lookAt = new Vector3(-3f, 0.5f, 30f), drift = new Vector3(0f, -0.4f, 3f), start = 16.5, duration = 5.5 },
            };

            TimelineAsset timeline = ScriptableObject.CreateInstance<TimelineAsset>();
            SaveAsset(timeline, TimelineFolder + "/IntroTimeline.playable");
            director.playableAsset = timeline;

            // Track 1: the camera shots.
            CinemachineTrack cameraTrack = timeline.CreateTrack<CinemachineTrack>(null, "Intro Cameras");
            director.SetGenericBinding(cameraTrack, brain);
            Transform cameraParent = Child("Intro Cameras", introObject.transform).transform;

            // Shots 2-4 film the land, which WorldLayout turned so the boat sails out to sea.
            for (int i = 1; i <= 3; i++)
            {
                shots[i].position = WorldLayout.ToWorld(shots[i].position);
                shots[i].lookAt = WorldLayout.ToWorld(shots[i].lookAt);
                shots[i].drift = WorldLayout.ToWorld(shots[i].drift);
            }

            foreach (Shot shot in shots)
            {
                GameObject camObject = Child(shot.name, cameraParent);
                camObject.transform.position = shot.position;
                camObject.transform.rotation = Quaternion.LookRotation(shot.lookAt - shot.position);
                CinemachineCamera cam = camObject.AddComponent<CinemachineCamera>();
                cam.Priority.Enabled = true;
                cam.Priority.Value = 0;          // only live while the Timeline uses it
                LensSettings lens = LensSettings.Default;
                lens.FieldOfView = 50f;
                lens.FarClipPlane = 3000f;
                cam.Lens = lens;
                CameraDrift drift = camObject.AddComponent<CameraDrift>();
                drift.velocity = shot.drift;

                TimelineClip clip = cameraTrack.CreateClip<CinemachineShot>();
                clip.displayName = shot.name;
                clip.start = shot.start;
                clip.duration = shot.duration;
                CinemachineShot shotAsset = (CinemachineShot)clip.asset;
                shotAsset.VirtualCamera.exposedName = GUID.Generate().ToString();
                director.SetReferenceValue(shotAsset.VirtualCamera.exposedName, cam);
            }

            // Track 2: sunset -> night, and the plankton waking up.
            AnimationClip skyClip = new AnimationClip();
            skyClip.name = "IntroSunsetToNight";
            skyClip.SetCurve("", typeof(SkyController), "sunset",
                new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(4f, 1f), new Keyframe(15f, 0f)));
            skyClip.SetCurve("", typeof(OceanController), "nightGlow",
                new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(9f, 0f), new Keyframe(18f, 1f)));
            SaveAsset(skyClip, TimelineFolder + "/IntroSunsetToNight.anim");

            AnimationTrack skyTrack = timeline.CreateTrack<AnimationTrack>(null, "Sky and Sea");
            director.SetGenericBinding(skyTrack, animator);
            TimelineClip skyTimelineClip = skyTrack.CreateClip(skyClip);
            skyTimelineClip.start = 0;
            skyTimelineClip.duration = 22;

            EditorUtility.SetDirty(timeline);
            EditorUtility.SetDirty(director);
        }
    }
}
#endif
