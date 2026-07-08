Shader "Hex World Studio/Flowing Water"
{
    // Simple URP-compatible unlit water shader for the default
    // river renderer per ADR-0024. Scrolls the base texture along
    // the V axis at _FlowSpeed, tinted by _BaseColor. Buyers who
    // want fancier water (normal maps, refraction, foam) can swap
    // this for a URP Shader Graph material and the RiverRenderer
    // will use it as-is — the strip UVs are the standard
    // U-across-V-along-flow convention.
    Properties
    {
        _BaseColor ("Base Color", Color) = (0.35, 0.55, 0.85, 0.9)
        _BaseMap   ("Base Texture", 2D)  = "white" {}
        _FlowSpeed ("Flow Speed",   Float) = 0.35
        _TileV     ("Tile V (flow)", Float) = 1.0
        _TileU     ("Tile U (bank)", Float) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        Pass
        {
            Name "ForwardUnlit"

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv          : TEXCOORD0;
            };

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _BaseMap_ST;
                float  _FlowSpeed;
                float  _TileV;
                float  _TileU;
            CBUFFER_END

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                float2 uv = float2(IN.uv.x * _TileU, IN.uv.y * _TileV);
                uv.y += _Time.y * _FlowSpeed;
                uv = TRANSFORM_TEX(uv, _BaseMap);
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv);
                return tex * _BaseColor;
            }
            ENDHLSL
        }
    }

    FallBack Off
}
