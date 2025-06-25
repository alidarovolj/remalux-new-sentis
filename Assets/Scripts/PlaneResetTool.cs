using UnityEngine;

/// <summary>
/// Инструмент для пересоздания плоскостей с исправленными размерами
/// </summary>
public class PlaneResetTool : MonoBehaviour
{
      [Header("Инструменты пересоздания")]
      [SerializeField] private bool autoReset = true;

      private ARManagerInitializer2 arManager;

      private void Start()
      {
            arManager = FindObjectOfType<ARManagerInitializer2>();

            if (arManager == null)
            {
                  Debug.LogError("[PlaneResetTool] ARManagerInitializer2 не найден!");
                  return;
            }

            if (autoReset)
            {
                  // Автоматически пересоздаем плоскости через 2 секунды после запуска
                  Invoke(nameof(ForceResetAllPlanes), 2.0f);
            }

            Debug.Log("[PlaneResetTool] ✅ Инициализирован. Готов к пересозданию плоскостей.");
      }

      [ContextMenu("🔥 ПРИНУДИТЕЛЬНО ПЕРЕСОЗДАТЬ ВСЕ ПЛОСКОСТИ")]
      public void ForceResetAllPlanes()
      {
            if (arManager == null)
            {
                  Debug.LogError("[PlaneResetTool] ARManagerInitializer2 не найден!");
                  return;
            }

            Debug.Log("[PlaneResetTool] 🔥 НАЧИНАЕМ ПРИНУДИТЕЛЬНОЕ ПЕРЕСОЗДАНИЕ ПЛОСКОСТЕЙ...");

            // 1. Удаляем ВСЕ существующие плоскости
            int deletedCount = arManager.GeneratedPlanes.Count;
            arManager.DeleteAllPlanes();
            Debug.Log($"[PlaneResetTool] ✅ Удалено {deletedCount} старых плоскостей");

            // 2. Очищаем любые проблемные плоскости
            arManager.CleanupProblematicPersistentPlanes();

            // 3. Принудительно запускаем обновление маски сегментации
            StartCoroutine(ForceSegmentationUpdate());

            Debug.Log("[PlaneResetTool] 🎯 Пересоздание запущено! Ожидайте новые плоскости с правильными размерами...");
      }

      private System.Collections.IEnumerator ForceSegmentationUpdate()
      {
            // Ждем немного чтобы удаление завершилось
            yield return new UnityEngine.WaitForSeconds(0.5f);

            // Находим WallSegmentation компонент
            var wallSegmentation = FindObjectOfType<WallSegmentation>();
            if (wallSegmentation != null)
            {
                  Debug.Log("[PlaneResetTool] 🔄 Принудительно обновляем сегментацию...");

                  // Принудительно запускаем обновление через рефлексию
                  var updateMethod = wallSegmentation.GetType().GetMethod("Update",
                      System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                  if (updateMethod != null)
                  {
                        // Запускаем Update несколько раз для принудительной обработки
                        for (int i = 0; i < 5; i++)
                        {
                              updateMethod.Invoke(wallSegmentation, null);
                              yield return new UnityEngine.WaitForSeconds(0.1f);
                        }
                        Debug.Log("[PlaneResetTool] ✅ Принудительное обновление сегментации завершено");
                  }
            }

            yield return new UnityEngine.WaitForSeconds(2.0f);

            // Проверяем результат
            CheckResults();
      }

      private void CheckResults()
      {
            if (arManager == null) return;

            int newPlaneCount = arManager.GeneratedPlanes.Count;
            Debug.Log($"[PlaneResetTool] 📊 РЕЗУЛЬТАТ: Создано {newPlaneCount} новых плоскостей");

            if (newPlaneCount > 0)
            {
                  Debug.Log("[PlaneResetTool] 🎉 УСПЕХ! Новые плоскости созданы с исправленными размерами!");

                  // Анализируем новые размеры
                  var analyzer = FindObjectOfType<PlaneGeometryAnalyzer>();
                  if (analyzer != null)
                  {
                        analyzer.AnalyzePlanesGeometry();
                  }
            }
            else
            {
                  Debug.LogWarning("[PlaneResetTool] ⚠️ Новые плоскости не созданы. Возможно, нужно подождать еще или проверить сегментацию.");
            }
      }

      [ContextMenu("📊 Анализ текущих плоскостей")]
      public void AnalyzeCurrentPlanes()
      {
            var analyzer = FindObjectOfType<PlaneGeometryAnalyzer>();
            if (analyzer != null)
            {
                  analyzer.AnalyzePlanesGeometry();
            }
            else
            {
                  Debug.LogError("[PlaneResetTool] PlaneGeometryAnalyzer не найден!");
            }
      }

      [ContextMenu("🗑️ Удалить только большие плоскости")]
      public void DeleteOversizedPlanesOnly()
      {
            if (arManager == null) return;

            Debug.Log("[PlaneResetTool] 🗑️ Удаляем только слишком большие плоскости...");

            int deletedCount = 0;
            var planesToCheck = new System.Collections.Generic.List<GameObject>(arManager.GeneratedPlanes);

            foreach (var plane in planesToCheck)
            {
                  if (plane == null) continue;

                  var meshFilter = plane.GetComponent<MeshFilter>();
                  if (meshFilter != null && meshFilter.mesh != null)
                  {
                        var size = Vector3.Scale(meshFilter.mesh.bounds.size, plane.transform.localScale);

                        // Проверяем размеры
                        if (size.x > 4.0f || size.y > 2.8f || size.z > 4.0f)
                        {
                              Debug.Log($"[PlaneResetTool] ❌ Удаляем большую плоскость: {plane.name} - размер: {size.x:F2}x{size.y:F2}x{size.z:F2}м");

                              // Удаляем безопасно
                              if (arManager.GeneratedPlanes.Contains(plane))
                              {
                                    arManager.GeneratedPlanes.Remove(plane);
                              }

                              GameObject.DestroyImmediate(plane);
                              deletedCount++;
                        }
                  }
            }

            Debug.Log($"[PlaneResetTool] ✅ Удалено {deletedCount} больших плоскостей");
      }

      private void Update()
      {
            // Горячие клавиши для быстрого доступа
            if (Input.GetKeyDown(KeyCode.R) && Input.GetKey(KeyCode.LeftControl))
            {
                  ForceResetAllPlanes();
            }

            if (Input.GetKeyDown(KeyCode.D) && Input.GetKey(KeyCode.LeftControl))
            {
                  DeleteOversizedPlanesOnly();
            }
      }
}