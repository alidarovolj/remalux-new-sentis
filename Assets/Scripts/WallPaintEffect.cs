using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Reflection;
using System.Collections.Generic;
using System.Collections;
using System.Linq;
using UnityEngine.XR.ARFoundation;

[RequireComponent(typeof(Camera))]
public class WallPaintEffect : MonoBehaviour
{
    [SerializeField] private Material wallPaintMaterial;
    [SerializeField] private WallSegmentation wallSegmentation;
    [SerializeField] private Color paintColor = Color.white;
    [SerializeField] private float blendFactor = 0.5f;
    [SerializeField] private bool useMask = true;
    [SerializeField] private ARSessionManager arSessionManager; // Ссылка на ARSessionManager
    [SerializeField] private bool attachToARPlanes = true; // Опция для крепления материала к AR-плоскостям
    [SerializeField] private bool showARPlaneDebugVisuals = true;

    private Camera mainCamera; // Added missing camera field
    private WallPaintFeature wallPaintFeature;
    private bool isInitialized = false;
    private bool isSegmentationReady = false;
    private bool shouldInitialize = false; // Flag to track when AR session is ready
    private ARPlaneManager planeManager; // Хранит ссылку на ARPlaneManager

    // Добавляем поле для отслеживания последней использованной маски
    private RenderTexture lastUsedMask;
    private int framesSinceLastSegmentationUpdate = 0;
    [SerializeField] private bool debugMode = false;

    // Добавляем поле для ARLightEstimation
    private ARLightEstimation lightEstimation;

    // Список плоскостей, к которым был применен материал
    private readonly List<ARPlane> processedPlanes = new List<ARPlane>();

    private void Start()
    {
        Debug.Log("[WallPaintEffect LOG] Start() called.");
        if (mainCamera == null)
        {
            mainCamera = Camera.main;
            Debug.Log("[WallPaintEffect LOG] Main camera assigned in Start.");
        }

        // Находим ARSessionManager, если он не задан
        if (arSessionManager == null)
        {
            arSessionManager = FindObjectOfType<ARSessionManager>();
        }

        // Находим ARPlaneManager
        planeManager = FindObjectOfType<ARPlaneManager>();
        if (planeManager != null && attachToARPlanes)
        {
            // Подписываемся на события создания/обновления плоскостей
            planeManager.planesChanged += OnPlanesChanged;
            Debug.Log("[WallPaintEffect LOG] Подписка на события ARPlaneManager.planesChanged выполнена");
        }
        else if (attachToARPlanes)
        {
            Debug.LogWarning("[WallPaintEffect LOG] ARPlaneManager не найден, невозможно подписаться на события planesChanged");
        }

        // Добавляем задержку для инициализации на iOS, чтобы убедиться, что AR сессия запустилась
#if UNITY_IOS && !UNITY_EDITOR
        if (arSessionManager != null && arSessionManager.IsSessionInitialized())
        {
            // Если сессия уже инициализирована
            shouldInitialize = true;
            StartCoroutine(InitializeWithDelay());
        }
        else
        {
            // Инициализируем после готовности AR сессии
            shouldInitialize = false;
            Debug.Log("WallPaintEffect ждет инициализации AR сессии");
        }
#else
        // В редакторе или на других платформах инициализируем сразу
        shouldInitialize = true;
        InitializeComponents();
#endif
    }

    // Обработчик события изменения плоскостей
    private void OnPlanesChanged(ARPlanesChangedEventArgs args)
    {
        if (!attachToARPlanes || !isInitialized) return;

        // Обработка новых плоскостей
        foreach (ARPlane plane in args.added)
        {
            ApplyMaterialToPlane(plane);
        }

        // Обработка обновленных плоскостей
        foreach (ARPlane plane in args.updated)
        {
            // Проверяем, изменился ли тип плоскости (например, из горизонтальной в вертикальную)
            if (!processedPlanes.Contains(plane) ||
                plane.alignment == UnityEngine.XR.ARSubsystems.PlaneAlignment.Vertical)
            {
                ApplyMaterialToPlane(plane);
            }
        }

        // Удаляем из списка обработанных плоскостей те, которые были удалены из сцены
        foreach (ARPlane plane in args.removed)
        {
            processedPlanes.Remove(plane);
        }
    }

    // Метод для применения материала к отдельной плоскости
    private void ApplyMaterialToPlane(ARPlane plane)
    {
        // Проверяем, является ли плоскость стеной (вертикальной плоскостью)
        if (plane.alignment == UnityEngine.XR.ARSubsystems.PlaneAlignment.Vertical)
        {
            MeshRenderer renderer = plane.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                // Создаем экземпляр материала для каждой плоскости, чтобы избежать взаимного влияния
                Material planeMaterial = new Material(wallPaintMaterial);
                renderer.material = planeMaterial;

                // Включаем ключевые слова для привязки к AR-пространству
                planeMaterial.EnableKeyword("USE_AR_WORLD_SPACE");

                // Если включен режим отладки, делаем линии границы плоскости более заметными
                if (showARPlaneDebugVisuals)
                {
                    LineRenderer lineRenderer = plane.GetComponent<LineRenderer>();
                    if (lineRenderer != null)
                    {
                        lineRenderer.startWidth = 0.05f;
                        lineRenderer.endWidth = 0.05f;
                        lineRenderer.startColor = Color.yellow;
                        lineRenderer.endColor = Color.yellow;
                    }

                    // Делаем материал плоскости более заметным
                    planeMaterial.SetFloat("_BlendFactor", 0.8f);
                    planeMaterial.SetColor("_PaintColor", new Color(1f, 0.5f, 0f, 0.8f)); // Оранжевый цвет для отладки

                    // Включаем режим отладки
                    planeMaterial.EnableKeyword("DEBUG_OVERLAY");
                }

                // Если есть ARLightEstimation, добавляем материал для обновления освещения
                if (lightEstimation != null)
                {
                    lightEstimation.AddMaterial(planeMaterial);
                }

                // Добавляем плоскость в список обработанных, если её там еще нет
                if (!processedPlanes.Contains(plane))
                {
                    processedPlanes.Add(plane);
                }

                Debug.Log($"[WallPaintEffect LOG] Применен материал к вертикальной AR плоскости: {plane.name}");
            }
        }
    }

    // Ответ на сообщение от ARSessionManager
    public void OnARSessionInitialized()
    {
        Debug.Log("WallPaintEffect получил уведомление о готовности AR сессии");
        shouldInitialize = true;

#if UNITY_IOS && !UNITY_EDITOR
        StartCoroutine(InitializeWithDelay());
#else
        if (!isInitialized)
        {
            InitializeComponents();
        }
#endif
    }

    // Добавляем метод с задержкой для iOS
    private IEnumerator InitializeWithDelay()
    {
        Debug.Log("[WallPaintEffect LOG] InitializeWithDelay() coroutine started.");
        // Проверяем флаг инициализации
        if (!shouldInitialize)
        {
            Debug.Log("WallPaintEffect: инициализация отложена, так как shouldInitialize = false");
            yield break;
        }

        // Ждем один кадр, чтобы убедиться что AR сессия инициализирована
        yield return null;

        // Дополнительная задержка для iOS
        yield return new WaitForSeconds(0.5f);

        // Теперь инициализируем наши компоненты
        InitializeComponents();
        Debug.Log("[WallPaintEffect LOG] InitializeWithDelay() coroutine finished.");
    }

    // Основной метод инициализации, вынесенный для повторного использования
    private void InitializeComponents()
    {
        Debug.Log("[WallPaintEffect LOG] InitializeComponents() called.");
        if (isInitialized)
        {
            Debug.LogWarning("[WallPaintEffect LOG] InitializeComponents: Уже инициализировано.");
            return; // Избегаем повторной инициализации
        }

        Debug.Log("WallPaintEffect: Начало инициализации компонентов");

        // 1. Проверяем материал, назначенный в инспекторе
        if (wallPaintMaterial != null)
        {
            Debug.Log($"[WallPaintEffect LOG] Используем материал, назначенный в инспекторе: {wallPaintMaterial.name}");
            // Важно: НЕ создаем копию через `new Material(wallPaintMaterial)`
            // Будем использовать сам назначеннный материал.
            // Если нужны уникальные изменения для этого экземпляра,
            // Unity создаст инстанс автоматически при изменении свойств (SetColor, SetFloat и т.д.).
        }
        else
        {
            // 2. Если в инспекторе не назначен, пробуем загрузить из Resources
            Debug.LogWarning("[WallPaintEffect LOG] Материал не назначен в инспекторе. Пытаемся загрузить из Resources/Materials/WallPaint...");
            wallPaintMaterial = Resources.Load<Material>("Materials/WallPaint");

            if (wallPaintMaterial == null)
            {
                // 3. Если и в Resources нет, создаем новый с нуля
                Debug.LogWarning("[WallPaintEffect LOG] Материал не найден в Resources. Создаем новый материал с шейдером Custom/WallPaint.");
                Shader wallPaintShader = Shader.Find("Custom/WallPaint");
                if (wallPaintShader != null)
                {
                    wallPaintMaterial = new Material(wallPaintShader);
                    wallPaintMaterial.name = "WallPaint_RuntimeCreated"; // Даем имя для отладки
                    // Устанавливаем дефолтные значения из инспектора
                    wallPaintMaterial.SetColor("_PaintColor", paintColor);
                    wallPaintMaterial.SetFloat("_BlendFactor", blendFactor);
                    Debug.Log("[WallPaintEffect LOG] Новый материал создан.");
                }
                else
                {
                    Debug.LogError("[WallPaintEffect LOG] Шейдер Custom/WallPaint не найден! Невозможно создать материал. Компонент будет отключен.");
                    enabled = false;
                    return;
                }
            }
            else
            {
                Debug.Log("[WallPaintEffect LOG] Материал успешно загружен из Resources.");
            }
        }

        // Проверяем, что у материала ЕСТЬ нужные свойства (уже после того как материал точно есть)
        if (!wallPaintMaterial.HasProperty("_SegmentationMask") || !wallPaintMaterial.HasProperty("_PaintColor") || !wallPaintMaterial.HasProperty("_BlendFactor"))
        {
            Debug.LogError($"[WallPaintEffect LOG] Материал '{wallPaintMaterial.name}' не содержит необходимые свойства (_SegmentationMask, _PaintColor, _BlendFactor). Убедитесь, что используется правильный шейдер (Custom/WallPaint). Компонент будет отключен.");
            // Попытка найти правильный шейдер не имеет смысла, если сам материал некорректен.
            enabled = false;
            return;
        }

        // Если сегментация не указана, пытаемся найти ее в сцене
        if (wallSegmentation == null)
        {
            wallSegmentation = FindObjectOfType<WallSegmentation>();
            if (wallSegmentation == null)
            {
                Debug.LogWarning("WallSegmentation component not found. Wall masking will not be applied.");
            }
            else
            {
                Debug.Log("WallPaintEffect: Найден компонент WallSegmentation");
            }
        }

        // Находим WallPaintFeature в URP Renderer 
        FindAndSetupWallPaintFeature();

        // Применяем начальные настройки
        UpdatePaintParameters();
        isInitialized = true;
        Debug.Log("WallPaintEffect инициализирован успешно");
        Debug.Log("[WallPaintEffect LOG] InitializeComponents() finished. isInitialized = true.");

        // Запускаем отладочную корутину после небольшой задержки, если включено
        // StartCoroutine(DebugAfterDelay());

        // Ищем ARLightEstimation, если он есть
        lightEstimation = FindObjectOfType<ARLightEstimation>();
        if (lightEstimation != null)
        {
            Debug.Log("[WallPaintEffect LOG] Найден компонент ARLightEstimation. Добавляем материал в список для освещения.");
            lightEstimation.AddMaterial(wallPaintMaterial);
        }
        else
        {
            Debug.Log("[WallPaintEffect LOG] ARLightEstimation не найден. Будет использовано стандартное освещение.");
        }
    }

    // Корутина для запуска отладки с задержкой
    private IEnumerator DebugAfterDelay()
    {
        // Ждем 1 секунду для полной инициализации
        yield return new WaitForSeconds(1.0f);

        // Выводим диагностическую информацию
        DebugSegmentationStatus();

        // Проверяем режим рендеринга
        if (wallPaintMaterial != null && !wallPaintMaterial.HasProperty("_DebugOverlay"))
        {
            Debug.LogWarning("WallPaintEffect: Материал не содержит свойство _DebugOverlay! Возможно, шейдер не обновлен.");
        }

        // Исправляем настройки рендеринга
        FixRenderingMode();
        Debug.Log("WallPaintEffect: Запущен режим отладки. Проверьте отображение сетки на экране.");

        // Через 5 секунд отключаем режим отладки
        yield return new WaitForSeconds(5.0f);
        DisableDebugMode();
        Debug.Log("WallPaintEffect: Отладочный режим отключен.");
    }

    // Метод для поиска WallPaintFeature в URP Renderer
    private void FindAndSetupWallPaintFeature()
    {
        // Сначала проверяем текущий активный рендер пайплайн
        var urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urpAsset == null)
        {
            Debug.LogError("URP is not active. WallPaintFeature requires URP to work properly.");
            return;
        }

        // Получаем рендереры через рефлексию (поскольку renderers - приватное поле)
        var rendererDataField = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList",
            BindingFlags.NonPublic | BindingFlags.Instance);

        if (rendererDataField == null)
        {
            // Пробуем альтернативное имя поля (может различаться в разных версиях Unity)
            rendererDataField = typeof(UniversalRenderPipelineAsset).GetField("m_RendererData",
                BindingFlags.NonPublic | BindingFlags.Instance);

            if (rendererDataField == null)
            {
                Debug.LogError("Could not access URP Renderer data through reflection.");
                FindWallPaintFeatureFallback();
                return;
            }
        }

        var rendererDataList = rendererDataField.GetValue(urpAsset) as IList<ScriptableRendererData>;
        if (rendererDataList == null || rendererDataList.Count == 0)
        {
            // Пробуем получить как единичный объект, а не список
            var singleRendererData = rendererDataField.GetValue(urpAsset) as ScriptableRendererData;
            if (singleRendererData != null)
            {
                CheckRendererDataForFeature(singleRendererData);
            }
            else
            {
                Debug.LogError("No URP Renderer data found");
                FindWallPaintFeatureFallback();
            }
            return;
        }

        // Проверяем все доступные рендереры на наличие WallPaintFeature
        foreach (var rendererData in rendererDataList)
        {
            if (rendererData == null) continue;
            CheckRendererDataForFeature(rendererData);
            if (wallPaintFeature != null) break;
        }

        // Если не удалось найти через рефлексию, используем запасной метод
        if (wallPaintFeature == null)
        {
            FindWallPaintFeatureFallback();
        }
    }

    // Проверяем конкретный ScriptableRendererData на наличие WallPaintFeature
    private void CheckRendererDataForFeature(ScriptableRendererData rendererData)
    {
        if (rendererData == null) return;

        // Получаем список features через рефлексию
        var featuresField = typeof(ScriptableRendererData).GetField("m_RendererFeatures",
            BindingFlags.NonPublic | BindingFlags.Instance);

        if (featuresField == null)
        {
            Debug.LogWarning("Could not access renderer features field through reflection.");
            return;
        }

        var features = featuresField.GetValue(rendererData) as List<ScriptableRendererFeature>;
        if (features == null) return;

        // Ищем WallPaintFeature в списке
        foreach (var feature in features)
        {
            if (feature is WallPaintFeature wallPaintFeatureFound)
            {
                wallPaintFeature = wallPaintFeatureFound;

                // Проверяем, есть ли уже материал в фиче
                if (wallPaintFeature.passMaterial == null)
                {
                    wallPaintFeature.SetPassMaterial(wallPaintMaterial);
                }

                Debug.Log("WallPaintFeature found in URP Renderer and connected successfully");
                return;
            }
        }
    }

    // Запасной метод поиска WallPaintFeature в случае, если рефлексия не сработает
    private void FindWallPaintFeatureFallback()
    {
        WallPaintFeature[] features = FindObjectsOfType<WallPaintFeature>(true);
        if (features.Length > 0)
        {
            wallPaintFeature = features[0];

            // Проверяем, есть ли уже материал в фиче
            if (wallPaintFeature.passMaterial == null)
            {
                wallPaintFeature.SetPassMaterial(wallPaintMaterial);
            }
            Debug.Log("WallPaintFeature found using fallback method");
        }
        else
        {
            Debug.LogWarning("WallPaintFeature not found on the URP Renderer. Please add it manually in the editor.");
        }
    }

    private void OnEnable()
    {
        if (isInitialized)
        {
            UpdatePaintParameters();
        }
    }

    private void Update()
    {
        if (!isInitialized) return;

        // Добавляем периодическую проверку состояния сегментации
        if (Time.frameCount % 60 == 0) // Каждые ~60 кадров
        {
            DebugSegmentationStatus();
        }

        // Проверяем изменения в маске сегментации
        if (wallSegmentation != null && wallSegmentation.segmentationMaskTexture != null)
        {
            // Если маска изменилась или прошло больше 10 кадров с последнего обновления
            if (lastUsedMask != wallSegmentation.segmentationMaskTexture || framesSinceLastSegmentationUpdate > 10)
            {
                if (debugMode)
                {
                    Debug.Log("[WallPaintEffect LOG] Обнаружено обновление маски сегментации. Обновляем материал.");
                }

                // Обновляем ссылку на последнюю использованную маску
                lastUsedMask = wallSegmentation.segmentationMaskTexture;
                framesSinceLastSegmentationUpdate = 0;

                // Принудительно обновляем материал
                EnsureTexturesInitialized();
                UpdatePaintParameters();

                if (!isSegmentationReady && lastUsedMask != null)
                {
                    isSegmentationReady = true;
                    if (debugMode)
                    {
                        Debug.Log("[WallPaintEffect LOG] Сегментация стала доступна, статус эффекта: " + IsReady());
                    }
                }
            }
            else
            {
                framesSinceLastSegmentationUpdate++;
            }
        }
        else if (isSegmentationReady)
        {
            isSegmentationReady = false;
            if (debugMode)
            {
                Debug.Log("[WallPaintEffect LOG] Сегментация стала недоступна, статус эффекта: " + IsReady());
            }
        }

        // Устанавливаем значение маски в зависимости от useMask
        if (wallPaintMaterial != null)
        {
            float useMaskValue = useMask ? 1.0f : 0.0f;
            if (wallPaintMaterial.HasProperty("_UseMask"))
            {
                float currentValue = wallPaintMaterial.GetFloat("_UseMask");
                if (currentValue != useMaskValue)
                {
                    wallPaintMaterial.SetFloat("_UseMask", useMaskValue);
                }
            }

            // Устанавливаем значение прозрачности, чтобы избежать черного экрана
            if (wallPaintMaterial.HasProperty("_BlendFactor"))
            {
                float currentBlend = wallPaintMaterial.GetFloat("_BlendFactor");
                // Если значение нулевое, устанавливаем слабое значение для избежания черного экрана
                if (currentBlend < 0.01f)
                {
                    wallPaintMaterial.SetFloat("_BlendFactor", 0.1f);
                }
            }
        }

        // Пытаемся настроить освещение, если еще не сделали этого
        if (lightEstimation == null && isInitialized && mainCamera != null)
        {
            TrySetupLightEstimation();
        }
    }

    // Новый метод для отладки состояния сегментации
    private void DebugSegmentationStatus()
    {
        // Состояние сегментации
        string segmentationState = wallSegmentation != null
            ? (wallSegmentation.IsModelInitialized ? "Инициализирована" : "Не инициализирована")
            : "Отсутствует";

        // Информация о текстуре сегментации
        string maskInfo = "Маска сегментации: ";
        if (wallSegmentation != null && wallSegmentation.segmentationMaskTexture != null)
        {
            RenderTexture mask = wallSegmentation.segmentationMaskTexture;
            maskInfo += $"{mask.width}x{mask.height}";

            // Проверяем, используется ли маска в материале
            if (wallPaintMaterial != null)
            {
                Texture usedMask = wallPaintMaterial.GetTexture("_SegmentationMask");
                if (usedMask == mask)
                {
                    maskInfo += " (установлена в материал)";
                }
                else
                {
                    maskInfo += " (НЕ установлена в материал!)";
                }
            }
        }
        else
        {
            maskInfo += "отсутствует";
        }

        // Информация о материале
        string materialInfo = "Материал: ";
        if (wallPaintMaterial != null)
        {
            materialInfo += wallPaintMaterial.shader.name;
            if (wallPaintMaterial.HasProperty("_BlendFactor"))
            {
                materialInfo += $", Blend Factor: {wallPaintMaterial.GetFloat("_BlendFactor")}";
            }
            if (wallPaintMaterial.HasProperty("_PaintColor"))
            {
                Color color = wallPaintMaterial.GetColor("_PaintColor");
                materialInfo += $", Color: ({color.r:F2}, {color.g:F2}, {color.b:F2}, {color.a:F2})";
            }
        }
        else
        {
            materialInfo += "отсутствует";
        }

        // Выводим собранную информацию
        Debug.Log($"[WallPaintEffect] Статус: {segmentationState}\n{maskInfo}\n{materialInfo}");
    }

    private void UpdatePaintParameters()
    {
        Debug.Log("[WallPaintEffect LOG] UpdatePaintParameters() called.");
        if (wallPaintMaterial == null)
        {
            // Этого не должно происходить, если InitializeComponents отработал корректно
            Debug.LogError("[WallPaintEffect LOG] UpdatePaintParameters: wallPaintMaterial is null! Попытка повторной инициализации...");
            InitializeComponents(); // Попробуем инициализировать снова
            if (wallPaintMaterial == null)
            {
                Debug.LogError("[WallPaintEffect LOG] UpdatePaintParameters: Повторная инициализация не помогла. wallPaintMaterial все еще null. Прерывание.");
                return; // Прерываем, если материал так и не появился
            }
        }

        // Убедимся, что шейдер правильный перед установкой параметров
        if (wallPaintMaterial.shader == null || !wallPaintMaterial.shader.name.Contains("Custom/WallPaint"))
        {
            Debug.LogWarning($"[WallPaintEffect LOG] UpdatePaintParameters: У материала '{wallPaintMaterial.name}' неправильный шейдер ({wallPaintMaterial.shader?.name}). Пропускаем установку параметров.");
            // Можно попытаться назначить правильный шейдер, но это рискованно
            // Shader correctShader = Shader.Find("Custom/WallPaint");
            // if (correctShader) wallPaintMaterial.shader = correctShader;
            return; // Лучше прервать, чем работать с неправильным шейдером
        }

        // Ensure textures are initialized before setting other parameters
        EnsureTexturesInitialized();

        wallPaintMaterial.SetColor("_PaintColor", paintColor);
        wallPaintMaterial.SetFloat("_BlendFactor", blendFactor);

        Debug.Log($"[WallPaintEffect LOG] UpdatePaintParameters: Color={paintColor}, Blend={blendFactor}, UseMask={useMask}");

        if (useMask)
        {
            if (wallSegmentation != null && wallSegmentation.segmentationMaskTexture != null)
            {
                Debug.Log("[WallPaintEffect LOG] UpdatePaintParameters: USE_MASK is true, segmentation mask is available. Setting texture and enabling keyword.");
                wallPaintMaterial.SetTexture("_SegmentationMask", wallSegmentation.segmentationMaskTexture);
                wallPaintMaterial.EnableKeyword("USE_SEGMENTATION_MASK");
            }
            else
            {
                Debug.LogWarning("[WallPaintEffect LOG] UpdatePaintParameters: USE_MASK is true, but segmentation mask is NULL. Disabling keyword.");
                wallPaintMaterial.DisableKeyword("USE_SEGMENTATION_MASK");
            }
        }
        else
        {
            Debug.Log("[WallPaintEffect LOG] UpdatePaintParameters: USE_MASK is false. Disabling keyword.");
            wallPaintMaterial.DisableKeyword("USE_SEGMENTATION_MASK");
        }

        // Включаем поддержку привязки к AR-пространству
        wallPaintMaterial.EnableKeyword("USE_AR_WORLD_SPACE");

        // Применяем материал к AR плоскостям, если они есть
        ApplyMaterialToARPlanes();

        // Обновляем материал в WallPaintFeature, если он уже найден
        if (wallPaintFeature != null)
        {
            // Дополнительная проверка перед установкой
            if (wallPaintFeature.passMaterial != wallPaintMaterial)
            {
                wallPaintFeature.SetPassMaterial(wallPaintMaterial);
                Debug.Log("[WallPaintEffect LOG] UpdatePaintParameters: обновлен passMaterial в WallPaintFeature.");
            }
        }
    }

    // Новый метод для применения материала к AR плоскостям
    private void ApplyMaterialToARPlanes()
    {
        // Если функция прикрепления к плоскостям отключена, выходим
        if (!attachToARPlanes) return;

        // Получаем ARPlaneManager, если он еще не сохранен
        if (planeManager == null)
        {
            planeManager = FindObjectOfType<ARPlaneManager>();
            if (planeManager == null)
            {
                Debug.LogWarning("[WallPaintEffect LOG] ARPlaneManager не найден в сцене.");
                return;
            }
            else if (!planeManager.enabled)
            {
                Debug.LogWarning("[WallPaintEffect LOG] ARPlaneManager найден, но не активен.");
                return;
            }

            // Подписываемся на события плоскостей, если еще не подписаны
            planeManager.planesChanged += OnPlanesChanged;
        }

        // Проверяем, есть ли материал для AR плоскостей
        if (planeManager.planePrefab == null)
        {
            Debug.LogWarning("[WallPaintEffect LOG] ARPlaneManager не имеет назначенного префаба.");
            return;
        }

        // Настраиваем материал для привязки к AR-пространству
        wallPaintMaterial.EnableKeyword("USE_AR_WORLD_SPACE");

        // Применяем материал ко всем существующим вертикальным плоскостям
        ARPlane[] planes = FindObjectsOfType<ARPlane>();
        if (planes.Length == 0)
        {
            Debug.Log("[WallPaintEffect LOG] AR плоскости не найдены в сцене.");
            return;
        }

        // Очищаем список обработанных плоскостей и заполняем его заново
        processedPlanes.Clear();

        foreach (ARPlane plane in planes)
        {
            ApplyMaterialToPlane(plane);
        }
    }

    // Обновляем метод EnsureTexturesInitialized для более надежного обновления маски
    private void EnsureTexturesInitialized()
    {
        Debug.Log("[WallPaintEffect LOG] EnsureTexturesInitialized() called.");
        if (wallPaintMaterial == null)
        {
            Debug.LogWarning("[WallPaintEffect LOG] EnsureTexturesInitialized: wallPaintMaterial is null. Aborting.");
            return;
        }

        // Ensure _MainTex is initialized
        if (wallPaintMaterial.GetTexture("_MainTex") == null)
        {
            Debug.Log("[WallPaintEffect LOG] EnsureTexturesInitialized: _MainTex is null. Setting default white texture.");
            wallPaintMaterial.SetTexture("_MainTex", Texture2D.whiteTexture);
        }

        // Ensure _SegmentationMask is initialized if needed
        if (useMask)
        {
            if (wallSegmentation != null && wallSegmentation.segmentationMaskTexture != null)
            {
                // Проверяем, отличается ли текущая маска от той, что уже установлена в материале
                Texture currentMask = wallPaintMaterial.GetTexture("_SegmentationMask");
                if (currentMask != wallSegmentation.segmentationMaskTexture)
                {
                    Debug.Log($"[WallPaintEffect LOG] EnsureTexturesInitialized: Обновляем маску сегментации для материала (Текущая: {currentMask}, Новая: {wallSegmentation.segmentationMaskTexture})");
                    wallPaintMaterial.SetTexture("_SegmentationMask", wallSegmentation.segmentationMaskTexture);
                    wallPaintMaterial.EnableKeyword("USE_SEGMENTATION_MASK");
                    lastUsedMask = wallSegmentation.segmentationMaskTexture;
                }
            }
            else if (wallPaintMaterial.GetTexture("_SegmentationMask") == null)
            {
                Debug.LogWarning("[WallPaintEffect LOG] EnsureTexturesInitialized: useMask is true, но маска недоступна. Устанавливаем временную черную текстуру.");
                wallPaintMaterial.SetTexture("_SegmentationMask", Texture2D.blackTexture);
                wallPaintMaterial.EnableKeyword("USE_SEGMENTATION_MASK");
            }
        }
        else if (wallPaintMaterial.IsKeywordEnabled("USE_SEGMENTATION_MASK"))
        {
            Debug.Log("[WallPaintEffect LOG] EnsureTexturesInitialized: useMask is false, отключаем USE_SEGMENTATION_MASK keyword.");
            wallPaintMaterial.DisableKeyword("USE_SEGMENTATION_MASK");
        }

        // Включаем YIQ цветовое пространство для лучшего сохранения яркости текстуры
        if (!wallPaintMaterial.IsKeywordEnabled("USE_YIQ"))
        {
            Debug.Log("[WallPaintEffect LOG] EnsureTexturesInitialized: Включаем USE_YIQ keyword для лучшего сохранения текстуры.");
            wallPaintMaterial.EnableKeyword("USE_YIQ");
        }
    }

    public void SetPaintColor(Color color)
    {
        paintColor = color;
        UpdatePaintParameters();
    }

    public Color GetPaintColor()
    {
        return paintColor;
    }

    public void SetBlendFactor(float factor)
    {
        blendFactor = Mathf.Clamp01(factor);
        UpdatePaintParameters();
    }

    public float GetBlendFactor()
    {
        return blendFactor;
    }

    public void SetUseMask(bool useMaskValue)
    {
        useMask = useMaskValue;
        UpdatePaintParameters();
    }

    // Метод для получения материала из WallPaintFeature
    public Material GetMaterial()
    {
        // Возвращаем текущий материал, который используется компонентом
        // Не пытаемся получить его из WallPaintFeature здесь
        if (wallPaintMaterial == null)
        {
            Debug.LogWarning("WallPaintMaterial is null в WallPaintEffect при вызове GetMaterial()");
        }
        return wallPaintMaterial;
    }

    // Проверка готовности эффекта
    public bool IsReady()
    {
        return isInitialized && wallPaintMaterial != null && (!useMask || isSegmentationReady);
    }

    // Экспериментальные методы для отладки - вызывайте их из редактора Unity для тестирования

    // Метод для принудительного обновления материала
    public void ForceUpdateMaterial()
    {
        Debug.Log("[WallPaintEffect LOG] ForceUpdateMaterial called.");

        if (wallPaintMaterial == null)
        {
            Debug.LogError("[WallPaintEffect LOG] ForceUpdateMaterial: wallPaintMaterial is null!");
            return;
        }

        // Принудительно сбрасываем счетчики для обновления маски на следующем кадре
        lastUsedMask = null;
        framesSinceLastSegmentationUpdate = 100; // Большое значение для гарантированного обновления

        // Обновляем параметры и текстуры
        EnsureTexturesInitialized();
        UpdatePaintParameters();

        // Если у нас есть доступ к WallPaintFeature, обновляем материал там тоже
        if (wallPaintFeature != null)
        {
            wallPaintFeature.SetPassMaterial(wallPaintMaterial);
            Debug.Log("[WallPaintEffect LOG] ForceUpdateMaterial: Материал обновлен в WallPaintFeature.");
        }
        else
        {
            Debug.LogWarning("[WallPaintEffect LOG] ForceUpdateMaterial: wallPaintFeature is null, не можем обновить материал в рендер-пайплайне.");
            FindAndSetupWallPaintFeature();
        }

        // Обновляем материал в ARLightEstimation
        if (lightEstimation != null && wallPaintMaterial != null)
        {
            lightEstimation.AddMaterial(wallPaintMaterial);
        }
    }

    // Метод для проверки и исправления текстур
    public void FixMaterialTextures()
    {
        if (wallPaintMaterial == null)
        {
            Debug.LogError("FixMaterialTextures: Материал не найден");
            return;
        }
        Debug.Log("[WallPaintEffect LOG] FixMaterialTextures() called.");

        // Убедимся, что текстуры правильно установлены
        EnsureTexturesInitialized();

        // Проверим, что ключевое слово USE_MASK правильно установлено
        if (isSegmentationReady && useMask)
        {
            wallPaintMaterial.EnableKeyword("USE_MASK");
            Debug.Log("Включено ключевое слово USE_MASK");
        }
        else
        {
            wallPaintMaterial.DisableKeyword("USE_MASK");
            Debug.Log("Отключено ключевое слово USE_MASK");
        }

        // Установить значения по умолчанию для безопасности
        if (wallPaintMaterial.HasProperty("_BlendFactor"))
        {
            float currentBlend = wallPaintMaterial.GetFloat("_BlendFactor");
            if (currentBlend > 0.8f)
            {
                wallPaintMaterial.SetFloat("_BlendFactor", 0.5f);
                Debug.Log("BlendFactor уменьшен до 0.5 для предотвращения непрозрачного эффекта");
            }
        }

        ForceUpdateMaterial();
    }

    // Метод для мгновенного изменения цвета и прозрачности
    public void SetColorAndOpacity(Color color, float opacity)
    {
        if (wallPaintMaterial == null) return;

        // Установить цвет
        wallPaintMaterial.SetColor("_PaintColor", color);

        // Установить прозрачность
        wallPaintMaterial.SetFloat("_BlendFactor", Mathf.Clamp01(opacity));

        // Обновить мгновенно
        ForceUpdateMaterial();

        Debug.Log($"Установлен цвет {color} и прозрачность {opacity}");
    }

    // Метод для принудительной настройки режима рендеринга
    public void FixRenderingMode()
    {
        bool needsMaterialUpdate = false;

        if (wallPaintMaterial != null)
        {
            // Включаем отладочную сетку, если свойство существует
            if (wallPaintMaterial.HasProperty("_DebugOverlay"))
            {
                if (!wallPaintMaterial.IsKeywordEnabled("DEBUG_OVERLAY"))
                {
                    wallPaintMaterial.EnableKeyword("DEBUG_OVERLAY");
                    needsMaterialUpdate = true;
                    Debug.Log("WallPaintEffect: Включен режим отладки для визуализации покрытия (DEBUG_OVERLAY)");
                }
            }
            else
            {
                Debug.LogWarning("WallPaintEffect: Материал не содержит свойство _DebugOverlay для FixRenderingMode.");
            }

            // Устанавливаем безопасный BlendFactor для отладки, если он слишком высокий
            if (wallPaintMaterial.HasProperty("_BlendFactor"))
            {
                float currentBlend = wallPaintMaterial.GetFloat("_BlendFactor");
                if (currentBlend < 0.01f || currentBlend > 0.95f) // Если почти невидимый или почти непрозрачный
                {
                    // wallPaintMaterial.SetFloat("_BlendFactor", 0.3f); // Более заметное значение для отладки
                    // needsMaterialUpdate = true;
                    // Debug.Log("WallPaintEffect: BlendFactor установлен на 0.3 для отладки");
                }
            }

            // Эта часть логики вызывала снятие галочки "Use Mask"
            // Проверяем, нужно ли включать или выключать маску на основе состояния WallSegmentation
            // Но не меняем this.useMask, чтобы настройка инспектора сохранялась.
            // Вместо этого Update() должен управлять ключевым словом USE_MASK.
            /*
            if (wallSegmentation == null || !wallSegmentation.IsModelInitialized)
            {
                // SetUseMaskMode(false); // Не вызываем, чтобы не сбрасывать галочку в инспекторе
                if (wallPaintMaterial.IsKeywordEnabled("USE_MASK"))
                {
                    wallPaintMaterial.DisableKeyword("USE_MASK");
                    needsMaterialUpdate = true;
                    Debug.Log("WallPaintEffect (FixRenderingMode): Отключено ключевое слово USE_MASK, т.к. сегментация не готова.");
                }
            }
            else
            {
                // Если сегментация готова, то USE_MASK должен управляться this.useMask (из инспектора) и логикой в Update()
                // Здесь не нужно принудительно включать, если this.useMask = false.
                // SetUseMaskMode(true); // Не вызываем, чтобы не включать, если в инспекторе снято.
                if (this.useMask && !wallPaintMaterial.IsKeywordEnabled("USE_MASK"))
                {
                     wallPaintMaterial.EnableKeyword("USE_MASK");
                     needsMaterialUpdate = true;
                     Debug.Log("WallPaintEffect (FixRenderingMode): Включено ключевое слово USE_MASK, т.к. сегментация готова и useMask=true.");
                }
            }
            */

            // Строка, которая, согласно логам (в районе ~741), приводила к вызову SetUseMaskMode(false)
            // и логу "WallPaintEffect: Отключен режим USE_MASK"
            // SetUseMaskMode(false); // Отключаем маску для чистоты эксперимента <-- ЗАКОММЕНТИРОВАНО
            // Debug.Log("WallPaintEffect: Отключен режим USE_MASK (для чистоты эксперимента) - вызов закомментирован в FixRenderingMode");


            // Вместо этого, если мы в режиме отладки и хотим видеть эффект без маски,
            // можно временно отключить ключевое слово, не меняя this.useMask
            // if (wallPaintMaterial.IsKeywordEnabled("USE_MASK"))
            // {
            //     wallPaintMaterial.DisableKeyword("USE_MASK");
            //     needsMaterialUpdate = true;
            //     Debug.Log("WallPaintEffect (FixRenderingMode): Временно отключено USE_MASK для отладочной сетки.");
            // }

        }
        else
        {
            Debug.LogWarning("WallPaintEffect: WallPaintMaterial is null in FixRenderingMode.");
            return;
        }

        if (needsMaterialUpdate)
        {
            ForceUpdateMaterial();
        }
        else
        {
            // Даже если ключевые слова не менялись, обновим на всякий случай, если другие параметры могли измениться
            ForceUpdateMaterial(); // Это может быть избыточно, но для отладки безопасно
            Debug.Log("WallPaintEffect (FixRenderingMode): Материал принудительно обновлен, даже если ключевые слова не менялись.");
        }
    }

    // Отдельный метод для включения/отключения режима использования маски
    public void SetUseMaskMode(bool useMaskValue)
    {
        if (wallPaintMaterial == null)
        {
            Debug.LogError("Cannot set mask mode: Material is null");
            return;
        }

        useMask = useMaskValue;

        if (useMaskValue)
        {
            wallPaintMaterial.EnableKeyword("USE_SEGMENTATION_MASK");
            Debug.Log("WallPaintEffect: Включен режим USE_SEGMENTATION_MASK");
        }
        else
        {
            wallPaintMaterial.DisableKeyword("USE_SEGMENTATION_MASK");
            Debug.Log("WallPaintEffect: Отключен режим USE_SEGMENTATION_MASK");
        }

        // Force material update
        ForceUpdateMaterial();
    }

    // Метод для отключения режима отладки
    public void DisableDebugMode()
    {
        if (wallPaintMaterial == null)
        {
            Debug.LogError("Cannot disable debug mode: Material is null");
            return;
        }

        // Disable debug overlay
        if (wallPaintMaterial.HasProperty("_DebugOverlay"))
        {
            wallPaintMaterial.DisableKeyword("DEBUG_OVERLAY");
        }

        // Force material update
        ForceUpdateMaterial();
    }

    // Добавляем метод для создания компонента ARLightEstimation, если он отсутствует
    private void TrySetupLightEstimation()
    {
        if (lightEstimation != null)
            return;

        // Ищем ARCameraManager с явным указанием типа из ARFoundation
        UnityEngine.XR.ARFoundation.ARCameraManager cameraManager = FindObjectOfType<UnityEngine.XR.ARFoundation.ARCameraManager>();
        if (cameraManager != null)
        {
            // Проверяем, есть ли уже ARLightEstimation на этом объекте
            lightEstimation = cameraManager.GetComponent<ARLightEstimation>();

            // Если нет, то создаем
            if (lightEstimation == null)
            {
                lightEstimation = cameraManager.gameObject.AddComponent<ARLightEstimation>();
                Debug.Log("[WallPaintEffect LOG] Автоматически добавлен компонент ARLightEstimation к камере AR");
            }

            // Добавляем материал в список для обновления освещения
            if (wallPaintMaterial != null)
            {
                lightEstimation.AddMaterial(wallPaintMaterial);
            }
        }
    }

    // Метод для включения/отключения привязки к AR плоскостям
    public void SetAttachToARPlanes(bool attach)
    {
        if (attachToARPlanes == attach) return;

        attachToARPlanes = attach;

        if (attach)
        {
            // Если функция включается, находим менеджер плоскостей и подписываемся на события
            if (planeManager == null)
            {
                planeManager = FindObjectOfType<ARPlaneManager>();
            }

            if (planeManager != null)
            {
                planeManager.planesChanged += OnPlanesChanged;
                // Применяем материал к существующим плоскостям
                ApplyMaterialToARPlanes();
            }
        }
        else
        {
            // Если функция отключается, отписываемся от событий
            if (planeManager != null)
            {
                planeManager.planesChanged -= OnPlanesChanged;
            }
        }
    }

    private void OnDestroy()
    {
        // Отписываемся от событий при уничтожении объекта
        if (planeManager != null)
        {
            planeManager.planesChanged -= OnPlanesChanged;
        }
    }

    // Debug function that can be called from the Unity Editor to test plane visualization
    [ContextMenu("Create Test Plane At Camera")]
    private void CreateTestPlaneAtCamera()
    {
        if (mainCamera == null) mainCamera = GetComponent<Camera>();
        if (mainCamera == null)
        {
            Debug.LogError("Main camera not found!");
            return;
        }

        // Find ARPlaneManager
        ARPlaneManager planeManager = FindObjectOfType<ARPlaneManager>();
        if (planeManager == null || planeManager.planePrefab == null)
        {
            Debug.LogError("ARPlaneManager or planePrefab not found!");
            return;
        }

        // Create a new GameObject with the same prefab as AR planes
        GameObject testPlaneObj = Instantiate(planeManager.planePrefab);
        testPlaneObj.name = "TestPlane_" + System.DateTime.Now.Ticks;

        // Position it 2 meters in front of the camera
        Vector3 cameraPosition = mainCamera.transform.position;
        Vector3 cameraForward = mainCamera.transform.forward;
        testPlaneObj.transform.position = cameraPosition + cameraForward * 2f;

        // Rotate it to face the camera
        testPlaneObj.transform.rotation = Quaternion.LookRotation(-cameraForward);

        // Set a reasonable size
        testPlaneObj.transform.localScale = new Vector3(2f, 2f, 1f);

        // Add a mesh if it doesn't have one
        MeshFilter meshFilter = testPlaneObj.GetComponent<MeshFilter>();
        if (meshFilter == null)
        {
            meshFilter = testPlaneObj.AddComponent<MeshFilter>();
        }

        if (meshFilter.sharedMesh == null)
        {
            // Create a simple quad mesh
            Mesh quadMesh = new Mesh();
            quadMesh.vertices = new Vector3[] {
                new Vector3(-0.5f, -0.5f, 0),
                new Vector3(0.5f, -0.5f, 0),
                new Vector3(0.5f, 0.5f, 0),
                new Vector3(-0.5f, 0.5f, 0)
            };
            quadMesh.triangles = new int[] { 0, 1, 2, 0, 2, 3 };
            quadMesh.uv = new Vector2[] {
                new Vector2(0, 0),
                new Vector2(1, 0),
                new Vector2(1, 1),
                new Vector2(0, 1)
            };
            quadMesh.RecalculateNormals();
            meshFilter.sharedMesh = quadMesh;
        }

        // Ensure it has a MeshRenderer
        MeshRenderer renderer = testPlaneObj.GetComponent<MeshRenderer>();
        if (renderer == null)
        {
            renderer = testPlaneObj.AddComponent<MeshRenderer>();
        }

        // Apply material to the test plane
        Material testMaterial = new Material(wallPaintMaterial);
        testMaterial.SetColor("_PaintColor", Color.green);
        testMaterial.SetFloat("_BlendFactor", 0.7f);
        renderer.material = testMaterial;

        Debug.Log("Created test plane at camera position + 2m forward");

        // Add test plane to simulated ARPlane list for processing
        ARPlane testARPlane = testPlaneObj.GetComponent<ARPlane>();
        if (testARPlane != null && !processedPlanes.Contains(testARPlane))
        {
            processedPlanes.Add(testARPlane);
        }
    }

    // Добавляем публичный метод для включения режима отладки
    public void EnableDebugMode()
    {
        Debug.Log("WallPaintEffect: Включаем режим отладки");

        // Включаем отображение отладочной сетки
        if (wallPaintMaterial != null)
        {
            wallPaintMaterial.EnableKeyword("DEBUG_OVERLAY");
            wallPaintMaterial.SetFloat("_DebugGridSize", 10f);
        }

        // Увеличиваем контрастность материала для лучшей видимости
        SetBlendFactor(0.9f);

        // Применяем яркий цвет для отладки
        SetPaintColor(Color.green);

        // Принудительно пересоздаем тестовую плоскость
        CreateTestPlaneAtCamera();

        // Обновляем материалы на всех AR плоскостях
        ARPlane[] planes = FindObjectsOfType<ARPlane>();
        foreach (ARPlane plane in planes)
        {
            ApplyMaterialToPlane(plane);
        }

        // Форсированно обновляем материал
        ForceUpdateMaterial();

        Debug.Log("WallPaintEffect: Режим отладки включен. Тестовая плоскость создана.");
    }
}