// STARLIT TIDES - Bioluminescent whale
//
// A dark whale covered in glowing plankton spots.
// VERTEX:   a gentle swimming wave runs along the body, strongest at the tail
//           (uv.y = 0 at the tail, 1 at the head).
// FRAGMENT: dark navy back, lighter belly with glowing grooves, twinkling
//           spots (one random dot per cell, like the ocean's plankton), and a
//           cyan Fresnel rim so the silhouette glows against the night.

Shader "StarlitTides/Whale"
{
    Properties
    {
        _BodyColor ("Back Colour", Color) = (0.03, 0.05, 0.12, 1)
        _BellyColor ("Belly Colour", Color) = (0.12, 0.16, 0.26, 1)
        [HDR] _RimColor ("Rim Glow", Color) = (0.3, 2.0, 2.6, 1)
        [HDR] _SpotColor ("Spot Glow", Color) = (0.4, 2.6, 3.4, 1)
        [HDR] _GrooveColor ("Belly Groove Glow", Color) = (0.2, 1.2, 1.6, 1)
        _SpotScale ("Spot Scale", Float) = 10
        _SpotDensity ("Spot Density", Range(0, 1)) = 0.4
        _SwimAmount ("Swim Wave", Range(0, 1)) = 0.35
    }

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "WhaleForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "StarlitNoise.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BodyColor, _BellyColor, _RimColor, _SpotColor, _GrooveColor;
                float _SpotScale, _SpotDensity, _SwimAmount;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float belly : TEXCOORD3;
                float fogFactor : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                // Swimming: a sideways wave travels from head to tail, bigger toward the tail.
                float3 p = IN.positionOS.xyz;
                float tail = 1.0 - IN.uv.y;
                p.x += sin(_Time.y * 2.2 + p.z * 0.35) * _SwimAmount * tail * tail;

                OUT.positionWS = TransformObjectToWorld(p);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.uv = IN.uv;
                OUT.belly = saturate(-IN.normalOS.y * 1.5);   // 1 on the underside
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);
                float3 V = normalize(GetWorldSpaceViewDir(IN.positionWS));

                // Simple lighting: moonlight (wrapped so the dark side isn't pitch black) + ambient.
                Light moon = GetMainLight();
                float wrap = saturate(dot(N, moon.direction) * 0.5 + 0.5);
                float3 lighting = moon.color * wrap * 0.6 + SampleSH(N);
                float3 baseColor = lerp(_BodyColor.rgb, _BellyColor.rgb, IN.belly);
                float3 color = baseColor * lighting;

                // Glowing spots: one random dot per cell on the skin, twinkling slowly.
                float2 cellUV = IN.uv * float2(_SpotScale * 2.0, _SpotScale * 5.0);
                float2 cell = floor(cellUV);
                float3 h = SN_Hash32(cell);
                float spot = 0;
                if (h.z < _SpotDensity)
                {
                    float d = length(frac(cellUV) - (0.25 + 0.5 * h.xy));
                    float twinkle = 0.6 + 0.4 * sin(_Time.y * (1.0 + h.x * 2.0) + h.y * 30.0);
                    spot = saturate(1.0 - d / 0.22) * twinkle;
                }
                color += _SpotColor.rgb * spot * spot * (1.0 - IN.belly * 0.5);

                // Belly grooves: thin glowing lines along the underside.
                float grooves = pow(abs(sin(IN.uv.x * 70.0)), 24.0) * IN.belly;
                color += _GrooveColor.rgb * grooves;

                // Fresnel rim: edges glow cyan.
                float rim = pow(1.0 - saturate(dot(N, V)), 3.0);
                color += _RimColor.rgb * rim;

                color = MixFog(color, IN.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
