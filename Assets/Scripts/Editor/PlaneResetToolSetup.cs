using UnityEngine;
using UnityEditor;

/// <summary>
/// Инструмент для быстрого добавления PlaneResetTool к сцене
/// </summary>
public class PlaneResetToolSetup : EditorWindow
{
      [MenuItem("Tools/🔥 Добавить PlaneResetTool к сцене")]
      public static void AddPlaneResetToolToScene()
      {
            // Проверяем, есть ли уже PlaneResetTool в сцене
            PlaneResetTool existingTool = FindObjectOfType<PlaneResetTool>();
            if (existingTool != null)
            {
                  Debug.LogWarning("[PlaneResetToolSetup] PlaneResetTool уже существует в сцене!");
                  Selection.activeGameObject = existingTool.gameObject;
                  return;
            }

            // Создаем новый GameObject с PlaneResetTool
            GameObject planeResetToolObject = new GameObject("PlaneResetTool");
            PlaneResetTool resetTool = planeResetToolObject.AddComponent<PlaneResetTool>();

            // Устанавливаем автоматический сброс по умолчанию
            SerializedObject serializedTool = new SerializedObject(resetTool);
            serializedTool.FindProperty("autoReset").boolValue = true;
            serializedTool.ApplyModifiedProperties();

            // Выделяем созданный объект
            Selection.activeGameObject = planeResetToolObject;

            Debug.Log("[PlaneResetToolSetup] ✅ PlaneResetTool добавлен к сцене!");
            Debug.Log("[PlaneResetToolSetup] 🎯 Используйте контекстное меню или горячие клавиши:");
            Debug.Log("  • Ctrl+R - Принудительное пересоздание всех плоскостей");
            Debug.Log("  • Ctrl+D - Удаление только больших плоскостей");
            Debug.Log("  • Правый клик на компоненте → Контекстное меню");
      }

      [MenuItem("Tools/📊 Анализ текущих плоскостей")]
      public static void AnalyzePlanes()
      {
            PlaneGeometryAnalyzer analyzer = FindObjectOfType<PlaneGeometryAnalyzer>();
            if (analyzer != null)
            {
                  // Используем рефлексию для вызова метода анализа
                  var method = analyzer.GetType().GetMethod("AnalyzePlanesGeometry");
                  if (method != null)
                  {
                        method.Invoke(analyzer, null);
                        Debug.Log("[PlaneResetToolSetup] ✅ Анализ плоскостей выполнен - смотрите консоль");
                  }
            }
            else
            {
                  Debug.LogError("[PlaneResetToolSetup] PlaneGeometryAnalyzer не найден в сцене!");
            }
      }

      [MenuItem("Tools/🗑️ Удалить все большие плоскости")]
      public static void DeleteOversizedPlanes()
      {
            ARManagerInitializer2 arManager = FindObjectOfType<ARManagerInitializer2>();
            if (arManager == null)
            {
                  Debug.LogError("[PlaneResetToolSetup] ARManagerInitializer2 не найден!");
                  return;
            }

            int deletedCount = 0;
            var planesToCheck = new System.Collections.Generic.List<GameObject>(arManager.GeneratedPlanes);

            foreach (var plane in planesToCheck)
            {
                  if (plane == null) continue;

                  var meshFilter = plane.GetComponent<MeshFilter>();
                  if (meshFilter != null && meshFilter.mesh != null)
                  {
                        var size = Vector3.Scale(meshFilter.mesh.bounds.size, plane.transform.localScale);

                        // Более строгие критерии: максимум 3.0м по любой стороне
                        if (size.x > 3.0f || size.y > 2.5f || size.z > 3.0f)
                        {
                              Debug.Log($"[PlaneResetToolSetup] ❌ Удаляем большую плоскость: {plane.name} - размер: {size.x:F2}x{size.y:F2}x{size.z:F2}м");

                              if (arManager.GeneratedPlanes.Contains(plane))
                              {
                                    arManager.GeneratedPlanes.Remove(plane);
                              }

                              DestroyImmediate(plane);
                              deletedCount++;
                        }
                  }
            }

            Debug.Log($"[PlaneResetToolSetup] ✅ Удалено {deletedCount} больших плоскостей");
      }

      [MenuItem("Tools/🔥 ЭКСТРЕННЫЙ СБРОС - Удалить ВСЕ плоскости")]
      public static void EmergencyResetAllPlanes()
      {
            if (!EditorUtility.DisplayDialog("Подтверждение",
                "Это удалит ВСЕ плоскости в сцене. Продолжить?",
                "Да, удалить все", "Отмена"))
            {
                  return;
            }

            ARManagerInitializer2 arManager = FindObjectOfType<ARManagerInitializer2>();
            if (arManager != null)
            {
                  int deletedCount = arManager.GeneratedPlanes.Count;
                  arManager.DeleteAllPlanes();
                  arManager.CleanupProblematicPersistentPlanes();
                  Debug.Log($"[PlaneResetToolSetup] 🔥 ЭКСТРЕННЫЙ СБРОС: Удалено {deletedCount} плоскостей");
            }

            // Также удаляем любые оставшиеся плоскости в сцене
            var allPlanes = FindObjectsOfType<GameObject>();
            int extraDeleted = 0;
            foreach (var obj in allPlanes)
            {
                  if (obj.name.Contains("MyARPlane") || obj.name.Contains("ARPlane"))
                  {
                        DestroyImmediate(obj);
                        extraDeleted++;
                  }
            }

            if (extraDeleted > 0)
            {
                  Debug.Log($"[PlaneResetToolSetup] 🧹 Дополнительно удалено {extraDeleted} объектов плоскостей");
            }

            Debug.Log("[PlaneResetToolSetup] ✅ Экстренный сброс завершен. Плоскости будут пересозданы автоматически.");
      }
}