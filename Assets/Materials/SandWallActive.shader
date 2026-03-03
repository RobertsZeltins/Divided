// Applied to the sand wall while a character is burrowing through it.
//
// The normal sand material is opaque (queue ~2000, ZWrite On).
// That opaque pass writes depth at ~33 units from camera; the character at
// ~35 units then fails ZTest and becomes invisible inside the wall.
//
// Swapping to THIS shader during burrowing fixes it:
//   • ZWrite Off  — no depth written, so the character's ZTest always passes.
//   • Transparent queue — renders after opaque geometry.
//   • Low alpha    — sand is visible but see-through, like Ori's glowing sand.
//   • _PulseSpeed  — optional brightness pulse for the "active burrowing" feel.

Shader "Custom/SandWallActive"
{
    Properties
    {
        _BaseColor   ("Sand Color",        Color)        = (0.82, 0.65, 0.28, 0.38)
        _GlowColor   ("Edge Glow Color",   Color)        = (1.00, 0.82, 0.30, 0.20)
        _MainTex     ("Sand Texture",      2D)           = "white" {}
        _PulseSpeed  ("Pulse Speed",       Range(0, 6))  = 2.5
        _PulseAmount ("Pulse Brightness",  Range(0, 0.4))= 0.15
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Transparent-20"   // 2980 — before the character sprite (3020)
            "RenderType"     = "Transparent"
        }

        Pass
        {
            Name "SandActive"

            ZWrite Off
            ZTest  LEqual
            Blend  SrcAlpha OneMinusSrcAlpha
            Cull   Off                            // visible from inside the sand wall too

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            CBUFFER_START(UnityPerMaterial)
                half4  _BaseColor;
                half4  _GlowColor;
                float4 _MainTex_ST;
                half   _PulseSpeed;
                half   _PulseAmount;
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
                OUT.uv          = TRANSFORM_TEX(IN.uv, _MainTex);
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                half4 tex  = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                half4 col  = tex * _BaseColor;

                // Brightness pulse — gives the sand a rhythmic "alive" shimmer
                // while the character burrows through it.
                half pulse = _PulseAmount * (0.5h + 0.5h * sin(_Time.y * _PulseSpeed));
                col.rgb   += _GlowColor.rgb * (_GlowColor.a + pulse);

                return col;
            }
            ENDHLSL
        }
    }
}
