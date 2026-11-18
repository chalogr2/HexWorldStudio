Shader "Hex World Studio/Tile Highlight"
{
    // Flat translucent tint drawn over a tile's own meshes to mark hover
    // and selection. Unlit, no textures: the color and its alpha are the
    // whole effect. Depth offset pulls it just in front of the surface it
    // copies so the two never z-fight; depth test stays on so only the
    // surfaces you can actually see get tinted.
    Properties
    {
        _BaseColor ("Color", Color) = (1, 1, 1, 0.3)
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ForwardUnlit"

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Back
            Offset -1, -1

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                return _BaseColor;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
