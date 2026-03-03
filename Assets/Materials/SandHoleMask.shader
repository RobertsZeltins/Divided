// Renders NOTHING to the colour buffer but writes stencil = 1 at every covered pixel.
// Placed on the HoleCap disk and the HoleTrail TrailRenderer.
//
// Rendering order
//   Queue 1999 (Geometry-1)  →  this mask runs
//   Queue 2000 (Geometry)    →  SandWallWithHole skips stencil-1 pixels
//   Queue 3020+              →  character / drill render into the now-empty hole
//
// ZTest Always: writes stencil even though the hole mesh sits behind the sand's
// front face in world space. Without this the mask would fail the depth test
// and the sand would never be punched through.

Shader "Custom/SandHoleMask"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue"          = "Geometry-1"   // = 1999
            "RenderType"     = "Opaque"
        }

        Pass
        {
            Name "HoleMaskWrite"

            ZWrite   Off         // only stencil matters; don't pollute the depth buffer
            ZTest    Always      // write stencil regardless of what is in front
            ColorMask 0          // output nothing to colour buffer

            Stencil
            {
                Ref       1
                Comp      Always
                Pass      Replace  // stamp 1 wherever this mesh covers
            }

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            // Fragment does nothing; stencil write happens in fixed-function hardware.
            half4 Frag(Varyings IN) : SV_Target { return 0; }
            ENDHLSL
        }
    }
}
