using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Collections.Generic;
using System.Linq;
using Unity.XR.CoreUtils;

/// <summary>
/// Система точного измерения поверхностей для создания плоскостей реальных размеров
/// </summary>
public class PreciseSurfaceMeasurement : MonoBehaviour
{
      [Header("Компоненты для измерения")]
      [SerializeField] private ARPlaneManager planeManager;
      [SerializeField] private ARPointCloudManager pointCloudManager;
      [SerializeField] private XROrigin sessionOrigin;
      [SerializeField] private Camera arCamera;

      [Header("Настройки калибровки")]
      [Tooltip("Эталонный объект для калибровки (например, стандартный лист A4 = 21см)")]
      [SerializeField] private float referenceObjectSize = 0.21f; // Размер эталонного объекта в метрах
      [SerializeField] private bool useAutomaticCalibration = true;
      [SerializeField] private float calibrationTolerance = 0.05f; // 5см допустимая погрешность

      [Header("Измерение поверхностей")]
      [SerializeField] private bool useDepthData = true;
      [SerializeField] private bool usePointCloud = true;
      [SerializeField] private bool useMultipleReferences = true;
      [SerializeField] private int minPointsForMeasurement = 50;

      [Header("Настройки точности")]
      [SerializeField] private float meshResolution = 0.02f; // 2см точность
      [SerializeField] private float outlierThreshold = 0.1f; // 10см для фильтрации выбросов
      [SerializeField] private bool smoothMeasurements = true;

      [Header("Отладка")]
      [SerializeField] private bool debugMode = true;
      [SerializeField] private bool showMeasurementVisualization = true;
      [SerializeField] private Material debugMaterial;

      // Приватные поля
      private float currentScaleFactor = 1.0f;
      private List<CalibrationPoint> calibrationPoints = new List<CalibrationPoint>();
      private Dictionary<ARPlane, SurfaceMeasurement> surfaceMeasurements = new Dictionary<ARPlane, SurfaceMeasurement>();
      private List<GameObject> debugObjects = new List<GameObject>();

      // События
      public System.Action<SurfaceMeasurement> OnSurfaceMeasured;
      public System.Action<float> OnScaleFactorUpdated;

      private void Start()
      {
            InitializeComponents();
            if (useAutomaticCalibration)
            {
                  StartAutomaticCalibration();
            }
      }

      private void InitializeComponents()
      {
            if (planeManager == null)
                  planeManager = FindObjectOfType<ARPlaneManager>();

            if (pointCloudManager == null)
                  pointCloudManager = FindObjectOfType<ARPointCloudManager>();

            if (sessionOrigin == null)
                  sessionOrigin = FindObjectOfType<XROrigin>();

            if (arCamera == null)
                  arCamera = Camera.main;

            if (planeManager != null)
            {
                  planeManager.planesChanged += OnPlanesChanged;
            }

            Debug.Log($"[PreciseSurfaceMeasurement] ✅ Система точного измерения инициализирована");
      }

      private void OnDestroy()
      {
            if (planeManager != null)
            {
                  planeManager.planesChanged -= OnPlanesChanged;
            }
      }

      /// <summary>
      /// Измеряет точные размеры поверхности
      /// </summary>
      public SurfaceMeasurement MeasureSurface(ARPlane plane)
      {
            if (plane == null) return null;

            var measurement = new SurfaceMeasurement();
            measurement.PlaneId = plane.trackableId;
            measurement.PlaneAlignment = plane.alignment;
            measurement.CenterPosition = plane.center;
            measurement.PlaneNormal = plane.normal;

            // 1. Базовые размеры от AR Foundation
            measurement.ARFoundationSize = plane.size;

            // 2. Улучшенные размеры с использованием point cloud
            if (usePointCloud && pointCloudManager != null)
            {
                  measurement.PointCloudSize = MeasureUsingPointCloud(plane);
            }

            // 3. Размеры с использованием depth данных
            if (useDepthData)
            {
                  measurement.DepthBasedSize = MeasureUsingDepthData(plane);
            }

            // 4. Применяем калибровку
            measurement.CalibratedSize = ApplyCalibration(measurement.GetBestSize());

            // 5. Фильтруем выбросы и сглаживаем
            if (smoothMeasurements)
            {
                  measurement.SmoothedSize = SmoothMeasurement(measurement.CalibratedSize, plane);
            }

            // 6. Финальные размеры
            measurement.FinalSize = measurement.SmoothedSize != Vector2.zero ?
                measurement.SmoothedSize : measurement.CalibratedSize;

            // 7. Вычисляем confidence
            measurement.Confidence = CalculateConfidence(measurement);

            // Сохраняем измерение
            surfaceMeasurements[plane] = measurement;

            if (debugMode)
            {
                  LogMeasurementResults(measurement);
            }

            OnSurfaceMeasured?.Invoke(measurement);
            return measurement;
      }

      /// <summary>
      /// Измерение размеров с использованием point cloud
      /// </summary>
      private Vector2 MeasureUsingPointCloud(ARPlane plane)
      {
            var pointClouds = pointCloudManager.trackables;
            var relevantPoints = new List<Vector3>();

            foreach (var pointCloud in pointClouds)
            {
                  if (pointCloud.positions.HasValue)
                  {
                        var positions = pointCloud.positions.Value;
                        var planeCenter = plane.center;
                        var planeNormal = plane.normal;

                        for (int i = 0; i < positions.Length; i++)
                        {
                              var worldPos = sessionOrigin.transform.TransformPoint(positions[i]);
                              var distanceToPlane = Vector3.Dot(worldPos - planeCenter, planeNormal);

                              // Точки близко к плоскости
                              if (Mathf.Abs(distanceToPlane) < 0.05f)
                              {
                                    relevantPoints.Add(worldPos);
                              }
                        }
                  }
            }

            if (relevantPoints.Count < minPointsForMeasurement)
            {
                  if (debugMode)
                        Debug.LogWarning($"[PreciseSurfaceMeasurement] Недостаточно точек для измерения: {relevantPoints.Count}");
                  return Vector2.zero;
            }

            // Вычисляем границы
            var bounds = CalculateBounds(relevantPoints, plane.normal);
            return new Vector2(bounds.size.x, bounds.size.z);
      }

      /// <summary>
      /// Измерение с использованием depth данных
      /// </summary>
      private Vector2 MeasureUsingDepthData(ARPlane plane)
      {
            // Создаем сетку точек для сэмплирования
            var samplePoints = GenerateSamplePoints(plane);
            var validDepthPoints = new List<Vector3>();

            foreach (var screenPoint in samplePoints)
            {
                  // Попытка получить depth для точки
                  var ray = arCamera.ScreenPointToRay(screenPoint);
                  RaycastHit hit;

                  if (Physics.Raycast(ray, out hit, 10.0f))
                  {
                        var distanceToPlane = Vector3.Dot(hit.point - plane.center, plane.normal);
                        if (Mathf.Abs(distanceToPlane) < 0.05f)
                        {
                              validDepthPoints.Add(hit.point);
                        }
                  }
            }

            if (validDepthPoints.Count < minPointsForMeasurement / 2)
            {
                  return Vector2.zero;
            }

            var bounds = CalculateBounds(validDepthPoints, plane.normal);
            return new Vector2(bounds.size.x, bounds.size.z);
      }

      /// <summary>
      /// Генерирует точки для сэмплирования на плоскости
      /// </summary>
      private List<Vector3> GenerateSamplePoints(ARPlane plane)
      {
            var points = new List<Vector3>();
            var center = plane.center;
            var size = plane.size;
            var right = Vector3.Cross(plane.normal, Vector3.up).normalized;
            var up = Vector3.Cross(right, plane.normal).normalized;

            int samplesX = Mathf.Max(5, Mathf.RoundToInt(size.x / meshResolution));
            int samplesY = Mathf.Max(5, Mathf.RoundToInt(size.y / meshResolution));

            for (int x = 0; x < samplesX; x++)
            {
                  for (int y = 0; y < samplesY; y++)
                  {
                        float u = (x / (float)(samplesX - 1)) - 0.5f;
                        float v = (y / (float)(samplesY - 1)) - 0.5f;

                        var worldPoint = center + right * u * size.x + up * v * size.y;
                        var screenPoint = arCamera.WorldToScreenPoint(worldPoint);
                        points.Add(screenPoint);
                  }
            }

            return points;
      }

      /// <summary>
      /// Вычисляет границы точек относительно плоскости
      /// </summary>
      private Bounds CalculateBounds(List<Vector3> points, Vector3 planeNormal)
      {
            if (points.Count == 0) return new Bounds();

            // Создаем локальную систему координат для плоскости
            var right = Vector3.Cross(planeNormal, Vector3.up).normalized;
            var up = Vector3.Cross(right, planeNormal).normalized;

            var localPoints = new List<Vector3>();
            var center = points.Aggregate(Vector3.zero, (sum, point) => sum + point) / points.Count;

            foreach (var point in points)
            {
                  var localPoint = point - center;
                  var x = Vector3.Dot(localPoint, right);
                  var y = Vector3.Dot(localPoint, up);
                  var z = Vector3.Dot(localPoint, planeNormal);

                  localPoints.Add(new Vector3(x, y, z));
            }

            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            foreach (var point in localPoints)
            {
                  bounds.Encapsulate(point);
            }

            return bounds;
      }

      /// <summary>
      /// Применяет калибровку к размерам
      /// </summary>
      private Vector2 ApplyCalibration(Vector2 rawSize)
      {
            return rawSize * currentScaleFactor;
      }

      /// <summary>
      /// Сглаживает измерения для устранения шума
      /// </summary>
      private Vector2 SmoothMeasurement(Vector2 newSize, ARPlane plane)
      {
            if (!surfaceMeasurements.ContainsKey(plane))
            {
                  return newSize;
            }

            var previousMeasurement = surfaceMeasurements[plane];
            var previousSize = previousMeasurement.FinalSize;

            // Экспоненциальное сглаживание
            float alpha = 0.3f; // Коэффициент сглаживания
            return Vector2.Lerp(previousSize, newSize, alpha);
      }

      /// <summary>
      /// Вычисляет уверенность в измерении
      /// </summary>
      private float CalculateConfidence(SurfaceMeasurement measurement)
      {
            float confidence = 0.5f; // Базовая уверенность

            // Увеличиваем confidence если есть данные от нескольких источников
            int sourcesCount = 0;
            if (measurement.ARFoundationSize != Vector2.zero) sourcesCount++;
            if (measurement.PointCloudSize != Vector2.zero) sourcesCount++;
            if (measurement.DepthBasedSize != Vector2.zero) sourcesCount++;

            confidence += sourcesCount * 0.15f;

            // Уменьшаем confidence если размеры сильно отличаются
            var sizes = new List<Vector2>();
            if (measurement.ARFoundationSize != Vector2.zero) sizes.Add(measurement.ARFoundationSize);
            if (measurement.PointCloudSize != Vector2.zero) sizes.Add(measurement.PointCloudSize);
            if (measurement.DepthBasedSize != Vector2.zero) sizes.Add(measurement.DepthBasedSize);

            if (sizes.Count > 1)
            {
                  var avgSize = sizes.Aggregate(Vector2.zero, (sum, size) => sum + size) / sizes.Count;
                  var maxDeviation = sizes.Max(size => Vector2.Distance(size, avgSize));

                  if (maxDeviation > 0.5f) // Если отклонение больше 50см
                  {
                        confidence -= 0.3f;
                  }
            }

            return Mathf.Clamp01(confidence);
      }

      /// <summary>
      /// Автоматическая калибровка системы
      /// </summary>
      private void StartAutomaticCalibration()
      {
            if (debugMode)
            {
                  Debug.Log("[PreciseSurfaceMeasurement] 🔧 Запуск автоматической калибровки...");
            }

            // Ищем известные объекты для калибровки
            StartCoroutine(AutoCalibrationCoroutine());
      }

      private System.Collections.IEnumerator AutoCalibrationCoroutine()
      {
            yield return new WaitForSeconds(2.0f); // Даем время системе AR стабилизироваться

            // Пытаемся найти стандартные объекты для калибровки (упрощенная версия)
            var referencePlanes = new List<ARPlane>();
            foreach (var plane in planeManager.trackables)
            {
                  // Ищем плоскости подходящего размера для калибровки
                  if (plane.size.x > 0.15f && plane.size.x < 0.35f &&
                      plane.size.y > 0.15f && plane.size.y < 0.35f)
                  {
                        referencePlanes.Add(plane);
                  }
            }

            if (referencePlanes.Count > 0)
            {
                  // Предполагаем что это лист A4 или подобный объект
                  float totalSize = 0f;
                  foreach (var plane in referencePlanes)
                  {
                        totalSize += (plane.size.x + plane.size.y) / 2f;
                  }
                  var avgSize = totalSize / referencePlanes.Count;
                  currentScaleFactor = referenceObjectSize / avgSize;

                  if (debugMode)
                  {
                        Debug.Log($"[PreciseSurfaceMeasurement] ✅ Калибровка выполнена. Масштаб: {currentScaleFactor:F3}");
                  }

                  OnScaleFactorUpdated?.Invoke(currentScaleFactor);
            }
      }

      /// <summary>
      /// Ручная калибровка с эталонным объектом
      /// </summary>
      public void CalibrateWithReference(Vector2 measuredSize, float actualSize)
      {
            float averageMeasured = (measuredSize.x + measuredSize.y) / 2f;
            currentScaleFactor = actualSize / averageMeasured;

            if (debugMode)
            {
                  Debug.Log($"[PreciseSurfaceMeasurement] 🎯 Ручная калибровка: масштаб {currentScaleFactor:F3}");
            }

            OnScaleFactorUpdated?.Invoke(currentScaleFactor);
      }

      /// <summary>
      /// Получает самый точный размер из доступных измерений
      /// </summary>
      public Vector2 GetPreciseSize(ARPlane plane)
      {
            if (surfaceMeasurements.ContainsKey(plane))
            {
                  return surfaceMeasurements[plane].FinalSize;
            }

            // Если нет сохраненного измерения, делаем новое
            var measurement = MeasureSurface(plane);
            return measurement?.FinalSize ?? plane.size;
      }

      private void OnPlanesChanged(ARPlanesChangedEventArgs args)
      {
            foreach (var plane in args.added)
            {
                  MeasureSurface(plane);
            }

            foreach (var plane in args.updated)
            {
                  MeasureSurface(plane);
            }
      }

      private void LogMeasurementResults(SurfaceMeasurement measurement)
      {
            Debug.Log($"[PreciseSurfaceMeasurement] 📏 Результаты измерения:");
            Debug.Log($"  ├─ AR Foundation: {measurement.ARFoundationSize.x:F2}×{measurement.ARFoundationSize.y:F2}м");
            if (measurement.PointCloudSize != Vector2.zero)
                  Debug.Log($"  ├─ Point Cloud: {measurement.PointCloudSize.x:F2}×{measurement.PointCloudSize.y:F2}м");
            if (measurement.DepthBasedSize != Vector2.zero)
                  Debug.Log($"  ├─ Depth-based: {measurement.DepthBasedSize.x:F2}×{measurement.DepthBasedSize.y:F2}м");
            Debug.Log($"  ├─ Калиброванный: {measurement.CalibratedSize.x:F2}×{measurement.CalibratedSize.y:F2}м");
            Debug.Log($"  ├─ Финальный: {measurement.FinalSize.x:F2}×{measurement.FinalSize.y:F2}м");
            Debug.Log($"  └─ Уверенность: {measurement.Confidence:F1}%");
      }

      [ContextMenu("Показать все измерения")]
      public void ShowAllMeasurements()
      {
            Debug.Log($"[PreciseSurfaceMeasurement] 📊 Всего измерений: {surfaceMeasurements.Count}");
            foreach (var kvp in surfaceMeasurements)
            {
                  LogMeasurementResults(kvp.Value);
            }
      }

      [ContextMenu("Сбросить калибровку")]
      public void ResetCalibration()
      {
            currentScaleFactor = 1.0f;
            calibrationPoints.Clear();
            Debug.Log("[PreciseSurfaceMeasurement] 🔄 Калибровка сброшена");
      }
}

/// <summary>
/// Данные измерения поверхности
/// </summary>
[System.Serializable]
public class SurfaceMeasurement
{
      public TrackableId PlaneId;
      public PlaneAlignment PlaneAlignment;
      public Vector3 CenterPosition;
      public Vector3 PlaneNormal;

      [Header("Размеры из разных источников")]
      public Vector2 ARFoundationSize;    // Размер от AR Foundation
      public Vector2 PointCloudSize;      // Размер на основе point cloud
      public Vector2 DepthBasedSize;      // Размер на основе depth данных
      public Vector2 CalibratedSize;      // Размер после калибровки
      public Vector2 SmoothedSize;        // Сглаженный размер
      public Vector2 FinalSize;           // Финальный размер

      [Header("Метаданные")]
      public float Confidence;            // Уверенность в измерении (0-1)
      public float Timestamp;             // Время измерения

      public Vector2 GetBestSize()
      {
            // Приоритет: Point Cloud > Depth > AR Foundation
            if (PointCloudSize != Vector2.zero && PointCloudSize.x > 0.1f && PointCloudSize.y > 0.1f)
                  return PointCloudSize;
            if (DepthBasedSize != Vector2.zero && DepthBasedSize.x > 0.1f && DepthBasedSize.y > 0.1f)
                  return DepthBasedSize;
            return ARFoundationSize;
      }
}

/// <summary>
/// Точка калибровки
/// </summary>
[System.Serializable]
public class CalibrationPoint
{
      public Vector3 Position;
      public float MeasuredSize;
      public float ActualSize;
      public float Confidence;
}