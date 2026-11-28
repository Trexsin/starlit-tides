// =============================================================================
//  STARLIT TIDES - Jellyfish (Phase 9 redesign)
// -----------------------------------------------------------------------------
//  Players mixed the old jellyfish up with the light orbs: both were small
//  points of pure glow. This one is modelled on a real moon jelly (Aurelia):
//    - a wide, flat, TRANSLUCENT bell you can see the sea through (alpha
//      blended, not additive), so it reads as a body, not a light;
//    - its four horseshoe gonads, the "four-leaf clover" seen from above;
//    - a scalloped rim with a band of bioluminescent spots, and a slow wave of
//      light running round it;
//    - fine fringe tentacles and four frilly oral arms.
//  The bell PULSES like a real jelly: a quick contraction (narrower, taller),
//  then a slow relaxation. Every jelly gets its own phase.
//
//  Mesh layout (Phase9Builder.JellyBellMesh): uv.x = part (0 bell, 1 fringe
//  tentacle, 2 oral arm), uv.y = 0..1 (bell: distance from the centre over the
//  rim radius; tentacles and arms: along their length).
//  Output is premultiplied alpha: rgb = body * alpha + glow.
// =============================================================================
Shader "StarlitTides/JellyBell"
{
    Properties
    {
        _BodyColor ("Body (rgb) and Opacity (a)", Color) = (0.78, 0.74, 1.0, 0.3)
        [HDR] _GonadColor ("Clover / Oral Arms Glow", Color) = (2.4, 0.55, 1.8, 1)
        [HDR] _RimColor ("Rim Lights", Color) = (1.8, 0.8, 2.6, 1)
        [HDR] _TentacleColor ("Tentacles", Color) = (1.2, 0.6, 1.6, 1)
        _PulseSpeed ("Pulse Speed", Float) = 1.6
        _PulseAmount ("Pulse Amount", Range(0, 0.4)) = 0.16
        _SwayAmount ("Tentacle Sway", Range(0, 0.6)) = 0.22
        _GonadRadius ("Clover Distance From Centre", Range(0, 0.8)) = 0.36
        _GonadSize ("Clover Ring Size", Range(0.05, 0.4)) = 0.17
        _Lobes ("Rim Spots", Float) = 32
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            Name "JellyBell"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _BodyColor, _GonadColor, _RimColor, _TentacleColor;
                float _PulseSpeed, _PulseAmount, _SwayAmount, _GonadRadius, _GonadSize, _Lobes;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float3 positionOS : TEXCOORD2;
                float2 uv         : TEXCOORD3;
                float  pulse      : TEXCOORD4;   // 0 relaxed .. 1 fully contracted
                float  phase      : TEXCOORD5;
                float  fogFactor  : TEXCOORD6;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float3 origin = TransformObjectToWorld(float3(0, 0, 0));
                float phase = frac(sin(dot(origin.xz, float2(12.9898, 78.233))) * 43758.5453) * 6.2831;
                float t = _Time.y * _PulseSpeed + phase;
                // Quick contraction, slow relaxation (a sharpened sine).
                float s = 0.5 + 0.5 * sin(t);
                float pulse = s * s * s;

                float3 p = IN.positionOS.xyz;
                float part = IN.uv.x;
                float along = IN.uv.y;
                if (part < 0.5)
                {
                    // Bell: narrower and taller on the contraction, most at the rim.
                    p.xz *= 1.0 - pulse * _PulseAmount * (0.25 + along);
                    p.y  *= 1.0 + pulse * _PulseAmount * 0.9;
                }
                else
                {
                    // Tentacles and arms hang from the bell: follow its rim in, then sway
                    // in slow travelling waves, more toward the tips.
                    float attach = part < 1.5 ? 1.0 : 0.25;
                    p.xz *= 1.0 - pulse * _PulseAmount * (0.25 + attach) * (1.0 - along * 0.6);
                    float a = atan2(IN.positionOS.z, IN.positionOS.x);
                    float wave = t * 0.6 - along * 3.2 + a * 2.0;
                    float reach = _SwayAmount * along * (part < 1.5 ? 0.35 : 0.6);
                    p.x += sin(wave) * reach;
                    p.z += cos(wave * 0.8 + 1.3) * reach;
                    p.y += pulse * 0.04 * along;   // the tips lag behind the bell
                }

                OUT.positionOS = IN.positionOS.xyz;
                OUT.uv = IN.uv;
                OUT.positionWS = TransformObjectToWorld(p);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.pulse = pulse;
                OUT.phase = phase;
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float3 N = normalize(IN.normalWS);
                float3 V = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float fresnel = pow(saturate(1.0 - abs(dot(N, V))), 2.0);
                float part = IN.uv.x;
                float along = saturate(IN.uv.y);

                float alpha;
                float3 glow;
                if (part < 0.5)
                {
                    // ---- the bell ----
                    float angle = atan2(IN.positionOS.z, IN.positionOS.x);
                    float2 q = float2(cos(angle), sin(angle)) * along;   // flat map of the bell, rim = 1

                    // Four horseshoe gonads (the clover), each open toward the centre.
                    float clover = 0.0;
                    [unroll] for (int k = 0; k < 4; k++)
                    {
                        float ca = 0.7854 + k * 1.5708;
                        float2 c = float2(cos(ca), sin(ca)) * _GonadRadius;
                        float2 d = q - c;
                        float ring = 1.0 - saturate(abs(length(d) - _GonadSize) / (_GonadSize * 0.38));
                        float open = saturate(0.6 + dot(normalize(d + 1e-5), normalize(c)) * 0.8);   // gap facing the middle
                        clover += ring * ring * open;
                    }

                    // Fine radial canals.
                    float canals = pow(0.5 + 0.5 * cos(angle * 16.0), 24.0) * smoothstep(0.15, 0.9, along) * 0.35;

                    // Rim lights: a band of spots, with a wave of light running round.
                    float band = smoothstep(0.84, 0.95, along) * (1.0 - smoothstep(1.0, 1.06, along));
                    float spots = pow(0.5 + 0.5 * cos(angle * _Lobes), 6.0);
                    float runner = 0.35 + 0.65 * pow(0.5 + 0.5 * sin(angle - _Time.y * 2.2 - IN.phase), 4.0);

                    alpha = _BodyColor.a * (0.55 + 0.9 * fresnel + 0.25 * band);
                    glow = _GonadColor.rgb * clover * (0.8 + 0.5 * IN.pulse)
                         + _RimColor.rgb * (band * spots * runner * 1.4 + fresnel * 0.18 + canals * 0.5);
                    alpha = saturate(alpha + clover * 0.25);
                }
                else if (part < 1.5)
                {
                    // ---- fringe tentacles: faint threads fading out ----
                    float fade = 1.0 - along;
                    alpha = 0.35 * fade;
                    glow = _TentacleColor.rgb * fade * 0.35;
                }
                else
                {
                    // ---- oral arms: frilly, softly lit by the clover's colour ----
                    float fade = 1.0 - along * 0.8;
                    alpha = 0.32 * fade;
                    glow = _GonadColor.rgb * 0.22 * fade + _TentacleColor.rgb * 0.08;
                }

                float3 col = _BodyColor.rgb * alpha * 0.55 + glow;
                // Fog: fade toward the fog colour (premultiplied).
                float fogI = ComputeFogIntensity(IN.fogFactor);
                col = lerp(unity_FogColor.rgb * alpha, col, fogI);
                return half4(col, alpha);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
