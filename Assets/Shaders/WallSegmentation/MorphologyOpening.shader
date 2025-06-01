Shader "WallSegmentation/MorphologyOpening"
{
    Properties
    {
        _MainTex ("Mask Texture", 2D) = "white" {}
        _KernelSize ("Kernel Size", Int) = 3
        _Iterations ("Iterations", Int) = 1
    }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline" }
        
        Pass
        {
            Name "Erosion"
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
            int _KernelSize;
            
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
                
                int halfKernel = _KernelSize / 2;
                float minValue = 1.0;
                
                // Erosion: find minimum in neighborhood
                for (int y = -halfKernel; y <= halfKernel; y++)
                {
                    for (int x = -halfKernel; x <= halfKernel; x++)
                    {
                        float2 sampleUV = uv + float2(x, y) * texelSize;
                        float value = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, sampleUV).r;
                        minValue = min(minValue, value);
                    }
                }
                
                return float4(minValue, minValue, minValue, 1);
            }
            ENDHLSL
        }
        
        Pass
        {
            Name "Dilation"
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
            int _KernelSize;
            
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
                
                int halfKernel = _KernelSize / 2;
                float maxValue = 0.0;
                
                // Dilation: find maximum in neighborhood
                for (int y = -halfKernel; y <= halfKernel; y++)
                {
                    for (int x = -halfKernel; x <= halfKernel; x++)
                    {
                        float2 sampleUV = uv + float2(x, y) * texelSize;
                        float value = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, sampleUV).r;
                        maxValue = max(maxValue, value);
                    }
                }
                
                return float4(maxValue, maxValue, maxValue, 1);
            }
            ENDHLSL
        }
    }
} 