// Punches fully transparent holes in the sand wall while a character burrows.
// Hole data is passed via _HoleMap: a 64x1 RGBAFloat texture where each pixel
// encodes one hole as (worldX, worldY, radius, unused). Pixels with radius < 0.001
// are inactive. The rest of the sand renders normally (fully opaque).

Shader "Custom/BurrowHole"
{
    Properties
    {
        _BaseColor ("Sand Color",   Color) = (0.82, 0.65, 0.28, 1)
        _MainTex   ("Sand Texture", 2D)   = "white" {}
        _HoleMap   ("Hole Data Map", 2D)  = "black" {}
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Geometry"
            "RenderType"     = "Opaque"
        }

        Pass
        {
            Name "BurrowHole"
            ZWrite On
            ZTest  LEqual
            Cull   Back

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);  SAMPLER(sampler_MainTex);
            TEXTURE2D(_HoleMap);  SAMPLER(sampler_HoleMap);

            CBUFFER_START(UnityPerMaterial)
                half4  _BaseColor;
                float4 _MainTex_ST;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float2 worldXY     : TEXCOORD1;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                float3 wp       = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(wp);
                OUT.uv          = TRANSFORM_TEX(IN.uv, _MainTex);
                OUT.worldXY     = wp.xy;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float2 pos = IN.worldXY;

                // Sample the 64x1 hole map. Each texel: rg = world XY, b = radius.
                [loop]
                for (int i = 0; i < 64; i++)
                {
                    float  u = (i + 0.5) / 64.0;
                    float4 h = SAMPLE_TEXTURE2D_LOD(_HoleMap, sampler_HoleMap, float2(u, 0.5), 0);
                    if (h.b < 0.001) continue;
                    float2 d = pos - h.rg;
                    if (dot(d, d) < h.b * h.b) discard;
                }

                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                return tex * _BaseColor;
            }
            ENDHLSL
        }
    }
}
