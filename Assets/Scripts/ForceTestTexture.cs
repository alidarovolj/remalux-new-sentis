using UnityEngine;
using UnityEngine.UI;

[System.Serializable]
public class ForceTestTexture : MonoBehaviour
{
      [Header("Настройки")]
      public bool enableTest = true;
      public Color color1 = Color.red;
      public Color color2 = Color.yellow;

      private RawImage rawImage;

      void Start()
      {
            rawImage = GetComponent<RawImage>();
            if (rawImage != null && enableTest)
            {
                  SetupBrightTexture();
            }
      }

      [ContextMenu("Создать яркую текстуру")]
      public void SetupBrightTexture()
      {
            if (rawImage == null)
                  rawImage = GetComponent<RawImage>();

            Debug.Log("🔥 [ForceTestTexture] Создаём ЭКСТРЕМАЛЬНО яркую текстуру!");

            // Создаем очень простую и яркую текстуру
            Texture2D texture = new Texture2D(128, 128);

            for (int y = 0; y < 128; y++)
            {
                  for (int x = 0; x < 128; x++)
                  {
                        // Большие квадраты 32x32
                        bool useColor1 = ((x / 32) + (y / 32)) % 2 == 0;
                        texture.SetPixel(x, y, useColor1 ? color1 : color2);
                  }
            }

            texture.Apply();

            // Принудительно настраиваем RawImage
            rawImage.texture = texture;
            rawImage.color = Color.white;
            rawImage.material = null;
            rawImage.enabled = true;
            gameObject.SetActive(true);

            // Принудительно устанавливаем размер и позицию
            RectTransform rect = rawImage.rectTransform;
            rect.sizeDelta = new Vector2(400, 400);
            rect.anchoredPosition = Vector2.zero;

            Debug.Log("✅ [ForceTestTexture] Яркая текстура установлена! Размер: " + rect.sizeDelta);
      }

      void Update()
      {
            if (enableTest && rawImage != null && Time.frameCount % 60 == 0)
            {
                  // Каждую секунду меняем цвета местами для анимации
                  Color temp = color1;
                  color1 = color2;
                  color2 = temp;
                  SetupBrightTexture();
            }
      }
}