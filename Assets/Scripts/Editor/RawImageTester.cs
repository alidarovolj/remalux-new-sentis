using UnityEngine;
using UnityEngine.UI;
using UnityEditor;

public class RawImageTester : EditorWindow
{
      [MenuItem("Tools/Test RawImage")]
      public static void ShowWindow()
      {
            GetWindow<RawImageTester>("RawImage Tester");
      }

      void OnGUI()
      {
            GUILayout.Label("RawImage Tester", EditorStyles.boldLabel);

            GUILayout.Space(10);

            if (GUILayout.Button("🔥 Активировать UltraBrightUITest"))
            {
                  ActivateUltraBrightTest();
            }

            if (GUILayout.Button("🚫 Отключить обновления масок"))
            {
                  DisableMaskUpdates();
            }

            if (GUILayout.Button("✅ Включить обновления масок"))
            {
                  EnableMaskUpdates();
            }

            GUILayout.Space(10);

            if (GUILayout.Button("Создать тестовую яркую текстуру"))
            {
                  CreateTestTexture();
            }

            if (GUILayout.Button("Найти и исправить RawImage"))
            {
                  FixRawImage();
            }

            GUILayout.Space(10);

            if (GUILayout.Button("🔧 Принудительная диагностика"))
            {
                  ForceDiagnostic();
            }
      }

      void ActivateUltraBrightTest()
      {
            RawImage rawImage = FindObjectOfType<RawImage>();
            if (rawImage == null)
            {
                  Debug.LogError("RawImage не найден в сцене!");
                  return;
            }

            // Добавляем или активируем UltraBrightUITest
            var ultraBright = rawImage.GetComponent<UltraBrightUITest>();
            if (ultraBright == null)
            {
                  ultraBright = rawImage.gameObject.AddComponent<UltraBrightUITest>();
            }
            ultraBright.enabled = true;

            Debug.Log("✅ UltraBrightUITest активирован!");
      }

      void DisableMaskUpdates()
      {
            var debugMaskLinker = FindObjectOfType<DebugMaskLinker>();
            if (debugMaskLinker != null)
            {
                  // Используем рефлексию для изменения приватного поля
                  var field = debugMaskLinker.GetType().GetField("enableMaskUpdates", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                  if (field != null)
                  {
                        field.SetValue(debugMaskLinker, false);
                        Debug.Log("🚫 Обновления масок отключены!");
                  }
            }
      }

      void EnableMaskUpdates()
      {
            var debugMaskLinker = FindObjectOfType<DebugMaskLinker>();
            if (debugMaskLinker != null)
            {
                  var field = debugMaskLinker.GetType().GetField("enableMaskUpdates", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                  if (field != null)
                  {
                        field.SetValue(debugMaskLinker, true);
                        Debug.Log("✅ Обновления масок включены!");
                  }
            }
      }

      void CreateTestTexture()
      {
            RawImage rawImage = FindObjectOfType<RawImage>();
            if (rawImage == null)
            {
                  Debug.LogError("RawImage не найден в сцене!");
                  return;
            }

            // Создаем простую яркую текстуру
            Texture2D testTexture = new Texture2D(128, 128, TextureFormat.RGBA32, false);

            for (int y = 0; y < 128; y++)
            {
                  for (int x = 0; x < 128; x++)
                  {
                        Color color = ((x / 16) + (y / 16)) % 2 == 0 ? Color.magenta : Color.cyan;
                        testTexture.SetPixel(x, y, color);
                  }
            }

            testTexture.Apply();
            rawImage.texture = testTexture;
            rawImage.material = null;
            rawImage.color = Color.white;

            Debug.Log("✅ Тестовая текстура создана и применена!");
      }

      void FixRawImage()
      {
            RawImage rawImage = FindObjectOfType<RawImage>();
            if (rawImage == null)
            {
                  Debug.LogError("RawImage не найден в сцене!");
                  return;
            }

            // Принудительные исправления
            rawImage.rectTransform.sizeDelta = new Vector2(400, 400);
            rawImage.rectTransform.anchoredPosition = Vector2.zero;
            rawImage.enabled = true;
            rawImage.gameObject.SetActive(true);
            rawImage.material = null;
            rawImage.color = Color.white;

            // Исправляем Canvas
            Canvas canvas = rawImage.GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                  canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                  canvas.sortingOrder = 100;
            }

            Debug.Log("🔧 RawImage исправлен!");
      }

      void ForceDiagnostic()
      {
            RawImage rawImage = FindObjectOfType<RawImage>();
            if (rawImage == null)
            {
                  Debug.LogError("❌ RawImage не найден в сцене!");
                  return;
            }

            Debug.Log("🔍 === ПРИНУДИТЕЛЬНАЯ ДИАГНОСТИКА ===");
            Debug.Log($"GameObject: {rawImage.gameObject.name}");
            Debug.Log($"Активен: {rawImage.gameObject.activeInHierarchy}");
            Debug.Log($"RawImage enabled: {rawImage.enabled}");
            Debug.Log($"Размер: {rawImage.rectTransform.sizeDelta}");
            Debug.Log($"Позиция: {rawImage.rectTransform.anchoredPosition}");
            Debug.Log($"Цвет: {rawImage.color}");
            Debug.Log($"Текстура: {(rawImage.texture != null ? $"{rawImage.texture.width}x{rawImage.texture.height}" : "NULL")}");
            Debug.Log($"Материал: {(rawImage.material != null ? rawImage.material.name : "NULL")}");

            Canvas canvas = rawImage.GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                  Debug.Log($"Canvas RenderMode: {canvas.renderMode}");
                  Debug.Log($"Canvas SortingOrder: {canvas.sortingOrder}");
            }

            // Проверяем компоненты на GameObject
            var components = rawImage.GetComponents<MonoBehaviour>();
            Debug.Log($"Компоненты на GameObject ({components.Length}):");
            foreach (var comp in components)
            {
                  Debug.Log($"  - {comp.GetType().Name} (enabled: {comp.enabled})");
            }
      }
}