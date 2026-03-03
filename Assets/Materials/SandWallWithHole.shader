// Replaces the sand wall's normal opaque material during all gameplay.
//
// Behaviour
//   • Where stencil == 0 (no hole mask):  renders fully-opaque sand, writes depth.
//   • Where stencil == 1 (hole punched by SandHoleMask):  fragment is SKIPPED
//     entirely — no colour, no depth written.  The camera sees straight through
//     to whatever is rendered behind the sand wall.
//
// Because depth is not written in hole pixels the character's ZTest LEqual passes
// there (background depth >> character depth), making the character visible only
// inside the hole.  Outside the hole the sand's depth (≈ 32.9) blocks the character
// (character depth ≈ 35 fails ZTest LEqual).

Shader "Custom/SandWallWithHole"
{
    Properties
    {
        _BaseColor ("Sand Color",    Color) = (0.82, 0.65, 0.28, 1)
        _BaseMap   ("Sand Texture",  2D)    = "white" {}
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Geometry"   // = 2000, after the hole mask at 1999
            "RenderType"     = "Opaque"
        }

        Pass
        {
            Name "SandWithHole"

            ZWrite On
            ZTest  LEqual
            Cull   Back

            Stencil
            {
                Ref  1
                Comp NotEqual   // skip pixels where stencil == 1  (= inside the hole)
                Pass Keep       // don't alter the stencil value
            }

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                half4  _BaseColor;
                float4 _BaseMap_ST;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv          = TRANSFORM_TEX(IN.uv, _BaseMap);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;
            }
            ENDHLSL
        }
    }
}
