// Unlit URP shader used by the burrowing drill visual and its sand trail.
//
// ZTest Always ensures the drill is drawn on screen even when the opaque
// sand wall has already written a closer depth value at those pixels.
// Without this the character is completely invisible inside the sand.
//
// ZWrite Off so the drill does not corrupt the depth buffer for objects
// that should legitimately be hidden behind it.

Shader "Custom/DrillCharacter"
{
    Properties
    {
        _BaseColor ("Color", Color)           = (1, 0.87, 0.3, 1)
        _EdgeGlow  ("Brightness Multiplier",  Range(0.5, 3)) = 1.4
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Transparent+30"   // after water (3010) and character fix (3020)
            "RenderType"     = "Transparent"
        }

        Pass
        {
            Name "DrillUnlit"

            ZTest  LEqual                        // visible only inside the hole (where sand wrote no depth)
            ZWrite Off
            Cull   Off                           // visible from both sides of the flat mesh
            Blend  SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half  _EdgeGlow;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4  color      : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                half4  color       : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.color       = IN.color;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                // Vertex colour lets the TrailRenderer's gradient drive transparency.
                return _BaseColor * _EdgeGlow * IN.color;
            }
            ENDHLSL
        }
    }
}
