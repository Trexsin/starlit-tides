// =============================================================================
//  StylizedMaterials - one way to make materials for the "Moonlit Storybook" look
// -----------------------------------------------------------------------------
//  Phase 8, Step 2. Every builder used to make its own "Universal Render
//  Pipeline/Lit" materials. They now all come through here and use
//  StarlitTides/Stylized (Shaders/StarlitStylized.shader) instead.
//
//  The property names are the same as URP Lit (_BaseColor, _BaseMap,
//  _Smoothness, _EmissionColor), so the builders' colours carry over 1:1.
//
//  Kinds of material, each with its own defaults:
//    Prop      houses, pier, boat, buoys, obstacles. Outline on.
//    Detail    small or thin parts (windows, lantern metal, cloth awnings):
//              the prop look without an outline, which would swamp them.
//    Natural   terrain rocks, logs. Outline on, more paint noise.
//    Foliage   leaves and bark. No outline (hundreds of trees would turn into
//              black blobs), a strong dark-to-light height gradient.
//    Distant   mountains. No outline, a tall gradient, rim on the ridges.
//    Glow      lamps, windows, orbs. No outline and almost no rim: the
//              emission is the point.
// =============================================================================
#if UNITY_EDITOR
using UnityEngine;

namespace StarlitTides.EditorTools
{
    public static class StylizedMaterials
    {
        public const string ShaderName = "StarlitTides/Stylized";
        const string OutlinePass = "SRPDefaultUnlit";

        public enum Kind { Prop, Detail, Natural, Foliage, Distant, Glow }

        /// <summary>Makes (but does not save) a stylized material.</summary>
        public static Material Create(string name, Color color, float smoothness, Kind kind,
                                      Color emission = default, Texture tex = null, bool instancing = true)
        {
            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogWarning("[Starlit Tides] Shader '" + ShaderName + "' not found (copy Phase 8 Shaders). Falling back to URP Lit for " + name);
                shader = Shader.Find("Universal Render Pipeline/Lit");
            }

            var m = new Material(shader) { name = name };
            m.SetColor("_BaseColor", color);
            m.SetFloat("_Smoothness", smoothness);
            m.SetFloat("_Metallic", 0f);
            if (tex != null) m.SetTexture("_BaseMap", tex);
            if (emission != default)
            {
                m.EnableKeyword("_EMISSION");   // only matters for the URP Lit fallback
                m.SetColor("_EmissionColor", emission);
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            }
            m.enableInstancing = instancing;
            ApplyKind(m, kind);
            return m;
        }

        /// <summary>Turns on the vertex-colour accent (red channel = mask): snow caps, moss tops.</summary>
        public static void SetAccent(Material m, Color accent)
        {
            m.EnableKeyword("_VERTEX_ACCENT");
            m.SetFloat("_VertexAccent", 1f);
            m.SetColor("_AccentColor", accent);
            UnityEditor.EditorUtility.SetDirty(m);   // the material may already be saved as an asset
        }

        public static void ApplyKind(Material m, Kind kind)
        {
            bool outline = kind == Kind.Prop || kind == Kind.Natural;
            m.SetShaderPassEnabled(OutlinePass, outline);

            switch (kind)
            {
                case Kind.Prop:
                case Kind.Detail:
                    m.SetFloat("_PaintStrength", 0.07f);
                    m.SetFloat("_GradientStrength", 0.22f);
                    m.SetFloat("_GradientHeight", 4f);
                    break;
                case Kind.Natural:
                    m.SetFloat("_PaintStrength", 0.14f);
                    m.SetFloat("_PaintScale", 0.9f);
                    m.SetFloat("_GradientStrength", 0.3f);
                    m.SetFloat("_GradientHeight", 2f);
                    m.SetFloat("_RimStrength", 1.5f);
                    break;
                case Kind.Foliage:
                    m.SetFloat("_PaintStrength", 0.12f);
                    m.SetFloat("_PaintScale", 0.5f);
                    m.SetFloat("_GradientStrength", 0.45f);
                    m.SetFloat("_GradientHeight", 9f);
                    m.SetFloat("_Wrap", 0.5f);            // leaves let light through: softer terminator
                    m.SetFloat("_RimStrength", 0.9f);
                    break;
                case Kind.Distant:
                    m.SetFloat("_PaintStrength", 0.05f);
                    m.SetFloat("_PaintScale", 0.02f);
                    m.SetFloat("_GradientStrength", 0.45f);
                    m.SetFloat("_GradientHeight", 70f);
                    m.SetFloat("_RimStrength", 0.45f);
                    m.SetFloat("_RimWidth", 0.22f);
                    // far ranges should recede into the night, not glow
                    m.SetFloat("_AmbientStrength", 0.75f);
                    m.SetFloat("_MidTone", 0.42f);
                    m.SetColor("_ShadowTint", new Color(0.06f, 0.05f, 0.13f));
                    break;
                case Kind.Glow:
                    m.SetFloat("_PaintStrength", 0f);
                    m.SetFloat("_GradientStrength", 0f);
                    m.SetFloat("_RimStrength", 0.2f);
                    break;
            }
        }
    }
}
#endif
