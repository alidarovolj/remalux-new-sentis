using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Анализатор геометрии плоскостей для оценки точности системы распознавания стен
/// </summary>
public class PlaneGeometryAnalyzer : MonoBehaviour
{
      [Header("Настройки анализа")]
      [SerializeField] private bool enableContinuousAnalysis = true;
      [SerializeField] private float analysisInterval = 2.0f;
      [SerializeField] private bool showDetailedLogs = true;

      [Header("Критерии оценки")]
      [SerializeField] private float minWallHeight = 1.5f; // Минимальная высота стены
      [SerializeField] private float maxWallHeight = 2.8f; // ИСПРАВЛЕНО: Реалистичная максимальная высота стены
      [SerializeField] private float minWallWidth = 0.5f;  // Минимальная ширина стены
      [SerializeField] private float maxWallWidth = 4.0f;  // ИСПРАВЛЕНО: Реалистичная максимальная ширина стены

      private ARManagerInitializer2 arManager;
      private WallPainterController wallPainterController;
      private float lastAnalysisTime = 0f;

      private void Start()
      {
            // Сначала ищем новую систему WallPainterController
            wallPainterController = FindObjectOfType<WallPainterController>();

            // Затем ищем старую систему ARManagerInitializer2  
            arManager = FindObjectOfType<ARManagerInitializer2>();

            if (wallPainterController == null && arManager == null)
            {
                  Debug.LogError("[PlaneGeometryAnalyzer] ❌ Не найдены ни WallPainterController, ни ARManagerInitializer2!");
                  return;
            }

            string activeSystem = wallPainterController != null ? "WallPainterController (новая)" : "ARManagerInitializer2 (старая)";
            Debug.Log($"[PlaneGeometryAnalyzer] ✅ Инициализирован с системой: {activeSystem}. Начинаю анализ геометрии плоскостей...");
      }

      private void Update()
      {
            if (!enableContinuousAnalysis || (wallPainterController == null && arManager == null)) return;

            if (Time.time - lastAnalysisTime >= analysisInterval)
            {
                  AnalyzePlanesGeometry();
                  lastAnalysisTime = Time.time;
            }
      }

      [ContextMenu("Анализировать геометрию плоскостей")]
      public void AnalyzePlanesGeometry()
      {
            var planes = GetAllPlanes();
            if (planes.Count == 0)
            {
                  Debug.Log("[PlaneGeometryAnalyzer] 📊 Нет созданных плоскостей для анализа");
                  return;
            }

            Debug.Log($"[PlaneGeometryAnalyzer] 📊 === АНАЛИЗ ГЕОМЕТРИИ {planes.Count} ПЛОСКОСТЕЙ ===");

            var verticalPlanes = new List<PlaneGeometryData>();
            var horizontalPlanes = new List<PlaneGeometryData>();

            foreach (var plane in planes)
            {
                  if (plane == null) continue;

                  var geometryData = AnalyzePlaneGeometry(plane);

                  if (geometryData.IsVertical)
                        verticalPlanes.Add(geometryData);
                  else
                        horizontalPlanes.Add(geometryData);

                  if (showDetailedLogs)
                  {
                        LogPlaneDetails(geometryData);
                  }
            }

            // Общая статистика
            LogSummaryStatistics(verticalPlanes, horizontalPlanes);

            // Анализ качества определения
            AnalyzeDetectionQuality(verticalPlanes, horizontalPlanes);
      }

      private PlaneGeometryData AnalyzePlaneGeometry(GameObject plane)
      {
            var data = new PlaneGeometryData();
            data.Name = plane.name;
            data.Position = plane.transform.position;
            data.Rotation = plane.transform.rotation;

            // Получаем размеры плоскости
            var meshFilter = plane.GetComponent<MeshFilter>();
            if (meshFilter != null && meshFilter.mesh != null)
            {
                  var bounds = meshFilter.mesh.bounds;

                  // Учитываем масштаб и вращение для правильного определения размеров
                  var scale = plane.transform.localScale;
                  var rotation = plane.transform.rotation;

                  // Вычисляем размеры с учетом ориентации
                  Vector3 size = Vector3.Scale(bounds.size, scale);

                  // Для вертикальных плоскостей используем правильные оси
                  var forward = plane.transform.forward;
                  var right = plane.transform.right;
                  var up = plane.transform.up;

                  // Определяем какой размер соответствует ширине и высоте
                  float dotUp = Mathf.Abs(Vector3.Dot(up, Vector3.up));
                  float dotRight = Mathf.Abs(Vector3.Dot(right, Vector3.up));
                  float dotForward = Mathf.Abs(Vector3.Dot(forward, Vector3.up));

                  if (dotUp > dotRight && dotUp > dotForward)
                  {
                        // Up axis направлен вверх
                        data.Width = Mathf.Max(size.x, size.z);
                        data.Height = size.y;
                        data.Depth = Mathf.Min(size.x, size.z);
                  }
                  else if (dotRight > dotForward)
                  {
                        // Right axis направлен вверх
                        data.Width = Mathf.Max(size.y, size.z);
                        data.Height = size.x;
                        data.Depth = Mathf.Min(size.y, size.z);
                  }
                  else
                  {
                        // Forward axis направлен вверх
                        data.Width = Mathf.Max(size.x, size.y);
                        data.Height = size.z;
                        data.Depth = Mathf.Min(size.x, size.y);
                  }

                  // Обеспечиваем минимальные размеры
                  data.Width = Mathf.Max(data.Width, 0.01f);
                  data.Height = Mathf.Max(data.Height, 0.01f);
                  data.Depth = Mathf.Max(data.Depth, 0.01f);
            }
            else
            {
                  // Fallback: используем масштаб трансформа
                  data.Width = Mathf.Max(plane.transform.localScale.x, 0.01f);
                  data.Height = Mathf.Max(plane.transform.localScale.y, 0.01f);
                  data.Depth = Mathf.Max(plane.transform.localScale.z, 0.01f);
            }

            // Определяем ориентацию плоскости
            var normal = plane.transform.forward;

            // Угол с вертикалью
            data.AngleWithVertical = Vector3.Angle(normal, Vector3.up);
            data.IsVertical = data.AngleWithVertical > 45f && data.AngleWithVertical < 135f;

            // Расстояние до камеры
            var camera = Camera.main;
            if (camera != null)
            {
                  data.DistanceToCamera = Vector3.Distance(data.Position, camera.transform.position);
            }

            // Анализ материала
            var renderer = plane.GetComponent<Renderer>();
            if (renderer != null && renderer.material != null)
            {
                  data.MaterialName = renderer.material.name;
                  data.ShaderName = renderer.material.shader.name;
            }

            return data;
      }

      private void LogPlaneDetails(PlaneGeometryData data)
      {
            string orientation = data.IsVertical ? "ВЕРТИКАЛЬНАЯ" : "ГОРИЗОНТАЛЬНАЯ";

            Debug.Log($"[PlaneGeometryAnalyzer] 📏 {data.Name}:");
            Debug.Log($"  └─ Ориентация: {orientation} (угол с вертикалью: {data.AngleWithVertical:F1}°)");
            Debug.Log($"  └─ Размеры: {data.Width:F2}м × {data.Height:F2}м × {data.Depth:F2}м");
            Debug.Log($"  └─ Позиция: ({data.Position.x:F2}, {data.Position.y:F2}, {data.Position.z:F2})");
            Debug.Log($"  └─ Расстояние до камеры: {data.DistanceToCamera:F2}м");
            Debug.Log($"  └─ Материал: {data.MaterialName} (шейдер: {data.ShaderName})");
      }

      private void LogSummaryStatistics(List<PlaneGeometryData> verticalPlanes, List<PlaneGeometryData> horizontalPlanes)
      {
            Debug.Log($"[PlaneGeometryAnalyzer] 📈 === ОБЩАЯ СТАТИСТИКА ===");
            Debug.Log($"  ├─ Вертикальных плоскостей (стены): {verticalPlanes.Count}");
            Debug.Log($"  └─ Горизонтальных плоскостей (пол/потолок): {horizontalPlanes.Count}");

            if (verticalPlanes.Count > 0)
            {
                  var avgWidth = verticalPlanes.Average(p => p.Width);
                  var avgHeight = verticalPlanes.Average(p => p.Height);
                  var avgDistance = verticalPlanes.Average(p => p.DistanceToCamera);

                  Debug.Log($"[PlaneGeometryAnalyzer] 🏗️ СТЕНЫ - Средние размеры:");
                  Debug.Log($"  ├─ Ширина: {avgWidth:F2}м (мин: {verticalPlanes.Min(p => p.Width):F2}м, макс: {verticalPlanes.Max(p => p.Width):F2}м)");
                  Debug.Log($"  ├─ Высота: {avgHeight:F2}м (мин: {verticalPlanes.Min(p => p.Height):F2}м, макс: {verticalPlanes.Max(p => p.Height):F2}м)");
                  Debug.Log($"  └─ Расстояние: {avgDistance:F2}м");
            }

            if (horizontalPlanes.Count > 0)
            {
                  var avgArea = horizontalPlanes.Average(p => p.Width * p.Depth);
                  Debug.Log($"[PlaneGeometryAnalyzer] 🏠 ПОЛ/ПОТОЛОК - Средняя площадь: {avgArea:F2}м²");
            }
      }

      private void AnalyzeDetectionQuality(List<PlaneGeometryData> verticalPlanes, List<PlaneGeometryData> horizontalPlanes)
      {
            Debug.Log($"[PlaneGeometryAnalyzer] ✅ === АНАЛИЗ КАЧЕСТВА ОПРЕДЕЛЕНИЯ ===");

            int validWalls = 0;
            int invalidWalls = 0;

            foreach (var plane in verticalPlanes)
            {
                  bool isValidWall = IsValidWallDimensions(plane);
                  if (isValidWall)
                        validWalls++;
                  else
                        invalidWalls++;

                  if (!isValidWall && showDetailedLogs)
                  {
                        Debug.LogWarning($"[PlaneGeometryAnalyzer] ⚠️ {plane.Name} - подозрительные размеры стены: {plane.Width:F2}м × {plane.Height:F2}м");
                  }
            }

            float accuracy = verticalPlanes.Count > 0 ? (float)validWalls / verticalPlanes.Count * 100f : 0f;

            Debug.Log($"[PlaneGeometryAnalyzer] 🎯 ТОЧНОСТЬ ОПРЕДЕЛЕНИЯ СТЕН:");
            Debug.Log($"  ├─ Валидных стен: {validWalls}/{verticalPlanes.Count} ({accuracy:F1}%)");
            Debug.Log($"  ├─ Подозрительных: {invalidWalls}");
            Debug.Log($"  └─ Общая оценка: {GetQualityRating(accuracy)}");

            // Анализ распределения по расстояниям
            if (verticalPlanes.Count > 0)
            {
                  var nearPlanes = verticalPlanes.Count(p => p.DistanceToCamera < 2.0f);
                  var midPlanes = verticalPlanes.Count(p => p.DistanceToCamera >= 2.0f && p.DistanceToCamera < 5.0f);
                  var farPlanes = verticalPlanes.Count(p => p.DistanceToCamera >= 5.0f);

                  Debug.Log($"[PlaneGeometryAnalyzer] 📍 РАСПРЕДЕЛЕНИЕ ПО РАССТОЯНИЯМ:");
                  Debug.Log($"  ├─ Близко (<2м): {nearPlanes} плоскостей");
                  Debug.Log($"  ├─ Средне (2-5м): {midPlanes} плоскостей");
                  Debug.Log($"  └─ Далеко (>5м): {farPlanes} плоскостей");
            }
      }



      private bool IsValidWallDimensions(PlaneGeometryData plane)
      {
            return plane.Width >= minWallWidth && plane.Width <= maxWallWidth &&
                   plane.Height >= minWallHeight && plane.Height <= maxWallHeight;
      }

      private string GetQualityRating(float accuracy)
      {
            if (accuracy >= 90f) return "ОТЛИЧНО 🌟";
            if (accuracy >= 75f) return "ХОРОШО ✅";
            if (accuracy >= 60f) return "УДОВЛЕТВОРИТЕЛЬНО ⚠️";
            return "ТРЕБУЕТ УЛУЧШЕНИЯ ❌";
      }

      [ContextMenu("Показать детальную информацию")]
      public void ShowDetailedPlaneInfo()
      {
            showDetailedLogs = true;
            AnalyzePlanesGeometry();
      }

      [ContextMenu("Экспорт данных в лог")]
      public void ExportPlaneDataToLog()
      {
            var planes = GetAllPlanes();
            Debug.Log($"[PlaneGeometryAnalyzer] 📋 === ЭКСПОРТ ДАННЫХ {planes.Count} ПЛОСКОСТЕЙ ===");

            foreach (var plane in planes)
            {
                  if (plane == null) continue;
                  var data = AnalyzePlaneGeometry(plane);

                  string csvLine = $"{data.Name},{data.IsVertical},{data.Width:F3},{data.Height:F3},{data.Depth:F3}," +
                                 $"{data.Position.x:F3},{data.Position.y:F3},{data.Position.z:F3}," +
                                 $"{data.AngleWithVertical:F1},{data.DistanceToCamera:F3}";

                  Debug.Log($"[PlaneGeometryAnalyzer] CSV: {csvLine}");
            }
      }

      private List<GameObject> GetAllPlanes()
      {
            var planes = new List<GameObject>();

            if (wallPainterController != null)
            {
                  planes.AddRange(wallPainterController.GeneratedPlanes);
            }

            if (arManager != null)
            {
                  planes.AddRange(arManager.GeneratedPlanes);
            }

            return planes;
      }
}

[System.Serializable]
public class PlaneGeometryData
{
      public string Name;
      public Vector3 Position;
      public Quaternion Rotation;
      public float Width;
      public float Height;
      public float Depth;
      public bool IsVertical;
      public float AngleWithVertical;
      public float DistanceToCamera;
      public string MaterialName;
      public string ShaderName;
}