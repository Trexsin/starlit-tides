// =============================================================================
//  STARLIT TIDES - Fake volumetric light beam (lighthouse, god rays)
// -----------------------------------------------------------------------------
//  URP has no built-in volumetric lighting, so we fake it: an open cone mesh
//  drawn with ADDITIVE blending.  The beam looks like light hitting dusty air:
//    - bright at the source, fading along its length       (uv.y)
//    - soft edges: faces seen edge-on fade out             (Fresnel / N.V)
//    - soft where it touches geometry                      (depth fade)
//    - drifting dust / mist inside the beam                (scrolling noise)
//  Works with any cone mesh whose UV.y goes 0 (apex) -> 1 (wide end).
// =============================================================================
Shader "StarlitTides/LightBeam"
{
    Properties
    {
        [HDR] _Color ("Beam Colour", Color) = (2.0, 1.6, 0.9, 1)
        _Intensity ("Intensity", Range(0, 5)) = 1
        _LengthFalloff ("Length Falloff", Range(0.2, 8)) = 1.6
        _EdgeSoftness ("Edge Softness", Range(0.5, 8)) = 2.5
        _DepthFade ("Soft Intersection Distance", Range(0.01, 10)) = 2
        _DustScale ("Dust Scale", Float) = 0.15
        _DustSpeed ("Dust Speed", Float) = 0.4
        _DustAmount ("Dust Amount", Range(0, 1)) = 0.45
        _CameraFade ("Fade When Camera Is Inside (m)", Float) = 4
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Beam"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex BeamVert
            #pragma fragment BeamFrag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "StarlitNoise.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float _Intensity, _LengthFalloff, _EdgeSoftness, _DepthFade;
                float _DustScale, _DustSpeed, _DustAmount, _CameraFade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS   : TEXCOORD1;
                float2 uv         : TEXCOORD2;
                float  eyeDepth   : TEXCOORD3;
                float  fogFactor  : TEXCOORD4;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings BeamVert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.positionCS = TransformWorldToHClip(OUT.positionWS);
                OUT.uv = IN.uv;
                OUT.eyeDepth = -TransformWorldToView(OUT.positionWS).z;
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 BeamFrag(Varyings IN) : SV_Target
            {
                float3 V = normalize(GetWorldSpaceViewDir(IN.positionWS));
                float3 N = normalize(IN.normalWS);

                float along = pow(saturate(1.0 - IN.uv.y), _LengthFalloff);          // fades toward the far end
                float edge = pow(abs(dot(N, V)), _EdgeSoftness);                      // soft silhouette

                float2 screenUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float soft = saturate((sceneEye - IN.eyeDepth) / _DepthFade);         // soft where it hits things
                float camFade = saturate(IN.eyeDepth / _CameraFade);                  // don't blind the camera

                float dust = SN_Fbm3(IN.positionWS * _DustScale + float3(0, -_Time.y * _DustSpeed, _Time.y * _DustSpeed * 0.5), 3);
                float dustMask = lerp(1.0, 0.4 + dust * 1.2, _DustAmount);

                float a = along * edge * soft * camFade * dustMask * _Intensity;
                float3 col = _Color.rgb * a;
                // Additive blending: fog should fade the beam to black, not to fog colour.
                col *= ComputeFogIntensity(IN.fogFactor);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
