using UnityEngine;
using UnityEditor;
using UnityEngine.XR.ARFoundation;
using Unity.XR.CoreUtils;
using System.Linq;

namespace WallPainting.Editor
{
      /// <summary>
      /// Инструмент для автоматической настройки WallPainter в сцене
      /// </summary>
      public class WallPainterSetup : EditorWindow
      {
            private GameObject arSessionOrigin;
            private WallSegmentation wallSegmentation;
            private Camera arCamera;
            private ARRaycastManager raycastManager;
            private Material wallPaintMaterial;

            private bool createPrefab = true;
            private bool disableOldComponents = true;
            private bool setupDebugMode = false;

            [MenuItem("AR Tools/Setup Wall Painter")]
            public static void ShowWindow()
            {
                  GetWindow<WallPainterSetup>("Wall Painter Setup");
            }

            private void OnEnable()
            {
                  FindRequiredComponents();
            }

            private void OnGUI()
            {
                  GUILayout.Label("Wall Painter Setup", EditorStyles.boldLabel);

                  EditorGUILayout.Space();

                  EditorGUILayout.HelpBox(
                      "Этот инструмент поможет настроить новую архитектуру генерации плоскостей стен. " +
                      "Он автоматически найдет необходимые компоненты и создаст недостающие.",
                      MessageType.Info
                  );

                  EditorGUILayout.Space();

                  // Отображение найденных компонентов
                  EditorGUILayout.LabelField("Найденные компоненты:", EditorStyles.boldLabel);

                  EditorGUI.BeginDisabledGroup(true);
                  arSessionOrigin = EditorGUILayout.ObjectField("AR Session Origin", arSessionOrigin, typeof(GameObject), true) as GameObject;
                  wallSegmentation = EditorGUILayout.ObjectField("Wall Segmentation", wallSegmentation, typeof(WallSegmentation), true) as WallSegmentation;
                  arCamera = EditorGUILayout.ObjectField("AR Camera", arCamera, typeof(Camera), true) as Camera;
                  raycastManager = EditorGUILayout.ObjectField("Raycast Manager", raycastManager, typeof(ARRaycastManager), true) as ARRaycastManager;
                  EditorGUI.EndDisabledGroup();

                  EditorGUILayout.Space();

                  // Опции настройки
                  EditorGUILayout.LabelField("Опции настройки:", EditorStyles.boldLabel);

                  createPrefab = EditorGUILayout.Toggle("Создать префаб плоскости", createPrefab);
                  wallPaintMaterial = EditorGUILayout.ObjectField("Материал покраски", wallPaintMaterial, typeof(Material), false) as Material;
                  disableOldComponents = EditorGUILayout.Toggle("Отключить старые компоненты", disableOldComponents);
                  setupDebugMode = EditorGUILayout.Toggle("Включить режим отладки", setupDebugMode);

                  EditorGUILayout.Space();

                  // Кнопка настройки
                  bool canSetup = arSessionOrigin != null && wallSegmentation != null;

                  EditorGUI.BeginDisabledGroup(!canSetup);
                  if (GUILayout.Button("Настроить Wall Painter", GUILayout.Height(40)))
                  {
                        SetupWallPainter();
                  }
                  EditorGUI.EndDisabledGroup();

                  if (!canSetup)
                  {
                        EditorGUILayout.HelpBox(
                            "Не все необходимые компоненты найдены. Убедитесь, что в сцене есть AR Session Origin и Wall Segmentation.",
                            MessageType.Warning
                        );
                  }

                  EditorGUILayout.Space();

                  // Кнопка обновления
                  if (GUILayout.Button("Обновить поиск компонентов"))
                  {
                        FindRequiredComponents();
                  }
            }

            private void FindRequiredComponents()
            {
                  // Сначала ищем XR Origin (новый подход)
                  var xrOrigin = GameObject.FindObjectOfType<XROrigin>();
                  if (xrOrigin != null)
                  {
                        arSessionOrigin = xrOrigin.gameObject;
                  }
                  else
                  {
                        // Fallback на устаревший ARSessionOrigin
#pragma warning disable CS0618 // Тип или член устарел
                        var arSessionOriginOld = GameObject.FindObjectOfType<ARSessionOrigin>();
#pragma warning restore CS0618
                        if (arSessionOriginOld != null)
                        {
                              arSessionOrigin = arSessionOriginOld.gameObject;
                        }
                  }

                  // Ищем Wall Segmentation
                  wallSegmentation = GameObject.FindObjectOfType<WallSegmentation>();

                  // Ищем AR Camera
                  arCamera = Camera.main;
                  if (arCamera == null && arSessionOrigin != null)
                  {
                        arCamera = arSessionOrigin.GetComponentInChildren<Camera>();
                  }

                  // Ищем ARRaycastManager
                  if (arSessionOrigin != null)
                  {
                        raycastManager = arSessionOrigin.GetComponent<ARRaycastManager>();
                  }
            }

            private void SetupWallPainter()
            {
                  // 1. Добавляем WallPainterController
                  WallPainterController controller = arSessionOrigin.GetComponent<WallPainterController>();
                  if (controller == null)
                  {
                        controller = arSessionOrigin.AddComponent<WallPainterController>();
                        Debug.Log("[WallPainterSetup] Добавлен WallPainterController");
                  }

                  // 2. Настраиваем ссылки
                  SerializedObject serializedController = new SerializedObject(controller);

                  serializedController.FindProperty("wallSegmentation").objectReferenceValue = wallSegmentation;
                  serializedController.FindProperty("arCamera").objectReferenceValue = arCamera;
                  serializedController.FindProperty("raycastManager").objectReferenceValue = raycastManager;

                  // 3. Создаем префаб если нужно
                  GameObject wallPlanePrefab = null;
                  if (createPrefab)
                  {
                        wallPlanePrefab = CreateWallPlanePrefab();
                        serializedController.FindProperty("wallPlanePrefab").objectReferenceValue = wallPlanePrefab;
                  }

                  // 4. Настраиваем материал
                  if (wallPaintMaterial == null)
                  {
                        wallPaintMaterial = CreateDefaultMaterial();
                  }
                  serializedController.FindProperty("wallPaintMaterial").objectReferenceValue = wallPaintMaterial;

                  // 5. Настраиваем режим отладки
                  serializedController.FindProperty("debugMode").boolValue = setupDebugMode;
                  serializedController.FindProperty("showContourVisualization").boolValue = setupDebugMode;

                  // 6. Настраиваем оптимальные параметры
                  serializedController.FindProperty("segmentationConfidence").floatValue = 0.75f;
                  serializedController.FindProperty("maxRaycastDistance").floatValue = 5.0f;
                  serializedController.FindProperty("minContourArea").intValue = 1000;
                  serializedController.FindProperty("contourSimplificationTolerance").floatValue = 2.0f;

                  serializedController.ApplyModifiedProperties();

                  // 7. Отключаем старые компоненты
                  if (disableOldComponents)
                  {
                        DisableOldComponents();
                  }

                  // 8. Сохраняем сцену
                  EditorUtility.SetDirty(arSessionOrigin);
                  UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(arSessionOrigin.scene);

                  Debug.Log("[WallPainterSetup] Настройка завершена успешно!");
                  EditorUtility.DisplayDialog("Wall Painter Setup",
                      "Настройка завершена успешно!\n\n" +
                      "Теперь вы можете запустить сцену и касаться стен для создания полноразмерных плоскостей.",
                      "OK");
            }

            private GameObject CreateWallPlanePrefab()
            {
                  // Создаем GameObject
                  GameObject wallPlane = new GameObject("WallPlanePrefab");

                  // Добавляем необходимые компоненты
                  MeshFilter meshFilter = wallPlane.AddComponent<MeshFilter>();
                  MeshRenderer meshRenderer = wallPlane.AddComponent<MeshRenderer>();
                  MeshCollider meshCollider = wallPlane.AddComponent<MeshCollider>();

                  // Настраиваем материал
                  if (wallPaintMaterial != null)
                  {
                        meshRenderer.material = wallPaintMaterial;
                  }

                  // Создаем папку для префабов если её нет
                  if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
                  {
                        AssetDatabase.CreateFolder("Assets", "Prefabs");
                  }

                  if (!AssetDatabase.IsValidFolder("Assets/Prefabs/WallPainting"))
                  {
                        AssetDatabase.CreateFolder("Assets/Prefabs", "WallPainting");
                  }

                  // Сохраняем как префаб
                  string prefabPath = "Assets/Prefabs/WallPainting/WallPlanePrefab.prefab";
                  GameObject prefab = PrefabUtility.SaveAsPrefabAsset(wallPlane, prefabPath);

                  // Удаляем временный объект из сцены
                  DestroyImmediate(wallPlane);

                  Debug.Log($"[WallPainterSetup] Создан префаб: {prefabPath}");

                  return prefab;
            }

            private Material CreateDefaultMaterial()
            {
                  // Создаем материал с URP Lit шейдером
                  Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                  if (shader == null)
                  {
                        // Fallback на стандартный шейдер
                        shader = Shader.Find("Standard");
                  }

                  Material material = new Material(shader);
                  material.name = "WallPaintMaterial";

                  // Настраиваем базовые параметры
                  material.SetColor("_BaseColor", new Color(0.9f, 0.9f, 0.9f, 1f));
                  material.SetFloat("_Smoothness", 0.3f);

                  // Сохраняем материал
                  if (!AssetDatabase.IsValidFolder("Assets/Materials"))
                  {
                        AssetDatabase.CreateFolder("Assets", "Materials");
                  }

                  if (!AssetDatabase.IsValidFolder("Assets/Materials/WallPainting"))
                  {
                        AssetDatabase.CreateFolder("Assets/Materials", "WallPainting");
                  }

                  string materialPath = "Assets/Materials/WallPainting/WallPaintMaterial.mat";
                  AssetDatabase.CreateAsset(material, materialPath);

                  Debug.Log($"[WallPainterSetup] Создан материал: {materialPath}");

                  return material;
            }

            private void DisableOldComponents()
            {
                  // Список типов компонентов для отключения
                  string[] componentTypesToDisable = {
                "RaycastController",
                "WallPlaneController",
                "ARPlaneDebugger", // Если использует массовый рейкастинг
                "PlaneDebugVisualizer" // Если создает множество отладочных лучей
            };

                  foreach (string typeName in componentTypesToDisable)
                  {
                        System.Type type = System.Type.GetType(typeName);
                        if (type == null)
                        {
                              // Пробуем найти с полным именем
                              type = System.AppDomain.CurrentDomain.GetAssemblies()
                                  .SelectMany(assembly => assembly.GetTypes())
                                  .FirstOrDefault(t => t.Name == typeName);
                        }

                        if (type != null)
                        {
                              Component[] components = GameObject.FindObjectsOfType(type) as Component[];
                              foreach (Component comp in components)
                              {
                                    if (comp != null)
                                    {
                                          (comp as Behaviour).enabled = false;
                                          Debug.Log($"[WallPainterSetup] Отключен компонент: {typeName} на {comp.gameObject.name}");
                                    }
                              }
                        }
                  }
            }
      }

      /// <summary>
      /// Валидатор для проверки правильности настройки
      /// </summary>
      [InitializeOnLoad]
      public static class WallPainterValidator
      {
            static WallPainterValidator()
            {
                  EditorApplication.delayCall += ValidateSetup;
            }

            private static void ValidateSetup()
            {
                  if (!EditorApplication.isPlayingOrWillChangePlaymode)
                  {
                        var controller = GameObject.FindObjectOfType<WallPainterController>();
                        if (controller != null)
                        {
                              SerializedObject so = new SerializedObject(controller);

                              // Проверяем критические ссылки
                              if (so.FindProperty("wallSegmentation").objectReferenceValue == null)
                              {
                                    Debug.LogWarning("[WallPainterValidator] WallPainterController: не установлена ссылка на WallSegmentation!");
                              }

                              if (so.FindProperty("wallPlanePrefab").objectReferenceValue == null)
                              {
                                    Debug.LogWarning("[WallPainterValidator] WallPainterController: не установлен префаб плоскости!");
                              }
                        }
                  }
            }
      }
}