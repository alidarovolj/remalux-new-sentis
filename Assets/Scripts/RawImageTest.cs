using UnityEngine;
using UnityEngine.UI;

public class RawImageTest : MonoBehaviour
{
      private RawImage rawImage;

      void Start()
      {
            rawImage = GetComponent<RawImage>();
            if (rawImage != null)
            {
                  Debug.Log("[RawImageTest] 🧪 Начинаем тест RawImage", gameObject);

                  // Увеличиваем размер для лучшей видимости
                  rawImage.rectTransform.sizeDelta = new Vector2(400, 400);
                  rawImage.rectTransform.anchoredPosition = new Vector2(0, 0); // Центр экрана

                  // Создаем очень яркую и заметную тестовую текстуру
                  CreateBrightTestTexture();

                  // Проверяем Canvas настройки
                  CheckCanvasSettings();
            }
            else
            {
                  Debug.LogError("[RawImageTest] ❌ RawImage компонент не найден!", gameObject);
            }
      }

      private void CreateBrightTestTexture()
      {
            Texture2D testTexture = new Texture2D(512, 512, TextureFormat.ARGB32, false);

            // Создаем анимированный паттерн
            float time = Time.time;

            for (int y = 0; y < 512; y++)
            {
                  for (int x = 0; x < 512; x++)
                  {
                        // Создаем радужный градиент с анимацией
                        float centerX = 256f;
                        float centerY = 256f;
                        float distance = Vector2.Distance(new Vector2(x, y), new Vector2(centerX, centerY));

                        float hue = (distance + time * 50f) % 360f / 360f;
                        Color color = Color.HSVToRGB(hue, 1f, 1f);

                        // Добавляем концентрические круги
                        if (Mathf.Sin(distance * 0.1f + time * 5f) > 0.5f)
                        {
                              color = Color.white;
                        }

                        testTexture.SetPixel(x, y, color);
                  }
            }

            testTexture.Apply();
            rawImage.texture = testTexture;
            rawImage.color = Color.white;

            Debug.Log("[RawImageTest] ✅ Создана анимированная радужная тестовая текстура 512x512", gameObject);
      }

      private void CheckCanvasSettings()
      {
            Canvas canvas = rawImage.GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                  Debug.Log($"[RawImageTest] 📋 Canvas информация:", gameObject);
                  Debug.Log($"  - RenderMode: {canvas.renderMode}", gameObject);
                  Debug.Log($"  - SortingOrder: {canvas.sortingOrder}", gameObject);
                  Debug.Log($"  - WorldCamera: {(canvas.worldCamera != null ? canvas.worldCamera.name : "NULL")}", gameObject);

                  // Принудительно устанавливаем Overlay режим для максимальной видимости
                  if (canvas.renderMode != RenderMode.ScreenSpaceOverlay)
                  {
                        Debug.LogWarning($"[RawImageTest] ⚠️ Изменяем Canvas с {canvas.renderMode} на ScreenSpaceOverlay", gameObject);
                        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                  }

                  // Устанавливаем высокий sortingOrder
                  if (canvas.sortingOrder < 100)
                  {
                        Debug.Log($"[RawImageTest] 📈 Увеличиваем sortingOrder с {canvas.sortingOrder} до 100", gameObject);
                        canvas.sortingOrder = 100;
                  }
            }
            else
            {
                  Debug.LogError("[RawImageTest] ❌ Canvas не найден в родительских объектах!", gameObject);
            }
      }

      void Update()
      {
            // Обновляем тестовую текстуру каждые 30 кадров для анимации
            if (Time.frameCount % 30 == 0 && rawImage != null && rawImage.texture != null)
            {
                  CreateBrightTestTexture();
            }

            // Каждые 5 секунд логируем статус
            if (Time.frameCount % 300 == 0)
            {
                  Debug.Log($"[RawImageTest] 📊 Статус: GameObject активен: {gameObject.activeInHierarchy}, RawImage enabled: {rawImage?.enabled}, Текстура: {(rawImage?.texture != null ? $"{rawImage.texture.width}x{rawImage.texture.height}" : "NULL")}", gameObject);
            }
      }
}