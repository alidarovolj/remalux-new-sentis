using UnityEngine;
using UnityEngine.UI;

public class TestMaskDisplay : MonoBehaviour
{
      [Header("Test Settings")]
      [SerializeField] private RawImage targetRawImage;
      [SerializeField] private int textureSize = 512;
      [SerializeField] private bool animatePattern = true;
      [SerializeField] private bool forceToTop = true;

      private RenderTexture testMask;
      private Material testMaterial;

      void Start()
      {
            Debug.Log("[TestMaskDisplay] 🎯 Начинаем тестирование отображения маски", gameObject);

            if (targetRawImage == null)
                  targetRawImage = GetComponent<RawImage>();

            if (targetRawImage == null)
            {
                  Debug.LogError("[TestMaskDisplay] ❌ RawImage не найден!", gameObject);
                  return;
            }

            SetupCanvasForMaxVisibility();
            SetupRawImageForMaxVisibility();
            CreateTestMask();
            ApplyTestMask();
      }

      void Update()
      {
            if (animatePattern && testMask != null)
            {
                  UpdateTestPattern();
            }

            // Периодически проверяем настройки
            if (Time.frameCount % 180 == 0) // Каждые 3 секунды
            {
                  EnsureMaxVisibility();
            }
      }

      private void SetupCanvasForMaxVisibility()
      {
            Canvas canvas = targetRawImage.GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                  Debug.Log($"[TestMaskDisplay] 🎨 Настраиваем Canvas для максимальной видимости", gameObject);

                  // Принудительно устанавливаем Screen Space - Overlay
                  canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                  canvas.sortingOrder = 9999; // Максимальный приоритет
                  canvas.overrideSorting = true;

                  Debug.Log($"[TestMaskDisplay] ✅ Canvas настроен: RenderMode={canvas.renderMode}, SortingOrder={canvas.sortingOrder}", gameObject);
            }
            else
            {
                  Debug.LogError("[TestMaskDisplay] ❌ Canvas не найден!", gameObject);
            }
      }

      private void SetupRawImageForMaxVisibility()
      {
            // Устанавливаем большой размер и центральную позицию
            targetRawImage.rectTransform.sizeDelta = new Vector2(400, 400);
            targetRawImage.rectTransform.anchoredPosition = Vector2.zero;

            // Убеждаемся в максимальной видимости
            targetRawImage.gameObject.SetActive(true);
            targetRawImage.enabled = true;
            targetRawImage.color = Color.white;
            targetRawImage.raycastTarget = false; // Отключаем raycast чтобы не мешал

            Debug.Log("[TestMaskDisplay] 📐 RawImage настроен для максимальной видимости (400x400, в центре)", gameObject);
      }

      private void CreateTestMask()
      {
            // Создаем RenderTexture как это делает WallSegmentation
            testMask = new RenderTexture(textureSize, textureSize, 0, RenderTextureFormat.ARGB32);
            testMask.name = "TestMaskDisplay_RenderTexture";
            testMask.Create();

            // Создаем тестовый материал
            testMaterial = new Material(Shader.Find("Unlit/Texture"));
            if (testMaterial == null)
            {
                  // Fallback to default UI material
                  testMaterial = Resources.GetBuiltinResource<Material>("UI/Default.mat");
            }

            // Заполняем тестовым паттерном
            UpdateTestPattern();

            Debug.Log($"[TestMaskDisplay] ✅ Создана тестовая маска {textureSize}x{textureSize} с материалом {testMaterial.name}", gameObject);
      }

      private void UpdateTestPattern()
      {
            // Создаем временную текстуру для рисования паттерна
            Texture2D tempTexture = new Texture2D(textureSize, textureSize, TextureFormat.RGBA32, false);

            float time = Time.time;

            for (int y = 0; y < textureSize; y++)
            {
                  for (int x = 0; x < textureSize; x++)
                  {
                        // Создаем вращающийся спиральный паттерн
                        float centerX = textureSize * 0.5f;
                        float centerY = textureSize * 0.5f;

                        float dx = x - centerX;
                        float dy = y - centerY;
                        float distance = Mathf.Sqrt(dx * dx + dy * dy);
                        float angle = Mathf.Atan2(dy, dx);

                        // Спираль с анимацией
                        float spiral = Mathf.Sin(distance * 0.1f + angle * 4f + time * 3f);

                        // Радужные цвета
                        float hue = (angle + time * 0.5f) % (2 * Mathf.PI) / (2 * Mathf.PI);
                        Color color = Color.HSVToRGB(hue, 0.8f, 0.9f);

                        // Модуляция яркости спиралью
                        color = Color.Lerp(Color.black, color, (spiral + 1f) * 0.5f);

                        // Добавляем пульсирующие кольца
                        float ringIntensity = Mathf.Sin(distance * 0.05f + time * 2f);
                        if (ringIntensity > 0.7f)
                        {
                              color = Color.white;
                        }

                        tempTexture.SetPixel(x, y, color);
                  }
            }

            tempTexture.Apply();

            // Копируем в RenderTexture
            Graphics.Blit(tempTexture, testMask);

            // Очищаем временную текстуру
            DestroyImmediate(tempTexture);
      }

      private void ApplyTestMask()
      {
            if (targetRawImage != null && testMask != null)
            {
                  targetRawImage.texture = testMask;
                  targetRawImage.material = null; // НИКАКИХ материалов - только стандартный UI!
                  targetRawImage.color = Color.white; // Белый цвет

                  Debug.Log($"[TestMaskDisplay] ✅ Тестовая маска применена к RawImage БЕЗ материала", gameObject);

                  // Принудительно обновляем Canvas
                  Canvas.ForceUpdateCanvases();
            }
      }

      private void EnsureMaxVisibility()
      {
            if (targetRawImage == null) return;

            // Переустанавливаем настройки если что-то сбилось
            if (!targetRawImage.gameObject.activeInHierarchy)
            {
                  targetRawImage.gameObject.SetActive(true);
                  Debug.LogWarning("[TestMaskDisplay] ⚠️ RawImage GameObject был деактивирован, активируем заново", gameObject);
            }

            if (!targetRawImage.enabled)
            {
                  targetRawImage.enabled = true;
                  Debug.LogWarning("[TestMaskDisplay] ⚠️ RawImage компонент был отключен, включаем заново", gameObject);
            }

            // Проверяем Canvas
            Canvas canvas = targetRawImage.GetComponentInParent<Canvas>();
            if (canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                  canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                  Debug.LogWarning("[TestMaskDisplay] ⚠️ Canvas режим был изменен, возвращаем ScreenSpaceOverlay", gameObject);
            }

            // Логируем статус
            string textureInfo = targetRawImage.texture != null ? $"{targetRawImage.texture.width}x{targetRawImage.texture.height}" : "NULL";
            Debug.Log($"[TestMaskDisplay] 📊 Статус проверки: Active={targetRawImage.gameObject.activeInHierarchy}, Enabled={targetRawImage.enabled}, Texture={textureInfo}", gameObject);
      }

      void OnDestroy()
      {
            if (testMask != null)
            {
                  testMask.Release();
                  DestroyImmediate(testMask);
            }

            if (testMaterial != null)
            {
                  DestroyImmediate(testMaterial);
            }
      }
}