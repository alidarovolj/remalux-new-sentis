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

/// <summary>
/// Утилита для быстрого создания и настройки сцены с AR/ML функциональностью
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
    
    [MenuItem("Remalux/Создать AR сцену")]
    public static void ShowWindow()
    {
        GetWindow<SceneSetupUtility>("Создать AR сцену");
    }
    
    private void OnGUI()
    {
        GUILayout.Label("Настройка AR сцены для перекраски стен", EditorStyles.boldLabel);
        
        EditorGUILayout.Space();
        
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
            
            // Создаем корневые объекты
            GameObject arRoot = new GameObject("[AR]");
            GameObject uiRoot = new GameObject("[UI]");
            GameObject managersRoot = new GameObject("[Managers]");
            
            // Настраиваем AR
            if (setupAR)
            {
                SetupARComponents(arRoot);
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
        // Создаем AR Session
        GameObject arSessionObj = new GameObject("AR Session");
        arSessionObj.transform.SetParent(parent.transform);
        arSessionObj.AddComponent<ARSession>();
        arSessionObj.AddComponent<ARInputManager>();

        // Создаем XR Origin
        GameObject xrOriginObj = new GameObject("XR Origin");
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
        
        // Добавляем дополнительные менеджеры
        trackersObj.AddComponent<ARPointCloudManager>();
        trackersObj.AddComponent<ARAnchorManager>();
        
        // Активируем все компоненты
        arSessionObj.SetActive(true);
        xrOriginObj.SetActive(true);
        
        Debug.Log("AR компоненты настроены. Должен отображаться фид камеры.");
    }
    
    private void SetupUI(GameObject parent)
    {
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
        
        // Создаем текстуру для маски сегментации
        RenderTexture maskTexture = new RenderTexture(256, 256, 0, RenderTextureFormat.RFloat);
        maskTexture.enableRandomWrite = true;
        maskTexture.Create();
        
        // Назначаем маску в компонент WallSegmentation через рефлексию
        SetPrivateField(segmentation, "segmentationMaskTexture", maskTexture);
        
        // Назначаем ARCameraManager
        SetPrivateField(segmentation, "arCameraManager", cameraManager);
        
        // Выдаем подробные инструкции пользователю
        Debug.LogWarning(
            "ВАЖНО: необходимо настроить модель сегментации стен!\n" +
            "1. Найдите объект [Managers]/Wall Segmentation в сцене\n" +
            "2. Назначьте вашу ONNX модель в поле Model Asset\n" +
            "3. Если у вас нет модели, попробуйте использовать тестовую модель из директории Assets/Models/model.onnx"
        );
        
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
            
            // Создаем материал для шейдера перекраски
            Shader wallPaintShader = Shader.Find("Custom/WallPaint");
            if (wallPaintShader != null)
            {
                Material wallPaintMaterial = new Material(wallPaintShader);
                
                // Настраиваем материал
                wallPaintMaterial.SetColor("_PaintColor", Color.red); // Используем красный цвет по умолчанию для лучшей видимости
                wallPaintMaterial.SetFloat("_BlendFactor", 0.7f);
                
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
                
                AssetDatabase.CreateAsset(wallPaintMaterial, materialPath);
                AssetDatabase.SaveAssets();
                
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
} 