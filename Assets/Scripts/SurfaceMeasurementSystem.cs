using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Collections.Generic;
using System.Linq;
using System.Collections;

/// <summary>
/// Система точного измерения поверхностей для создания плоскостей реальных размеров
/// </summary>
public class SurfaceMeasurementSystem : MonoBehaviour
{
      [Header("Основные компоненты")]
      [SerializeField] private ARPlaneManager planeManager;
      [SerializeField] private Camera arCamera;

      [Header("Настройки измерения")]
      [Tooltip("Использовать многоточечное измерение для более точных размеров")]
      [SerializeField] private bool useMultiPointMeasurement = true;

      [Tooltip("Количество точек для измерения по каждой оси")]
      [Range(3, 10)]
      [SerializeField] private int measurementPoints = 5;

      [Tooltip("Множитель коррекции размера (1.0 = без изменений)")]
      [Range(0.5f, 2.0f)]
      [SerializeField] private float sizeCorrectionFactor = 1.2f;

      [Header("Калибровка размеров")]
      [Tooltip("Автоматическая калибровка на основе известных объектов")]
      [SerializeField] private bool enableAutoCalibration = true;

      [Tooltip("Размер эталонного объекта для калибровки (например, лист A4 = 0.21м)")]
      [SerializeField] private float referenceObjectSize = 0.21f;

      [Header("Фильтрация измерений")]
      [Tooltip("Минимальный размер поверхности в метрах")]
      [SerializeField] private float minSurfaceSize = 0.3f;

      [Tooltip("Максимальный размер поверхности в метрах")]
      [SerializeField] private float maxSurfaceSize = 10.0f;

      [Tooltip("Сглаживание измерений между кадрами")]
      [SerializeField] private bool smoothMeasurements = true;

      [Header("Отладка")]
      [SerializeField] private bool debugMode = true;
      [SerializeField] private bool visualizeMeasurements = false;

      // Приватные поля
      private Dictionary<ARPlane, PreciseMeasurement> measurements = new Dictionary<ARPlane, PreciseMeasurement>();
      private float currentCalibrationFactor = 1.0f;
      private List<GameObject> debugVisualizations = new List<GameObject>();

      // События
      public System.Action<ARPlane, Vector2> OnSurfaceMeasured;

      private void Start()
      {
            InitializeSystem();
      }

      private void InitializeSystem()
      {
            if (arCamera == null)
                  arCamera = Camera.main;

            if (planeManager == null)
                  planeManager = FindObjectOfType<ARPlaneManager>();

            if (planeManager != null)
            {
                  planeManager.planesChanged += OnPlanesChanged;
            }

            if (enableAutoCalibration)
            {
                  StartCoroutine(AutoCalibrationRoutine());
            }

            Debug.Log("[SurfaceMeasurementSystem] ✅ Система точного измерения поверхностей инициализирована");
      }

      private void OnDestroy()
      {
            if (planeManager != null)
            {
                  planeManager.planesChanged -= OnPlanesChanged;
            }

            ClearDebugVisualizations();
      }

      /// <summary>
      /// Получает точные размеры поверхности
      /// </summary>
      public Vector2 GetPreciseSurfaceSize(ARPlane plane)
      {
            if (plane == null) return Vector2.zero;

            // Используем сохраненное измерение если есть
            if (measurements.ContainsKey(plane))
            {
                  var measurement = measurements[plane];
                  return measurement.CalibratedSize;
            }

            // Иначе делаем новое измерение
            return MeasurePlane(plane);
      }

      /// <summary>
      /// Измеряет плоскость с повышенной точностью
      /// </summary>
      private Vector2 MeasurePlane(ARPlane plane)
      {
            var measurement = new PreciseMeasurement();
            measurement.PlaneId = plane.trackableId;
            measurement.OriginalSize = plane.size;
            measurement.Timestamp = Time.time;

            Vector2 preciseSize = Vector2.zero;

            if (useMultiPointMeasurement)
            {
                  preciseSize = MeasureUsingMultiplePoints(plane);
            }
            else
            {
                  preciseSize = plane.size;
            }

            // Применяем коррекцию размера
            preciseSize *= sizeCorrectionFactor;

            // Применяем калибровку
            preciseSize *= currentCalibrationFactor;

            // Сглаживание если включено
            if (smoothMeasurements && measurements.ContainsKey(plane))
            {
                  var previousSize = measurements[plane].CalibratedSize;
                  preciseSize = Vector2.Lerp(previousSize, preciseSize, 0.3f);
            }

            // Ограничиваем размеры
            preciseSize.x = Mathf.Clamp(preciseSize.x, minSurfaceSize, maxSurfaceSize);
            preciseSize.y = Mathf.Clamp(preciseSize.y, minSurfaceSize, maxSurfaceSize);

            measurement.CalibratedSize = preciseSize;
            measurement.Confidence = CalculateConfidence(plane, preciseSize);

            measurements[plane] = measurement;

            if (debugMode)
            {
                  Debug.Log($"[SurfaceMeasurementSystem] 📏 Измерена поверхность: " +
                           $"Оригинал: {plane.size.x:F2}×{plane.size.y:F2}м → " +
                           $"Точное: {preciseSize.x:F2}×{preciseSize.y:F2}м " +
                           $"(уверенность: {measurement.Confidence:F1})");
            }

            if (visualizeMeasurements)
            {
                  CreateMeasurementVisualization(plane, preciseSize);
            }

            OnSurfaceMeasured?.Invoke(plane, preciseSize);
            return preciseSize;
      }

      /// <summary>
      /// Измерение с использованием множественных точек
      /// </summary>
      private Vector2 MeasureUsingMultiplePoints(ARPlane plane)
      {
            // В симуляции Unity или при недостатке AR данных используем упрощенный подход
            if (Application.isEditor || !Application.isMobilePlatform)
            {
                  return plane.size * sizeCorrectionFactor;
            }

            var samplePoints = GenerateSamplePoints(plane);
            var validPoints = new List<Vector3>();

            foreach (var screenPoint in samplePoints)
            {
                  Ray ray = arCamera.ScreenPointToRay(screenPoint);
                  RaycastHit hit;

                  if (Physics.Raycast(ray, out hit, 10.0f))
                  {
                        // Проверяем что точка принадлежит этой плоскости
                        float distanceToPlane = Vector3.Dot(hit.point - plane.center, plane.normal);
                        if (Mathf.Abs(distanceToPlane) < 0.1f) // Увеличили допуск до 10см
                        {
                              validPoints.Add(hit.point);
                        }
                  }
            }

            // Снизили требования к минимальному количеству точек
            if (validPoints.Count < 2)
            {
                  if (debugMode)
                        Debug.LogWarning($"[SurfaceMeasurementSystem] Недостаточно точек для измерения: {validPoints.Count}, используем размер AR Foundation");
                  return plane.size * sizeCorrectionFactor;
            }

            var calculatedSize = CalculateSizeFromPoints(validPoints, plane);

            // Проверяем разумность результата
            if (calculatedSize.x < 0.1f || calculatedSize.y < 0.1f ||
                calculatedSize.x > 20f || calculatedSize.y > 20f)
            {
                  if (debugMode)
                        Debug.LogWarning($"[SurfaceMeasurementSystem] Неразумный размер {calculatedSize.x:F2}×{calculatedSize.y:F2}м, используем AR Foundation");
                  return plane.size * sizeCorrectionFactor;
            }

            return calculatedSize;
      }

      /// <summary>
      /// Генерирует точки для сэмплирования на плоскости
      /// </summary>
      private List<Vector3> GenerateSamplePoints(ARPlane plane)
      {
            var points = new List<Vector3>();
            var center = plane.center;
            var size = plane.size;

            // Создаем локальную систему координат для плоскости
            var forward = plane.normal;
            var right = Vector3.Cross(forward, Vector3.up).normalized;
            var up = Vector3.Cross(right, forward).normalized;

            // Генерируем сетку точек
            for (int x = 0; x < measurementPoints; x++)
            {
                  for (int y = 0; y < measurementPoints; y++)
                  {
                        float u = (x / (float)(measurementPoints - 1)) - 0.5f;
                        float v = (y / (float)(measurementPoints - 1)) - 0.5f;

                        var worldPoint = center + right * u * size.x + up * v * size.y;
                        var screenPoint = arCamera.WorldToScreenPoint(worldPoint);

                        if (screenPoint.z > 0 && screenPoint.x >= 0 && screenPoint.x <= Screen.width &&
                            screenPoint.y >= 0 && screenPoint.y <= Screen.height)
                        {
                              points.Add(screenPoint);
                        }
                  }
            }

            return points;
      }

      /// <summary>
      /// Вычисляет размер поверхности по точкам
      /// </summary>
      private Vector2 CalculateSizeFromPoints(List<Vector3> points, ARPlane plane)
      {
            if (points.Count < 4) return plane.size;

            // Создаем локальную систему координат
            var forward = plane.normal;
            var right = Vector3.Cross(forward, Vector3.up).normalized;
            var up = Vector3.Cross(right, forward).normalized;
            var center = points.Aggregate(Vector3.zero, (sum, p) => sum + p) / points.Count;

            // Проецируем точки на локальные оси
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;

            foreach (var point in points)
            {
                  var localPoint = point - center;
                  float x = Vector3.Dot(localPoint, right);
                  float y = Vector3.Dot(localPoint, up);

                  minX = Mathf.Min(minX, x);
                  maxX = Mathf.Max(maxX, x);
                  minY = Mathf.Min(minY, y);
                  maxY = Mathf.Max(maxY, y);
            }

            float width = maxX - minX;
            float height = maxY - minY;

            return new Vector2(Mathf.Abs(width), Mathf.Abs(height));
      }

      /// <summary>
      /// Вычисляет уверенность в измерении
      /// </summary>
      private float CalculateConfidence(ARPlane plane, Vector2 measuredSize)
      {
            float confidence = 0.7f; // Базовая уверенность

            // Увеличиваем уверенность для стабильных плоскостей
            if (plane.trackingState == TrackingState.Tracking)
            {
                  confidence += 0.2f;
            }

            // Проверяем разумность размеров
            if (measuredSize.x > minSurfaceSize && measuredSize.x < maxSurfaceSize &&
                measuredSize.y > minSurfaceSize && measuredSize.y < maxSurfaceSize)
            {
                  confidence += 0.1f;
            }

            // Проверяем соотношение сторон
            float aspectRatio = Mathf.Max(measuredSize.x, measuredSize.y) / Mathf.Min(measuredSize.x, measuredSize.y);
            if (aspectRatio < 5.0f) // Разумное соотношение сторон
            {
                  confidence += 0.1f;
            }

            return Mathf.Clamp01(confidence);
      }

      /// <summary>
      /// Автоматическая калибровка системы
      /// </summary>
      private System.Collections.IEnumerator AutoCalibrationRoutine()
      {
            yield return new WaitForSeconds(3.0f); // Даем время AR системе стабилизироваться

            // В симуляции Unity автокалибровка не нужна
            if (Application.isEditor || !Application.isMobilePlatform)
            {
                  if (debugMode)
                  {
                        Debug.Log("[SurfaceMeasurementSystem] 🔧 Автокалибровка пропущена в симуляции Unity. Используем коэффициент по умолчанию.");
                  }
                  currentCalibrationFactor = sizeCorrectionFactor; // Используем size correction factor как базовый коэффициент
                  yield break;
            }

            if (debugMode)
            {
                  Debug.Log("[SurfaceMeasurementSystem] 🔧 Начинаю автоматическую калибровку...");
            }

            // Ищем небольшие плоскости для калибровки (упрощенная версия)
            var suitablePlanes = new List<ARPlane>();
            foreach (var plane in planeManager.trackables)
            {
                  // Ищем плоскости подходящего размера для калибровки
                  if (plane.size.x > 0.15f && plane.size.x < 0.35f &&
                      plane.size.y > 0.15f && plane.size.y < 0.35f)
                  {
                        suitablePlanes.Add(plane);
                  }
            }

            if (suitablePlanes.Count > 0)
            {
                  // Предполагаем что это стандартные объекты (лист A4, книга, планшет)
                  float totalSize = 0f;
                  foreach (var plane in suitablePlanes)
                  {
                        totalSize += (plane.size.x + plane.size.y) / 2f;
                  }
                  var avgSize = totalSize / suitablePlanes.Count;
                  currentCalibrationFactor = referenceObjectSize / avgSize;

                  if (debugMode)
                  {
                        Debug.Log($"[SurfaceMeasurementSystem] ✅ Автокалибровка завершена. " +
                                 $"Коэффициент: {currentCalibrationFactor:F3} " +
                                 $"(найдено {suitablePlanes.Count} эталонных объектов)");
                  }
            }
            else
            {
                  if (debugMode)
                  {
                        Debug.Log("[SurfaceMeasurementSystem] ℹ️ Эталонные объекты для калибровки не найдены, используем размер по умолчанию");
                  }
                  // Используем size correction factor как fallback
                  currentCalibrationFactor = sizeCorrectionFactor;
            }
      }

      /// <summary>
      /// Ручная калибровка с указанием реального размера объекта
      /// </summary>
      public void ManualCalibration(ARPlane referencePlane, float realSize)
      {
            if (referencePlane == null) return;

            float measuredSize = (referencePlane.size.x + referencePlane.size.y) / 2f;
            currentCalibrationFactor = realSize / measuredSize;

            if (debugMode)
            {
                  Debug.Log($"[SurfaceMeasurementSystem] 🎯 Ручная калибровка: коэффициент {currentCalibrationFactor:F3}");
            }
      }

      /// <summary>
      /// Создает визуализацию измерения
      /// </summary>
      private void CreateMeasurementVisualization(ARPlane plane, Vector2 size)
      {
            var visualization = GameObject.CreatePrimitive(PrimitiveType.Cube);
            visualization.name = $"MeasurementViz_{plane.trackableId}";
            visualization.transform.position = plane.center + plane.normal * 0.01f;
            visualization.transform.rotation = Quaternion.LookRotation(plane.normal);
            visualization.transform.localScale = new Vector3(size.x, size.y, 0.02f);

            var renderer = visualization.GetComponent<Renderer>();
            renderer.material.color = new Color(0, 1, 0, 0.3f); // Полупрозрачный зеленый

            debugVisualizations.Add(visualization);

            // Автоматически удаляем через 5 секунд
            Destroy(visualization, 5.0f);
      }

      private void ClearDebugVisualizations()
      {
            foreach (var obj in debugVisualizations)
            {
                  if (obj != null) Destroy(obj);
            }
            debugVisualizations.Clear();
      }

      private void OnPlanesChanged(ARPlanesChangedEventArgs args)
      {
            // Измеряем новые и обновленные плоскости
            foreach (var plane in args.added.Concat(args.updated))
            {
                  MeasurePlane(plane);
            }

            // Удаляем данные для удаленных плоскостей
            foreach (var plane in args.removed)
            {
                  if (measurements.ContainsKey(plane))
                  {
                        measurements.Remove(plane);
                  }
            }
      }

      [ContextMenu("Показать все измерения")]
      public void ShowAllMeasurements()
      {
            Debug.Log($"[SurfaceMeasurementSystem] 📊 Всего измерений: {measurements.Count}");
            foreach (var kvp in measurements)
            {
                  var m = kvp.Value;
                  Debug.Log($"  Плоскость {kvp.Key}: {m.OriginalSize.x:F2}×{m.OriginalSize.y:F2}м → " +
                           $"{m.CalibratedSize.x:F2}×{m.CalibratedSize.y:F2}м (доверие: {m.Confidence:F1})");
            }
      }

      [ContextMenu("Сбросить калибровку")]
      public void ResetCalibration()
      {
            currentCalibrationFactor = 1.0f;
            measurements.Clear();
            Debug.Log("[SurfaceMeasurementSystem] 🔄 Калибровка и измерения сброшены");
      }
}

/// <summary>
/// Данные точного измерения поверхности
/// </summary>
[System.Serializable]
public class PreciseMeasurement
{
      public TrackableId PlaneId;
      public Vector2 OriginalSize;        // Исходный размер от AR Foundation
      public Vector2 CalibratedSize;      // Размер после калибровки
      public float Confidence;            // Уверенность в измерении (0-1)
      public float Timestamp;             // Время измерения
}