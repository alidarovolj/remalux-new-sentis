using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.XR.ARFoundation;
using UnityEngine.Rendering.Universal;
using System.IO;
using UnityEngine.EventSystems;
using System.Reflection;
using UnityEngine.UI;
using Unity.XR.CoreUtils;
using UnityEngine.InputSystem;
using UnityEngine.XR.ARFoundation.InternalUtils;
using System.Linq;
using System;
#if UNITY_IOS
using UnityEngine.XR.ARKit;
#endif

/// <summary>
/// Утилита для быстрого создания и настройки сцены с AR/ML функциональностью.
/// Улучшена в соответствии с рекомендациями по организации иерархии сцены.
/// </summary>
public class SceneSetupUtility : EditorWindow
{
    private bool setupAR = true;
    private bool setupUI = true;
    private bool setupWallSegmentation = true;
    private bool setupWallPainting = true;
    private bool setupEventSystem = true;
    private bool setupLighting = true;

    private string sceneName = "AR_WallPainting";

    // Конфигурационный ScriptableObject для настроек
    private ARSceneConfig sceneConfig;

    [MenuItem("Remalux/Создать AR сцену")]
    public static void ShowWindow()
    {
        GetWindow<SceneSetupUtility>("Создать AR сцену");
    }

    private void OnEnable()
    {
        // Ищем существующую конфигурацию или создаем новую
        sceneConfig = AssetDatabase.FindAssets("t:ARSceneConfig")
            .Select(AssetDatabase.GUIDToAssetPath)
            .Select(AssetDatabase.LoadAssetAtPath<ARSceneConfig>)
            .FirstOrDefault();

        if (sceneConfig == null)
        {
            CreateDefaultConfig();
        }
    }

    private void CreateDefaultConfig()
    {
        // Создаем конфигурацию по умолчанию
        sceneConfig = ScriptableObject.CreateInstance<ARSceneConfig>();
        string configPath = "Assets/Config/ARSceneConfig.asset";
        string directory = Path.GetDirectoryName(configPath);

        if (!Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        AssetDatabase.CreateAsset(sceneConfig, configPath);
        AssetDatabase.SaveAssets();
    }

    private void OnGUI()
    {
        GUILayout.Label("Настройка AR сцены для перекраски стен", EditorStyles.boldLabel);

        EditorGUILayout.Space();

        // Отображаем поле для ScriptableObject конфигурации
        EditorGUI.BeginChangeCheck();
        sceneConfig = (ARSceneConfig)EditorGUILayout.ObjectField("Конфигурация", sceneConfig, typeof(ARSceneConfig), false);
        if (EditorGUI.EndChangeCheck() && sceneConfig == null)
        {
            CreateDefaultConfig();
        }

        EditorGUILayout.Space();

        if (sceneConfig != null)
        {
            // Редактирование конфигурации
            if (GUILayout.Button("Редактировать конфигурацию"))
            {
                Selection.activeObject = sceneConfig;
                EditorGUIUtility.PingObject(sceneConfig);
            }
        }

        EditorGUILayout.Space();

        // Чекбоксы для компонентов
        setupAR = EditorGUILayout.Toggle("Настроить AR компоненты", setupAR);
        setupUI = EditorGUILayout.Toggle("Настроить UI", setupUI);
        setupWallSegmentation = EditorGUILayout.Toggle("Настроить сегментацию стен", setupWallSegmentation);
        setupWallPainting = EditorGUILayout.Toggle("Настроить эффект перекраски", setupWallPainting);
        setupEventSystem = EditorGUILayout.Toggle("Добавить Event System", setupEventSystem);
        setupLighting = EditorGUILayout.Toggle("Настроить освещение", setupLighting);

        EditorGUILayout.Space();

        sceneName = EditorGUILayout.TextField("Имя сцены", sceneName);

        EditorGUILayout.Space();

        if (GUILayout.Button("Создать сцену"))
        {
            CreateScene();
        }
    }

    private void CreateScene()
    {
        // Проверяем наличие Unity Sentis
        bool isSentisAvailable = IsSentisInstalled();
        if (!isSentisAvailable && setupWallSegmentation)
        {
            bool proceed = EditorUtility.DisplayDialog("Предупреждение",
                "Unity Sentis не обнаружен в проекте, а он необходим для работы сегментации стен.\n\n" +
                "Выполните одно из следующих действий:\n" +
                "1. Установите пакет Unity Sentis из Package Manager\n" +
                "2. Отключите компонент сегментации стен в настройках\n\n" +
                "Продолжить создание сцены без сегментации стен?",
                "Продолжить без сегментации", "Отмена");

            if (!proceed)
            {
                Debug.LogWarning("Создание сцены отменено. Установите Unity Sentis или отключите сегментацию стен.");
                return;
            }

            setupWallSegmentation = false;
            setupWallPainting = false;
        }

        // Создаем новую сцену
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            // Используем EmptyScene вместо DefaultGameObjects, чтобы избежать создания лишних объектов
            EditorSceneManager.NewScene(NewSceneSetup.EmptyScene);

            // Удаляем дефолтные объекты, если они есть (для надежности)
            CleanupDefaultObjects();

            // Создаем директории заранее
            string resourcesDirectory = "Assets/Resources/Materials";
            if (!Directory.Exists(resourcesDirectory))
            {
                Directory.CreateDirectory(resourcesDirectory);
            }

            // Директория для сцены
            string scenesDirectory = "Assets/Scenes";
            if (!Directory.Exists(scenesDirectory))
            {
                Directory.CreateDirectory(scenesDirectory);
            }

            // Сначала создаем необходимые ресурсы для шейдеров и материалов
            if (setupWallPainting)
            {
                CreateWallPaintResources();
            }

            // Создаем корневые объекты
            GameObject arRoot = new GameObject("[AR]");
            GameObject uiRoot = new GameObject("[UI]");
            GameObject managersRoot = new GameObject("[Managers]");

            // Настраиваем AR
            if (setupAR)
            {
                SetupARComponents(arRoot);

                // Проверяем и исправляем ссылки XROrigin после настройки AR компонентов
                GameObject xrOriginObj = GameObject.Find("XR Origin");
                if (xrOriginObj != null)
                {
                    FixXROriginReferences(xrOriginObj);
                }
            }

            // Настраиваем UI
            if (setupUI)
            {
                SetupUI(uiRoot);

                // Добавляем UIInitializer
                SetupUIInitializer(uiRoot);
            }

            // Настраиваем Event System
            if (setupEventSystem)
            {
                SetupEventSystem();
            }

            // Настраиваем сегментацию стен
            if (setupWallSegmentation)
            {
                SetupWallSegmentation(managersRoot);
            }

            // Настраиваем освещение
            if (setupLighting)
            {
                SetupLighting();
            }

            // Настраиваем URP Renderer Feature для эффекта перекраски, если нужно
            if (setupWallPainting)
            {
                SetupURPRendererFeature();
            }

            // Добавляем ARManagerInitializer в менеджеры
            SetupARManagerInitializer(managersRoot);

            // Проверяем и исправляем все компоненты AR
            ValidateAndFixARComponents();

            // Проверяем и автоматически ищем модель для WallSegmentation если она не назначена
            if (setupWallSegmentation)
            {
                AutoAssignWallSegmentationModel();
            }

            // Сохраняем сцену
            string scenePath = "Assets/Scenes/" + sceneName + ".unity";

            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), scenePath);
            Debug.Log("Сцена создана и сохранена: " + scenePath);

            // Отображаем информацию о настройке сцены
            ShowSetupSummary();
        }
    }

    // Метод для удаления дефолтных объектов Unity после создания сцены
    private void CleanupDefaultObjects()
    {
        // Ищем и удаляем дефолтную камеру и источник света
        Camera defaultCamera = GameObject.FindObjectOfType<Camera>();
        if (defaultCamera != null && defaultCamera.gameObject.name == "Main Camera")
        {
            Debug.Log("Удаляем дефолтную камеру Unity");
            GameObject.DestroyImmediate(defaultCamera.gameObject);
        }

        Light defaultLight = GameObject.FindObjectOfType<Light>();
        if (defaultLight != null && defaultLight.gameObject.name == "Directional Light")
        {
            Debug.Log("Удаляем дефолтный источник света Unity");
            GameObject.DestroyImmediate(defaultLight.gameObject);
        }

        // Также можно удалить все объекты верхнего уровня для полной очистки
        // Но это может быть рискованно, если в сцене уже есть важные объекты
        // Поэтому этот код закомментирован
        /*
        GameObject[] rootObjects = UnityEngine.SceneManagement.SceneManager.GetActiveScene()
            .GetRootGameObjects();
        
        foreach (GameObject obj in rootObjects)
        {
            GameObject.DestroyImmediate(obj);
        }
        */
    }

    private void SetupARComponents(GameObject parent)
    {
        // Объявляем переменные в начале метода
        GameObject arSessionObj;
        GameObject xrOriginObj;

        if (sceneConfig != null && sceneConfig.arSessionPrefab != null && sceneConfig.xrOriginPrefab != null)
        {
            // Используем префабы из конфигурации для AR компонентов
            arSessionObj = PrefabUtility.InstantiatePrefab(sceneConfig.arSessionPrefab, parent.transform) as GameObject;
            xrOriginObj = PrefabUtility.InstantiatePrefab(sceneConfig.xrOriginPrefab, parent.transform) as GameObject;

            // Проверяем и исправляем ссылки в XROrigin
            FixXROriginReferences(xrOriginObj);

            // Явная инициализация AR сессии
            var sessionComponent = arSessionObj.GetComponent<ARSession>();
            if (sessionComponent != null)
            {
                sessionComponent.enabled = true;
                Debug.Log("AR сессия включена явно.");
            }

            Debug.Log("AR компоненты настроены из префабов.");

            return;
        }

        // Иначе создаем компоненты программно
        // Создаем AR Session
        arSessionObj = new GameObject("AR Session");
        arSessionObj.transform.SetParent(parent.transform);
        ARSession arSessionComponent = arSessionObj.AddComponent<ARSession>();
        arSessionComponent.enabled = true; // Явно включаем
        arSessionObj.AddComponent<ARInputManager>();

        // Создаем XR Origin
        xrOriginObj = new GameObject("XR Origin");
        xrOriginObj.transform.SetParent(parent.transform);

        // Добавляем XROrigin компонент
        XROrigin xrOrigin = xrOriginObj.AddComponent<XROrigin>();

        // Создаем Camera Offset (это важный объект для XR Origin)
        GameObject cameraOffsetObj = new GameObject("Camera Offset");
        cameraOffsetObj.transform.SetParent(xrOriginObj.transform);
        cameraOffsetObj.transform.localPosition = Vector3.zero;

        // Создаем и настраиваем AR камеру
        GameObject arCameraObj = new GameObject("AR Camera");
        arCameraObj.transform.SetParent(cameraOffsetObj.transform);
        arCameraObj.transform.localPosition = Vector3.zero;
        arCameraObj.transform.localRotation = Quaternion.identity;

        // Добавляем Camera
        Camera arCamera = arCameraObj.AddComponent<Camera>();
        arCamera.clearFlags = CameraClearFlags.SolidColor;
        arCamera.backgroundColor = Color.black;
        arCamera.nearClipPlane = 0.1f;
        arCamera.farClipPlane = 20f;
        arCamera.tag = "MainCamera";

        // Проверяем наличие AudioListener в сцене
        if (FindObjectsOfType<AudioListener>().Length == 0)
        {
            arCameraObj.AddComponent<AudioListener>();
        }

        // Добавляем основные AR компоненты для камеры
        ARCameraManager cameraManager = arCameraObj.AddComponent<ARCameraManager>();
        cameraManager.enabled = true; // Явно включаем

        ARCameraBackground cameraBackground = arCameraObj.AddComponent<ARCameraBackground>();
        cameraBackground.useCustomMaterial = false;
        cameraBackground.enabled = true; // Явно включаем

        // Добавляем TrackedPoseDriver с настройками для входной системы
        var trackedPoseDriver = arCameraObj.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
        trackedPoseDriver.enabled = true;

        // Настраиваем ссылки на XROrigin (это важно!)
        xrOrigin.Camera = arCamera;
        xrOrigin.CameraFloorOffsetObject = cameraOffsetObj;

        // Создаем трекеры для AR функций
        GameObject trackersObj = new GameObject("AR Trackers");
        trackersObj.transform.SetParent(xrOriginObj.transform);

        // Добавляем AR Raycast Manager
        var raycastManager = trackersObj.AddComponent<ARRaycastManager>();
        raycastManager.enabled = true; // Явно включаем

        // Добавляем AR Plane Manager
        var planeManager = trackersObj.AddComponent<ARPlaneManager>();
        planeManager.enabled = true; // Явно включаем

        // Пытаемся использовать префаб плоскости из конфигурации
        if (sceneConfig != null && sceneConfig.arPlanePrefab != null)
        {
            planeManager.planePrefab = sceneConfig.arPlanePrefab;
        }
        else
        {
            // Создаем префаб для визуализации плоскостей
            GameObject planePrefab = new GameObject("AR Plane Visualization");
            planePrefab.AddComponent<ARPlane>();
            planePrefab.AddComponent<MeshFilter>();
            planePrefab.AddComponent<MeshRenderer>();
            planePrefab.AddComponent<ARPlaneMeshVisualizer>();
            planePrefab.AddComponent<LineRenderer>();

            // Сохраняем префаб
            string prefabPath = "Assets/Prefabs/AR/ARPlaneVisualization.prefab";
            string prefabDirectory = Path.GetDirectoryName(prefabPath);
            if (!Directory.Exists(prefabDirectory))
            {
                Directory.CreateDirectory(prefabDirectory);
            }

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(planePrefab, prefabPath);
            DestroyImmediate(planePrefab);

            // Назначаем префаб
            planeManager.planePrefab = prefab;

            // Обновляем конфигурацию
            if (sceneConfig != null)
            {
                sceneConfig.arPlanePrefab = prefab;
                EditorUtility.SetDirty(sceneConfig);
                AssetDatabase.SaveAssets();
            }
        }

        // Добавляем дополнительные менеджеры
        var pointCloudManager = trackersObj.AddComponent<ARPointCloudManager>();
        pointCloudManager.enabled = true; // Явно включаем

        var anchorManager = trackersObj.AddComponent<ARAnchorManager>();
        anchorManager.enabled = true; // Явно включаем

        // Настройка ARSession для iOS
#if UNITY_IOS
        Debug.Log("Настройка ARKit сессии для iOS.");
        // Можно добавить специфичные для ARKit настройки здесь
#endif

        // Активируем все компоненты
        arSessionObj.SetActive(true);
        xrOriginObj.SetActive(true);

        // Сохраняем компоненты как префабы для дальнейшего использования
        if (sceneConfig != null && sceneConfig.arSessionPrefab == null)
        {
            string sessionPrefabPath = "Assets/Prefabs/AR/ARSession.prefab";
            string xrOriginPrefabPath = "Assets/Prefabs/AR/XROrigin.prefab";

            // Создаем директорию, если не существует
            string prefabDirectory = Path.GetDirectoryName(sessionPrefabPath);
            if (!Directory.Exists(prefabDirectory))
            {
                Directory.CreateDirectory(prefabDirectory);
            }

            // Сохраняем префабы
            sceneConfig.arSessionPrefab = PrefabUtility.SaveAsPrefabAsset(arSessionObj, sessionPrefabPath);
            sceneConfig.xrOriginPrefab = PrefabUtility.SaveAsPrefabAsset(xrOriginObj, xrOriginPrefabPath);

            // Сохраняем конфигурацию
            EditorUtility.SetDirty(sceneConfig);
            AssetDatabase.SaveAssets();
        }

        Debug.Log("AR компоненты настроены. Должен отображаться фид камеры.");
    }

    // Метод для добавления ARManagerInitializer
    private void AddARManagerInitializer(GameObject managersRoot)
    {
        Debug.LogWarning("Метод AddARManagerInitializer устарел и будет удален в будущих версиях. Используйте SetupARManagerInitializer.");
        // Делегируем вызов новому методу для обеспечения согласованности
        SetupARManagerInitializer(managersRoot);
    }

    // Вспомогательный метод для проверки и исправления ссылок в XROrigin
    private void FixXROriginReferences(GameObject xrOriginObj)
    {
        if (xrOriginObj == null) return;

        XROrigin xrOrigin = xrOriginObj.GetComponent<XROrigin>();
        if (xrOrigin == null) return;

        Debug.Log("Проверка и исправление ссылок в XROrigin...");

        // Проверка на неправильную настройку CameraFloorOffsetObject
        if (xrOrigin.CameraFloorOffsetObject != null &&
            xrOrigin.CameraFloorOffsetObject.name == "AR Trackers")
        {
            Debug.LogWarning("Обнаружена некорректная настройка: AR Trackers указан как CameraFloorOffsetObject. Исправляем...");

            // Ищем Camera Offset в иерархии
            Transform cameraOffsetTrans = xrOriginObj.transform.Find("Camera Offset");
            if (cameraOffsetTrans == null)
            {
                // Создаем новый Camera Offset
                GameObject offsetObj = new GameObject("Camera Offset");
                offsetObj.transform.SetParent(xrOriginObj.transform, false);
                offsetObj.transform.localPosition = Vector3.zero;
                cameraOffsetTrans = offsetObj.transform;
                Debug.Log("Создан новый Camera Offset");
            }

            // Устанавливаем правильную ссылку
            xrOrigin.CameraFloorOffsetObject = cameraOffsetTrans.gameObject;
            Debug.Log("Исправлена ссылка на CameraFloorOffsetObject с AR Trackers на Camera Offset");

            // Если камера существует, перемещаем её под Camera Offset
            if (xrOrigin.Camera != null && xrOrigin.Camera.transform.parent != cameraOffsetTrans)
            {
                xrOrigin.Camera.transform.SetParent(cameraOffsetTrans, false);
                xrOrigin.Camera.transform.localPosition = Vector3.zero;
                xrOrigin.Camera.transform.localRotation = Quaternion.identity;
                Debug.Log("Камера перемещена под правильный Camera Offset");
            }
        }

        // Проверяем и исправляем ссылку на Camera
        if (xrOrigin.Camera == null)
        {
            Debug.LogWarning("XROrigin.Camera не установлена. Исправляем...");

            // Ищем или создаем Camera Offset
            Transform cameraOffsetTrans = null;

            // Проверяем, есть ли уже ссылка на CameraFloorOffsetObject
            if (xrOrigin.CameraFloorOffsetObject != null)
            {
                cameraOffsetTrans = xrOrigin.CameraFloorOffsetObject.transform;
            }
            else
            {
                // Ищем Camera Offset в иерархии
                cameraOffsetTrans = xrOriginObj.transform.Find("Camera Offset");
                if (cameraOffsetTrans == null)
                {
                    // Создаем Camera Offset
                    GameObject offsetObj = new GameObject("Camera Offset");
                    offsetObj.transform.SetParent(xrOriginObj.transform, false);
                    offsetObj.transform.localPosition = Vector3.zero;
                    cameraOffsetTrans = offsetObj.transform;
                    Debug.Log("Создан новый Camera Offset");
                }

                // Устанавливаем ссылку на Camera Offset
                xrOrigin.CameraFloorOffsetObject = cameraOffsetTrans.gameObject;
                Debug.Log("Ссылка на Camera Offset в XROrigin была исправлена.");
            }

            // Ищем AR Camera в Camera Offset
            Transform cameraTrans = cameraOffsetTrans.Find("AR Camera");
            if (cameraTrans == null)
            {
                // Ищем любую камеру в иерархии XROrigin
                Camera anyCamera = xrOriginObj.GetComponentInChildren<Camera>();
                if (anyCamera != null)
                {
                    // Перемещаем найденную камеру под Camera Offset, если она не там
                    if (anyCamera.transform.parent != cameraOffsetTrans)
                    {
                        anyCamera.transform.SetParent(cameraOffsetTrans, false);
                        anyCamera.transform.localPosition = Vector3.zero;
                        anyCamera.transform.localRotation = Quaternion.identity;
                        Debug.Log("Найдена камера и перемещена под Camera Offset");
                    }

                    cameraTrans = anyCamera.transform;
                    xrOrigin.Camera = anyCamera;
                }
                else
                {
                    // Если камеры нет совсем, создаем новую
                    GameObject cameraObj = new GameObject("AR Camera");
                    cameraObj.transform.SetParent(cameraOffsetTrans, false);
                    cameraObj.transform.localPosition = Vector3.zero;
                    cameraObj.transform.localRotation = Quaternion.identity;

                    Camera cam = cameraObj.AddComponent<Camera>();
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = Color.black;
                    cam.nearClipPlane = 0.1f;
                    cam.farClipPlane = 20f;
                    cam.tag = "MainCamera";

                    cameraTrans = cameraObj.transform;
                    xrOrigin.Camera = cam;
                    Debug.Log("Создана новая AR камера в Camera Offset");
                }
            }
            else
            {
                Camera cam = cameraTrans.GetComponent<Camera>();
                if (cam != null)
                {
                    xrOrigin.Camera = cam;
                    Debug.Log("Ссылка на камеру в XROrigin была исправлена.");
                }
                else
                {
                    // Если объект AR Camera существует, но не имеет компонента Camera
                    cam = cameraTrans.gameObject.AddComponent<Camera>();
                    cam.clearFlags = CameraClearFlags.SolidColor;
                    cam.backgroundColor = Color.black;
                    cam.nearClipPlane = 0.1f;
                    cam.farClipPlane = 20f;
                    cam.tag = "MainCamera";

                    xrOrigin.Camera = cam;
                    Debug.Log("Добавлен компонент Camera к AR Camera");
                }
            }

            // Проверяем и настраиваем AR компоненты на камере
            if (cameraTrans != null)
            {
                GameObject cameraObj = cameraTrans.gameObject;

                // Проверяем и исправляем ARCameraManager
                ARCameraManager cameraManager = cameraTrans.GetComponent<ARCameraManager>();
                if (cameraManager == null)
                {
                    cameraManager = cameraObj.AddComponent<ARCameraManager>();
                    Debug.Log("ARCameraManager был добавлен на камеру.");
                }
                cameraManager.enabled = true;

                // Проверим наличие и настройки ARCameraBackground
                ARCameraBackground cameraBackground = cameraTrans.GetComponent<ARCameraBackground>();
                if (cameraBackground == null)
                {
                    cameraBackground = cameraObj.AddComponent<ARCameraBackground>();
                    Debug.Log("ARCameraBackground был добавлен на камеру.");
                }
                cameraBackground.useCustomMaterial = false;
                cameraBackground.enabled = true;

                // Проверяем наличие TrackedPoseDriver
                var trackedPoseDriver = cameraTrans.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
                if (trackedPoseDriver == null)
                {
                    trackedPoseDriver = cameraObj.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
                    trackedPoseDriver.enabled = true;
                    Debug.Log("TrackedPoseDriver добавлен к камере.");
                }
            }
        }
        else
        {
            // Если камера существует, проверяем правильное родительское представление
            Camera arCamera = xrOrigin.Camera;
            GameObject arCameraObj = arCamera.gameObject;

            // Проверяем наличие Camera Offset
            if (xrOrigin.CameraFloorOffsetObject == null)
            {
                Transform parent = arCameraObj.transform.parent;
                if (parent != null && parent.name == "Camera Offset")
                {
                    // Если родитель камеры - Camera Offset, используем его
                    xrOrigin.CameraFloorOffsetObject = parent.gameObject;
                    Debug.Log("Найден и установлен Camera Offset на основе иерархии камеры");
                }
                else
                {
                    // Создаем новый Camera Offset и перемещаем камеру под него
                    GameObject offsetObj = new GameObject("Camera Offset");
                    offsetObj.transform.SetParent(xrOriginObj.transform, false);
                    offsetObj.transform.localPosition = Vector3.zero;

                    // Перемещаем камеру под Camera Offset
                    Vector3 worldPos = arCameraObj.transform.position;
                    Quaternion worldRot = arCameraObj.transform.rotation;

                    arCameraObj.transform.SetParent(offsetObj.transform, false);

                    // Восстанавливаем мировые координаты камеры
                    arCameraObj.transform.position = worldPos;
                    arCameraObj.transform.rotation = worldRot;

                    xrOrigin.CameraFloorOffsetObject = offsetObj;
                    Debug.Log("Создан новый Camera Offset и камера перемещена под него");
                }
            }
            // Проверяем, не является ли CameraFloorOffsetObject объектом AR Trackers
            else if (xrOrigin.CameraFloorOffsetObject.name == "AR Trackers")
            {
                Debug.LogWarning("CameraFloorOffsetObject был неправильно установлен на AR Trackers. Исправляем...");

                // Ищем Camera Offset
                Transform cameraOffsetTrans = xrOriginObj.transform.Find("Camera Offset");
                if (cameraOffsetTrans == null)
                {
                    // Создаем новый Camera Offset
                    GameObject offsetObj = new GameObject("Camera Offset");
                    offsetObj.transform.SetParent(xrOriginObj.transform, false);
                    offsetObj.transform.localPosition = Vector3.zero;
                    cameraOffsetTrans = offsetObj.transform;
                }

                // Перемещаем камеру под Camera Offset
                arCameraObj.transform.SetParent(cameraOffsetTrans, false);
                arCameraObj.transform.localPosition = Vector3.zero;
                arCameraObj.transform.localRotation = Quaternion.identity;

                // Устанавливаем правильную ссылку
                xrOrigin.CameraFloorOffsetObject = cameraOffsetTrans.gameObject;
                Debug.Log("Исправлена ссылка на CameraFloorOffsetObject с AR Trackers на Camera Offset");
            }
            else if (arCameraObj.transform.parent != xrOrigin.CameraFloorOffsetObject.transform)
            {
                Debug.LogWarning("Камера находится не в CameraFloorOffsetObject. Перемещаем...");

                // Перемещаем камеру под Camera Offset
                arCameraObj.transform.SetParent(xrOrigin.CameraFloorOffsetObject.transform, false);
                arCameraObj.transform.localPosition = Vector3.zero;
                arCameraObj.transform.localRotation = Quaternion.identity;
                Debug.Log("Камера перемещена в правильный Camera Offset");
            }

            // Проверяем компоненты AR на камере
            ARCameraManager cameraManager = arCameraObj.GetComponent<ARCameraManager>();
            if (cameraManager == null)
            {
                cameraManager = arCameraObj.AddComponent<ARCameraManager>();
                cameraManager.enabled = true;
                Debug.Log("ARCameraManager добавлен на камеру");
            }

            ARCameraBackground cameraBackground = arCameraObj.GetComponent<ARCameraBackground>();
            if (cameraBackground == null)
            {
                cameraBackground = arCameraObj.AddComponent<ARCameraBackground>();
                cameraBackground.useCustomMaterial = false;
                cameraBackground.enabled = true;
                Debug.Log("ARCameraBackground добавлен на камеру");
            }

            // Проверяем TrackedPoseDriver
            var trackedPoseDriver = arCameraObj.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
            if (trackedPoseDriver == null)
            {
                trackedPoseDriver = arCameraObj.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
                trackedPoseDriver.enabled = true;
                Debug.Log("TrackedPoseDriver добавлен на камеру");
            }
        }

        // Финальная проверка
        if (xrOrigin.Camera == null)
        {
            Debug.LogError("После всех исправлений XROrigin.Camera все еще null!");
        }
        else
        {
            Debug.Log($"XROrigin настроен успешно: Camera = {xrOrigin.Camera.name}, CameraFloorOffsetObject = {(xrOrigin.CameraFloorOffsetObject != null ? xrOrigin.CameraFloorOffsetObject.name : "null")}");
        }

        // Применяем изменения
        EditorUtility.SetDirty(xrOrigin);
    }

    // Метод для настройки URP Renderer Feature
    private void SetupURPRendererFeature()
    {
        // Ищем все URP активы в проекте
        string[] guids = AssetDatabase.FindAssets("t:UniversalRenderPipelineAsset");
        if (guids.Length == 0)
        {
            Debug.LogWarning("Не найдены URP активы в проекте. Убедитесь, что проект использует URP.");
            return;
        }

        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            UniversalRenderPipelineAsset urpAsset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);

            if (urpAsset != null)
            {
                // Получаем renderer data через рефлексию
                ScriptableRendererData rendererData = GetRendererDataFromURP(urpAsset);

                if (rendererData != null)
                {
                    // Проверяем наличие WallPaintFeature
                    bool hasWallPaintFeature = false;
                    foreach (var feature in rendererData.rendererFeatures)
                    {
                        if (feature.GetType().Name == "WallPaintFeature")
                        {
                            hasWallPaintFeature = true;
                            break;
                        }
                    }

                    if (!hasWallPaintFeature)
                    {
                        // Ищем тип WallPaintFeature
                        System.Type wallPaintFeatureType = FindTypeInAllAssemblies("WallPaintFeature");
                        if (wallPaintFeatureType != null && typeof(ScriptableRendererFeature).IsAssignableFrom(wallPaintFeatureType))
                        {
                            // Создаем новый экземпляр и добавляем его
                            ScriptableRendererFeature feature = ScriptableObject.CreateInstance(wallPaintFeatureType) as ScriptableRendererFeature;
                            if (feature != null)
                            {
                                // Добавляем через рефлексию, так как rendererFeatures только для чтения
                                AddRendererFeatureToRendererData(rendererData, feature);

                                // Сохраняем изменения
                                EditorUtility.SetDirty(rendererData);
                                AssetDatabase.SaveAssets();

                                Debug.Log("WallPaintFeature добавлен в URP Renderer: " + path);
                                return;
                            }
                        }
                        else
                        {
                            Debug.LogWarning("WallPaintFeature не найден в проекте. Добавьте его вручную.");
                        }
                    }
                    else
                    {
                        Debug.Log("WallPaintFeature уже добавлен в URP Renderer: " + path);
                        return;
                    }
                }
            }
        }
    }

    // Вспомогательный метод для получения ScriptableRendererData из URP Asset
    private ScriptableRendererData GetRendererDataFromURP(UniversalRenderPipelineAsset urpAsset)
    {
        // Используем рефлексию для доступа к приватному полю m_RendererDataList
        FieldInfo renderersField = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", BindingFlags.NonPublic | BindingFlags.Instance);
        if (renderersField != null)
        {
            var renderers = renderersField.GetValue(urpAsset) as ScriptableRendererData[];
            if (renderers != null && renderers.Length > 0)
            {
                return renderers[0]; // Берем первый рендерер
            }
        }

        return null;
    }

    // Вспомогательный метод для добавления feature в renderer data
    private void AddRendererFeatureToRendererData(ScriptableRendererData rendererData, ScriptableRendererFeature feature)
    {
        // Получаем приватное поле m_RendererFeatures
        FieldInfo field = typeof(ScriptableRendererData).GetField("m_RendererFeatures", BindingFlags.NonPublic | BindingFlags.Instance);
        if (field != null)
        {
            var features = field.GetValue(rendererData) as System.Collections.Generic.List<ScriptableRendererFeature>;
            if (features != null)
            {
                // Добавляем feature и его имя
                features.Add(feature);
                AssetDatabase.AddObjectToAsset(feature, rendererData);

                // Называем feature
                feature.name = "WallPaintFeature";
            }
        }
    }

    private void SetupUI(GameObject parent)
    {
        // Пробуем использовать префаб UI из конфигурации
        if (sceneConfig != null && sceneConfig.uiPrefab != null)
        {
            GameObject uiInstance = PrefabUtility.InstantiatePrefab(sceneConfig.uiPrefab, parent.transform) as GameObject;
            Debug.Log("UI компоненты настроены из префаба.");
            return;
        }

        // Создаем Canvas
        GameObject canvas = new GameObject("Canvas");
        canvas.transform.SetParent(parent.transform);
        Canvas canvasComponent = canvas.AddComponent<Canvas>();
        canvasComponent.renderMode = RenderMode.ScreenSpaceOverlay;

        // Добавляем Scaler для поддержки разных разрешений
        CanvasScaler scaler = canvas.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920, 1080);
        scaler.matchWidthOrHeight = 0.5f;

        // Добавляем GraphicRaycaster для обработки нажатий
        canvas.AddComponent<GraphicRaycaster>();

        // Добавляем ColorPickerCanvas компонент
        ColorPickerCanvas colorPickerCanvas = canvas.AddComponent<ColorPickerCanvas>();

        // Создаем кнопку для переключения палитры
        GameObject toggleButton = new GameObject("ToggleButton");
        toggleButton.transform.SetParent(canvas.transform);
        RectTransform toggleRectTransform = toggleButton.AddComponent<RectTransform>();
        toggleRectTransform.anchorMin = new Vector2(1, 0);
        toggleRectTransform.anchorMax = new Vector2(1, 0);
        toggleRectTransform.pivot = new Vector2(1, 0);
        toggleRectTransform.anchoredPosition = new Vector2(-20, 20);
        toggleRectTransform.sizeDelta = new Vector2(150, 60);

        // Добавляем компоненты для кнопки
        Image toggleImage = toggleButton.AddComponent<Image>();
        toggleImage.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);
        Button toggleButtonComponent = toggleButton.AddComponent<Button>();

        // Создаем текст для кнопки
        GameObject toggleText = new GameObject("Text");
        toggleText.transform.SetParent(toggleButton.transform);
        RectTransform textRectTransform = toggleText.AddComponent<RectTransform>();
        textRectTransform.anchorMin = Vector2.zero;
        textRectTransform.anchorMax = Vector2.one;
        textRectTransform.offsetMin = Vector2.zero;
        textRectTransform.offsetMax = Vector2.zero;

        UnityEngine.UI.Text text = toggleText.AddComponent<UnityEngine.UI.Text>();
        text.text = "Выбрать цвет";
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = 20;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;

        // Создаем панель выбора цвета
        GameObject colorPickerPanel = new GameObject("ColorPickerPanel");
        colorPickerPanel.transform.SetParent(canvas.transform);
        RectTransform panelRectTransform = colorPickerPanel.AddComponent<RectTransform>();
        panelRectTransform.anchorMin = new Vector2(0.5f, 0);
        panelRectTransform.anchorMax = new Vector2(0.5f, 0);
        panelRectTransform.pivot = new Vector2(0.5f, 0);
        panelRectTransform.anchoredPosition = new Vector2(0, 100);
        panelRectTransform.sizeDelta = new Vector2(600, 300);

        // Добавляем компоненты для панели
        Image panelImage = colorPickerPanel.AddComponent<Image>();
        panelImage.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);

        // Добавляем ColorPickerUI компонент
        ColorPickerUI colorPickerUI = colorPickerPanel.AddComponent<ColorPickerUI>();

        // Настраиваем ColorPickerCanvas с использованием рефлексии
        // поскольку у нас нет прямого доступа к полям из-за сериализации
        SetPrivateField(colorPickerCanvas, "colorPickerPanel", colorPickerPanel);
        SetPrivateField(colorPickerCanvas, "toggleButton", toggleButtonComponent);
        SetPrivateField(colorPickerCanvas, "colorPickerTransform", panelRectTransform);

        // По умолчанию скрываем панель выбора цвета
        colorPickerPanel.SetActive(false);

        // Сохраняем Canvas как префаб, если его еще нет в конфигурации
        if (sceneConfig != null && sceneConfig.uiPrefab == null)
        {
            string uiPrefabPath = "Assets/Prefabs/UI/MainCanvas.prefab";

            // Создаем директорию, если не существует
            string prefabDirectory = Path.GetDirectoryName(uiPrefabPath);
            if (!Directory.Exists(prefabDirectory))
            {
                Directory.CreateDirectory(prefabDirectory);
            }

            // Сохраняем префаб
            sceneConfig.uiPrefab = PrefabUtility.SaveAsPrefabAsset(canvas, uiPrefabPath);

            // Сохраняем конфигурацию
            EditorUtility.SetDirty(sceneConfig);
            AssetDatabase.SaveAssets();
        }

        Debug.Log("UI компоненты настроены");
    }

    private void SetupEventSystem()
    {
        // Проверяем, существует ли уже EventSystem
        if (FindObjectOfType<EventSystem>() == null)
        {
            GameObject eventSystem = new GameObject("EventSystem");
            eventSystem.AddComponent<EventSystem>();

            // Используем InputSystemUIInputModule вместо StandaloneInputModule для совместимости с Input System Package
            eventSystem.AddComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>();

            Debug.Log("EventSystem добавлен с поддержкой Input System Package");
        }
        else
        {
            Debug.Log("EventSystem уже существует в сцене");
        }
    }

    // Вспомогательный метод для поиска типа по имени в любой сборке
    private System.Type FindTypeInAllAssemblies(string typeName)
    {
        // Проверяем во всех загруженных сборках
        foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
        {
            // Ищем тип с указанным именем
            System.Type foundType = assembly.GetType(typeName);
            if (foundType != null)
                return foundType;

            // Если не нашли, ищем тип с полным именем, добавляя пространство имен
            foundType = assembly.GetType("Remalux." + typeName);
            if (foundType != null)
                return foundType;

            // Ищем среди всех типов в сборке (для случая, если пространство имен другое)
            foreach (System.Type type in assembly.GetTypes())
            {
                if (type.Name == typeName)
                    return type;
            }
        }

        return null;
    }

    private void SetupWallSegmentation(GameObject parent)
    {
        // Проверяем наличие типа WallSegmentation
        System.Type wallSegmentationType = FindTypeInAllAssemblies("WallSegmentation");
        if (wallSegmentationType == null)
        {
            Debug.LogError("Тип WallSegmentation не найден. Убедитесь, что скрипт WallSegmentation.cs существует в проекте.");
            return;
        }

        // Проверяем наличие типа WallPaintEffect
        System.Type wallPaintEffectType = FindTypeInAllAssemblies("WallPaintEffect");
        if (wallPaintEffectType == null && setupWallPainting)
        {
            Debug.LogError("Тип WallPaintEffect не найден. Убедитесь, что скрипт WallPaintEffect.cs существует в проекте.");
            setupWallPainting = false;
        }

        // Находим AR камеру с компонентом ARCameraManager
        ARCameraManager cameraManager = null;
        Camera arCamera = null;

        // Находим все компоненты ARCameraManager в сцене
        ARCameraManager[] cameraManagers = FindObjectsOfType<ARCameraManager>();
        if (cameraManagers.Length > 0)
        {
            cameraManager = cameraManagers[0];
            arCamera = cameraManager.GetComponent<Camera>();
        }
        else
        {
            Debug.LogError("ARCameraManager не найден в сцене. Убедитесь, что AR компоненты настроены.");
            return;
        }

        // Удаляем существующие объекты, чтобы избежать дублирования
        var existingSegmentations = FindObjectsOfType(wallSegmentationType);
        foreach (var seg in existingSegmentations)
        {
            Debug.Log($"Уже существует объект WallSegmentation: {((Component)seg).gameObject.name}. Используем его.");
            DestroyImmediate(((Component)seg).gameObject);
        }

        // Создаем объект Wall Segmentation
        GameObject wallSegmentationObj = new GameObject("Wall Segmentation");
        wallSegmentationObj.transform.SetParent(parent.transform);

        // Добавляем компонент WallSegmentation
        Component segmentation = wallSegmentationObj.AddComponent(wallSegmentationType);

        // Добавляем WallSegmentationModelLoader для безопасной загрузки моделей
        wallSegmentationObj.AddComponent<WallSegmentationModelLoader>();
        Debug.Log("Добавлен WallSegmentationModelLoader для предотвращения крашей при загрузке моделей");

        // Используем конфигурацию для настройки разрешения текстуры
        Vector2Int resolution = (sceneConfig != null) ? sceneConfig.segmentationMaskResolution : new Vector2Int(256, 256);

        // Создаем текстуру для маски сегментации 
        RenderTexture maskTexture = new RenderTexture(resolution.x, resolution.y, 0, RenderTextureFormat.RFloat);
        maskTexture.enableRandomWrite = true;
        maskTexture.Create();

        // Назначаем маску в компонент WallSegmentation через рефлексию
        SetPrivateField(segmentation, "segmentationMaskTexture", maskTexture);

        // Назначаем ARCameraManager
        SetPrivateField(segmentation, "arCameraManager", cameraManager);

        // Ищем ARSessionManager
        System.Type arSessionManagerType = FindTypeInAllAssemblies("ARSessionManager");
        var sessionManager = FindObjectOfType(arSessionManagerType);
        if (sessionManager != null)
        {
            SetPrivateField(segmentation, "arSessionManager", sessionManager);
        }

        // Если у нас есть модель в конфигурации, назначаем её
        if (sceneConfig != null && sceneConfig.wallSegmentationModel != null)
        {
            try
            {
                SetPrivateField(segmentation, "modelAsset", sceneConfig.wallSegmentationModel);
                Debug.Log("Модель из конфигурации назначена в WallSegmentation");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Ошибка при назначении модели из конфигурации: {e.Message}");
            }
        }

        // Выдаем подробные инструкции пользователю только если модель не назначена
        if (sceneConfig == null || sceneConfig.wallSegmentationModel == null)
        {
            Debug.LogWarning(
                "ВАЖНО: необходимо настроить модель сегментации стен!\n" +
                "1. Найдите объект [Managers]/Wall Segmentation в сцене\n" +
                "2. Назначьте вашу ONNX модель в поле Model Asset\n" +
                "3. Если у вас нет модели, попробуйте использовать тестовую модель из директории Assets/Models/model.onnx\n" +
                "4. Если Unity зависает при назначении модели, используйте компонент WallSegmentationModelLoader"
            );
        }

        Debug.Log("Компонент сегментации стен настроен");

        // Создаем эффект перекраски стен, если включено
        if (setupWallPainting)
        {
            // Удаляем существующие объекты WallPaintEffect
            var existingEffects = FindObjectsOfType(wallPaintEffectType);
            foreach (var effect in existingEffects)
            {
                Debug.Log($"Уже существует объект WallPaintEffect: {((Component)effect).gameObject.name}. Используем его.");
                DestroyImmediate(((Component)effect).gameObject);
            }

            GameObject wallPaintEffectObj = new GameObject("Wall Paint Effect");
            wallPaintEffectObj.transform.SetParent(parent.transform);

            // Добавляем компонент WallPaintEffect
            Component paintEffect = wallPaintEffectObj.AddComponent(wallPaintEffectType);

            // Назначаем ссылку на WallSegmentation
            SetPrivateField(paintEffect, "wallSegmentation", segmentation);

            // Назначаем ссылку на ARSessionManager
            if (sessionManager != null)
            {
                SetPrivateField(paintEffect, "arSessionManager", sessionManager);
            }

            // Используем материал из конфигурации, если он есть
            if (sceneConfig != null && sceneConfig.wallPaintMaterial != null)
            {
                SetPrivateField(paintEffect, "wallPaintMaterial", sceneConfig.wallPaintMaterial);
            }
            else
            {
                // Ищем материал в проекте
                Material existingMaterial = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/WallPaint.mat");
                if (existingMaterial != null)
                {
                    SetPrivateField(paintEffect, "wallPaintMaterial", existingMaterial);
                    Debug.Log("Найден и использован существующий материал WallPaint.mat");

                    // Обновляем конфигурацию
                    if (sceneConfig != null)
                    {
                        sceneConfig.wallPaintMaterial = existingMaterial;
                        EditorUtility.SetDirty(sceneConfig);
                        AssetDatabase.SaveAssets();
                    }
                }
                else
                {
                    // Создаем материал для шейдера перекраски
                    Shader wallPaintShader = Shader.Find("Custom/WallPaint");
                    if (wallPaintShader != null)
                    {
                        Material wallPaintMaterial = new Material(wallPaintShader);

                        // Получаем цвет и коэффициент смешивания из конфигурации или используем значения по умолчанию
                        Color paintColor = (sceneConfig != null) ? sceneConfig.defaultPaintColor : Color.red;
                        float blendFactor = (sceneConfig != null) ? sceneConfig.defaultBlendFactor : 0.7f;

                        // Настраиваем материал
                        wallPaintMaterial.SetColor("_PaintColor", paintColor);
                        wallPaintMaterial.SetFloat("_BlendFactor", blendFactor);

                        // Назначаем материал
                        SetPrivateField(paintEffect, "wallPaintMaterial", wallPaintMaterial);

                        // Сохраняем материал в проект
                        string materialPath = "Assets/Materials/WallPaint.mat";
                        string materialDirectory = Path.GetDirectoryName(materialPath);
                        if (!Directory.Exists(materialDirectory))
                        {
                            Directory.CreateDirectory(materialDirectory);
                        }

                        Material savedMaterial = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
                        if (savedMaterial == null)
                        {
                            AssetDatabase.CreateAsset(wallPaintMaterial, materialPath);
                            savedMaterial = wallPaintMaterial;
                        }
                        AssetDatabase.SaveAssets();

                        // Обновляем конфигурацию
                        if (sceneConfig != null)
                        {
                            sceneConfig.wallPaintMaterial = savedMaterial;
                            EditorUtility.SetDirty(sceneConfig);
                            AssetDatabase.SaveAssets();
                        }

                        // Выводим инструкцию по настройке URP
                        Debug.LogWarning(
                            "ВАЖНО: требуется настройка URP Renderer!\n" +
                            "1. Откройте Project Settings > Graphics\n" +
                            "2. Выберите используемый URP Asset и нажмите Edit\n" +
                            "3. В Renderer Features нажмите Add и выберите WallPaintFeature\n" +
                            "4. Сохраните изменения"
                        );
                    }
                    else
                    {
                        Debug.LogError("Шейдер Custom/WallPaint не найден. Создайте шейдер для перекраски стен.");
                    }
                }
            }

            Debug.Log("Эффект перекраски стен настроен");
        }
    }

    private void SetupLighting()
    {
        // Проверяем наличие существующих источников света
        Light existingLight = FindObjectOfType<Light>();

        // Если нет существующего света, создаем новый
        if (existingLight == null)
        {
            GameObject lightObject = new GameObject("Directional Light");
            Light mainLight = lightObject.AddComponent<Light>();
            mainLight.type = LightType.Directional;
            mainLight.intensity = 1.0f;
            mainLight.color = Color.white;
            lightObject.transform.rotation = Quaternion.Euler(50, -30, 0);
        }

        Debug.Log("Освещение настроено");
    }

    // Вспомогательный метод для установки приватных полей через рефлексию
    private void SetPrivateField(object target, string fieldName, object value)
    {
        FieldInfo field = target.GetType().GetField(fieldName,
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);

        if (field != null)
        {
            field.SetValue(target, value);
        }
        else
        {
            Debug.LogWarning($"Поле {fieldName} не найдено в типе {target.GetType().Name}");
        }
    }

    private void CreateWallPaintResources()
    {
        // Проверяем наличие директории Resources/Materials
        string materialsDir = "Assets/Resources/Materials";
        if (!Directory.Exists(materialsDir))
        {
            Directory.CreateDirectory(materialsDir);
        }

        // Создаем шейдер
        string shaderPath = materialsDir + "/WallPaint.shader";

        // Создаем улучшенный шейдер с более надежной поддержкой iOS и явной проверкой текстуры
        string shaderContent = @"Shader ""Custom/WallPaint""
{
    Properties
    {
        _MainTex (""Texture"", 2D) = ""white"" {}
        _PaintColor (""Paint Color"", Color) = (1,0,0,1)
        _BlendFactor (""Blend Factor"", Range(0,1)) = 0.5
        _SegmentationMask (""Segmentation Mask"", 2D) = ""black"" {}
    }
    SubShader
    {
        Tags { ""RenderType""=""Opaque"" ""RenderPipeline"" = ""UniversalPipeline"" }
        LOD 100
        
        // Добавляем Cull Off для теста на iOS
        Cull Off
        ZWrite On
        ZTest LEqual

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ USE_MASK
            
            // Добавляем специфичные для платформы прагмы
            #pragma multi_compile_instancing
            #pragma prefer_hlslcc gles
            #pragma exclude_renderers d3d11_9x
            #pragma target 2.0

            #include ""Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl""

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float2 uv : TEXCOORD0;
                float4 positionHCS : SV_POSITION;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_SegmentationMask);
            SAMPLER(sampler_SegmentationMask);
            
            half4 _PaintColor;
            float _BlendFactor;
            float4 _MainTex_ST;

            Varyings vert(Attributes IN)
            {
                Varyings OUT;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);
                
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);
                
                // Получаем цвет исходного изображения
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                
                #ifdef USE_MASK
                // Проверяем доступность текстуры маски
                float mask = 0;
                
                // Используем маску сегментации для определения области покраски
                #if defined(SHADER_API_METAL) || defined(SHADER_API_VULKAN) || defined(SHADER_API_GLES3)
                    // Осторожное семплирование для мобильных платформ
                    mask = SAMPLE_TEXTURE2D(_SegmentationMask, sampler_SegmentationMask, IN.uv).r;
                #else
                    mask = SAMPLE_TEXTURE2D(_SegmentationMask, sampler_SegmentationMask, IN.uv).r;
                #endif
                
                // Применяем цвет только в области стен (если маска > 0.1)
                if (mask > 0.1)
                {
                    color = lerp(color, _PaintColor, _BlendFactor * mask);
                }
                #else
                // Без маски - просто смешиваем цвета
                color = lerp(color, _PaintColor, _BlendFactor);
                #endif
                
                return color;
            }
            ENDHLSL
        }
    }
    FallBack ""Hidden/Universal Render Pipeline/FallbackError""
}";

        File.WriteAllText(shaderPath, shaderContent);
        AssetDatabase.ImportAsset(shaderPath);

        // Получаем созданный шейдер
        Shader wallPaintShader = AssetDatabase.LoadAssetAtPath<Shader>(shaderPath);
        if (wallPaintShader == null)
        {
            Debug.LogError("Не удалось создать шейдер для перекраски стен!");
            return;
        }

        // Создаем материал на основе шейдера
        Material wallPaintMaterial = new Material(wallPaintShader);
        wallPaintMaterial.SetColor("_PaintColor", Color.red);
        wallPaintMaterial.SetFloat("_BlendFactor", 0.7f);

        // Сохраняем материал в проект
        string materialPath = materialsDir + "/WallPaint.mat";
        AssetDatabase.CreateAsset(wallPaintMaterial, materialPath);
        AssetDatabase.SaveAssets();

        // Добавляем информацию о необходимых настройках для iOS
        Debug.Log("Ресурсы для перекраски стен созданы успешно.");
        Debug.Log("ВАЖНО для iOS: убедитесь, что в Info.plist добавлены разрешения:");
        Debug.Log("NSCameraUsageDescription - Необходим доступ к камере для AR-функций");
        Debug.Log("NSLocationWhenInUseUsageDescription - Используется для AR-функций");
        Debug.Log("Также проверьте, что минимальная версия iOS установлена на 13.0 или выше в Player Settings.");
    }

    // Добавляем новый метод для создания и настройки ARManagerInitializer
    private void SetupARManagerInitializer(GameObject managersRoot)
    {
        // Проверяем, существует ли уже ARManagerInitializer
        System.Type arManagerInitializerType = FindTypeInAllAssemblies("ARManagerInitializer");
        if (arManagerInitializerType == null)
        {
            Debug.LogWarning("Тип ARManagerInitializer не найден. Убедитесь, что скрипт ARManagerInitializer.cs существует в проекте.");
            return;
        }

        // Используем стандартный FindObjectOfType для большей надежности
        var existingObject = GameObject.FindObjectOfType(arManagerInitializerType);
        if (existingObject != null)
        {
            Debug.Log("ARManagerInitializer уже существует в сцене");
            return;
        }

        // Создаем объект для ARManagerInitializer
        GameObject initializerObj = new GameObject("AR Manager Initializer");
        initializerObj.transform.SetParent(managersRoot.transform);
        initializerObj.AddComponent(arManagerInitializerType);

        EditorUtility.SetDirty(initializerObj);

        Debug.Log("Добавлен ARManagerInitializer для автоматической настройки AR компонентов в рантайме");
    }

    // Добавляем новый метод для проверки и исправления всех компонентов AR
    private void ValidateAndFixARComponents()
    {
        Debug.Log("Проверка и исправление компонентов AR...");

        // Находим все объекты XROrigin в сцене
        XROrigin[] xrOrigins = FindObjectsOfType<XROrigin>();

        // Проверяем на дублирование XROrigin
        if (xrOrigins.Length > 1)
        {
            Debug.LogWarning($"Найдено {xrOrigins.Length} экземпляров XROrigin. Проверяем и исправляем...");

            // Находим "правильный" XROrigin - тот, который находится на объекте "XR Origin"
            XROrigin mainXROrigin = null;
            foreach (var origin in xrOrigins)
            {
                if (origin.gameObject.name == "XR Origin")
                {
                    mainXROrigin = origin;
                    break;
                }
            }

            // Если не нашли по имени, берем первый
            if (mainXROrigin == null && xrOrigins.Length > 0)
            {
                mainXROrigin = xrOrigins[0];
            }

            // Удаляем лишние компоненты XROrigin, если основной найден
            if (mainXROrigin != null)
            {
                foreach (var origin in xrOrigins)
                {
                    if (origin != mainXROrigin)
                    {
                        Debug.LogWarning($"Удаление лишнего компонента XROrigin с объекта {origin.gameObject.name}");
                        DestroyImmediate(origin);
                    }
                }
            }
        }

        // Получаем актуальный XROrigin после исправлений
        XROrigin xrOrigin = FindObjectOfType<XROrigin>();
        if (xrOrigin == null)
        {
            Debug.LogError("XROrigin не найден после проверки. Создайте сцену заново с включенной опцией 'Настроить AR компоненты'.");
            return;
        }

        // Проверяем, не указан ли AR Trackers как CameraFloorOffsetObject
        if (xrOrigin.CameraFloorOffsetObject != null &&
            xrOrigin.CameraFloorOffsetObject.name == "AR Trackers")
        {
            Debug.LogWarning("Обнаружена некорректная настройка: AR Trackers указан как CameraFloorOffsetObject. Исправляем...");

            // Ищем Camera Offset в иерархии
            Transform cameraOffsetTrans = xrOrigin.transform.Find("Camera Offset");
            if (cameraOffsetTrans == null)
            {
                // Создаем Camera Offset
                GameObject offsetObj = new GameObject("Camera Offset");
                offsetObj.transform.SetParent(xrOrigin.transform, false);
                offsetObj.transform.localPosition = Vector3.zero;
                cameraOffsetTrans = offsetObj.transform;
            }

            // Если камера существует, но находится не в Camera Offset
            if (xrOrigin.Camera != null)
            {
                GameObject cameraObj = xrOrigin.Camera.gameObject;
                if (cameraObj.transform.parent != cameraOffsetTrans)
                {
                    Debug.Log("Перемещаем камеру в правильный Camera Offset");
                    cameraObj.transform.SetParent(cameraOffsetTrans, false);
                    cameraObj.transform.localPosition = Vector3.zero;
                    cameraObj.transform.localRotation = Quaternion.identity;
                }
            }

            // Устанавливаем правильную ссылку на Camera Offset
            xrOrigin.CameraFloorOffsetObject = cameraOffsetTrans.gameObject;
            Debug.Log("Исправлена ссылка на CameraFloorOffsetObject с AR Trackers на Camera Offset");
        }

        // Проверяем наличие камеры в XROrigin
        if (xrOrigin.Camera == null)
        {
            Debug.LogError("XROrigin не содержит ссылки на камеру. Исправляем...");

            // Находим Camera Offset
            Transform cameraOffsetTrans = null;
            if (xrOrigin.CameraFloorOffsetObject != null)
            {
                cameraOffsetTrans = xrOrigin.CameraFloorOffsetObject.transform;
            }
            else
            {
                // Ищем или создаем Camera Offset
                cameraOffsetTrans = xrOrigin.transform.Find("Camera Offset");
                if (cameraOffsetTrans == null)
                {
                    GameObject offsetObj = new GameObject("Camera Offset");
                    offsetObj.transform.SetParent(xrOrigin.transform, false);
                    offsetObj.transform.localPosition = Vector3.zero;
                    cameraOffsetTrans = offsetObj.transform;
                    // Исправляем проблему - проверяем, не установлен ли уже CameraFloorOffsetObject
                    if (xrOrigin.CameraFloorOffsetObject == null)
                    {
                        xrOrigin.CameraFloorOffsetObject = offsetObj;
                        Debug.Log("Создан новый Camera Offset для XROrigin");
                    }
                    else
                    {
                        Debug.LogWarning("Обнаружен объект Camera Offset, несовпадающий с CameraFloorOffsetObject. Используем существующий CameraFloorOffsetObject.");
                        DestroyImmediate(offsetObj);
                        cameraOffsetTrans = xrOrigin.CameraFloorOffsetObject.transform;
                    }
                }
            }

            // Ищем камеру под Camera Offset
            Camera childCamera = cameraOffsetTrans.GetComponentInChildren<Camera>();
            if (childCamera != null)
            {
                xrOrigin.Camera = childCamera;
                Debug.Log("Найдена и установлена камера в XROrigin");
            }
            else
            {
                // Ищем камеру в дочерних объектах XROrigin
                childCamera = xrOrigin.GetComponentInChildren<Camera>();
                if (childCamera != null)
                {
                    // Перемещаем камеру под Camera Offset
                    childCamera.transform.SetParent(cameraOffsetTrans, false);
                    childCamera.transform.localPosition = Vector3.zero;
                    childCamera.transform.localRotation = Quaternion.identity;
                    xrOrigin.Camera = childCamera;
                    Debug.Log("Камера перемещена под Camera Offset и установлена в XROrigin");
                }
                else
                {
                    // Создаем новую камеру
                    GameObject cameraObj = new GameObject("AR Camera");
                    cameraObj.transform.SetParent(cameraOffsetTrans, false);
                    cameraObj.transform.localPosition = Vector3.zero;
                    cameraObj.transform.localRotation = Quaternion.identity;

                    Camera camera = cameraObj.AddComponent<Camera>();
                    camera.clearFlags = CameraClearFlags.SolidColor;
                    camera.backgroundColor = Color.black;
                    camera.nearClipPlane = 0.1f;
                    camera.farClipPlane = 20f;
                    camera.tag = "MainCamera";

                    // Добавляем необходимые AR компоненты
                    ARCameraManager cameraManager = cameraObj.AddComponent<ARCameraManager>();
                    cameraManager.enabled = true;

                    ARCameraBackground cameraBackground = cameraObj.AddComponent<ARCameraBackground>();
                    cameraBackground.useCustomMaterial = false;
                    cameraBackground.enabled = true;

                    var trackedPoseDriver = cameraObj.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
                    trackedPoseDriver.enabled = true;

                    xrOrigin.Camera = camera;
                    Debug.Log("Создана новая AR Camera и установлена в XROrigin");
                }
            }
        }

        // Проверяем наличие необходимых компонентов на камере
        if (xrOrigin.Camera != null)
        {
            GameObject cameraObj = xrOrigin.Camera.gameObject;

            // Проверяем ARCameraManager
            ARCameraManager cameraManager = cameraObj.GetComponent<ARCameraManager>();
            if (cameraManager == null)
            {
                cameraManager = cameraObj.AddComponent<ARCameraManager>();
                cameraManager.enabled = true;
                Debug.Log("Добавлен ARCameraManager на камеру");
            }

            // Проверяем ARCameraBackground
            ARCameraBackground cameraBackground = cameraObj.GetComponent<ARCameraBackground>();
            if (cameraBackground == null)
            {
                cameraBackground = cameraObj.AddComponent<ARCameraBackground>();
                cameraBackground.useCustomMaterial = false;
                cameraBackground.enabled = true;
                Debug.Log("Добавлен ARCameraBackground на камеру");
            }

            // Проверяем TrackedPoseDriver
            var trackedPoseDriver = cameraObj.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
            if (trackedPoseDriver == null)
            {
                trackedPoseDriver = cameraObj.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
                trackedPoseDriver.enabled = true;
                Debug.Log("Добавлен TrackedPoseDriver на камеру");
            }
        }

        // Находим ARSessionManager
        System.Type arSessionManagerType = FindTypeInAllAssemblies("ARSessionManager");
        var sessionManager = FindObjectOfType(arSessionManagerType);

        // Исправляем ссылки в ARSessionManager
        if (sessionManager != null && xrOrigin != null)
        {
            Debug.Log("Обновление ссылок в ARSessionManager...");

            // Устанавливаем правильный XROrigin
            SetPrivateField(sessionManager, "xrOrigin", xrOrigin);
            Debug.Log($"Обновлена ссылка на XROrigin в ARSessionManager");

            // Проверяем и обновляем ссылку на камеру
            if (xrOrigin.Camera != null)
            {
                ARCameraManager cameraManager = xrOrigin.Camera.GetComponent<ARCameraManager>();
                ARCameraBackground cameraBackground = xrOrigin.Camera.GetComponent<ARCameraBackground>();

                if (cameraManager != null)
                {
                    SetPrivateField(sessionManager, "arCameraManager", cameraManager);
                    Debug.Log("Обновлена ссылка на ARCameraManager в ARSessionManager");
                }

                if (cameraBackground != null)
                {
                    SetPrivateField(sessionManager, "arCameraBackground", cameraBackground);
                    Debug.Log("Обновлена ссылка на ARCameraBackground в ARSessionManager");
                }
            }

            EditorUtility.SetDirty(sessionManager as UnityEngine.Object);
        }

        // Убеждаемся что ARSession существует и настроен
        ARSession arSession = FindObjectOfType<ARSession>();
        if (arSession == null)
        {
            GameObject arSessionObj = new GameObject("AR Session");
            arSession = arSessionObj.AddComponent<ARSession>();
            arSessionObj.AddComponent<ARInputManager>();
            Debug.Log("Создан новый AR Session объект, так как он отсутствовал в сцене");
        }

        // Проверяем и создаем компонент ARSessionManager, если он отсутствует
        if (sessionManager == null && arSessionManagerType != null)
        {
            Debug.Log("ARSessionManager не найден, создаем новый...");
            GameObject sessionObj = arSession.gameObject;
            sessionManager = sessionObj.AddComponent(arSessionManagerType);

            // Устанавливаем ссылки
            if (xrOrigin != null && xrOrigin.Camera != null)
            {
                SetPrivateField(sessionManager, "xrOrigin", xrOrigin);
                SetPrivateField(sessionManager, "arSession", arSession);

                ARCameraManager cameraManager = xrOrigin.Camera.GetComponent<ARCameraManager>();
                ARCameraBackground cameraBackground = xrOrigin.Camera.GetComponent<ARCameraBackground>();

                if (cameraManager != null)
                    SetPrivateField(sessionManager, "arCameraManager", cameraManager);

                if (cameraBackground != null)
                    SetPrivateField(sessionManager, "arCameraBackground", cameraBackground);

                Debug.Log("Настроены ссылки в новом ARSessionManager");
            }

            EditorUtility.SetDirty(sessionManager as UnityEngine.Object);
        }

        // Исправляем ссылки в Wall Segmentation
        var wallSegmentation = FindObjectOfType<MonoBehaviour>(t => t.GetType().Name == "WallSegmentation");
        if (wallSegmentation != null && sessionManager != null)
        {
            Debug.Log("Обновление ссылок в WallSegmentation...");

            // Используем рефлексию для установки ссылки на ARSessionManager
            SetPrivateField(wallSegmentation, "arSessionManager", sessionManager);

            // Проверяем ARCameraManager, если не указан
            if (GetPrivateField(wallSegmentation, "arCameraManager") == null && xrOrigin.Camera != null)
            {
                ARCameraManager cameraManager = xrOrigin.Camera.GetComponent<ARCameraManager>();
                if (cameraManager != null)
                {
                    SetPrivateField(wallSegmentation, "arCameraManager", cameraManager);
                    Debug.Log("Установлена ссылка на ARCameraManager в WallSegmentation");
                }
            }

            EditorUtility.SetDirty(wallSegmentation);
        }

        // Исправляем ссылки в Wall Paint Effect
        var wallPaintEffect = FindObjectOfType<MonoBehaviour>(t => t.GetType().Name == "WallPaintEffect");
        if (wallPaintEffect != null && sessionManager != null)
        {
            Debug.Log("Обновление ссылок в WallPaintEffect...");

            // Используем рефлексию для установки ссылки на ARSessionManager
            SetPrivateField(wallPaintEffect, "arSessionManager", sessionManager);

            // Устанавливаем ссылку на WallSegmentation, если не указана
            if (GetPrivateField(wallPaintEffect, "wallSegmentation") == null && wallSegmentation != null)
            {
                SetPrivateField(wallPaintEffect, "wallSegmentation", wallSegmentation);
                Debug.Log("Установлена ссылка на WallSegmentation в WallPaintEffect");
            }

            EditorUtility.SetDirty(wallPaintEffect);
        }

        // Добавляем ARManagerInitializer, если он не существует
        GameObject managersRoot = GameObject.Find("[Managers]");
        if (managersRoot != null)
        {
            SetupARManagerInitializer(managersRoot);
        }
        else
        {
            // Если объект [Managers] не найден, создаем его
            managersRoot = new GameObject("[Managers]");
            SetupARManagerInitializer(managersRoot);
        }

        Debug.Log("Проверка и исправление компонентов AR завершены");
    }

    // Вспомогательный метод для поиска объекта с компонентом определенного типа по имени
    private T FindObjectOfType<T>(System.Func<System.Type, bool> predicate) where T : Component
    {
        foreach (var obj in FindObjectsOfType<T>())
        {
            if (predicate(obj.GetType()))
            {
                return obj;
            }
        }
        return null;
    }

    // Вспомогательный метод для получения значения приватного поля
    private object GetPrivateField(object target, string fieldName)
    {
        FieldInfo field = target.GetType().GetField(fieldName,
            BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance);

        if (field != null)
        {
            return field.GetValue(target);
        }
        return null;
    }

    // Новый метод для автоматического поиска и назначения модели для WallSegmentation
    private void AutoAssignWallSegmentationModel()
    {
        // Ищем компонент WallSegmentation
        var wallSegmentation = FindObjectOfType<MonoBehaviour>(t => t.GetType().Name == "WallSegmentation");
        if (wallSegmentation == null) return;

        // Проверяем если модель уже назначена
        object currentModel = GetPrivateField(wallSegmentation, "modelAsset");
        if (currentModel != null) return;

        Debug.Log("Модель для WallSegmentation не назначена, ищем подходящую модель в проекте...");

        try
        {
            // Ищем модели ONNX в проекте
            string[] modelPaths = AssetDatabase.FindAssets("t:UnityEngine.Object")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.EndsWith(".onnx"))
                .ToArray();

            // Проверяем есть ли модели
            if (modelPaths.Length > 0)
            {
                // Предпочитаем модели с ключевыми словами "wall", "segment", etc.
                string selectedModelPath = modelPaths
                    .FirstOrDefault(p => p.ToLower().Contains("wall") || p.ToLower().Contains("segment")) ?? modelPaths[0];

                Debug.Log($"Найдена потенциальная модель: {selectedModelPath}");

                // Безопасная загрузка модели
                try
                {
                    // Загружаем модель
                    var modelAsset = AssetDatabase.LoadAssetAtPath(selectedModelPath, typeof(UnityEngine.Object));
                    if (modelAsset != null)
                    {
                        // Проверка размера модели
                        long fileSize = new System.IO.FileInfo(selectedModelPath).Length;
                        Debug.Log($"Размер модели: {fileSize / 1024 / 1024} MB");

                        // Если модель слишком большая, предупреждаем, но не назначаем
                        if (fileSize > 150 * 1024 * 1024) // > 150MB
                        {
                            Debug.LogWarning($"Модель слишком большая ({fileSize / 1024 / 1024} MB), что может вызвать проблемы. Рекомендуется использовать меньшую модель.");
                            EditorUtility.DisplayDialog("Предупреждение о модели",
                                $"Найденная модель ({System.IO.Path.GetFileName(selectedModelPath)}) имеет большой размер ({fileSize / 1024 / 1024} MB).\n\nАвтоматическое назначение пропущено, чтобы избежать возможного сбоя Unity.\n\nРекомендуется использовать модель размером до 50MB.", "OK");
                            return;
                        }

                        // Проверяем тип модели
                        var modelAssetType = FindTypeInAllAssemblies("ModelAsset");
                        bool isValidModelType = false;

                        if (modelAssetType != null && modelAssetType.IsInstanceOfType(modelAsset))
                        {
                            isValidModelType = true;
                        }
                        else
                        {
                            // Проверка известных имен типов моделей
                            string typeName = modelAsset.GetType().Name;
                            isValidModelType = typeName.Contains("Model") ||
                                             typeName.Contains("ONNX") ||
                                             typeName.Contains("Sentis") ||
                                             typeName.Contains("Neural");

                            if (!isValidModelType)
                            {
                                Debug.LogWarning($"Найденный ассет может быть не моделью ML ({typeName}). Автоматическое назначение пропущено.");
                                return;
                            }
                        }

                        if (isValidModelType)
                        {
                            // Назначаем модель в WallSegmentation
                            SetPrivateField(wallSegmentation, "modelAsset", modelAsset);

                            // Обновляем конфигурацию
                            if (sceneConfig != null)
                            {
                                sceneConfig.wallSegmentationModel = modelAsset;
                                EditorUtility.SetDirty(sceneConfig);
                                AssetDatabase.SaveAssets();
                            }

                            Debug.Log($"Автоматически назначена модель для WallSegmentation: {selectedModelPath}");
                        }
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogError($"Ошибка при загрузке модели: {e.Message}");
                }
            }
            else
            {
                Debug.LogWarning("Не найдены ONNX модели в проекте. Необходимо добавить модель для работы сегментации стен.");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Ошибка при автоматическом назначении модели: {e.Message}");
        }
    }

    // Новый метод для отображения сводки о настройке сцены
    private void ShowSetupSummary()
    {
        // Собираем информацию о XROrigin
        XROrigin xrOrigin = FindObjectOfType<XROrigin>();
        var wallSegmentation = FindObjectOfType<MonoBehaviour>(t => t.GetType().Name == "WallSegmentation");
        var wallPaintEffect = FindObjectOfType<MonoBehaviour>(t => t.GetType().Name == "WallPaintEffect");

        // Строим сообщение
        string summary = "======= Сводка настройки AR сцены =======\n\n";

        // AR компоненты
        summary += "AR компоненты:\n";
        if (xrOrigin != null)
        {
            string cameraInfo = xrOrigin.Camera ? xrOrigin.Camera.name : "ОТСУТСТВУЕТ!";
            string offsetInfo = xrOrigin.CameraFloorOffsetObject ? xrOrigin.CameraFloorOffsetObject.name : "ОТСУТСТВУЕТ!";
            summary += $"- XROrigin настроен (Камера: {cameraInfo}, CameraOffset: {offsetInfo})\n";
        }
        else
        {
            summary += "- XROrigin не найден в сцене! AR не будет работать.\n";
        }

        // WallSegmentation
        summary += "\nСегментация стен:\n";
        if (wallSegmentation != null)
        {
            object modelAsset = GetPrivateField(wallSegmentation, "modelAsset");
            summary += modelAsset != null
                ? "- WallSegmentation настроен, модель назначена.\n"
                : "- WallSegmentation настроен, но МОДЕЛЬ НЕ НАЗНАЧЕНА! Сегментация работать не будет.\n";
        }
        else
        {
            summary += "- Компонент WallSegmentation отсутствует в сцене.\n";
        }

        // WallPaintEffect
        summary += "\nЭффект перекраски стен:\n";
        if (wallPaintEffect != null)
        {
            object material = GetPrivateField(wallPaintEffect, "wallPaintMaterial");
            summary += material != null
                ? "- WallPaintEffect настроен с материалом.\n"
                : "- WallPaintEffect настроен, но БЕЗ МАТЕРИАЛА. Перекраска может не работать.\n";
        }
        else
        {
            summary += "- Компонент WallPaintEffect отсутствует в сцене.\n";
        }

        summary += "\n======= Рекомендации =======\n";
        if (wallSegmentation != null && GetPrivateField(wallSegmentation, "modelAsset") == null)
        {
            summary += "1. Назначьте модель ONNX в компоненте WallSegmentation.\n";
            summary += "   ВАЖНО: Если Unity крашится при назначении модели, причины могут быть:\n";
            summary += "     - Модель слишком большая (>100MB)\n";
            summary += "     - Модель несовместима с Unity Sentis\n";
            summary += "     - Проблемы с форматом модели\n\n";
            summary += "   Рекомендации:\n";
            summary += "     - Используйте модели до 50MB\n";
            summary += "     - Убедитесь, что модель экспортирована в формате ONNX opset 7-15\n";
            summary += "     - Установите пакет Unity Sentis через Package Manager\n";
            summary += "     - Попробуйте использовать тестовую модель малого размера\n";
        }

        Debug.Log(summary);
        EditorUtility.DisplayDialog("Настройка AR сцены завершена", summary, "OK");
    }

    // Проверка наличия Unity Sentis в проекте
    private bool IsSentisInstalled()
    {
        try
        {
            // Попытка найти типы из Unity Sentis
            var modelLoaderType = System.Type.GetType("Unity.Sentis.ModelLoader, Unity.Sentis");
            return modelLoaderType != null;
        }
        catch
        {
            return false;
        }
    }

    // Добавляем новый метод для настройки UIInitializer
    private void SetupUIInitializer(GameObject uiRoot)
    {
        // Проверяем, существует ли уже UIInitializer
        var existingInitializer = FindObjectOfType<MonoBehaviour>(t => t.GetType().Name == "UIInitializer");
        if (existingInitializer != null)
        {
            Debug.Log("UIInitializer уже существует в сцене");
            return;
        }

        // Находим тип UIInitializer
        System.Type uiInitializerType = FindTypeInAllAssemblies("UIInitializer");
        if (uiInitializerType == null)
        {
            Debug.LogWarning("Тип UIInitializer не найден. Убедитесь, что скрипт UIInitializer.cs существует в проекте.");
            return;
        }

        // Создаем GameObject для UIInitializer
        GameObject initializerObj = new GameObject("UI Initializer");
        initializerObj.transform.SetParent(uiRoot.transform);
        initializerObj.AddComponent(uiInitializerType);

        Debug.Log("Добавлен UIInitializer для автоматической инициализации UI компонентов");
    }
}