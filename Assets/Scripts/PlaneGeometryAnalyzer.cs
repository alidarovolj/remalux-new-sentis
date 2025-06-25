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
      private float lastAnalysisTime = 0f;

      private void Start()
      {
            // Находим ARManagerInitializer2
            arManager = FindObjectOfType<ARManagerInitializer2>();
            if (arManager == null)
            {
                  Debug.LogError("[PlaneGeometryAnalyzer] ARManagerInitializer2 не найден!");
                  return;
            }

            Debug.Log("[PlaneGeometryAnalyzer] ✅ Инициализирован. Начинаю анализ геометрии плоскостей...");
      }

      private void Update()
      {
            if (!enableContinuousAnalysis || arManager == null) return;

            if (Time.time - lastAnalysisTime >= analysisInterval)
            {
                  AnalyzePlanesGeometry();
                  lastAnalysisTime = Time.time;
            }
      }

      [ContextMenu("Анализировать геометрию плоскостей")]
      public void AnalyzePlanesGeometry()
      {
            if (arManager == null) return;

            var planes = arManager.GeneratedPlanes;
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
                  data.Width = bounds.size.x * plane.transform.localScale.x;
                  data.Height = bounds.size.y * plane.transform.localScale.y;
                  data.Depth = bounds.size.z * plane.transform.localScale.z;
            }

            // Определяем ориентацию плоскости
            var up = plane.transform.up;
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
            if (arManager == null) return;

            var planes = arManager.GeneratedPlanes;
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