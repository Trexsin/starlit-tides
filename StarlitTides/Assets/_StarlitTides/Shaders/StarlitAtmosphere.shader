// STARLIT TIDES - Atmosphere (full-screen pass, before post-processing)
//
// The "air" of the scene: what makes distance feel like distance.
//   1. HEIGHT FOG: thick in a layer of sea mist, thinning with altitude. It is
//      integrated analytically along each view ray (exponential height fog), so
//      far water and the coast melt into haze while the sky stays clear.
//   2. MOON SCATTERING: the haze is brighter looking toward the moon (forward
//      "Mie" scattering), so the whole bay glows silver around it.
//   3. GOD RAYS: a screen-space radial blur of the bright sky toward the moon.
//      Clouds and the coastline cut the light into shafts.
// Driven by globals from SkyController (_Atmo*), so storms thicken the air and
// the intro sunset tints it.

Shader "Hidden/StarlitTides/Atmosphere"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" }
        ZWrite Off Cull Off ZTest Always Blend Off

        Pass
        {
            Name "StarlitAtmosphere"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _AtmoColor;        // haze colour (lit by the sky)
            float4 _AtmoMoonColor;    // scattered moonlight colour
            float4 _AtmoParams;       // x density, y height falloff, z start distance, w max fog
            float4 _AtmoRayParams;    // x ray strength, y brightness threshold, z decay, w enabled (0/1)
            float  _AtmoBaseHeight;
            float3 _MoonDirection;    // set by SkyController

            bool IsSky(float rawDepth)
            {
            #if UNITY_REVERSED_Z
                return rawDepth < 0.000001;
            #else
                return rawDepth > 0.999999;
            #endif
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half3 col = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
                if (_AtmoParams.x <= 0.0) return half4(col, 1);

                float rawDepth = SampleSceneDepth(uv);
                bool sky = IsSky(rawDepth);
            #if !UNITY_REVERSED_Z
                rawDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1, rawDepth);
            #endif
                float3 posWS = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
                float3 camPos = _WorldSpaceCameraPos;
                float3 ray = posWS - camPos;
                float dist = length(ray);
                float3 rd = ray / max(dist, 1e-4);
                if (sky) dist = 1600.0;

                // ---- exponential height fog, integrated along the ray ----
                float density = _AtmoParams.x, falloff = _AtmoParams.y;
                float h0 = camPos.y - _AtmoBaseHeight;
                float dy = rd.y * dist;
                float kdy = falloff * dy;
                float heightTerm = abs(kdy) > 1e-4 ? (1.0 - exp(-kdy)) / kdy : 1.0;
                float fogInt = density * exp(-falloff * h0) * dist * heightTerm;
                float fog = 1.0 - exp(-fogInt);
                fog *= smoothstep(_AtmoParams.z, _AtmoParams.z + 35.0, dist);   // keep the play area crisp
                fog = min(fog, _AtmoParams.w);

                // ---- forward scattering toward the moon ----
                float3 moonDir = normalize(_MoonDirection);
                float mu = saturate(dot(rd, moonDir));
                float mie = pow(mu, 8.0) * 0.55 + pow(mu, 60.0) * 1.4;
                float3 fogCol = _AtmoColor.rgb + _AtmoMoonColor.rgb * mie;
                col = lerp(col, fogCol, fog);

                // ---- god rays: march toward the moon on screen, gathering bright sky ----
                if (_AtmoRayParams.w > 0.5)
                {
                    float3 moonPos = camPos + moonDir * 1000.0;
                    float4 moonCS = TransformWorldToHClip(moonPos);
                    if (moonCS.w > 0.0)
                    {
                        float2 moonUV = ComputeNormalizedDeviceCoordinates(moonPos, UNITY_MATRIX_VP);
                        const int STEPS = 28;
                        float2 delta = (moonUV - uv) / STEPS;
                        float2 suv = uv;
                        float decay = 1.0, light = 0.0;
                        [loop] for (int i = 0; i < STEPS; i++)
                        {
                            suv += delta;
                            if (any(suv < 0.0) || any(suv > 1.0)) break;
                            float d = SampleSceneDepth(suv);
                            if (IsSky(d))
                            {
                                float l = Luminance(SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, suv).rgb);
                                light += max(l - _AtmoRayParams.y, 0.0) * decay;
                            }
                            decay *= _AtmoRayParams.z;
                        }
                        light /= STEPS;
                        float onScreen = saturate(1.2 - length(moonUV - 0.5) * 1.1);
                        col += _AtmoMoonColor.rgb * light * _AtmoRayParams.x * onScreen;
                    }
                }
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
