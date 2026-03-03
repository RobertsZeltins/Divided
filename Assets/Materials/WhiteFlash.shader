Shader "Custom/WhiteFlash"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
    }
    SubShader
    {
        Tags
        {
            "Queue"           = "Transparent"
            "RenderType"      = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
            "IgnoreProjector" = "True"
        }

        // SrcAlpha One = additive with alpha masking.
        // Adds white on top of whatever is beneath — always visible, character gets
        // brighter/whiter. Never invisible because addition can only increase pixel values.
        Blend SrcAlpha One
        ZWrite Off
        Cull Off
        Lighting Off

        Pass
        {
            HLSLPROGRAM
            #pragma vertex   vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : COLOR;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float4 color       : COLOR;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                // Raw UV — SpriteRenderer bakes atlas UVs into mesh vertices directly.
                // TRANSFORM_TEX would apply _MainTex_ST on top of already-correct UVs,
                // potentially zeroing them out and breaking the alpha lookup.
                OUT.uv    = IN.uv;
                OUT.color = IN.color;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half texAlpha = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv).a;
                half amount   = texAlpha * IN.color.a; // vertex alpha driven by sr.color.a
                return half4(1.0h, 1.0h, 1.0h, amount);
            }
            ENDHLSL
        }
    }
}
