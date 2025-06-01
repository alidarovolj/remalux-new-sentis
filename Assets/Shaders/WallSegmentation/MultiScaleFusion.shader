Shader "WallSegmentation/MultiScaleFusion"
{
    Properties
    {
        _Scale1Tex ("Scale 1.0", 2D) = "white" {}
        _Scale2Tex ("Scale 0.75", 2D) = "white" {}
        _Scale3Tex ("Scale 0.5", 2D) = "white" {}
        _Weight1 ("Weight Scale 1.0", Range(0.0, 1.0)) = 0.5
        _Weight2 ("Weight Scale 0.75", Range(0.0, 1.0)) = 0.3
        _Weight3 ("Weight Scale 0.5", Range(0.0, 1.0)) = 0.2
    }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline" }
        
        Pass
        {
            Name "MultiScaleFusion"
            Cull Off
            ZWrite Off
            ZTest Always
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            
            TEXTURE2D(_Scale1Tex);
            SAMPLER(sampler_Scale1Tex);
            TEXTURE2D(_Scale2Tex);
            SAMPLER(sampler_Scale2Tex);
            TEXTURE2D(_Scale3Tex);
            SAMPLER(sampler_Scale3Tex);
            
            float _Weight1;
            float _Weight2;
            float _Weight3;
            
            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = IN.uv;
                return OUT;
            }
            
            float4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                
                float2 uv = IN.uv;
                
                float4 scale1 = SAMPLE_TEXTURE2D(_Scale1Tex, sampler_Scale1Tex, uv);
                float4 scale2 = SAMPLE_TEXTURE2D(_Scale2Tex, sampler_Scale2Tex, uv);
                float4 scale3 = SAMPLE_TEXTURE2D(_Scale3Tex, sampler_Scale3Tex, uv);
                
                // Normalize weights
                float totalWeight = _Weight1 + _Weight2 + _Weight3;
                float w1 = _Weight1 / totalWeight;
                float w2 = _Weight2 / totalWeight;
                float w3 = _Weight3 / totalWeight;
                
                // Weighted fusion
                float4 result = scale1 * w1 + scale2 * w2 + scale3 * w3;
                
                // Confidence boosting: if multiple scales agree, increase confidence
                float consensus = 0.0;
                float threshold = 0.5;
                
                if (scale1.r > threshold) consensus += w1;
                if (scale2.r > threshold) consensus += w2;
                if (scale3.r > threshold) consensus += w3;
                
                // Boost result based on consensus
                result.r = result.r * (1.0 + consensus * 0.3);
                result.r = saturate(result.r);
                
                return float4(result.r, result.r, result.r, 1);
            }
            ENDHLSL
        }
    }
} 