Shader "Custom/WallPaint"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _PaintColor ("Paint Color", Color) = (1,1,1,1)
        _BlendFactor ("Blend Factor", Range(0.0, 1.0)) = 0.7
        _Alpha ("Alpha", Range(0.0, 1.0)) = 0.8
        _SegmentationMask ("Segmentation Mask", 2D) = "black" {}
        [Toggle(USE_MASK)] _UseMask ("Use Segmentation Mask", Float) = 1
        [Toggle(DEBUG_OVERLAY)] _DebugOverlay ("Debug Overlay", Float) = 0
        _DebugGrid ("Debug Grid Size", Range(5, 30)) = 10
        [Toggle(USE_AR_WORLD_SPACE)] _UseARSpace ("Use AR World Space", Float) = 1
        _PlaneID ("Plane ID", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline" = "UniversalPipeline" }
        LOD 200
        
        Pass
        {
            Name "WallPaintPass"
            Tags { "LightMode" = "UniversalForward" }
            
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Back
            
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
            };
            
            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 worldPos : TEXCOORD2;
            };
            
            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            
            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _PaintColor;
                float _BlendFactor;
                float _Alpha;
            CBUFFER_END
            
            Varyings vert(Attributes input)
            {
                Varyings output;
                
                VertexPositionInputs vertexInput = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInput = GetVertexNormalInputs(input.normalOS);
                
                output.positionHCS = vertexInput.positionCS;
                output.uv = TRANSFORM_TEX(input.uv, _MainTex);
                output.normalWS = normalInput.normalWS;
                output.worldPos = vertexInput.positionWS;
                
                return output;
            }
            
            half4 frag(Varyings input) : SV_Target
            {
                // Sample the texture
                half4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                
                // Basic lighting calculation
                Light mainLight = GetMainLight();
                float NdotL = saturate(dot(input.normalWS, mainLight.direction));
                half3 lighting = mainLight.color * NdotL + SampleSH(input.normalWS);
                
                // Blend texture with paint color
                half3 blendedColor = lerp(texColor.rgb, _PaintColor.rgb, _BlendFactor);
                
                // Apply lighting
                blendedColor *= lighting;
                
                // Final color with alpha
                half4 finalColor = half4(blendedColor, _PaintColor.a * _Alpha * texColor.a);
                
                return finalColor;
            }
            ENDHLSL
        }
    }
    
    // Fallback for Built-in Render Pipeline
    SubShader
    {
        Tags 
        { 
            "RenderType"="Transparent" 
            "Queue"="Transparent" 
        }
        
        LOD 200
        
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float3 normal : NORMAL;
            };
            
            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
                float3 worldNormal : TEXCOORD1;
            };
            
            sampler2D _MainTex;
            float4 _MainTex_ST;
            fixed4 _PaintColor;
            float _BlendFactor;
            float _Alpha;
            
            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.worldNormal = UnityObjectToWorldNormal(v.normal);
                return o;
            }
            
            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 texColor = tex2D(_MainTex, i.uv);
                
                // Simple lighting
                float3 lightDir = normalize(_WorldSpaceLightPos0.xyz);
                float NdotL = max(0, dot(i.worldNormal, lightDir));
                float3 lighting = _LightColor0.rgb * NdotL + unity_AmbientSky.rgb;
                
                // Blend colors
                fixed3 blendedColor = lerp(texColor.rgb, _PaintColor.rgb, _BlendFactor);
                blendedColor *= lighting;
                
                return fixed4(blendedColor, _PaintColor.a * _Alpha * texColor.a);
            }
            ENDCG
        }
    }
    
    Fallback "Legacy Shaders/Transparent/Diffuse"
} 