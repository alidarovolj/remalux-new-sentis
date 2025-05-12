using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Reflection;
using System.Collections.Generic;
using System.Collections;
using System.Linq;

[RequireComponent(typeof(Camera))]
public class WallPaintEffect : MonoBehaviour
{
    [SerializeField] private Material wallPaintMaterial;
    [SerializeField] private WallSegmentation wallSegmentation;
    [SerializeField] private Color paintColor = Color.white;
    [SerializeField] private float blendFactor = 0.5f;
    [SerializeField] private bool useMask = true;
    [SerializeField] private ARSessionManager arSessionManager; // Ссылка на ARSessionManager

    private Camera mainCamera; // Added missing camera field
    private WallPaintFeature wallPaintFeature;
    private bool isInitialized = false;
    private bool isSegmentationReady = false;
    private bool shouldInitialize = false; // Flag to track when AR session is ready

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

        // Проверяем, что у нас есть сегментация и она готова
        if (wallSegmentation != null)
        {
            if (!isSegmentationReady && wallSegmentation.IsModelInitialized)
            {
                // Сегментация только что стала доступна
                isSegmentationReady = true;
                Debug.Log("WallPaintEffect: Сегментация стен готова к использованию");

                // Получаем текстуру из сегментации и устанавливаем как параметр материала
                if (wallSegmentation.segmentationMaskTexture != null && wallPaintMaterial != null)
                {
                    // Устанавливаем маску сегментации в материал
                    wallPaintMaterial.SetTexture("_SegmentationMask", wallSegmentation.segmentationMaskTexture);

                    // Log для отладки
                    Debug.Log($"WallPaintEffect: Установлена маска сегментации размером {wallSegmentation.segmentationMaskTexture.width}x{wallSegmentation.segmentationMaskTexture.height}");

                    // Проверяем, что материал доступен в WallPaintFeature
                    if (wallPaintFeature != null && !ReferenceEquals(wallPaintFeature.passMaterial, wallPaintMaterial))
                    {
                        wallPaintFeature.SetPassMaterial(wallPaintMaterial);
                    }
                }
                else
                {
                    Debug.LogError("WallPaintEffect: Маска сегментации недоступна, хотя сегментация готова");
                }
            }
            else if (isSegmentationReady)
            {
                // Сегментация уже была готова, проверяем, что маска обновлена в материале
                if (wallSegmentation.segmentationMaskTexture != null && wallPaintMaterial != null &&
                    wallPaintMaterial.GetTexture("_SegmentationMask") != wallSegmentation.segmentationMaskTexture)
                {
                    // Обновляем маску
                    wallPaintMaterial.SetTexture("_SegmentationMask", wallSegmentation.segmentationMaskTexture);

                    // Обновляем материал в WallPaintFeature
                    if (wallPaintFeature != null)
                    {
                        wallPaintFeature.SetPassMaterial(wallPaintMaterial);
                    }
                }
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
                wallPaintMaterial.EnableKeyword("USE_MASK");
            }
            else
            {
                Debug.LogWarning("[WallPaintEffect LOG] UpdatePaintParameters: USE_MASK is true, but segmentation mask is NULL. Disabling keyword.");
                wallPaintMaterial.DisableKeyword("USE_MASK");
            }
        }
        else
        {
            Debug.Log("[WallPaintEffect LOG] UpdatePaintParameters: USE_MASK is false. Disabling keyword.");
            wallPaintMaterial.DisableKeyword("USE_MASK");
        }

        // Обновляем материал в WallPaintFeature, если он уже найден
        if (wallPaintFeature != null)
        {
             // Дополнительная проверка перед установкой
            if (wallPaintFeature.passMaterial != wallPaintMaterial)
            {
                Debug.Log($"[WallPaintEffect LOG] UpdatePaintParameters: Обновляем материал в WallPaintFeature. Текущий: {wallPaintFeature.passMaterial?.name}, Новый: {wallPaintMaterial?.name}");
                wallPaintFeature.SetPassMaterial(wallPaintMaterial);
            }
             else {
                // Debug.Log($"[WallPaintEffect LOG] UpdatePaintParameters: Материал в WallPaintFeature уже {wallPaintMaterial?.name}. Пропуск SetPassMaterial.");
             }
        }
        else
        {
            Debug.LogWarning("[WallPaintEffect LOG] UpdatePaintParameters: wallPaintFeature is NULL. Cannot set pass material yet.");
            // Попробуем найти фичу еще раз на всякий случай
            FindAndSetupWallPaintFeature();
             if (wallPaintFeature != null) {
                 Debug.Log("[WallPaintEffect LOG] UpdatePaintParameters: WallPaintFeature найден повторно. Устанавливаем материал.");
                 wallPaintFeature.SetPassMaterial(wallPaintMaterial);
             }
        }
    }

    // New method to ensure textures are properly initialized
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
        if (useMask && wallPaintMaterial.GetTexture("_SegmentationMask") == null)
        {
            if (wallSegmentation != null && wallSegmentation.segmentationMaskTexture != null)
            {
                Debug.Log("[WallPaintEffect LOG] EnsureTexturesInitialized: useMask is true, _SegmentationMask is null. Assigning from wallSegmentation.");
                wallPaintMaterial.SetTexture("_SegmentationMask", wallSegmentation.segmentationMaskTexture);
            }
            else
            {
                Debug.LogWarning("[WallPaintEffect LOG] EnsureTexturesInitialized: useMask is true, _SegmentationMask is null, AND wallSegmentation/mask is also null. Setting default black transparent texture.");
                wallPaintMaterial.SetTexture("_SegmentationMask", Texture2D.blackTexture); // Default to a black (transparent if shader handles alpha correctly)
            }
        }
        else if (!useMask && wallPaintMaterial.IsKeywordEnabled("USE_MASK"))
        {
            Debug.Log("[WallPaintEffect LOG] EnsureTexturesInitialized: useMask is false, but USE_MASK keyword is enabled. Disabling and clearing texture.");
            wallPaintMaterial.DisableKeyword("USE_MASK");
            // wallPaintMaterial.SetTexture("_SegmentationMask", null); // Optionally clear
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
        if (!isInitialized)
        {
            Debug.LogWarning("WallPaintEffect не инициализирован, нельзя обновить материал");
            return;
        }

        // Обновить существующий материал и его параметры
        UpdatePaintParameters();

        // Пересоздать связь с WallPaintFeature
        if (wallPaintFeature != null && wallPaintMaterial != null)
        {
            wallPaintFeature.SetPassMaterial(wallPaintMaterial);
            Debug.Log("Материал принудительно обновлен в WallPaintFeature");
        }
        else
        {
            Debug.LogWarning("WallPaintFeature недоступен, обновление невозможно");
        }

        DebugSegmentationStatus();
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
            wallPaintMaterial.EnableKeyword("USE_MASK");
            Debug.Log("WallPaintEffect: Включен режим USE_MASK");
        }
        else
        {
            wallPaintMaterial.DisableKeyword("USE_MASK");
            Debug.Log("WallPaintEffect: Отключен режим USE_MASK");
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
}