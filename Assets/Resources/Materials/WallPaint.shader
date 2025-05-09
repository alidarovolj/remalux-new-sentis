Shader "Custom/WallPaint"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _PaintColor ("Paint Color", Color) = (1,0,0,1)
        _BlendFactor ("Blend Factor", Range(0,1)) = 0.5
        _SegmentationMask ("Segmentation Mask", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline" }
        LOD 100
        
        // Добавляем Cull Off для теста на iOS
        Cull Off
        ZWrite On
        ZTest LEqual

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ USE_MASK
            
            // Добавляем специфичные для платформы прагмы
            #pragma multi_compile_instancing
            #pragma prefer_hlslcc gles
            #pragma exclude_renderers d3d11_9x
            #pragma target 2.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float2 uv : TEXCOORD0;
                float4 positionHCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_SegmentationMask);
            SAMPLER(sampler_SegmentationMask);
            
            half4 _PaintColor;
            float _BlendFactor;
            float4 _MainTex_ST;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                
                // Получаем цвет исходного изображения
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                
                #ifdef USE_MASK
                // Проверяем доступность текстуры маски
                float mask = 0;
                
                // Используем маску сегментации для определения области покраски
                #if defined(SHADER_API_METAL) || defined(SHADER_API_VULKAN) || defined(SHADER_API_GLES3)
                    // Осторожное семплирование для мобильных платформ
                    mask = SAMPLE_TEXTURE2D(_SegmentationMask, sampler_SegmentationMask, IN.uv).r;
                #else
                    mask = SAMPLE_TEXTURE2D(_SegmentationMask, sampler_SegmentationMask, IN.uv).r;
                #endif
                
                // Применяем цвет только в области стен (если маска > 0.1)
                if (mask > 0.1)
                {
                    color = lerp(color, _PaintColor, _BlendFactor * mask);
                }
                #else
                // Без маски - просто смешиваем цвета
                color = lerp(color, _PaintColor, _BlendFactor);
                #endif
                
                return color;
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}