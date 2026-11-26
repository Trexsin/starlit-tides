// =============================================================================
//  STARLIT TIDES - Bioluminescent jellyfish
// -----------------------------------------------------------------------------
//  Additive "light only" shader: the jellyfish is made of glow, like a real
//  bioluminescent jelly at night.
//    VERTEX   - the bell PULSES (widens and flattens, then relaxes) and the
//               tentacles SWAY in slow waves. Each jellyfish gets its own
//               phase from its world position, so they never move in sync.
//               Bell = object-space y >= 0, tentacles = y < 0.
//    FRAGMENT - bright Fresnel rim (edges glow most, like a soap bubble),
//               radial "veins" on the bell, brighter on each pulse.
// =============================================================================
Shader "StarlitTides/Jellyfish"
{
    Properties
    {
        [HDR] _BellColor ("Bell Colour", Color) = (1.4, 0.35, 1.6, 1)
        [HDR] _TentacleColor ("Tentacle Colour", Color) = (0.5, 0.7, 2.2, 1)
        _PulseSpeed ("Pulse Speed", Float) = 2.4
        _PulseAmount ("Pulse Amount", Range(0, 0.4)) = 0.14
        _SwayAmount ("Tentacle Sway", Range(0, 0.5)) = 0.18
        _RimPower ("Rim Power", Range(0.5, 6)) = 2
        _Veins ("Vein Count", Float) = 8
        _BellHeight ("Bell Height (object units)", Float) = 0.8
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "JellyGlow"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BellColor, _TentacleColor;
                float _PulseSpeed, _PulseAmount, _SwayAmount, _RimPower, _Veins, _BellHeight;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                float  pulse      : TEXCOORD3;
                float  fogFactor  : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                // per-jellyfish phase from its world position
                float3 origin = TransformObjectToWorld(float3(0, 0, 0));
                float phase = frac(sin(dot(origin.xz, float2(12.9898, 78.233))) * 43758.5453) * 6.2831;
                float t = _Time.y * _PulseSpeed + phase;
                float pulse = sin(t);

                float3 p = IN.positionOS.xyz;
                if (p.y >= 0)
                {
                    // bell: wider + flatter on the pulse, strongest near the rim
                    float rim = 1.0 - saturate(p.y / _BellHeight);
                    p.xz *= 1.0 + pulse * _PulseAmount * (0.4 + rim);
                    p.y  *= 1.0 - pulse * _PulseAmount * 0.6;
                }
                else
                {
                    // tentacles: travelling waves, more movement toward the tips
                    float depth = -p.y;
                    p.x += sin(t * 0.7 + p.y * 2.6) * _SwayAmount * depth;
                    p.z += cos(t * 0.55 + p.y * 2.1) * _SwayAmount * depth * 0.8;
                    p.y *= 1.0 + pulse * 0.06;
                }

                OUT.positionOS = IN.positionOS.xyz;
                OUT.positionWS = TransformObjectToWorld(p);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.pulse = pulse;
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);
                float3 V = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float rim = pow(1.0 - abs(dot(N, V)), _RimPower);
                float glow = 0.75 + 0.35 * IN.pulse;

                float3 col;
                if (IN.positionOS.y >= 0)
                {
                    float angle = atan2(IN.positionOS.z, IN.positionOS.x);
                    float veins = pow(0.5 + 0.5 * cos(angle * _Veins), 6.0);
                    float top = saturate(IN.positionOS.y / _BellHeight);
                    col = _BellColor.rgb * (rim * 1.6 + 0.12 + veins * 0.35 * (1.0 - top) + top * 0.25) * glow;
                }
                else
                {
                    float fade = saturate(1.0 + IN.positionOS.y * 0.55);   // tips fade out
                    col = _TentacleColor.rgb * (0.35 + rim * 0.6) * fade * glow;
                }
                col *= ComputeFogIntensity(IN.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
