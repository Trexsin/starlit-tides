// STARLIT TIDES - Glowing wave crest (the whale's waves)
//
// An additive (light-only) shader for the curved crest mesh.
// uv.x goes across the wave (0..1), uv.y goes over the top of it (0..1).
// It's brightest along the top, fades at the ends, and foam noise
// scrolls over it so the wave looks like it's breaking.

Shader "StarlitTides/WaveCrest"
{
    Properties
    {
        [HDR] _Color ("Glow Colour", Color) = (0.15, 1.0, 1.4, 1)
        [HDR] _FoamColor ("Foam Colour", Color) = (0.6, 1.0, 1.2, 1)
        _FoamSpeed ("Foam Speed", Float) = 1.5
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Crest"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "StarlitNoise.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color, _FoamColor;
                float _FoamSpeed;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float fogFactor : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.uv = IN.uv;
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float top = sin(IN.uv.y * PI);                       // 1 at the top of the crest
                float ends = saturate(min(IN.uv.x, 1.0 - IN.uv.x) * 6.0);   // fade at both ends
                float foam = SN_Fbm2(float2(IN.positionWS.x * 0.8, IN.uv.y * 3.0 - _Time.y * _FoamSpeed), 3);

                float glow = top * top * top * top * ends;   // a thin bright line along the crest
                float3 col = _Color.rgb * glow * (0.5 + foam);
                col += _FoamColor.rgb * smoothstep(0.6, 0.85, foam * top) * top * ends;
                col *= ComputeFogIntensity(IN.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
