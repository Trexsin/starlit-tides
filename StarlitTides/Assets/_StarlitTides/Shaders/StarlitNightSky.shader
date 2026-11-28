// =============================================================================
//  STARLIT TIDES - Procedural Night Sky (skybox shader)
// -----------------------------------------------------------------------------
//  Everything is generated from the view direction. No textures, any resolution.
//    - night gradient (deep navy zenith -> purple -> glowing horizon)
//    - two layers of twinkling, coloured stars
//    - Milky Way band with dust lanes and a warm core
//    - large soft moon with craters, limb darkening and a halo
//    - animated AURORA curtains            (Extra Feature 4: aurora night)
//    - storm clouds lit by LIGHTNING        (Extra Feature 4: storm)
//    - SUNSET colours for the intro          (Extra Feature 6: day-to-night)
//    - horizon blends into the scene fog so mountains melt into the sky
//
//  SkyController.cs sets the globals: _MoonDirection, _SunDirection,
//  _SunsetBlend, _StormAmount, _AuroraAmount, _LightningFlash, _SkyTime
// =============================================================================
Shader "Skybox/StarlitNight"
{
    Properties
    {
        [Header(Night Gradient)]
        _ZenithColor  ("Zenith Colour", Color) = (0.012, 0.016, 0.06, 1)
        _MidColor     ("Mid Sky Colour", Color) = (0.05, 0.035, 0.14, 1)
        _HorizonColor ("Horizon Colour", Color) = (0.16, 0.10, 0.30, 1)
        _HorizonGlow  ("Horizon Glow Colour", Color) = (0.05, 0.22, 0.30, 1)
        _GroundColor  ("Below Horizon Colour", Color) = (0.01, 0.012, 0.03, 1)
        _HorizonFogStrength ("Blend Horizon Into Fog", Range(0, 1)) = 0.85
        _HorizonFogHeight ("Horizon Fog Height", Range(0.01, 0.5)) = 0.12

        [Header(Stars)]
        _StarDensity  ("Star Density", Range(0, 1)) = 0.55
        _StarBrightness ("Star Brightness", Range(0, 10)) = 2.5
        _TwinkleSpeed ("Twinkle Speed", Range(0, 10)) = 2.2

        [Header(Milky Way)]
        _GalaxyNormal ("Galaxy Plane Normal", Vector) = (0.10, -0.86, 0.50, 0)
        _GalaxyWidth  ("Galaxy Width", Range(0.02, 0.6)) = 0.16
        _GalaxyIntensity ("Galaxy Intensity", Range(0, 4)) = 1.1
        _GalaxyColorA ("Galaxy Colour A", Color) = (0.30, 0.20, 0.55, 1)
        _GalaxyColorB ("Galaxy Colour B", Color) = (0.15, 0.45, 0.60, 1)
        _GalaxyCore   ("Galaxy Core Colour", Color) = (1.0, 0.75, 0.55, 1)

        [Header(Moon)]
        _MoonSize     ("Moon Size (radians)", Range(0.005, 0.15)) = 0.04
        [HDR] _MoonColor ("Moon Colour", Color) = (3.2, 3.1, 2.8, 1)
        _MoonHaloColor ("Moon Halo Colour", Color) = (0.35, 0.45, 0.8, 1)
        _MoonHalo     ("Moon Halo Strength", Range(0, 3)) = 0.9

        [Header(Aurora)]
        [HDR] _AuroraColorA ("Aurora Lower Colour", Color) = (0.1, 2.2, 1.2, 1)
        [HDR] _AuroraColorB ("Aurora Upper Colour", Color) = (1.4, 0.3, 2.0, 1)
        _AuroraDistance ("Aurora Distance", Range(1, 8)) = 3.2

        [Header(Night Clouds)]
        _NightCloudCover ("Night Cloud Cover", Range(0, 1)) = 0.45
        _NightCloudColor ("Night Cloud Colour", Color) = (0.04, 0.045, 0.09, 1)
        [HDR] _CloudLiningColor ("Moonlit Silver Lining", Color) = (1.3, 1.4, 1.75, 1)

        [Header(Storm)]
        _CloudColor   ("Storm Cloud Colour", Color) = (0.035, 0.04, 0.06, 1)
        [HDR] _LightningColor ("Lightning Colour", Color) = (2.5, 2.8, 4.0, 1)

        [Header(Sunset  intro only)]
        _SunsetHorizon ("Sunset Horizon", Color) = (1.0, 0.42, 0.18, 1)
        _SunsetMid     ("Sunset Mid", Color) = (0.75, 0.32, 0.45, 1)
        _SunsetZenith  ("Sunset Zenith", Color) = (0.16, 0.22, 0.52, 1)
        [HDR] _SunColor ("Sun Colour", Color) = (8, 5, 2.5, 1)
    }

    SubShader
    {
        Tags { "Queue" = "Background" "RenderType" = "Background" "PreviewType" = "Skybox" "RenderPipeline" = "UniversalPipeline" }
        Cull Off
        ZWrite Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex SkyVert
            #pragma fragment SkyFrag
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #pragma multi_compile _ STARLIT_QUALITY_LOW   // set by SettingsManager on Low
            #include "StarlitNoise.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ZenithColor, _MidColor, _HorizonColor, _HorizonGlow, _GroundColor;
                float  _HorizonFogStrength, _HorizonFogHeight;
                float  _StarDensity, _StarBrightness, _TwinkleSpeed;
                float4 _GalaxyNormal;
                float  _GalaxyWidth, _GalaxyIntensity;
                float4 _GalaxyColorA, _GalaxyColorB, _GalaxyCore;
                float  _MoonSize;
                float4 _MoonColor, _MoonHaloColor;
                float  _MoonHalo;
                float4 _AuroraColorA, _AuroraColorB;
                float  _AuroraDistance;
                float4 _CloudColor, _LightningColor;
                float  _NightCloudCover;
                float4 _NightCloudColor, _CloudLiningColor;
                float4 _SunsetHorizon, _SunsetMid, _SunsetZenith, _SunColor;
            CBUFFER_END

            // Globals from SkyController.cs
            float3 _MoonDirection;   // direction TO the moon
            float3 _SunDirection;    // direction TO the sun (intro)
            float  _SunsetBlend;     // 0 = night, 1 = sunset
            float  _StormAmount;     // 0..1
            float  _AuroraAmount;    // 0..1
            float  _LightningFlash;  // 0..1
            float  _SkyTime;

            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings   { float4 positionCS : SV_POSITION; float3 dir : TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };

            Varyings SkyVert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.dir = IN.positionOS.xyz;   // skybox cube: object position = view direction
                return OUT;
            }

            // One layer of stars on a 3D grid of cells around the sky sphere.
            float3 StarLayer(float3 dir, float scale, float density, float size, float seed)
            {
                float3 p = dir * scale;
                float3 cell = floor(p);
                float3 h = SN_Hash33(cell + seed);
                if (h.x > density) return 0;
                float3 starPos = 0.25 + 0.5 * SN_Hash33(cell + seed + 17.0);
                float d = length(frac(p) - starPos);
                float core = saturate(1.0 - d / size);
                core = core * core * core;
                float twinkle = 0.55 + 0.45 * sin(_SkyTime * _TwinkleSpeed * (0.5 + h.y) + h.z * 60.0);
                // star colours: mostly white, some blue, some warm gold
                float3 tint = lerp(float3(0.75, 0.85, 1.0), float3(1.0, 0.85, 0.6), step(0.7, h.y));
                tint = lerp(tint, float3(0.6, 0.8, 1.0), step(0.9, h.z));
                return tint * core * twinkle * (0.4 + h.z * 1.6);
            }

            // Milky Way: a soft band around a great circle, broken up by noise.
            float3 MilkyWay(float3 dir, out float bandMask)
            {
                float3 n = normalize(_GalaxyNormal.xyz);
                float dist = dot(dir, n);
                float band = exp(-(dist * dist) / (_GalaxyWidth * _GalaxyWidth));
                bandMask = band;
                // Phase 9 (performance): outside the band the noise below is invisible.
                if (band < 0.004) return 0;
                float clouds = SN_Fbm3(dir * 4.0, 4);
                float dust = SN_Fbm3(dir * 9.0 + 3.1, 3);
                float lane = smoothstep(0.45, 0.65, dust) * exp(-(dist * dist) / (_GalaxyWidth * _GalaxyWidth * 0.12));
                float m = band * smoothstep(0.25, 0.85, clouds);
                m *= 1.0 - lane * 0.85;   // dark dust lanes through the middle
                bandMask = band;
                float3 col = lerp(_GalaxyColorA.rgb, _GalaxyColorB.rgb, SN_Fbm3(dir * 2.0 + 7.0, 2));
                // warm core towards one side of the band
                float3 coreDir = normalize(cross(n, float3(1, 0, 0)));
                float core = pow(saturate(dot(dir, coreDir)), 6.0) * band;
                col += _GalaxyCore.rgb * core * 0.8;
                return col * m * _GalaxyIntensity;
            }

            // Moon disc with craters and halo. Returns colour, sets disc mask.
            float3 Moon(float3 dir, float3 moonDir, out float discMask)
            {
                float d = dot(dir, moonDir);
                float cosR = cos(_MoonSize);
                discMask = smoothstep(cosR - 0.00008, cosR + 0.00008, d);
                // two-part halo: tight bright glow + wide soft scatter
                float halo = pow(saturate(d), 3000.0) * 1.2 + pow(saturate(d), 120.0) * 0.35 + pow(saturate(d), 12.0) * 0.08;
                float3 haloCol = _MoonHaloColor.rgb * halo * _MoonHalo * (1.0 - discMask);
                // Phase 9 (performance): the crater noise only matters on the disc itself.
                if (discMask <= 0.0) return haloCol;

                // local 2D coordinates on the moon disc (-1..1)
                float3 up = abs(moonDir.y) < 0.99 ? float3(0, 1, 0) : float3(1, 0, 0);
                float3 t = normalize(cross(up, moonDir));
                float3 b = cross(moonDir, t);
                float2 uv = float2(dot(dir, t), dot(dir, b)) / sin(_MoonSize);
                float r2 = saturate(dot(uv, uv));
                float limb = sqrt(1.0 - r2 * 0.85);                         // limb darkening
                float maria = smoothstep(0.45, 0.7, SN_Fbm2(uv * 2.2 + 4.0, 4)); // dark "seas"
                float craters = SN_Fbm2(uv * 9.0, 3);
                float shade = limb * (1.0 - maria * 0.35) * (0.85 + craters * 0.25);
                float3 disc = _MoonColor.rgb * shade * discMask;
                return disc + haloCol;
            }

            // Aurora: stacked horizontal slices of a wavy ribbon. Each slice is
            // a little higher, so together they form glowing vertical curtains
            // with vertical "rays", green at the bottom and violet at the top.
            float3 Aurora(float3 dir)
            {
                float3 acc = 0;
                if (dir.y < 0.01) return acc;
                // Phase 9 (performance): 12 slices (8 on Low) instead of 20.
            #if defined(STARLIT_QUALITY_LOW)
                const int STEPS = 8;
            #else
                const int STEPS = 12;
            #endif
                [loop] for (int i = 0; i < STEPS; i++)
                {
                    float fi = i / (float)(STEPS - 1);
                    float h = 1.0 + fi * 0.9;                    // slice height
                    float2 p = dir.xz / dir.y * h * 0.32;        // project onto the slice
                    float t = _SkyTime;
                    float wave = sin(p.x * 0.8 + t * 0.12) * 0.9
                               + sin(p.x * 2.3 - t * 0.21) * 0.35
                               + (SN_Noise2(float2(p.x * 0.5, t * 0.04)) - 0.5) * 2.2;
                    float distToRibbon = p.y - _AuroraDistance - wave;
                    float ribbon = exp(-distToRibbon * distToRibbon * 2.5);
                    float rays = 0.35 + 0.65 * SN_Noise2(float2(p.x * 9.0 + t * 0.25, fi * 1.5 + t * 0.05));
                    float3 c = lerp(_AuroraColorA.rgb, _AuroraColorB.rgb, smoothstep(0.15, 0.95, fi));
                    acc += c * ribbon * rays * (1.0 - fi * 0.7);
                }
                acc /= STEPS;
                acc *= smoothstep(0.0, 0.12, dir.y) * smoothstep(0.95, 0.35, dir.y);
                return acc * 2.5;
            }

            half4 SkyFrag(Varyings IN) : SV_Target
            {
                float3 dir = normalize(IN.dir);
                float y = dir.y;

                // ---------- night gradient ----------
                float3 sky = lerp(_HorizonColor.rgb, _MidColor.rgb, smoothstep(0.0, 0.25, y));
                sky = lerp(sky, _ZenithColor.rgb, smoothstep(0.2, 0.85, y));
                sky += _HorizonGlow.rgb * exp(-abs(y) * 14.0);           // thin glowing band at the horizon
                if (y < 0) sky = lerp(_HorizonColor.rgb * 0.6, _GroundColor.rgb, smoothstep(0.0, 0.2, -y));

                // Phase 9 (performance): the ocean is see-through (refraction), so the sky is
                // drawn under the whole sea too, on most of the screen. Below the horizon
                // every layer (stars, Milky Way, aurora, clouds) has faded to zero, so give
                // the same result without computing them. (Measured: the sky cost ~12 ms a
                // frame at 2940x1846 on a MacBook Air before this.)
                if (y < -0.05)
                {
                    float3 below = lerp(sky, sky * 0.45, _StormAmount);
                    if (_SunsetBlend > 0.001)
                        below = lerp(below, lerp(_SunsetHorizon.rgb * 0.4, _GroundColor.rgb * 3.0, smoothstep(0.0, 0.25, -y)), _SunsetBlend);
                    below += _LightningColor.rgb * _LightningFlash * 0.06 * _StormAmount;
                    float belowFog = (1.0 - smoothstep(0.0, _HorizonFogHeight, abs(y))) * _HorizonFogStrength;
                    below = lerp(below, unity_FogColor.rgb, belowFog * (1.0 - _SunsetBlend * 0.6));
                    return half4(below, 1);
                }

                // ---------- storm clouds (computed early: they hide stars) ----------
                float cloudCover = 0;
                float3 cloudCol = 0;
                if (_StormAmount > 0.001)
                {
                    float2 cuv = dir.xz / (max(y, 0.0) + 0.12);
                    float cn = SN_Fbm2(cuv * 0.9 + float2(_SkyTime * 0.05, _SkyTime * 0.02), 4);   // Phase 9: 4 octaves (was 5)
                    cloudCover = smoothstep(0.62 - _StormAmount * 0.45, 0.85 - _StormAmount * 0.25, cn) * _StormAmount;
                    cloudCover *= smoothstep(-0.05, 0.08, y);
                    cloudCol = _CloudColor.rgb * (0.6 + cn * 0.8);
                    // Phase 9 (performance): the flash shape only matters during a flash.
                    if (_LightningFlash > 0.002)
                        cloudCol += _LightningColor.rgb * _LightningFlash * SN_Fbm2(cuv * 0.5 + 11.0, 3) * (0.4 + cn);
                    sky = lerp(sky, sky * 0.45, _StormAmount);            // darker, heavier sky
                }

                // ---------- night clouds: drifting layers with moonlit silver linings ----------
                // Projected on a dome, so they bunch up into soft banks at the horizon.
                float nc = 0;
                float3 ncCol = 0;
                if (_NightCloudCover > 0.001 && y > -0.02)
                {
                    float2 cuv = dir.xz / (max(y, 0.0) + 0.18);
                    float2 wind = float2(_SkyTime * 0.012, _SkyTime * 0.004);
                    float n = SN_Fbm2(cuv * 0.45 + wind, 4);              // Phase 9: 4 + 2 octaves (was 5 + 3)
                    float detail = SN_Fbm2(cuv * 1.7 - wind * 2.0, 2);
                    float shape = n + (detail - 0.5) * 0.25;
                    float lo = 0.62 - _NightCloudCover * 0.35;
                    nc = smoothstep(lo, lo + 0.24, shape) * smoothstep(-0.02, 0.14, y) * (1.0 - _SunsetBlend * 0.5);
                    float toMoon = saturate(dot(dir, normalize(_MoonDirection)));
                    float thin = 1.0 - saturate((shape - lo) * 3.5);          // cloud edges are thin
                    float3 cbase = _NightCloudColor.rgb * (0.7 + 0.6 * detail);
                    // Silver linings: thin cloud edges near the moon light up; thick cores stay dark.
                    float3 lining = _CloudLiningColor.rgb * (pow(toMoon, 60.0) * 1.3 + pow(toMoon, 10.0) * 0.18 + 0.02) * thin;
                    ncCol = cbase + lining;
                }

                // ---------- stars + Milky Way ----------
                float bandMask;
                float3 galaxy = MilkyWay(dir, bandMask);
                float starVis = saturate(y * 6.0 + 0.2) * (1.0 - _SunsetBlend);
                float3 stars = StarLayer(dir, 190.0, _StarDensity * (0.35 + bandMask * 0.65), 0.28, 0.0) * 0.6
                             + StarLayer(dir, 75.0, _StarDensity * 0.35, 0.18, 51.0) * 1.6
                             + StarLayer(dir, 330.0, bandMask * _StarDensity, 0.3, 97.0) * 0.4;  // dense stars inside the band
                float3 night = sky + (stars * _StarBrightness + galaxy) * starVis * (1.0 - nc);

                // ---------- aurora ----------
                if (_AuroraAmount > 0.001)
                    night += Aurora(dir) * _AuroraAmount * (1.0 - cloudCover) * (1.0 - nc * 0.75);

                // ---------- moon ----------
                float disc;
                float3 moon = Moon(dir, normalize(_MoonDirection), disc);
                night = lerp(night, 0, disc);               // the moon hides the stars behind it
                night += moon * (1.0 - _SunsetBlend * 0.7) * (1.0 - nc * 0.7);   // thin clouds veil the moon
                night = lerp(night, ncCol, nc * 0.9);
                night += ncCol * nc * 0.1;

                // ---------- sunset (intro cinematic) ----------
                float3 col = night;
                if (_SunsetBlend > 0.001)
                {
                    float3 sunDir = normalize(_SunDirection);
                    float sunAmount = saturate(dot(dir, sunDir));
                    float3 dusk = lerp(_SunsetHorizon.rgb, _SunsetMid.rgb, smoothstep(0.0, 0.2, y));
                    dusk = lerp(dusk, _SunsetZenith.rgb, smoothstep(0.15, 0.7, y));
                    dusk += _SunsetHorizon.rgb * pow(sunAmount, 8.0) * 0.8;          // glow around the sun
                    dusk += _SunColor.rgb * smoothstep(0.9993, 0.9997, sunAmount);    // sun disc
                    if (y < 0) dusk = lerp(_SunsetHorizon.rgb * 0.4, _GroundColor.rgb * 3.0, smoothstep(0.0, 0.25, -y));
                    col = lerp(night, dusk, _SunsetBlend);
                }

                // ---------- clouds over everything ----------
                col = lerp(col, cloudCol, cloudCover);
                col += _LightningColor.rgb * _LightningFlash * 0.06 * _StormAmount;   // whole sky flickers

                // ---------- melt the horizon into the scene fog ----------
                float horizonFog = (1.0 - smoothstep(0.0, _HorizonFogHeight, abs(y))) * _HorizonFogStrength;
                col = lerp(col, unity_FogColor.rgb, horizonFog * (1.0 - _SunsetBlend * 0.6));

                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
