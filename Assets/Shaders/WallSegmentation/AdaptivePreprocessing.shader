Shader "WallSegmentation/AdaptivePreprocessing"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Brightness ("Brightness", Range(0.5, 2.0)) = 1.0
        _Contrast ("Contrast", Range(0.5, 3.0)) = 1.0
        _Sharpening ("Sharpening Strength", Range(0.0, 2.0)) = 0.0
        _AutoContrast ("Auto Contrast", Range(0.0, 1.0)) = 0.0
    }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline" }
        
        Pass
        {
            Name "AdaptivePreprocessing"
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
            float _Brightness;
            float _Contrast;
            float _Sharpening;
            float _AutoContrast;
            
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
                float4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv);
                
                // Apply sharpening filter if enabled
                if (_Sharpening > 0.0)
                {
                    float2 texelSize = _MainTex_TexelSize.xy;
                    float4 sharp = color * (1.0 + 4.0 * _Sharpening);
                    
                    sharp -= SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(0, texelSize.y)) * _Sharpening;
                    sharp -= SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv - float2(0, texelSize.y)) * _Sharpening;
                    sharp -= SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv + float2(texelSize.x, 0)) * _Sharpening;
                    sharp -= SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, uv - float2(texelSize.x, 0)) * _Sharpening;
                    
                    color = sharp;
                }
                
                // Apply brightness
                color.rgb *= _Brightness;
                
                // Apply contrast
                color.rgb = ((color.rgb - 0.5) * _Contrast) + 0.5;
                
                // Auto contrast enhancement
                if (_AutoContrast > 0.0)
                {
                    float luminance = dot(color.rgb, float3(0.299, 0.587, 0.114));
                    float contrastBoost = 1.0 + _AutoContrast * (0.5 - abs(luminance - 0.5));
                    color.rgb = ((color.rgb - 0.5) * contrastBoost) + 0.5;
                }
                
                // Clamp values
                color.rgb = saturate(color.rgb);
                
                return color;
            }
            ENDHLSL
        }
    }
} 