// #define REAL_MASK_PROCESSING_DISABLED // Закомментируйте эту строку, чтобы включить реальную обработку

using UnityEngine;
using Unity.Sentis;
using System.Collections;
using System.Collections.Generic; // <--- Убедитесь, что это есть
using System.IO;
using System.Linq;
using System;
using System.Reflection;          // <--- Убедитесь, что это есть
using System.Text;              // <--- Убедитесь, что это есть
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;  // <--- Убедитесь, что это есть
using UnityEngine.XR.Management;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe; // <--- Убедитесь, что это есть
using Unity.XR.CoreUtils;           // <--- Убедитесь, что это есть

// Если используете другие пакеты рендеринга, их using директивы тоже должны быть здесь
// using UnityEngine.Rendering;
// using UnityEngine.Rendering.Universal;

/// <summary>
/// Компонент для сегментации стен с использованием ML модели в Unity Sentis.
/// Обновлен для безопасной загрузки моделей, предотвращающей краш Unity.
/// </summary>
public class WallSegmentation : MonoBehaviour
{
    [Header("Настройки ML модели")]
    [Tooltip("Ссылка на ML модель в формате ONNX или Sentis")]
    public UnityEngine.Object modelAsset;

    [NonSerialized]
    public string modelFilePath;

    [Tooltip("Предпочитаемый бэкенд для исполнения модели (0 = CPU, 1 = GPUCompute)")]
    public int preferredBackend = 0;

    [Tooltip("Использовать безопасную асинхронную загрузку модели")]
    public bool useSafeModelLoading = true;

    [Tooltip("Тайм-аут загрузки модели в секундах")]
    public float modelLoadTimeout = 30f;

    [Tooltip("Принудительно использовать метод захвата изображения для XR Simulation")]
    public bool forceXRSimulationCapture = true;

    [Header("Настройки Постобработки Маски")]
    [Tooltip("Включить общую постобработку маски (включая размытие, резкость, контраст, морфологию)")]
    [SerializeField] private bool enablePostProcessing = true;
    [Tooltip("Включить Гауссово размытие для сглаживания маски")]
    [SerializeField] private bool enableGaussianBlur = true;
    [Tooltip("Материал для Гауссова размытия")]
    [SerializeField] private Material gaussianBlurMaterial;
    [Tooltip("Размер ядра Гауссова размытия (в пикселях)")]
    [SerializeField, Range(1, 10)] private int blurSize = 3;
    [Tooltip("Включить повышение резкости краев маски")]
    [SerializeField] private bool enableSharpen = true;
    [Tooltip("Материал для повышения резкости")]
    [SerializeField] private Material sharpenMaterial;
    [Tooltip("Включить повышение контраста маски")]
    [SerializeField] private bool enableContrast = true; // Это поле управляет ВКЛ/ВЫКЛ контраста
    [Tooltip("Материал для повышения контраста")]
    [SerializeField] private Material contrastMaterial;
    // [Tooltip("Множитель контраста для постобработки")] // ЭТА СТРОКА И СЛЕДУЮЩАЯ БУДУТ УДАЛЕНЫ
    // [SerializeField, Range(0.1f, 5.0f)] private float contrastFactor = 1.0f; // ЭТА СТРОКА БУДЕТ УДАЛЕНА

    [Header("Настройки сегментации")]
    [Tooltip("Индекс класса стены в модели")][SerializeField] private int wallClassIndex = 1;     // Стена (ИЗМЕНЕНО для segformer-b4-wall)
    [Tooltip("Индекс класса пола в модели")][SerializeField] private int floorClassIndex = 2; // Пол (ИЗМЕНЕНО для segformer-b4-wall, если есть, иначе -1)
    [Tooltip("Порог вероятности для определения пола")][SerializeField, Range(0.01f, 1.0f)] private float floorConfidence = 0.15f; // ИСПРАВЛЕНО: повышен для консистентности
    [Tooltip("Обнаруживать также горизонтальные поверхности (пол)")] public bool detectFloor = false;

    [Header("Настройки качества и производительности")]
    [Tooltip("Целевое разрешение для обработки (ширина, высота)")]
    public Vector2Int inputResolution = new Vector2Int(640, 480);

    [Tooltip("Автоматически оптимизировать разрешение на основе производительности")]
    public bool adaptiveResolution = true;

    [Tooltip("Шаг изменения разрешения для адаптивного режима")] // Новое поле
    public Vector2Int resolutionStep = new Vector2Int(64, 48); // Новое поле

    [Tooltip("Максимальное разрешение для высокого качества")]
    public Vector2Int maxResolution = new Vector2Int(768, 768);

    [Tooltip("Минимальное разрешение для производительности")]
    public Vector2Int minResolution = new Vector2Int(384, 384);

    [Tooltip("Целевое время обработки в миллисекундах (для адаптивного разрешения)")]
    [Range(16f, 100f)]
    public float targetProcessingTimeMs = 50f;

    [Tooltip("Фактор качества маски (0-1), влияет на выбор разрешения")]
    [Range(0.1f, 1.0f)]
    public float qualityFactor = 0.7f;

    [Header("Ограничение частоты инференса")]
    [Tooltip("Максимальная частота выполнения сегментации (FPS). 0 = без ограничений")]
    [Range(0f, 60f)]
    public float maxSegmentationFPS = 15f;

    [Header("Temporal Interpolation (Временная интерполяция)")]
    [Tooltip("Включить плавную интерполяцию маски между инференсами для избежания мерцания")]
    public bool enableTemporalInterpolation = true;

    [Tooltip("Скорость интерполяции маски (1.0 = мгновенное обновление, 0.1 = плавное)")]
    [Range(0.1f, 1.0f)]
    public float maskInterpolationSpeed = 0.6f;

    [Tooltip("Использовать экспоненциальное сглаживание для более естественной интерполяции")]
    public bool useExponentialSmoothing = true;

    [Tooltip("Максимальное время показа старой маски без нового инференса (сек)")]
    [Range(1f, 10f)]
    public float maxMaskAgeSeconds = 3f;

    [Tooltip("Материал для временной интерполяции маски")] // Новое поле
    [SerializeField] private Material temporalBlendMaterial; // Новое поле

    [Tooltip("Использовать симуляцию, если не удаётся получить изображение с камеры")]
    public bool useSimulationIfNoCamera = true;

    [Tooltip("Количество неудачных попыток получения изображения перед включением симуляции")]
    public int failureThresholdForSimulation = 10;

    [Header("Компоненты")]
    [Tooltip("Ссылка на ARSessionManager")]
    public ARSessionManager arSessionManager;

    [Tooltip("Ссылка на XROrigin")] public XROrigin xrOrigin = null;

    [Tooltip("Текстура для вывода маски сегментации")] public RenderTexture segmentationMaskTexture;

    [Tooltip("Порог вероятности для определения стены")]
    public float segmentationConfidenceThreshold = 0.15f; // ИСПРАВЛЕНО: повышен с 0.01f до 0.15f для лучшего качества сегментации

    [Tooltip("Порог вероятности для определения пола")]
    public float floorConfidenceThreshold = 0.15f;    // ИСПРАВЛЕНО: повышен для консистентности

    [Tooltip("Путь к файлу модели (.sentis или .onnx) в StreamingAssets")] public string modelPath = "";

    [Tooltip("Предпочитаемый бэкенд для исполнения модели (0 = CPU, 1 = GPUCompute)")]
    public int selectedBackend = 0; // 0 = CPU, 1 = GPUCompute (через BackendType)

    [Header("Настройки материалов и отладки маски")] // Новый заголовок для инспектора
    [SerializeField]
    [Tooltip("Материал, используемый для преобразования выхода модели в маску сегментации.")]
    private Material segmentationMaterial; // Добавлено поле

    [Tooltip("Множитель контраста")]
    [SerializeField, Range(0.1f, 5.0f)] private float contrastFactor = 1.0f;

    [Header("Настройки морфологических операций")]
    [Tooltip("Включить морфологическое закрытие (Dilate -> Erode) для удаления мелких дыр")]
    [SerializeField] private bool enableMorphologicalClosing = false;
    [Tooltip("Включить морфологическое открытие (Erode -> Dilate) для удаления мелкого шума")]
    [SerializeField] private bool enableMorphologicalOpening = false;
    [Tooltip("Материал для операции расширения (Dilate)")]
    [SerializeField] private Material dilateMaterial;
    [Tooltip("Материал для операции сужения (Erode)")]
    [SerializeField] private Material erodeMaterial;

    [Header("Настройки пороговой обработки уверенности")]
    [Tooltip("Верхний порог вероятности для определения стены (используется как основной или высокий порог для гистерезиса)")]
    [SerializeField] private float wallConfidenceInternal = 0.15f; // Переименовано для создания публичного свойства
    public float WallConfidence { get { return wallConfidenceInternal; } } // Добавлено публичное свойство
    [Tooltip("Включить гистерезисную пороговую обработку (использует верхний и нижний пороги)")]
    [SerializeField] private bool enableHysteresisThresholding = false;
    [Tooltip("Нижний порог вероятности для гистерезисной обработки (должен быть меньше wallConfidence)")]
    [SerializeField, Range(0.0001f, 1.0f)] private float lowWallConfidence = 0.05f;

    [Header("GPU Optimization (Оптимизация GPU)")]
    [Tooltip("Compute Shader для обработки масок на GPU")]
    public ComputeShader segmentationProcessor;

    [Tooltip("Использовать GPU для анализа качества маски (намного быстрее)")]
    public bool useGPUQualityAnalysis = true;

    [Tooltip("Размер downsampling для анализа качества (больше = быстрее, меньше точности)")]
    [Range(2, 16)]
    public int qualityAnalysisDownsample = 8;

    [Tooltip("Использовать GPU для всей постобработки (максимальная производительность)")]
    public bool useGPUPostProcessing = true;

    [Tooltip("Использовать комплексное ядро постобработки (все в одном проходе)")]
    public bool useComprehensiveGPUProcessing = true;

    [Header("Performance Profiling (Профилирование производительности)")]
    [Tooltip("Включить встроенное профилирование производительности")]
    public bool enablePerformanceProfiling = true;

    [Tooltip("Интервал логирования статистики производительности (секунды)")]
    [Range(1f, 30f)]
    public float profilingLogInterval = 5f;

    [Tooltip("Показывать детальную статистику в логах")]
    public bool showDetailedProfiling = false;

    [Header("Advanced Memory Management (Продвинутое управление памятью)")]
    [Tooltip("Включить систему детекции утечек памяти")]
    public bool enableMemoryLeakDetection = true;

    [Tooltip("Максимальный размер пула текстур (MB) перед принудительной очисткой")]
    [Range(50, 500)]
    public int maxTexturePoolSizeMB = 200;

    [Tooltip("Интервал проверки памяти (секунды)")]
    [Range(5, 60)]
    public int memoryCheckIntervalSeconds = 15;

    [Tooltip("Автоматически очищать неиспользуемые ресурсы")]
    public bool enableAutomaticCleanup = true;

    [Tooltip("Включить продвинутое управление памятью")]
    public bool enableAdvancedMemoryManagement = true;

    [Tooltip("Интервал проверки памяти (секунды)")]
    public int memoryCheckInterval = 15;

    [Tooltip("Интервал логирования статистики производительности (секунды)")]
    public float performanceLogInterval = 5f;

    [Tooltip("Включить детальную отладку")]
    public bool enableDetailedDebug = false;

    [Tooltip("Флаги отладки")]
    public DebugFlags debugFlags = DebugFlags.None;

    // Свойства для получения AR компонентов
    public ARSessionManager ARSessionManager
    {
        get
        {
            if (arSessionManager == null)
            {
                arSessionManager = FindObjectOfType<ARSessionManager>();
            }
            return arSessionManager;
        }
        set
        {
            arSessionManager = value;
        }
    }

    public XROrigin XROrigin
    {
        get
        {
            if (xrOrigin == null)
            {
                xrOrigin = FindObjectOfType<XROrigin>();
            }
            return xrOrigin;
        }
        set
        {
            xrOrigin = value;
        }
    }

    public ARCameraManager ARCameraManager
    {
        get
        {
            if (XROrigin != null && XROrigin.Camera != null)
            {
                return XROrigin.Camera.GetComponent<ARCameraManager>();
            }
            return FindObjectOfType<ARCameraManager>();
        }
        set
        {
            // Тут мы можем сохранить ссылку на ARCameraManager, если нужно
            // Но так как в getter мы его получаем динамически, создадим приватное поле
            arCameraManager = value;
        }
    }

    [Header("Отладка")]
    [Tooltip("Включить режим отладки с выводом логов")]
    public bool debugMode = false;

    [System.Flags]
    public enum DebugFlags
    {
        None = 0,
        Initialization = 1 << 0,
        ExecutionFlow = 1 << 1,
        TensorProcessing = 1 << 2,
        TensorAnalysis = 1 << 3, // Для вывода содержимого тензора
        CameraTexture = 1 << 4,  // Для отладки получения текстуры с камеры
        PlaneGeneration = 1 << 5, // Для отладки создания плоскостей в ARManagerInitializer
        DetailedExecution = 1 << 6, // Более детальные логи выполнения модели
        DetailedTensor = 1 << 7, // Более детальные логи обработки тензора
        Performance = 1 << 8    // Для отладки производительности и времени обработки
    }
    [Tooltip("Флаги для детальной отладки различных частей системы")]
    public DebugFlags debugLevel = DebugFlags.None;

    [Tooltip("Сохранять отладочные маски в указанный путь")] // Добавлено
    public bool saveDebugMasks = false;

    // private bool isProcessing = false; // Флаг, показывающий, что идет обработка сегментации // Закомментировано из-за CS0414

    [Tooltip("Использовать симуляцию сегментации, если не удалось получить изображение с камеры")]
    public bool useSimulatedSegmentationFallback = true; // Переименовано для ясности

    [Tooltip("Счетчик неудачных попыток получения изображения перед активацией симуляции")]
    public int simulationFallbackThreshold = 10;

    private Worker engine; // TODO: Review usage, might be legacy or Sentis internal
    private Model runtimeModel;
    private Worker worker; // Sentis Worker
    private Texture2D cameraTexture;    // Текстура для захвата изображения с камеры
    private bool isModelInitialized = false; // Флаг, что модель успешно инициализирована
    private bool isInitializing = false;     // Флаг, что идет процесс инициализации модели
    private string lastErrorMessage = null;  // Последнее сообщение об ошибке при инициализации
    private bool isInitializationFailed = false; // Флаг, что инициализация модели провалилась
    // private int consecutiveFailCount = 0; // Закомментировано из-за CS0414
    // private bool usingSimulatedSegmentation = false; // Закомментировано из-за CS0414

    [System.NonSerialized]
    private int sentisModelWidth = 512; // Значения по умолчанию, обновятся из модели
    [System.NonSerialized]
    private int sentisModelHeight = 512;
    // private int debugMaskCounter = 0; // Закомментировано из-за CS0414

    // События для уведомления о состоянии модели и обновлении маски
    public delegate void ModelInitializedHandler(); // Раскомментировано
    public event ModelInitializedHandler OnModelInitialized; // Раскомментировано

    public delegate void SegmentationMaskUpdatedHandler(RenderTexture mask); // Раскомментировано
    public event SegmentationMaskUpdatedHandler OnSegmentationMaskUpdated; // Раскомментировано

    // Свойства для доступа к состоянию инициализации модели
    public bool IsModelInitialized { get { return isModelInitialized; } private set { isModelInitialized = value; } } // Добавлено свойство
    private Model model; // Sentis Model object

    // AR Components
    private ARCameraManager arCameraManager;
    // private ARPlaneManager arPlaneManager; // Если потребуется для контекста

    private RenderTexture lastSuccessfulMask; // Последняя успешно полученная и обработанная маска
    // private bool hasValidMask = false; // Закомментировано из-за CS0414
    // private float lastValidMaskTime = 0f; // Закомментировано из-за CS0414
    // private int stableFrameCount = 0; // Закомментировано из-за CS0414
    private const int REQUIRED_STABLE_FRAMES = 2; // Уменьшено с 3 до 2 для более быстрой реакции

    // Параметры сглаживания маски для улучшения визуального качества
    [Header("Настройки качества маски")]
    [Tooltip("Применять сглаживание к маске сегментации")]
    public bool applyMaskSmoothing = true; // ПРОВЕРЕНО: должно быть включено для устранения зазубренных краев
    [Tooltip("Значение размытия для сглаживания маски (в пикселях)")]
    [Range(1, 10)]
    public int maskBlurSize = 4; // ИСПРАВЛЕНО: установлен в оптимальное значение (3-5) для лучшего сглаживания
    [Tooltip("Повышать резкость краев на маске")]
    public bool enhanceEdges = true; // ПРОВЕРЕНО: уже включено согласно анализу
    [Tooltip("Повышать контраст маски")]
    public bool enhanceContrast = true; // ПРОВЕРЕНО: уже включено согласно анализу
    // [Tooltip("Множитель контраста для постобработки")] // ЭТА СТРОКА И СЛЕДУЮЩАЯ БУДУТ УДАЛЕНЫ
    // [SerializeField, Range(0.1f, 5.0f)] private float contrastFactor = 1.0f; // ЭТА СТРОКА БУДЕТ УДАЛЕНА

    // Добавляем оптимизированный пул текстур для уменьшения аллокаций памяти
    private class TexturePool
    {
        private Dictionary<Vector2Int, List<RenderTexture>> availableTextures = new Dictionary<Vector2Int, List<RenderTexture>>();
        private Dictionary<Vector2Int, List<RenderTexture>> inUseTextures = new Dictionary<Vector2Int, List<RenderTexture>>();
        private Dictionary<int, Vector2Int> textureToSize = new Dictionary<int, Vector2Int>();
        private RenderTextureFormat defaultFormat;

        public TexturePool(RenderTextureFormat format = RenderTextureFormat.ARGB32)
        {
            defaultFormat = format;
        }

        public RenderTexture GetTexture(int width, int height, RenderTextureFormat format = RenderTextureFormat.ARGB32)
        {
            RenderTextureFormat textureFormat = format != RenderTextureFormat.ARGB32 ? format : defaultFormat;
            Vector2Int size = new Vector2Int(width, height);

            if (availableTextures.ContainsKey(size) && availableTextures[size].Count > 0)
            {
                RenderTexture texture = availableTextures[size][0];
                availableTextures[size].RemoveAt(0);

                if (!inUseTextures.ContainsKey(size))
                {
                    inUseTextures[size] = new List<RenderTexture>();
                }

                inUseTextures[size].Add(texture);
                textureToSize[texture.GetInstanceID()] = size;
                return texture;
            }

            RenderTexture newTexture = new RenderTexture(width, height, 0, textureFormat);
            newTexture.enableRandomWrite = true;
            newTexture.Create();

            if (!inUseTextures.ContainsKey(size))
            {
                inUseTextures[size] = new List<RenderTexture>();
            }

            inUseTextures[size].Add(newTexture);
            textureToSize[newTexture.GetInstanceID()] = size;
            return newTexture;
        }

        public void ReleaseTexture(RenderTexture texture)
        {
            if (texture == null) return;

            int instanceId = texture.GetInstanceID();
            if (!textureToSize.TryGetValue(instanceId, out Vector2Int sizeKey))
            {
                // This texture was not from our pool or already fully released
                if (texture.IsCreated()) // Check if it's a valid RT
                {
                    // Attempt to release it directly if it's not from the pool but still exists
                    Debug.LogWarning($"[TexturePool] Attempting to release an unpooled or already released RenderTexture: {texture.name} (ID: {instanceId}). Forcing release.");
                    texture.Release();
                    UnityEngine.Object.DestroyImmediate(texture, true); // Allow destroying assets
                }
                return;
            }

            if (inUseTextures.TryGetValue(sizeKey, out List<RenderTexture> usedList) && usedList.Contains(texture))
            {
                usedList.Remove(texture);
                if (!availableTextures.TryGetValue(sizeKey, out List<RenderTexture> availableList))
                {
                    availableList = new List<RenderTexture>();
                    availableTextures[sizeKey] = availableList;
                }
                availableList.Add(texture); // Add back to available instead of destroying immediately
                // Debug.Log($"[TexturePool] Released texture {texture.name} (ID: {instanceId}) to available pool for size {sizeKey}. Available: {availableList.Count}");
            }
            else
            {
                // If it's not in "inUse" but we have a sizeKey, it might be an anomaly or already in available.
                // To be safe, if it's not in available, let's try to release it properly.
                bool wasInAvailable = false;
                if (availableTextures.TryGetValue(sizeKey, out List<RenderTexture> currentAvailableList))
                {
                    if (currentAvailableList.Contains(texture))
                    {
                        wasInAvailable = true; // It's already in the available pool, do nothing more.
                    }
                }

                if (!wasInAvailable)
                {
                    // This case implies it was known to the pool (had a sizeKey) but wasn't in 'inUse' or 'available'.
                    // This could happen if ReleaseAllCreatedTextures was called, which destroys them.
                    // Or if it's a texture that was Get'ed but then ReleaseTexture(rt) called before rt was returned to pool via ReleaseAll.
                    // The original code had RenderTexture.ReleaseTemporary(texture); here, which is wrong for non-temporary.
                    // We should only destroy it if it's still valid and created.
                    if (texture.IsCreated())
                    {
                        Debug.LogWarning($"[TexturePool] Releasing a known texture (ID: {instanceId}, Size: {sizeKey.x}x{sizeKey.y}) that was not in 'inUse' or 'available'. Destroying it.");
                        texture.Release();
                        UnityEngine.Object.DestroyImmediate(texture, true); // Allow destroying assets
                    }
                    textureToSize.Remove(instanceId); // Remove from tracking if we destroy it.
                }
            }
        }

        public void ReleaseAllCreatedTextures() // Renamed from ClearAll for clarity
        {
            Debug.Log($"[TexturePool] Releasing all textures. InUse: {inUseTextures.Sum(kvp => kvp.Value.Count)}, Available: {availableTextures.Sum(kvp => kvp.Value.Count)}");
            foreach (var kvp in inUseTextures)
            {
                foreach (var texture in kvp.Value)
                {
                    if (texture != null && texture.IsCreated())
                    {
                        // Debug.Log($"[TexturePool] Destroying in-use texture: {texture.name} (ID: {texture.GetInstanceID()})");
                        texture.Release();
                        UnityEngine.Object.DestroyImmediate(texture, true); // Allow destroying assets
                    }
                }
                kvp.Value.Clear();
            }
            inUseTextures.Clear();

            foreach (var kvp in availableTextures)
            {
                foreach (var texture in kvp.Value)
                {
                    if (texture != null && texture.IsCreated())
                    {
                        // Debug.Log($"[TexturePool] Destroying available texture: {texture.name} (ID: {texture.GetInstanceID()})");
                        texture.Release();
                        UnityEngine.Object.DestroyImmediate(texture, true); // Allow destroying assets
                    }
                }
                kvp.Value.Clear();
            }
            availableTextures.Clear();
            textureToSize.Clear(); // Clear all tracking
            Debug.Log("[TexturePool] All textures released and pool cleared.");
        }

        public int EstimatePoolSize()
        {
            int totalBytes = 0;

            foreach (var sizeGroup in availableTextures)
            {
                Vector2Int size = sizeGroup.Key;
                int count = sizeGroup.Value.Count;
                totalBytes += size.x * size.y * 4 * count; // ARGB32 = 4 bytes per pixel
            }

            foreach (var sizeGroup in inUseTextures)
            {
                Vector2Int size = sizeGroup.Key;
                int count = sizeGroup.Value.Count;
                totalBytes += size.x * size.y * 4 * count;
            }

            return totalBytes;
        }

        public int ForceCleanup()
        {
            int releasedCount = 0;

            foreach (var sizeGroup in availableTextures)
            {
                foreach (var texture in sizeGroup.Value)
                {
                    if (texture != null && texture.IsCreated())
                    {
                        texture.Release();
                        UnityEngine.Object.Destroy(texture);
                        releasedCount++;
                    }
                }
            }

            availableTextures.Clear();

            var newTextureToSize = new Dictionary<int, Vector2Int>();
            foreach (var sizeGroup in inUseTextures)
            {
                foreach (var texture in sizeGroup.Value)
                {
                    if (texture != null)
                    {
                        newTextureToSize[texture.GetInstanceID()] = sizeGroup.Key;
                    }
                }
            }
            textureToSize = newTextureToSize;

            return releasedCount;
        }
    }

    private class Texture2DPool
    {
        private Dictionary<Vector2Int, List<Texture2D>> availableTextures = new Dictionary<Vector2Int, List<Texture2D>>();
        private Dictionary<Vector2Int, List<Texture2D>> inUseTextures = new Dictionary<Vector2Int, List<Texture2D>>();
        private Dictionary<int, Vector2Int> textureToKey = new Dictionary<int, Vector2Int>();

        public Texture2D GetTexture(int width, int height, TextureFormat format = TextureFormat.ARGB32)
        {
            Vector2Int key = new Vector2Int(width, height);

            if (!availableTextures.ContainsKey(key))
                availableTextures[key] = new List<Texture2D>();
            if (!inUseTextures.ContainsKey(key))
                inUseTextures[key] = new List<Texture2D>();

            Texture2D texture;
            if (availableTextures[key].Count > 0)
            {
                texture = availableTextures[key][0];
                availableTextures[key].RemoveAt(0);
            }
            else
            {
                texture = new Texture2D(width, height, format, false);
            }

            inUseTextures[key].Add(texture);
            textureToKey[texture.GetInstanceID()] = key;
            return texture;
        }

        public void ReleaseTexture(Texture2D texture)
        {
            if (texture == null) return;

            int instanceID = texture.GetInstanceID();
            if (textureToKey.TryGetValue(instanceID, out Vector2Int key))
            {
                if (inUseTextures[key].Remove(texture))
                {
                    availableTextures[key].Add(texture);
                }
                textureToKey.Remove(instanceID);
            }
        }

        public void ClearAll()
        {
            foreach (var textureList in availableTextures.Values)
            {
                foreach (var texture in textureList)
                {
                    if (texture != null) DestroyImmediate(texture);
                }
            }
            availableTextures.Clear();
            inUseTextures.Clear();
            textureToKey.Clear();
        }

        public int EstimatePoolSize()
        {
            int totalBytes = 0;
            foreach (var sizeGroup in availableTextures)
            {
                Vector2Int size = sizeGroup.Key;
                int count = sizeGroup.Value.Count;
                totalBytes += size.x * size.y * 4 * count; // ARGB32 = 4 bytes per pixel
            }
            return totalBytes;
        }

        public int ForceCleanup()
        {
            int releasedCount = 0;
            foreach (var textureList in availableTextures.Values)
            {
                releasedCount += textureList.Count;
                foreach (var texture in textureList)
                {
                    if (texture != null) DestroyImmediate(texture);
                }
                textureList.Clear();
            }
            return releasedCount;
        }
    }

    private TexturePool texturePool;
    private Texture2DPool texture2DPool;

    // Memory Profiling Variables
    private long baselineMemoryUsage = 0;
    private Dictionary<string, int> resourceCounts = new Dictionary<string, int>();
    private Dictionary<string, float> resourceCreationTimes = new Dictionary<string, float>();
    private int totalTexturesCreated = 0;
    private int totalTexturesReleased = 0;
    // private float lastMemoryCheckTime = 0f; // Закомментировано из-за CS0414

    // Performance Profiling Variables
    private List<float> processingTimes = new List<float>();
    private float lastQualityScore = 0f;
    private System.Diagnostics.Stopwatch processingStopwatch = new System.Diagnostics.Stopwatch();
    private float totalProcessingTime = 0f;
    private int processedFrameCount = 0;

    // GPU Post-processing textures
    private RenderTexture tempMask1;
    private RenderTexture tempMask2;
    private RenderTexture previousMask;
    private RenderTexture interpolatedMask;
    private Vector2Int currentResolution = new Vector2Int(640, 480);

    // Добавляем переменные для управления частотой кадров
    private float lastFrameProcessTime = 0f;
    private int cameraFrameSkipCounter = 0;
    private const int CAMERA_FRAME_SKIP_COUNT = 2; // Пропускать 2 из 3 кадров для ~20 FPS на 60 FPS камере, если maxSegmentationFPS ~20

    private Coroutine processingCoroutine = null;

    private void Awake()
    {
        // Попытка найти ARSessionManager, если он не назначен в инспекторе
        if (arSessionManager == null)
        {
            arSessionManager = FindObjectOfType<ARSessionManager>();
            if (arSessionManager == null)
            {
                Debug.LogError("[WallSegmentation] ARSessionManager не найден на сцене!");
            }
        }

        // Попытка найти ARCameraManager
        if (xrOrigin != null && xrOrigin.CameraFloorOffsetObject != null)
        {
            arCameraManager = xrOrigin.CameraFloorOffsetObject.GetComponentInChildren<ARCameraManager>();
        }

        if (arCameraManager == null)
        {
            // Попытка найти через Camera.main, если это AR камера
            if (Camera.main != null)
            {
                arCameraManager = Camera.main.GetComponent<ARCameraManager>();
            }
        }

        if (arCameraManager == null)
        {
            // Крайний случай: поиск по всей сцене
            arCameraManager = FindObjectOfType<ARCameraManager>();
            if (arCameraManager == null)
            {
                Debug.LogError("[WallSegmentation] ARCameraManager не найден! Сегментация не сможет работать.");
            }
            else
            {
                Debug.Log("[WallSegmentation] ARCameraManager найден через FindObjectOfType.");
            }
        }
        else
        {
            Debug.Log("[WallSegmentation] ARCameraManager успешно найден и назначен.");
        }
    }

    private void OnEnable()
    {
        if (arCameraManager != null)
        {
            arCameraManager.frameReceived += OnCameraFrameReceived;
            Debug.Log("[WallSegmentation] Подписался на ARCameraManager.frameReceived.");
        }
        else
        {
            Debug.LogWarning("[WallSegmentation] ARCameraManager не найден в OnEnable. Не могу подписаться на события кадра.");
        }
    }

    private void OnDisable()
    {
        if (arCameraManager != null)
        {
            arCameraManager.frameReceived -= OnCameraFrameReceived;
            Debug.Log("[WallSegmentation] Отписался от ARCameraManager.frameReceived.");
        }
    }

    private void Start()
    {
        if (arCameraManager == null)
        {
            Debug.LogError("[WallSegmentation] ARCameraManager не доступен в Start! Проверьте инициализацию в Awake.");
            // Можно добавить дополнительную логику здесь, например, попытаться найти его снова или отключить компонент
        }

        // Инициализируем систему профилирования производительности
        if (enablePerformanceProfiling)
        {
            processingStopwatch = new System.Diagnostics.Stopwatch();
            Debug.Log($"[WallSegmentation] Инициализирована система отслеживания производительности. Целевое время: {targetProcessingTimeMs}ms");
        }

        // Инициализируем пулы текстур для оптимизации памяти
        texturePool = new TexturePool();
        texture2DPool = new Texture2DPool();

        // Устанавливаем базовую линию использования памяти
        baselineMemoryUsage = System.GC.GetTotalMemory(false);

        // Создаем GPU текстуры для постобработки
        CreateGPUPostProcessingTextures();

        // Инициализируем материалы постобработки
        InitializePostProcessingMaterials();

        // Начинаем инициализацию ML модели
        if (!isModelInitialized && !isInitializing)
        {
            StartCoroutine(InitializeMLModel());
        }

        // Запускаем корутины для мониторинга производительности и памяти
        if (enableAdvancedMemoryManagement && memoryCheckInterval > 0)
        {
            StartCoroutine(MonitorMemoryUsage());
        }

        if (enablePerformanceProfiling && performanceLogInterval > 0)
        {
            StartCoroutine(LogPerformanceStats());
        }
    }

    /// <summary>
    /// Инициализирует материалы для постобработки
    /// </summary>
    private void InitializePostProcessingMaterials()
    {
        try
        {
            // Проверяем и создаем материалы для постобработки
            if (enableGaussianBlur && gaussianBlurMaterial != null)
            {
                if (gaussianBlurMaterial.shader.name.Contains("SegmentationPostProcess"))
                {
                    Debug.Log("[WallSegmentation] gaussianBlurMaterial использует корректный шейдер Hidden/SegmentationPostProcess");
                }
                else
                {
                    Debug.LogWarning($"[WallSegmentation] gaussianBlurMaterial использует неожиданный шейдер: {gaussianBlurMaterial.shader.name}");
                }
            }

            if (enableSharpen && sharpenMaterial != null)
            {
                if (sharpenMaterial.shader.name.Contains("SegmentationPostProcess"))
                {
                    Debug.Log("[WallSegmentation] sharpenMaterial использует корректный шейдер Hidden/SegmentationPostProcess");
                }
                else
                {
                    Debug.LogWarning($"[WallSegmentation] sharpenMaterial использует неожиданный шейдер: {sharpenMaterial.shader.name}");
                }
            }

            if (enableContrast && contrastMaterial != null)
            {
                if (contrastMaterial.shader.name.Contains("SegmentationPostProcess"))
                {
                    Debug.Log("[WallSegmentation] contrastMaterial использует корректный шейдер Hidden/SegmentationPostProcess");
                }
                else
                {
                    Debug.LogWarning($"[WallSegmentation] contrastMaterial использует неожиданный шейдер: {contrastMaterial.shader.name}");
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[WallSegmentation] Ошибка инициализации материалов постобработки: {e.Message}");
        }
    }

    /// <summary>
    /// Корутина для инициализации ML модели
    /// </summary>
    private IEnumerator InitializeMLModel()
    {
        if (isInitializing)
        {
            Debug.LogWarning("[WallSegmentation] Инициализация модели уже выполняется");
            yield break;
        }

        isInitializing = true;
        isInitializationFailed = false;
        lastErrorMessage = null;

        Debug.Log("[WallSegmentation] 🚀 Начинаем инициализацию ML модели...");

        // Шаг 1: Определяем путь к модели
        string modelFilePath = GetModelPath();
        if (string.IsNullOrEmpty(modelFilePath))
        {
            HandleInitializationError("Не найден файл модели в StreamingAssets");
            yield break;
        }

        Debug.Log($"[WallSegmentation] 📁 Загружаем модель из: {modelFilePath}");

        // Шаг 2: Загружаем модель
        yield return StartCoroutine(LoadModel(modelFilePath));

        if (runtimeModel == null)
        {
            HandleInitializationError("Не удалось загрузить модель");
            yield break;
        }

        // Шаг 3: Создаем Worker для выполнения модели
        BackendType backend = (selectedBackend == 1) ? BackendType.GPUCompute : BackendType.CPU;
        Debug.Log($"[WallSegmentation] ⚙️ Создаем Worker с бэкендом: {backend}");

        try
        {
            worker = SentisCompat.CreateWorker(runtimeModel, selectedBackend) as Worker;
        }
        catch (System.Exception e)
        {
            HandleInitializationError($"Не удалось создать Worker: {e.Message}");
            yield break;
        }

        if (worker == null)
        {
            HandleInitializationError("Не удалось создать Worker");
            yield break;
        }

        // Шаг 4: Определяем размеры входа модели
        try
        {
            var inputs = runtimeModel.inputs;
            if (inputs != null && inputs.Count > 0)
            {
                var inputInfo = inputs[0];
                var shapeProperty = inputInfo.GetType().GetProperty("shape");
                if (shapeProperty != null)
                {
                    var shape = shapeProperty.GetValue(inputInfo);
                    int[] dimensions = GetShapeDimensions(shape);

                    if (dimensions != null && dimensions.Length >= 4)
                    {
                        sentisModelHeight = dimensions[2];
                        sentisModelWidth = dimensions[3];
                        Debug.Log($"[WallSegmentation] 📐 Размеры модели: {sentisModelWidth}x{sentisModelHeight}");
                    }
                }
            }
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[WallSegmentation] Не удалось определить размеры модели: {e.Message}. Используются значения по умолчанию.");
        }

        // Шаг 5: Инициализация текстур
        InitializeTextures();

        // Шаг 6: Финализация
        isModelInitialized = true;
        isInitializing = false;

        OnModelInitialized?.Invoke();
        Debug.Log("[WallSegmentation] ✅ ML модель успешно инициализирована!");
    }

    /// <summary>
    /// Обрабатывает ошибку инициализации
    /// </summary>
    private void HandleInitializationError(string errorMessage)
    {
        isInitializationFailed = true;
        isInitializing = false;
        lastErrorMessage = errorMessage;
        Debug.LogError($"[WallSegmentation] ❌ Ошибка инициализации модели: {errorMessage}");

        // Включаем заглушку для продолжения работы
        Debug.Log("[WallSegmentation] 🔄 Активируем заглушку сегментации для продолжения работы");
    }

    /// <summary>
    /// Извлекает размерности формы тензора через reflection
    /// </summary>
    private int[] GetShapeDimensions(object shape)
    {
        if (shape == null) return null;

        try
        {
            // Если shape - это массив int[], возвращаем его напрямую
            if (shape is int[] shapeArray)
            {
                return shapeArray;
            }

            // Пробуем получить массив через свойство или метод
            var shapeType = shape.GetType();

            // Ищем свойство dimensions или shape
            var dimensionsProperty = shapeType.GetProperty("dimensions") ?? shapeType.GetProperty("shape");
            if (dimensionsProperty != null)
            {
                var dimensions = dimensionsProperty.GetValue(shape);
                if (dimensions is int[] dimensionsArray)
                {
                    return dimensionsArray;
                }
            }

            // Ищем метод ToArray()
            var toArrayMethod = shapeType.GetMethod("ToArray", new Type[0]);
            if (toArrayMethod != null)
            {
                var result = toArrayMethod.Invoke(shape, null);
                if (result is int[] resultArray)
                {
                    return resultArray;
                }
            }

            Debug.LogWarning($"[WallSegmentation] Не удалось извлечь размерности из объекта типа: {shapeType.Name}");
            return null;
        }
        catch (System.Exception e)
        {
            Debug.LogWarning($"[WallSegmentation] Ошибка при извлечении размерностей: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Определяет путь к файлу модели
    /// </summary>
    private string GetModelPath()
    {
        string[] possiblePaths = new string[]
        {
            Path.Combine(Application.streamingAssetsPath, modelPath),
            Path.Combine(Application.streamingAssetsPath, "segformer-model.sentis"),
            Path.Combine(Application.streamingAssetsPath, "model.sentis"),
            Path.Combine(Application.streamingAssetsPath, "model.onnx")
        };

        foreach (string path in possiblePaths)
        {
            if (File.Exists(path))
            {
                Debug.Log($"[WallSegmentation] 🎯 Найден файл модели: {path}");
                return path;
            }
        }

        Debug.LogError("[WallSegmentation] ❌ Не найден ни один файл модели в StreamingAssets");
        return null;
    }

    /// <summary>
    /// Корутина для загрузки модели
    /// </summary>
    private IEnumerator LoadModel(string filePath)
    {
        try
        {
            if (filePath.EndsWith(".sentis"))
            {
                // Загружаем Sentis модель
                runtimeModel = ModelLoader.Load(filePath);
                Debug.Log("[WallSegmentation] ✅ Sentis модель загружена");
            }
            else if (filePath.EndsWith(".onnx"))
            {
                // Загружаем ONNX модель
                runtimeModel = ModelLoader.Load(filePath);
                Debug.Log("[WallSegmentation] ✅ ONNX модель загружена");
            }
            else
            {
                throw new System.Exception($"Неподдерживаемый формат модели: {Path.GetExtension(filePath)}");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[WallSegmentation] ❌ Ошибка загрузки модели: {e.Message}");
            runtimeModel = null;
        }

        yield return null;
    }

    /// <summary>
    /// Инициализирует текстуры сегментации
    /// </summary>
    private void InitializeTextures()
    {
        // Safely release specific, possibly outdated textures before getting/creating new ones.

        // Release RenderTextures explicitly
        RenderTexture[] rtsToRelease = { segmentationMaskTexture, tempMask1, tempMask2, previousMask, interpolatedMask };
        string[] rtNames = { "segmentationMaskTexture", "tempMask1", "tempMask2", "previousMask", "interpolatedMask" };

        for (int i = 0; i < rtsToRelease.Length; i++)
        {
            if (rtsToRelease[i] != null)
            {
                if (rtsToRelease[i].IsCreated())
                {
                    rtsToRelease[i].Release();
                }
                UnityEngine.Object.DestroyImmediate(rtsToRelease[i], true);
                TrackResourceRelease(rtNames[i] + "_RenderTexture_Explicit");
            }
        }
        segmentationMaskTexture = null;
        tempMask1 = null;
        tempMask2 = null;
        previousMask = null;
        interpolatedMask = null;

        // Release Texture2D explicitly
        if (cameraTexture != null)
        {
            texture2DPool.ReleaseTexture(cameraTexture); // Return to pool if it was from there
            UnityEngine.Object.DestroyImmediate(cameraTexture, true); // Then destroy the Unity object
            TrackResourceRelease("cameraTexture_Texture2D_Explicit");
            cameraTexture = null;
        }


        // Создаем или получаем из пула cameraTexture. Она будет заполнена в ProcessCameraFrameCoroutine
        // cameraTexture = texture2DPool.GetTexture(inputResolution.x, inputResolution.y, TextureFormat.RGB24); // Moved to ProcessCameraFrameCoroutine
        // TrackResourceCreation("cameraTexture_Texture2D");


        int width = currentResolution.x;
        int height = currentResolution.y;

        // segmentationMaskTexture
        segmentationMaskTexture = texturePool.GetTexture(width, height);
        segmentationMaskTexture.name = "SegmentationMask_Main";
        segmentationMaskTexture.enableRandomWrite = true; // Устанавливаем ДО Create()
        if (!segmentationMaskTexture.IsCreated()) { segmentationMaskTexture.Create(); }
        ClearRenderTexture(segmentationMaskTexture, Color.clear);
        TrackResourceCreation("segmentationMaskTexture_RenderTexture");
        Debug.Log($"[WallSegmentation] Создана/получена RenderTexture для маски: {width}x{height}, randomWrite: {segmentationMaskTexture.enableRandomWrite}");


        // tempMask1 (для CPU постобработки)
        if (!useGPUPostProcessing) // Только если используется CPU путь
        {
            tempMask1 = texturePool.GetTexture(width, height);
            tempMask1.name = "SegmentationMask_Temp1";
            tempMask1.enableRandomWrite = true; // Для CPU путь это может и не нужно, но для консистентности
            if (!tempMask1.IsCreated()) { tempMask1.Create(); }
            ClearRenderTexture(tempMask1, Color.clear);
            TrackResourceCreation("tempMask1_RenderTexture");

            // tempMask2 (для CPU постобработки)
            tempMask2 = texturePool.GetTexture(width, height);
            tempMask2.name = "SegmentationMask_Temp2";
            tempMask2.enableRandomWrite = true;
            if (!tempMask2.IsCreated()) { tempMask2.Create(); }
            ClearRenderTexture(tempMask2, Color.clear);
            TrackResourceCreation("tempMask2_RenderTexture");
        }
        else
        {
            // Если используем GPU, эти текстуры не нужны, освободим если были
            if (tempMask1 != null) { texturePool.ReleaseTexture(tempMask1); tempMask1 = null; TrackResourceRelease("tempMask1_RenderTexture"); }
            if (tempMask2 != null) { texturePool.ReleaseTexture(tempMask2); tempMask2 = null; TrackResourceRelease("tempMask2_RenderTexture"); }
        }


        // Текстуры для временной интерполяции
        if (enableTemporalInterpolation && temporalBlendMaterial != null)
        {
            previousMask = texturePool.GetTexture(width, height);
            previousMask.name = "SegmentationResult_Previous";
            if (!previousMask.IsCreated()) { previousMask.Create(); }
            ClearRenderTexture(previousMask, Color.clear); // Очищаем предыдущую маску
            TrackResourceCreation("previousMask_RenderTexture");

            interpolatedMask = texturePool.GetTexture(width, height);
            interpolatedMask.name = "SegmentationResult_Interpolated";
            interpolatedMask.enableRandomWrite = true; // Нужен для Graphics.Blit в него
            if (!interpolatedMask.IsCreated()) { interpolatedMask.Create(); }
            ClearRenderTexture(interpolatedMask, Color.clear);
            TrackResourceCreation("interpolatedMask_RenderTexture");
        }
        else
        {
            if (previousMask != null) { texturePool.ReleaseTexture(previousMask); previousMask = null; TrackResourceRelease("previousMask_RenderTexture"); }
            if (interpolatedMask != null) { texturePool.ReleaseTexture(interpolatedMask); interpolatedMask = null; TrackResourceRelease("interpolatedMask_RenderTexture"); }
        }

        CreateGPUPostProcessingTextures(); // Создаст или получит из пула текстуры для GPU пост-обработки, если нужно

        Debug.Log($"[WallSegmentation] Пересозданы текстуры с разрешением ({width}, {height})");
    }

    /// <summary>
    /// Получает среднее время обработки сегментации в миллисекундах
    /// </summary>
    public float GetAverageProcessingTimeMs()
    {
        if (processedFrameCount == 0) return 0f;
        return (totalProcessingTime / processedFrameCount) * 1000f;
    }

    /// <summary>
    /// Получает текущее разрешение обработки
    /// </summary>
    public Vector2Int GetCurrentResolution()
    {
        return currentResolution;
    }

    /// <summary>
    /// Получает последнюю оценку качества маски
    /// </summary>
    public float GetLastQualityScore()
    {
        return lastQualityScore;
    }

    /// <summary>
    /// Устанавливает адаптивное разрешение на основе целевой производительности
    /// </summary>
    public void SetAdaptiveResolution(bool enabled)
    {
        adaptiveResolution = enabled;
        if (enabled)
        {
            Debug.Log($"[WallSegmentation] Адаптивное разрешение включено. Текущее: {currentResolution}");
        }
        else
        {
            Debug.Log($"[WallSegmentation] Адаптивное разрешение отключено. Фиксированное: {inputResolution}");
            currentResolution = inputResolution;
        }
    }

    /// <summary>
    /// Устанавливает фиксированное разрешение обработки
    /// </summary>
    public void SetFixedResolution(Vector2Int resolution)
    {
        adaptiveResolution = false;
        currentResolution = resolution;
        inputResolution = resolution;
        Debug.Log($"[WallSegmentation] Установлено фиксированное разрешение: {resolution}");

        // Пересоздаем текстуры с новым разрешением
        CreateGPUPostProcessingTextures();
    }

    /// <summary>
    /// Устанавливает фиксированное разрешение обработки (перегрузка для двух int значений)
    /// </summary>
    public void SetFixedResolution(int width, int height)
    {
        SetFixedResolution(new Vector2Int(width, height));
    }

    /// <summary>
    /// Анализирует качество маски и обновляет lastQualityScore
    /// </summary>
    private float AnalyzeMaskQuality(RenderTexture mask)
    {
        if (mask == null) return 0f;

        try
        {
            // Простой анализ качества на основе заполненности маски
            RenderTexture.active = mask;
            Texture2D tempTexture = new Texture2D(mask.width, mask.height, TextureFormat.RGBA32, false);
            tempTexture.ReadPixels(new Rect(0, 0, mask.width, mask.height), 0, 0);
            tempTexture.Apply();
            RenderTexture.active = null;

            Color[] pixels = tempTexture.GetPixels();
            int validPixels = 0;
            int totalPixels = pixels.Length;

            for (int i = 0; i < totalPixels; i++)
            {
                if (pixels[i].r > 0.1f) // Считаем пиксель валидным если его красный канал > 0.1
                {
                    validPixels++;
                }
            }

            DestroyImmediate(tempTexture);

            float quality = (float)validPixels / totalPixels;
            lastQualityScore = Mathf.Clamp01(quality);
            return lastQualityScore;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[WallSegmentation] Ошибка анализа качества маски: {e.Message}");
            lastQualityScore = 0f;
            return 0f;
        }
    }

    /// <summary>
    /// Получает текущее использование памяти текстурами в МБ
    /// </summary>
    public float GetCurrentTextureMemoryUsage()
    {
        int totalBytes = 0;

        // Подсчитываем размер текстур в пуле
        if (texturePool != null)
        {
            totalBytes += texturePool.EstimatePoolSize();
        }

        // Добавляем временные текстуры GPU
        if (tempMask1 != null && tempMask1.IsCreated())
        {
            totalBytes += tempMask1.width * tempMask1.height * 4; // RGBA32
        }
        if (tempMask2 != null && tempMask2.IsCreated())
        {
            totalBytes += tempMask2.width * tempMask2.height * 4;
        }

        // Добавляем интерполяционные текстуры
        if (previousMask != null && previousMask.IsCreated())
        {
            totalBytes += previousMask.width * previousMask.height * 4;
        }
        if (interpolatedMask != null && interpolatedMask.IsCreated())
        {
            totalBytes += interpolatedMask.width * interpolatedMask.height * 4;
        }

        return totalBytes / 1024 / 1024; // Конвертируем в MB
    }

    /// <summary>
    /// Детекция утечек памяти
    /// </summary>
    private void DetectMemoryLeaks(float memoryGrowthMB, int texturePoolSizeMB)
    {
        bool potentialLeak = false;
        string leakReason = "";

        // Проверка 1: Рост памяти больше 150MB
        if (memoryGrowthMB > 150)
        {
            potentialLeak = true;
            leakReason += $"Excessive memory growth: {memoryGrowthMB:F1}MB; ";
        }

        // Проверка 2: Размер пула текстур превышает лимит
        if (texturePoolSizeMB > maxTexturePoolSizeMB)
        {
            potentialLeak = true;
            leakReason += $"Texture pool too large: {texturePoolSizeMB}MB; ";
        }

        // Проверка 3: Дисбаланс создания/освобождения текстур
        int textureBalance = totalTexturesCreated - totalTexturesReleased;
        if (textureBalance > 20)
        {
            potentialLeak = true;
            leakReason += $"Texture leak: {textureBalance} textures not released; ";
        }

        if (potentialLeak)
        {
            Debug.LogWarning($"[MemoryManager] ⚠️ Potential memory leak detected: {leakReason}");

            if (enableAutomaticCleanup)
            {
                Debug.Log("[MemoryManager] Attempting automatic cleanup...");
                PerformAutomaticCleanup();
            }
        }
    }

    /// <summary>
    /// Выполняет автоматическую очистку памяти
    /// </summary>
    private void PerformAutomaticCleanup()
    {
        try
        {
            Debug.Log("[MemoryManager] 🧹 Performing automatic memory cleanup...");

            // Принудительно очищаем пулы текстур
            if (texturePool != null)
            {
                int releasedTextures = texturePool.ForceCleanup();
                Debug.Log($"[MemoryManager] Released {releasedTextures} pooled textures");
            }

            if (texture2DPool != null)
            {
                int released2D = texture2DPool.ForceCleanup();
                Debug.Log($"[MemoryManager] Released {released2D} 2D textures");
            }

            // Пересоздаем временные текстуры GPU если они слишком большие
            if (tempMask1 != null && (tempMask1.width > currentResolution.x * 1.5f || tempMask1.height > currentResolution.y * 1.5f))
            {
                CreateGPUPostProcessingTextures();
                Debug.Log("[MemoryManager] Recreated GPU post-processing textures");
            }

            // Принудительная сборка мусора
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            // Обновляем baseline после очистки
            baselineMemoryUsage = GC.GetTotalMemory(false);

            Debug.Log($"[MemoryManager] ✅ Cleanup completed. New baseline: {baselineMemoryUsage / 1024 / 1024}MB");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[MemoryManager] Ошибка автоматической очистки: {e.Message}");
        }
    }

    /// <summary>
    /// Создает GPU текстуры для постобработки
    /// </summary>
    private void CreateGPUPostProcessingTextures()
    {
        // Освобождаем старые текстуры
        if (tempMask1 != null) tempMask1.Release();
        if (tempMask2 != null) tempMask2.Release();
        if (previousMask != null) previousMask.Release();
        if (interpolatedMask != null) interpolatedMask.Release();

        // Создаем новые
        tempMask1 = new RenderTexture(currentResolution.x, currentResolution.y, 0, RenderTextureFormat.ARGB32);
        tempMask2 = new RenderTexture(currentResolution.x, currentResolution.y, 0, RenderTextureFormat.ARGB32);
        previousMask = new RenderTexture(currentResolution.x, currentResolution.y, 0, RenderTextureFormat.ARGB32);
        interpolatedMask = new RenderTexture(currentResolution.x, currentResolution.y, 0, RenderTextureFormat.ARGB32);

        tempMask1.Create();
        tempMask2.Create();
        previousMask.Create();
        interpolatedMask.Create();
    }

    /// <summary>
    /// Трекинг создания ресурсов
    /// </summary>
    private void TrackResourceCreation(string resourceType)
    {
        if (!enableMemoryLeakDetection) return;

        string key = resourceType;
        if (resourceCounts.ContainsKey(key))
        {
            resourceCounts[key]++;
        }
        else
        {
            resourceCounts[key] = 1;
            resourceCreationTimes[key] = Time.realtimeSinceStartup;
        }

        if (resourceType.Contains("Texture"))
        {
            totalTexturesCreated++;
        }
    }

    /// <summary>
    /// Трекинг освобождения ресурсов
    /// </summary>
    private void TrackResourceRelease(string resourceType)
    {
        totalTexturesReleased++;

        if (resourceCounts.ContainsKey(resourceType))
        {
            resourceCounts[resourceType]--;
            if (resourceCounts[resourceType] <= 0)
            {
                resourceCounts.Remove(resourceType);
                resourceCreationTimes.Remove(resourceType);
            }
        }

        if (enableDetailedDebug && (debugFlags & DebugFlags.Performance) != 0)
        {
            Debug.Log($"[WallSegmentation] Освобожден ресурс: {resourceType}. Всего освобождено: {totalTexturesReleased}");
        }
    }

    /// <summary>
    /// Корутина для мониторинга использования памяти
    /// </summary>
    private IEnumerator MonitorMemoryUsage()
    {
        while (true)
        {
            yield return new WaitForSeconds(memoryCheckInterval);

            try
            {
                // Получаем текущее использование памяти
                long currentMemory = System.GC.GetTotalMemory(false);
                float memoryGrowthMB = (currentMemory - baselineMemoryUsage) / 1024f / 1024f;

                // Получаем размер пула текстур
                int texturePoolSizeMB = texturePool != null ? texturePool.EstimatePoolSize() / 1024 / 1024 : 0;

                // Проверяем на утечки памяти
                DetectMemoryLeaks(memoryGrowthMB, texturePoolSizeMB);

                // Выполняем автоматическую очистку если нужно
                if (enableAutomaticCleanup)
                {
                    PerformAutomaticCleanup();
                }

                if (enableDetailedDebug && (debugFlags & DebugFlags.Performance) != 0)
                {
                    Debug.Log($"[WallSegmentation] Память: рост {memoryGrowthMB:F1}MB, пул текстур {texturePoolSizeMB}MB");
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[WallSegmentation] Ошибка мониторинга памяти: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Корутина для логирования статистики производительности
    /// </summary>
    private IEnumerator LogPerformanceStats()
    {
        while (true)
        {
            yield return new WaitForSeconds(performanceLogInterval);

            try
            {
                if (processedFrameCount > 0)
                {
                    float avgProcessingTime = GetAverageProcessingTimeMs();
                    float memoryUsage = GetCurrentTextureMemoryUsage();

                    if (enableDetailedDebug)
                    {
                        Debug.Log($"[WallSegmentation] Статистика производительности:" +
                                $"\n  • Обработано кадров: {processedFrameCount}" +
                                $"\n  • Среднее время обработки: {avgProcessingTime:F1}ms" +
                                $"\n  • Текущее разрешение: {currentResolution}" +
                                $"\n  • Использование памяти текстур: {memoryUsage:F1}MB" +
                                $"\n  • Последняя оценка качества: {lastQualityScore:F2}" +
                                $"\n  • Создано текстур: {totalTexturesCreated}" +
                                $"\n  • Освобождено текстур: {totalTexturesReleased}");
                    }
                    else
                    {
                        Debug.Log($"[WallSegmentation] Производительность: {avgProcessingTime:F1}ms, {processedFrameCount} кадров, {memoryUsage:F1}MB");
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[WallSegmentation] Ошибка логирования статистики: {e.Message}");
            }
        }
    }

    private void OnCameraFrameReceived(ARCameraFrameEventArgs eventArgs)
    {
        if (!isModelInitialized || isInitializing || worker == null || !enabled || !gameObject.activeInHierarchy)
        {
            return; // Модель еще не готова, занята или компонент выключен
        }

        // Ограничение частоты обработки кадров, если maxSegmentationFPS > 0
        if (maxSegmentationFPS > 0 && Time.time < lastFrameProcessTime + (1.0f / maxSegmentationFPS))
        {
            return;
        }

        if (processingCoroutine != null)
        {
            // Предыдущая обработка еще не завершена, пропускаем этот кадр
            // Это помогает избежать накопления запросов, если обработка медленная
            if ((debugFlags & DebugFlags.ExecutionFlow) != 0) Debug.Log("[WallSegmentation] Пропускаем кадр, предыдущая обработка еще идет.");
            return;
        }

        if (!arCameraManager.TryAcquireLatestCpuImage(out XRCpuImage cpuImage))
        {
            if ((debugFlags & DebugFlags.CameraTexture) != 0) Debug.LogError("[WallSegmentation] Не удалось получить CPU изображение с камеры.");
            cpuImage.Dispose(); // Убедимся, что XRCpuImage освобождается, даже если она пустая
            return;
        }

        // Запускаем корутину для асинхронной обработки
        processingCoroutine = StartCoroutine(ProcessCameraFrameCoroutine(cpuImage));
        lastFrameProcessTime = Time.time; // Обновляем время последней обработки
    }

    private IEnumerator ProcessCameraFrameCoroutine(XRCpuImage cpuImage)
    {
        if ((debugFlags & DebugFlags.ExecutionFlow) != 0) Debug.Log("[WallSegmentation] ProcessCameraFrameCoroutine: Начало обработки кадра.");

        // Шаг 1: Конвертация XRCpuImage в Texture2D (cameraTexture)
        var conversionParams = new XRCpuImage.ConversionParams
        {
            inputRect = new RectInt(0, 0, cpuImage.width, cpuImage.height),
            outputDimensions = new Vector2Int(inputResolution.x, inputResolution.y),
            outputFormat = TextureFormat.RGB24, // Формат, который ожидает модель
            transformation = XRCpuImage.Transformation.MirrorY // Зависит от ориентации камеры и ожиданий модели
        };

        // Инициализация cameraTexture, если она еще не создана или размеры не совпадают
        if (cameraTexture == null || cameraTexture.width != inputResolution.x || cameraTexture.height != inputResolution.y)
        {
            if (cameraTexture != null) texture2DPool.ReleaseTexture(cameraTexture); // Возвращаем старую текстуру в пул
            cameraTexture = texture2DPool.GetTexture(inputResolution.x, inputResolution.y, TextureFormat.RGB24);
            cameraTexture.name = "WallSegmentation_CameraInputTex";
            if ((debugFlags & DebugFlags.CameraTexture) != 0) Debug.Log($"[WallSegmentation] Создана/пересоздана cameraTexture ({inputResolution.x}x{inputResolution.y}).");
        }

        var convertRequestHandler = cpuImage.ConvertAsync(conversionParams);

        while (!convertRequestHandler.status.IsDone())
        {
            yield return null;
        }

        if (convertRequestHandler.status != XRCpuImage.AsyncConversionStatus.Ready)
        {
            if ((debugFlags & DebugFlags.CameraTexture) != 0) Debug.LogError($"[WallSegmentation] Ошибка конвертации CPU изображения: {convertRequestHandler.status}");
            cpuImage.Dispose();
            processingCoroutine = null;
            yield break;
        }

        // Копируем данные в cameraTexture
        // Ensure NativeArray is not Disposed before GetRawTextureData returns
        var rawTextureData = convertRequestHandler.GetData<byte>();
        try
        {
            if (cameraTexture != null && rawTextureData.IsCreated && rawTextureData.Length > 0)
            {
                cameraTexture.LoadRawTextureData(rawTextureData);
                cameraTexture.Apply();
                if ((debugFlags & DebugFlags.CameraTexture) != 0) Debug.Log("[WallSegmentation] cameraTexture обновлена данными с камеры.");
            }
            else
            {
                if ((debugFlags & DebugFlags.CameraTexture) != 0) Debug.LogError("[WallSegmentation] Не удалось загрузить данные в cameraTexture (null, not created, or empty).");
                cpuImage.Dispose();
                processingCoroutine = null;
                yield break;
            }
        }
        finally
        {
            // rawTextureData.Dispose(); // GetData<byte>() returns a view, XRCpuImage.Dispose() handles the underlying data.
            // Важно освободить XRCpuImage ПОСЛЕ того, как данные из нее были использованы
            cpuImage.Dispose();
        }

        // TODO: Шаг 2: Запуск инференса (RunInference) и постобработка
        yield return StartCoroutine(RunInferenceAndPostProcess());

        processingCoroutine = null; // Освобождаем флаг корутины
        if ((debugFlags & DebugFlags.ExecutionFlow) != 0) Debug.Log("[WallSegmentation] ProcessCameraFrameCoroutine: Обработка кадра завершена.");
    }

    private IEnumerator RunInferenceAndPostProcess()
    {
        if (!isModelInitialized || worker == null)
        {
            if (debugFlags.HasFlag(DebugFlags.ExecutionFlow)) Debug.LogWarning("[WallSegmentation] Модель не инициализирована или Worker не создан. Пропуск инференса.");
            yield break;
        }

        processingStopwatch.Restart();

        // 1. Подготовка входных данных (TextureToTensor)
        Tensor inputTensor = null;
        RenderTexture postProcessSource = null; // Текстура, которая пойдет на вход в постобработку

        if (cameraTexture != null)
        {
            if (debugFlags.HasFlag(DebugFlags.TensorProcessing)) Debug.Log("[WallSegmentation] Запускаем TextureToTensor...");
            object tensorObject = SentisCompat.TextureToTensor(cameraTexture);
            if (tensorObject == null)
            {
                if (debugFlags.HasFlag(DebugFlags.TensorProcessing)) Debug.LogError("[WallSegmentation] TextureToTensor вернул null. Пропуск кадра.");
                processingStopwatch.Stop();
                yield break;
            }
            inputTensor = tensorObject as Tensor;
            if (inputTensor == null && SentisCompat.TensorType != null)
            {
                // Попытка приведения, если tensorObject это обертка или другой совместимый тип
                try
                {
                    if (SentisCompat.TensorType.IsInstanceOfType(tensorObject))
                    {
                        // This cast might not be direct if it's just 'object'. 
                        // A more robust way would be to have a SentisCompat.GetTensorData or similar if needed.
                        // For now, if it's not a direct 'Tensor', we rely on the dynamic nature of reflection in Execute.
                        if (debugFlags.HasFlag(DebugFlags.TensorProcessing)) Debug.LogWarning("[WallSegmentation] tensorObject is not directly castable to Tensor but is of compatible type. Relying on reflection for Execute.");
                        // We'll pass tensorObject directly to ExecuteModelCoroutine
                    }
                    else
                    {
                        Debug.LogError($"[WallSegmentation] Результат TextureToTensor ({tensorObject.GetType().FullName}) не является Tensor и не совместим с {SentisCompat.TensorType.FullName}. Пропуск кадра.");
                        processingStopwatch.Stop();
                        yield break;
                    }
                }
                catch (Exception ex)
                {
                    Debug.LogError($"[WallSegmentation] Ошибка при проверке или приведении типа тензора: {ex.Message}");
                    processingStopwatch.Stop();
                    yield break;
                }
            }
        }
        else
        {
            if (debugFlags.HasFlag(DebugFlags.ExecutionFlow)) Debug.LogError("[WallSegmentation] cameraTexture is null. Пропуск инференса.");
            processingStopwatch.Stop();
            yield break;
        }

        // Убедимся, что текстуры для вывода существуют и соответствуют разрешению
        // ЭТОТ ВЫЗОВ БЫЛ ПРОБЛЕМОЙ - ОН ВЫЗЫВАЛСЯ КАЖДЫЙ КАДР
        // InitializeTextures(); 

        // 2. Запуск инференса модели (Execute)
        Tensor outputTensor = null;
        yield return StartCoroutine(ExecuteModelCoroutine(inputTensor, tensor => outputTensor = tensor));

        if (outputTensor == null)
        {
            if ((debugFlags & DebugFlags.ExecutionFlow) != 0) Debug.LogError("[WallSegmentation] ExecuteModelCoroutine не вернул выходной тензор.");
            inputTensor?.Dispose();
            yield break;
        }

        // Шаг 3: Получение выходного тензора - уже сделано через callback в ExecuteModelCoroutine
        if ((debugFlags & DebugFlags.TensorProcessing) != 0) Debug.Log("[WallSegmentation] Выходной тензор получен.");

        // Шаг 4: Отрисовка тензора в RenderTexture (segmentationMaskTexture)
        // Убедимся, что segmentationMaskTexture существует и имеет правильные размеры
        if (segmentationMaskTexture == null || segmentationMaskTexture.width != inputResolution.x || segmentationMaskTexture.height != inputResolution.y)
        {
            // InitializeTextures(); // Пересоздаст segmentationMaskTexture с нужными размерами
            if (segmentationMaskTexture == null) // Если все еще null после пересоздания
            {
                if ((debugFlags & DebugFlags.ExecutionFlow) != 0) Debug.LogError("[WallSegmentation] segmentationMaskTexture не удалось создать даже после InitializeTextures().");
                inputTensor?.Dispose();
                outputTensor?.Dispose();
                yield break;
            }
        }

        bool renderSuccess = SentisCompat.RenderTensorToTexture(outputTensor, segmentationMaskTexture);
        if (!renderSuccess)
        {
            if ((debugFlags & DebugFlags.TensorProcessing) != 0) Debug.LogError("[WallSegmentation] Ошибка отрисовки выходного тензора в segmentationMaskTexture.");
            // Можно не прерывать, если SentisCompat сам нарисовал плейсхолдер
        }
        else
        {
            if ((debugFlags & DebugFlags.TensorProcessing) != 0) Debug.Log("[WallSegmentation] Выходной тензор отрисован в segmentationMaskTexture.");
        }

        // Шаг 5: Постобработка
        if (enablePostProcessing)
        {
            if ((debugFlags & DebugFlags.ExecutionFlow) != 0) Debug.Log("[WallSegmentation] Начало постобработки.");

            // Получаем временные текстуры из пула
            // Убедимся, что они соответствуют текущему разрешению
            if (tempMask1 == null || tempMask1.width != currentResolution.x || tempMask1.height != currentResolution.y)
            {
                if (tempMask1 != null) texturePool.ReleaseTexture(tempMask1);
                tempMask1 = texturePool.GetTexture(currentResolution.x, currentResolution.y);
                tempMask1.name = "WallSegmentation_TempMask1";
            }
            if (tempMask2 == null || tempMask2.width != currentResolution.x || tempMask2.height != currentResolution.y)
            {
                if (tempMask2 != null) texturePool.ReleaseTexture(tempMask2);
                tempMask2 = texturePool.GetTexture(currentResolution.x, currentResolution.y);
                tempMask2.name = "WallSegmentation_TempMask2";
            }

            RenderTexture source = segmentationMaskTexture;
            RenderTexture destination = tempMask1;
            bool swapped = false; // Отслеживаем, какая текстура содержит актуальные данные

            if (!useGPUPostProcessing) // CPU/Material-based post-processing
            {
                if (enableGaussianBlur && gaussianBlurMaterial != null)
                {
                    gaussianBlurMaterial.SetInt("_BlurSize", blurSize);
                    Graphics.Blit(source, destination, gaussianBlurMaterial, 0); // Предполагаем, что основной эффект в первом проходе
                    source = destination; destination = swapped ? tempMask1 : tempMask2; swapped = !swapped;
                    if ((debugFlags & DebugFlags.TensorProcessing) != 0) Debug.Log("[WallSegmentation] CPU Gaussian Blur применен.");
                }

                if (enableSharpen && sharpenMaterial != null)
                {
                    // sharpenMaterial.SetFloat("_Sharpness", sharpnessFactor); // Если есть параметр
                    Graphics.Blit(source, destination, sharpenMaterial, 0);
                    source = destination; destination = swapped ? tempMask1 : tempMask2; swapped = !swapped;
                    if ((debugFlags & DebugFlags.TensorProcessing) != 0) Debug.Log("[WallSegmentation] CPU Sharpen применен.");
                }

                if (enableContrast && contrastMaterial != null)
                {
                    contrastMaterial.SetFloat("_ContrastFactor", contrastFactor);
                    Graphics.Blit(source, destination, contrastMaterial, 0);
                    source = destination; destination = swapped ? tempMask1 : tempMask2; swapped = !swapped;
                    if ((debugFlags & DebugFlags.TensorProcessing) != 0) Debug.Log("[WallSegmentation] CPU Contrast применен.");
                }

                if (enableMorphologicalClosing && dilateMaterial != null && erodeMaterial != null)
                {
                    Graphics.Blit(source, destination, dilateMaterial); // Dilate
                    Graphics.Blit(destination, source, erodeMaterial);  // Erode (результат в source)
                    // destination = swapped ? tempMask1 : tempMask2; swapped = !swapped; // Не нужно, т.к. результат уже в source
                    if ((debugFlags & DebugFlags.TensorProcessing) != 0) Debug.Log("[WallSegmentation] CPU Morphological Closing применен.");
                }

                if (enableMorphologicalOpening && dilateMaterial != null && erodeMaterial != null)
                {
                    Graphics.Blit(source, destination, erodeMaterial);  // Erode
                    Graphics.Blit(destination, source, dilateMaterial); // Dilate (результат в source)
                    if ((debugFlags & DebugFlags.TensorProcessing) != 0) Debug.Log("[WallSegmentation] CPU Morphological Opening применен.");
                }

                // Копируем результат обратно в segmentationMaskTexture, если он не там
                if (source != segmentationMaskTexture)
                {
                    Graphics.Blit(source, segmentationMaskTexture);
                    if ((debugFlags & DebugFlags.TensorProcessing) != 0) Debug.Log("[WallSegmentation] Результат CPU постобработки скопирован в segmentationMaskTexture.");
                }
            }
            else // GPU Compute Shader post-processing
            {
                if (segmentationProcessor == null)
                {
                    if ((debugFlags & DebugFlags.ExecutionFlow) != 0) Debug.LogError("[WallSegmentation] segmentationProcessor (ComputeShader) не назначен. GPU постобработка невозможна.");
                }
                else
                {
                    if ((debugFlags & DebugFlags.ExecutionFlow) != 0) Debug.Log("[WallSegmentation] Начало GPU постобработки.");
                    // TODO: Реализовать полноценную GPU постобработку с использованием segmentationProcessor (ComputeShader)

                    // Пример для Gaussian Blur, если useComprehensiveGPUProcessing = false
                    if (enableGaussianBlur && !useComprehensiveGPUProcessing)
                    {
                        try
                        {
                            int kernelGaussianBlur = segmentationProcessor.FindKernel("GaussianBlurCS");
                            segmentationProcessor.SetInt("_BlurSizeCS", blurSize); // _BlurSizeCS - предполагаемое имя в шейдере
                            segmentationProcessor.SetTexture(kernelGaussianBlur, "_InputTextureCS", source);
                            segmentationProcessor.SetTexture(kernelGaussianBlur, "_ResultTextureCS", destination);

                            uint threadsX, threadsY, threadsZ;
                            segmentationProcessor.GetKernelThreadGroupSizes(kernelGaussianBlur, out threadsX, out threadsY, out threadsZ);
                            segmentationProcessor.Dispatch(kernelGaussianBlur, Mathf.CeilToInt((float)source.width / threadsX), Mathf.CeilToInt((float)source.height / threadsY), 1);

                            source = destination; destination = swapped ? tempMask1 : tempMask2; swapped = !swapped;
                            if ((debugFlags & DebugFlags.TensorProcessing) != 0) Debug.Log("[WallSegmentation] GPU Gaussian Blur применен.");
                        }
                        catch (Exception e)
                        {
                            if ((debugFlags & DebugFlags.ExecutionFlow) != 0) Debug.LogError($"[WallSegmentation] Ошибка GPU Gaussian Blur: {e.Message}");
                        }
                    }
                    // ... другие эффекты (Sharpen, Contrast, Morphology) аналогично ...

                    // Если используется комплексное ядро
                    if (useComprehensiveGPUProcessing)
                    {
                        try
                        {
                            int kernelComprehensive = segmentationProcessor.FindKernel("ComprehensivePostProcessCS");
                            // Установка всех необходимых параметров для комплексного ядра
                            segmentationProcessor.SetBool("_EnableGaussianBlurCS", enableGaussianBlur);
                            segmentationProcessor.SetInt("_BlurSizeCS", blurSize);
                            segmentationProcessor.SetBool("_EnableSharpenCS", enableSharpen);
                            // ... другие параметры ...
                            segmentationProcessor.SetBool("_EnableContrastCS", enableContrast);
                            segmentationProcessor.SetFloat("_ContrastFactorCS", contrastFactor);
                            segmentationProcessor.SetBool("_EnableMorphCloseCS", enableMorphologicalClosing);
                            segmentationProcessor.SetBool("_EnableMorphOpenCS", enableMorphologicalOpening);

                            segmentationProcessor.SetTexture(kernelComprehensive, "_InputTextureCS", source);
                            segmentationProcessor.SetTexture(kernelComprehensive, "_ResultTextureCS", destination); // Результат в destination

                            uint threadsX, threadsY, threadsZ;
                            segmentationProcessor.GetKernelThreadGroupSizes(kernelComprehensive, out threadsX, out threadsY, out threadsZ);
                            segmentationProcessor.Dispatch(kernelComprehensive, Mathf.CeilToInt((float)source.width / threadsX), Mathf.CeilToInt((float)source.height / threadsY), 1);

                            source = destination; // Результат теперь в source (бывшем destination)
                            // destination и swapped здесь не меняем для последнего шага копирования
                            if ((debugFlags & DebugFlags.TensorProcessing) != 0) Debug.Log("[WallSegmentation] GPU Comprehensive PostProcess применен.");
                        }
                        catch (Exception e)
                        {
                            if ((debugFlags & DebugFlags.ExecutionFlow) != 0) Debug.LogError($"[WallSegmentation] Ошибка GPU Comprehensive PostProcess: {e.Message}");
                        }
                    }

                    // Копируем результат обратно в segmentationMaskTexture, если он не там
                    if (source != segmentationMaskTexture)
                    {
                        Graphics.Blit(source, segmentationMaskTexture);
                        if ((debugFlags & DebugFlags.TensorProcessing) != 0) Debug.Log("[WallSegmentation] Результат GPU постобработки скопирован в segmentationMaskTexture.");
                    }
                }
            }
            if ((debugFlags & DebugFlags.ExecutionFlow) != 0) Debug.Log("[WallSegmentation] Постобработка завершена.");
        }

        // Шаг 6: Временная интерполяция (если включена)
        if (enableTemporalInterpolation && temporalBlendMaterial != null)
        {
            if (previousMask == null || previousMask.width != currentResolution.x || previousMask.height != currentResolution.y)
            {
                if (previousMask != null) texturePool.ReleaseTexture(previousMask);
                previousMask = texturePool.GetTexture(currentResolution.x, currentResolution.y);
                previousMask.name = "WallSegmentation_PreviousMask";
                // При первой инициализации previousMask, копируем в нее текущую маску, чтобы избежать пустого первого кадра интерполяции
                Graphics.Blit(segmentationMaskTexture, previousMask);
                if ((debugFlags & DebugFlags.ExecutionFlow) != 0) Debug.Log("[WallSegmentation] PreviousMask инициализирована и заполнена текущей маской.");
            }

            if (interpolatedMask == null || interpolatedMask.width != currentResolution.x || interpolatedMask.height != currentResolution.y)
            {
                if (interpolatedMask != null) texturePool.ReleaseTexture(interpolatedMask);
                interpolatedMask = texturePool.GetTexture(currentResolution.x, currentResolution.y);
                interpolatedMask.name = "WallSegmentation_InterpolatedMask";
            }

            // Проверка на возраст маски (TODO: более точная логика с lastSuccessfulInferenceTime)
            // float timeSinceLastGoodMask = Time.time - lastValidMaskTime; 
            // if (timeSinceLastGoodMask < maxMaskAgeSeconds) 
            // {
            temporalBlendMaterial.SetTexture("_PreviousMaskTex", previousMask);
            temporalBlendMaterial.SetTexture("_CurrentMaskTex", segmentationMaskTexture); // Текущая обработанная маска
            temporalBlendMaterial.SetFloat("_InterpolationFactor", useExponentialSmoothing ? Time.deltaTime * maskInterpolationSpeed * 10f : maskInterpolationSpeed); // Корректируем скорость для deltaTime если экспоненциальное

            Graphics.Blit(null, interpolatedMask, temporalBlendMaterial, 0); // Blit null source, shader uses textures
            Graphics.Blit(interpolatedMask, segmentationMaskTexture); // Копируем интерполированный результат обратно в основную маску

            if ((debugFlags & DebugFlags.ExecutionFlow) != 0) Debug.Log("[WallSegmentation] Временная интерполяция применена.");
            // }
            // else
            // {
            //     if ((debugFlags & DebugFlags.ExecutionFlow) != 0) Debug.LogWarning("[WallSegmentation] Предыдущая маска слишком старая, интерполяция пропущена.");
            // }

            // Обновляем previousMask для следующего кадра
            Graphics.Blit(segmentationMaskTexture, previousMask);
        }
        else if (enableTemporalInterpolation && temporalBlendMaterial == null)
        {
            if ((debugFlags & DebugFlags.ExecutionFlow) != 0) Debug.LogWarning("[WallSegmentation] Временная интерполяция включена, но temporalBlendMaterial не назначен.");
        }

        // Обновляем маску для подписчиков
        OnSegmentationMaskUpdated?.Invoke(segmentationMaskTexture);

        if (enablePerformanceProfiling)
        {
            processingStopwatch.Stop();
            float frameTime = (float)processingStopwatch.Elapsed.TotalMilliseconds;
            processingTimes.Add(frameTime);
            totalProcessingTime += frameTime;
            processedFrameCount++;
            if ((debugFlags & DebugFlags.Performance) != 0 || showDetailedProfiling)
                Debug.Log($"[WallSegmentation] Время обработки кадра: {frameTime:F2}ms (Адаптивное разрешение: {adaptiveResolution}, Текущее: {currentResolution.x}x{currentResolution.y})");
        }

        // Очистка тензоров
        inputTensor?.Dispose();
        outputTensor?.Dispose(); // outputTensor - это Peek, он может не требовать Dispose, но лучше проверить документацию Sentis
                                 // Для Sentis 2.x, Tensor, возвращаемый PeekOutput(), обычно не нужно освобождать вручную,
                                 // он управляется Worker-ом и перезаписывается при следующем Execute.
                                 // Однако, если мы его клонируем или преобразуем, клон нужно освобождать.
                                 // В данном случае, т.к. мы просто читаем из него, можно оставить без Dispose или проверить конкретную версию.
                                 // Для безопасности, если SentisCompat.RenderTensorToTexture не делает копию, и мы не делаем, то не Dispose.
                                 // Но так как мы приводим к `Tensor outputTensor = worker.PeekOutput() as Tensor;`, это может быть копия или каст.
                                 // Пока оставим Dispose с комментарием.

        yield return null; // Даем один кадр на завершение всех операций рендеринга перед следующим ProcessCameraFrameCoroutine
    }

    private IEnumerator ExecuteModelCoroutine(Tensor inputTensor, System.Action<Tensor> onCompleted)
    {
        if (worker == null || runtimeModel == null || runtimeModel.inputs == null || runtimeModel.inputs.Count == 0)
        {
            if ((debugFlags & DebugFlags.ExecutionFlow) != 0) Debug.LogError("[ExecuteModelCoroutine] Worker, runtimeModel или входы модели не инициализированы.");
            onCompleted?.Invoke(null);
            yield break;
        }

        bool scheduledSuccessfully = false;
        try
        {
            string inputName = runtimeModel.inputs[0].name;
            worker.SetInput(inputName, inputTensor);
            worker.Schedule();
            scheduledSuccessfully = true;
        }
        catch (Exception e)
        {
            if ((debugFlags & DebugFlags.ExecutionFlow) != 0) Debug.LogError($"[ExecuteModelCoroutine] Ошибка при SetInput/Schedule: {e.Message}\n{e.StackTrace}");
            // Сразу вызываем onCompleted с null, так как выполнение не было запланировано
            onCompleted?.Invoke(null);
            yield break; // Выходим из корутины, если планирование не удалось
        }

        // Этот yield теперь находится ВНЕ блока try...catch, который мог бы вызвать ошибку CS1626
        if (scheduledSuccessfully)
        {
            // Даем Sentis время на обработку. 
            yield return null;
        }

        Tensor output = null;
        try
        {
            // Получаем результат только если планирование прошло успешно
            if (scheduledSuccessfully)
            {
                output = worker.PeekOutput() as Tensor;
                if (output == null)
                {
                    if ((debugFlags & DebugFlags.TensorProcessing) != 0) Debug.LogError("[ExecuteModelCoroutine] PeekOutput вернул null или не Tensor после успешного Schedule.");
                }
            }
        }
        catch (Exception e)
        {
            // Эта ошибка может возникнуть при PeekOutput
            if ((debugFlags & DebugFlags.ExecutionFlow) != 0) Debug.LogError($"[ExecuteModelCoroutine] Ошибка при PeekOutput: {e.Message}\n{e.StackTrace}");
            output = null;
        }
        finally
        {
            // onCompleted вызывается в любом случае, чтобы RunInferenceAndPostProcess мог продолжить
            onCompleted?.Invoke(output);
        }
    }

    private void ClearRenderTexture(RenderTexture rt, Color clearColor)
    {
        RenderTexture.active = rt;
        GL.Clear(true, true, clearColor);
        RenderTexture.active = null;
    }

    private void OnDestroy()
    {
        if (isModelInitialized && worker != null)
        {
            SentisCompat.DisposeWorker(worker);
            worker = null;
        }
        if (model != null)
        {
            // Assuming 'model' might be a Sentis Model object that needs disposal,
            // but Sentis 2.x Models are UnityEngine.Objects and managed by GC mostly.
            // If it were a raw pointer or unmanaged resource, it would need explicit release.
            // For now, let runtime handle it or add specific SentisCompat.DisposeModel if available/needed.
            model = null;
        }
        runtimeModel = null; // This is just a reference to model, so nulling it is enough.

        // Explicitly release member textures before clearing pools
        if (texturePool != null)
        {
            if (segmentationMaskTexture != null) { texturePool.ReleaseTexture(segmentationMaskTexture); segmentationMaskTexture = null; TrackResourceRelease("segmentationMaskTexture_OnDestroy"); }
            if (tempMask1 != null) { texturePool.ReleaseTexture(tempMask1); tempMask1 = null; TrackResourceRelease("tempMask1_OnDestroy"); }
            if (tempMask2 != null) { texturePool.ReleaseTexture(tempMask2); tempMask2 = null; TrackResourceRelease("tempMask2_OnDestroy"); }
            if (previousMask != null) { texturePool.ReleaseTexture(previousMask); previousMask = null; TrackResourceRelease("previousMask_OnDestroy"); }
            if (interpolatedMask != null) { texturePool.ReleaseTexture(interpolatedMask); interpolatedMask = null; TrackResourceRelease("interpolatedMask_OnDestroy"); }
        }
        if (texture2DPool != null)
        {
            if (cameraTexture != null) { texture2DPool.ReleaseTexture(cameraTexture); cameraTexture = null; TrackResourceRelease("cameraTexture_OnDestroy"); }
        }

        // Release textures from the pool
        texturePool?.ReleaseAllCreatedTextures(); // Use the new method name
        texture2DPool?.ClearAll(); // Assuming Texture2DPool has a similar ClearAll or a more specific release method.

        if (arCameraManager != null) // Removed null check for OnCameraFrameReceived
        {
            arCameraManager.frameReceived -= OnCameraFrameReceived;
        }
        StopAllCoroutines(); // Stop any running coroutines like InitializeMLModel, MonitorMemoryUsage etc.
        Debug.Log("[WallSegmentation] Cleaned up resources on destroy.");
    }
}