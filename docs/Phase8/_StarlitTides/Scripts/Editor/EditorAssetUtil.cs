// =============================================================================
//  EditorAssetUtil - reliable texture imports for the builders, plus a check
//  that Rebuild Everything runs at the end.
// -----------------------------------------------------------------------------
//  The builders write PNGs from code and use them straight away in materials.
//  In batch mode Unity imports synchronously, but in a normal (open) editor,
//  Unity 6 can hand the import to its background import workers. Then
//  LoadAssetAtPath returns null for a moment, and the material was saved with
//  NO texture: glow halos turned into white squares and the colour LUT
//  vanished (found after a live-editor rebuild). These helpers force a
//  synchronous import and stop with an error instead of silently saving null.
// =============================================================================
#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace StarlitTides.EditorTools
{
    public static class EditorAssetUtil
    {
        const ImportAssetOptions Sync = ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate;

        /// <summary>Imports a PNG written from code, applies importer settings, and returns the loaded texture (never null).</summary>
        public static Texture2D ImportTexture(string path, System.Action<TextureImporter> configure = null)
        {
            AssetDatabase.ImportAsset(path, Sync);
            var imp = AssetImporter.GetAtPath(path) as TextureImporter;
            if (imp == null) throw new System.Exception("[Starlit Tides] No texture importer for " + path);
            if (configure != null)
            {
                configure(imp);
                EditorUtility.SetDirty(imp);
                imp.SaveAndReimport();
                AssetDatabase.ImportAsset(path, Sync);
            }
            return LoadTexture(path);
        }

        /// <summary>Loads a texture, forcing a synchronous import if it isn't ready yet. Throws if it can't.</summary>
        public static Texture2D LoadTexture(string path)
        {
            var tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (tex == null)
            {
                AssetDatabase.ImportAsset(path, Sync);
                tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            if (tex == null)
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                tex = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            }
            if (tex == null) throw new System.Exception("[Starlit Tides] Texture did not import: " + path + ". Run Rebuild Everything again.");
            return tex;
        }

        /// <summary>
        /// Writes a material to disk right away. In an open editor a later synchronous
        /// import can reload a just-created material from disk, dropping changes that
        /// were only in memory (that is how two glow materials lost their textures).
        /// </summary>
        public static void Persist(Material m)
        {
            if (m == null) return;
            EditorUtility.SetDirty(m);
            AssetDatabase.SaveAssetIfDirty(m);
        }

        /// <summary>
        /// After a rebuild: every glow / VFX material needs its sprite, and the night
        /// grade needs its LUT. Logs an error per problem; returns the count.
        /// </summary>
        public static int CheckGeneratedAssets()
        {
            var problems = new List<string>();
            foreach (string guid in AssetDatabase.FindAssets("t:Material", new[] { "Assets/_StarlitTides/Generated" }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var m = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (m == null || m.shader == null) { problems.Add(path + ": no shader"); continue; }
                string s = m.shader.name;
                if ((s == "StarlitTides/GlowParticle" || s == "StarlitTides/Vfx") && m.GetTexture("_MainTex") == null)
                    problems.Add(path + ": glow/VFX material has no sprite texture (would draw as a square)");
            }
            var night = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/_StarlitTides/Generated/Settings/PP_StarlitNight.asset");
            if (night != null && night.TryGet(out ColorLookup lut) && lut.active && lut.texture.value == null)
                problems.Add("PP_StarlitNight: the colour-grading LUT texture is missing");

            foreach (string p in problems) Debug.LogError("[Starlit Tides] Asset check: " + p);
            Debug.Log("[Starlit Tides] Asset check: " + (problems.Count == 0 ? "all generated materials have their textures." : problems.Count + " problem(s), see errors above."));
            return problems.Count;
        }
    }
}
#endif
