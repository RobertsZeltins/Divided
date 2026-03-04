// Punches fully transparent holes in the sand wall while a character burrows.
// Uses URP PBR lighting so the sand matches the original URP Lit material exactly —
// same normal map, AO, smoothness, and metallic response.
// Hole data is passed via _HoleMap: a 64x1 RGBAFloat texture where each pixel
// encodes one hole as (worldX, worldY, radius, unused).

Shader "Custom/BurrowHole"
{
    Properties
    {
        _BaseColor         ("Sand Color",        Color)       = (0.82, 0.65, 0.28, 1)
        _BaseMap           ("Sand Texture",       2D)         = "white" {}
        _BumpMap           ("Normal Map",         2D)         = "bump"  {}
        _BumpScale         ("Normal Strength",    Float)      = 1.0
        _Smoothness        ("Smoothness",         Range(0,1)) = 0.0
        _Metallic          ("Metallic",           Range(0,1)) = 0.0
        _OcclusionMap      ("Occlusion",          2D)         = "white" {}
        _OcclusionStrength ("Occlusion Strength", Range(0,1)) = 1.0
        _HoleMap           ("Hole Data Map",      2D)         = "black" {}
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
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }
            ZWrite On
            ZTest  LEqual
            Cull   Back

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag

            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile _ _SHADOWS_SOFT
            #pragma multi_compile _ DIRLIGHTMAP_COMBINED
            #pragma multi_compile _ LIGHTMAP_ON
            #pragma multi_compile_fog

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);      SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);      SAMPLER(sampler_BumpMap);
            TEXTURE2D(_OcclusionMap); SAMPLER(sampler_OcclusionMap);
            TEXTURE2D(_HoleMap);      SAMPLER(sampler_HoleMap);

            CBUFFER_START(UnityPerMaterial)
                half4  _BaseColor;
                float4 _BaseMap_ST;
                float  _BumpScale;
                half   _Smoothness;
                half   _Metallic;
                half   _OcclusionStrength;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS       : POSITION;
                float3 normalOS         : NORMAL;
                float4 tangentOS        : TANGENT;
                float2 uv               : TEXCOORD0;
                float2 staticLightmapUV : TEXCOORD1;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
                float2 worldXY     : TEXCOORD1;
                float3 positionWS  : TEXCOORD2;
                float3 normalWS    : TEXCOORD3;
                float3 tangentWS   : TEXCOORD4;
                float3 bitangentWS : TEXCOORD5;
                // Handles either lightmap UV or per-vertex SH depending on what
                // is active — mirrors exactly what URP Lit does for static meshes.
                DECLARE_LIGHTMAP_OR_SH(staticLightmapUV, vertexSH, 6);
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs posInputs  = GetVertexPositionInputs(IN.positionOS.xyz);
                VertexNormalInputs   normInputs = GetVertexNormalInputs(IN.normalOS, IN.tangentOS);

                OUT.positionHCS = posInputs.positionCS;
                OUT.positionWS  = posInputs.positionWS;
                OUT.uv          = TRANSFORM_TEX(IN.uv, _BaseMap);
                OUT.worldXY     = posInputs.positionWS.xy;
                OUT.normalWS    = normInputs.normalWS;
                OUT.tangentWS   = normInputs.tangentWS;
                OUT.bitangentWS = normInputs.bitangentWS;

                // Write lightmap UV or bake per-vertex SH — same as URP Lit.
                OUTPUT_LIGHTMAP_UV(IN.staticLightmapUV, unity_LightmapST, OUT.staticLightmapUV);
                OUTPUT_SH(normInputs.normalWS, OUT.vertexSH);

                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                // ── Hole punch ────────────────────────────────────────────────
                float2 pos = IN.worldXY;
                [loop]
                for (int i = 0; i < 64; i++)
                {
                    float  u = (i + 0.5) / 64.0;
                    float4 h = SAMPLE_TEXTURE2D_LOD(_HoleMap, sampler_HoleMap, float2(u, 0.5), 0);
                    if (h.b < 0.001) continue;
                    float2 d = pos - h.rg;
                    if (dot(d, d) < h.b * h.b) discard;
                }

                // ── Surface data ──────────────────────────────────────────────
                half4 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, IN.uv) * _BaseColor;

                half4 normalSample = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, IN.uv);
                half3 normalTS     = UnpackNormalScale(normalSample, _BumpScale);
                half3 normalWS     = normalize(TransformTangentToWorld(
                                        normalTS,
                                        half3x3(IN.tangentWS, IN.bitangentWS, IN.normalWS)));

                half ao = lerp(1.0h,
                               SAMPLE_TEXTURE2D(_OcclusionMap, sampler_OcclusionMap, IN.uv).r,
                               _OcclusionStrength);

                // ── URP PBR lighting ──────────────────────────────────────────
                InputData inputData = (InputData)0;
                inputData.positionWS              = IN.positionWS;
                inputData.normalWS                = normalWS;
                inputData.viewDirectionWS         = GetWorldSpaceNormalizeViewDir(IN.positionWS);
                inputData.shadowCoord             = TransformWorldToShadowCoord(IN.positionWS);
                inputData.fogCoord                = 0;
                inputData.vertexLighting          = half3(0, 0, 0);
                inputData.bakedGI                 = SAMPLE_GI(IN.staticLightmapUV, IN.vertexSH, normalWS);
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(IN.positionHCS);
                inputData.shadowMask              = half4(1, 1, 1, 1);

                SurfaceData surfaceData = (SurfaceData)0;
                surfaceData.albedo      = albedo.rgb;
                surfaceData.metallic    = _Metallic;
                surfaceData.smoothness  = _Smoothness;
                surfaceData.occlusion   = ao;
                surfaceData.normalTS    = normalTS;
                surfaceData.alpha       = 1.0h;

                return UniversalFragmentPBR(inputData, surfaceData);
            }
            ENDHLSL
        }

        // Shadow caster and depth passes — reuse URP Lit's implementations.
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
}
