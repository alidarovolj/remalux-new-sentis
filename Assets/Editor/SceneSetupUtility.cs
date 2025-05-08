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
        // Создаем новую сцену
        if (EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
        {
            EditorSceneManager.NewScene(NewSceneSetup.DefaultGameObjects);
            
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
            
            // Сохраняем сцену
            string scenePath = "Assets/Scenes/" + sceneName + ".unity";
            
            // Создаем директорию, если не существует
            string directory = Path.GetDirectoryName(scenePath);
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), scenePath);
            Debug.Log("Сцена создана и сохранена: " + scenePath);
        }
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
            
            Debug.Log("AR компоненты настроены из префабов.");
            return;
        }
        
        // Иначе создаем компоненты программно
        // Создаем AR Session
        arSessionObj = new GameObject("AR Session");
        arSessionObj.transform.SetParent(parent.transform);
        arSessionObj.AddComponent<ARSession>();
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
        ARCameraBackground cameraBackground = arCameraObj.AddComponent<ARCameraBackground>();
        cameraBackground.useCustomMaterial = false;
        
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
        
        // Добавляем AR Plane Manager
        var planeManager = trackersObj.AddComponent<ARPlaneManager>();
        
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
        trackersObj.AddComponent<ARPointCloudManager>();
        trackersObj.AddComponent<ARAnchorManager>();
        
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
    
    // Вспомогательный метод для проверки и исправления ссылок в XROrigin
    private void FixXROriginReferences(GameObject xrOriginObj)
    {
        if (xrOriginObj == null) return;
        
        XROrigin xrOrigin = xrOriginObj.GetComponent<XROrigin>();
        if (xrOrigin == null) return;
        
        // Проверяем и исправляем ссылку на Camera
        if (xrOrigin.Camera == null)
        {
            // Ищем камеру в иерархии
            Transform cameraOffsetTrans = xrOriginObj.transform.Find("Camera Offset");
            if (cameraOffsetTrans != null)
            {
                Transform cameraTrans = cameraOffsetTrans.Find("AR Camera");
                if (cameraTrans != null)
                {
                    Camera cam = cameraTrans.GetComponent<Camera>();
                    if (cam != null)
                    {
                        xrOrigin.Camera = cam;
                        Debug.Log("Ссылка на камеру в XROrigin была исправлена.");
                    }
                }
                
                // Проверяем и исправляем ссылку на Camera Floor Offset Object
                if (xrOrigin.CameraFloorOffsetObject == null)
                {
                    xrOrigin.CameraFloorOffsetObject = cameraOffsetTrans.gameObject;
                    Debug.Log("Ссылка на Camera Offset в XROrigin была исправлена.");
                }
            }
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
        
        // Создаем объект Wall Segmentation
        GameObject wallSegmentationObj = new GameObject("Wall Segmentation");
        wallSegmentationObj.transform.SetParent(parent.transform);
        
        // Добавляем компонент WallSegmentation
        Component segmentation = wallSegmentationObj.AddComponent(wallSegmentationType);
        
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
        
        // Если у нас есть модель в конфигурации, назначаем её
        if (sceneConfig != null && sceneConfig.wallSegmentationModel != null)
        {
            SetPrivateField(segmentation, "modelAsset", sceneConfig.wallSegmentationModel);
        }
        
        // Выдаем подробные инструкции пользователю только если модель не назначена
        if (sceneConfig == null || sceneConfig.wallSegmentationModel == null)
        {
            Debug.LogWarning(
                "ВАЖНО: необходимо настроить модель сегментации стен!\n" +
                "1. Найдите объект [Managers]/Wall Segmentation в сцене\n" +
                "2. Назначьте вашу ONNX модель в поле Model Asset\n" +
                "3. Если у вас нет модели, попробуйте использовать тестовую модель из директории Assets/Models/model.onnx"
            );
        }
        
        Debug.Log("Компонент сегментации стен настроен");
        
        // Создаем эффект перекраски стен, если включено
        if (setupWallPainting)
        {
            // Проверяем наличие типа WallPaintEffect
            System.Type wallPaintEffectType = FindTypeInAllAssemblies("WallPaintEffect");
            if (wallPaintEffectType == null)
            {
                Debug.LogError("Тип WallPaintEffect не найден. Убедитесь, что скрипт WallPaintEffect.cs существует в проекте.");
                return;
            }
            
            GameObject wallPaintEffectObj = new GameObject("Wall Paint Effect");
            wallPaintEffectObj.transform.SetParent(parent.transform);
            
            // Добавляем компонент WallPaintEffect
            Component paintEffect = wallPaintEffectObj.AddComponent(wallPaintEffectType);
            
            // Используем материал из конфигурации, если он есть
            if (sceneConfig != null && sceneConfig.wallPaintMaterial != null)
            {
                SetPrivateField(paintEffect, "wallPaintMaterial", sceneConfig.wallPaintMaterial);
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
                    
                    // Назначаем материал и ссылку на WallSegmentation
                    SetPrivateField(paintEffect, "wallPaintMaterial", wallPaintMaterial);
                    SetPrivateField(paintEffect, "wallSegmentation", segmentation);
                    
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

        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ USE_MASK

            #include ""Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl""

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float2 uv : TEXCOORD0;
                float4 positionHCS : SV_POSITION;
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
                OUT.positionHCS = TransformObjectToHClip(IN.positionOS.xyz);
                OUT.uv = TRANSFORM_TEX(IN.uv, _MainTex);
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                half4 color = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                
                #ifdef USE_MASK
                // Используем маску сегментации для определения области покраски
                float mask = SAMPLE_TEXTURE2D(_SegmentationMask, sampler_SegmentationMask, IN.uv).r;
                
                // Применяем цвет только в области стен
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
        
        Debug.Log("Ресурсы для перекраски стен созданы успешно.");
    }
} 