// =============================================================================
//  StarlitTides/Stylized  -  the "Moonlit Storybook" look for every solid prop
// -----------------------------------------------------------------------------
//  Phase 8, Step 2. Replaces "Universal Render Pipeline/Lit" on houses, rocks,
//  trees, the lighthouse, the pier, the boat, obstacles and buoys.
//
//  Realistic (PBR) shading on simple procedural shapes looks cheap: flat grey
//  faces, black shadows. This shader paints them instead:
//
//   1. BANDED LIGHT   N.L (with a little wrap) goes through a soft 3-tone ramp:
//                     shadow -> mid tone -> lit. smoothstep() keeps the band
//                     edges soft, so it reads as painted, not hard cel shading.
//   2. COLOURED SHADOW the shadow tone is ambient light + an indigo tint, never
//                     black. Ambient comes from the scene's light probe (SampleSH),
//                     so the sunset intro and the storm still change it.
//   3. MOONLIT RIM    a fresnel band on the silhouette, strongest when you look
//                     toward the moon (the game camera does), on the moon side.
//                     It separates dark objects from the dark sea.
//   4. SPECULAR BLOB  glossy materials get one crisp highlight instead of a
//                     blurry PBR lobe.
//   5. HEIGHT GRADIENT darker at the base, lighter at the top of each object.
//   6. PAINT NOISE    triplanar low-frequency noise nudges the flat colour, like
//                     brush strokes. (Skipped on the Low quality preset.)
//   7. OUTLINE PASS   inverted hull: the mesh is drawn again, pushed out along
//                     its *smoothed* normal (baked into UV3 by MeshBuilder) and
//                     only its back faces shown, in a deep indigo. The push is
//                     in clip space, so the line is the same pixel width at any
//                     distance, and it fades out far away. Off on Low quality.
//                     Builders switch it off per material with
//                     material.SetShaderPassEnabled("SRPDefaultUnlit", false).
//
//  Property names match URP Lit (_BaseColor, _BaseMap, _Smoothness,
//  _EmissionColor), so scripts that drive them (LanternFlicker, LightMeter)
//  keep working. Passes: forward, outline, shadow caster, depth, depth-normals
//  (needed for shadows, SSAO and the ocean's depth/refraction).
// =============================================================================
Shader "StarlitTides/Stylized"
{
    Properties
    {
        [MainColor] _BaseColor ("Base Colour", Color) = (0.6, 0.6, 0.65, 1)
        [MainTexture] _BaseMap ("Base Map", 2D) = "white" {}
        _Smoothness     ("Gloss (0 = matte, 1 = wet)", Range(0, 1)) = 0.15
        [HDR] _EmissionColor ("Emission", Color) = (0, 0, 0, 1)

        [Header(Banded light)]
        _Wrap           ("Light Wrap", Range(0, 1)) = 0.4
        _BandThreshold1 ("Shadow -> Mid", Range(0, 1)) = 0.12
        _BandThreshold2 ("Mid -> Lit", Range(0, 1)) = 0.55
        _MidTone        ("Mid Tone Brightness", Range(0, 1)) = 0.6
        _BandSoftness   ("Band Softness", Range(0.001, 0.3)) = 0.06
        _ShadowTint     ("Shadow Tint (indigo)", Color) = (0.15, 0.12, 0.32, 1)
        _AmbientStrength("Ambient Strength", Range(0, 3)) = 1.6

        [Header(Moonlit rim)]
        _RimColor       ("Rim Colour", Color) = (0.85, 0.92, 1, 1)
        _RimStrength    ("Rim Strength", Range(0, 3)) = 1.2
        _RimWidth       ("Rim Width", Range(0, 1)) = 0.42
        _RimSoftness    ("Rim Softness", Range(0.01, 0.5)) = 0.16

        [Header(Accent from vertex colour red)]
        [Toggle(_VERTEX_ACCENT)] _VertexAccent ("Use Vertex Accent Mask", Float) = 0
        _AccentColor    ("Accent Colour (snow, moss)", Color) = (0.55, 0.6, 0.8, 1)

        [Header(Highlight)]
        _SpecularStrength ("Specular Blob Strength", Range(0, 3)) = 1

        [Header(Painted look)]
        _GradientStrength ("Height Gradient", Range(0, 1)) = 0.25
        _GradientHeight   ("Gradient Height (m)", Float) = 4
        _PaintStrength    ("Paint Noise", Range(0, 0.5)) = 0.08
        _PaintScale       ("Paint Noise Scale (1/m)", Float) = 0.6

        [Header(Outline)]
        _OutlineColor     ("Outline Colour", Color) = (0.035, 0.03, 0.09, 1)
        _OutlineWidth     ("Outline Width (px at 1080p)", Range(0, 6)) = 1.6
        _OutlineFadeStart ("Outline Fade Start (m)", Float) = 35
        _OutlineFadeEnd   ("Outline Fade End (m)", Float) = 110

        // kept so code written for URP Lit can set them without warnings
        [HideInInspector] _Metallic ("Metallic", Range(0, 1)) = 0
        [HideInInspector] _Cull ("Cull", Float) = 2
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);

    // Every property in one per-material buffer: required for the SRP Batcher.
    CBUFFER_START(UnityPerMaterial)
        float4 _BaseMap_ST;
        float4 _BaseColor;
        float  _Smoothness;
        float4 _EmissionColor;
        float  _Wrap, _BandThreshold1, _BandThreshold2, _MidTone, _BandSoftness;
        float4 _ShadowTint;
        float  _AmbientStrength;
        float4 _RimColor;
        float  _RimStrength, _RimWidth, _RimSoftness;
        float  _SpecularStrength;
        float  _GradientStrength, _GradientHeight, _PaintStrength, _PaintScale;
        float4 _OutlineColor;
        float  _OutlineWidth, _OutlineFadeStart, _OutlineFadeEnd;
        float  _Metallic, _Cull;
        float4 _AccentColor;
        float  _VertexAccent;
    CBUFFER_END
    ENDHLSL

    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        // ---------------------------------------------------------------------
        // 1. Forward lit pass
        // ---------------------------------------------------------------------
        Pass
        {
            Name "StylizedForward"
            Tags { "LightMode" = "UniversalForward" }
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile _ STARLIT_QUALITY_LOW
            #pragma shader_feature_local _VERTEX_ACCENT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "StarlitNoise.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;       // red = accent mask (snow caps, moss)
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                float  heightRel  : TEXCOORD3;   // metres above the object's origin
                float  fogFactor  : TEXCOORD4;
                float  accent     : TEXCOORD5;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.heightRel = pos.positionWS.y - UNITY_MATRIX_M._m13;   // object origin height
                OUT.fogFactor = ComputeFogFactor(pos.positionCS.z);
                #if defined(_VERTEX_ACCENT)
                    OUT.accent = IN.color.r;
                #endif
                return OUT;
            }

            // Soft three-tone ramp: 0 = shadow, _MidTone = mid, 1 = lit.
            float Ramp(float x)
            {
                float a = smoothstep(_BandThreshold1 - _BandSoftness, _BandThreshold1 + _BandSoftness, x);
                float b = smoothstep(_BandThreshold2 - _BandSoftness, _BandThreshold2 + _BandSoftness, x);
                return a * _MidTone + b * (1.0 - _MidTone);
            }

            float WrappedNdotL(float3 N, float3 L)
            {
                return saturate((dot(N, L) + _Wrap) / (1.0 + _Wrap));
            }

            // Triplanar value noise: three planar lookups blended by the normal.
            float PaintNoise(float3 posWS, float3 N)
            {
                float3 w = pow(abs(N), 4.0);
                w /= max(w.x + w.y + w.z, 1e-4);
                float3 p = posWS * _PaintScale;
                float n1 = SN_Noise2(p.yz) * w.x + SN_Noise2(p.xz) * w.y + SN_Noise2(p.xy) * w.z;
                float3 q = p * 3.1 + 17.0;
                float n2 = SN_Noise2(q.yz) * w.x + SN_Noise2(q.xz) * w.y + SN_Noise2(q.xy) * w.z;
                return (n1 * 0.7 + n2 * 0.3) * 2.0 - 1.0;   // -1..1
            }

            half4 Frag(Varyings IN, bool frontFace : SV_IsFrontFace) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float3 N = normalize(IN.normalWS) * (frontFace ? 1.0 : -1.0);
                float3 posWS = IN.positionWS;
                float3 V = normalize(GetWorldSpaceViewDir(posWS));
                float2 screenUV = GetNormalizedScreenSpaceUV(IN.positionCS);

                // ---- albedo: colour x texture x height gradient x paint noise ----
                float3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv).rgb * _BaseColor.rgb;
                #if defined(_VERTEX_ACCENT)
                    albedo = lerp(albedo, _AccentColor.rgb, saturate(IN.accent));   // snow caps, moss tops
                #endif
                float g = saturate(IN.heightRel / max(_GradientHeight, 0.01));
                albedo *= lerp(1.0 - _GradientStrength, 1.0 + _GradientStrength * 0.35, g);
                #if !defined(STARLIT_QUALITY_LOW)
                    albedo *= 1.0 + PaintNoise(posWS, N) * _PaintStrength;
                #endif

                // ---- main light (the moon): banded, with soft received shadows ----
                float4 shadowCoord = TransformWorldToShadowCoord(posWS);
                Light moon = GetMainLight(shadowCoord);
                float shadow = moon.shadowAttenuation;
                float band = Ramp(WrappedNdotL(N, moon.direction) * shadow);

                // ---- ambient (scene probe) and coloured shadow ----
                float3 ambient = SampleSH(N) * _AmbientStrength;
                #if defined(_SCREEN_SPACE_OCCLUSION)
                    AmbientOcclusionFactor ao = GetScreenSpaceAmbientOcclusion(screenUV);
                    ambient *= ao.indirectAmbientOcclusion;
                    band *= ao.directAmbientOcclusion;
                #endif
                float3 lightTone = lerp(_ShadowTint.rgb, moon.color, band);
                float3 color = albedo * (ambient + lightTone);

                // ---- extra lights (lanterns, windows, the boat candle): banded too ----
                #if defined(_ADDITIONAL_LIGHTS)
                {
                    InputData inputData = (InputData)0;
                    inputData.positionWS = posWS;
                    inputData.normalizedScreenSpaceUV = screenUV;
                    uint lightCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(lightCount)
                        Light light = GetAdditionalLight(lightIndex, posWS, half4(1, 1, 1, 1));
                        float atten = light.distanceAttenuation * light.shadowAttenuation;
                        color += albedo * light.color * Ramp(WrappedNdotL(N, light.direction)) * atten;
                    LIGHT_LOOP_END
                }
                #endif

                // ---- moonlit rim: brightest looking toward the moon, on its side ----
                float fresnel = 1.0 - saturate(dot(N, V));
                float rim = smoothstep(1.0 - _RimWidth, 1.0 - _RimWidth + _RimSoftness, fresnel);
                float towardMoon = saturate(dot(-V, moon.direction));
                float moonSide = saturate(dot(N, moon.direction) * 0.6 + 0.6);
                rim *= (0.35 + 0.65 * towardMoon) * moonSide * lerp(0.35, 1.0, shadow);
                color += rim * _RimStrength * _RimColor.rgb * moon.color * lerp(0.6, 1.0, albedo.g + 0.3);

                // ---- one crisp specular blob for glossy surfaces ----
                float3 H = normalize(moon.direction + V);
                float t = lerp(0.9, 0.995, _Smoothness);
                float spec = smoothstep(t - 0.004, t + 0.004, saturate(dot(N, H))) * smoothstep(0.25, 0.6, _Smoothness);
                color += spec * band * _SpecularStrength * moon.color;

                color += _EmissionColor.rgb;
                color = MixFog(color, IN.fogFactor);
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        // ---------------------------------------------------------------------
        // 2. Outline (inverted hull). URP draws "SRPDefaultUnlit" passes in the
        //    same opaque pass as the forward pass.
        // ---------------------------------------------------------------------
        Pass
        {
            Name "Outline"
            Tags { "LightMode" = "SRPDefaultUnlit" }
            Cull Front
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex OutlineVert
            #pragma fragment OutlineFrag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma multi_compile _ STARLIT_QUALITY_LOW

            struct A
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float3 smoothN    : TEXCOORD3;   // smoothed normal from MeshBuilder (0 if none)
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct V
            {
                float4 positionCS : SV_POSITION;
                float  fogFactor  : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            V OutlineVert(A IN)
            {
                V OUT = (V)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                // Smoothed normals keep the hull closed on flat-shaded meshes
                // (split vertices would otherwise tear apart at every edge).
                float3 n = dot(IN.smoothN, IN.smoothN) > 0.01 ? IN.smoothN : IN.normalOS;
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(n);
                float4 cs = TransformWorldToHClip(posWS);
                float2 dirCS = mul((float3x3)UNITY_MATRIX_VP, nWS).xy;
                dirCS /= max(length(dirCS), 1e-5);

                float fade = 1.0 - smoothstep(_OutlineFadeStart, _OutlineFadeEnd, cs.w);
                float px = _OutlineWidth * fade * (_ScreenParams.y / 1080.0);
                #if defined(STARLIT_QUALITY_LOW)
                    px = 0.0;
                #endif
                // pixels -> clip space: 2 / screen size, times w undoes the perspective divide
                cs.xy += dirCS * px * 2.0 / _ScreenParams.xy * cs.w;
                OUT.positionCS = cs;
                OUT.fogFactor = ComputeFogFactor(cs.z);
                return OUT;
            }

            half4 OutlineFrag(V IN) : SV_Target
            {
                return half4(MixFog(_OutlineColor.rgb, IN.fogFactor), 1.0);
            }
            ENDHLSL
        }

        // ---------------------------------------------------------------------
        // 3. Shadow caster
        // ---------------------------------------------------------------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; };

            V ShadowVert(A IN)
            {
                V OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                float3 nWS = TransformObjectToWorldNormal(IN.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDir = normalize(_LightPosition - posWS);
                #else
                    float3 lightDir = _LightDirection;
                #endif
                float4 cs = TransformWorldToHClip(ApplyShadowBias(posWS, nWS, lightDir));
                #if UNITY_REVERSED_Z
                    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                OUT.positionCS = cs;
                return OUT;
            }

            half4 ShadowFrag(V IN) : SV_Target { return 0; }
            ENDHLSL
        }

        // ---------------------------------------------------------------------
        // 4. Depth only (depth prepass / ocean depth)
        // ---------------------------------------------------------------------
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing

            struct A { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };

            V DepthVert(A IN)
            {
                V OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half DepthFrag(V IN) : SV_Target { return IN.positionCS.z; }
            ENDHLSL
        }

        // ---------------------------------------------------------------------
        // 5. Depth + normals (SSAO)
        // ---------------------------------------------------------------------
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DNVert
            #pragma fragment DNFrag
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            struct A { float4 positionOS : POSITION; float3 normalOS : NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 positionCS : SV_POSITION; float3 normalWS : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            V DNVert(A IN)
            {
                V OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
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
