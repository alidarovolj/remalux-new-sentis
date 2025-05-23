using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using Unity.XR.CoreUtils;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Collections;

/// <summary>
/// Скрипт для валидации критических исправлений проекта
/// Проверяет:
/// - Настройки сегментации (пороги уверенности, сглаживание маски)
/// - Дублирование AR-плоскостей
/// - Блокировку UI (Raycast Target)
/// </summary>
public class CriticalFixesValidator : MonoBehaviour
{
      [Header("Компоненты для проверки")]
      [SerializeField] private WallSegmentation wallSegmentation;
      [SerializeField] private ARManagerInitializer2 arManagerInitializer;
      [SerializeField] private RawImage[] debugRawImages;

      [Header("Настройки валидации")]
      [SerializeField] private bool enablePeriodicValidation = true;
      [SerializeField] private float validationInterval = 5f; // Интервал проверки в секундах

      private float lastValidationTime = 0f;
      private int validationCount = 0;

      void Start()
      {
            // Автоматический поиск компонентов
            if (wallSegmentation == null)
                  wallSegmentation = FindObjectOfType<WallSegmentation>();

            if (arManagerInitializer == null)
                  arManagerInitializer = FindObjectOfType<ARManagerInitializer2>();

            // Поиск всех RawImage в сцене
            if (debugRawImages == null || debugRawImages.Length == 0)
                  debugRawImages = FindObjectsOfType<RawImage>();

            // Первичная проверка
            ValidateAllFixes();

            Debug.Log("[CriticalFixesValidator] Инициализирован и готов к валидации исправлений.");
      }

      void Update()
      {
            if (enablePeriodicValidation && Time.time - lastValidationTime >= validationInterval)
            {
                  ValidateAllFixes();
                  lastValidationTime = Time.time;
            }
      }

      public void ValidateAllFixes()
      {
            validationCount++;
            Debug.Log($"[CriticalFixesValidator] === ВАЛИДАЦИЯ #{validationCount} ===");

            ValidateSegmentationSettings();
            ValidateARPlanesDuplication();
            ValidateUIRaycastBlocking();
            ValidateAdaptiveResolution();

            Debug.Log($"[CriticalFixesValidator] === ВАЛИДАЦИЯ #{validationCount} ЗАВЕРШЕНА ===");
      }

      private void ValidateSegmentationSettings()
      {
            Debug.Log("[CriticalFixesValidator] 🔍 ПРОВЕРКА НАСТРОЕК СЕГМЕНТАЦИИ:");

            if (wallSegmentation == null)
            {
                  Debug.LogError("[CriticalFixesValidator] ❌ WallSegmentation не найден!");
                  return;
            }

            // Проверяем пороги уверенности через рефлексию
            var wallConfidenceField = typeof(WallSegmentation).GetField("wallConfidence",
                BindingFlags.NonPublic | BindingFlags.Instance);
            var segmentationConfidenceThresholdField = typeof(WallSegmentation).GetField("segmentationConfidenceThreshold",
                BindingFlags.Public | BindingFlags.Instance);
            var applyMaskSmoothingField = typeof(WallSegmentation).GetField("applyMaskSmoothing",
                BindingFlags.Public | BindingFlags.Instance);
            var maskBlurSizeField = typeof(WallSegmentation).GetField("maskBlurSize",
                BindingFlags.Public | BindingFlags.Instance);

            if (wallConfidenceField != null)
            {
                  float wallConfidence = (float)wallConfidenceField.GetValue(wallSegmentation);
                  if (wallConfidence >= 0.1f && wallConfidence <= 0.25f)
                        Debug.Log($"[CriticalFixesValidator] ✅ wallConfidence: {wallConfidence:F3} (ИСПРАВЛЕНО)");
                  else
                        Debug.LogWarning($"[CriticalFixesValidator] ⚠️ wallConfidence: {wallConfidence:F3} (рекомендуется 0.1-0.25)");
            }

            if (segmentationConfidenceThresholdField != null)
            {
                  float threshold = (float)segmentationConfidenceThresholdField.GetValue(wallSegmentation);
                  if (threshold >= 0.1f && threshold <= 0.25f)
                        Debug.Log($"[CriticalFixesValidator] ✅ segmentationConfidenceThreshold: {threshold:F3} (ИСПРАВЛЕНО)");
                  else
                        Debug.LogWarning($"[CriticalFixesValidator] ⚠️ segmentationConfidenceThreshold: {threshold:F3} (рекомендуется 0.1-0.25)");
            }

            if (applyMaskSmoothingField != null)
            {
                  bool smoothing = (bool)applyMaskSmoothingField.GetValue(wallSegmentation);
                  if (smoothing)
                        Debug.Log("[CriticalFixesValidator] ✅ applyMaskSmoothing: ВКЛЮЧЕНО (ИСПРАВЛЕНО)");
                  else
                        Debug.LogWarning("[CriticalFixesValidator] ⚠️ applyMaskSmoothing: ОТКЛЮЧЕНО (рекомендуется включить)");
            }

            if (maskBlurSizeField != null)
            {
                  int blurSize = (int)maskBlurSizeField.GetValue(wallSegmentation);
                  if (blurSize >= 3 && blurSize <= 6)
                        Debug.Log($"[CriticalFixesValidator] ✅ maskBlurSize: {blurSize} (ИСПРАВЛЕНО)");
                  else
                        Debug.LogWarning($"[CriticalFixesValidator] ⚠️ maskBlurSize: {blurSize} (рекомендуется 3-6)");
            }
      }

      private void ValidateARPlanesDuplication()
      {
            Debug.Log("[CriticalFixesValidator] 🔍 ПРОВЕРКА ДУБЛИРОВАНИЯ AR-ПЛОСКОСТЕЙ:");

            if (arManagerInitializer == null)
            {
                  Debug.LogError("[CriticalFixesValidator] ❌ ARManagerInitializer2 не найден!");
                  return;
            }

            // Ищем все объекты с именем MyARPlane_Debug
            GameObject[] allPlanes = GameObject.FindObjectsOfType<GameObject>()
                .Where(go => go.name.StartsWith("MyARPlane_Debug"))
                .ToArray();

            Debug.Log($"[CriticalFixesValidator] Найдено AR-плоскостей с префиксом 'MyARPlane_Debug': {allPlanes.Length}");

            if (allPlanes.Length <= 5)
            {
                  Debug.Log("[CriticalFixesValidator] ✅ Количество плоскостей в норме (≤5)");
            }
            else if (allPlanes.Length <= 10)
            {
                  Debug.LogWarning($"[CriticalFixesValidator] ⚠️ Повышенное количество плоскостей ({allPlanes.Length}), возможны дубли");
            }
            else
            {
                  Debug.LogError($"[CriticalFixesValidator] ❌ Критическое количество плоскостей ({allPlanes.Length}), вероятно есть проблема с дублированием");
            }

            // Проверяем на наличие очень близких плоскостей
            var duplicateGroups = new List<List<GameObject>>();
            var processed = new HashSet<GameObject>();

            foreach (var plane in allPlanes)
            {
                  if (processed.Contains(plane)) continue;

                  var group = new List<GameObject> { plane };
                  processed.Add(plane);

                  foreach (var otherPlane in allPlanes)
                  {
                        if (otherPlane == plane || processed.Contains(otherPlane)) continue;

                        float distance = Vector3.Distance(plane.transform.position, otherPlane.transform.position);
                        if (distance < 0.4f) // Используем тот же порог, что в исправлении
                        {
                              group.Add(otherPlane);
                              processed.Add(otherPlane);
                        }
                  }

                  if (group.Count > 1)
                  {
                        duplicateGroups.Add(group);
                  }
            }

            if (duplicateGroups.Count == 0)
            {
                  Debug.Log("[CriticalFixesValidator] ✅ Дублирующихся плоскостей не обнаружено");
            }
            else
            {
                  Debug.LogWarning($"[CriticalFixesValidator] ⚠️ Обнаружено {duplicateGroups.Count} групп потенциально дублирующихся плоскостей:");
                  foreach (var group in duplicateGroups)
                  {
                        string names = string.Join(", ", group.Select(p => p.name));
                        Debug.LogWarning($"[CriticalFixesValidator]   Группа дублей: {names}");
                  }
            }
      }

      private void ValidateUIRaycastBlocking()
      {
            Debug.Log("[CriticalFixesValidator] 🔍 ПРОВЕРКА БЛОКИРОВКИ UI:");

            int totalRawImages = 0;
            int blockingRawImages = 0;

            foreach (var rawImage in debugRawImages)
            {
                  if (rawImage == null) continue;

                  totalRawImages++;

                  if (rawImage.raycastTarget)
                  {
                        blockingRawImages++;
                        Debug.LogWarning($"[CriticalFixesValidator] ⚠️ RawImage '{rawImage.gameObject.name}' имеет raycastTarget = true (может блокировать касания)");
                  }
            }

            if (blockingRawImages == 0)
            {
                  Debug.Log($"[CriticalFixesValidator] ✅ Все RawImage ({totalRawImages}) имеют raycastTarget = false (ИСПРАВЛЕНО)");
            }
            else
            {
                  Debug.LogWarning($"[CriticalFixesValidator] ⚠️ {blockingRawImages} из {totalRawImages} RawImage могут блокировать касания");
            }
      }

      private void ValidateAdaptiveResolution()
      {
            Debug.Log("[CriticalFixesValidator] 🔍 ПРОВЕРКА АДАПТИВНОГО РАЗРЕШЕНИЯ И ПРОИЗВОДИТЕЛЬНОСТИ:");

            if (wallSegmentation == null)
            {
                  Debug.LogError("[CriticalFixesValidator] ❌ WallSegmentation не найден для проверки адаптивного разрешения!");
                  return;
            }

            try
            {
                  // Получаем информацию о производительности
                  float avgProcessingTime = wallSegmentation.GetAverageProcessingTimeMs();
                  Vector2Int currentResolution = wallSegmentation.GetCurrentResolution();
                  float qualityScore = wallSegmentation.GetLastQualityScore();

                  Debug.Log($"[CriticalFixesValidator] Текущие метрики производительности:");
                  Debug.Log($"[CriticalFixesValidator]   📊 Среднее время обработки: {avgProcessingTime:F1}ms");
                  Debug.Log($"[CriticalFixesValidator]   🖼️ Текущее разрешение: {currentResolution.x}x{currentResolution.y}");
                  Debug.Log($"[CriticalFixesValidator]   ⭐ Оценка качества маски: {qualityScore:F2}");

                  // Проверяем производительность
                  if (avgProcessingTime < 16f)
                  {
                        Debug.Log("[CriticalFixesValidator] ✅ Отличная производительность (< 16ms)");
                  }
                  else if (avgProcessingTime < 33f)
                  {
                        Debug.Log("[CriticalFixesValidator] ✅ Хорошая производительность (< 33ms)");
                  }
                  else if (avgProcessingTime < 50f)
                  {
                        Debug.LogWarning("[CriticalFixesValidator] ⚠️ Умеренная производительность (33-50ms)");
                  }
                  else
                  {
                        Debug.LogWarning("[CriticalFixesValidator] ⚠️ Низкая производительность (> 50ms) - рассмотрите уменьшение разрешения");
                  }

                  // Проверяем разрешение
                  if (currentResolution.x >= 768)
                  {
                        Debug.Log("[CriticalFixesValidator] ✅ Высокое разрешение (768+px) - максимальное качество");
                  }
                  else if (currentResolution.x >= 512)
                  {
                        Debug.Log("[CriticalFixesValidator] ✅ Стандартное разрешение (512+px) - хорошее качество");
                  }
                  else if (currentResolution.x >= 384)
                  {
                        Debug.LogWarning("[CriticalFixesValidator] ⚠️ Низкое разрешение (384+px) - производительность важнее качества");
                  }
                  else
                  {
                        Debug.LogWarning("[CriticalFixesValidator] ⚠️ Очень низкое разрешение (< 384px) - возможны проблемы с качеством");
                  }

                  // Проверяем качество маски
                  if (qualityScore > 0.8f)
                  {
                        Debug.Log("[CriticalFixesValidator] ✅ Отличное качество маски (> 0.8)");
                  }
                  else if (qualityScore > 0.6f)
                  {
                        Debug.Log("[CriticalFixesValidator] ✅ Хорошее качество маски (> 0.6)");
                  }
                  else if (qualityScore > 0.4f)
                  {
                        Debug.LogWarning("[CriticalFixesValidator] ⚠️ Умеренное качество маски (0.4-0.6)");
                  }
                  else if (qualityScore > 0)
                  {
                        Debug.LogWarning("[CriticalFixesValidator] ⚠️ Низкое качество маски (< 0.4)");
                  }
                  else
                  {
                        Debug.Log("[CriticalFixesValidator] ℹ️ Качество маски еще не оценено");
                  }
            }
            catch (System.Exception e)
            {
                  Debug.LogError($"[CriticalFixesValidator] ❌ Ошибка при валидации адаптивного разрешения: {e.Message}");
            }
      }

      [ContextMenu("Принудительная валидация")]
      public void ForceValidation()
      {
            ValidateAllFixes();
      }

      [ContextMenu("Исправить блокирующие RawImage")]
      public void FixBlockingRawImages()
      {
            int fixedCount = 0;
            foreach (var rawImage in debugRawImages)
            {
                  if (rawImage != null && rawImage.raycastTarget)
                  {
                        rawImage.raycastTarget = false;
                        Debug.Log($"[CriticalFixesValidator] Исправлен raycastTarget для {rawImage.gameObject.name}");
                        fixedCount++;
                  }
            }
            Debug.Log($"[CriticalFixesValidator] Исправлено {fixedCount} блокирующих RawImage");
      }

      [ContextMenu("Включить адаптивное разрешение")]
      public void EnableAdaptiveResolution()
      {
            if (wallSegmentation != null)
            {
                  wallSegmentation.SetAdaptiveResolution(true);
                  Debug.Log("[CriticalFixesValidator] ✅ Адаптивное разрешение включено");
            }
            else
            {
                  Debug.LogError("[CriticalFixesValidator] ❌ WallSegmentation не найден!");
            }
      }

      [ContextMenu("Отключить адаптивное разрешение")]
      public void DisableAdaptiveResolution()
      {
            if (wallSegmentation != null)
            {
                  wallSegmentation.SetAdaptiveResolution(false);
                  Debug.Log("[CriticalFixesValidator] ⚠️ Адаптивное разрешение отключено");
            }
            else
            {
                  Debug.LogError("[CriticalFixesValidator] ❌ WallSegmentation не найден!");
            }
      }

      [ContextMenu("Установить высокое качество (768px)")]
      public void SetHighQuality()
      {
            if (wallSegmentation != null)
            {
                  wallSegmentation.SetFixedResolution(768, 768);
                  Debug.Log("[CriticalFixesValidator] 🎯 Установлено высокое качество (768x768)");
            }
            else
            {
                  Debug.LogError("[CriticalFixesValidator] ❌ WallSegmentation не найден!");
            }
      }

      [ContextMenu("Установить стандартное качество (512px)")]
      public void SetStandardQuality()
      {
            if (wallSegmentation != null)
            {
                  wallSegmentation.SetFixedResolution(512, 512);
                  Debug.Log("[CriticalFixesValidator] ⚖️ Установлено стандартное качество (512x512)");
            }
            else
            {
                  Debug.LogError("[CriticalFixesValidator] ❌ WallSegmentation не найден!");
            }
      }

      [ContextMenu("Установить производительный режим (384px)")]
      public void SetPerformanceMode()
      {
            if (wallSegmentation != null)
            {
                  wallSegmentation.SetFixedResolution(384, 384);
                  Debug.Log("[CriticalFixesValidator] ⚡ Установлен производительный режим (384x384)");
            }
            else
            {
                  Debug.LogError("[CriticalFixesValidator] ❌ WallSegmentation не найден!");
            }
      }

      // ================================
      // ПРИОРИТЕТ 2: Реалистичное нанесение краски
      // ================================

      [ContextMenu("Priority 2: Validate Realistic Paint System")]
      public void ValidateRealisticPaintSystem()
      {
            Debug.Log("=== ПРИОРИТЕТ 2: ПРОВЕРКА СИСТЕМЫ РЕАЛИСТИЧНОЙ ПОКРАСКИ ===");

            bool hasWallPaintShader = CheckWallPaintShader();
            bool hasPaintMaterials = CheckPaintMaterials();
            bool hasColorManager = CheckColorManager();
            bool hasProperMaterialSetup = CheckMaterialParameters();

            bool allPriority2Checks = hasWallPaintShader && hasPaintMaterials && hasColorManager && hasProperMaterialSetup;

            if (allPriority2Checks)
            {
                  Debug.Log("✅ ПРИОРИТЕТ 2 ВЫПОЛНЕН: Система реалистичной покраски полностью функциональна!");
            }
            else
            {
                  Debug.LogWarning("⚠️ ПРИОРИТЕТ 2 НЕ ЗАВЕРШЕН: Требуются дополнительные исправления");
            }
      }

      private bool CheckWallPaintShader()
      {
            Shader wallPaintShader = Shader.Find("Custom/WallPaint");
            if (wallPaintShader != null)
            {
                  Debug.Log("✅ Шейдер Custom/WallPaint найден");
                  return true;
            }
            else
            {
                  Debug.LogError("❌ Шейдер Custom/WallPaint не найден! Требуется для реалистичного наложения краски");
                  return false;
            }
      }

      private bool CheckPaintMaterials()
      {
            var arManager = FindObjectOfType<ARManagerInitializer2>();
            if (arManager != null)
            {
                  Material vertMaterial = arManager.VerticalPlaneMaterial;
                  if (vertMaterial != null)
                  {
                        if (vertMaterial.shader.name == "Custom/WallPaint")
                        {
                              Debug.Log($"✅ Материал вертикальных плоскостей использует правильный шейдер: {vertMaterial.shader.name}");

                              // Проверяем ключевые параметры
                              bool hasColorProp = vertMaterial.HasProperty("_PaintColor");
                              bool hasBlendProp = vertMaterial.HasProperty("_BlendFactor");
                              bool hasMaskProp = vertMaterial.HasProperty("_SegmentationMask");

                              Debug.Log($"  - _PaintColor: {(hasColorProp ? "✅" : "❌")}");
                              Debug.Log($"  - _BlendFactor: {(hasBlendProp ? "✅" : "❌")}");
                              Debug.Log($"  - _SegmentationMask: {(hasMaskProp ? "✅" : "❌")}");

                              return hasColorProp && hasBlendProp && hasMaskProp;
                        }
                        else
                        {
                              Debug.LogWarning($"⚠️ Материал плоскостей использует неправильный шейдер: {vertMaterial.shader.name}");
                              return false;
                        }
                  }
                  else
                  {
                        Debug.LogWarning("⚠️ Материал вертикальных плоскостей не назначен в ARManagerInitializer2");
                        return false;
                  }
            }
            else
            {
                  Debug.LogError("❌ ARManagerInitializer2 не найден!");
                  return false;
            }
      }

      private bool CheckColorManager()
      {
            var colorManager = FindObjectOfType<ARWallPaintColorManager>();
            if (colorManager != null)
            {
                  Debug.Log("✅ ARWallPaintColorManager найден и активен");

                  Color currentColor = colorManager.GetCurrentColor();
                  float transparency = colorManager.GetCurrentTransparency();

                  Debug.Log($"  - Текущий цвет: RGB({currentColor.r:F2}, {currentColor.g:F2}, {currentColor.b:F2})");
                  Debug.Log($"  - Прозрачность: {transparency:F2} ({transparency * 100:F0}%)");

                  return true;
            }
            else
            {
                  Debug.LogWarning("⚠️ ARWallPaintColorManager не найден. Система управления цветами недоступна");
                  return false;
            }
      }

      private bool CheckMaterialParameters()
      {
            var arManager = FindObjectOfType<ARManagerInitializer2>();
            if (arManager == null) return false;

            // Получаем сгенерированные плоскости через рефлексию
            var generatedPlanesField = typeof(ARManagerInitializer2).GetField("generatedPlanes",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (generatedPlanesField != null)
            {
                  var generatedPlanes = (List<GameObject>)generatedPlanesField.GetValue(arManager);

                  if (generatedPlanes != null && generatedPlanes.Count > 0)
                  {
                        int properlyConfiguredPlanes = 0;

                        foreach (var plane in generatedPlanes)
                        {
                              if (plane == null) continue;

                              var renderer = plane.GetComponent<MeshRenderer>();
                              if (renderer != null && renderer.material != null)
                              {
                                    var material = renderer.material;

                                    if (material.shader.name == "Custom/WallPaint")
                                    {
                                          bool hasValidPaintColor = material.HasProperty("_PaintColor");
                                          bool hasValidBlendFactor = material.HasProperty("_BlendFactor");
                                          bool hasUseMaskKeyword = material.IsKeywordEnabled("USE_MASK");
                                          bool hasARSpaceKeyword = material.IsKeywordEnabled("USE_AR_WORLD_SPACE");

                                          if (hasValidPaintColor && hasValidBlendFactor && hasUseMaskKeyword && hasARSpaceKeyword)
                                          {
                                                properlyConfiguredPlanes++;
                                          }
                                    }
                              }
                        }

                        Debug.Log($"✅ Правильно настроенных плоскостей: {properlyConfiguredPlanes}/{generatedPlanes.Count}");
                        return properlyConfiguredPlanes > 0;
                  }
                  else
                  {
                        Debug.Log("ℹ️ Пока нет созданных плоскостей для проверки");
                        return true; // Считаем нормальным если плоскостей еще нет
                  }
            }

            return false;
      }

      [ContextMenu("Priority 2: Apply Test Colors")]
      public void ApplyTestColors()
      {
            var colorManager = FindObjectOfType<ARWallPaintColorManager>();
            if (colorManager != null)
            {
                  Debug.Log("[CriticalFixesValidator] Применяем тестовые цвета...");

                  StartCoroutine(ColorTestSequence(colorManager));
            }
            else
            {
                  Debug.LogWarning("ARWallPaintColorManager не найден для тестирования цветов");
            }
      }

      private System.Collections.IEnumerator ColorTestSequence(ARWallPaintColorManager colorManager)
      {
            Color[] testColors = {
                  new Color(0.9f, 0.2f, 0.2f, 0.7f), // Красный
                  new Color(0.2f, 0.7f, 0.3f, 0.7f), // Зеленый
                  new Color(0.2f, 0.4f, 0.9f, 0.7f), // Синий
                  new Color(0.9f, 0.7f, 0.2f, 0.7f), // Желтый
            };

            string[] colorNames = { "Красный", "Зеленый", "Синий", "Желтый" };

            for (int i = 0; i < testColors.Length; i++)
            {
                  Debug.Log($"🎨 Применяем {colorNames[i]} цвет...");
                  colorManager.SetColor(testColors[i]);
                  yield return new WaitForSeconds(1.5f);
            }

            Debug.Log("🎨 Тестирование цветов завершено!");
      }

      [ContextMenu("Priority 2: Test Transparency")]
      public void TestTransparency()
      {
            var colorManager = FindObjectOfType<ARWallPaintColorManager>();
            if (colorManager != null)
            {
                  StartCoroutine(TransparencyTestSequence(colorManager));
            }
      }

      private System.Collections.IEnumerator TransparencyTestSequence(ARWallPaintColorManager colorManager)
      {
            float[] transparencyLevels = { 0.3f, 0.6f, 0.9f, 0.6f };
            string[] transparencyNames = { "30%", "60%", "90%", "60%" };

            for (int i = 0; i < transparencyLevels.Length; i++)
            {
                  Debug.Log($"🔧 Устанавливаем прозрачность {transparencyNames[i]}...");
                  colorManager.SetTransparency(transparencyLevels[i]);
                  yield return new WaitForSeconds(1.0f);
            }

            Debug.Log("🔧 Тестирование прозрачности завершено!");
      }

      [ContextMenu("Priority 2: Force Update All Materials")]
      public void ForceUpdateAllMaterials()
      {
            var arManager = FindObjectOfType<ARManagerInitializer2>();
            if (arManager == null)
            {
                  Debug.LogError("ARManagerInitializer2 не найден!");
                  return;
            }

            // Получаем сгенерированные плоскости через рефлексию
            var generatedPlanesField = typeof(ARManagerInitializer2).GetField("generatedPlanes",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (generatedPlanesField != null)
            {
                  var generatedPlanes = (List<GameObject>)generatedPlanesField.GetValue(arManager);

                  if (generatedPlanes != null)
                  {
                        int updatedCount = 0;

                        foreach (var plane in generatedPlanes)
                        {
                              if (plane == null) continue;

                              var renderer = plane.GetComponent<MeshRenderer>();
                              if (renderer != null && renderer.material != null)
                              {
                                    var material = renderer.material;

                                    // Принудительно настраиваем материал для реалистичной покраски
                                    if (material.shader.name == "Custom/WallPaint")
                                    {
                                          material.EnableKeyword("USE_MASK");
                                          material.EnableKeyword("USE_AR_WORLD_SPACE");

                                          // Устанавливаем реалистичные параметры
                                          if (material.HasProperty("_PaintColor"))
                                          {
                                                material.SetColor("_PaintColor", new Color(0.8f, 0.4f, 0.2f, 0.7f));
                                          }
                                          if (material.HasProperty("_BlendFactor"))
                                          {
                                                material.SetFloat("_BlendFactor", 0.7f);
                                          }

                                          updatedCount++;
                                    }
                              }
                        }

                        Debug.Log($"🔧 Принудительно обновлено материалов: {updatedCount}");
                  }
            }
      }

      [ContextMenu("Диагностика проблем рейкастинга")]
      public void DiagnoseRaycastIssues()
      {
            Debug.Log("[CriticalFixesValidator] === ДИАГНОСТИКА ПРОБЛЕМ РЕЙКАСТИНГА ===");

            // 1. Проверяем наличие коллайдеров в сцене
            Collider[] allColliders = FindObjectsOfType<Collider>();
            Debug.Log($"[CriticalFixesValidator] Всего коллайдеров в сцене: {allColliders.Length}");

            int enabledColliders = 0;
            foreach (var collider in allColliders)
            {
                  if (collider.enabled && collider.gameObject.activeInHierarchy)
                  {
                        enabledColliders++;
                        Debug.Log($"[CriticalFixesValidator] Активный коллайдер: {collider.name} ({collider.GetType().Name}) на слое {LayerMask.LayerToName(collider.gameObject.layer)}");
                  }
            }

            Debug.Log($"[CriticalFixesValidator] Активных коллайдеров: {enabledColliders}");

            // 2. Проверяем настройки рейкастинга в ARManagerInitializer2
            var arManager = FindObjectOfType<ARManagerInitializer2>();
            if (arManager != null)
            {
                  // Получаем LayerMask через рефлексию
                  var hitLayerMaskField = typeof(ARManagerInitializer2).GetField("hitLayerMask", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                  if (hitLayerMaskField != null)
                  {
                        var layerMask = (LayerMask)hitLayerMaskField.GetValue(arManager);
                        Debug.Log($"[CriticalFixesValidator] hitLayerMask в ARManagerInitializer2: {layerMask.value} ({layerMask})");
                  }
            }

            // 3. Тестовый рейкаст
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                  Vector3 rayOrigin = mainCamera.transform.position;
                  Vector3 rayDirection = mainCamera.transform.forward;

                  Debug.Log($"[CriticalFixesValidator] Тестовый рейкаст из позиции камеры: {rayOrigin} в направлении: {rayDirection}");

                  RaycastHit hit;
                  if (Physics.Raycast(rayOrigin, rayDirection, out hit, 100f, -1))
                  {
                        Debug.Log($"[CriticalFixesValidator] ✅ Тестовый рейкаст ПОПАЛ в {hit.collider.name} на расстоянии {hit.distance:F2}м");
                  }
                  else
                  {
                        Debug.LogWarning($"[CriticalFixesValidator] ❌ Тестовый рейкаст ПРОМАХ");
                  }
            }

            // 4. Проверяем симулированную среду
            GameObject simulationEnv = GameObject.Find("Basic Simulation Environment") ??
                                       GameObject.Find("Simulation Environment") ??
                                       GameObject.Find("Simulation Environment (Auto-Created)");

            if (simulationEnv != null)
            {
                  var envColliders = simulationEnv.GetComponentsInChildren<Collider>();
                  Debug.Log($"[CriticalFixesValidator] Симулированная среда '{simulationEnv.name}' имеет {envColliders.Length} коллайдеров");
            }
            else
            {
                  Debug.LogWarning("[CriticalFixesValidator] Симулированная среда не найдена!");
            }

            Debug.Log("[CriticalFixesValidator] === КОНЕЦ ДИАГНОСТИКИ ===");
      }

      [ContextMenu("Принудительно создать симулированную среду")]
      public void ForceCreateSimulationEnvironment()
      {
            Debug.Log("[CriticalFixesValidator] Принудительное создание симулированной среды...");

            // Удаляем старые среды
            GameObject[] oldEnvs = {
                  GameObject.Find("Basic Simulation Environment"),
                  GameObject.Find("Simulation Environment"),
                  GameObject.Find("Simulation Environment (Auto-Created)")
            };

            foreach (var env in oldEnvs)
            {
                  if (env != null)
                  {
                        Debug.Log($"[CriticalFixesValidator] Удаляю старую среду: {env.name}");
                        DestroyImmediate(env);
                  }
            }

            // Создаем новую среду
            var sceneHelper = FindObjectOfType<SceneSetupHelper>();
            if (sceneHelper == null)
            {
                  var helperGO = new GameObject("SceneSetupHelper");
                  sceneHelper = helperGO.AddComponent<SceneSetupHelper>();
            }

            // Сбрасываем флаг через рефлексию
            var environmentCreatedField = typeof(SceneSetupHelper).GetField("environmentCreated", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            if (environmentCreatedField != null)
            {
                  environmentCreatedField.SetValue(null, false);
            }

            // Вызываем создание среды
            var setupMethod = typeof(SceneSetupHelper).GetMethod("SetupEnvironment", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            if (setupMethod != null)
            {
                  setupMethod.Invoke(sceneHelper, null);
                  Debug.Log("[CriticalFixesValidator] ✅ Симулированная среда принудительно создана");
            }
      }
}