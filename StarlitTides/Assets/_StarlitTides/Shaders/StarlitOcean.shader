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
        _ReflectionRoughness ("Reflection Roughness", Range(0, 1)) = 0.04
        _FresnelPower   ("Fresnel Power", Range(1, 10)) = 5
        _SpecularPower  ("Moon Glint Sharpness", Range(16, 2048)) = 900
        _SpecularIntensity ("Moon Glint Intensity", Range(0, 20)) = 8
        _MoonPathGlitter ("Moon Path Glitter", Range(0, 10)) = 4.5
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
        _RippleScale    ("Ripple Scale", Float) = 0.5
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
        [HDR] _BioColor2 ("Bioluminescence Colour 2 (blooms)", Color) = (2.2, 0.35, 3.0, 1)
        [HDR] _SparkleColor3 ("Sparkle Accent Colour", Color) = (3.4, 2.2, 0.5, 1)
        _BioHueScale ("Colour Bloom Size", Float) = 0.02
        _BioAmbient     ("Ambient Plankton Glow", Range(0, 2)) = 0.02
        _BioCrest       ("Wave Crest Glow", Range(0, 4)) = 0.1
        _BioShore       ("Breaking Surf Glow", Range(0, 6)) = 1.6
        _BioDisturb     ("Disturbance Glow", Range(0, 10)) = 3.0
        _SparkleScale   ("Plankton Sparkle Cell Size (m)", Float) = 1.1
        _SparkleDensity ("Plankton Sparkle Density", Range(0, 1)) = 0.35
        _SparkleIntensity ("Plankton Sparkle Intensity", Range(0, 20)) = 5

        [Header(Stylized look  Phase 8)]
        _StylizeAmount  ("Stylize Amount (0 = Phase 7 look)", Range(0, 1)) = 1
        _NearColor      ("Near Water Colour (looking down)", Color) = (0.035, 0.34, 0.42, 1)
        _ReflectionTint ("Reflection Tint (no grey wash)", Color) = (0.46, 0.56, 0.72, 1)
        _ColorBands     ("Colour Bands", Range(1, 8)) = 4
        _BandSoftness   ("Band Softness", Range(0.01, 0.5)) = 0.2
        _StarGlint      ("Star Glint Strength", Range(0, 20)) = 5
        _StarGlintCell  ("Star Glint Cell Size (m)", Float) = 1.1
        _MicroRipple    ("Micro Ripple Strength", Range(0, 1)) = 0.22
        _FoamLines      ("Crest Foam Lines", Range(0, 1)) = 0.55
        [HDR] _TrailColor ("Wake Glow Colour", Color) = (0.25, 2.3, 2.6, 1)
        _TrailWidth     ("Wake Width (m)", Float) = 0.75
        _TrailStrength  ("Wake Strength", Range(0, 6)) = 1.8
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
                float4 _BioColor, _BioColor2, _SparkleColor3;
                float  _BioHueScale;
                float  _BioAmbient, _BioCrest, _BioShore, _BioDisturb;
                float  _SparkleScale, _SparkleDensity, _SparkleIntensity;
                float  _StylizeAmount;
                float4 _NearColor, _ReflectionTint;
                float  _ColorBands, _BandSoftness, _StarGlint, _StarGlintCell, _MicroRipple, _FoamLines;
                float4 _TrailColor;
                float  _TrailWidth, _TrailStrength;
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
            float  _StarlitLaneHalfWidth; // PlayArea.cs: half width of the play lane (0 = no channel tint)
            float4 _TrailPoints[28];      // boat wake: flow XZ, time recorded, strength (oldest first)
            int    _TrailCount;
            float  _TrailLife;
            float4 _TrailBounds;          // wake bounding box in flow XZ (min x, min z, max x, max z)

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
                // Phase 8, Step 6: far water has the ripples faded to nothing anyway,
                // so skip the ~21 noise lookups there (a big share of the ocean's cost).
                if (distanceFade <= 0.001) return float3(0, 1, 0);
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
                // Micro ripples: a fine, fast layer that gives close water a crisp,
                // detailed sparkle. Faded out with distance so it never shimmers.
                float2 pf = p * 4.3 + float2(t * 1.1, -t * 0.8);
                float ef = 0.2;
                float f0 = SN_Fbm2(pf, 2);
                float fx = SN_Fbm2(pf + float2(ef, 0), 2);
                float fz = SN_Fbm2(pf + float2(0, ef), 2);
                slope += float2(fx - f0, fz - f0) / ef * _MicroRipple * distanceFade;   // Phase 8: calmer (was 0.5)
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
                    // Tight bright core + a crisp ring: reads as light spilling out of
                    // the plankton, not as a soft blurry pool.
                    float core = exp(-(d * d) / (r * r * 0.07));
                    float ringPos = (d - r * 0.72) / (r * 0.12);
                    float ring = exp(-ringPos * ringPos) * 0.8;
                    float halo = exp(-(d * d) / (r * r * 0.5)) * 0.12;
                    core += halo;
                    glow += (core + ring) * dp.w;
                }
                return glow;
            }

            // Many overlapping ripples (e.g. a fast dash) would add up to a solid
            // blown-out blob. This curve grows linearly at first, then levels off.
            float SoftLimit(float x) { return x / (1.0 + x * 0.6); }

            // ---------------- Phase 8 helpers ----------------
            // Soft posterize: n flat bands with a smooth step between them.
            float SoftBands(float x, float n, float softness)
            {
                float k = x * n;
                float band = floor(k) + smoothstep(0.5 - softness, 0.5 + softness, frac(k));
                return saturate(band / n);
            }

            // One twinkling 4-point star per cell (where the moon glints on the
            // waves): fewer, larger sparkles instead of single noisy pixels.
            // Fades out once a cell is only a few pixels wide (no aliasing).
            float StarGlint(float2 flowXZ, float cell)
            {
                float2 p = flowXZ / cell;
                float2 c = floor(p);
                float3 h = SN_Hash32(c + 17.0);
                if (h.z > 0.45) return 0;
                float2 o = frac(p) - 0.5 - (h.xy - 0.5) * 0.5;
                float core = saturate(1.0 - length(o) / 0.08);
                float arms = saturate(1.0 - abs(o.x) / 0.014) * saturate(1.0 - abs(o.y) / 0.24)
                           + saturate(1.0 - abs(o.y) / 0.014) * saturate(1.0 - abs(o.x) / 0.24);
                float twinkle = 0.45 + 0.55 * sin(_OceanTime * (2.0 + h.x * 4.0) + h.y * 30.0);
                float pixels = 1.0 / max(fwidth(p.x), 1e-4);          // cell size in pixels
                return (core * core * 1.6 + arms * 0.7) * twinkle * saturate((pixels - 6.0) / 10.0);
            }

            // The boat's glowing wake: a ribbon through the recorded points (in
            // flow space, so it drifts away with the water). Bright edges and a
            // softer middle, widening and fading with age.
            float WakeGlow(float2 flowXZ)
            {
                if (_TrailCount < 2) return 0;
                if (any(flowXZ < _TrailBounds.xy) || any(flowXZ > _TrailBounds.zw)) return 0;   // outside the wake's box
                float glow = 0;
                [loop] for (int i = 0; i < _TrailCount - 1; i++)
                {
                    float4 a = _TrailPoints[i], b = _TrailPoints[i + 1];
                    float2 ab = b.xy - a.xy;
                    float t = saturate(dot(flowXZ - a.xy, ab) / max(dot(ab, ab), 1e-4));
                    float d = distance(flowXZ, a.xy + ab * t);
                    float age = _OceanTime - lerp(a.z, b.z, t);
                    float life = saturate(1.0 - age / max(_TrailLife, 0.01));
                    float w = _TrailWidth * (1.0 + age * 0.9);
                    if (d > w * 2.5) continue;
                    float edge = (d - w * 0.8) / (w * 0.28);
                    float g = exp(-(d * d) / (w * w)) * 0.45 + exp(-edge * edge) * 0.8;
                    glow = max(glow, g * life * life * lerp(a.w, b.w, t));
                }
                return glow;
            }

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

                // ---- Phase 8: stylized colour ramp ----
                // Looking down at near water = teal, grazing / far water = deep indigo,
                // softly posterized into a few bands (painted, not photographic).
                // Shallow water still shows what is below it (rocks, sand).
                {
                    float facing = saturate(saturate(dot(N, viewDirWS)) * 1.25);
                    float nearAmt = 1.0 - smoothstep(25.0, 220.0, camDist);
                    float ramp = SoftBands(facing * lerp(0.35, 1.0, nearAmt), _ColorBands, _BandSoftness);
                    float3 styl = lerp(_DeepColor.rgb, _NearColor.rgb, ramp);
                    styl = lerp(body, styl, saturate(depthFactor * 1.2));
                    body = lerp(body, styl, _StylizeAmount);
                }

                // ---- Fresnel reflection of sky + reflection probe ----
                float NdotV = saturate(dot(N, viewDirWS));
                float fresnel = 0.02 + 0.98 * pow(1.0 - NdotV, _FresnelPower);
                float3 R = reflect(-viewDirWS, N);
                R.y = abs(R.y);   // never reflect "below" the horizon
                float3 reflection = GlossyEnvironmentReflection(R, posWS, _ReflectionRoughness, 1.0, screenUV) * _ReflectionStrength;
                reflection = lerp(reflection, _HorizonColor.rgb, saturate(1.0 - nearFade) * 0.35);

                // Phase 8: tint the reflection toward indigo-lilac instead of a grey wash,
                // and band the fresnel a little.
                reflection = lerp(reflection, Luminance(reflection) * _ReflectionTint.rgb * 1.3, _StylizeAmount * 0.5);
                fresnel = lerp(fresnel, SoftBands(fresnel, 3.0, 0.25), _StylizeAmount * 0.5);
                float3 color = lerp(body, reflection, fresnel);

                // ---- moonlight: sharp glint + glittering moon path ----
                Light mainLight = GetMainLight();
                float3 H = normalize(mainLight.direction + viewDirWS);
                float NdotH = saturate(dot(N, H));
                float glint = pow(NdotH, _SpecularPower) * _SpecularIntensity * lerp(1.0, 0.2, _StylizeAmount);   // Phase 8: fewer pixel speckles
                float glitterCells = PlanktonSparkle(IN.flowXZ * 3.0 + _OceanTime * 0.5, 0.35, 0.6);
                float moonPath = pow(NdotH, _SpecularPower * 0.08) * glitterCells * _MoonPathGlitter;
                color += (glint + moonPath) * mainLight.color;
                // Phase 8: star-shaped glints where the moon catches the waves
                #if !defined(STARLIT_QUALITY_LOW)
                    float starMask = pow(NdotH, _SpecularPower * 0.1);
                    color += StarGlint(IN.flowXZ, _StarGlintCell) * starMask * _StarGlint * _StylizeAmount * mainLight.color;
                #endif

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
                // Phase 8: crest foam as soft flowing lines (noise contours) instead of speckle
                float foamContour = 1.0 - abs(frac(foamNoise * 3.0 + _OceanTime * 0.04) * 2.0 - 1.0);
                float crestLines = saturate(crest * 2.5) * smoothstep(0.8, 0.92, foamContour) * _FoamLines;
                crestFoam = lerp(crestFoam, max(crestFoam * 0.4, crestLines), _StylizeAmount);
                float foam = saturate(shoreFoam + crestFoam);
                color = lerp(color, _FoamColor.rgb, foam * 0.55);

                // ---- BIOLUMINESCENCE ----
                float sparkle = PlanktonSparkle(IN.flowXZ, _SparkleScale, _SparkleDensity) * nearFade;
                float plankton = 0.6 + 0.4 * SN_Fbm2(IN.flowXZ * 0.08 + _OceanTime * 0.03, 2);   // patchy blooms
                // large drifting "bloom" patches break up the regular wave pattern
                float bloomPatch = smoothstep(0.35, 0.75, SN_Fbm2(IN.flowXZ * 0.025 + 5.0, 3));
                float glow = _BioAmbient * plankton
                           + crest * _BioCrest * (0.25 + plankton) * (0.15 + bloomPatch * 1.6)   // glow gathers in drifting blooms, the open sea stays dark
                           + shoreFoam * _BioShore * plankton
                           + SoftLimit(DisturbanceGlow(posWS)) * _BioDisturb * (0.7 + 0.6 * foamNoise);

                // Colourful plankton: big drifting blooms shift between cyan and
                // magenta, and each sparkle picks cyan, magenta or gold.
                float hueMix = smoothstep(0.64, 0.9, SN_Fbm2(IN.flowXZ * _BioHueScale + float2(31.0, 7.0) + _OceanTime * 0.01, 3));
                float3 bioCol = lerp(_BioColor.rgb, _BioColor2.rgb, hueMix);
                float sparkleHash = SN_Hash12(floor(IN.flowXZ / _SparkleScale) + 3.7);
                float3 sparkleCol = sparkleHash < 0.55 ? bioCol : (sparkleHash < 0.82 ? _BioColor2.rgb : _SparkleColor3.rgb);
                float3 bioLight = (glow * bioCol + sparkle * _SparkleIntensity * sparkleCol) * _SeaNightGlow;
                // Phase 8: the boat's glowing wake, with a slow swirl in it
                #if !defined(STARLIT_QUALITY_LOW)   // the wake loops over 28 segments per pixel near the boat
                    float wake = WakeGlow(IN.flowXZ) * (0.6 + 0.8 * SN_Noise2(IN.flowXZ * 1.3 + _OceanTime * 0.35));
                    bioLight += wake * _TrailStrength * _TrailColor.rgb * _SeaNightGlow;
                #endif

                // Starburst power-up: the plankton glows brighter and golden light
                // shimmers across the whole sea (sparkles and wave crests the most).
                bioLight *= 1.0 + _SeaGlowBoost * 0.4;
                float boost = _SeaGlowBoost * (0.07 + 0.3 * bloomPatch + sparkle * 2.5 + crest * 0.7);
                bioLight += boost * _SeaGlowColor.rgb;

                // ---- channel edge (Phase 8) ----
                // Past the play lane the sea is a little darker and the plankton
                // dimmer, so the lane reads as a glowing channel. The effect fades
                // out far to the sides and behind the boat (the bay stays as it was).
                if (_StarlitLaneHalfWidth > 0.0)
                {
                    float ax = abs(posWS.x);
                    float outside = smoothstep(_StarlitLaneHalfWidth + 3.0, _StarlitLaneHalfWidth + 14.0, ax)
                                  * (1.0 - smoothstep(_StarlitLaneHalfWidth + 90.0, _StarlitLaneHalfWidth + 200.0, ax))
                                  * smoothstep(-60.0, -30.0, posWS.z);
                    color *= 1.0 - 0.14 * outside;
                    bioLight *= 1.0 - 0.4 * outside;
                }

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
