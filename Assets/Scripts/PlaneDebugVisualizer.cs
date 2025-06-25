using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Визуализатор для отладки процесса создания плоскостей из сегментации
/// </summary>
public class PlaneDebugVisualizer : MonoBehaviour
{
      [Header("Ссылки")]
      [SerializeField] private ARManagerInitializer2 arManager;

      [Header("Настройки визуализации")]
      [SerializeField] private bool showRaycastDebug = true;
      [SerializeField] private bool showPlaneCreationDebug = true;
      [SerializeField] private float debugLineDuration = 5f;

      [Header("Статистика")]
      [SerializeField] private int totalRaycastAttempts = 0;
      [SerializeField] private int successfulRaycasts = 0;
      [SerializeField] private int failedRaycasts = 0;
      [SerializeField] private List<string> hitObjects = new List<string>();

      private void Start()
      {
            if (arManager == null)
            {
                  arManager = FindObjectOfType<ARManagerInitializer2>();
            }

            if (arManager == null)
            {
                  Debug.LogError("[PlaneDebugVisualizer] ARManagerInitializer2 не найден!");
                  enabled = false;
                  return;
            }

            Debug.Log("[PlaneDebugVisualizer] ✅ Инициализирован для отладки создания плоскостей");
      }

      private void Update()
      {
            // Периодически выводим статистику
            if (Time.frameCount % 300 == 0 && totalRaycastAttempts > 0)
            {
                  LogStatistics();
            }
      }

      /// <summary>
      /// Регистрирует попытку рейкаста (вызывается из ARManagerInitializer2)
      /// </summary>
      public void RegisterRaycast(Ray ray, bool hit, RaycastHit hitInfo, float maxDistance)
      {
            totalRaycastAttempts++;

            if (hit)
            {
                  successfulRaycasts++;

                  // Добавляем объект в список попаданий
                  string objectName = hitInfo.collider.gameObject.name;
                  if (!hitObjects.Contains(objectName))
                  {
                        hitObjects.Add(objectName);
                  }

                  if (showRaycastDebug)
                  {
                        // Зеленая линия для успешного рейкаста
                        Debug.DrawRay(ray.origin, ray.direction * hitInfo.distance, Color.green, debugLineDuration);
                        Debug.DrawRay(hitInfo.point, hitInfo.normal * 0.5f, Color.yellow, debugLineDuration);

                        if (Time.frameCount % 10 == 0) // Логируем каждый 10-й успешный рейкаст
                        {
                              Debug.Log($"[PlaneDebugVisualizer] ✅ Raycast hit: {objectName} at {hitInfo.point}, distance: {hitInfo.distance:F2}m, normal: {hitInfo.normal}");
                        }
                  }
            }
            else
            {
                  failedRaycasts++;

                  if (showRaycastDebug)
                  {
                        // Красная линия для неудачного рейкаста
                        Debug.DrawRay(ray.origin, ray.direction * maxDistance, Color.red, debugLineDuration * 0.5f);

                        if (Time.frameCount % 50 == 0) // Логируем каждый 50-й неудачный рейкаст
                        {
                              Debug.Log($"[PlaneDebugVisualizer] ❌ Raycast miss from {ray.origin} in direction {ray.direction}");
                        }
                  }
            }
      }

      /// <summary>
      /// Регистрирует создание плоскости
      /// </summary>
      public void RegisterPlaneCreation(Vector3 position, Vector3 size, bool created)
      {
            if (showPlaneCreationDebug)
            {
                  if (created)
                  {
                        Debug.Log($"[PlaneDebugVisualizer] 🟩 Плоскость создана: позиция {position}, размер {size}");

                        // Рисуем зеленый куб в месте создания плоскости
                        DrawDebugCube(position, size, Color.green, debugLineDuration);
                  }
                  else
                  {
                        Debug.Log($"[PlaneDebugVisualizer] 🟥 Плоскость НЕ создана: позиция {position}, размер {size}");

                        // Рисуем красный куб в месте, где плоскость не была создана
                        DrawDebugCube(position, size, Color.red, debugLineDuration * 0.5f);
                  }
            }
      }

      /// <summary>
      /// Выводит статистику
      /// </summary>
      private void LogStatistics()
      {
            float successRate = totalRaycastAttempts > 0 ? (float)successfulRaycasts / totalRaycastAttempts * 100f : 0f;

            Debug.Log($@"[PlaneDebugVisualizer] 📊 СТАТИСТИКА РЕЙКАСТОВ:
- Всего попыток: {totalRaycastAttempts}
- Успешных: {successfulRaycasts} ({successRate:F1}%)
- Неудачных: {failedRaycasts}
- Найдено объектов: {hitObjects.Count}
- Объекты: {string.Join(", ", hitObjects)}");
      }

      /// <summary>
      /// Рисует отладочный куб
      /// </summary>
      private void DrawDebugCube(Vector3 center, Vector3 size, Color color, float duration)
      {
            Vector3 halfSize = size * 0.5f;

            // Нижняя грань
            Vector3 p1 = center + new Vector3(-halfSize.x, -halfSize.y, -halfSize.z);
            Vector3 p2 = center + new Vector3(halfSize.x, -halfSize.y, -halfSize.z);
            Vector3 p3 = center + new Vector3(halfSize.x, -halfSize.y, halfSize.z);
            Vector3 p4 = center + new Vector3(-halfSize.x, -halfSize.y, halfSize.z);

            // Верхняя грань
            Vector3 p5 = center + new Vector3(-halfSize.x, halfSize.y, -halfSize.z);
            Vector3 p6 = center + new Vector3(halfSize.x, halfSize.y, -halfSize.z);
            Vector3 p7 = center + new Vector3(halfSize.x, halfSize.y, halfSize.z);
            Vector3 p8 = center + new Vector3(-halfSize.x, halfSize.y, halfSize.z);

            // Рисуем рёбра
            Debug.DrawLine(p1, p2, color, duration);
            Debug.DrawLine(p2, p3, color, duration);
            Debug.DrawLine(p3, p4, color, duration);
            Debug.DrawLine(p4, p1, color, duration);

            Debug.DrawLine(p5, p6, color, duration);
            Debug.DrawLine(p6, p7, color, duration);
            Debug.DrawLine(p7, p8, color, duration);
            Debug.DrawLine(p8, p5, color, duration);

            Debug.DrawLine(p1, p5, color, duration);
            Debug.DrawLine(p2, p6, color, duration);
            Debug.DrawLine(p3, p7, color, duration);
            Debug.DrawLine(p4, p8, color, duration);
      }

      private void OnGUI()
      {
            if (!enabled) return;

            // Отображаем статистику на экране
            GUILayout.BeginArea(new Rect(10, 200, 300, 200));
            GUILayout.BeginVertical("box");

            GUILayout.Label("Статистика рейкастов:", GUI.skin.label);
            GUILayout.Label($"Всего: {totalRaycastAttempts}");
            GUILayout.Label($"Успешных: {successfulRaycasts}");
            GUILayout.Label($"Неудачных: {failedRaycasts}");

            if (totalRaycastAttempts > 0)
            {
                  float rate = (float)successfulRaycasts / totalRaycastAttempts * 100f;
                  GUILayout.Label($"Успешность: {rate:F1}%");
            }

            GUILayout.Label($"Плоскостей создано: {(arManager != null ? arManager.GeneratedPlanes.Count : 0)}");

            GUILayout.EndVertical();
            GUILayout.EndArea();
      }
}