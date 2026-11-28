// StarlitWake: the foam of the boat's wake (Phase 9, used by BoatSplash).
//
// BoatSplash builds ribbons lying on the water: the two arms of the Kelvin V,
// the churned band behind the stern, and curved crests inside the V. This
// shader turns each ribbon into broken white foam:
//   - soft edges across the ribbon (uv.y: 0 one edge, 1 the other)
//   - noise along it (uv.x is metres along the wake, so the pattern stays put
//     on the water instead of swimming)
//   - the foam breaks up as it ages (uv2.x = age 0..1), like real wake foam
//   - a faint glow of stirred-up plankton underneath (this is a night sea)
// Premultiplied alpha: white foam covers the water, the glow adds light.

Shader "StarlitTides/Wake"
{
    Properties
    {
        _FoamColor  ("Foam Colour", Color) = (0.86, 0.95, 1.0, 1)
        [HDR] _GlowColor ("Plankton Glow (HDR)", Color) = (0.2, 1.1, 1.4, 1)
        _NoiseScale ("Foam Noise Scale (per metre)", Float) = 0.9
        _Glow       ("Glow Amount", Range(0, 2)) = 0.2
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+5" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Wake"
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "StarlitNoise.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _FoamColor, _GlowColor;
                float _NoiseScale, _Glow;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; };
            struct Varyings { float4 positionCS : SV_POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; float2 uv2 : TEXCOORD1; float fog : TEXCOORD2; };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.color = IN.color;
                OUT.uv = IN.uv;
                OUT.uv2 = IN.uv2;
                OUT.fog = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float across = abs(IN.uv.y * 2.0 - 1.0);          // 0 in the middle, 1 at the edges
                float age = saturate(IN.uv2.x);
                float t = _Time.y;
                float seed = IN.uv2.y;
                // Big patches, long streaks running along the ribbon, and fine bubbles.
                float n       = SN_Fbm2(float2(IN.uv.x * _NoiseScale, IN.uv.y * 1.7 + seed) + float2(0.0, t * 0.12), 3);
                float streak  = SN_Noise2(float2(IN.uv.x * _NoiseScale * 0.35, IN.uv.y * 9.0 + seed * 3.0));
                float bubbles = SN_Noise2(float2(IN.uv.x * _NoiseScale * 4.0, IN.uv.y * 6.0 + seed) - t * 0.4);
                // Frayed edges: the noise pushes the edge in and out.
                float edge = across + (n - 0.5) * 0.6;
                float body = 1.0 - smoothstep(0.3, 0.92, edge);
                // Fresh foam is dense; it thins into lacy streaks and then breaks up.
                float threshold = lerp(0.4, 0.78, age);
                float shape = n * 0.5 + streak * 0.32 + bubbles * 0.18 + (1.0 - across) * 0.2;
                float foam = smoothstep(threshold, threshold + 0.12, shape) * body;
                float a = saturate(foam * IN.color.a);
                float glow = body * IN.color.a * _Glow * (1.0 - age * 0.7) * (0.6 + 0.4 * n);
                float3 rgb = _FoamColor.rgb * a + _GlowColor.rgb * glow * 0.35;
                rgb = MixFogColor(rgb, unity_FogColor.rgb * a, IN.fog);   // (premultiplied: fog scales with coverage)
                return half4(rgb, a);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
