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

    private WallPaintFeature wallPaintFeature;
    private bool isInitialized = false;
    private bool isSegmentationReady = false;
    private bool shouldInitialize = false; // Flag to track when AR session is ready

    private void Start()
    {
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
    }

    // Основной метод инициализации, вынесенный для повторного использования
    private void InitializeComponents()
    {
        if (isInitialized)
        {
            return; // Избегаем повторной инициализации
        }

        Debug.Log("WallPaintEffect: Начало инициализации компонентов");

        if (wallPaintMaterial == null)
        {
            // Попробуем найти материал в папке Materials
            wallPaintMaterial = Resources.Load<Material>("Materials/WallPaint");

            if (wallPaintMaterial == null)
            {
                Debug.LogError("Wall Paint Material is not assigned and not found in Resources. Disabling component.");
                enabled = false;
                return;
            }
            else
            {
                Debug.Log("WallPaintEffect: Материал успешно загружен из Resources");
            }
        }

        // Создаем копию материала, чтобы не изменять общий шаблон
        wallPaintMaterial = new Material(wallPaintMaterial);

        // Проверяем, что шейдер имеет свойство _SegmentationMask
        if (!wallPaintMaterial.HasProperty("_SegmentationMask"))
        {
            Debug.LogError("WallPaint material doesn't have _SegmentationMask property. Make sure the shader is correct.");

            // Проверяем шейдер
            Shader shader = wallPaintMaterial.shader;
            if (shader != null)
            {
                Debug.LogError("Текущий шейдер: " + shader.name);
            }

            TryRecreateWallPaintMaterial();

            if (!wallPaintMaterial.HasProperty("_SegmentationMask"))
            {
                enabled = false;
                return;
            }
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

    // Метод для попытки пересоздания материала стены
    private void TryRecreateWallPaintMaterial()
    {
        // Попробуем создать новый материал с правильным шейдером
        Shader wallPaintShader = Shader.Find("Custom/WallPaint");
        if (wallPaintShader != null)
        {
            wallPaintMaterial = new Material(wallPaintShader);
            wallPaintMaterial.SetColor("_PaintColor", paintColor);
            wallPaintMaterial.SetFloat("_BlendFactor", blendFactor);

            // Если у нас есть WallPaintFeature, обновляем материал там
            if (wallPaintFeature != null)
            {
                wallPaintFeature.SetPassMaterial(wallPaintMaterial);
                Debug.Log("Wall paint material recreated successfully");
            }
        }
        else
        {
            Debug.LogError("Cannot find shader 'Custom/WallPaint'. Make sure it exists in the project.");
        }
    }

    private void UpdatePaintParameters()
    {
        if (wallPaintMaterial != null)
        {
            // Применяем текущие настройки к материалу
            wallPaintMaterial.SetColor("_PaintColor", paintColor);
            wallPaintMaterial.SetFloat("_BlendFactor", blendFactor);

            // Применяем или отключаем маску
            if (isSegmentationReady && useMask)
            {
                wallPaintMaterial.SetTexture("_SegmentationMask", wallSegmentation.segmentationMaskTexture);
                wallPaintMaterial.EnableKeyword("USE_MASK");
            }
            else
            {
                wallPaintMaterial.DisableKeyword("USE_MASK");
            }
        }

        if (wallPaintFeature != null && !ReferenceEquals(wallPaintFeature.passMaterial, wallPaintMaterial))
        {
            // Обновляем материал в фиче
            wallPaintFeature.SetPassMaterial(wallPaintMaterial);
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
        if (wallPaintMaterial == null)
        {
            Debug.LogWarning("Wall Paint Material is null in WallPaintEffect");
        }
        return wallPaintMaterial;
    }

    // Проверка готовности эффекта
    public bool IsReady()
    {
        return isInitialized && wallPaintMaterial != null && (!useMask || isSegmentationReady);
    }
}