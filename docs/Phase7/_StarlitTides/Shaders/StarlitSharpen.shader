// STARLIT TIDES - Contrast-adaptive sharpening (full-screen pass)
//
// Runs after post-processing (URP "Full Screen Pass" renderer feature).
// Bloom, anti-aliasing and the soft night grade all blur fine detail a little;
// this pass gives it back. It works like AMD's CAS:
//   - look at the pixel and its 4 neighbours
//   - the less local contrast there is, the more it sharpens
//     (flat areas get crisper, already-sharp edges don't ring or halo)
// Strength comes from the global _StarlitSharpness (SettingsManager sets it
// per quality level). 0 = off.

Shader "Hidden/StarlitTides/Sharpen"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off Cull Off ZTest Always Blend Off

        Pass
        {
            Name "StarlitSharpen"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float _StarlitSharpness;

            half3 Load(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_PointClamp, uv).rgb;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half3 c = Load(uv);
                if (_StarlitSharpness <= 0.001) return half4(c, 1);

                float2 t = _BlitTexture_TexelSize.xy;
                half3 n = Load(uv + float2(0, t.y));
                half3 s = Load(uv - float2(0, t.y));
                half3 e = Load(uv + float2(t.x, 0));
                half3 w = Load(uv - float2(t.x, 0));

                // Local contrast -> how much to sharpen (CAS "amp").
                half3 mn = min(c, min(min(n, s), min(e, w)));
                half3 mx = max(c, max(max(n, s), max(e, w)));
                half3 amp = sqrt(saturate(min(mn, 1.0 - mx) / max(mx, 1e-4)));

                // Negative-lobe weight: -1/8 (soft) .. -1/5 (strong).
                half peak = -1.0 / lerp(8.0, 5.0, saturate(_StarlitSharpness));
                half3 wgt = amp * peak;
                half3 result = (c + (n + s + e + w) * wgt) / (1.0 + 4.0 * wgt);
                return half4(saturate(result), 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
