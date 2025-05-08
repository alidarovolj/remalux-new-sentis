Shader "Custom/WallPaint"
{
    Properties
    {
        _MainTex ("Camera Texture", 2D) = "white" {}
        _MaskTex ("Segmentation Mask", 2D) = "white" {}
        _PaintColor ("Paint Color", Color) = (1,1,1,1)
        _BlendFactor ("Blend Factor", Range(0,1)) = 0.5
    }
    
    SubShader
    {
        Tags { "RenderType"="Opaque" }
        LOD 100
        
        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"
            
            // Вспомогательные функции для конвертации цветовых пространств вынесены за пределы frag
            float HueToRGB(float p, float q, float t)
            {
                if (t < 0.0) t += 1.0;
                if (t > 1.0) t -= 1.0;
                if (t < 1.0/6.0) return p + (q - p) * 6.0 * t;
                if (t < 1.0/2.0) return q;
                if (t < 2.0/3.0) return p + (q - p) * (2.0/3.0 - t) * 6.0;
                return p;
            }

            float3 HSLToRGB(float3 hsl)
            {
                float3 rgb = float3(0.0, 0.0, 0.0);
                
                if (hsl.y == 0.0)
                {
                    rgb = float3(hsl.z, hsl.z, hsl.z); // Серый цвет
                }
                else
                {
                    float q = (hsl.z < 0.5) ? (hsl.z * (1.0 + hsl.y)) : (hsl.z + hsl.y - hsl.z * hsl.y);
                    float p = 2.0 * hsl.z - q;
                    
                    rgb.r = HueToRGB(p, q, hsl.x + 1.0/3.0);
                    rgb.g = HueToRGB(p, q, hsl.x);
                    rgb.b = HueToRGB(p, q, hsl.x - 1.0/3.0);
                }
                
                return rgb;
            }

            float3 RGBToHSL(float3 rgb)
            {
                float3 hsl = float3(0.0, 0.0, 0.0); // h, s, l
                float minVal = min(min(rgb.r, rgb.g), rgb.b);
                float maxVal = max(max(rgb.r, rgb.g), rgb.b);
                float delta = maxVal - minVal;
                
                hsl.z = (maxVal + minVal) / 2.0; // Яркость (Luminance)
                
                if (delta == 0.0) // Если это серый цвет
                {
                    hsl.x = 0.0; // Оттенок (Hue)
                    hsl.y = 0.0; // Насыщенность (Saturation)
                }
                else
                {
                    hsl.y = (hsl.z < 0.5) ? (delta / (maxVal + minVal)) : (delta / (2.0 - maxVal - minVal));
                    
                    float deltaR = (((maxVal - rgb.r) / 6.0) + (delta / 2.0)) / delta;
                    float deltaG = (((maxVal - rgb.g) / 6.0) + (delta / 2.0)) / delta;
                    float deltaB = (((maxVal - rgb.b) / 6.0) + (delta / 2.0)) / delta;
                    
                    if (rgb.r == maxVal)
                        hsl.x = deltaB - deltaG;
                    else if (rgb.g == maxVal)
                        hsl.x = (1.0 / 3.0) + deltaR - deltaB;
                    else // rgb.b == maxVal
                        hsl.x = (2.0 / 3.0) + deltaG - deltaR;
                        
                    if (hsl.x < 0.0)
                        hsl.x += 1.0;
                    if (hsl.x > 1.0)
                        hsl.x -= 1.0;
                }
                
                return hsl;
            }
            
            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
            };
            
            struct v2f
            {
                float2 uv : TEXCOORD0;
                float4 vertex : SV_POSITION;
            };
            
            sampler2D _MainTex;
            sampler2D _MaskTex;
            float4 _PaintColor;
            float _BlendFactor;
            
            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }
            
            float4 frag (v2f i) : SV_Target
            {
                // Получаем оригинальный цвет с камеры
                float4 originalColor = tex2D(_MainTex, i.uv);
                
                // Получаем значение маски
                float maskValue = tex2D(_MaskTex, i.uv).r;
                
                // Конвертируем оригинальный цвет в HSL 
                float3 originalHSL = RGBToHSL(originalColor.rgb);
                // Конвертируем цвет покраски в HSL
                float3 paintHSL = RGBToHSL(_PaintColor.rgb);

                // Создаем новый HSL, используя Hue и Saturation от цвета покраски, и Luminance от оригинала
                float3 newHSL = float3(paintHSL.x, paintHSL.y, originalHSL.z);
                
                // Конвертируем новый HSL обратно в RGB
                float3 newColorRGB = HSLToRGB(newHSL);
                
                // Смешиваем цвета на основе маски и фактора смешивания
                // BlendFactor определяет, насколько сильно применяется эффект покраски
                float3 finalColor = lerp(originalColor.rgb, newColorRGB, maskValue * _BlendFactor);
                
                return float4(finalColor, originalColor.a); // Сохраняем альфа-канал оригинала
            }
            ENDCG
        }
    }
} 