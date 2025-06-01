Shader "WallSegmentation/HysteresisThresholding"
{
    Properties
    {
        _MainTex ("Mask Texture", 2D) = "white" {}
        _HighThreshold ("High Threshold", Range(0.0, 1.0)) = 0.7
        _LowThreshold ("Low Threshold", Range(0.0, 1.0)) = 0.3
    }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline" }
        
        Pass
        {
            Name "HysteresisThresholding"
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
            
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            float4 _MainTex_TexelSize;
            float _HighThreshold;
            float _LowThreshold;
            
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
                float2 texelSize = _MainTex_TexelSize.xy;
                
                float centerValue = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv).r;
                
                // High threshold - strong edges
                if (centerValue >= _HighThreshold)
                {
                    return float4(1, 1, 1, 1);
                }
                
                // Low threshold - check neighborhood for connectivity
                if (centerValue >= _LowThreshold)
                {
                    // Check 8-connected neighborhood
                    float neighbors[8];
                    neighbors[0] = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(-texelSize.x, -texelSize.y)).r;
                    neighbors[1] = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(0, -texelSize.y)).r;
                    neighbors[2] = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(texelSize.x, -texelSize.y)).r;
                    neighbors[3] = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(-texelSize.x, 0)).r;
                    neighbors[4] = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(texelSize.x, 0)).r;
                    neighbors[5] = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(-texelSize.x, texelSize.y)).r;
                    neighbors[6] = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(0, texelSize.y)).r;
                    neighbors[7] = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(texelSize.x, texelSize.y)).r;
                    
                    // If any neighbor is above high threshold, keep this pixel
                    for (int i = 0; i < 8; i++)
                    {
                        if (neighbors[i] >= _HighThreshold)
                        {
                            return float4(centerValue, centerValue, centerValue, 1);
                        }
                    }
                }
                
                // Below low threshold or not connected to strong edges
                return float4(0, 0, 0, 1);
            }
            ENDHLSL
        }
    }
} 