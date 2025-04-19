Shader "Hidden/PackSmoothness"
{
    Properties
    {
        _SmoothnessMap ("Smoothness", 2D) = "black" {}
        _SurfaceMap ("Smoothness", 2D) = "black" {}
        _InvertSmoothness("Invert Smoothness", Range(0.0, 1.0)) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            // make fog work

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };

            TEXTURE2D(_SmoothnessMap);
            TEXTURE2D(_SurfaceMap);
            SAMPLER(sampler_SmoothnessMap);
            SAMPLER(sampler_SurfaceMap);
            float _InvertSmoothness;
            

            Varyings vert (Attributes v)
            {
                Varyings o;
                o.vertex = TransformObjectToHClip(v.vertex);
                o.uv = v.uv;
                return o;
            }

            half4 frag (Varyings i) : SV_Target
            {
                // sample the texture
                half4 surface = SAMPLE_TEXTURE2D(_SurfaceMap, sampler_SurfaceMap, i.uv);
                half4 smoothness = SAMPLE_TEXTURE2D(_SmoothnessMap, sampler_SmoothnessMap, i.uv);
                
                smoothness = _InvertSmoothness < 0.5? smoothness : 1-smoothness;

                half4 result = half4(surface.x, surface.y, surface.z, smoothness.r);
                
                return result;
            }
            ENDHLSL
        }
    }
}
