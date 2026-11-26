// =============================================================================
//  STARLIT TIDES - Additive glow particle
// -----------------------------------------------------------------------------
//  Tiny, fast shader for every glowing particle in the game: fireflies,
//  plankton motes, orb bursts, sparkles, shooting stars.
//  colour = texture x particle colour x HDR tint, added on top of the scene,
//  so overlapping particles get brighter (and Bloom makes them glow).
//  Soft-particle fade stops hard edges where particles touch the water.
// =============================================================================
Shader "StarlitTides/GlowParticle"
{
    Properties
    {
        _MainTex ("Particle Texture", 2D) = "white" {}
        [HDR] _TintColor ("Tint (HDR)", Color) = (1, 1, 1, 1)
        _SoftFade ("Soft Particle Distance", Range(0.01, 3)) = 0.5
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Blend One One
        ZWrite Off
        Cull Off

        Pass
        {
            Name "Glow"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _TintColor;
                float _SoftFade;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
                float eyeDepth : TEXCOORD1;
                float fogFactor : TEXCOORD2;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                float3 posWS = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionCS = TransformWorldToHClip(posWS);
                OUT.eyeDepth = -TransformWorldToView(posWS).z;
                OUT.color = IN.color;
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.fogFactor = ComputeFogFactor(OUT.positionCS.z);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                float2 screenUV = GetNormalizedScreenSpaceUV(IN.positionCS);
                float sceneEye = LinearEyeDepth(SampleSceneDepth(screenUV), _ZBufferParams);
                float soft = saturate((sceneEye - IN.eyeDepth) / _SoftFade);
                float3 col = tex.rgb * tex.a * IN.color.rgb * IN.color.a * _TintColor.rgb * soft;
                col *= ComputeFogIntensity(IN.fogFactor);   // additive: fade to black in fog
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
    FallBack Off
}
