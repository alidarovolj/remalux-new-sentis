using UnityEngine;
using Unity.Collections;
using System.Collections;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Диагностический скрипт для отладки проблем с генерацией плоскостей из сегментации
/// </summary>
public class SegmentationPlaneDebugger : MonoBehaviour
{
      [Header("Ссылки на компоненты")]
      [SerializeField] private WallSegmentation wallSegmentation;
      [SerializeField] private WallPainterController wallPainterController;
      [SerializeField] private ARManagerInitializer2 arManager;

      [Header("Настройки отладки")]
      [SerializeField] private bool enableDebugLogging = true;
      [SerializeField] private bool visualizeRaycastResults = true;
      [SerializeField] private float raycastVisualizationDuration = 5f;

      [Header("Статус системы")]
      [SerializeField] private bool isSegmentationInitialized = false;
#pragma warning disable 0414 // Field assigned but never used - используется для отображения в инспекторе
      [SerializeField] private bool isReceivingMaskUpdates = false;
#pragma warning restore 0414
      [SerializeField] private int maskUpdateCount = 0;
      [SerializeField] private int raycastHitCount = 0;
      [SerializeField] private int planesGeneratedCount = 0;

      [Header("Последние результаты")]
      [SerializeField] private string lastMaskInfo = "Не получено";
      [SerializeField] private string lastRaycastResult = "Не выполнен";
#pragma warning disable 0414 // Field assigned but never used - используется для отображения в инспекторе
      [SerializeField] private string lastPlaneGenerationResult = "Не создавались";
#pragma warning restore 0414

      private void Start()
      {
            // Находим компоненты, если не назначены
            if (wallSegmentation == null)
                  wallSegmentation = FindObjectOfType<WallSegmentation>();

            // Сначала ищем новую систему WallPainterController
            if (wallPainterController == null)
                  wallPainterController = FindObjectOfType<WallPainterController>();

            // Если не найдена, ищем старую систему ARManagerInitializer2
            if (arManager == null)
                  arManager = FindObjectOfType<ARManagerInitializer2>();

            if (wallSegmentation == null)
            {
                  Debug.LogError("[SegmentationPlaneDebugger] ❌ Не удалось найти WallSegmentation!");
                  return;
            }

            if (wallPainterController == null && arManager == null)
            {
                  Debug.LogError("[SegmentationPlaneDebugger] ❌ Не удалось найти WallPainterController или ARManagerInitializer2!");
                  return;
            }

            // Определяем какая система активна
            string activeSystem = wallPainterController != null ? "WallPainterController (новая)" : "ARManagerInitializer2 (старая)";
            Debug.Log($"[SegmentationPlaneDebugger] ✅ Подключен к системе: {activeSystem}");

            // Подписываемся на события
            wallSegmentation.OnSegmentationMaskUpdated += OnSegmentationMaskUpdated;

            StartCoroutine(MonitorSystemStatus());
      }

      private void OnDestroy()
      {
            if (wallSegmentation != null)
                  wallSegmentation.OnSegmentationMaskUpdated -= OnSegmentationMaskUpdated;
      }

      private void OnSegmentationMaskUpdated(RenderTexture mask)
      {
            maskUpdateCount++;
            isReceivingMaskUpdates = true;

            if (mask != null)
            {
                  lastMaskInfo = $"Размер: {mask.width}x{mask.height}, Формат: {mask.format}";

                  if (enableDebugLogging)
                  {
                        Debug.Log($"[SegmentationPlaneDebugger] 📊 Получена маска сегментации #{maskUpdateCount}: {lastMaskInfo}");

                        // Анализируем содержимое маски
                        StartCoroutine(AnalyzeMaskContent(mask));
                  }
            }
            else
            {
                  lastMaskInfo = "NULL маска";
                  Debug.LogWarning("[SegmentationPlaneDebugger] ⚠️ Получена NULL маска сегментации!");
            }
      }

      private IEnumerator AnalyzeMaskContent(RenderTexture mask)
      {
            // Создаем временную текстуру для анализа
            int sampleWidth = Mathf.Min(mask.width, 128);
            int sampleHeight = Mathf.Min(mask.height, 128);

            RenderTexture tempRT = RenderTexture.GetTemporary(sampleWidth, sampleHeight, 0, RenderTextureFormat.ARGB32);
            Graphics.Blit(mask, tempRT);

            Texture2D tempTex = new Texture2D(sampleWidth, sampleHeight, TextureFormat.RGBA32, false);
            RenderTexture.active = tempRT;
            tempTex.ReadPixels(new Rect(0, 0, sampleWidth, sampleHeight), 0, 0);
            tempTex.Apply();
            RenderTexture.active = null;

            // Анализируем пиксели
            Color32[] pixels = tempTex.GetPixels32();
            int redPixelCount = 0;
            float maxRedValue = 0f;

            foreach (var pixel in pixels)
            {
                  if (pixel.r > 10) // Порог для подсчета "красных" пикселей
                  {
                        redPixelCount++;
                        maxRedValue = Mathf.Max(maxRedValue, pixel.r / 255f);
                  }
            }

            float redPixelPercentage = (float)redPixelCount / pixels.Length * 100f;

            Debug.Log($"[SegmentationPlaneDebugger] 🔍 Анализ маски: " +
                      $"Красных пикселей: {redPixelCount}/{pixels.Length} ({redPixelPercentage:F1}%), " +
                      $"Макс. красный: {maxRedValue:F2}");

            // Проверяем настройки активной системы
            if (wallPainterController != null)
            {
                  Debug.Log($"[SegmentationPlaneDebugger] ⚙️ Настройки WallPainterController: " +
                            $"debugMode={GetPrivateField<bool>(wallPainterController, "debugMode")}, " +
                            $"segmentationConfidence={GetPrivateField<float>(wallPainterController, "segmentationConfidence")}, " +
                            $"maxRaycastDistance={GetPrivateField<float>(wallPainterController, "maxRaycastDistance")}, " +
                            $"minContourArea={GetPrivateField<int>(wallPainterController, "minContourArea")}");
            }
            else if (arManager != null)
            {
                  Debug.Log($"[SegmentationPlaneDebugger] ⚙️ Настройки ARManager: " +
                            $"useDetectedPlanes={arManager.useDetectedPlanes}, " +
                            $"wallPixelThreshold={GetPrivateField<byte>(arManager, "wallPixelThreshold")}, " +
                            $"minAreaSizeInPixels={GetPrivateField<int>(arManager, "minAreaSizeInPixels")}, " +
                            $"enableDetailedRaycastLogging={GetPrivateField<bool>(arManager, "enableDetailedRaycastLogging")}");

                  // Включаем детальное логирование рейкастов для отладки
                  SetPrivateField(arManager, "enableDetailedRaycastLogging", true);
            }

            // Освобождаем ресурсы
            Destroy(tempTex);
            RenderTexture.ReleaseTemporary(tempRT);

            yield return null;
      }

      private IEnumerator MonitorSystemStatus()
      {
            while (true)
            {
                  // Проверяем состояние сегментации
                  if (wallSegmentation != null)
                  {
                        isSegmentationInitialized = wallSegmentation.IsModelInitialized;

                        if (!isSegmentationInitialized && Time.frameCount % 60 == 0)
                        {
                              Debug.LogWarning($"[SegmentationPlaneDebugger] ⚠️ Модель сегментации НЕ инициализирована! " +
                                               $"IsInitializing={wallSegmentation.IsInitializing}, " +
                                               $"IsInitializationFailed={wallSegmentation.IsInitializationFailed}");

                              if (wallSegmentation.IsInitializationFailed)
                              {
                                    Debug.LogError($"[SegmentationPlaneDebugger] ❌ Инициализация модели провалилась: {wallSegmentation.LastErrorMessage}");
                              }
                        }
                  }

                  // Проверяем количество сгенерированных плоскостей
                  if (wallPainterController != null)
                  {
                        // Для новой системы ищем объекты с тегом "WallPlane" или по названию
                        var wallPlanes = GameObject.FindObjectsOfType<GameObject>()
                            .Where(go => go.name.Contains("WallPlane") || go.name.Contains("Wall_") || go.tag == "WallPlane")
                            .ToArray();
                        planesGeneratedCount = wallPlanes.Length;

                        if (planesGeneratedCount > 0 && enableDebugLogging && Time.frameCount % 300 == 0)
                        {
                              Debug.Log($"[SegmentationPlaneDebugger] ✅ Найдено плоскостей WallPainter: {planesGeneratedCount}");
                              foreach (var plane in wallPlanes.Take(3)) // Показываем первые 3
                              {
                                    if (plane != null)
                                    {
                                          Debug.Log($"  - {plane.name}: Pos={plane.transform.position}, Size={GetPlaneSize(plane)}");
                                    }
                              }
                        }
                  }
                  else if (arManager != null)
                  {
                        var generatedPlanes = arManager.GeneratedPlanes;
                        planesGeneratedCount = generatedPlanes?.Count ?? 0;

                        if (planesGeneratedCount > 0 && enableDebugLogging && Time.frameCount % 300 == 0)
                        {
                              Debug.Log($"[SegmentationPlaneDebugger] ✅ Сгенерировано плоскостей ARManager: {planesGeneratedCount}");
                              foreach (var plane in generatedPlanes.Take(3)) // Показываем первые 3
                              {
                                    if (plane != null)
                                    {
                                          Debug.Log($"  - {plane.name}: Pos={plane.transform.position}, Size={GetPlaneSize(plane)}");
                                    }
                              }
                        }
                  }

                  yield return new WaitForSeconds(1f);
            }
      }

      private Vector2 GetPlaneSize(GameObject plane)
      {
            var meshFilter = plane.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.mesh != null)
            {
                  var bounds = meshFilter.mesh.bounds;
                  return new Vector2(bounds.size.x, bounds.size.y);
            }
            return Vector2.zero;
      }

      // Вспомогательные методы для доступа к приватным полям через рефлексию
      private T GetPrivateField<T>(object obj, string fieldName)
      {
            var field = obj.GetType().GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null)
            {
                  return (T)field.GetValue(obj);
            }
            return default(T);
      }

      private void SetPrivateField(object obj, string fieldName, object value)
      {
            var field = obj.GetType().GetField(fieldName, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (field != null)
            {
                  field.SetValue(obj, value);
            }
      }

      // Тестовый метод для ручного запуска рейкаста
      [ContextMenu("Test Raycast")]
      public void TestRaycast()
      {
            if (Camera.main == null)
            {
                  Debug.LogError("[SegmentationPlaneDebugger] ❌ Camera.main не найдена!");
                  return;
            }

            // Выполняем рейкаст из центра экрана
            Ray ray = Camera.main.ViewportPointToRay(new Vector3(0.5f, 0.5f, 0));
            RaycastHit hit;

            // Получаем LayerMask из ARManager
            LayerMask layerMask = GetPrivateField<LayerMask>(arManager, "hitLayerMask");
            float maxDistance = GetPrivateField<float>(arManager, "maxRayDistance");

            Debug.Log($"[SegmentationPlaneDebugger] 🎯 Тестовый рейкаст: LayerMask={LayerMaskToString(layerMask)}, MaxDistance={maxDistance}");

            if (Physics.Raycast(ray, out hit, maxDistance, layerMask))
            {
                  raycastHitCount++;
                  lastRaycastResult = $"Попадание в {hit.collider.name} на расстоянии {hit.distance:F2}м";

                  Debug.Log($"[SegmentationPlaneDebugger] ✅ Рейкаст попал: {lastRaycastResult}, " +
                            $"Точка: {hit.point}, Нормаль: {hit.normal}, " +
                            $"Слой: {LayerMask.LayerToName(hit.collider.gameObject.layer)}");

                  if (visualizeRaycastResults)
                  {
                        Debug.DrawRay(ray.origin, ray.direction * hit.distance, Color.green, raycastVisualizationDuration);
                        Debug.DrawRay(hit.point, hit.normal, Color.blue, raycastVisualizationDuration);
                  }
            }
            else
            {
                  lastRaycastResult = "Промах";
                  Debug.LogWarning($"[SegmentationPlaneDebugger] ❌ Рейкаст не попал ни во что! Проверьте наличие коллайдеров в сцене.");

                  if (visualizeRaycastResults)
                  {
                        Debug.DrawRay(ray.origin, ray.direction * maxDistance, Color.red, raycastVisualizationDuration);
                  }

                  // Проверяем, какие объекты вообще есть в сцене
                  CheckSceneColliders();
            }
      }

      private void CheckSceneColliders()
      {
            Collider[] allColliders = FindObjectsOfType<Collider>();
            Debug.Log($"[SegmentationPlaneDebugger] 📦 Всего коллайдеров в сцене: {allColliders.Length}");

            var collidersInLayers = allColliders
                .Where(c => ((1 << c.gameObject.layer) & GetPrivateField<LayerMask>(arManager, "hitLayerMask").value) != 0)
                .ToArray();

            Debug.Log($"[SegmentationPlaneDebugger] 🎯 Коллайдеров в целевых слоях: {collidersInLayers.Length}");

            foreach (var collider in collidersInLayers.Take(5))
            {
                  Debug.Log($"  - {collider.name} (Слой: {LayerMask.LayerToName(collider.gameObject.layer)}, " +
                            $"Позиция: {collider.transform.position}, Активен: {collider.enabled})");
            }
      }

      private string LayerMaskToString(LayerMask mask)
      {
            List<string> layers = new List<string>();
            for (int i = 0; i < 32; i++)
            {
                  if ((mask.value & (1 << i)) != 0)
                  {
                        string layerName = LayerMask.LayerToName(i);
                        if (!string.IsNullOrEmpty(layerName))
                        {
                              layers.Add($"{layerName}({i})");
                        }
                  }
            }
            return string.Join(", ", layers);
      }
}