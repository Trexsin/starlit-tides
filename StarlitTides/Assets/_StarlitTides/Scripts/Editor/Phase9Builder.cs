// Phase9Builder: adds the voyage to the scene (Phase 9).
// Menu: Starlit Tides > 12. Build Voyage (Phase 9)   (after 5, 7, 10 and 11)
//       Starlit Tides > Rebuild Everything            (runs it last)
//
//   - VoyageDirector: the six zones (looks, rules, pacing, the coast departing)
//   - EncounterDirector: planned creature encounters, wired to the life-size prefabs
//   - two real-scale jellyfish: moon jelly (0.4 m bell, blooms) and lion's mane (2 m)
//   - SpeedFx: water streaks, bow spray, air lines and a Speed volume
//     (chromatic aberration + lens) that pulses on dashes and zone surges
//   - hidden orbs: rarer and worth a little less (the light economy, see the plan)

#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace StarlitTides.EditorTools
{
    public static class Phase9Builder
    {
        const string RootName = "== STARLIT VOYAGE (Phase 9) ==";
        const string Gen = "Assets/_StarlitTides/Generated/Phase9";
        const string GameplayPrefabs = "Assets/_StarlitTides/Prefabs/Gameplay";

        [MenuItem("Starlit Tides/12. Build Voyage (Phase 9)", priority = 27)]
        public static void BuildMenu()
        {
            if (Object.FindAnyObjectByType<ObstacleSpawner>() == null)
            {
                EditorUtility.DisplayDialog("Starlit Tides", "Build the gameplay first (Starlit Tides > 5. Build Gameplay).", "OK");
                return;
            }
            Build();
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        }

        public static void Build()
        {
            EnsureFolder(Gen);
            GameObject old = GameObject.Find(RootName);
            if (old != null) Object.DestroyImmediate(old);
            Transform root = new GameObject(RootName).transform;

            // ---- the voyage ----
            var voyage = new GameObject("Voyage Director").AddComponent<VoyageDirector>();
            voyage.transform.SetParent(root, false);
            voyage.zones = VoyageDirector.DefaultZones();

            // ---- creatures ----
            // (The moon jelly's body is life-size, 0.4 m; its glow halo is ~1.7 m so a bloom reads from afar.)
            GameObject moonJelly = BuildJellyVariant("MoonJelly", 0.33f, 0.33f,
                new Color(1.2f, 1.1f, 3.2f), new Color(0.8f, 1.2f, 3.0f), new Color(0.75f, 0.9f, 2.6f),
                radius: 0.45f, colliderRadius: 0.35f, damage: 0.08f, floatOffset: -0.06f, light: false, haloSize: 1.7f);
            GameObject lionsMane = BuildJellyVariant("LionsMane", 1.67f, 2.0f,
                new Color(2.6f, 1.0f, 0.3f), new Color(2.4f, 0.5f, 0.25f), new Color(2.4f, 0.9f, 0.3f),
                radius: 2.2f, colliderRadius: 1.9f, damage: 0.2f, floatOffset: -0.3f, light: true, haloSize: 5f);

            var enc = new GameObject("Encounter Director").AddComponent<EncounterDirector>();
            enc.transform.SetParent(root, false);
            enc.moonJelly = moonJelly;
            enc.lionsMane = lionsMane;
            enc.turtle = Prefab("SeaTurtle");
            enc.dolphin = Prefab("Dolphin");
            enc.manta = Prefab("MantaRay");
            enc.orcaMale = Prefab("Orca");
            enc.orcaFemale = Prefab("OrcaFemale");
            enc.humpback = Prefab("Humpback");
            enc.giantWhale = Object.FindAnyObjectByType<WhaleEvent>();
            if (enc.orcaFemale == null || enc.humpback == null)
                Debug.LogWarning("[Starlit Tides] Phase 9: run 10. Build Sea Creatures first (OrcaFemale / Humpback prefabs missing).");

            // Single jellies as filler are the small moon jellies (the big pink one is retired).
            ObstacleSpawner spawner = Object.FindAnyObjectByType<ObstacleSpawner>();
            if (spawner != null) { spawner.jellyfishPrefabs = new[] { moonJelly }; EditorUtility.SetDirty(spawner); }

            // Hidden orbs: rarer, and only in Lantern Bay (VoyageDirector).
            HiddenOrbs hidden = Object.FindAnyObjectByType<HiddenOrbs>();
            if (hidden != null) { hidden.spawnInterval = new Vector2(10f, 16f); hidden.lightValue = 0.15f; EditorUtility.SetDirty(hidden); }

            // ---- speed ----
            BuildSpeedFx(root);

            // ---- colour: a storm you can still see in, a richer magenta sunset ----
            SkyController sky = Object.FindAnyObjectByType<SkyController>();
            if (sky != null)
            {
                sky.stormPreset = new SkyController.AtmospherePreset
                {
                    fogColor = new Color(0.07f, 0.09f, 0.15f), fogDensity = 0.0022f,
                    ambientSky = new Color(0.16f, 0.2f, 0.34f), ambientEquator = new Color(0.1f, 0.12f, 0.2f), ambientGround = new Color(0.03f, 0.03f, 0.05f),
                    lightColor = new Color(0.6f, 0.7f, 0.95f), lightIntensity = 0.55f,
                };
                sky.sunsetPreset = new SkyController.AtmospherePreset
                {
                    fogColor = new Color(0.44f, 0.22f, 0.33f), fogDensity = 0.0008f,
                    ambientSky = new Color(0.42f, 0.34f, 0.6f), ambientEquator = new Color(0.68f, 0.36f, 0.34f), ambientGround = new Color(0.12f, 0.07f, 0.08f),
                    lightColor = new Color(1.0f, 0.6f, 0.38f), lightIntensity = 1.4f,
                };
                EditorUtility.SetDirty(sky);
            }

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[Starlit Tides] Phase 9 voyage built: 6 zones, encounter director, life-size jellies, speed effects.");
        }

        static GameObject Prefab(string name)
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>(GameplayPrefabs + "/" + name + ".prefab");
        }

        // A jellyfish at real size, made from the Jellyfish prefab with its own colours.
        static GameObject BuildJellyVariant(string name, float scale, float scaleY, Color bell, Color tentacle, Color halo,
                                            float radius, float colliderRadius, float damage, float floatOffset, bool light, float haloSize)
        {
            GameObject source = Prefab("Jellyfish");
            if (source == null) throw new System.Exception("[Starlit Tides] Jellyfish prefab missing: run 5. Build Gameplay first.");
            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(source);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = name;

            Jellyfish j = go.GetComponent<Jellyfish>();
            Transform visual = j.visual != null ? j.visual : go.transform.GetChild(0);
            visual.localScale = new Vector3(scale, scaleY, scale);
            Transform haloT = visual.Find("Halo");
            if (haloT != null) haloT.localScale = new Vector3(haloSize / scale, haloSize / scaleY, haloSize / scale);

            // Materials: copies with this species' colours.
            foreach (MeshRenderer r in visual.GetComponentsInChildren<MeshRenderer>(true))
            {
                Material src = r.sharedMaterial;
                if (src == null) continue;
                Material m = new Material(src) { name = "M_" + name + "_" + r.gameObject.name };
                if (m.HasProperty("_BellColor")) { m.SetColor("_BellColor", bell); m.SetColor("_TentacleColor", tentacle); }
                else if (m.HasProperty("_TintColor")) m.SetColor("_TintColor", halo);
                r.sharedMaterial = SaveAsset(m, Gen + "/" + m.name + ".mat");
            }

            SphereCollider col = go.GetComponent<SphereCollider>();
            if (col != null) { col.radius = colliderRadius; col.center = new Vector3(0f, -0.1f * scaleY, 0f); }

            j.radius = radius; j.lightDamage = damage; j.floatOffset = floatOffset;
            j.bobAmplitude = 0.06f * Mathf.Max(1f, scale); j.followWaves = 0.9f;

            WaterDisturber disturber = go.GetComponent<WaterDisturber>();
            Light glow = go.GetComponentInChildren<Light>(true);
            if (!light)
            {
                // Blooms bring dozens of moon jellies: no point lights or sea-glow points each
                // (their own glowing bells and halos are plenty, and it keeps the frame rate).
                if (glow != null) Object.DestroyImmediate(glow.gameObject);
                if (disturber != null) Object.DestroyImmediate(disturber);
                j.glowLight = null; j.waterGlow = null;
            }
            else
            {
                if (glow != null) { glow.color = new Color(1f, 0.6f, 0.3f); glow.intensity = 2.4f; glow.range = 7f; j.glowLightBase = 2.4f; }
                if (disturber != null) disturber.radius = 3.2f;
            }

            string path = GameplayPrefabs + "/" + name + ".prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        static void BuildSpeedFx(Transform root)
        {
            var fx = new GameObject("Speed FX").AddComponent<SpeedFx>();
            fx.transform.SetParent(root, false);

            fx.waterStreaks = VfxKit.WaterStreaks(fx.transform);

            BoatController boat = Object.FindAnyObjectByType<BoatController>();
            if (boat != null)
            {
                Transform oldSpray = boat.transform.Find("Bow Spray");
                if (oldSpray != null) Object.DestroyImmediate(oldSpray.gameObject);
                fx.bowSpray = VfxKit.BowSpray(boat.transform);
            }

            Camera cam = Camera.main;
            if (cam != null)
            {
                Transform oldLines = cam.transform.Find("Air Lines");
                if (oldLines != null) Object.DestroyImmediate(oldLines.gameObject);
                fx.airLines = VfxKit.AirLines(cam.transform);
            }

            // The Speed volume: a little colour fringing and lens bulge, weighted by SpeedFx.
            string profilePath = Gen + "/PP_Speed.asset";
            if (AssetDatabase.LoadAssetAtPath<Object>(profilePath) != null) AssetDatabase.DeleteAsset(profilePath);
            var profile = ScriptableObject.CreateInstance<VolumeProfile>();
            AssetDatabase.CreateAsset(profile, profilePath);
            var ca = profile.Add<ChromaticAberration>(true); ca.name = "ChromaticAberration"; ca.intensity.Override(0.55f);
            AssetDatabase.AddObjectToAsset(ca, profile);
            var lens = profile.Add<LensDistortion>(true); lens.name = "LensDistortion"; lens.intensity.Override(-0.16f);
            AssetDatabase.AddObjectToAsset(lens, profile);
            EditorUtility.SetDirty(profile);
            var volGo = new GameObject("Speed Volume");
            volGo.transform.SetParent(fx.transform, false);
            var vol = volGo.AddComponent<Volume>();
            vol.isGlobal = true; vol.priority = 9f; vol.weight = 0f; vol.sharedProfile = profile;
            fx.speedVolume = vol;
        }

        static T SaveAsset<T>(T asset, string path) where T : Object
        {
            if (AssetDatabase.LoadAssetAtPath<Object>(path) != null) AssetDatabase.DeleteAsset(path);
            AssetDatabase.CreateAsset(asset, path);
            return asset;
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
