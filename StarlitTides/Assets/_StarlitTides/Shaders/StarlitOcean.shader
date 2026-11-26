// =============================================================================
//  STARLIT TIDES - Bioluminescent Ocean (URP, hand-written HLSL)
// -----------------------------------------------------------------------------
//  The hero shader of the game. One pass does all of this:
//    VERTEX   - 4 summed Gerstner waves (real ocean wave shape, sharp crests)
//             - analytic wave normals, distance fade, calm zone for the lagoon
//             - the whole wave pattern scrolls toward -Z  (the "tide")
//    FRAGMENT - procedural ripple normals (no textures needed)
//             - depth-based colour  (shallow teal -> deep navy) using the
//               camera Depth Texture
//             - refraction of what is under the water (camera Opaque Texture)
//             - Fresnel reflection of the sky / reflection probe
//             - moonlight glint + a glittering "moon path"
//             - reflections of village lanterns (URP additional lights)
//             - stylized shoreline foam bands + white wave-crest foam
//             - BIOLUMINESCENCE: neon cyan glow on crests, breaking surf,
//               twinkling plankton sparkles, and glowing rings wherever an
//               object touches the water (boat, rocks, jellyfish, orbs)
//             - global "Starburst" boost that lights up the whole sea
//             - URP fog
//
//  Driven at runtime by OceanController.cs, which sets these GLOBAL values:
//    _OceanTime, _TideOffset, _WaveScale, _SeaGlowBoost, _SeaGlowColor,
//    _DisturbPoints[32], _DisturbCount
//
//  REQUIREMENTS: URP Asset > Depth Texture ON and Opaque Texture ON.
// =============================================================================
Shader "StarlitTides/Ocean"
{
    Properties
    {
        [Header(Water Colour)]
        _ShallowColor   ("Shallow Colour", Color) = (0.06, 0.42, 0.48, 1)
        _DeepColor      ("Deep Colour", Color) = (0.008, 0.02, 0.07, 1)
        _HorizonColor   ("Horizon Colour (grazing angles)", Color) = (0.07, 0.08, 0.2, 1)
        _DepthDistance  ("Depth Fade Distance (m)", Range(0.5, 30)) = 6
        _Absorption     ("Underwater Absorption (RGB)", Vector) = (0.45, 0.12, 0.08, 0)
        _RefractionStrength ("Refraction Strength", Range(0, 0.1)) = 0.025

        [Header(Reflection and Lighting)]
        _ReflectionStrength ("Reflection Strength", Range(0, 2)) = 0.9
        _ReflectionRoughness ("Reflection Roughness", Range(0, 1)) = 0.08
        _FresnelPower   ("Fresnel Power", Range(1, 10)) = 5
        _SpecularPower  ("Moon Glint Sharpness", Range(16, 2048)) = 900
        _SpecularIntensity ("Moon Glint Intensity", Range(0, 20)) = 6
        _MoonPathGlitter ("Moon Path Glitter", Range(0, 10)) = 3
        _LanternReflection ("Lantern Reflection Strength", Range(0, 10)) = 2.5

        [Header(Waves  xy dir  z steepness  w wavelength)]
        _WaveA ("Wave A", Vector) = (0.3, -1, 0.16, 24)
        _WaveB ("Wave B", Vector) = (-0.71, -0.69, 0.12, 13.7)
        _WaveC ("Wave C", Vector) = (0.83, -0.37, 0.10, 7.3)
        _WaveD ("Wave D", Vector) = (-0.41, -1, 0.07, 3.9)
        _WaveFadeStart ("Wave Fade Start (m from centre)", Float) = 180
        _WaveFadeEnd   ("Wave Fade End (m from centre)", Float) = 650
        _CalmZone ("Calm Zone (centre XZ, radius XZ)", Vector) = (-85, 95, 34, 28)
        _TideInfluence ("Tide Scroll Influence", Range(0, 1)) = 1

        [Header(Ripples)]
        _RippleScale    ("Ripple Scale", Float) = 0.35
        _RippleStrength ("Ripple Strength", Range(0, 1)) = 0.35
        _RippleSpeed    ("Ripple Speed", Float) = 0.6

        [Header(Foam)]
        [HDR] _FoamColor ("Foam Colour", Color) = (0.45, 0.85, 1.0, 1)
        _FoamDistance   ("Shore Foam Distance (m)", Range(0.1, 6)) = 1.1
        _FoamSpeed      ("Shore Foam Speed", Float) = 0.35
        _FoamScale      ("Foam Noise Scale", Float) = 0.6
        _CrestFoamThreshold ("Crest Foam Height", Float) = 0.62
        _CrestFoamAmount ("Crest Foam Amount", Range(0, 1)) = 0.18

        [Header(Bioluminescence)]
        [HDR] _BioColor ("Bioluminescence Colour", Color) = (0.15, 2.6, 3.2, 1)
        _BioAmbient     ("Ambient Plankton Glow", Range(0, 2)) = 0.02
        _BioCrest       ("Wave Crest Glow", Range(0, 4)) = 0.7
        _BioShore       ("Breaking Surf Glow", Range(0, 6)) = 1.6
        _BioDisturb     ("Disturbance Glow", Range(0, 10)) = 3.0
        _SparkleScale   ("Plankton Sparkle Cell Size (m)", Float) = 1.1
        _SparkleDensity ("Plankton Sparkle Density", Range(0, 1)) = 0.35
        _SparkleIntensity ("Plankton Sparkle Intensity", Range(0, 20)) = 5
    }

    SubShader
    {
        // Rendered after opaque objects (so it can read their depth and colour)
        // but before normal transparent effects (particles render on top).
        Tags
        {
            "RenderType" = "Transparent"
            "Queue" = "Transparent-50"
            "RenderPipeline" = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "OceanForward"
            Tags { "LightMode" = "UniversalForward" }

            // The water is drawn fully opaque: "transparency" comes from
            // sampling the scene colour behind it (refraction). This avoids
            // sorting problems and lets particles depth-test against the sea.
            Blend Off
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex OceanVert
            #pragma fragment OceanFrag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            // Low quality setting: cheaper ripples, no lantern reflections (set by SettingsManager).
            #pragma multi_compile _ STARLIT_QUALITY_LOW

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
            #include "StarlitNoise.hlsl"

            // ---------- per-material values (SRP Batcher compatible) ----------
            CBUFFER_START(UnityPerMaterial)
                float4 _ShallowColor, _DeepColor, _HorizonColor;
                float  _DepthDistance;
                float4 _Absorption;
                float  _RefractionStrength;
                float  _ReflectionStrength, _ReflectionRoughness, _FresnelPower;
                float  _SpecularPower, _SpecularIntensity, _MoonPathGlitter, _LanternReflection;
                float4 _WaveA, _WaveB, _WaveC, _WaveD;
                float  _WaveFadeStart, _WaveFadeEnd;
                float4 _CalmZone;
                float  _TideInfluence;
                float  _RippleScale, _RippleStrength, _RippleSpeed;
                float4 _FoamColor;
                float  _FoamDistance, _FoamSpeed, _FoamScale, _CrestFoamThreshold, _CrestFoamAmount;
                float4 _BioColor;
                float  _BioAmbient, _BioCrest, _BioShore, _BioDisturb;
                float  _SparkleScale, _SparkleDensity, _SparkleIntensity;
            CBUFFER_END

            // ---------- global values written by OceanController.cs ----------
            float  _OceanTime;        // our own clock (matches the CPU copy)
            float  _TideOffset;       // metres the sea pattern has flowed toward -Z
            float  _WaveScale;        // weather: 1 = calm night, ~1.8 = storm
            float  _SeaGlowBoost;     // Starburst power-up: 0..1
            float4 _SeaGlowColor;     // Starburst tint (HDR)
            float  _SeaNightGlow;     // 0 = no plankton glow (daytime in the intro), 1 = full
            float4 _DisturbPoints[32];// xy = world XZ, z = radius, w = strength
            int    _DisturbCount;

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float  waveHeight : TEXCOORD2;   // vertical displacement, for crest foam/glow
                float  eyeDepth   : TEXCOORD3;   // view-space depth of the water surface
                float  fogFactor  : TEXCOORD4;
                float2 flowXZ     : TEXCOORD5;   // world XZ shifted by the tide
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // -----------------------------------------------------------------
            // One Gerstner wave.  wave = (dirX, dirZ, steepness, wavelength)
            // Points move in circles: they bunch up at crests (sharp peaks)
            // and spread out in troughs - this is what makes it look like water
            // instead of a sine-wave carpet.  We also accumulate the tangent
            // and binormal so we get an exact normal with no extra samples.
            // -----------------------------------------------------------------
            float3 GerstnerWave(float4 wave, float2 p, float amp, inout float3 tangent, inout float3 binormal)
            {
                float steepness = wave.z * amp;
                float k = TWO_PI / wave.w;                 // wave number
                float c = sqrt(9.8 / k);                   // deep-water phase speed
                float2 d = normalize(wave.xy);
                float f = k * (dot(d, p) - c * _OceanTime);
                float a = steepness / k;                   // amplitude
                float s, co;
                sincos(f, s, co);

                tangent  += float3(-d.x * d.x * steepness * s, d.x * steepness * co, -d.x * d.y * steepness * s);
                binormal += float3(-d.x * d.y * steepness * s, d.y * steepness * co, -d.y * d.y * steepness * s);
                return float3(d.x * a * co, a * s, d.y * a * co);
            }

            // How strong the waves are at this world position (0..1):
            // fades out far away (no aliasing at the horizon) and inside the
            // calm lagoon ellipse.  Mirrored exactly in OceanController.cs.
            float WaveAmplitude(float2 worldXZ)
            {
                float dist = length(worldXZ);
                float far = 1.0 - smoothstep(_WaveFadeStart, _WaveFadeEnd, dist);
                float2 e = (worldXZ - _CalmZone.xy) / max(_CalmZone.zw, 0.001);
                float calm = smoothstep(0.7, 1.3, length(e));
                return far * calm * _WaveScale;
            }

            Varyings OceanVert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);

                // The tide: sample the wave field further "upstream" so the
                // whole pattern flows toward the camera (-Z).
                float2 p = posWS.xz + float2(0, _TideOffset * _TideInfluence);
                float amp = WaveAmplitude(posWS.xz);

                float3 tangent = float3(1, 0, 0);
                float3 binormal = float3(0, 0, 1);
                float3 offset = 0;
                offset += GerstnerWave(_WaveA, p, amp, tangent, binormal);
                offset += GerstnerWave(_WaveB, p, amp, tangent, binormal);
                offset += GerstnerWave(_WaveC, p, amp, tangent, binormal);
                offset += GerstnerWave(_WaveD, p, amp, tangent, binormal);
                posWS += offset;

                OUT.positionWS = posWS;
                OUT.normalWS   = normalize(cross(binormal, tangent));
                OUT.waveHeight = offset.y;
                OUT.flowXZ     = posWS.xz + float2(0, _TideOffset * _TideInfluence);
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.eyeDepth   = -TransformWorldToView(posWS).z;
                OUT.fogFactor  = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            // Small moving ripples, made from a procedural height field.
            // The slope (finite differences) bends the normal.
            float3 RippleNormal(float2 flowXZ, float distanceFade)
            {
                float2 p = flowXZ * _RippleScale;
                float t = _OceanTime * _RippleSpeed;
                float e = 0.15;
                float2 o1 = float2(t * 0.6, t * 0.35);
                float2 o2 = float2(-t * 0.4, t * 0.55);
            #if defined(STARLIT_QUALITY_LOW)
                // Low quality: one 2-octave noise instead of two (about 3x cheaper).
                float hl  = SN_Fbm2(p + o1, 2);
                float hlx = SN_Fbm2(p + float2(e, 0) + o1, 2);
                float hlz = SN_Fbm2(p + float2(0, e) + o1, 2);
                float2 slopeLow = float2(hlx - hl, hlz - hl) / e * _RippleStrength * distanceFade;
                return normalize(float3(-slopeLow.x, 1, -slopeLow.y));
            #endif
                float h  = SN_Fbm2(p + o1, 3) + SN_Fbm2(p * 1.7 + o2, 2) * 0.5;
                float hx = SN_Fbm2(p + float2(e, 0) + o1, 3) + SN_Fbm2((p + float2(e, 0)) * 1.7 + o2, 2) * 0.5;
                float hz = SN_Fbm2(p + float2(0, e) + o1, 3) + SN_Fbm2((p + float2(0, e)) * 1.7 + o2, 2) * 0.5;
                float2 slope = float2(hx - h, hz - h) / e;
                slope *= _RippleStrength * distanceFade;
                return normalize(float3(-slope.x, 1, -slope.y));
            }

            // Twinkling plankton: one random point per grid cell that blinks.
            float PlanktonSparkle(float2 flowXZ, float cellSize, float density)
            {
                float2 p = flowXZ / cellSize;
                float2 cell = floor(p);
                float2 f = frac(p);
                float3 h = SN_Hash32(cell);
                if (h.z > density) return 0;
                float2 starPos = 0.2 + 0.6 * h.xy;
                float d = length(f - starPos);
                float blink = saturate(sin(_OceanTime * (1.5 + h.x * 3.0) + h.y * 40.0) * 1.4 - 0.4);
                float dotShape = saturate(1.0 - d / 0.16);
                return dotShape * dotShape * blink;
            }

            // Glow from every object touching the water: a bright core plus
            // an expanding ring, like light spilling out of disturbed plankton.
            float DisturbanceGlow(float3 posWS)
            {
                float glow = 0;
                [loop] for (int i = 0; i < _DisturbCount; i++)
                {
                    float4 dp = _DisturbPoints[i];
                    float r = max(dp.z, 0.05);
                    float d = distance(posWS.xz, dp.xy);
                    if (d > r * 2.0) continue;
                    float core = exp(-(d * d) / (r * r * 0.18));
                    float ringPos = (d - r * 0.75) / (r * 0.25);
                    float ring = exp(-ringPos * ringPos) * 0.6;
                    glow += (core + ring) * dp.w;
                }
                return glow;
            }

            // Many overlapping ripples (e.g. a fast dash) would add up to a solid
            // blown-out blob. This curve grows linearly at first, then levels off.
            float SoftLimit(float x) { return x / (1.0 + x * 0.6); }

            half4 OceanFrag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                float3 posWS = IN.positionWS;
                float3 viewDirWS = normalize(GetWorldSpaceViewDir(posWS));
                float camDist = length(GetCameraPositionWS() - posWS);
                float nearFade = 1.0 - smoothstep(60.0, 260.0, camDist);  // fade detail with distance

                // ---- normal: big waves + small ripples ----
                float3 waveN = normalize(IN.normalWS);
                float3 rippleN = RippleNormal(IN.flowXZ, nearFade);
                float3 N = normalize(float3(waveN.xz + rippleN.xz, waveN.y * rippleN.y).xzy);

                // ---- water depth from the camera depth texture ----
                float2 screenUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float waterDepth = max(sceneEye - IN.eyeDepth, 0);

                // ---- refraction: bend the view of what is underwater ----
                float2 refrUV = screenUV + N.xz * _RefractionStrength * saturate(waterDepth * 0.5);
                float refrEye = LinearEyeDepth(SampleSceneDepth(refrUV), _ZBufferParams);
                float refrDepth = refrEye - IN.eyeDepth;
                if (refrDepth < 0) { refrUV = screenUV; refrDepth = waterDepth; } // don't refract things in front
                float3 sceneBelow = SampleSceneColor(refrUV);

                // ---- water body colour (Beer-Lambert style absorption) ----
                float3 transmittance = exp(-refrDepth * _Absorption.rgb);
                float depthFactor = 1.0 - exp(-refrDepth / _DepthDistance);
                float3 waterTint = lerp(_ShallowColor.rgb, _DeepColor.rgb, depthFactor);
                float3 body = lerp(sceneBelow * transmittance * _ShallowColor.rgb * 2.0, waterTint, saturate(depthFactor * 1.15));

                // ---- Fresnel reflection of sky + reflection probe ----
                float NdotV = saturate(dot(N, viewDirWS));
                float fresnel = 0.02 + 0.98 * pow(1.0 - NdotV, _FresnelPower);
                float3 R = reflect(-viewDirWS, N);
                R.y = abs(R.y);   // never reflect "below" the horizon
                float3 reflection = GlossyEnvironmentReflection(R, posWS, _ReflectionRoughness, 1.0, screenUV) * _ReflectionStrength;
                reflection = lerp(reflection, _HorizonColor.rgb, saturate(1.0 - nearFade) * 0.35);

                float3 color = lerp(body, reflection, fresnel);

                // ---- moonlight: sharp glint + glittering moon path ----
                Light mainLight = GetMainLight();
                float3 H = normalize(mainLight.direction + viewDirWS);
                float NdotH = saturate(dot(N, H));
                float glint = pow(NdotH, _SpecularPower) * _SpecularIntensity;
                float glitterCells = PlanktonSparkle(IN.flowXZ * 3.0 + _OceanTime * 0.5, 0.35, 0.6);
                float moonPath = pow(NdotH, _SpecularPower * 0.08) * glitterCells * _MoonPathGlitter;
                color += (glint + moonPath) * mainLight.color;

                // ---- lantern / window lights reflected on the water ----
                #if defined(_ADDITIONAL_LIGHTS) && !defined(STARLIT_QUALITY_LOW)
                {
                    InputData inputData = (InputData)0;
                    inputData.positionWS = posWS;
                    inputData.normalizedScreenSpaceUV = screenUV;
                    uint lightCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(lightCount)
                        Light light = GetAdditionalLight(lightIndex, posWS);
                        float3 h2 = normalize(light.direction + viewDirWS);
                        float spec = pow(saturate(dot(N, h2)), 180.0);
                        color += light.color * light.distanceAttenuation * spec * _LanternReflection;
                    LIGHT_LOOP_END
                }
                #endif

                // ---- foam ----
                float foamNoise = SN_Fbm2(IN.flowXZ * _FoamScale + float2(_OceanTime * 0.1, 0), 3);
                float edge = saturate(1.0 - waterDepth / _FoamDistance);                // 1 where water meets land
                float bands = 0.5 + 0.5 * sin((edge * 3.0 + _OceanTime * _FoamSpeed) * TWO_PI);
                float shoreFoam = smoothstep(0.55, 0.7, edge * (0.55 + 0.45 * bands) + foamNoise * 0.4);
                shoreFoam = max(shoreFoam, smoothstep(0.88, 0.97, edge));               // solid contact line
                float crest = saturate((IN.waveHeight - _CrestFoamThreshold * _WaveScale) / (0.3 * _WaveScale + 0.01));
                crest *= crest;                                                          // thin, sharp crest lines
                float crestFoam = crest * smoothstep(0.45, 0.85, foamNoise + crest * 0.3) * _CrestFoamAmount;
                float foam = saturate(shoreFoam + crestFoam);
                color = lerp(color, _FoamColor.rgb, foam * 0.55);

                // ---- BIOLUMINESCENCE ----
                float sparkle = PlanktonSparkle(IN.flowXZ, _SparkleScale, _SparkleDensity) * nearFade;
                float plankton = 0.6 + 0.4 * SN_Fbm2(IN.flowXZ * 0.08 + _OceanTime * 0.03, 2);   // patchy blooms
                // large drifting "bloom" patches break up the regular wave pattern
                float bloomPatch = smoothstep(0.35, 0.75, SN_Fbm2(IN.flowXZ * 0.025 + 5.0, 3));
                float glow = _BioAmbient * plankton
                           + crest * _BioCrest * (0.25 + plankton) * (0.3 + bloomPatch)
                           + shoreFoam * _BioShore * plankton
                           + SoftLimit(DisturbanceGlow(posWS)) * _BioDisturb * (0.7 + 0.6 * foamNoise)
                           + sparkle * _SparkleIntensity;
                float3 bioLight = glow * _BioColor.rgb * _SeaNightGlow;

                // Starburst power-up: the plankton glows brighter and golden light
                // shimmers across the whole sea (sparkles and wave crests the most).
                bioLight *= 1.0 + _SeaGlowBoost * 0.4;
                float boost = _SeaGlowBoost * (0.07 + 0.3 * bloomPatch + sparkle * 2.5 + crest * 0.7);
                bioLight += boost * _SeaGlowColor.rgb;

                color += bioLight;

                // ---- fog ----
                color = MixFog(color, IN.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
