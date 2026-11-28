// ShaderWarmup: draws everything once while the menu is showing (Phase 9).
//
// On a Mac (Metal) the graphics driver turns a shader into GPU code the first
// time it is drawn with a given setup. That can take 50-100 ms: a visible hitch
// in the middle of play when the first rock, orb, splash or creature appears
// (measured: 100, 83 and 50 ms hitches early in a run on a MacBook Air).
//
// So, shortly after the scene loads (while the menu is up, and after the
// first-launch auto-detect), this collects every prefab the scene's scripts
// refer to, makes render-only copies of them (all scripts stripped, so nothing
// moves, spawns or scores), draws them a few times with a hidden copy of the
// game camera, and throws the copies away. The driver caches the result, so
// later launches are hitch-free too.

using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace StarlitTides
{
    public class ShaderWarmup : MonoBehaviour
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        static void Boot()
        {
            var go = new GameObject("Shader Warmup");
            DontDestroyOnLoad(go);   // (the render check keeps watching after a Retry)
            go.AddComponent<ShaderWarmup>();
        }

        IEnumerator Start()
        {
            yield return new WaitForSecondsRealtime(1.5f);
            while (DisplayManager.Instance != null && DisplayManager.Instance.Detecting) yield return new WaitForSecondsRealtime(0.5f);
            Camera main = Camera.main;
            if (main == null) { Destroy(gameObject); yield break; }
            float start = Time.realtimeSinceStartup;

            // Every prefab any script in the scene points at (spawners, VFX, encounters...).
            var prefabs = new HashSet<GameObject>();
            foreach (MonoBehaviour mb in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include))
            {
                if (mb == null) continue;
                foreach (FieldInfo f in mb.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance))
                {
                    if (f.FieldType == typeof(GameObject)) Add(prefabs, f.GetValue(mb) as GameObject);
                    else if (f.FieldType == typeof(GameObject[]) && f.GetValue(mb) is GameObject[] arr) foreach (GameObject g in arr) Add(prefabs, g);
                    else if (f.FieldType == typeof(AmbientSeaField.Band[]) && f.GetValue(mb) is AmbientSeaField.Band[] bands) AddBands(prefabs, bands);
                    else if (f.FieldType == typeof(AmbientSeaField.ScenerySet[]) && f.GetValue(mb) is AmbientSeaField.ScenerySet[] sets)
                        foreach (var s in sets) if (s != null) AddBands(prefabs, s.bands);
                }
            }

            // Render-only copies, laid out in a grid in front of a hidden camera far below the sea.
            Vector3 origin = new Vector3(0f, -600f, 0f);
            var holder = new GameObject("Warmup Copies");
            holder.SetActive(false);   // so no Awake/OnEnable runs while we strip the scripts
            holder.transform.position = origin;
            int i = 0;
            foreach (GameObject prefab in prefabs)
            {
                GameObject copy = Instantiate(prefab, holder.transform);
                StripScripts(copy);
                int col = i % 8, row = i / 8;
                copy.transform.localPosition = new Vector3((col - 3.5f) * 9f, (row - 3f) * 7f, 70f);
                i++;
            }
            holder.SetActive(true);
            foreach (ParticleSystem ps in holder.GetComponentsInChildren<ParticleSystem>(true))
            {
                ps.Simulate(0.3f, true, true);   // have particles on screen to draw
                ps.Play(true);
            }

            var camGo = new GameObject("Warmup Camera");
            Camera cam = camGo.AddComponent<Camera>();
            cam.CopyFrom(main);
            var srcData = main.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            var data = camGo.AddComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>();
            if (srcData != null)
            {
                data.renderPostProcessing = srcData.renderPostProcessing;
                data.antialiasing = srcData.antialiasing;
                data.requiresDepthTexture = true;
                data.requiresColorTexture = true;
            }
            cam.transform.SetPositionAndRotation(origin, Quaternion.identity);
            cam.fieldOfView = 90f;
            cam.farClipPlane = 400f;
            var rt = new RenderTexture(640, 360, 24, RenderTextureFormat.DefaultHDR);
            cam.targetTexture = rt;
            cam.enabled = false;
            for (int pass = 0; pass < 3; pass++)
            {
                cam.Render();
                yield return null;
            }

            cam.targetTexture = null;
            rt.Release();
            Destroy(rt);
            Destroy(camGo);
            Destroy(holder);
            Debug.Log("[Starlit Tides] Shader warm-up: drew " + i + " prefabs in " + ((Time.realtimeSinceStartup - start) * 1000f).ToString("0") + " ms.");

            // Then keep watch: every few seconds, check that everything floating in the sea
            // still has a working material and shader, and log the first problem of each
            // kind (a broken material draws as a magenta shape and logs nothing by itself).
            var reported = new HashSet<string>();
            while (true)
            {
                yield return new WaitForSecondsRealtime(4f);
                foreach (DriftingObject d in DriftingObject.Active)
                {
                    if (d == null) continue;
                    foreach (Renderer r in d.GetComponentsInChildren<Renderer>())
                    {
                        if (!r.enabled) continue;
                        Material m = r.sharedMaterial;
                        string problem = m == null ? "no material" : m.shader == null ? "no shader" : !m.shader.isSupported ? "shader not supported (" + m.shader.name + ")" : null;
                        if (problem == null) continue;
                        string key = d.name + "/" + r.name + ":" + problem;
                        if (reported.Add(key)) Debug.LogWarning("[Starlit Tides] Render check: " + key + (m != null ? " material " + m.name : ""));
                    }
                }
            }
        }

        static void Add(HashSet<GameObject> set, GameObject g)
        {
            // Prefab assets only (scene objects have a valid scene).
            if (g != null && !g.scene.IsValid()) set.Add(g);
        }

        static void AddBands(HashSet<GameObject> set, AmbientSeaField.Band[] bands)
        {
            if (bands == null) return;
            foreach (var b in bands) if (b != null && b.prefabs != null) foreach (GameObject g in b.prefabs) Add(set, g);
        }

        // Removes every script (and collider/light that could touch the game), leaving renderers.
        static void StripScripts(GameObject go)
        {
            for (int pass = 0; pass < 3; pass++)
                foreach (MonoBehaviour mb in go.GetComponentsInChildren<MonoBehaviour>(true))
                    if (mb != null) DestroyImmediate(mb);
            foreach (Collider c in go.GetComponentsInChildren<Collider>(true)) DestroyImmediate(c);
            foreach (Rigidbody r in go.GetComponentsInChildren<Rigidbody>(true)) DestroyImmediate(r);
            foreach (AudioSource a in go.GetComponentsInChildren<AudioSource>(true)) DestroyImmediate(a);
        }
    }
}
