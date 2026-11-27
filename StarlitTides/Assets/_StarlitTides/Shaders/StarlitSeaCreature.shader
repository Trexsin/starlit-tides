// STARLIT TIDES - Bioluminescent sea creature (manta ray, sea turtle, dolphin)
//
// One shader for all the new sea-creature obstacles. Each creature's mesh
// stores animation masks in its VERTEX COLOURS, so the same code flaps a
// manta's wings, paddles a turtle's flippers and beats a dolphin's tail:
//   R = fin weight   (0 on the body, 1 at a wing/flipper tip)
//   G = body wave    (0 at the head, 1 at the tail)
//   B = glow mask    (where the bioluminescent pattern may appear)
//   A = fin phase    (0..1, so front and back flippers can alternate)
//
// VERTEX:   fin stroke (up/down + forward/back sweep, lagging toward the tip
//           so wings ripple) and a travelling body wave along _SwimAxis.
// FRAGMENT: wet skin = wrapped moonlight + ambient + lantern/boat lights
//           (Forward+ light loop) + a sharp specular highlight, then glowing
//           patterns: twinkling spots, shell-scute seams (Voronoi), flank
//           stripes with a travelling pulse, wing-edge glow and a Fresnel rim.
// Also has DepthOnly / DepthNormals passes so the ocean refraction, depth
// colour and SSAO all see the animated shape.

Shader "StarlitTides/SeaCreature"
{
    Properties
    {
        [Header(Skin)]
        _BackColor ("Back Colour", Color) = (0.03, 0.05, 0.1, 1)
        _BellyColor ("Belly Colour", Color) = (0.55, 0.62, 0.7, 1)
        _Gloss ("Wet Highlight Sharpness", Range(8, 512)) = 96
        _SpecStrength ("Wet Highlight Strength", Range(0, 4)) = 1.2
        _Wrap ("Light Wrap", Range(0, 1)) = 0.5
        _BellyLow ("Belly Edge Start", Range(-1, 1)) = 0
        _BellyHigh ("Belly Edge End", Range(-1, 1)) = 0.7

        [Header(Stylized)]
        _Toon ("Toon Banding", Range(0, 1)) = 0.6
        _ToonSteps ("Toon Steps", Range(2, 6)) = 3
        _Iridescence ("Iridescent Sheen", Range(0, 2)) = 0.5
        _IridScale ("Iridescence Hue Spread", Range(0.2, 4)) = 1.4

        [Header(Glow)]
        [HDR] _GlowColor ("Glow Colour", Color) = (0.3, 2.4, 3.2, 1)
        [HDR] _RimColor ("Rim Colour", Color) = (0.2, 1.4, 2.0, 1)
        _RimPower ("Rim Power", Range(1, 8)) = 3
        _SpotStrength ("Spots", Range(0, 4)) = 1
        _SpotScale ("Spot Cells (u, v)", Vector) = (10, 14, 0, 0)
        _SpotDensity ("Spot Density", Range(0, 1)) = 0.45
        _SeamStrength ("Scute Seams", Range(0, 4)) = 0
        _SeamScale ("Seam Cells", Float) = 5
        _StripeStrength ("Flank Stripes", Range(0, 4)) = 0
        _StripeWidth ("Stripe Width", Range(0.005, 0.2)) = 0.035
        _EdgeGlow ("Wing Edge Glow", Range(0, 4)) = 0
        _PulseSpeed ("Glow Pulse Speed", Float) = 1.6

        [Header(Animation)]
        _FinSpeed ("Fin Speed", Float) = 2.4
        _FinLift ("Fin Up-Down", Float) = 0.5
        _FinSweep ("Fin Forward-Back", Float) = 0.1
        _FinLag ("Fin Lag Toward Tip", Float) = 0.8
        _SwimSpeed ("Body Wave Speed", Float) = 6
        _SwimFreq ("Body Wave Frequency", Float) = 2
        _SwimAmount ("Body Wave Amount", Float) = 0.1
        _SwimAxis ("Body Wave Axis", Vector) = (1, 0, 0, 0)
        _AnimTime ("Animation Time Offset", Float) = 0
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float4 _BackColor, _BellyColor, _GlowColor, _RimColor;
        float _Gloss, _SpecStrength, _Wrap, _RimPower, _BellyLow, _BellyHigh;
        float _Toon, _ToonSteps, _Iridescence, _IridScale;
        float _SpotStrength, _SpotDensity, _SeamStrength, _SeamScale;
        float _StripeStrength, _StripeWidth, _EdgeGlow, _PulseSpeed;
        float4 _SpotScale, _SwimAxis;
        float _FinSpeed, _FinLift, _FinSweep, _FinLag, _SwimSpeed, _SwimFreq, _SwimAmount, _AnimTime;
    CBUFFER_END

    // Shared by every pass so the depth/normals match the visible shape.
    float3 AnimateCreature(float3 p, float4 mask)
    {
        float t = _Time.y + _AnimTime;
        // Fin stroke: a loop (lift + sweep) that lags further out toward the tip.
        float phase = t * _FinSpeed - abs(p.x) * _FinLag + mask.a * 6.2831853;
        p.y += mask.r * sin(phase) * _FinLift;
        p.z += mask.r * cos(phase) * _FinSweep;
        // Body wave travelling from head to tail.
        float wave = sin(t * _SwimSpeed - p.z * _SwimFreq);
        p += _SwimAxis.xyz * (mask.g * mask.g * wave * _SwimAmount);
        return p;
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "CreatureForward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "StarlitNoise.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                float4 mask : TEXCOORD3;
                float belly : TEXCOORD4;
                float fogFactor : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float3 p = AnimateCreature(IN.positionOS.xyz, IN.color);
                // Bend the normal with the fin stroke (finite difference of the lift).
                float3 n = IN.normalOS;
                float t = _Time.y + _AnimTime;
                float slope = IN.color.r * cos(t * _FinSpeed - abs(IN.positionOS.x) * _FinLag + IN.color.a * 6.2831853) * _FinLift * 0.6;
                n = normalize(n + float3(-sign(IN.positionOS.x) * slope * n.y, 0, 0));

                OUT.positionWS = TransformObjectToWorld(p);
                OUT.normalWS = TransformObjectToWorldNormal(n);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.uv = IN.uv;
                OUT.mask = IN.color;
                OUT.belly = smoothstep(_BellyLow, _BellyHigh, -IN.normalOS.y);
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            // Distance to the nearest Voronoi border (turtle-shell scutes).
            float VoronoiEdge(float2 p)
            {
                float2 cell = floor(p);
                float2 f = frac(p);
                float d1 = 8.0, d2 = 8.0;
                [unroll] for (int y = -1; y <= 1; y++)
                [unroll] for (int x = -1; x <= 1; x++)
                {
                    float2 o = float2(x, y);
                    float2 h = SN_Hash32(cell + o).xy;
                    float d = length(o + 0.15 + 0.7 * h - f);
                    if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) { d2 = d; }
                }
                return d2 - d1;
            }

            half4 Frag(Varyings IN, bool frontFace : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                float3 N = normalize(IN.normalWS) * (frontFace ? 1.0 : -1.0);
                float3 V = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float t = _Time.y + _AnimTime;

                float3 albedo = lerp(_BackColor.rgb, _BellyColor.rgb, IN.belly);

                // ---- lighting: moon + sky ambient + nearby lights, wet specular ----
                Light moon = GetMainLight();
                float ndl = dot(N, moon.direction);
                float wrapped = saturate((ndl + _Wrap) / (1.0 + _Wrap));
                // Stylized: soft bands of light (painted look) blended with smooth shading.
                float stepped = wrapped * _ToonSteps;
                float banded = (floor(stepped) + smoothstep(0.35, 0.65, frac(stepped))) / _ToonSteps;
                float3 diffuse = moon.color * lerp(wrapped, banded, _Toon) + SampleSH(N);
                float3 H = normalize(moon.direction + V);
                float3 spec = moon.color * pow(saturate(dot(N, H)), _Gloss) * _SpecStrength;

                #if defined(_ADDITIONAL_LIGHTS)
                {
                    InputData inputData = (InputData)0;
                    inputData.positionWS = IN.positionWS;
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                    uint lightCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(lightCount)
                        Light light = GetAdditionalLight(lightIndex, IN.positionWS);
                        float3 lc = light.color * light.distanceAttenuation;
                        diffuse += lc * saturate((dot(N, light.direction) + _Wrap) / (1.0 + _Wrap));
                        float3 h2 = normalize(light.direction + V);
                        spec += lc * pow(saturate(dot(N, h2)), _Gloss) * _SpecStrength;
                    LIGHT_LOOP_END
                }
                #endif

                float3 color = albedo * diffuse + spec;

                // ---- bioluminescent patterns (only where the glow mask allows) ----
                float pulse = 0.65 + 0.35 * sin(t * _PulseSpeed);

                float2 cellUV = IN.uv * _SpotScale.xy;
                float2 cell = floor(cellUV);
                float3 h = SN_Hash32(cell + 17.0);
                float spot = 0;
                if (h.z < _SpotDensity)
                {
                    float d = length(frac(cellUV) - (0.25 + 0.5 * h.xy));
                    float twinkle = 0.55 + 0.45 * sin(t * (1.2 + h.x * 2.5) + h.y * 40.0);
                    spot = saturate(1.0 - d / (0.16 + 0.1 * h.x));
                    spot *= spot * twinkle;
                }

                float seam = 1.0 - smoothstep(0.02, 0.09, VoronoiEdge(IN.uv * _SeamScale));

                float side = frac(IN.uv.x * 2.0);
                float flank = min(side, 1.0 - side);
                float stripe = exp(-(flank * flank) / (_StripeWidth * _StripeWidth));
                stripe *= 0.55 + 0.45 * sin(IN.uv.y * 24.0 - t * 5.0);   // light racing along the body

                float edge = smoothstep(0.82, 1.0, abs(IN.uv.x * 2.0 - 1.0));

                float glow = (spot * _SpotStrength + seam * _SeamStrength + stripe * _StripeStrength) * pulse
                           + edge * _EdgeGlow * (0.7 + 0.3 * pulse);
                color += _GlowColor.rgb * glow * IN.mask.b;

                // ---- Iridescence: a thin-film rainbow sheen that shifts with the view angle ----
                float facing = 1.0 - saturate(dot(N, V));
                float3 irid = 0.5 + 0.5 * cos(6.2831853 * (facing * _IridScale + IN.uv.y * 0.35 + float3(0.0, 0.33, 0.67)));
                color += irid * irid * pow(facing, 1.5) * _Iridescence * (0.4 + 0.6 * saturate(ndl + 0.6));

                // ---- Fresnel rim: the silhouette glows against the dark sea ----
                float rim = pow(facing, _RimPower);
                color += _RimColor.rgb * rim * (0.6 + 0.4 * pulse);

                color = MixFog(color, IN.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct A { float4 positionOS : POSITION; float4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            V DepthVert(A IN)
            {
                V OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(AnimateCreature(IN.positionOS.xyz, IN.color));
                return OUT;
            }

            half DepthFrag(V IN) : SV_Target { return IN.positionCS.z; }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex DNVert
            #pragma fragment DNFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; float4 color : COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            V DNVert(A IN)
            {
                V OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(AnimateCreature(IN.positionOS.xyz, IN.color));
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                return OUT;
            }

            half4 DNFrag(V IN) : SV_Target
            {
                float3 n = normalize(IN.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 oct = PackNormalOctQuadEncode(n);
                    return half4(PackFloat2To888(saturate(oct * 0.5 + 0.5)), 0.0);
                #else
                    return half4(n, 0.0);
                #endif
            }
            ENDHLSL
        }
    }
    FallBack Off
}
