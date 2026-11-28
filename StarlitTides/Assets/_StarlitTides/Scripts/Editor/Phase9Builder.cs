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
            // (Moon jelly 0.6 m and lion's mane 2.4 m: a touch bigger than average real ones so they
            //  read well from the boat; the moon jelly's glow halo is ~2 m so a bloom reads from afar.)
            // Moon jelly (Aurelia): a milky, see-through saucer with the pink four-leaf clover,
            // ~1.2 m across (bigger than life, so it reads at speed). Lion's mane: amber and red, ~2.9 m, long fringe.
            // (Neither is a glowing ball any more: that is what the orbs look like.)
            GameObject moonJelly = BuildJellyVariant("MoonJelly", 1.0f, 1.0f, new JellyLook
                {
                    body = new Color(0.82f, 0.76f, 1f, 0.38f), clover = new Color(3.4f, 0.6f, 2.4f), rim = new Color(2.2f, 0.9f, 3.2f),
                    tentacles = new Color(1.5f, 0.7f, 2.0f), fringe = 56, fringeLength = 0.55f, armLength = 1.1f, dome = 0.5f, pulseSpeed = 1.6f
                },
                radius: 0.9f, colliderRadius: 0.75f, damage: 0.15f, floatOffset: 0.02f, light: false);
            GameObject lionsMane = BuildJellyVariant("LionsMane", 2.4f, 2.2f, new JellyLook
                {
                    body = new Color(1f, 0.6f, 0.32f, 0.36f), clover = new Color(3.4f, 0.75f, 0.25f), rim = new Color(3.0f, 1.4f, 0.4f),
                    tentacles = new Color(2.6f, 0.8f, 0.3f), fringe = 120, fringeLength = 2.6f, armLength = 1.8f, dome = 0.55f, pulseSpeed = 1.1f
                },
                radius: 3.0f, colliderRadius: 2.5f, damage: 0.3f, floatOffset: -0.25f, light: true);

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

            // ---- no glowing "highlight" ring on the water around obstacles and orbs ----
            // (Players found the bright plankton rings around every rock, log, orb and
            //  creature too much: the sea should glow, not outline the hazards.)
            string[] plain = { "Rock_1", "Rock_2", "Rock_3", "Log", "Orb", "LionsMane", "MantaRay", "SeaTurtle", "Dolphin", "Orca", "OrcaFemale", "Humpback", "Jellyfish" };
            foreach (string n in plain) RemoveWaterGlow(GameplayPrefabs + "/" + n + ".prefab");

            // ---- challenge: the dash is a choice, not a reflex; every collision costs light ----
            // Players dashed every second (1.1 s cooldown, 0.3 s untouchable each time), so
            // they rarely got hit at all; and the small creatures cost almost nothing.
            BoatController dashBoat = Object.FindAnyObjectByType<BoatController>();
            if (dashBoat != null) { dashBoat.dashCooldown = 2.5f; dashBoat.dashInvulnerability = 0.2f; EditorUtility.SetDirty(dashBoat); }
            SetMinimumDamage("SeaTurtle", 0.15f);
            SetMinimumDamage("Dolphin", 0.2f);
            SetMinimumDamage("MantaRay", 0.2f);
            SetMinimumDamage("Log", 0.22f);

            // ---- challenge: combos break after 2 s without an orb (orbs are ~1.05-1.3 s apart) ----
            ComboSystem combo = Object.FindAnyObjectByType<ComboSystem>();
            if (combo != null) { combo.comboWindow = 2.0f; EditorUtility.SetDirty(combo); }
            OrbSpawner orbs = Object.FindAnyObjectByType<OrbSpawner>();
            if (orbs != null) { orbs.maxActiveOrbs = 18; EditorUtility.SetDirty(orbs); }

            // ---- lighter on laptops (heat, battery, memory) ----
            ApplyPerformanceSettings();

            // ---- a more vivid picture ----
            ApplyVividGrade();

            // ---- forward movement: the camera keeps the boat close (it drifted too far ahead) ----
            CameraAnchor anchorFollow = Object.FindAnyObjectByType<CameraAnchor>();
            if (anchorFollow != null) { anchorFollow.followZ = 0.75f; anchorFollow.restZ = 2f; EditorUtility.SetDirty(anchorFollow); }
            PlayArea area = Object.FindAnyObjectByType<PlayArea>();
            if (area != null) { area.laneZ = new Vector2(-3f, 10f); EditorUtility.SetDirty(area); }

            // ---- smooth zone changes (values that live in the scene) ----
            voyage.lookBlendTime = 14f;
            voyage.transitionLead = 6f;
            WeatherManager weather = Object.FindAnyObjectByType<WeatherManager>();
            if (weather != null) { weather.transitionTime = 12f; EditorUtility.SetDirty(weather); }
            AmbientSeaField field = Object.FindAnyObjectByType<AmbientSeaField>();
            if (field != null) { field.sceneryCrossfade = 18f; EditorUtility.SetDirty(field); }

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
                    fogColor = new Color(0.34f, 0.12f, 0.3f), fogDensity = 0.00045f,   // thin, deep magenta: the sea stays rich under the bright sky
                    ambientSky = new Color(0.42f, 0.34f, 0.6f), ambientEquator = new Color(0.68f, 0.36f, 0.34f), ambientGround = new Color(0.12f, 0.07f, 0.08f),
                    lightColor = new Color(1.0f, 0.6f, 0.38f), lightIntensity = 1.4f,
                };
                EditorUtility.SetDirty(sky);
            }

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            Debug.Log("[Starlit Tides] Phase 9 voyage built: 6 zones, encounter director, life-size jellies, speed effects.");
        }

        // Players found the night too dim and muddy (navy and teal, low contrast, glows that
        // didn't pop). This pushes the night grade: more saturation and contrast, a brighter
        // exposure, stronger bloom so glowing things glow, a lighter touch of the colour LUT,
        // cool violet shadows with warm highlights, and a softer vignette.
        // Measured on a MacBook Air: ~550 MB of RAM and the GPU ~88% busy in play. The quality
        // presets carried desktop-class settings: 4x MSAA and 64-bit HDR buffers on High
        // (big render targets: memory and bandwidth = heat), and a 4096 shadow map with 4
        // cascades for a night scene where shadows barely show.
        static void ApplyPerformanceSettings()
        {
            string[] names = { "Low", "Medium", "High" };
            for (int i = 0; i < 3; i++)
            {
                var a = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/_StarlitTides/Settings/URP/URP_" + names[i] + ".asset");
                if (a == null) continue;
                // No MSAA: its depth and colour copies were ~50 MB at 1920 px. SMAA on the camera
                // (SettingsManager) smooths edges almost as well with no extra buffers.
                a.msaaSampleCount = 1;
                a.hdrColorBufferPrecision = HDRColorBufferPrecision._32Bits;
                a.mainLightShadowmapResolution = i == 0 ? 512 : i == 1 ? 1024 : 2048;
                a.shadowDistance = i == 0 ? 35f : i == 1 ? 50f : 70f;
                a.shadowCascadeCount = i == 0 ? 1 : i == 1 ? 1 : 2;
                EditorUtility.SetDirty(a);
            }
            // Generated meshes don't need a second, CPU-side copy at runtime (nothing in the game
            // reads them): only the terrain's grass/tree meshes must stay readable.
            var keepReadable = new HashSet<Mesh>();
            foreach (Terrain t in Object.FindObjectsByType<Terrain>(FindObjectsInactive.Include))
            {
                if (t.terrainData == null) continue;
                foreach (DetailPrototype d in t.terrainData.detailPrototypes)
                    if (d.prototype != null) foreach (MeshFilter mf in d.prototype.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh != null) keepReadable.Add(mf.sharedMesh);
                foreach (TreePrototype tp in t.terrainData.treePrototypes)
                    if (tp.prefab != null) foreach (MeshFilter mf in tp.prefab.GetComponentsInChildren<MeshFilter>(true)) if (mf.sharedMesh != null) keepReadable.Add(mf.sharedMesh);
            }
            int madeStatic = 0;
            foreach (string guid in AssetDatabase.FindAssets("t:Mesh", new[] { "Assets/_StarlitTides/Generated" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                foreach (Object o in AssetDatabase.LoadAllAssetsAtPath(path))
                {
                    if (!(o is Mesh m) || keepReadable.Contains(m) || !m.isReadable) continue;
                    var so = new SerializedObject(m);
                    SerializedProperty readable = so.FindProperty("m_IsReadable");
                    if (readable == null) continue;
                    readable.boolValue = false;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    EditorUtility.SetDirty(m);
                    madeStatic++;
                }
            }
            Debug.Log("[Starlit Tides] Phase 9: " + madeStatic + " meshes no longer keep a CPU copy (" + keepReadable.Count + " terrain meshes kept readable).");

            // Shader code: keep at most 4 decompressed chunks in memory (was unlimited).
            PlayerSettings.SetDefaultShaderChunkCount(4);

            // Dithering hides any banding from the 32-bit colour buffer in the dark gradients.
            Camera cam = Camera.main;
            if (cam != null)
            {
                var data = cam.GetUniversalAdditionalCameraData();
                data.dithering = true;
                EditorUtility.SetDirty(data);
            }
        }

        static void ApplyVividGrade()
        {
            Volume night = null;
            foreach (Volume v in Object.FindObjectsByType<Volume>(FindObjectsInactive.Include))
                if (v.name == "Global Volume (Starlit Night)" || v.name == "Global Volume") { night = v; break; }
            if (night == null || night.sharedProfile == null) { Debug.LogWarning("[Starlit Tides] Phase 9: night volume not found, grade unchanged."); return; }
            VolumeProfile p = night.sharedProfile;
            if (p.TryGet(out ColorAdjustments adj)) { adj.postExposure.Override(0.55f); adj.contrast.Override(30f); adj.saturation.Override(32f); }
            if (p.TryGet(out Bloom bloom)) { bloom.intensity.Override(1.45f); bloom.threshold.Override(0.85f); bloom.scatter.Override(0.66f); }
            if (p.TryGet(out WhiteBalance wb)) { wb.temperature.Override(-3f); wb.tint.Override(4f); }
            if (p.TryGet(out ColorLookup lut)) lut.contribution.Override(0.55f);
            if (p.TryGet(out Vignette vig)) vig.intensity.Override(0.24f);
            if (!p.TryGet(out ShadowsMidtonesHighlights smh))
            {
                smh = p.Add<ShadowsMidtonesHighlights>(true);
                smh.name = "ShadowsMidtonesHighlights";
                AssetDatabase.AddObjectToAsset(smh, p);
            }
            smh.shadows.Override(new Vector4(0.96f, 0.93f, 1.1f, 0f));      // cool violet shadows
            smh.midtones.Override(new Vector4(1f, 1f, 1f, 0.02f));
            smh.highlights.Override(new Vector4(1.06f, 1.01f, 0.95f, 0.03f)); // warm highlights
            EditorUtility.SetDirty(p);
        }

        static void RemoveWaterGlow(string path)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) return;
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            bool changed = false;
            foreach (WaterDisturber d in root.GetComponentsInChildren<WaterDisturber>(true)) { Object.DestroyImmediate(d, true); changed = true; }
            foreach (Jellyfish j in root.GetComponentsInChildren<Jellyfish>(true)) j.waterGlow = null;
            if (changed) PrefabUtility.SaveAsPrefabAsset(root, path);
            PrefabUtility.UnloadPrefabContents(root);
        }

        // Raises a creature's light damage to at least this much (prefab asset).
        static void SetMinimumDamage(string name, float damage)
        {
            string path = GameplayPrefabs + "/" + name + ".prefab";
            if (AssetDatabase.LoadAssetAtPath<GameObject>(path) == null) return;
            GameObject root = PrefabUtility.LoadPrefabContents(path);
            Obstacle ob = root.GetComponent<Obstacle>();
            if (ob != null && ob.lightDamage < damage) { ob.lightDamage = damage; PrefabUtility.SaveAsPrefabAsset(root, path); }
            PrefabUtility.UnloadPrefabContents(root);
        }

        static GameObject Prefab(string name)
        {
            return AssetDatabase.LoadAssetAtPath<GameObject>(GameplayPrefabs + "/" + name + ".prefab");
        }

        // How one species of jellyfish looks (StarlitJellyBell.shader + JellyBellMesh).
        struct JellyLook
        {
            public Color body;          // rgb + opacity of the translucent bell
            public Color clover, rim, tentacles;   // HDR glows
            public int fringe;          // fringe tentacles round the rim
            public float fringeLength;  // in bell radii
            public float armLength;     // oral arms, in bell radii
            public float dome;          // bell height / bell radius
            public float pulseSpeed;
        }

        // A jellyfish at real size, made from the Jellyfish prefab: Phase 9 redesign.
        // The old one was a small blob of pure glow, and players mixed it up with the
        // light orbs. Now it is a translucent moon-jelly body (see StarlitJellyBell.shader).
        static GameObject BuildJellyVariant(string name, float scale, float scaleY, JellyLook look,
                                            float radius, float colliderRadius, float damage, float floatOffset, bool light)
        {
            GameObject source = Prefab("Jellyfish");
            if (source == null) throw new System.Exception("[Starlit Tides] Jellyfish prefab missing: run 5. Build Gameplay first.");
            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(source);
            PrefabUtility.UnpackPrefabInstance(go, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            go.name = name;

            Jellyfish j = go.GetComponent<Jellyfish>();
            Transform visual = j.visual != null ? j.visual : go.transform.GetChild(0);
            visual.localScale = new Vector3(scale, scaleY, scale);
            // No flat halo quad and no round glow sprite: a glowing ball is exactly what an orb looks like.
            Transform haloT = visual.Find("Halo");
            if (haloT != null) Object.DestroyImmediate(haloT.gameObject);
            foreach (ParticleSystem ps in go.GetComponentsInChildren<ParticleSystem>(true)) Object.DestroyImmediate(ps.gameObject);

            // The new body: one mesh (bell, fringe, oral arms), one material.
            Mesh mesh = SaveAsset(JellyBellMesh(look), Gen + "/" + name + "_Body.asset");
            Shader shader = Shader.Find("StarlitTides/JellyBell");
            var mat = new Material(shader) { name = "M_" + name + "_Bell" };
            mat.SetColor("_BodyColor", look.body);
            mat.SetColor("_GonadColor", look.clover);
            mat.SetColor("_RimColor", look.rim);
            mat.SetColor("_TentacleColor", look.tentacles);
            mat.SetFloat("_PulseSpeed", look.pulseSpeed);
            mat = SaveAsset(mat, Gen + "/" + mat.name + ".mat");
            Transform body = visual.Find("Body");
            if (body == null) throw new System.Exception("[Starlit Tides] Jellyfish prefab has no Body.");
            body.GetComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer bodyRenderer = body.GetComponent<MeshRenderer>();
            bodyRenderer.sharedMaterial = mat;
            bodyRenderer.shadowCastingMode = ShadowCastingMode.Off;
            bodyRenderer.receiveShadows = false;
            // Any other renderer the old prefab had (it shouldn't): gone.
            foreach (MeshRenderer r in visual.GetComponentsInChildren<MeshRenderer>(true))
                if (r != bodyRenderer) Object.DestroyImmediate(r.gameObject);
            j.pulseSpeed = look.pulseSpeed;

            SphereCollider col = go.GetComponent<SphereCollider>();
            if (col != null) { col.radius = colliderRadius; col.center = new Vector3(0f, 0.1f * scaleY, 0f); }

            j.radius = radius; j.lightDamage = damage; j.floatOffset = floatOffset;
            j.bobAmplitude = 0.05f * Mathf.Max(1f, scale); j.followWaves = 0.9f;

            WaterDisturber disturber = go.GetComponent<WaterDisturber>();
            Light glow = go.GetComponentInChildren<Light>(true);
            if (!light)
            {
                // Blooms bring dozens of moon jellies: no point lights or sea-glow points each.
                if (glow != null) Object.DestroyImmediate(glow.gameObject);
                if (disturber != null) Object.DestroyImmediate(disturber);
                j.glowLight = null; j.waterGlow = null;
            }
            else
            {
                if (glow != null) { glow.color = new Color(1f, 0.55f, 0.25f); glow.intensity = 1.8f; glow.range = 6f; glow.transform.localPosition = new Vector3(0f, 0.6f, 0f); j.glowLightBase = 1.8f; }
                if (disturber != null) disturber.radius = 3.2f;
            }

            string path = GameplayPrefabs + "/" + name + ".prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            Object.DestroyImmediate(go);
            return prefab;
        }

        // Moon-jelly body in object units: bell radius 0.6, the rim at y = 0, the dome
        // above, fringe tentacles and four frilly oral arms hanging below.
        // uv.x = part (0 bell, 1 fringe, 2 arm), uv.y = 0..1 (see the shader).
        static Mesh JellyBellMesh(JellyLook look)
        {
            const float R = 0.6f;
            float H = R * look.dome;
            var verts = new List<Vector3>(); var uvs = new List<Vector2>(); var tris = new List<int>();

            // ---- bell: a low saucer with a scalloped rim that curls down a little ----
            int rings = 14, segs = 64;
            for (int i = 0; i <= rings; i++)
            {
                float v = i / (float)rings;                          // 0 centre .. 1 rim
                for (int k = 0; k <= segs; k++)
                {
                    float a = k / (float)segs * Mathf.PI * 2f;
                    float scallop = 1f + 0.045f * Mathf.Sin(a * 16f) * Mathf.SmoothStep(0f, 1f, (v - 0.8f) / 0.2f);
                    float r = v * R * scallop;
                    float y = H * Mathf.Pow(Mathf.Max(0f, 1f - v * v), 0.65f) - (v > 0.9f ? (v - 0.9f) * 10f * 0.06f * R : 0f);
                    verts.Add(new Vector3(Mathf.Cos(a) * r, y, Mathf.Sin(a) * r));
                    uvs.Add(new Vector2(0f, v));
                }
            }
            for (int i = 0; i < rings; i++)
                for (int k = 0; k < segs; k++)
                {
                    int a0 = i * (segs + 1) + k, b0 = a0 + segs + 1;
                    tris.Add(a0); tris.Add(b0); tris.Add(a0 + 1);
                    tris.Add(a0 + 1); tris.Add(b0); tris.Add(b0 + 1);
                }

            // ---- a ribbon hanging from (x, y, z): uv.x = part ----
            void Ribbon(Vector3 top, Vector3 side, Vector3 outward, float length, int parts, float part, float frill)
            {
                int start = verts.Count;
                for (int p = 0; p <= parts; p++)
                {
                    float t = p / (float)parts;
                    Vector3 c = top + Vector3.down * (t * length) + outward * (t * length * 0.12f);
                    float w = 1f - t * 0.6f;
                    Vector3 wobble = frill > 0f ? outward * (Mathf.Sin(t * 18f) * frill) : Vector3.zero;
                    verts.Add(c - side * w + wobble); uvs.Add(new Vector2(part, t));
                    verts.Add(c + side * w - wobble); uvs.Add(new Vector2(part, t));
                }
                for (int p = 0; p < parts; p++)
                {
                    int a0 = start + p * 2;
                    tris.Add(a0); tris.Add(a0 + 2); tris.Add(a0 + 1);
                    tris.Add(a0 + 1); tris.Add(a0 + 2); tris.Add(a0 + 3);
                }
            }

            // fringe tentacles round the rim
            for (int i = 0; i < look.fringe; i++)
            {
                float a = (i + 0.5f) / look.fringe * Mathf.PI * 2f;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 side = new Vector3(-dir.z, 0f, dir.x) * 0.009f;
                float len = look.fringeLength * R * (0.8f + 0.4f * Mathf.Repeat(i * 0.618f, 1f));
                Ribbon(dir * R * 0.98f + Vector3.down * 0.05f * R, side, dir, len, 8, 1f, 0f);
            }
            // four frilly oral arms from the middle
            for (int i = 0; i < 4; i++)
            {
                float a = 0.7854f + i * 1.5708f;
                Vector3 dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
                Vector3 side = new Vector3(-dir.z, 0f, dir.x) * 0.06f;
                Ribbon(dir * R * 0.08f, side, dir, look.armLength * R, 12, 2f, 0.03f);
            }

            var mesh = new Mesh { name = "JellyBell" };
            mesh.indexFormat = verts.Count > 65000 ? UnityEngine.Rendering.IndexFormat.UInt32 : UnityEngine.Rendering.IndexFormat.UInt16;
            mesh.SetVertices(verts); mesh.SetUVs(0, uvs); mesh.SetTriangles(tris, 0);
            mesh.RecalculateNormals();
            float reach = R * (1.3f + Mathf.Max(look.fringeLength, look.armLength));
            mesh.bounds = new Bounds(new Vector3(0f, -reach * 0.4f, 0f), new Vector3(R * 3f, reach * 1.6f + H, R * 3f));   // room for the sway
            mesh.UploadMeshData(true);   // (non-readable: less memory)
            return mesh;
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

                // The wake: foam ribbons (Kelvin V arms, stern band, transverse crests) built
                // each frame by BoatSplash in world space, drawn with StarlitWake.shader.
                var splashGo = new GameObject("Boat Wake");
                splashGo.transform.SetParent(fx.transform, false);
                splashGo.AddComponent<MeshFilter>();
                var wakeRenderer = splashGo.AddComponent<MeshRenderer>();
                wakeRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                wakeRenderer.receiveShadows = false;
                Shader wakeShader = Shader.Find("StarlitTides/Wake");
                if (wakeShader == null) throw new System.Exception("[Starlit Tides] Shader 'StarlitTides/Wake' not found.");
                wakeRenderer.sharedMaterial = SaveAsset(new Material(wakeShader) { name = "M_Wake" }, Gen + "/M_Wake.mat");
                var splash = splashGo.AddComponent<BoatSplash>();
                splash.boat = boat;
                splash.turnSpray = VfxKit.TurnSpray(splashGo.transform);

                // The boat's glowing "trail" (glow circles dropped behind it) made a bright cyan
                // band; the foam ribbons replace it. Keep only a small glow round the hull.
                WaterDisturber hullGlow = boat.GetComponent<WaterDisturber>();
                if (hullGlow != null)
                {
                    hullGlow.leaveTrail = false;
                    hullGlow.radius = 1.4f; hullGlow.strength = 0.45f;
                    EditorUtility.SetDirty(hullGlow);
                }

                // The ocean's own glowing wake band becomes a faint plankton glow under the foam.
                OceanController ocean = Object.FindAnyObjectByType<OceanController>();
                if (ocean != null && ocean.oceanMaterial != null)
                {
                    ocean.oceanMaterial.SetFloat("_TrailStrength", 0.2f);
                    ocean.oceanMaterial.SetFloat("_TrailWidth", 0.55f);
                    EditorUtility.SetDirty(ocean.oceanMaterial);
                }
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
