using UnityEngine;
using UnityEditor;
using System.IO;

namespace WallPainting.Editor
{
      /// <summary>
      /// Утилита для быстрого создания префаба плоскости стены
      /// </summary>
      public static class CreateWallPlanePrefab
      {
            [MenuItem("AR Tools/Create Wall Plane Prefab")]
            public static void CreatePrefab()
            {
                  CreateWallPlanePrefabInternal();
            }

            [MenuItem("AR Tools/Create Wall Paint Material")]
            public static void CreateMaterial()
            {
                  CreateWallPaintMaterialInternal();
            }

            static GameObject CreateWallPlanePrefabInternal()
            {
                  // Путь для сохранения префаба
                  string folderPath = "Assets/Prefabs/WallPainting";
                  string prefabPath = Path.Combine(folderPath, "WallPlanePrefab.prefab");

                  // Создаем папку если её нет
                  if (!AssetDatabase.IsValidFolder(folderPath))
                  {
                        string[] folders = folderPath.Split('/');
                        string currentPath = folders[0];
                        for (int i = 1; i < folders.Length; i++)
                        {
                              if (!AssetDatabase.IsValidFolder(Path.Combine(currentPath, folders[i])))
                              {
                                    AssetDatabase.CreateFolder(currentPath, folders[i]);
                              }
                              currentPath = Path.Combine(currentPath, folders[i]);
                        }
                  }

                  // Создаем GameObject
                  GameObject planeGO = new GameObject("WallPlanePrefab");

                  // Добавляем необходимые компоненты
                  MeshFilter meshFilter = planeGO.AddComponent<MeshFilter>();
                  MeshRenderer meshRenderer = planeGO.AddComponent<MeshRenderer>();
                  MeshCollider meshCollider = planeGO.AddComponent<MeshCollider>();

                  // Создаем простой меш плоскости как заглушку
                  Mesh planeMesh = new Mesh();
                  planeMesh.name = "WallPlaneMesh";

                  // Вершины для квадрата
                  Vector3[] vertices = new Vector3[]
                  {
                        new Vector3(-0.5f, -0.5f, 0),
                        new Vector3(0.5f, -0.5f, 0),
                        new Vector3(0.5f, 0.5f, 0),
                        new Vector3(-0.5f, 0.5f, 0)
                  };

                  // UV координаты
                  Vector2[] uv = new Vector2[]
                  {
                        new Vector2(0, 0),
                        new Vector2(1, 0),
                        new Vector2(1, 1),
                        new Vector2(0, 1)
                  };

                  // Треугольники
                  int[] triangles = new int[]
                  {
                        0, 2, 1,
                        0, 3, 2
                  };

                  planeMesh.vertices = vertices;
                  planeMesh.uv = uv;
                  planeMesh.triangles = triangles;
                  planeMesh.RecalculateNormals();
                  planeMesh.RecalculateBounds();

                  meshFilter.mesh = planeMesh;

                  // Создаем префаб
                  GameObject prefab = PrefabUtility.SaveAsPrefabAsset(planeGO, prefabPath);
                  GameObject.DestroyImmediate(planeGO);

                  AssetDatabase.SaveAssets();
                  AssetDatabase.Refresh();

                  Debug.Log($"[CreateWallPlanePrefab] Префаб создан: {prefabPath}");

                  // Выделяем префаб в Project окне
                  Selection.activeObject = prefab;
                  EditorGUIUtility.PingObject(prefab);

                  return prefab;
            }

            static Material CreateWallPaintMaterialInternal()
            {
                  // Путь для сохранения материала
                  string folderPath = "Assets/Materials/WallPainting";
                  string materialPath = Path.Combine(folderPath, "WallPaintMaterial.mat");

                  // Создаем папку если её нет
                  if (!AssetDatabase.IsValidFolder(folderPath))
                  {
                        string[] folders = folderPath.Split('/');
                        string currentPath = folders[0];
                        for (int i = 1; i < folders.Length; i++)
                        {
                              if (!AssetDatabase.IsValidFolder(Path.Combine(currentPath, folders[i])))
                              {
                                    AssetDatabase.CreateFolder(currentPath, folders[i]);
                              }
                              currentPath = Path.Combine(currentPath, folders[i]);
                        }
                  }

                  // Находим подходящий шейдер
                  Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                  if (shader == null)
                  {
                        shader = Shader.Find("Standard");
                  }
                  if (shader == null)
                  {
                        shader = Shader.Find("Unlit/Color");
                  }

                  // Создаем материал
                  Material material = new Material(shader);
                  material.name = "WallPaintMaterial";

                  // Настраиваем свойства материала
                  material.color = new Color(0.9f, 0.9f, 0.9f, 1.0f); // Светло-серый цвет

                  // Для URP
                  if (material.HasProperty("_BaseColor"))
                  {
                        material.SetColor("_BaseColor", material.color);
                  }

                  // Делаем материал немного прозрачным для визуализации
                  if (material.HasProperty("_Surface"))
                  {
                        material.SetFloat("_Surface", 1); // 0 = Opaque, 1 = Transparent
                        material.SetFloat("_Blend", 0); // 0 = Alpha, 1 = Premultiply, 2 = Additive, 3 = Multiply
                        material.renderQueue = 3000;
                  }

                  // Сохраняем материал
                  AssetDatabase.CreateAsset(material, materialPath);
                  AssetDatabase.SaveAssets();
                  AssetDatabase.Refresh();

                  Debug.Log($"[CreateWallPlanePrefab] Материал создан: {materialPath}");

                  // Выделяем материал в Project окне
                  Selection.activeObject = material;
                  EditorGUIUtility.PingObject(material);

                  return material;
            }

            [MenuItem("AR Tools/Quick Setup Wall Painter", false, 1)]
            static void QuickSetupWallPainter()
            {
                  Debug.Log("[CreateWallPlanePrefab] Запуск быстрой настройки Wall Painter...");

                  // Находим WallPainterController
                  WallPainterController wallPainter = Object.FindObjectOfType<WallPainterController>();
                  if (wallPainter == null)
                  {
                        Debug.LogError("[CreateWallPlanePrefab] WallPainterController не найден в сцене!");
                        return;
                  }

                  // Получаем SerializedObject для редактирования private полей
                  SerializedObject serializedWallPainter = new SerializedObject(wallPainter);

                  // Настраиваем Wall Plane Prefab
                  SerializedProperty wallPlanePrefabProp = serializedWallPainter.FindProperty("wallPlanePrefab");
                  if (wallPlanePrefabProp != null && wallPlanePrefabProp.objectReferenceValue == null)
                  {
                        GameObject prefab = CreateWallPlanePrefabInternal();
                        if (prefab != null)
                        {
                              wallPlanePrefabProp.objectReferenceValue = prefab;
                              Debug.Log("[CreateWallPlanePrefab] ✅ Wall Plane Prefab создан и назначен");
                        }
                  }

                  // Настраиваем Wall Paint Material
                  SerializedProperty wallPaintMaterialProp = serializedWallPainter.FindProperty("wallPaintMaterial");
                  if (wallPaintMaterialProp != null && wallPaintMaterialProp.objectReferenceValue == null)
                  {
                        Material material = CreateWallPaintMaterialInternal();
                        if (material != null)
                        {
                              wallPaintMaterialProp.objectReferenceValue = material;
                              Debug.Log("[CreateWallPlanePrefab] ✅ Wall Paint Material создан и назначен");
                        }
                  }

                  // Находим и отключаем ARManagerInitializer2
                  var arManager2Type = System.Type.GetType("ARManagerInitializer2");
                  if (arManager2Type != null)
                  {
                        var arManager2 = Object.FindObjectOfType(arManager2Type) as MonoBehaviour;
                        if (arManager2 != null)
                        {
                              arManager2.enabled = false;
                              Debug.Log("[CreateWallPlanePrefab] ✅ ARManagerInitializer2 найден и отключен");

                              // Также отключаем связанные компоненты
                              DisableRelatedComponents();
                        }
                  }

                  // Включаем режим отладки для WallPainterController
                  SerializedProperty debugModeProp = serializedWallPainter.FindProperty("debugMode");
                  if (debugModeProp != null)
                  {
                        debugModeProp.boolValue = true;
                  }

                  // Применяем изменения
                  serializedWallPainter.ApplyModifiedProperties();
                  EditorUtility.SetDirty(wallPainter);

                  Debug.Log("[CreateWallPlanePrefab] ✅ Быстрая настройка завершена!");

                  // Показываем сообщение пользователю
                  EditorUtility.DisplayDialog(
                      "Настройка завершена",
                      "WallPainterController настроен!\n\n" +
                      "• Wall Plane Prefab создан\n" +
                      "• Wall Paint Material создан\n" +
                      "• ARManagerInitializer2 отключен\n" +
                      "• Режим отладки включен\n\n" +
                      "Теперь можно тестировать приложение!",
                      "OK"
                  );
            }

            static void DisableRelatedComponents()
            {
                  // Отключаем компоненты, которые зависят от ARManagerInitializer2
                  string[] componentsToDisable = {
                        "PlaneGeometryAnalyzer",
                        "WallMaterialSetter",
                        "PlaneDebugVisualizer",
                        "PlaneResetTool"
                  };

                  foreach (string componentName in componentsToDisable)
                  {
                        MonoBehaviour[] components = Object.FindObjectsOfType<MonoBehaviour>();
                        foreach (var comp in components)
                        {
                              if (comp.GetType().Name == componentName)
                              {
                                    comp.enabled = false;
                                    Debug.Log($"[CreateWallPlanePrefab] Отключен компонент: {componentName}");
                              }
                        }
                  }
            }
      }
}