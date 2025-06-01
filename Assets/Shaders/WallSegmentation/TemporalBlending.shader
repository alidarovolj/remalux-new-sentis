Shader "WallSegmentation/TemporalBlending"
{
    Properties
    {
        _CurrentTex ("Current Frame", 2D) = "white" {}
        _PreviousTex ("Previous Frame", 2D) = "white" {}
        _BlendWeight ("Current Frame Weight", Range(0.1, 1.0)) = 0.7
        _MinChangeThreshold ("Min Change Threshold", Range(0.0, 0.1)) = 0.05
    }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline" }
        
        Pass
        {
            Name "TemporalBlending"
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
            
            TEXTURE2D(_CurrentTex);
            SAMPLER(sampler_CurrentTex);
            TEXTURE2D(_PreviousTex);
            SAMPLER(sampler_PreviousTex);
            float _BlendWeight;
            float _MinChangeThreshold;
            
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
                
                float4 current = SAMPLE_TEXTURE2D(_CurrentTex, sampler_CurrentTex, uv);
                float4 previous = SAMPLE_TEXTURE2D(_PreviousTex, sampler_PreviousTex, uv);
                
                // Calculate difference
                float difference = abs(current.r - previous.r);
                
                // If change is too small, keep previous value for stability
                if (difference < _MinChangeThreshold)
                {
                    return previous;
                }
                
                // Temporal blending with adaptive weight based on difference
                float adaptiveWeight = _BlendWeight;
                
                // Increase weight for significant changes
                if (difference > 0.3)
                {
                    adaptiveWeight = min(1.0, _BlendWeight + 0.2);
                }
                
                float4 result = lerp(previous, current, adaptiveWeight);
                
                return result;
            }
            ENDHLSL
        }
    }
} 