// =============================================================================
//  StarlitTides/Vfx  -  the particle shader for every effect (VFX polish pass)
// -----------------------------------------------------------------------------
//  One shader, two blend modes (picked per material by VfxKit):
//    ADDITIVE  (Blend One One)                 light: sparks, glows, rings, flares
//    ALPHA     (Blend One OneMinusSrcAlpha,    matter: mist, spray, foam, rain
//               premultiplied)
//
//  What makes particles look finished instead of "dots on the screen":
//   * HOT CORE   the brightest part of each sprite is pushed toward white
//                (pow(alpha, k) * _CoreBoost), so glows have a white-hot centre
//                and a coloured falloff, like real light.
//   * SOFT       fades out where a particle meets the water or a rock (depth
//                difference), so nothing shows a hard cut line.
//   * NEAR FADE  fades out right in front of the camera (no screen-filling quads).
//   * FLIPBOOK   the particle system's texture-sheet animation just works (UVs).
//   * FOG        additive light fades with the fog; alpha particles mix into it.
// =============================================================================
Shader "StarlitTides/Vfx"
{
    Properties
    {
        _MainTex ("Sprite (white, alpha = shape)", 2D) = "white" {}
        [HDR] _TintColor ("Tint (HDR)", Color) = (1, 1, 1, 1)
        _CoreBoost ("Hot Core", Range(0, 6)) = 1.5
        _CorePower ("Core Tightness", Range(1, 16)) = 6
        _SoftFade ("Soft Edge Distance (m)", Range(0.01, 4)) = 0.6
        _NearFade ("Near Fade Distance (m)", Range(0, 6)) = 1.2
        [Toggle(_VFX_ALPHA)] _AlphaMode ("Alpha Blended (matter, not light)", Float) = 0
        [HideInInspector] _SrcBlend ("Src", Float) = 1
        [HideInInspector] _DstBlend ("Dst", Float) = 1
    }

    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Blend [_SrcBlend] [_DstBlend]
        ZWrite Off
        Cull Off

        Pass
        {
            Name "StarlitVfx"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #pragma shader_feature_local _VFX_ALPHA
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

            TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _TintColor;
                float  _CoreBoost, _CorePower, _SoftFade, _NearFade, _AlphaMode, _SrcBlend, _DstBlend;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
                float  eyeDepth   : TEXCOORD1;
                float  fogFactor  : TEXCOORD2;
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
                float nearFade = saturate((IN.eyeDepth - 0.3) / max(_NearFade, 0.01));
                float fade = soft * nearFade;

            #if defined(_VFX_ALPHA)
                // premultiplied alpha: rgb is already multiplied by alpha
                float a = tex.a * IN.color.a * _TintColor.a * fade;
                float3 rgb = tex.rgb * IN.color.rgb * _TintColor.rgb * a;
                rgb = lerp(unity_FogColor.rgb * a, rgb, ComputeFogIntensity(IN.fogFactor));
                return half4(rgb, a);
            #else
                float a = tex.a * IN.color.a;
                float3 col = tex.rgb * a * IN.color.rgb * _TintColor.rgb;
                // white-hot core: the densest part of the sprite goes toward white
                float core = pow(saturate(tex.a), _CorePower) * _CoreBoost * IN.color.a;
                col += core * dot(_TintColor.rgb * IN.color.rgb, float3(0.2126, 0.7152, 0.0722)) * 0.6;   // luma
                col *= fade * ComputeFogIntensity(IN.fogFactor);   // additive light fades in fog
                return half4(col, 0);
            #endif
            }
            ENDHLSL
        }
    }
    FallBack Off
}
