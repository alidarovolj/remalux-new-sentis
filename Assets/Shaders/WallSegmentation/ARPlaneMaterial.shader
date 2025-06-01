Shader "AR/PlaneMaterial"
{
    Properties
    {
        _BaseColor ("Base Color", Color) = (1, 1, 1, 0.5)
        _GridColor ("Grid Color", Color) = (0, 0, 0, 1)
        _GridScale ("Grid Scale", Float) = 10.0
        _GridLineWidth ("Grid Line Width", Range(0.01, 0.2)) = 0.05
        _FadeDistance ("Fade Distance", Float) = 5.0
        _PulseSpeed ("Pulse Speed", Float) = 2.0
        _PulseAmplitude ("Pulse Amplitude", Range(0.0, 1.0)) = 0.3
    }
    
    SubShader
    {
        Tags 
        { 
            "RenderType"="Transparent" 
            "Queue"="Transparent"
            "RenderPipeline" = "UniversalPipeline" 
        }
        
        Pass
        {
            Name "ForwardLit"
            Tags {"LightMode" = "UniversalForward"}
            
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                float distance : TEXCOORD3;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _GridColor;
                float _GridScale;
                float _GridLineWidth;
                float _FadeDistance;
                float _PulseSpeed;
                float _PulseAmplitude;
            CBUFFER_END
            
            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                
                VertexPositionInputs positionInputs = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionHCS = positionInputs.positionCS;
                OUT.positionWS = positionInputs.positionWS;
                OUT.uv = IN.uv;
                
                VertexNormalInputs normalInputs = GetVertexNormalInputs(IN.normalOS);
                OUT.normalWS = normalInputs.normalWS;
                
                // Calculate distance to camera
                float3 cameraPos = GetCameraPositionWS();
                OUT.distance = distance(OUT.positionWS, cameraPos);
                
                return OUT;
            }
            
            float4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                
                // Grid pattern
                float2 gridUV = IN.uv * _GridScale;
                float2 gridFrac = frac(gridUV);
                float2 gridLines = smoothstep(0.0, _GridLineWidth, gridFrac) * 
                                   smoothstep(_GridLineWidth, 0.0, gridFrac - (1.0 - _GridLineWidth));
                float gridPattern = max(gridLines.x, gridLines.y);
                
                // Distance-based fade
                float fadeFactor = 1.0 - saturate(IN.distance / _FadeDistance);
                
                // Pulse effect
                float pulse = 1.0 + _PulseAmplitude * sin(_Time.y * _PulseSpeed);
                
                // Combine colors
                float4 color = lerp(_BaseColor, _GridColor, gridPattern);
                color.a *= fadeFactor * pulse;
                
                // Simple lighting
                float3 lightDir = GetMainLight().direction;
                float NdotL = saturate(dot(normalize(IN.normalWS), lightDir));
                color.rgb *= (0.5 + 0.5 * NdotL);
                
                return color;
            }
            ENDHLSL
        }
    }
} 