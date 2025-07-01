using UnityEngine;
using System.Collections.Generic;
using System.Linq;

/// <summary>
/// Класс для улучшенного расчета геометрии плоскостей с более точными углами и размерами
/// </summary>
public static class PlaneGeometryEnhancer
{
      /// <summary>
      /// Улучшенная структура для хранения результата рейкаста с дополнительными данными
      /// </summary>
      public struct EnhancedRaycastHit
      {
            public RaycastHit hit;
            public float weight;
            public Vector3 rayDirection;
            public float confidence;
            public bool isValid;

            public EnhancedRaycastHit(RaycastHit hit, float weight, Vector3 rayDirection, float confidence, bool isValid)
            {
                  this.hit = hit;
                  this.weight = weight;
                  this.rayDirection = rayDirection;
                  this.confidence = confidence;
                  this.isValid = isValid;
            }
      }

      /// <summary>
      /// Результат анализа плоскости с улучшенной геометрией
      /// </summary>
      public struct EnhancedPlaneResult
      {
            public Vector3 position;
            public Quaternion rotation;
            public Vector2 size;
            public Vector3 normal;
            public float confidence;
            public bool isValid;

            public EnhancedPlaneResult(Vector3 position, Quaternion rotation, Vector2 size, Vector3 normal, float confidence, bool isValid)
            {
                  this.position = position;
                  this.rotation = rotation;
                  this.size = size;
                  this.normal = normal;
                  this.confidence = confidence;
                  this.isValid = isValid;
            }
      }

      /// <summary>
      /// Настройки для рейкастинга
      /// </summary>
      [System.Serializable]
      public struct RaycastSettings
      {
            public LayerMask layerMask;
            public float maxDistance;
            public float minDistance;
            public float maxWallAngleDeviation;

            public RaycastSettings(LayerMask layerMask, float maxDistance, float minDistance, float maxWallAngleDeviation)
            {
                  this.layerMask = layerMask;
                  this.maxDistance = maxDistance;
                  this.minDistance = minDistance;
                  this.maxWallAngleDeviation = maxWallAngleDeviation;
            }
      }

      /// <summary>
      /// Улучшенный расчет геометрии плоскости с более точными углами и размерами
      /// </summary>
      /// <param name="area">Область на маске сегментации</param>
      /// <param name="textureWidth">Ширина текстуры маски</param>
      /// <param name="textureHeight">Высота текстуры маски</param>
      /// <param name="camera">Камера для рейкастинга</param>
      /// <param name="raycastSettings">Настройки рейкастинга</param>
      /// <returns>Результат анализа плоскости</returns>
      public static EnhancedPlaneResult CalculateEnhancedPlaneGeometry(
          Rect area,
          int textureWidth,
          int textureHeight,
          Camera camera,
          RaycastSettings raycastSettings)
      {
            // 1. Выполняем адаптивный рейкастинг
            var raycastResults = PerformAdaptiveRaycasting(area, textureWidth, textureHeight, camera, raycastSettings);

            if (!raycastResults.Any(r => r.isValid))
            {
                  Debug.LogWarning("[PlaneGeometryEnhancer] Не найдено валидных результатов рейкастинга");
                  return new EnhancedPlaneResult(Vector3.zero, Quaternion.identity, Vector2.zero, Vector3.zero, 0f, false);
            }

            // 2. Продвинутая кластеризация результатов
            var clusteredResult = PerformAdvancedClustering(raycastResults);

            // 3. Расчет стабилизированной ориентации
            var enhancedRotation = CalculateStabilizedRotation(clusteredResult.hit.normal, camera.transform.up);

            // 4. Точный расчет размеров с компенсацией перспективы
            var accurateSize = CalculateAccuratePlaneSize(area, textureWidth, textureHeight, camera, clusteredResult.hit.point, enhancedRotation);

            // 5. Финальная позиция с небольшим смещением от поверхности
            var finalPosition = clusteredResult.hit.point + clusteredResult.hit.normal * 0.005f;

            Debug.Log($"[PlaneGeometryEnhancer] Создана улучшенная геометрия: Pos={finalPosition:F2}, Size={accurateSize:F2}, Confidence={clusteredResult.confidence:F2}");

            return new EnhancedPlaneResult(
                finalPosition,
                enhancedRotation,
                accurateSize,
                clusteredResult.hit.normal,
                clusteredResult.confidence,
                true
            );
      }

      /// <summary>
      /// Адаптивный рейкастинг с умным распределением лучей
      /// </summary>
      private static List<EnhancedRaycastHit> PerformAdaptiveRaycasting(
          Rect area,
          int textureWidth,
          int textureHeight,
          Camera camera,
          RaycastSettings settings)
      {
            var results = new List<EnhancedRaycastHit>();

            // Центр области
            float centerX = (area.x + area.width / 2f) / textureWidth;
            float centerY = (area.y + area.height / 2f) / textureHeight;

            // Размер области влияет на плотность рейкастов
            float areaSize = (area.width * area.height) / (textureWidth * textureHeight);
            int adaptiveRayCount = Mathf.Clamp(Mathf.RoundToInt(areaSize * 100), 5, 25);

            // Генерируем адаптивную сетку лучей
            var rayDirections = GenerateAdaptiveRayGrid(centerX, centerY, areaSize, adaptiveRayCount);

            foreach (var rayData in rayDirections)
            {
                  Ray ray = camera.ViewportPointToRay(new Vector3(rayData.uv.x, rayData.uv.y, 0));

                  if (Physics.Raycast(ray, out RaycastHit hit, settings.maxDistance, settings.layerMask, QueryTriggerInteraction.Ignore))
                  {
                        // Проверяем валидность попадания
                        bool isValid = ValidateRaycastHit(hit, settings);

                        if (isValid)
                        {
                              float confidence = CalculateHitConfidence(hit, ray, rayData.weight, settings);
                              results.Add(new EnhancedRaycastHit(hit, rayData.weight, ray.direction, confidence, true));
                        }
                  }
            }

            Debug.Log($"[PlaneGeometryEnhancer] Адаптивный рейкастинг: {results.Count}/{adaptiveRayCount} валидных попаданий");
            return results;
      }

      /// <summary>
      /// Данные для луча с UV координатами и весом
      /// </summary>
      private struct RayData
      {
            public Vector2 uv;
            public float weight;

            public RayData(Vector2 uv, float weight)
            {
                  this.uv = uv;
                  this.weight = weight;
            }
      }

      /// <summary>
      /// Генерирует адаптивную сетку лучей в зависимости от размера области
      /// </summary>
      private static List<RayData> GenerateAdaptiveRayGrid(float centerX, float centerY, float areaSize, int rayCount)
      {
            var rays = new List<RayData>();

            // Центральный луч с максимальным весом
            rays.Add(new RayData(new Vector2(centerX, centerY), 3.0f));

            // Адаптивный радиус в зависимости от размера области
            float baseRadius = Mathf.Sqrt(areaSize) * 0.5f;

            // Концентрические круги с убывающими весами
            for (int ring = 1; ring <= 3; ring++)
            {
                  float ringRadius = baseRadius * ring * 0.3f;
                  float ringWeight = 2.0f / ring; // Убывающий вес
                  int ringRayCount = Mathf.Max(4, rayCount / (4 * ring));

                  for (int i = 0; i < ringRayCount; i++)
                  {
                        float angle = (float)i / ringRayCount * 2 * Mathf.PI;
                        float uvX = centerX + Mathf.Cos(angle) * ringRadius;
                        float uvY = centerY + Mathf.Sin(angle) * ringRadius;

                        // Проверяем границы
                        if (uvX >= 0 && uvX <= 1 && uvY >= 0 && uvY <= 1)
                        {
                              rays.Add(new RayData(new Vector2(uvX, uvY), ringWeight));
                        }
                  }
            }

            return rays;
      }

      /// <summary>
      /// Проверяет валидность результата рейкаста
      /// </summary>
      private static bool ValidateRaycastHit(RaycastHit hit, RaycastSettings settings)
      {
            // Проверка минимального расстояния
            if (hit.distance < settings.minDistance)
                  return false;

            // Проверка нормали для вертикальных поверхностей
            float angleWithUp = Vector3.Angle(hit.normal, Vector3.up);
            bool isVertical = angleWithUp > (90f - settings.maxWallAngleDeviation) &&
                             angleWithUp < (90f + settings.maxWallAngleDeviation);

            return isVertical;
      }

      /// <summary>
      /// Рассчитывает уверенность попадания
      /// </summary>
      private static float CalculateHitConfidence(RaycastHit hit, Ray ray, float baseWeight, RaycastSettings settings)
      {
            // Фактор расстояния (ближе = лучше)
            float distanceFactor = 1.0f - Mathf.Clamp01(hit.distance / settings.maxDistance);

            // Фактор выравнивания нормали
            float angleWithUp = Vector3.Angle(hit.normal, Vector3.up);
            float idealAngle = 90f; // Идеальная вертикальная стена
            float angleFactor = 1.0f - Mathf.Abs(angleWithUp - idealAngle) / settings.maxWallAngleDeviation;

            // Фактор качества поверхности (основан на стабильности нормали)
            float surfaceQuality = CalculateSurfaceQuality(hit);

            return baseWeight * distanceFactor * angleFactor * surfaceQuality;
      }

      /// <summary>
      /// Оценивает качество поверхности
      /// </summary>
      private static float CalculateSurfaceQuality(RaycastHit hit)
      {
            // Простая эвристика на основе материала и типа коллайдера
            if (hit.collider is MeshCollider)
                  return 1.0f;
            else if (hit.collider is BoxCollider)
                  return 0.9f;
            else
                  return 0.7f;
      }

      /// <summary>
      /// Продвинутая кластеризация результатов рейкастинга
      /// </summary>
      private static EnhancedRaycastHit PerformAdvancedClustering(List<EnhancedRaycastHit> hits)
      {
            if (!hits.Any())
                  return new EnhancedRaycastHit();

            // Группировка по расстоянию и нормали
            var clusters = new Dictionary<int, List<EnhancedRaycastHit>>();

            for (int i = 0; i < hits.Count; i++)
            {
                  int clusterIndex = FindOrCreateCluster(hits[i], clusters, 0.2f, 15f); // 20см и 15° толерантность

                  if (!clusters.ContainsKey(clusterIndex))
                        clusters[clusterIndex] = new List<EnhancedRaycastHit>();

                  clusters[clusterIndex].Add(hits[i]);
            }

            // Находим лучший кластер по общей уверенности
            var bestCluster = clusters.Values
                .OrderByDescending(cluster => cluster.Sum(h => h.confidence))
                .First();

            // Вычисляем средневзвешенные значения для кластера
            return CalculateWeightedAverage(bestCluster);
      }

      /// <summary>
      /// Находит или создает кластер для результата рейкаста
      /// </summary>
      private static int FindOrCreateCluster(EnhancedRaycastHit hit, Dictionary<int, List<EnhancedRaycastHit>> clusters,
          float distanceTolerance, float angleTolerance)
      {
            foreach (var kvp in clusters)
            {
                  var cluster = kvp.Value;
                  if (cluster.Any())
                  {
                        var representative = cluster.First();

                        // Проверяем близость по расстоянию и углу нормали
                        bool distanceMatch = Mathf.Abs(hit.hit.distance - representative.hit.distance) < distanceTolerance;
                        bool angleMatch = Vector3.Angle(hit.hit.normal, representative.hit.normal) < angleTolerance;

                        if (distanceMatch && angleMatch)
                              return kvp.Key;
                  }
            }

            // Создаем новый кластер
            return clusters.Count;
      }

      /// <summary>
      /// Вычисляет средневзвешенные значения для кластера
      /// </summary>
      private static EnhancedRaycastHit CalculateWeightedAverage(List<EnhancedRaycastHit> cluster)
      {
            float totalWeight = cluster.Sum(h => h.confidence);

            Vector3 avgPoint = Vector3.zero;
            Vector3 avgNormal = Vector3.zero;
            float avgDistance = 0f;

            foreach (var hit in cluster)
            {
                  float normalizedWeight = hit.confidence / totalWeight;
                  avgPoint += hit.hit.point * normalizedWeight;
                  avgNormal += hit.hit.normal * normalizedWeight;
                  avgDistance += hit.hit.distance * normalizedWeight;
            }

            RaycastHit avgHit = cluster.First().hit;
            avgHit.point = avgPoint;
            avgHit.normal = avgNormal.normalized;
            avgHit.distance = avgDistance;

            return new EnhancedRaycastHit(avgHit, totalWeight, cluster.First().rayDirection, totalWeight, true);
      }

      /// <summary>
      /// Рассчитывает стабилизированную ориентацию плоскости
      /// </summary>
      private static Quaternion CalculateStabilizedRotation(Vector3 surfaceNormal, Vector3 cameraUp)
      {
            // Используем мировую вертикаль как базовую ось "вверх" для стабильности
            Vector3 worldUp = Vector3.up;

            // Если нормаль поверхности слишком близка к мировой вертикали, используем forward камеры
            if (Mathf.Abs(Vector3.Dot(surfaceNormal, worldUp)) > 0.95f)
            {
                  Vector3 cameraForward = Vector3.ProjectOnPlane(Camera.main.transform.forward, worldUp).normalized;
                  return Quaternion.LookRotation(surfaceNormal, cameraForward);
            }

            // Для вертикальных стен используем мировую вертикаль
            Vector3 planeUp = Vector3.ProjectOnPlane(worldUp, surfaceNormal).normalized;

            // Если проекция нулевая, используем right вектор
            if (planeUp.magnitude < 0.1f)
            {
                  Vector3 right = Vector3.Cross(surfaceNormal, worldUp).normalized;
                  planeUp = Vector3.Cross(right, surfaceNormal).normalized;
            }

            return Quaternion.LookRotation(surfaceNormal, planeUp);
      }

      /// <summary>
      /// Точный расчет размеров плоскости с компенсацией перспективы
      /// </summary>
      private static Vector2 CalculateAccuratePlaneSize(Rect area, int textureWidth, int textureHeight,
          Camera camera, Vector3 hitPoint, Quaternion planeRotation)
      {
            // Определяем UV координаты углов области
            Vector2[] uvCorners = {
            new Vector2(area.xMin / textureWidth, area.yMin / textureHeight), // Bottom-left
            new Vector2(area.xMax / textureWidth, area.yMin / textureHeight), // Bottom-right
            new Vector2(area.xMin / textureWidth, area.yMax / textureHeight), // Top-left
            new Vector2(area.xMax / textureWidth, area.yMax / textureHeight)  // Top-right
        };

            // Создаем плоскость на найденной поверхности
            Plane surfacePlane = new Plane(planeRotation * Vector3.forward, hitPoint);

            // Проецируем углы на плоскость
            Vector3[] worldCorners = new Vector3[4];
            for (int i = 0; i < 4; i++)
            {
                  Ray cornerRay = camera.ViewportPointToRay(new Vector3(uvCorners[i].x, uvCorners[i].y, 0));

                  if (surfacePlane.Raycast(cornerRay, out float distance))
                  {
                        worldCorners[i] = cornerRay.GetPoint(distance);
                  }
                  else
                  {
                        // Фоллбэк: проецируем на расстояние хита
                        worldCorners[i] = cornerRay.GetPoint(Vector3.Distance(camera.transform.position, hitPoint));
                  }
            }

            // Вычисляем размеры в локальных координатах плоскости
            Vector3 localRight = planeRotation * Vector3.right;
            Vector3 localUp = planeRotation * Vector3.up;

            // Проецируем углы в локальную систему координат плоскости
            float minX = float.MaxValue, maxX = float.MinValue;
            float minY = float.MaxValue, maxY = float.MinValue;

            foreach (var corner in worldCorners)
            {
                  Vector3 localPos = corner - hitPoint;
                  float x = Vector3.Dot(localPos, localRight);
                  float y = Vector3.Dot(localPos, localUp);

                  minX = Mathf.Min(minX, x);
                  maxX = Mathf.Max(maxX, x);
                  minY = Mathf.Min(minY, y);
                  maxY = Mathf.Max(maxY, y);
            }

            float width = maxX - minX;
            float height = maxY - minY;

            // Применяем сглаживание для устранения артефактов
            width = Mathf.Max(width, 0.1f);
            height = Mathf.Max(height, 0.1f);

            Debug.Log($"[PlaneGeometryEnhancer] Точные размеры: {width:F2}x{height:F2}м");

            return new Vector2(width, height);
      }
}