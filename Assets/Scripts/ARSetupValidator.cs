using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Unity.XR.CoreUtils;
using System.IO;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Компонент для проверки и автоматической настройки AR сцены
/// </summary>
[DefaultExecutionOrder(-100)] // Выполняем раньше большинства скриптов
public class ARSetupValidator : MonoBehaviour
{
      [Header("Компоненты для проверки")]
      [SerializeField] private bool validateARSession = true;
      [SerializeField] private bool validateARCameraManager = true;
      [SerializeField] private bool validateARPlaneManager = true;
      [SerializeField] private bool validateWallSegmentation = true;
      [SerializeField] private bool validateWallPaintEffect = true;

      [Header("Автоматическая настройка")]
      [SerializeField] private bool autoFixIssues = true;
      [SerializeField] private bool logDetailedInfo = true;

      [Header("Результаты проверки")]
      [SerializeField] private string validationStatus = "Не выполнено";
      [SerializeField] private List<string> issues = new List<string>();

      // Необходимые компоненты
      private ARSession arSession;
      private XROrigin xrOrigin;
      private ARCameraManager arCameraManager;
      private ARPlaneManager arPlaneManager;
      private WallSegmentation wallSegmentation;
      private MonoBehaviour wallPaintEffect; // Используем MonoBehaviour, так как у нас нет доступа к типу WallPaintEffect

      // Результаты проверки
      private bool isSetupValid = false;

      private void Awake()
      {
            RunValidation();
      }

      /// <summary>
      /// Unity Start метод
      /// </summary>
      private void Start()
      {
            // Находим необходимые компоненты
            FindRequiredComponents();

            // Проверяем и исправляем настройку AR сцены
            ValidateAndFixSetup();

            // Запускаем проверку с задержкой для более надежной инициализации AR
            Invoke("DelayedValidation", 0.5f);
      }

      /// <summary>
      /// Отложенная проверка конфигурации для более надежной инициализации AR
      /// </summary>
      private void DelayedValidation()
      {
            // Запускаем проверку еще раз после полной инициализации AR
            ValidateAndFixSetup();
      }

      /// <summary>
      /// Выполняет проверку настройки AR сцены
      /// </summary>
      public void RunValidation()
      {
            Debug.Log("ARSetupValidator: Запуск проверки настройки AR сцены...");
            issues.Clear();
            validationStatus = "Выполняется проверка...";

            // Находим необходимые компоненты
            FindRequiredComponents();

            // Проверяем каждый компонент
            bool hasErrors = false;

            if (validateARSession)
                  hasErrors |= !ValidateARSession();

            if (validateARCameraManager)
                  hasErrors |= !ValidateARCameraManager();

            if (validateARPlaneManager)
                  hasErrors |= !ValidateARPlaneManager();

            if (validateWallSegmentation)
                  hasErrors |= !ValidateWallSegmentation();

            if (validateWallPaintEffect)
                  hasErrors |= !ValidateWallPaintEffect();

            // Выполняем автоматическое исправление, если это включено
            if (hasErrors && autoFixIssues)
            {
                  FixIssues();
                  // Повторная проверка после исправлений
                  RunValidation();
                  return;
            }

            // Обновляем статус
            isSetupValid = !hasErrors;
            validationStatus = isSetupValid ? "Настройка корректна" : "Есть проблемы с настройкой";

            // Логируем результаты
            LogValidationResults();
      }

      /// <summary>
      /// Ищет необходимые компоненты в сцене
      /// </summary>
      private void FindRequiredComponents()
      {
            arSession = FindObjectOfType<ARSession>();
            xrOrigin = FindObjectOfType<XROrigin>();

            if (xrOrigin != null)
            {
                  Transform cameraTransform = xrOrigin.Camera.transform;
                  if (cameraTransform != null)
                  {
                        arCameraManager = cameraTransform.GetComponent<ARCameraManager>();

                        // Проверяем также наличие ARLightEstimation
                        if (arCameraManager != null && arCameraManager.GetComponent<ARLightEstimation>() == null)
                        {
                              issues.Add("ARLightEstimation отсутствует на AR камере");

                              if (autoFixIssues)
                              {
                                    arCameraManager.gameObject.AddComponent<ARLightEstimation>();
                                    issues.Add("Компонент ARLightEstimation автоматически добавлен на AR камеру");
                              }
                        }
                  }
            }

            arPlaneManager = FindObjectOfType<ARPlaneManager>();
            wallSegmentation = FindObjectOfType<WallSegmentation>();

            // Ищем WallPaintEffect среди всех MonoBehaviour
            var monoBehaviours = FindObjectsOfType<MonoBehaviour>();
            foreach (var behaviour in monoBehaviours)
            {
                  if (behaviour.GetType().Name == "WallPaintEffect")
                  {
                        wallPaintEffect = behaviour;
                        break;
                  }
            }
      }

      /// <summary>
      /// Проверяет настройку ARSession
      /// </summary>
      private bool ValidateARSession()
      {
            if (arSession == null)
            {
                  issues.Add("ARSession отсутствует в сцене");
                  return false;
            }

            if (xrOrigin == null)
            {
                  issues.Add("XROrigin отсутствует в сцене");
                  return false;
            }

            // Дополнительные проверки для ARSession

            return true;
      }

      /// <summary>
      /// Проверяет настройку ARCameraManager
      /// </summary>
      private bool ValidateARCameraManager()
      {
            if (arCameraManager == null)
            {
                  issues.Add("ARCameraManager отсутствует в сцене");
                  return false;
            }

            Camera arCamera = arCameraManager.GetComponent<Camera>();
            if (arCamera == null)
            {
                  issues.Add("AR камера не имеет компонента Camera");
                  return false;
            }

            // Проверка тегов
            if (arCamera.tag != "MainCamera")
            {
                  issues.Add("AR камера не имеет тег 'MainCamera'");
                  if (autoFixIssues)
                  {
                        arCamera.tag = "MainCamera";
                        issues.Add("Тег AR камеры автоматически установлен на 'MainCamera'");
                  }
            }

            return true;
      }

      /// <summary>
      /// Проверяет настройку ARPlaneManager
      /// </summary>
      private bool ValidateARPlaneManager()
      {
            if (arPlaneManager == null)
            {
                  issues.Add("ARPlaneManager отсутствует в сцене");
                  return false;
            }

            if (arPlaneManager.planePrefab == null)
            {
                  issues.Add("ARPlaneManager не имеет назначенного префаба плоскости");
                  return false;
            }

            // Проверяем материал префаба плоскости
            GameObject planePrefab = arPlaneManager.planePrefab;
            MeshRenderer meshRenderer = planePrefab.GetComponent<MeshRenderer>();

            if (meshRenderer == null)
            {
                  issues.Add("Префаб AR плоскости не содержит MeshRenderer");
                  return false;
            }

            if (meshRenderer.sharedMaterial == null)
            {
                  issues.Add("Префабу AR плоскости не назначен материал");
                  return false;
            }

            return true;
      }

      /// <summary>
      /// Проверяет настройку WallSegmentation
      /// </summary>
      private bool ValidateWallSegmentation()
      {
            if (wallSegmentation == null)
            {
                  issues.Add("WallSegmentation отсутствует в сцене");
                  return false;
            }

            // Проверяем наличие модели
            if (!wallSegmentation.IsModelInitialized)
            {
                  issues.Add("Модель сегментации не инициализирована в WallSegmentation");
                  return false;
            }

            // Проверяем маску сегментации
            if (wallSegmentation.segmentationMaskTexture == null)
            {
                  issues.Add("Текстура маски сегментации не создана в WallSegmentation");
                  return false;
            }

            return true;
      }

      /// <summary>
      /// Проверяет настройку WallPaintEffect
      /// </summary>
      private bool ValidateWallPaintEffect()
      {
            if (wallPaintEffect == null)
            {
                  issues.Add("WallPaintEffect отсутствует в сцене");
                  return false;
            }

            // Проверяем поля через рефлексию, так как у нас нет доступа к типу
            System.Type type = wallPaintEffect.GetType();

            // Проверяем материал
            object materialFieldValue = type.GetField("wallPaintMaterial").GetValue(wallPaintEffect);
            if (materialFieldValue == null)
            {
                  issues.Add("WallPaintEffect не имеет назначенного материала");
                  return false;
            }

            // Проверяем ссылку на WallSegmentation
            object segmentationFieldValue = type.GetField("wallSegmentation").GetValue(wallPaintEffect);
            if (segmentationFieldValue == null)
            {
                  issues.Add("WallPaintEffect не имеет ссылки на WallSegmentation");
                  if (autoFixIssues && wallSegmentation != null)
                  {
                        type.GetField("wallSegmentation").SetValue(wallPaintEffect, wallSegmentation);
                        issues.Add("Ссылка на WallSegmentation автоматически установлена в WallPaintEffect");
                  }
            }

            return true;
      }

      /// <summary>
      /// Пытается исправить обнаруженные проблемы
      /// </summary>
      private void FixIssues()
      {
            Debug.Log("ARSetupValidator: Пытаемся исправить обнаруженные проблемы...");

            // Создаем необходимые компоненты, если их нет
            if (arSession == null)
            {
                  GameObject sessionObj = new GameObject("AR Session");
                  arSession = sessionObj.AddComponent<ARSession>();
                  issues.Add("ARSession автоматически создан");
            }

            if (xrOrigin == null)
            {
                  GameObject sessionOriginObj = new GameObject("AR Session Origin");
                  xrOrigin = sessionOriginObj.AddComponent<XROrigin>();

                  // Создаем камеру
                  GameObject cameraObj = new GameObject("AR Camera");
                  cameraObj.transform.SetParent(sessionOriginObj.transform);
                  Camera arCamera = cameraObj.AddComponent<Camera>();
                  arCamera.tag = "MainCamera";

                  arCameraManager = cameraObj.AddComponent<ARCameraManager>();
                  cameraObj.AddComponent<ARLightEstimation>();

                  issues.Add("XROrigin с камерой автоматически создан");
            }

            if (arPlaneManager == null && xrOrigin != null)
            {
                  arPlaneManager = xrOrigin.gameObject.AddComponent<ARPlaneManager>();
                  issues.Add("ARPlaneManager автоматически создан");

                  // Не создаем префаб плоскости, это сложнее
            }

            // Для WallSegmentation и WallPaintEffect автоматическое создание сложнее,
            // так как требуется настройка многих зависимостей

            // Поиск и исправление ссылок в WallSegmentation
            if (wallSegmentation != null)
            {
                  // Используем свойства-геттеры вместо прямого доступа к приватным полям
                  if (wallSegmentation.ARSessionManager == null)
                  {
                        var sessionManager = FindObjectOfType<ARSessionManager>();
                        if (sessionManager != null)
                        {
                              wallSegmentation.ARSessionManager = sessionManager;
                              issues.Add("Ссылка на ARSessionManager в WallSegmentation автоматически установлена");
                        }
                  }
                  
                  if (wallSegmentation.XROrigin == null)
                  {
                        if (xrOrigin != null)
                        {
                              wallSegmentation.XROrigin = xrOrigin;
                              issues.Add("Ссылка на XROrigin в WallSegmentation автоматически установлена");
                        }
                  }
            }
      }

      /// <summary>
      /// Логирует результаты проверки
      /// </summary>
      private void LogValidationResults()
      {
            if (logDetailedInfo)
            {
                  string resultLog = "РЕЗУЛЬТАТЫ ПРОВЕРКИ AR СЦЕНЫ:\n";
                  resultLog += $"Статус: {validationStatus}\n";

                  if (issues.Count > 0)
                  {
                        resultLog += "Обнаружены проблемы:\n";
                        foreach (string issue in issues)
                        {
                              resultLog += $"- {issue}\n";
                        }
                  }
                  else
                  {
                        resultLog += "Проблем не обнаружено, настройка корректна.";
                  }

                  Debug.Log(resultLog);
            }
            else
            {
                  Debug.Log($"ARSetupValidator: {validationStatus}");
            }
      }

      /// <summary>
      /// Возвращает статус валидации
      /// </summary>
      public bool IsSetupValid()
      {
            return isSetupValid;
      }

      /// <summary>
      /// Возвращает список проблем
      /// </summary>
      public List<string> GetIssues()
      {
            return new List<string>(issues);
      }

      private void ValidateAndFixSetup()
      {
            Debug.Log("ARSetupValidator: Начало проверки настройки AR сцены");

            // Находим необходимые компоненты, если они не были найдены ранее
            FindRequiredComponents();

            // 1. Проверка ARSession
            if (arSession == null)
            {
                  Debug.LogError("ARSetupValidator: ARSession не найден. Необходимо добавить ARSession в сцену.");
                  return;
            }

            // 2. Проверка XROrigin
            if (xrOrigin == null)
            {
                  Debug.LogError("ARSetupValidator: XROrigin не найден. Необходимо добавить XROrigin в сцену.");
                  return;
            }

            // 3. Проверка ARCameraManager
            if (arCameraManager == null && xrOrigin != null)
            {
                  Debug.LogWarning("ARSetupValidator: ARCameraManager не найден. Проверяем камеру XROrigin.");

                  Camera arCamera = xrOrigin.Camera;
                  if (arCamera != null)
                  {
                        arCameraManager = arCamera.GetComponent<ARCameraManager>();
                        if (arCameraManager == null)
                        {
                              Debug.Log("ARSetupValidator: Добавляем ARCameraManager к камере XROrigin.");
                              arCameraManager = arCamera.gameObject.AddComponent<ARCameraManager>();
                        }
                  }
            }

            // 4. Проверка ARPlaneManager
            ARPlaneManager planeManager = FindObjectOfType<ARPlaneManager>();
            if (planeManager == null)
            {
                  Debug.LogWarning("ARSetupValidator: ARPlaneManager не найден. Добавляем его к XROrigin.");
                  planeManager = xrOrigin.gameObject.AddComponent<ARPlaneManager>();
            }

            // 5. Проверка настройки Prefab для ARPlaneManager
            if (planeManager != null && planeManager.planePrefab == null)
            {
                  Debug.LogWarning("ARSetupValidator: ARPlaneManager не имеет назначенного префаба. Создаем стандартный префаб.");

                  // Создаем простой префаб с сеткой и коллайдером
                  GameObject planePrefab = new GameObject("AR Plane Prefab");
                  planePrefab.AddComponent<MeshFilter>();
                  MeshRenderer renderer = planePrefab.AddComponent<MeshRenderer>();
                  planePrefab.AddComponent<ARPlane>();
                  planePrefab.AddComponent<MeshCollider>();

                  // Создаем материал для AR плоскостей
                  Shader arPlaneShader = Shader.Find("Universal Render Pipeline/Simple Lit");
                  if (arPlaneShader != null)
                  {
                        Material planeMaterial = new Material(arPlaneShader);
                        planeMaterial.name = "AR Plane Material";
                        planeMaterial.color = new Color(1f, 1f, 1f, 0.5f); // Полупрозрачный белый
                        renderer.material = planeMaterial;
                  }

                  // Сохраняем префаб в ресурсы, чтобы он был доступен в рантайме
                  if (!Directory.Exists("Assets/Resources"))
                  {
                        Directory.CreateDirectory("Assets/Resources");
                  }

#if UNITY_EDITOR
                  // Только в редакторе сохраняем как настоящий ассет
                  UnityEditor.PrefabUtility.SaveAsPrefabAsset(planePrefab, "Assets/Resources/ARPlanePrefab.prefab");
                  UnityEditor.AssetDatabase.Refresh();
                  Destroy(planePrefab); // Удаляем временный объект
                  planeManager.planePrefab = Resources.Load<GameObject>("ARPlanePrefab");
#else
                  // В рантайме устанавливаем созданный объект напрямую
                  planePrefab.SetActive(false); // Скрываем его, т.к. это шаблон
                  DontDestroyOnLoad(planePrefab); // Предотвращаем удаление
                  planeManager.planePrefab = planePrefab;
#endif
            }

            // 6. Проверка настройки ARPlaneManager
            if (planeManager != null)
            {
                  // Включаем обнаружение вертикальных плоскостей (стен)
                  if (!planeManager.requestedDetectionMode.HasFlag(PlaneDetectionMode.Vertical))
                  {
                        Debug.Log("ARSetupValidator: Включаем обнаружение вертикальных плоскостей в ARPlaneManager.");
                        planeManager.requestedDetectionMode |= PlaneDetectionMode.Vertical;
                  }
            }

            // 7. Проверка WallSegmentation
            if (wallSegmentation == null)
            {
                  Debug.LogWarning("ARSetupValidator: WallSegmentation не найден. Добавляем компонент.");
                  wallSegmentation = FindObjectOfType<WallSegmentation>();
                  if (wallSegmentation == null)
                  {
                        wallSegmentation = xrOrigin.gameObject.AddComponent<WallSegmentation>();
                  }

                  // Устанавливаем ссылки на компоненты
                  if (arCameraManager != null)
                  {
                        wallSegmentation.ARCameraManager = arCameraManager;
                  }
                  
                  var sessionManager = FindObjectOfType<ARSessionManager>();
                  if (sessionManager != null)
                  {
                        wallSegmentation.ARSessionManager = sessionManager;
                  }
                  
                  if (xrOrigin != null)
                  {
                        wallSegmentation.XROrigin = xrOrigin;
                  }
            }

            // 8. Проверка WallPaintEffect
            GameObject mainCameraObject = xrOrigin.Camera.gameObject;
            if (wallPaintEffect == null)
            {
                  Debug.LogWarning("ARSetupValidator: WallPaintEffect не найден. Проверяем камеру.");
                  wallPaintEffect = mainCameraObject.GetComponent<WallPaintEffect>();
                  if (wallPaintEffect == null)
                  {
                        Debug.Log("ARSetupValidator: Добавляем WallPaintEffect к основной камере.");
                        wallPaintEffect = mainCameraObject.AddComponent<WallPaintEffect>();
                  }
            }

            // 9. Проверка связи между компонентами
            if (wallPaintEffect is WallPaintEffect effect)
            {
                  // Устанавливаем ссылку на WallSegmentation
                  System.Reflection.FieldInfo wallSegField = wallPaintEffect.GetType().GetField("wallSegmentation", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
                  if (wallSegField != null && wallSegField.GetValue(wallPaintEffect) == null)
                  {
                        Debug.Log("ARSetupValidator: Устанавливаем ссылку на WallSegmentation в WallPaintEffect.");
                        wallSegField.SetValue(wallPaintEffect, wallSegmentation);
                  }
            }

            // 10. Проверка наличия ARLightEstimation
            ARLightEstimation lightEstimation = FindObjectOfType<ARLightEstimation>();
            if (lightEstimation == null && arCameraManager != null)
            {
                  Debug.Log("ARSetupValidator: Добавляем ARLightEstimation на камеру.");
                  lightEstimation = arCameraManager.gameObject.AddComponent<ARLightEstimation>();
            }

            Debug.Log("ARSetupValidator: Проверка и настройка AR сцены завершены.");
      }
}