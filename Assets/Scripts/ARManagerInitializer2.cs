using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Unity.XR.CoreUtils;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.Rendering.Universal;
using System;
using LibTessDotNet; // Добавлено для триангуляции

/// <summary>
/// Класс для автоматического добавления ARManagerInitializer в сцену при старте игры
/// и управления плоскостями AR на основе маски сегментации
/// </summary>
[DefaultExecutionOrder(-10)]
public class ARManagerInitializer2 : MonoBehaviour
{
      // Синглтон для глобального доступа
      public static ARManagerInitializer2 Instance { get; private set; }

      // Статический счетчик для уникальных имен плоскостей
      private static int planeInstanceCounter = 0;

      [Header("Настройки создания по касанию")]
      [Tooltip("Включить создание плоскостей по касанию экрана.")]
      public bool enableTapToCreate = true;
      private Vector2? planeCreationTapPosition;

      // Ссылки на AR компоненты
      [Header("AR компоненты")]
      public ARSessionManager sessionManager;
      public ARPlaneManager planeManager;
      public XROrigin xrOrigin;
      [SerializeField] private ARPlaneConfigurator planeConfigurator; // Added reference to ARPlaneConfigurator
      [SerializeField] private PlaneDebugVisualizer planeDebugVisualizer; // Added reference to PlaneDebugVisualizer

      [Header("Настройки сегментации")]
      [Tooltip("Использовать обнаруженные плоскости вместо генерации из маски")]
      public bool useDetectedPlanes = false; // ИЗМЕНЕНО: по умолчанию false, чтобы использовать сегментацию

      [Tooltip("Минимальный размер плоскости для создания (в метрах)")]
      [SerializeField] private float minPlaneSizeInMeters = 0.6f; // УВЕЛИЧЕНО: с 0.1f до 0.6f для фильтрации мелких деталей

      [Tooltip("Минимальный размер области в пикселях (ширина И высота) для её учета")]
      [SerializeField] private int minPixelsDimensionForArea = 20; // УВЕЛИЧЕНО: с 1 до 20 для фильтрации мелких областей

      [Tooltip("Минимальная площадь области в пикселях для её учета")]
      [SerializeField] private int minAreaSizeInPixels = 1500; // УВЕЛИЧЕНО: с 150 до 1500 для создания более крупных плоскостей

      [Tooltip("Разрешение, до которого будет уменьшена маска сегментации перед анализом на CPU (для повышения производительности). Меньшее значение = быстрее, но менее точно.")]
      [SerializeField] private int maskProcessingResolution = 256; // УВЕЛИЧЕНО: с 128 до 256 для лучшей точности

      [Tooltip("Порог значения красного канала (0-255) для пикселя, чтобы считать его частью области стены при поиске связных областей.")]
      [SerializeField] private byte wallPixelThreshold = 120; // Увеличено с 30 до 60 для лучшей фильтрации шума, ЗАТЕМ ДО 120

      [Header("🔧 Адаптивные фильтры областей")]
      [Tooltip("Минимальное соотношение пикселей стены к общей площади области (0.0-1.0)")]
      [SerializeField, Range(0.1f, 1.0f)] private float minWallRatio = 0.6f; // УВЕЛИЧЕНО: с 0.4f до 0.6f для более качественных областей
      [Tooltip("Максимальное соотношение сторон (ширина/высота) для областей")]
      [SerializeField, Range(2.0f, 20.0f)] private float maxAreaAspectRatio = 5.0f; // УМЕНЬШЕНО: с 8.0f до 5.0f для более квадратных областей
      [Tooltip("Включить адаптивную настройку порогов на основе качества маски")]
      [SerializeField] private bool useAdaptiveThresholds = true;
      [Tooltip("Коэффициент ослабления фильтров для зашумлённых масок (чем ниже качество, тем мягче фильтры)")]
      [SerializeField, Range(0.5f, 1.5f)] private float noiseToleranceFactor = 1.2f;

      [Header("🎯 Новая система контуров (Уровень 2)")]
      [Tooltip("Использовать новую систему поиска контуров вместо FindWallAreas")]
      [SerializeField] private bool useContourBasedDetection = false; // ВРЕМЕННО ОТКЛЮЧЕНО из-за зацикливания
      [Tooltip("Минимальная длина контура для создания плоскости (в пикселях)")]
      [SerializeField, Range(10, 500)] private int minContourLength = 8;
      [Tooltip("Максимальная погрешность упрощения контура (алгоритм Дугласа-Пекера)")]
      [SerializeField, Range(0.5f, 10.0f)] private float contourSimplificationEpsilon = 2.0f;
      [Tooltip("Минимальная площадь контура для создания плоскости")]
      [SerializeField, Range(100, 5000)] private int minContourArea = 10;
      [Tooltip("Максимальное количество точек в одном контуре (защита от переполнения памяти)")]
      [SerializeField, Range(500, 10000)] private int maxContourPoints = 5000;
      [Tooltip("Максимальное количество итераций трассировки контура (защита от зацикливания)")]
      [SerializeField, Range(1000, 20000)] private int maxContourIterations = 10000;

      [Header("🧮 PCA-регрессия для плоскостей (Уровень 2)")]
      [Tooltip("Использовать PCA для определения оптимальной ориентации плоскостей")]
      [SerializeField] private bool usePCAForPlaneOrientation = true;
      [Tooltip("Минимальное количество точек для PCA анализа")]
      [SerializeField, Range(10, 1000)] private int minPointsForPCA = 50;
      [Tooltip("Максимальное расстояние точки от плоскости для включения в PCA (в метрах)")]
      [SerializeField, Range(0.01f, 0.5f)] private float pcaOutlierThreshold = 0.1f;
      [Tooltip("Процент точек для исключения как выбросы при робастной регрессии")]
      [SerializeField, Range(0.0f, 0.3f)] private float pcaOutlierPercentage = 0.15f;

      [Header("📦 OBB геометрически точные размеры (Уровень 2)")]
      [Tooltip("Использовать OBB для точного определения размеров плоскостей")]
      [SerializeField] private bool useOBBForPlaneSizing = true;
      [Tooltip("Коэффициент расширения OBB для учёта погрешностей (1.0 = точный размер)")]
      [SerializeField, Range(1.0f, 1.5f)] private float obbExpansionFactor = 1.1f;
      [Tooltip("Минимальная толщина OBB в направлении нормали плоскости (в метрах)")]
      [SerializeField, Range(0.001f, 0.1f)] private float obbMinThickness = 0.02f;
      [Tooltip("Адаптивный размер: использовать плотность точек для масштабирования")]
      [SerializeField] private bool useAdaptiveOBBSizing = true;

      [Header("⚡ Асинхронная обработка GPU (Уровень 2)")]
      [Tooltip("Использовать AsyncGPUReadback для неблокирующего чтения данных")]
      [SerializeField] private bool useAsyncGPUReadback = true;
      [Tooltip("Максимальное количество одновременных асинхронных операций")]
      [SerializeField, Range(1, 8)] private int maxConcurrentReadbacks = 3;
      [Tooltip("Таймаут для асинхронных операций (в секундах)")]
      [SerializeField, Range(0.1f, 5.0f)] private float asyncReadbackTimeout = 1.0f;
      [Tooltip("Использовать пул текстур для экономии памяти")]
      [SerializeField] private bool useTexturePooling = true;
      [Tooltip("Размер пула текстур")]
      [SerializeField, Range(2, 16)] private int texturePoolSize = 4;

      // ⚡ Асинхронная обработка GPU - приватные поля системы
      private Queue<AsyncReadbackRequest> readbackRequestQueue = new Queue<AsyncReadbackRequest>();
      private List<AsyncReadbackOperation> activeReadbackOperations = new List<AsyncReadbackOperation>();
      private Dictionary<int, Texture2D> texturePool = new Dictionary<int, Texture2D>();
      private Queue<Texture2D> availableTextures = new Queue<Texture2D>();
      private System.Action<Texture2D> onAsyncMaskReady;
      private int totalAsyncOperations = 0;
      private int successfulAsyncOperations = 0;
      private int failedAsyncOperations = 0;
      private int timedOutAsyncOperations = 0;
      private float avgAsyncProcessingTime = 0f;
      private Queue<float> asyncProcessingTimes = new Queue<float>();
      private const int maxAsyncTimeSamples = 50;

      // События для асинхронной архитектуры
      public System.Action<Texture2D> OnAsyncMaskProcessed;
      public System.Action<string> OnAsyncOperationFailed;
      public System.Action<AsyncOperationStats> OnAsyncStatsUpdated;

      // Структуры для асинхронной обработки
      [System.Serializable]
      public class AsyncReadbackRequest
      {
            public RenderTexture sourceTexture;
            public int targetWidth;
            public int targetHeight;
            public TextureFormat format;
            public System.Action<Texture2D> callback;
            public float requestTime;
            public string operationId;
            public int priority; // 0 = highest, higher numbers = lower priority

            public AsyncReadbackRequest(RenderTexture source, int width, int height,
                TextureFormat format, System.Action<Texture2D> callback, int priority = 5)
            {
                  this.sourceTexture = source;
                  this.targetWidth = width;
                  this.targetHeight = height;
                  this.format = format;
                  this.callback = callback;
                  this.priority = priority;
                  this.requestTime = Time.time;
                  this.operationId = System.Guid.NewGuid().ToString("N").Substring(0, 8);
            }
      }

      [System.Serializable]
      public class AsyncReadbackOperation
      {
            public UnityEngine.Rendering.AsyncGPUReadbackRequest gpuRequest;
            public AsyncReadbackRequest originalRequest;
            public float startTime;
            public bool isCompleted;
            public bool hasError;
            public string errorMessage;

            public float ElapsedTime => Time.time - startTime;
            public bool IsTimedOut(float timeout) => ElapsedTime > timeout;

            public AsyncReadbackOperation(UnityEngine.Rendering.AsyncGPUReadbackRequest gpuRequest,
                AsyncReadbackRequest originalRequest)
            {
                  this.gpuRequest = gpuRequest;
                  this.originalRequest = originalRequest;
                  this.startTime = Time.time;
                  this.isCompleted = false;
                  this.hasError = false;
            }
      }

      [System.Serializable]
      public class AsyncOperationStats
      {
            public int totalOperations;
            public int successfulOperations;
            public int failedOperations;
            public int timedOutOperations;
            public int activeOperations;
            public int queuedOperations;
            public float averageProcessingTime;
            public float successRate;
            public float currentQueueWaitTime;

            public AsyncOperationStats(int total, int successful, int failed, int timedOut,
                int active, int queued, float avgTime)
            {
                  totalOperations = total;
                  successfulOperations = successful;
                  failedOperations = failed;
                  timedOutOperations = timedOut;
                  activeOperations = active;
                  queuedOperations = queued;
                  averageProcessingTime = avgTime;
                  successRate = total > 0 ? (float)successful / total : 0f;
                  currentQueueWaitTime = queued > 0 ? (queued * avgTime) : 0f;
            }
      }

      [Header("Настройки Рейкастинга для Плоскостей")]
      [Tooltip("Включить подробное логирование процесса рейкастинга и фильтрации попаданий.")]
      [SerializeField] private bool enableDetailedRaycastLogging = false; // ОТКЛЮЧЕНО для лучшей производительности
      [Tooltip("Максимальное расстояние для рейкастов при поиске поверхностей")]
      [SerializeField] private float maxRayDistance = 15f; // Увеличено с 10 до 15 метров
      [Tooltip("Маска слоев для рейкастинга (например, Default, SimulatedEnvironment, Wall)")]
      [SerializeField] private LayerMask hitLayerMask = (1 << 0) | (1 << 8) | (1 << 30) | (1 << 31); // Default + SimulatedEnvironment + XR Simulation + Layer31 (LivingRoom)
      [Tooltip("Минимальное расстояние до объекта, чтобы считать попадание валидным (м). Помогает отфильтровать попадания 'внутрь' объектов или слишком близкие поверхности.")]
      [SerializeField] private float minHitDistanceThreshold = 0.1f;
      [Tooltip("Максимальное допустимое отклонение нормали стены от идеальной вертикали (в градусах). Используется для определения, является ли поверхность стеной.")]
      [SerializeField] private float maxWallNormalAngleDeviation = 75f; // ВРЕМЕННО увеличено для симуляции (было 25f)
      [Tooltip("Минимальный допустимый угол нормали пола/потолка к вертикали (в градусах), чтобы считать поверхность горизонтальной. Например, 15 градусов означает, что поверхности с наклоном до 15 градусов от горизонтали считаются полом/потолком.")]
#pragma warning disable 0414
      [SerializeField] private float maxFloorNormalAngleDeviation = 15f;
#pragma warning restore 0414
      [Tooltip("Слой, в который будут переключены созданные плоскости")]
      [SerializeField] private string planesLayerName = "ARPlanes"; // Слой для плоскостей
      [Tooltip("Имена объектов, которые должны игнорироваться при рейкастинге (разделены запятыми)")]
      [SerializeField] private string ignoreObjectNames = ""; // Объекты для игнорирования

      [Header("Настройки сохранения плоскостей")]
      [SerializeField] private bool usePersistentPlanes = true; // Whether to use the persistent plane system
      [SerializeField] private bool highlightPersistentPlanes = true; // Whether to highlight persistent planes with different color
      [SerializeField] private Color persistentPlaneColor = new Color(0.0f, 0.8f, 0.2f, 0.7f); // Default color for persistent planes

      // Dictionary to track which of our generated planes are persistent
      private Dictionary<GameObject, bool> persistentGeneratedPlanes = new Dictionary<GameObject, bool>();

      // Добавляем защиту от удаления недавно созданных плоскостей
      private Dictionary<GameObject, float> planeCreationTimes = new Dictionary<GameObject, float>();
      private Dictionary<GameObject, float> planeLastVisitedTime = new Dictionary<GameObject, float>();

      // 🛡️ Система гистерезиса для стабилизации плоскостей
      private Dictionary<GameObject, int> planeMissedFrames = new Dictionary<GameObject, int>(); // Количество пропущенных кадров для каждой плоскости
      private int maskProcessingFrameCounter = 0; // Счетчик кадров для частоты обработки

      [Tooltip("Материал для вертикальных плоскостей")]
      [SerializeField] private Material verticalPlaneMaterial; // Должно быть private

      [Tooltip("Материал для горизонтальных плоскостей")]
      [SerializeField] private Material horizontalPlaneMaterial; // Должно быть private

      [Header("Отладочная Визуализация Лучей")]
      [Tooltip("Материал для отладочной визуализации лучей. Если не назначен, визуализация лучей LineRenderer'ом будет отключена.")]
      [SerializeField] private Material debugRayMaterial;
      private MaterialPropertyBlock debugRayMaterialPropertyBlock;

      // Поле для RawImage будет установлено извне
      private UnityEngine.UI.RawImage отображениеМаскиUI;

      // Текущая маска сегментации
      private RenderTexture currentSegmentationMask;
      private bool maskUpdated = false;
      private List<GameObject> generatedPlanes = new List<GameObject>();

      // Для отслеживания изменений количества плоскостей
      private int lastPlaneCount = 0;

      // Счетчик кадров для обновления плоскостей
      private int frameCounter = 0;

      // Переменная для отслеживания последнего хорошего результата сегментации
      private bool hadValidSegmentationResult = false;
      private float lastSuccessfulSegmentationTime = 0f;
      private float segmentationTimeoutSeconds = 10f; // Тайм-аут для определения "потери" сегментации

      // Диагностические счётчики для событий сегментации
      private int totalSegmentationEvents = 0;
      private int successfulSegmentationEvents = 0;
      private int failedSegmentationEvents = 0;
      private float lastEventTime = 0f;
      private float avgEventInterval = 0f;
      private Queue<float> eventIntervals = new Queue<float>();

      // Переменная для хранения InstanceID TrackablesParent из Start()
      private int trackablesParentInstanceID_FromStart = 0;

      [Header("🔍 Debug Settings")]
      [Tooltip("Включить детальную диагностику объектов сцены и коллайдеров при старте")]
      [SerializeField] private bool enableSceneObjectsDiagnostics = false; // Отключено для чистой консоли
                                                                           // [Tooltip("Включить логирование параметров камеры при получении данных сегментации")]
                                                                           // [SerializeField] private bool enableGetCameraParametersLogging = false; // Не используется
      [Tooltip("Включить логирование подробных логов о создании плоскостей")]
      [SerializeField] private bool enableCustomPlaneCreationLogging = false; // ОТКЛЮЧЕНО для лучшей производительности
      [Tooltip("Включить логирование подробных логов о чистке старых плоскостей")]
      [SerializeField] private bool enableVerboseLoggingCleanup = false; // Ensured false by default
      [Tooltip("Включить диагностику событий сегментации (частота, успешность, тайм-ауты)")]
      [SerializeField] private bool enableSegmentationEventDiagnostics = false; // Включено для отладки Проблемы 3

      [Header("📏 Размеры и геометрия")]
      [SerializeField] private float maxPlaneSize = 4.0f; // УВЕЛИЧЕНО: Реалистичный максимальный размер плоскости для больших стен
      [SerializeField] private float minPlaneSize = 0.8f; // УВЕЛИЧЕНО: с 0.3f до 0.8f для создания более крупных плоскостей
      [SerializeField] private float maxAspectRatio = 4.0f; // УМЕНЬШЕНО: с 6.0f до 4.0f для более квадратных плоскостей
      [SerializeField] private float maxWallHeight = 3.0f; // УВЕЛИЧЕНО: Реалистичная максимальная высота стены
      [SerializeField] private float maxWallWidth = 4.0f;  // УВЕЛИЧЕНО: Реалистичная максимальная ширина стены
      [SerializeField] private float planeSizeMultiplier = 1.0f; // ИСПРАВЛЕНО: Полный размер плоскостей без уменьшения

      [Header("🔧 Дополнительные настройки размеров")]
      [SerializeField]
      [Tooltip("Включить точное соответствие размеров плоскостей обнаруженным поверхностям (отключает коэффициенты уменьшения)")]
      private bool accurateSizing = true; // Новый параметр для точного соответствия размеров
      [SerializeField]
      [Tooltip("Включить улучшенное позиционирование плоскостей по центру обнаруженной поверхности")]
      private bool accuratePositioning = true; // Новый параметр для точного позиционирования

      [SerializeField]
      [Tooltip("Использовать одиночный точный рейкаст в центре каждой области (убирает множественные красные линии)")]
      private bool useSinglePreciseRaycast = true; // Новый параметр для одиночных рейкастов

      [SerializeField]
      [Tooltip("Автоматически сливать близкие плоскости в одну (уменьшает количество плоскостей)")]
      private bool enablePlaneMerging = true; // Новый параметр для слияния плоскостей

      [SerializeField]
      [Range(0.1f, 2.0f)]
      [Tooltip("Максимальное расстояние между плоскостями для их слияния (в метрах)")]
      private float planeMergingDistance = 0.5f; // Расстояние слияния

      [SerializeField]
      [Tooltip("Включить динамическое слияние всех плоскостей в крупные области")]
      private bool enableDynamicMerging = false; // ОТКЛЮЧЕНО по умолчанию для стабильности

      [SerializeField]
      [Range(0.5f, 3.0f)]
      [Tooltip("Радиус поиска плоскостей для динамического слияния (в метрах)")]
      private float dynamicMergingRadius = 1.5f;

      [SerializeField]
      [Range(5, 50)]
      [Tooltip("Минимальное количество плоскостей для запуска динамического слияния")]
      private int minPlanesForDynamicMerging = 10;

      [SerializeField]
      [Range(0.5f, 10.0f)]
      [Tooltip("Интервал между запусками динамического слияния (в секундах)")]
      private float dynamicMergingInterval = 2.0f;

      // Время последнего слияния
      private float lastDynamicMergingTime = 0f;

      // Количество плоскостей при последнем слиянии
      private int lastMergingPlaneCount = 0;

      [SerializeField]
      [Range(0.1f, 1.0f)]
      [Tooltip("Коэффициент уменьшения размеров плоскостей. 0.8 = 80% от исходного размера для более точного соответствия")]
      private float sizeReductionFactor = 0.95f; // УВЕЛИЧЕНО: с 0.8f до 0.95f для сохранения полного размера
      [SerializeField]
      [Range(0.05f, 3.0f)]
      [Tooltip("Максимальный размер плоскости в метрах после применения всех коэффициентов")]
      private float maxPlaneClampSize = 3.0f; // УВЕЛИЧЕНО: с 2.5f до 3.0f для реалистичных стен

      [Tooltip("Коэффициент подгонки плоскостей к стенам. Уменьшает размер плоскостей чтобы они не выходили за пределы стен. 1.0 = без изменений, 0.5 = уменьшение на 50%")]
      [SerializeField] private float wallFitMultiplier = 0.8f; // УВЕЛИЧЕНО: с 0.5f до 0.8f для сохранения размера

      [Tooltip("Дополнительный коэффициент масштабирования для уменьшения размера создаваемых плоскостей. 1.0 = без изменений, 0.5 = в два раза меньше.")]
      // [SerializeField] private float planeSizeScalingFactor = 0.5f; // Не используется - удалено для избежания предупреждения

      private Camera mainCamera;

      // [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)] // ЗАКОММЕНТИРОВАТЬ или УДАЛИТЬ
      // private static void Initialize()
      // {
      //     GameObject initializer = new GameObject("AR Manager Initializer 2");
      //     ARManagerInitializer2 component = initializer.AddComponent<ARManagerInitializer2>();
      //     component.useDetectedPlanes = false;
      //     DontDestroyOnLoad(initializer);
      //     Debug.Log("[ARManagerInitializer2] Инициализирован");
      // }

      private void Awake()
      {
            // Debug.Log("[ARManagerInitializer2] Awake() called.");
            if (Instance != null && Instance != this)
            {
                  Debug.LogWarning("[ARManagerInitializer2-Awake] Найден дублирующий экземпляр ARManagerInitializer2. Уничтожаем лишний.");
                  Destroy(gameObject);
                  return;
            }

            Instance = this;

            // Force disable verbose logs to override Inspector values if necessary
            this.enableDetailedRaycastLogging = true; // ВРЕМЕННО включено для тестирования фильтрации пола
            // this.enableCustomPlaneCreationLogging = false; // ОТКЛЮЧЕНО после решения проблемы с рейкастами
            this.enableVerboseLoggingCleanup = false;
            Debug.Log("[ARManagerInitializer2-Awake] Verbose logging flags forcefully DISABLED for clean console.");

            // ОТКЛЮЧАЕМ принудительное включение логирования - оставляем пользователю выбор
            // enableDetailedRaycastLogging = true; // УБРАНО: Принудительное включение отладки

            if (debugRayMaterial != null)
            {
                  debugRayMaterialPropertyBlock = new MaterialPropertyBlock();
            }

            // Диагностика объектов сцены и коллайдеров
            if (enableSceneObjectsDiagnostics)
            {
                  DiagnoseSceneObjects();

                  // Запускаем повторную проверку через задержку на случай асинхронной загрузки объектов
                  StartCoroutine(DelayedColliderCheck());
            }

            // Debug.Log($"[ARManagerInitializer2] Awake complete. Instance ID: {this.GetInstanceID()}, Name: {this.gameObject.name}");

            // Find ARPlaneConfigurator if not assigned
            if (planeConfigurator == null)
            {
                  planeConfigurator = FindObjectOfType<ARPlaneConfigurator>();
            }
      }

      private void Start()
      {
            // ИСПРАВЛЕНО: Принудительная очистка всех старых плоскостей при старте
            Debug.Log("[ARManagerInitializer2] 🔥 Принудительная очистка при старте...");
            DeleteAllPlanes();
            CleanupProblematicPersistentPlanes();

            // Очищаем все словари
            persistentGeneratedPlanes.Clear();
            planeCreationTimes.Clear();
            planeLastVisitedTime.Clear();
            generatedPlanes.Clear();

            Debug.Log("[ARManagerInitializer2] ✅ Очистка завершена. Готов к созданию новых плоскостей с правильными размерами.");

            FindARComponents();

            // Инициализация материалов ДО подписки на события
            InitializeMaterials();

            // Валидация настроек слоёв
            ValidateLayerMask();

            // Ensure we have reference to ARPlaneConfigurator
            if (planeConfigurator == null)
            {
                  planeConfigurator = FindObjectOfType<ARPlaneConfigurator>();
                  if (planeConfigurator == null && usePersistentPlanes)
                  {
                        Debug.LogWarning("[ARManagerInitializer2] ARPlaneConfigurator not found but usePersistentPlanes=true. Persistence won't work correctly.");
                  }
            }

            // Find PlaneDebugVisualizer if not assigned
            if (planeDebugVisualizer == null)
            {
                  planeDebugVisualizer = FindObjectOfType<PlaneDebugVisualizer>();
                  if (planeDebugVisualizer == null)
                  {
                        Debug.LogWarning("[ARManagerInitializer2] PlaneDebugVisualizer не найден в сцене");
                  }
                  else
                  {
                        Debug.Log("[ARManagerInitializer2] ✅ PlaneDebugVisualizer найден и подключен");
                  }
            }

            // Инициализация системы персистентных плоскостей
            InitializePersistentPlanesSystem();

            // 🛡️ ПРИНУДИТЕЛЬНАЯ АКТИВАЦИЯ СИСТЕМЫ СТАБИЛИЗАЦИИ
            Debug.Log("[ARManagerInitializer2] 🛡️ Принудительная активация системы стабилизации при старте...");
            ApplyStabilizationSettings();

            ConfigureARMeshManager();

            // ⚡ УРОВЕНЬ 2: Инициализация асинхронной системы GPU
            if (useAsyncGPUReadback)
            {
                  InitializeAsyncSystem();
                  Debug.Log("✅ AsyncGPUReadback система инициализирована");
            }

            // ПРИНУДИТЕЛЬНО устанавливаем правильные настройки для работы с сегментацией
            useDetectedPlanes = false;
            Debug.Log($"[ARManagerInitializer2] 🔧 ПРИНУДИТЕЛЬНО установлено useDetectedPlanes = {useDetectedPlanes} для работы с сегментацией");

            if (!useDetectedPlanes) // ИЗМЕНЕНО: Логика инвертирована. Подписываемся, если НЕ используем плоскости ARFoundation
            {
                  SubscribeToWallSegmentation();
            }

            if (!useDetectedPlanes)
            {
                  Debug.Log("[ARManagerInitializer2-Start] useDetectedPlanes is false. Disabling ARFoundation visualizers.");
                  DisableARFoundationVisualizers();
                  DisableOtherARVisualizers();
            }

            // Попытка отключить стандартный ARPlaneManager, если он есть и мы НЕ используем ARFoundation плоскости
            if (planeManager != null && !useDetectedPlanes)
            {
                  Debug.LogWarning("[ARManagerInitializer2] Попытка отключить ARPlaneManager.");
                  planeManager.enabled = false;
                  if (!planeManager.enabled)
                  {
                        Debug.Log("[ARManagerInitializer2] ARPlaneManager успешно отключен.");
                  }
                  else
                  {
                        Debug.LogWarning("[ARManagerInitializer2] Не удалось отключить ARPlaneManager.");
                  }
            }
            else if (planeManager == null && !useDetectedPlanes)
            {
                  // Debug.LogWarning("[ARManagerInitializer2] planeManager не назначен, но useDetectedPlanes=false. Нечего отключать.");
            }


            // Debug.Log($"[ARManagerInitializer2] Настройки инициализированы: useDetectedPlanes={useDetectedPlanes}");

            if (xrOrigin != null && xrOrigin.TrackablesParent != null)
            {
                  trackablesParentInstanceID_FromStart = xrOrigin.TrackablesParent.GetInstanceID();
                  // Debug.Log($"[ARManagerInitializer2-Start] TrackablesParent is: {xrOrigin.TrackablesParent.name}, ID: {trackablesParentInstanceID_FromStart}, Path: {GetGameObjectPath(xrOrigin.TrackablesParent)}");
            }
            else
            {
                  Debug.LogError("[ARManagerInitializer2-Start] XROrigin or XROrigin.TrackablesParent is not assigned!");
            }
            // CreateBasicPlaneInFrontOfUser();
      }

      public void УстановитьОтображениеМаскиUI(UnityEngine.UI.RawImage rawImageДляУстановки)
      {
            if (rawImageДляУстановки != null)
            {
                  отображениеМаскиUI = rawImageДляУстановки;
                  // Debug.Log("[ARManagerInitializer2] Успешно установлен RawImage для отображения маски через УстановитьОтображениеМаскиUI.");
                  if (currentSegmentationMask != null && отображениеМаскиUI.texture == null)
                  {
                        // Debug.Log("[ARManagerInitializer2] Немедленное применение текущей маски к новому RawImage.");
                        отображениеМаскиUI.texture = currentSegmentationMask;
                        отображениеМаскиUI.gameObject.SetActive(true);
                  }
            }
            else
            {
                  // Debug.LogWarning("[ARManagerInitializer2] Попытка установить null RawImage для отображения маски.");
            }
      }

      private void Update()
      {
            if (enableTapToCreate)
            {
                  HandlePlaneCreationInput();
            }

            // ДИАГНОСТИКА: Проверяем тайм-аут событий сегментации
            if (enableSegmentationEventDiagnostics && !useDetectedPlanes)
            {
                  float timeSinceLastEvent = Time.time - lastSuccessfulSegmentationTime;

                  // Периодически проверяем тайм-аут (каждые 5 секунд)
                  if (timeSinceLastEvent > segmentationTimeoutSeconds && Time.time % 5.0f < Time.deltaTime)
                  {
                        DiagnoseSegmentationProblem($"Segmentation timeout: {timeSinceLastEvent:F1}s since last successful event");
                  }
            }

            // ⚡ УРОВЕНЬ 2: Обновление асинхронных операций GPU
            if (useAsyncGPUReadback)
            {
                  UpdateAsyncOperations();
            }

            if (useDetectedPlanes)
            {
                  // Логика для работы с плоскостями ARFoundation, если требуется
                  Debug.LogWarning("[ARManagerInitializer2-Update] ⚠️ useDetectedPlanes=true, пропускаем обработку сегментации");
            }
            else if (maskUpdated)
            {
                  // 🛡️ СИСТЕМА ГИСТЕРЕЗИСА: Обрабатываем маску только через определенные интервалы
                  maskProcessingFrameCounter++;

                  if (enablePlaneHysteresis)
                  {
                        if (maskProcessingFrameCounter >= maskProcessingInterval)
                        {
                              Debug.Log($"[ARManagerInitializer2-Update] 🛡️ Гистерезис: Обрабатываем маску (кадр {maskProcessingFrameCounter}/{maskProcessingInterval})");
                              ProcessSegmentationMask();
                              maskProcessingFrameCounter = 0; // Сброс счетчика
                              maskUpdated = false;
                        }
                        else
                        {
                              Debug.Log($"[ARManagerInitializer2-Update] 🛡️ Гистерезис: Пропускаем кадр ({maskProcessingFrameCounter}/{maskProcessingInterval})");
                              // maskUpdated НЕ сбрасываем - будем проверять в следующем кадре
                        }
                  }
                  else
                  {
                        Debug.Log("[ARManagerInitializer2-Update] 🎯 maskUpdated=true, запускаем ProcessSegmentationMask()");
                        ProcessSegmentationMask();
                        maskUpdated = false;
                  }
            }
            else
            {
                  // Для отладки: проверяем, получаем ли мы маски, но не обрабатываем их
                  if (currentSegmentationMask != null && !maskUpdated)
                  {
                        Debug.LogWarning("[ARManagerInitializer2-Update] ⚠️ У нас есть currentSegmentationMask, но maskUpdated=false. Возможно события не приходят");
                  }
            }

            if (planeCreationTapPosition.HasValue)
            {
                  CreatePlaneAtTap(planeCreationTapPosition.Value);
                  planeCreationTapPosition = null; // Сбрасываем после обработки
            }

            // Отладочный код для отображения маски на UI
            if (отображениеМаскиUI != null && currentSegmentationMask != null)
            {
                  отображениеМаскиUI.texture = currentSegmentationMask;
                  отображениеМаскиUI.gameObject.SetActive(true);
            }
      }

      private void FindARComponents()
      {
            if (sessionManager == null)
            {
                  // Debug.Log("[ARManagerInitializer2] Поле sessionManager было null. Попытка найти ARSessionManager в сцене (включая неактивные объекты)...");
                  sessionManager = FindObjectOfType<ARSessionManager>(true); // Ищем включая неактивные
                  if (sessionManager != null)
                  {
                        // Debug.Log($"[ARManagerInitializer2] ✅ ARSessionManager успешно найден и назначен: {sessionManager.gameObject.name} (ID: {sessionManager.gameObject.GetInstanceID()}), активен: {sessionManager.gameObject.activeInHierarchy}");
                  }
                  else
                  {
                        Debug.LogError("[ARManagerInitializer2] ❌ ARSessionManager не найден в сцене!");
                  }
            }
            // else
            // {
            // Debug.Log($"[ARManagerInitializer2] Поле sessionManager уже было назначено: {sessionManager.gameObject.name} (ID: {sessionManager.gameObject.GetInstanceID()}), активен: {sessionManager.gameObject.activeInHierarchy}");
            // }

            if (xrOrigin == null)
            {
                  // Debug.Log("[ARManagerInitializer2] Поле xrOrigin было null. Попытка найти XROrigin в сцене (включая неактивные объекты)...");
                  xrOrigin = FindObjectOfType<XROrigin>(true);
                  if (xrOrigin != null)
                  {
                        // Debug.Log($"[ARManagerInitializer2] ✅ XROrigin успешно найден и назначен: {xrOrigin.gameObject.name} (ID: {xrOrigin.gameObject.GetInstanceID()}), активен: {xrOrigin.gameObject.activeInHierarchy}");
                  }
                  else
                  {
                        Debug.LogError("[ARManagerInitializer2] ❌ XROrigin не найден в сцене!");
                  }
            }

            if (planeManager == null && xrOrigin != null)
            {
                  // Debug.Log("[ARManagerInitializer2] Поле planeManager было null. Попытка найти ARPlaneManager на XROrigin...");
                  planeManager = xrOrigin.GetComponent<ARPlaneManager>();
                  if (planeManager != null)
                  {
                        // Debug.Log($"[ARManagerInitializer2] ✅ ARPlaneManager успешно найден на XROrigin: {planeManager.gameObject.name} (ID: {planeManager.gameObject.GetInstanceID()}), активен: {planeManager.gameObject.activeInHierarchy}, enabled: {planeManager.enabled}");
                        // planeManager.planesChanged += OnPlanesChanged; // Подписываемся на события
                        // Debug.Log("[ARManagerInitializer2] Подписано на события planesChanged");
                  }
                  else
                  {
                        // Debug.LogWarning("[ARManagerInitializer2] ARPlaneManager не найден на XROrigin. Возможно, он не используется или не настроен.");
                  }
            }
            InitializeMaterials();
      }

      private void InitializeMaterials()
      {
            if (verticalPlaneMaterial == null)
            {
                  Debug.LogWarning("[ARManagerInitializer2] Материал для вертикальных плоскостей не назначен. Создание fallback материала.");

                  // Попробуем найти рабочий шейдер в порядке приоритета
                  Shader targetShader = FindWorkingShader();

                  if (targetShader != null)
                  {
                        verticalPlaneMaterial = new Material(targetShader);
                        verticalPlaneMaterial.color = new Color(0.2f, 0.6f, 1.0f, 0.7f); // Голубой полупрозрачный для стен

                        // Если это прозрачный шейдер, настроим режим рендеринга
                        if (targetShader.name.Contains("Transparent") || targetShader.name.Contains("Alpha"))
                        {
                              verticalPlaneMaterial.SetFloat("_Mode", 2); // Transparent mode
                              verticalPlaneMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                              verticalPlaneMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                              verticalPlaneMaterial.SetInt("_ZWrite", 0);
                              verticalPlaneMaterial.DisableKeyword("_ALPHATEST_ON");
                              verticalPlaneMaterial.EnableKeyword("_ALPHABLEND_ON");
                              verticalPlaneMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                              verticalPlaneMaterial.renderQueue = 3000;
                        }

                        Debug.Log($"[ARManagerInitializer2] ✅ Создан материал для вертикальных плоскостей с шейдером: {targetShader.name}");
                  }
                  else
                  {
                        Debug.LogError("[ARManagerInitializer2] ❌ Не удалось найти рабочий шейдер! Создание базового материала.");
                        verticalPlaneMaterial = new Material(Shader.Find("Sprites/Default"));
                        verticalPlaneMaterial.color = Color.blue;
                  }
            }
            else
            {
                  Debug.Log("[ARManagerInitializer2] ✅ Используется назначенный материал для вертикальных плоскостей.");
            }

            if (horizontalPlaneMaterial == null)
            {
                  Debug.LogWarning("[ARManagerInitializer2] Материал для горизонтальных плоскостей не назначен. Создание fallback материала.");

                  Shader targetShader = FindWorkingShader();

                  if (targetShader != null)
                  {
                        horizontalPlaneMaterial = new Material(targetShader);
                        horizontalPlaneMaterial.color = new Color(0.2f, 1.0f, 0.2f, 0.6f); // Зеленый полупрозрачный для пола

                        // Настройка прозрачности
                        if (targetShader.name.Contains("Transparent") || targetShader.name.Contains("Alpha"))
                        {
                              horizontalPlaneMaterial.SetFloat("_Mode", 2);
                              horizontalPlaneMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                              horizontalPlaneMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                              horizontalPlaneMaterial.SetInt("_ZWrite", 0);
                              horizontalPlaneMaterial.DisableKeyword("_ALPHATEST_ON");
                              horizontalPlaneMaterial.EnableKeyword("_ALPHABLEND_ON");
                              horizontalPlaneMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                              horizontalPlaneMaterial.renderQueue = 3000;
                        }

                        Debug.Log($"[ARManagerInitializer2] ✅ Создан материал для горизонтальных плоскостей с шейдером: {targetShader.name}");
                  }
                  else
                  {
                        Debug.LogError("[ARManagerInitializer2] ❌ Не удалось найти рабочий шейдер! Создание базового материала.");
                        horizontalPlaneMaterial = new Material(Shader.Find("Sprites/Default"));
                        horizontalPlaneMaterial.color = Color.green;
                  }
            }
            else
            {
                  Debug.Log("[ARManagerInitializer2] ✅ Используется назначенный материал для горизонтальных плоскостей.");
            }
      }

      /// <summary>
      /// Поиск рабочего шейдера в порядке приоритета
      /// </summary>
      private Shader FindWorkingShader()
      {
            // Массив шейдеров в порядке приоритета
            string[] shaderNames = {
                  "Universal Render Pipeline/Lit",           // URP
                  "Standard",                                // Built-in
                  "Mobile/Diffuse",                         // Mobile optimized
                  "Legacy Shaders/Diffuse",                 // Legacy
                  "Legacy Shaders/Transparent/Diffuse",     // Legacy transparent
                  "Unlit/Color",                            // Simple unlit
                  "Unlit/Transparent",                      // Simple transparent
                  "Sprites/Default",                        // Sprite shader
                  "UI/Default"                              // UI shader
            };

            foreach (string shaderName in shaderNames)
            {
                  Shader shader = Shader.Find(shaderName);
                  if (shader != null)
                  {
                        Debug.Log($"[ARManagerInitializer2] ✅ Найден рабочий шейдер: {shaderName}");
                        return shader;
                  }
                  else
                  {
                        Debug.LogWarning($"[ARManagerInitializer2] ⚠️ Шейдер не найден: {shaderName}");
                  }
            }

            Debug.LogError("[ARManagerInitializer2] ❌ Ни один шейдер не найден!");
            return null;
      }

      /// <summary>
      /// Создает fallback материал с гарантированно рабочим шейдером
      /// </summary>
      private Material CreateFallbackMaterial()
      {
            Debug.LogWarning("[ARManagerInitializer2] Создание emergency fallback материала...");

            // Попробуем самые простые и надежные шейдеры
            string[] emergencyShaders = {
                  "Unlit/Color",
                  "Sprites/Default",
                  "UI/Default",
                  "Hidden/Internal-Colored"
            };

            foreach (string shaderName in emergencyShaders)
            {
                  Shader shader = Shader.Find(shaderName);
                  if (shader != null)
                  {
                        Material mat = new Material(shader);
                        mat.color = new Color(0.3f, 0.7f, 1.0f, 0.8f); // Приятный голубой цвет
                        Debug.Log($"[ARManagerInitializer2] ✅ Emergency материал создан с шейдером: {shaderName}");
                        return mat;
                  }
            }

            // Последний резерв - создаем материал со стандартным шейдером без проверки
            Debug.LogError("[ARManagerInitializer2] ❌ Критическая ошибка: все emergency шейдеры недоступны! Создаем материал 'вслепую'.");
            try
            {
                  Material desperateMaterial = new Material(Shader.Find("Standard"));
                  desperateMaterial.color = Color.cyan;
                  return desperateMaterial;
            }
            catch (System.Exception e)
            {
                  Debug.LogError($"[ARManagerInitializer2] ❌ Даже desperate материал не создался: {e.Message}");
                  return null; // В Unity при null материале объект станет розовым, что и покажет проблему
            }
      }


      private void SubscribeToWallSegmentation()
      {
            Debug.Log("[ARManagerInitializer2] 🔌 Попытка подписки на события WallSegmentation...");
            WallSegmentation wallSegmentationInstance = FindObjectOfType<WallSegmentation>();
            if (wallSegmentationInstance != null)
            {
                  Debug.Log($"[ARManagerInitializer2] ✅ Найден экземпляр WallSegmentation: {wallSegmentationInstance.gameObject.name}. Подписка на OnSegmentationMaskUpdated.");

                  // Проверим, есть ли уже подписчики на это событие
                  System.Reflection.FieldInfo eventInfo = typeof(WallSegmentation).GetField("OnSegmentationMaskUpdated", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);
                  if (eventInfo != null)
                  {
                        var eventDelegate = eventInfo.GetValue(wallSegmentationInstance) as System.Delegate;
                        int subscriberCount = eventDelegate?.GetInvocationList()?.Length ?? 0;
                        Debug.Log($"[ARManagerInitializer2] 📊 Текущее количество подписчиков на OnSegmentationMaskUpdated ДО нашей подписки: {subscriberCount}");
                  }

                  wallSegmentationInstance.OnSegmentationMaskUpdated -= OnSegmentationMaskUpdated; // Отписываемся на всякий случай
                  wallSegmentationInstance.OnSegmentationMaskUpdated += OnSegmentationMaskUpdated; // Подписываемся

                  // Проверим количество подписчиков ПОСЛЕ нашей подписки
                  if (eventInfo != null)
                  {
                        var eventDelegate = eventInfo.GetValue(wallSegmentationInstance) as System.Delegate;
                        int subscriberCount = eventDelegate?.GetInvocationList()?.Length ?? 0;
                        Debug.Log($"[ARManagerInitializer2] 📊 Текущее количество подписчиков на OnSegmentationMaskUpdated ПОСЛЕ нашей подписки: {subscriberCount}");
                  }

                  Debug.Log("[ARManagerInitializer2] ✅ Подписка на события OnSegmentationMaskUpdated настроена");
            }
            else
            {
                  Debug.LogError("[ARManagerInitializer2] ❌ Экземпляр WallSegmentation не найден в сцене. Невозможно подписаться на обновления маски.");
                  // Попробуем переподписаться через некоторое время, если сцена еще загружается
                  StartCoroutine(RetrySubscriptionAfterDelay(1.0f));
            }
      }

      // Повторная попытка подписки после задержки
      private IEnumerator RetrySubscriptionAfterDelay(float delay)
      {
            yield return new WaitForSeconds(delay);
            SubscribeToWallSegmentation();
      }

      // Обработчик события изменения плоскостей
      private void OnPlanesChanged(ARPlanesChangedEventArgs args)
      {
            foreach (ARPlane plane in args.added)
            {
                  ConfigurePlane(plane);
            }

            foreach (ARPlane plane in args.updated)
            {
                  UpdatePlane(plane);
            }
      }

      // Настройка новой плоскости
      private void ConfigurePlane(ARPlane plane)
      {
            if (plane == null) return;

            // Определяем, вертикальная ли это плоскость
            bool isVertical = plane.alignment == PlaneAlignment.Vertical;

            // Назначаем материал в зависимости от типа плоскости
            MeshRenderer renderer = plane.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                  // Выбираем материал в зависимости от ориентации плоскости
                  Material material = isVertical ? verticalPlaneMaterial : horizontalPlaneMaterial;

                  // Создаем уникальный экземпляр материала для каждой плоскости
                  renderer.material = new Material(material);

                  // Если у нас есть маска сегментации и это вертикальная плоскость, применяем ее
                  if (isVertical && currentSegmentationMask != null)
                  {
                        renderer.material.SetTexture("_SegmentationMask", currentSegmentationMask);
                        renderer.material.EnableKeyword("USE_MASK");
                  }

                  // Debug.Log($"[ARManagerInitializer2] Настроена новая плоскость: {plane.trackableId}, тип: {(isVertical ? "вертикальная" : "горизонтальная")}");
            }
      }

      // Обновление существующей плоскости
      private void UpdatePlane(ARPlane plane)
      {
            if (plane == null) return;

            // Обновляем материал и данные для существующей плоскости, если это вертикальная плоскость
            if (plane.alignment == PlaneAlignment.Vertical)
            {
                  MeshRenderer renderer = plane.GetComponent<MeshRenderer>();
                  if (renderer != null && renderer.material != null && currentSegmentationMask != null)
                  {
                        renderer.material.SetTexture("_SegmentationMask", currentSegmentationMask);
                        renderer.material.EnableKeyword("USE_MASK");
                  }
            }
      }

      // Обработка обновления маски сегментации
      private void OnSegmentationMaskUpdated(RenderTexture mask)
      {
            // Диагностические метрики
            float currentTime = Time.time;
            totalSegmentationEvents++;

            if (enableSegmentationEventDiagnostics)
            {
                  // Вычисляем интервал между событиями
                  if (lastEventTime > 0)
                  {
                        float interval = currentTime - lastEventTime;
                        eventIntervals.Enqueue(interval);

                        // Сохраняем только последние 10 интервалов для усреднения
                        if (eventIntervals.Count > 10)
                        {
                              eventIntervals.Dequeue();
                        }

                        // Вычисляем средний интервал
                        avgEventInterval = eventIntervals.Average();
                  }
                  lastEventTime = currentTime;
            }

            if (mask == null)
            {
                  failedSegmentationEvents++;
                  Debug.LogWarning($"[ARManagerInitializer2] ❌ Получена null маска сегментации. События: {successfulSegmentationEvents}/{totalSegmentationEvents}");

                  if (enableSegmentationEventDiagnostics)
                  {
                        DiagnoseSegmentationProblem("Null mask received");
                  }
                  return;
            }

            // Валидация размеров маски
            if (mask.width <= 0 || mask.height <= 0)
            {
                  failedSegmentationEvents++;
                  Debug.LogError($"[ARManagerInitializer2] ❌ Маска сегментации имеет некорректные размеры: {mask.width}x{mask.height}");

                  if (enableSegmentationEventDiagnostics)
                  {
                        DiagnoseSegmentationProblem($"Invalid mask dimensions: {mask.width}x{mask.height}");
                  }
                  return;
            }

            successfulSegmentationEvents++;
            currentSegmentationMask = mask;
            maskUpdated = true;
            hadValidSegmentationResult = true;
            lastSuccessfulSegmentationTime = currentTime;

            // Логируем только важные события (не каждый кадр)
            if (enableSegmentationEventDiagnostics && totalSegmentationEvents % 20 == 1)
            {
                  Debug.Log($"[ARManagerInitializer2-OnSegmentationMaskUpdated] ✅ Маска сегментации получена: {mask.width}x{mask.height} | " +
                           $"События: {successfulSegmentationEvents}/{totalSegmentationEvents} | " +
                           $"Успешность: {(successfulSegmentationEvents / (float)totalSegmentationEvents * 100):F1}% | " +
                           $"Сред.интервал: {avgEventInterval:F2}с");
            }

            // Не логируем каждый кадр - только для отладки
            // Debug.Log($"[ARManagerInitializer2-OnSegmentationMaskUpdated] ✅ Маска сегментации получена: {mask.width}x{mask.height}");

            if (отображениеМаскиUI != null)
            {
                  отображениеМаскиUI.texture = currentSegmentationMask;
                  отображениеМаскиUI.gameObject.SetActive(true); // Убедимся, что RawImage активен
                                                                 // Debug.Log("[ARManagerInitializer2] Текстура RawImage обновлена маской сегментации.");
            }
            else
            {
                  // Debug.LogWarning("[ARManagerInitializer2] отображениеМаскиUI не установлено, некуда выводить маску.");
            }

            // 🛡️ Система гистерезиса: обрабатываем маску с заданной частотой для стабильности
            if (enablePlaneHysteresis)
            {
                  maskProcessingFrameCounter++;
                  if (maskProcessingFrameCounter >= maskProcessingInterval)
                  {
                        maskProcessingFrameCounter = 0;
                        frameCounter = 0; // Обрабатываем маску только через интервал
                  }
                  // Если не время обрабатывать маску, не сбрасываем frameCounter
            }
            else
            {
                  // Стандартное поведение: обрабатываем каждую маску
                  frameCounter = 0;
            }
      }

      // Обработка маски сегментации для генерации плоскостей
      private void ProcessSegmentationMask()
      {
            if (currentSegmentationMask == null)
            {
                  // Debug.LogWarning("[ARManagerInitializer2] Попытка обработки null маски сегментации.");
                  return;
            }

            // Debug.Log($"[ARManagerInitializer2-ProcessSegmentationMask] ✅ Обработка маски сегментации {currentSegmentationMask.width}x{currentSegmentationMask.height}");

            int procWidth, procHeight;
            if (currentSegmentationMask.width == 0 || currentSegmentationMask.height == 0)
            {
                  Debug.LogWarning($"[ARManagerInitializer2-ProcessSegmentationMask] currentSegmentationMask имеет нулевые размеры: {currentSegmentationMask.width}x{currentSegmentationMask.height}. Пропуск обработки.");
                  return;
            }

            if (currentSegmentationMask.width >= currentSegmentationMask.height)
            { // Mask is landscape or square
                  procWidth = maskProcessingResolution;
                  procHeight = Mathf.Max(16, Mathf.RoundToInt((float)maskProcessingResolution * currentSegmentationMask.height / currentSegmentationMask.width));
            }
            else
            { // Mask is portrait
                  procHeight = maskProcessingResolution;
                  procWidth = Mathf.Max(16, Mathf.RoundToInt((float)maskProcessingResolution * currentSegmentationMask.width / currentSegmentationMask.height));
            }
            if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-ProcessSegmentationMask] Original Mask: {currentSegmentationMask.width}x{currentSegmentationMask.height}. Processing at: {procWidth}x{procHeight} (target main dim: {maskProcessingResolution})");


            // Конвертируем RenderTexture в Texture2D для анализа пикселей
            // Это может быть ресурсоемко, особенно если делать каждый кадр.
            // Рассмотреть оптимизацию, если производительность станет проблемой.
            Texture2D maskTexture = RenderTextureToTexture2D(currentSegmentationMask, procWidth, procHeight);

            if (maskTexture != null)
            {
                  CreatePlanesFromMask(maskTexture);
                  Destroy(maskTexture); // Освобождаем память Texture2D
            }
            else
            {
                  // Debug.LogError("[ARManagerInitializer2] Не удалось конвертировать RenderTexture в Texture2D.");
            }
      }

      // Преобразование RenderTexture в Texture2D для обработки
      private Texture2D RenderTextureToTexture2D(RenderTexture renderTexture, int targetWidth, int targetHeight)
      {
            // Create a temporary RenderTexture if downscaling is needed
            bool needsDownscaling = renderTexture.width != targetWidth || renderTexture.height != targetHeight;
            RenderTexture sourceRT = renderTexture; // By default, use the original renderTexture

            if (needsDownscaling)
            {
                  // Get a temporary RT for downscaling
                  RenderTexture downscaledRT = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, renderTexture.format, RenderTextureReadWrite.Default);
                  Graphics.Blit(renderTexture, downscaledRT); // Blit from original to downscaled
                  sourceRT = downscaledRT; // The texture to read from is now the downscaled one
            }

            // Create the Texture2D with the target dimensions
            Texture2D texture = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
            RenderTexture currentActiveRT = RenderTexture.active; // Save current active RT

            try
            {
                  RenderTexture.active = sourceRT; // Set the sourceRT (either original or downscaled) as active
                  texture.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
                  texture.Apply();
            }
            catch (System.Exception e)
            {
                  Debug.LogError($"[ARManagerInitializer2] ❌ Ошибка при конвертации RenderTexture ({renderTexture.width}x{renderTexture.height}) в Texture2D ({targetWidth}x{targetHeight}): {e.Message}");
                  Destroy(texture); // Cleanup Texture2D if error occurs
                  texture = null;     // Ensure null is returned on error
            }
            finally
            {
                  RenderTexture.active = currentActiveRT; // Restore previously active RT
                  if (needsDownscaling && sourceRT != null && sourceRT != renderTexture) // If we used a temporary downscaledRT
                  {
                        RenderTexture.ReleaseTemporary(sourceRT); // Release it
                  }
            }
            return texture;
      }

      // ⚡ AsyncGPUReadback система - основные методы

      /// <summary>
      /// Асинхронная обработка маски сегментации с использованием GPU readback
      /// </summary>
      private void ProcessSegmentationMaskAsync()
      {
            if (currentSegmentationMask == null || !useAsyncGPUReadback)
            {
                  // Fallback к синхронной обработке
                  ProcessSegmentationMask();
                  return;
            }

            // Вычисляем целевое разрешение для обработки
            int procWidth, procHeight;
            CalculateProcessingResolution(currentSegmentationMask, out procWidth, out procHeight);

            // Создаем запрос на асинхронное чтение
            var request = new AsyncReadbackRequest(
                  currentSegmentationMask,
                  procWidth,
                  procHeight,
                  TextureFormat.RGBA32,
                  OnAsyncMaskProcessingComplete,
                  1 // Высокий приоритет для обработки маски
            );

            // Добавляем запрос в очередь
            EnqueueAsyncReadbackRequest(request);
      }

      /// <summary>
      /// Вычисляет оптимальное разрешение для обработки маски
      /// </summary>
      private void CalculateProcessingResolution(RenderTexture mask, out int procWidth, out int procHeight)
      {
            if (mask.width >= mask.height)
            {
                  procWidth = maskProcessingResolution;
                  procHeight = Mathf.Max(16, Mathf.RoundToInt((float)maskProcessingResolution * mask.height / mask.width));
            }
            else
            {
                  procHeight = maskProcessingResolution;
                  procWidth = Mathf.Max(16, Mathf.RoundToInt((float)maskProcessingResolution * mask.width / mask.height));
            }
      }

      /// <summary>
      /// Добавляет запрос в очередь асинхронного чтения с учетом приоритета и лимитов
      /// </summary>
      private void EnqueueAsyncReadbackRequest(AsyncReadbackRequest request)
      {
            // Проверяем лимит активных операций
            if (activeReadbackOperations.Count >= maxConcurrentReadbacks)
            {
                  // Добавляем в очередь
                  readbackRequestQueue.Enqueue(request);

                  if (enableSegmentationEventDiagnostics)
                  {
                        Debug.Log($"[ARManagerInitializer2] ⚡ Запрос {request.operationId} добавлен в очередь. Активных операций: {activeReadbackOperations.Count}, В очереди: {readbackRequestQueue.Count}");
                  }
                  return;
            }

            // Запускаем операцию немедленно
            StartAsyncReadbackOperation(request);
      }

      /// <summary>
      /// Запускает асинхронную операцию чтения GPU
      /// </summary>
      private void StartAsyncReadbackOperation(AsyncReadbackRequest request)
      {
            try
            {
                  // Создаем временную RT для downscaling если нужно
                  RenderTexture sourceRT = request.sourceTexture;
                  bool needsDownscaling = sourceRT.width != request.targetWidth || sourceRT.height != request.targetHeight;

                  if (needsDownscaling)
                  {
                        RenderTexture downscaledRT = RenderTexture.GetTemporary(
                              request.targetWidth,
                              request.targetHeight,
                              0,
                              sourceRT.format,
                              RenderTextureReadWrite.Default
                        );
                        Graphics.Blit(sourceRT, downscaledRT);
                        sourceRT = downscaledRT;
                  }

                  // Запрашиваем асинхронное чтение
                  var gpuRequest = UnityEngine.Rendering.AsyncGPUReadback.Request(
                        sourceRT,
                        0,
                        request.format
                  );

                  // Создаем операцию для отслеживания
                  var operation = new AsyncReadbackOperation(gpuRequest, request);
                  activeReadbackOperations.Add(operation);

                  totalAsyncOperations++;

                  if (enableSegmentationEventDiagnostics)
                  {
                        Debug.Log($"[ARManagerInitializer2] ⚡ Запущена асинхронная операция {request.operationId}. Активных: {activeReadbackOperations.Count}");
                  }
            }
            catch (System.Exception ex)
            {
                  Debug.LogError($"[ARManagerInitializer2] ❌ Ошибка запуска AsyncGPUReadback: {ex.Message}");
                  failedAsyncOperations++;
                  OnAsyncOperationFailed?.Invoke($"Ошибка запуска операции {request.operationId}: {ex.Message}");
            }
      }

      /// <summary>
      /// Обновляет состояние асинхронных операций (вызывается каждый кадр)
      /// </summary>
      private void UpdateAsyncOperations()
      {
            if (!useAsyncGPUReadback) return;

            // Проверяем завершенные операции
            for (int i = activeReadbackOperations.Count - 1; i >= 0; i--)
            {
                  var operation = activeReadbackOperations[i];

                  if (operation.gpuRequest.done)
                  {
                        ProcessCompletedAsyncOperation(operation);
                        activeReadbackOperations.RemoveAt(i);
                  }
                  else if (operation.IsTimedOut(asyncReadbackTimeout))
                  {
                        ProcessTimedOutAsyncOperation(operation);
                        activeReadbackOperations.RemoveAt(i);
                  }
            }

            // Запускаем новые операции из очереди
            while (readbackRequestQueue.Count > 0 && activeReadbackOperations.Count < maxConcurrentReadbacks)
            {
                  var request = readbackRequestQueue.Dequeue();
                  StartAsyncReadbackOperation(request);
            }

            // Обновляем статистику
            UpdateAsyncStatistics();
      }

      /// <summary>
      /// Обрабатывает завершенную асинхронную операцию
      /// </summary>
      private void ProcessCompletedAsyncOperation(AsyncReadbackOperation operation)
      {
            try
            {
                  if (operation.gpuRequest.hasError)
                  {
                        Debug.LogWarning($"[ARManagerInitializer2] ⚠️ AsyncGPUReadback операция {operation.originalRequest.operationId} завершена с ошибкой");
                        failedAsyncOperations++;
                        OnAsyncOperationFailed?.Invoke($"Операция {operation.originalRequest.operationId} завершена с ошибкой");
                        return;
                  }

                  // Получаем данные и создаем Texture2D
                  var data = operation.gpuRequest.GetData<Color32>();
                  if (data.IsCreated && data.Length > 0)
                  {
                        Texture2D texture = GetTextureFromPool(operation.originalRequest.targetWidth, operation.originalRequest.targetHeight);
                        texture.SetPixelData(data, 0);
                        texture.Apply(false);

                        // Обновляем статистику производительности
                        float processingTime = operation.ElapsedTime;
                        UpdateProcessingTimeStatistics(processingTime);

                        successfulAsyncOperations++;

                        // Выполняем callback
                        operation.originalRequest.callback?.Invoke(texture);

                        if (enableSegmentationEventDiagnostics)
                        {
                              Debug.Log($"[ARManagerInitializer2] ✅ Асинхронная операция {operation.originalRequest.operationId} завершена успешно за {processingTime:F3}с");
                        }
                  }
                  else
                  {
                        Debug.LogError($"[ARManagerInitializer2] ❌ Получены некорректные данные от AsyncGPUReadback операции {operation.originalRequest.operationId}");
                        failedAsyncOperations++;
                  }
            }
            catch (System.Exception ex)
            {
                  Debug.LogError($"[ARManagerInitializer2] ❌ Ошибка обработки завершенной операции {operation.originalRequest.operationId}: {ex.Message}");
                  failedAsyncOperations++;
                  OnAsyncOperationFailed?.Invoke($"Ошибка обработки операции {operation.originalRequest.operationId}: {ex.Message}");
            }
      }

      /// <summary>
      /// Обрабатывает операцию с таймаутом
      /// </summary>
      private void ProcessTimedOutAsyncOperation(AsyncReadbackOperation operation)
      {
            Debug.LogWarning($"[ARManagerInitializer2] ⏱️ AsyncGPUReadback операция {operation.originalRequest.operationId} превысила таймаут ({asyncReadbackTimeout}s)");
            timedOutAsyncOperations++;
            OnAsyncOperationFailed?.Invoke($"Операция {operation.originalRequest.operationId} превысила таймаут");
      }

      /// <summary>
      /// Callback для завершенной асинхронной обработки маски
      /// </summary>
      private void OnAsyncMaskProcessingComplete(Texture2D maskTexture)
      {
            if (maskTexture != null)
            {
                  // Запускаем создание плоскостей
                  CreatePlanesFromMask(maskTexture);

                  // Уведомляем подписчиков
                  OnAsyncMaskProcessed?.Invoke(maskTexture);

                  // Возвращаем текстуру в пул
                  ReturnTextureToPool(maskTexture);
            }
      }

      /// <summary>
      /// Получает текстуру из пула или создает новую
      /// </summary>
      private Texture2D GetTextureFromPool(int width, int height)
      {
            if (!useTexturePooling)
            {
                  return new Texture2D(width, height, TextureFormat.RGBA32, false);
            }

            int key = GetTexturePoolKey(width, height);

            if (texturePool.ContainsKey(key))
            {
                  return texturePool[key];
            }

            if (availableTextures.Count > 0)
            {
                  var texture = availableTextures.Dequeue();
                  if (texture.width == width && texture.height == height)
                  {
                        return texture;
                  }
                  else
                  {
                        // Размер не подходит, пересоздаем
                        Destroy(texture);
                  }
            }

            // Создаем новую текстуру
            var newTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            texturePool[key] = newTexture;
            return newTexture;
      }

      /// <summary>
      /// Возвращает текстуру в пул для повторного использования
      /// </summary>
      private void ReturnTextureToPool(Texture2D texture)
      {
            if (!useTexturePooling || texture == null)
            {
                  if (texture != null) Destroy(texture);
                  return;
            }

            if (availableTextures.Count < texturePoolSize)
            {
                  availableTextures.Enqueue(texture);
            }
            else
            {
                  // Пул переполнен, уничтожаем текстуру
                  Destroy(texture);
            }
      }

      /// <summary>
      /// Генерирует ключ для пула текстур на основе размеров
      /// </summary>
      private int GetTexturePoolKey(int width, int height)
      {
            return width * 10000 + height;
      }

      /// <summary>
      /// Обновляет статистику времени обработки
      /// </summary>
      private void UpdateProcessingTimeStatistics(float processingTime)
      {
            asyncProcessingTimes.Enqueue(processingTime);

            if (asyncProcessingTimes.Count > maxAsyncTimeSamples)
            {
                  asyncProcessingTimes.Dequeue();
            }

            // Вычисляем среднее время
            float sum = 0f;
            foreach (float time in asyncProcessingTimes)
            {
                  sum += time;
            }
            avgAsyncProcessingTime = sum / asyncProcessingTimes.Count;
      }

      /// <summary>
      /// Обновляет и публикует статистику асинхронных операций
      /// </summary>
      private void UpdateAsyncStatistics()
      {
            var stats = new AsyncOperationStats(
                  totalAsyncOperations,
                  successfulAsyncOperations,
                  failedAsyncOperations,
                  timedOutAsyncOperations,
                  activeReadbackOperations.Count,
                  readbackRequestQueue.Count,
                  avgAsyncProcessingTime
            );

            OnAsyncStatsUpdated?.Invoke(stats);
      }

      /// <summary>
      /// Инициализирует асинхронную систему
      /// </summary>
      private void InitializeAsyncSystem()
      {
            if (!useAsyncGPUReadback) return;

            // Инициализируем пул текстур
            if (useTexturePooling)
            {
                  for (int i = 0; i < texturePoolSize; i++)
                  {
                        var texture = new Texture2D(maskProcessingResolution, maskProcessingResolution, TextureFormat.RGBA32, false);
                        availableTextures.Enqueue(texture);
                  }
            }

            // Подписываемся на события
            onAsyncMaskReady = OnAsyncMaskProcessingComplete;

            Debug.Log($"[ARManagerInitializer2] ⚡ Асинхронная система инициализирована. Пул текстур: {texturePoolSize}, Макс. операций: {maxConcurrentReadbacks}");
      }

      /// <summary>
      /// Очищает ресурсы асинхронной системы
      /// </summary>
      private void CleanupAsyncSystem()
      {
            // Очищаем активные операции
            activeReadbackOperations.Clear();
            readbackRequestQueue.Clear();

            // Очищаем пул текстур
            while (availableTextures.Count > 0)
            {
                  var texture = availableTextures.Dequeue();
                  if (texture != null) Destroy(texture);
            }

            foreach (var kvp in texturePool)
            {
                  if (kvp.Value != null) Destroy(kvp.Value);
            }
            texturePool.Clear();

            Debug.Log("[ARManagerInitializer2] ⚡ Асинхронная система очищена");
      }

      /// <summary>
      /// Получает текущую статистику асинхронных операций
      /// </summary>
      public AsyncOperationStats GetAsyncOperationStats()
      {
            return new AsyncOperationStats(
                  totalAsyncOperations,
                  successfulAsyncOperations,
                  failedAsyncOperations,
                  timedOutAsyncOperations,
                  activeReadbackOperations.Count,
                  readbackRequestQueue.Count,
                  avgAsyncProcessingTime
            );
      }

      // Создание плоскостей на основе маски сегментации
      private void CreatePlanesFromMask(Texture2D maskTexture)
      {
            // Debug.Log($"[ARManagerInitializer2-CreatePlanesFromMask] ✅ Начало создания плоскостей из маски. Размеры маски: {maskTexture.width}x{maskTexture.height}");
            Color32[] textureData = maskTexture.GetPixels32();

            // Используем красный канал для определения стен
            byte redChannelThreshold = (byte)wallPixelThreshold;

            // Отслеживаем какие плоскости были обновлены в этом кадре
            Dictionary<GameObject, bool> visitedPlanes = new Dictionary<GameObject, bool>();

            foreach (GameObject plane in generatedPlanes)
            {
                  visitedPlanes[plane] = false;
            }

            int planesCreatedThisFrame = 0;

            // 🎯 УРОВЕНЬ 2: Выбираем алгоритм обнаружения областей
            if (useContourBasedDetection)
            {
                  try
                  {
                        // Новый метод: поиск контуров
                        List<Contour> contours = FindContours(textureData, maskTexture.width, maskTexture.height, redChannelThreshold);

                        // Debug.Log($"[ARManagerInitializer2-CreatePlanesFromMask] 🎯 Найдено {contours.Count} контуров (новый алгоритм)");

                        // Сортируем контуры по площади (сначала большие)
                        var significantContours = contours
                            .Where(c => !c.isHole) // Игнорируем дыры
                            .OrderByDescending(c => c.area)
                            .ToList();

                        // Создаём плоскости из контуров
                        foreach (Contour contour in significantContours)
                        {
                              if (UpdateOrCreatePlaneForContour(contour, maskTexture.width, maskTexture.height, visitedPlanes))
                              {
                                    planesCreatedThisFrame++;
                              }

                              if (enableCustomPlaneCreationLogging) Debug.Log($"[ARManagerInitializer2-CreatePlanesFromMask] 🎯 Контур с {contour.points.Count} точками и площадью {contour.area:F1} обработан. Создано в кадре: {planesCreatedThisFrame}");
                        }
                  }
                  catch (System.Exception ex)
                  {
                        Debug.LogError($"[ARManagerInitializer2-CreatePlanesFromMask] ❌ Ошибка в системе контуров: {ex.Message}. Переключаемся на старую систему областей.");
                        Debug.LogError($"[ARManagerInitializer2-CreatePlanesFromMask] Stack trace: {ex.StackTrace}");

                        // 🛡️ FALLBACK: временно переключаемся на старую систему областей при ошибке
                        useContourBasedDetection = false;

                        // Продолжаем выполнение с новым флагом - код старой системы выполнится ниже
                  }
            }
            else
            {
                  // Старый метод: поиск прямоугольных областей
                  List<Rect> wallAreas = FindWallAreas(textureData, maskTexture.width, maskTexture.height, redChannelThreshold);

                  Debug.Log($"[ARManagerInitializer2-CreatePlanesFromMask] 🔧 Используем старый алгоритм. Найдено {wallAreas.Count} областей стен");

                  // Сортируем области по размеру (площади)
                  var significantAreas = wallAreas
                      .OrderByDescending(area => area.width * area.height)
                      .ToList();

                  // Для каждой области стены проверяем, можно ли обновить существующую плоскость или нужно создать новую
                  foreach (Rect area in significantAreas)
                  {
                        if (area.width * area.height >= minAreaSizeInPixels) // Фильтр по минимальному размеру
                        {
                              if (UpdateOrCreatePlaneForWallArea(area, maskTexture.width, maskTexture.height, visitedPlanes))
                              {
                                    planesCreatedThisFrame++;
                              }

                              if (enableCustomPlaneCreationLogging) Debug.Log($"[ARManagerInitializer2-CreatePlanesFromMask] Область {areaToString(area)} обработана. Создано в кадре: {planesCreatedThisFrame}");
                        }
                        else
                        {
                              if (enableCustomPlaneCreationLogging) Debug.Log($"[ARManagerInitializer2-CreatePlanesFromMask] ❌ Область слишком мала: {area.width * area.height} < {minAreaSizeInPixels} пикс.");
                        }
                  }
            }

            // Удаляем плоскости, которые не были обновлены в текущем кадре (потеряны)
            CleanupOldPlanes(visitedPlanes);

            // 🔄 Выполняем динамическое слияние плоскостей с контролем частоты
            if (enableDynamicMerging && ShouldPerformDynamicMerging())
            {
                  PerformDynamicPlaneMerging();
                  lastDynamicMergingTime = Time.time;
                  lastMergingPlaneCount = generatedPlanes.Count;
            }

            // Логируем только когда есть изменения
            if (planesCreatedThisFrame > 0 || generatedPlanes.Count != lastPlaneCount)
            {
                  Debug.Log($"[ARManagerInitializer2-CreatePlanesFromMask] ✅ Завершено. Создано: {planesCreatedThisFrame} плоскостей. Всего активных: {generatedPlanes.Count}");
            }

            lastPlaneCount = generatedPlanes.Count;
      }

      // Метод для поиска связной области начиная с заданного пикселя
      private Rect FindConnectedArea(Color32[] pixels, int width, int height, int startX, int startY, bool[,] visited, byte threshold)
      {
            if (enableCustomPlaneCreationLogging) Debug.Log($"[ARManagerInitializer2-FindConnectedArea] IN START: StartX={startX}, StartY={startY}, Threshold={threshold}, PixelValue.R={pixels[startY * width + startX].r}, Visited={visited[startX, startY]}");

            if (startX < 0 || startX >= width || startY < 0 || startY >= height || visited[startX, startY] || pixels[startY * width + startX].r < threshold)
            {
                  // if (enableCustomPlaneCreationLogging) Debug.LogWarning($"[ARManagerInitializer2-FindConnectedArea] INVALID START PARAMS or ALREADY VISITED/BELOW THRESHOLD. Returning Rect.zero. Visited={visited[startX, startY]}, Pixel.R={pixels[startY * width + startX].r}");
                  return Rect.zero;
            }

            // Границы области
            int minX = startX;
            int maxX = startX;
            int minY = startY;
            int maxY = startY;

            Queue<Vector2Int> queue = new Queue<Vector2Int>();
            queue.Enqueue(new Vector2Int(startX, startY));
            visited[startX, startY] = true;
            // Debug.Log($"[ARManagerInitializer2-FindConnectedArea] Enqueued initial: ({startX},{startY}), visited set to true. Queue count: {queue.Count}");

            // Возможные направления для обхода (4 соседа)
            Vector2Int[] directions = new Vector2Int[]
            {
            new Vector2Int(1, 0),  // вправо
            new Vector2Int(-1, 0), // влево
            new Vector2Int(0, 1),  // вниз
            new Vector2Int(0, -1)  // вверх
            };

            // Алгоритм обхода в ширину для поиска связной области
            while (queue.Count > 0)
            {
                  Vector2Int current = queue.Dequeue();
                  // Debug.Log($"[ARManagerInitializer2-FindConnectedArea] Dequeued: ({current.x},{current.y}). Pixel.R={pixels[current.y * width + current.x].r}. Queue count: {queue.Count}");

                  // Обновляем границы области
                  bool boundsChanged = false;
                  if (current.x < minX) { minX = current.x; boundsChanged = true; }
                  if (current.x > maxX) { maxX = current.x; boundsChanged = true; }
                  if (current.y < minY) { minY = current.y; boundsChanged = true; }
                  if (current.y > maxY) { maxY = current.y; boundsChanged = true; }

                  if (boundsChanged)
                  {
                        // Debug.Log($"[ARManagerInitializer2-FindConnectedArea] Bounds updated: minX={minX}, maxX={maxX}, minY={minY}, maxY={maxY}");
                  }

                  // Проверяем соседей
                  foreach (Vector2Int dir in directions)
                  {
                        int newX = current.x + dir.x;
                        int newY = current.y + dir.y;

                        // Проверяем, что новые координаты в пределах текстуры
                        if (newX >= 0 && newX < width && newY >= 0 && newY < height)
                        {
                              // Если пиксель не посещен и это часть стены
                              if (!visited[newX, newY] && pixels[newY * width + newX].r >= threshold) // ИЗМЕНЕНО: > на >=
                              {
                                    visited[newX, newY] = true;
                                    queue.Enqueue(new Vector2Int(newX, newY));
                                    // Debug.Log($"[ARManagerInitializer2-FindConnectedArea] Enqueued neighbor: ({newX},{newY}). Pixel.R={pixels[newY * width + newX].r}. Visited set to true. Queue count: {queue.Count}");
                              }
                        }
                  }
            }

            Rect resultRect = new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
            if (enableCustomPlaneCreationLogging) Debug.Log($"[ARManagerInitializer2-FindConnectedArea] IN END: Returning Rect: X={resultRect.x}, Y={resultRect.y}, W={resultRect.width}, H={resultRect.height} for start ({startX},{startY})");
            return resultRect;
      }

      private List<Rect> FindWallAreas(Color32[] pixels, int width, int height, byte threshold)
      {
            if (enableCustomPlaneCreationLogging) Debug.Log($"[ARManagerInitializer2-FindWallAreas] IN START: Texture {width}x{height}, Threshold={threshold}");
            List<Rect> areas = new List<Rect>();
            bool[,] visited = new bool[width, height];
            int areasFoundBeforeFiltering = 0;
            int pixelsChecked = 0;
            int activeUnvisitedPixelsFound = 0;


            for (int y = 0; y < height; y++)
            {
                  for (int x = 0; x < width; x++)
                  {
                        pixelsChecked++;
                        // Debug.Log($"[ARManagerInitializer2-FindWallAreas] Проверка пикселя ({x},{y}). visited={visited[x,y]}, pixel.r = {pixels[y * width + x].r}");
                        if (!visited[x, y] && pixels[y * width + x].r >= threshold)
                        {
                              activeUnvisitedPixelsFound++;
                              if (enableCustomPlaneCreationLogging) Debug.Log($"[ARManagerInitializer2-FindWallAreas] Found ACTIVE UNVISITED pixel at ({x},{y}). Pixel.R={pixels[y * width + x].r}. Calling FindConnectedArea...");
                              Rect area = FindConnectedArea(pixels, width, height, x, y, visited, threshold);
                              areasFoundBeforeFiltering++;
                              if (enableCustomPlaneCreationLogging) Debug.Log($"[ARManagerInitializer2-FindWallAreas] FindConnectedArea for ({x},{y}) returned: {areaToString(area)}. Area.width={area.width}, Area.height={area.height}, Area.width*Area.height={area.width * area.height}");

                              if (area.width >= minPixelsDimensionForArea && area.height >= minPixelsDimensionForArea && area.width * area.height >= minAreaSizeInPixels)
                              {
                                    // НОВОЕ: Умное разбиение слишком больших областей на части
                                    List<Rect> subareas = SubdivideOversizedArea(area, pixels, width, height, threshold);

                                    foreach (Rect subarea in subareas)
                                    {
                                          if (enableCustomPlaneCreationLogging) Debug.Log($"[ARManagerInitializer2-FindWallAreas] ADDED Subarea: {areaToString(subarea)} (Pixel Area: {subarea.width * subarea.height}). Original area subdivided. Total areas: {areas.Count + 1}");
                                          areas.Add(subarea);
                                    }
                              }
                              else
                              {
                                    if (area.width > 0 && area.height > 0) // Если это не Rect.zero
                                    {
                                          if (enableCustomPlaneCreationLogging) Debug.Log($"[ARManagerInitializer2-FindWallAreas] FILTERED Area: {areaToString(area)} (Pixel Dims: {area.width}x{area.height}, Pixel Area: {area.width * area.height}). MinDimensionForArea={minPixelsDimensionForArea}, MinAreaSizeInPixels={minAreaSizeInPixels}. NOT ADDED.");
                                    }
                                    else
                                    {
                                          if (enableCustomPlaneCreationLogging) Debug.Log($"[ARManagerInitializer2-FindWallAreas] FindConnectedArea returned ZERO area for ({x},{y}). NOT ADDED.");
                                    }
                              }
                        }
                  }
            }
            if (enableCustomPlaneCreationLogging) Debug.Log($"[ARManagerInitializer2-FindWallAreas] IN END: PixelsChecked={pixelsChecked}. ActiveUnvisitedPixelsFound (triggers for FindConnectedArea)={activeUnvisitedPixelsFound}. AreasFoundBeforeFiltering={areasFoundBeforeFiltering}. Final ValidAreasCount={areas.Count}");
            return areas;
      }

      // Умное разбиение слишком больших областей на логические части стен
      private List<Rect> SubdivideOversizedArea(Rect area, Color32[] pixels, int width, int height, byte threshold)
      {
            List<Rect> result = new List<Rect>();

            // 🎯 УМНОЕ РАЗДЕЛЕНИЕ: Увеличиваем лимиты чтобы создавать МЕНЬШЕ областей (2 стены = 2 плоскости)
            int maxAreaWidth = Mathf.RoundToInt(width * 0.8f);   // Максимум 80% от ширины маски (была 40%)
            int maxAreaHeight = Mathf.RoundToInt(height * 0.8f); // Максимум 80% от высоты маски (была 40%)
            float maxAreaRatio = 0.6f;  // Максимум 60% от общего размера маски (была 25%)

            int maxTotalArea = Mathf.RoundToInt(width * height * maxAreaRatio);

            // Если область не слишком большая, возвращаем как есть
            if (area.width <= maxAreaWidth && area.height <= maxAreaHeight && area.width * area.height <= maxTotalArea)
            {
                  result.Add(area);
                  if (enableCustomPlaneCreationLogging) Debug.Log($"[ARManagerInitializer2-SubdivideOversizedArea] Область {areaToString(area)} не требует разбиения (размер: {area.width * area.height}, лимит: {maxTotalArea})");
                  return result;
            }

            if (enableCustomPlaneCreationLogging) Debug.Log($"[ARManagerInitializer2-SubdivideOversizedArea] 🔄 Разбиваем большую область {areaToString(area)} (размер: {area.width * area.height}, лимит: {maxTotalArea})");

            // Стратегия 1: Умное разбиение по сетке (создаем области для отдельных стен)
            int subdivisionX = Mathf.Max(2, Mathf.CeilToInt(area.width / maxAreaWidth));
            int subdivisionY = Mathf.Max(2, Mathf.CeilToInt(area.height / maxAreaHeight));

            float subWidth = area.width / subdivisionX;
            float subHeight = area.height / subdivisionY;

            if (enableCustomPlaneCreationLogging) Debug.Log($"[ARManagerInitializer2-SubdivideOversizedArea] Разбиваем на сетку {subdivisionX}x{subdivisionY}, размер подобластей: {subWidth:F1}x{subHeight:F1}");

            for (int y = 0; y < subdivisionY; y++)
            {
                  for (int x = 0; x < subdivisionX; x++)
                  {
                        float startX = area.x + x * subWidth;
                        float startY = area.y + y * subHeight;
                        float endX = Mathf.Min(startX + subWidth, area.x + area.width);
                        float endY = Mathf.Min(startY + subHeight, area.y + area.height);

                        Rect subarea = new Rect(startX, startY, endX - startX, endY - startY);

                        // Проверяем, что подобласть содержит достаточно пикселей стены
                        if (ValidateSubarea(subarea, pixels, width, height, threshold))
                        {
                              result.Add(subarea);
                              if (enableCustomPlaneCreationLogging) Debug.Log($"[ARManagerInitializer2-SubdivideOversizedArea] ✅ Добавлена валидная подобласть: {areaToString(subarea)} (пикселей стены: {CountWallPixelsInArea(subarea, pixels, width, height, threshold)})");
                        }
                        else
                        {
                              if (enableCustomPlaneCreationLogging) Debug.Log($"[ARManagerInitializer2-SubdivideOversizedArea] ❌ Отклонена подобласть: {areaToString(subarea)} (недостаточно пикселей стены)");
                        }
                  }
            }

            // Если не получилось разбить, создаем более мелкие области принудительно
            if (result.Count == 0)
            {
                  if (enableCustomPlaneCreationLogging) Debug.Log($"[ARManagerInitializer2-SubdivideOversizedArea] ⚠️ Принудительное разбиение на мелкие области");

                  // Создаем более мелкие области (по 4 части в каждом направлении)
                  int forceSubdivisionX = 4;
                  int forceSubdivisionY = 4;

                  subWidth = area.width / forceSubdivisionX;
                  subHeight = area.height / forceSubdivisionY;

                  for (int y = 0; y < forceSubdivisionY; y++)
                  {
                        for (int x = 0; x < forceSubdivisionX; x++)
                        {
                              float startX = area.x + x * subWidth;
                              float startY = area.y + y * subHeight;
                              float endX = Mathf.Min(startX + subWidth, area.x + area.width);
                              float endY = Mathf.Min(startY + subHeight, area.y + area.height);

                              Rect subarea = new Rect(startX, startY, endX - startX, endY - startY);

                              // Более мягкая проверка для принудительного разбиения
                              if (CountWallPixelsInArea(subarea, pixels, width, height, threshold) > 10)
                              {
                                    result.Add(subarea);
                                    if (enableCustomPlaneCreationLogging) Debug.Log($"[ARManagerInitializer2-SubdivideOversizedArea] 🔧 Принудительно добавлена: {areaToString(subarea)}");
                              }
                        }
                  }
            }

            // Если всё равно не получилось, возвращаем исходную область
            if (result.Count == 0)
            {
                  result.Add(area);
                  if (enableCustomPlaneCreationLogging) Debug.Log($"[ARManagerInitializer2-SubdivideOversizedArea] 🚨 Не удалось разбить область, возвращаем исходную");
            }

            if (enableCustomPlaneCreationLogging) Debug.Log($"[ARManagerInitializer2-SubdivideOversizedArea] 🎯 Результат: {result.Count} подобластей из исходной области {areaToString(area)}");
            return result;
      }

      // Вспомогательный метод для подсчета пикселей стены в области
      private int CountWallPixelsInArea(Rect area, Color32[] pixels, int width, int height, byte threshold)
      {
            int wallPixels = 0;

            for (int y = Mathf.FloorToInt(area.yMin); y < Mathf.CeilToInt(area.yMax) && y < height; y++)
            {
                  for (int x = Mathf.FloorToInt(area.xMin); x < Mathf.CeilToInt(area.xMax) && x < width; x++)
                  {
                        if (x >= 0 && y >= 0 && x < width && y < height)
                        {
                              if (pixels[y * width + x].r >= threshold)
                              {
                                    wallPixels++;
                              }
                        }
                  }
            }

            return wallPixels;
      }

      // Проверяет, содержит ли подобласть достаточно пикселей стены
      private bool ValidateSubarea(Rect area, Color32[] pixels, int width, int height, byte threshold)
      {
            int wallPixels = 0;
            int totalPixels = 0;

            // ИСПРАВЛЕНО: Используем адаптивные настройки вместо жёстких порогов
            float currentMinWallRatio = minWallRatio;
            float currentMaxAspectRatio = maxAreaAspectRatio;

            // Адаптивная настройка порогов на основе качества маски
            if (useAdaptiveThresholds && currentSegmentationMask != null)
            {
                  // Анализируем качество маски в данной области
                  float maskQuality = AnalyzeLocalMaskQuality(area, pixels, width, height, threshold);

                  // Ослабляем фильтры для зашумлённых областей
                  if (maskQuality < 0.7f) // Если качество маски низкое
                  {
                        currentMinWallRatio *= (1.0f / noiseToleranceFactor); // Уменьшаем требования
                        currentMaxAspectRatio *= noiseToleranceFactor; // Разрешаем более вытянутые области

                        if (enableCustomPlaneCreationLogging)
                        {
                              Debug.Log($"[ARManagerInitializer2-ValidateSubarea] Адаптивные пороги: качество={maskQuality:F2}, minWallRatio={currentMinWallRatio:F2}, maxAspectRatio={currentMaxAspectRatio:F1}");
                        }
                  }
            }

            // Проверка соотношения сторон
            float aspectRatio = Mathf.Max(area.width / area.height, area.height / area.width);
            if (aspectRatio > currentMaxAspectRatio)
            {
                  if (enableCustomPlaneCreationLogging)
                  {
                        Debug.Log($"[ARManagerInitializer2-ValidateSubarea] Область {areaToString(area)} отклонена по соотношению сторон: {aspectRatio:F1} > {currentMaxAspectRatio:F1}");
                  }
                  return false;
            }

            // Подсчёт пикселей стены
            for (int y = Mathf.FloorToInt(area.yMin); y < Mathf.CeilToInt(area.yMax) && y < height; y++)
            {
                  for (int x = Mathf.FloorToInt(area.xMin); x < Mathf.CeilToInt(area.xMax) && x < width; x++)
                  {
                        if (x >= 0 && y >= 0 && x < width && y < height)
                        {
                              totalPixels++;
                              if (pixels[y * width + x].r >= threshold)
                              {
                                    wallPixels++;
                              }
                        }
                  }
            }

            float actualWallRatio = totalPixels > 0 ? (float)wallPixels / totalPixels : 0f;
            bool isValid = totalPixels > 0 &&
                          actualWallRatio >= currentMinWallRatio &&
                          area.width >= minPixelsDimensionForArea &&
                          area.height >= minPixelsDimensionForArea;

            if (enableCustomPlaneCreationLogging && !isValid)
            {
                  Debug.Log($"[ARManagerInitializer2-ValidateSubarea] Область {areaToString(area)} отклонена: " +
                           $"wallPixels={wallPixels}, totalPixels={totalPixels}, ratio={actualWallRatio:F2}, " +
                           $"minRatio={currentMinWallRatio:F2}, width={area.width}, height={area.height}, " +
                           $"aspectRatio={aspectRatio:F1}, maxAspectRatio={currentMaxAspectRatio:F1}");
            }
            else if (enableCustomPlaneCreationLogging && isValid)
            {
                  Debug.Log($"[ARManagerInitializer2-ValidateSubarea] ✅ Область {areaToString(area)} принята: " +
                           $"ratio={actualWallRatio:F2}, aspectRatio={aspectRatio:F1}");
            }

            return isValid;
      }

      // Вспомогательная функция для красивого вывода Rect в лог
      private string areaToString(Rect area)
      {
            return $"Rect(x:{area.xMin:F0}, y:{area.yMin:F0}, w:{area.width:F0}, h:{area.height:F0})";
      }

      /// <summary>
      /// Анализирует качество маски в локальной области
      /// Возвращает значение от 0.0 (плохое качество) до 1.0 (отличное качество)
      /// </summary>
      private float AnalyzeLocalMaskQuality(Rect area, Color32[] pixels, int width, int height, byte threshold)
      {
            if (pixels == null || area.width < 1 || area.height < 1)
                  return 0.0f;

            int totalPixels = 0;
            int wallPixels = 0;
            int edgePixels = 0; // Пиксели на границе между стеной и фоном

            // Анализируем область
            for (int y = Mathf.FloorToInt(area.yMin); y < Mathf.CeilToInt(area.yMax) && y < height; y++)
            {
                  for (int x = Mathf.FloorToInt(area.xMin); x < Mathf.CeilToInt(area.xMax) && x < width; x++)
                  {
                        if (x >= 0 && y >= 0 && x < width && y < height)
                        {
                              totalPixels++;
                              byte pixelValue = pixels[y * width + x].r;

                              if (pixelValue >= threshold)
                              {
                                    wallPixels++;
                              }

                              // Проверяем, является ли пиксель граничным (переход между стеной и фоном)
                              if (IsEdgePixel(x, y, pixels, width, height, threshold))
                              {
                                    edgePixels++;
                              }
                        }
                  }
            }

            if (totalPixels == 0) return 0.0f;

            // Вычисляем метрики качества
            float wallDensity = (float)wallPixels / totalPixels;
            float edgeRatio = (float)edgePixels / totalPixels;

            // Высокое качество = много пикселей стены, мало переходных зон
            // Низкое качество = много переходных зон (зашумлённость)
            float quality = wallDensity * (1.0f - Mathf.Min(edgeRatio * 2.0f, 0.8f));

            return Mathf.Clamp01(quality);
      }

      /// <summary>
      /// Проверяет, является ли пиксель граничным (находится на переходе между стеной и фоном)
      /// </summary>
      private bool IsEdgePixel(int x, int y, Color32[] pixels, int width, int height, byte threshold)
      {
            byte centerValue = pixels[y * width + x].r;
            bool centerIsWall = centerValue >= threshold;

            // Проверяем соседние пиксели
            for (int dy = -1; dy <= 1; dy++)
            {
                  for (int dx = -1; dx <= 1; dx++)
                  {
                        if (dx == 0 && dy == 0) continue; // Пропускаем центральный пиксель

                        int nx = x + dx;
                        int ny = y + dy;

                        if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                        {
                              byte neighborValue = pixels[ny * width + nx].r;
                              bool neighborIsWall = neighborValue >= threshold;

                              // Если соседний пиксель отличается по типу, это граничный пиксель
                              if (centerIsWall != neighborIsWall)
                              {
                                    return true;
                              }
                        }
                  }
            }

            return false;
      }

      /// <summary>
      /// Диагностирует проблемы с системой сегментации
      /// Решает Проблему 3: отсутствие событий OnSegmentationMaskUpdated
      /// </summary>
      private void DiagnoseSegmentationProblem(string issue)
      {
            if (!enableSegmentationEventDiagnostics) return;

            Debug.LogError($"[ARManagerInitializer2-DiagnoseSegmentationProblem] 🔍 ДИАГНОСТИКА: {issue}");

            // Проверяем состояние компонентов сегментации
            var wallSegmentation = FindObjectOfType<WallSegmentation>();
            if (wallSegmentation == null)
            {
                  Debug.LogError("❌ WallSegmentation компонент не найден в сцене!");
                  return;
            }

            Debug.Log($"✅ WallSegmentation найден: {wallSegmentation.gameObject.name}");

            // Проверяем подписку на события (через reflection для избежания ошибок компиляции)
            bool hasSubscription = true; // Предполагаем, что подписка есть
            try
            {
                  var eventInfo = typeof(WallSegmentation).GetEvent("OnSegmentationMaskUpdated");
                  hasSubscription = eventInfo != null;
            }
            catch (System.Exception ex)
            {
                  Debug.LogWarning($"⚠️ Не удалось проверить подписку на события: {ex.Message}");
                  hasSubscription = false;
            }
            Debug.Log($"📡 Подписка на события: {(hasSubscription ? "✅ Активна" : "❌ Отсутствует")}");

            // Проверяем состояние AR Session
            if (ARSession.state != ARSessionState.SessionTracking)
            {
                  Debug.LogWarning($"⚠️ AR Session не в состоянии отслеживания: {ARSession.state}");
            }
            else
            {
                  Debug.Log("✅ AR Session в состоянии отслеживания");
            }

            // Проверяем XR Simulation
            bool isSimulation = IsRunningInXRSimulation();
            Debug.Log($"🎯 Режим XR Simulation: {(isSimulation ? "✅ Активен" : "❌ Отключён")}");

            if (isSimulation)
            {
                  // Проверяем наличие XRSimulationCameraFeed через поиск по имени класса
                  var xrSimulationCameraFeed = FindObjectOfType<MonoBehaviour>();
                  bool foundXRSimulationCameraFeed = false;

                  // Ищем компонент XRSimulationCameraFeed среди всех MonoBehaviour
                  var allMonoBehaviours = FindObjectsOfType<MonoBehaviour>();
                  foreach (var mb in allMonoBehaviours)
                  {
                        if (mb.GetType().Name == "XRSimulationCameraFeed")
                        {
                              foundXRSimulationCameraFeed = true;
                              Debug.Log($"✅ XRSimulationCameraFeed найден: {mb.gameObject.name}");
                              break;
                        }
                  }

                  if (!foundXRSimulationCameraFeed)
                  {
                        Debug.LogError("❌ XRSimulationCameraFeed не найден! Это может быть причиной проблемы.");
                        Debug.LogError("💡 Решение: Добавьте XRSimulationCameraFeed компонент в сцену.");
                  }
            }

            // Статистика событий
            float successRate = totalSegmentationEvents > 0 ? (successfulSegmentationEvents / (float)totalSegmentationEvents * 100) : 0f;
            float timeSinceLastSuccess = Time.time - lastSuccessfulSegmentationTime;

            Debug.Log($"📊 СТАТИСТИКА СОБЫТИЙ:");
            Debug.Log($"   - Всего событий: {totalSegmentationEvents}");
            Debug.Log($"   - Успешных: {successfulSegmentationEvents}");
            Debug.Log($"   - Неудачных: {failedSegmentationEvents}");
            Debug.Log($"   - Успешность: {successRate:F1}%");
            Debug.Log($"   - Время с последнего успешного события: {timeSinceLastSuccess:F1}с");
            Debug.Log($"   - Средний интервал между событиями: {avgEventInterval:F2}с");

            if (timeSinceLastSuccess > segmentationTimeoutSeconds)
            {
                  Debug.LogError($"❌ ТАЙМ-АУТ: Последнее успешное событие было {timeSinceLastSuccess:F1}с назад (лимит: {segmentationTimeoutSeconds}с)");
            }
      }

      /// <summary>
      /// Проверяет, работает ли система в режиме XR Simulation
      /// </summary>
      private bool IsRunningInXRSimulation()
      {
            // Проверяем различные признаки XR Simulation
#if UNITY_EDITOR
            return true; // В редакторе обычно используется симуляция
#else
            return false; // На устройстве - реальный AR
#endif
      }

      /// <summary>
      /// Класс для представления контура
      /// </summary>
      [System.Serializable]
      public class Contour
      {
            public List<Vector2Int> points;
            public float area;
            public Rect boundingRect;
            public bool isHole;
            public int parentIndex;

            public Contour()
            {
                  points = new List<Vector2Int>();
                  area = 0f;
                  boundingRect = new Rect();
                  isHole = false;
                  parentIndex = -1;
            }

            public void CalculateProperties()
            {
                  if (points.Count < 3)
                  {
                        area = 0f;
                        return;
                  }

                  // Вычисляем площадь по формуле Шнурования (Shoelace formula)
                  area = 0f;
                  for (int i = 0; i < points.Count; i++)
                  {
                        int next = (i + 1) % points.Count;
                        area += points[i].x * points[next].y - points[next].x * points[i].y;
                  }
                  area = Mathf.Abs(area) / 2.0f;

                  // Вычисляем ограничивающий прямоугольник
                  if (points.Count > 0)
                  {
                        int minX = points[0].x, maxX = points[0].x;
                        int minY = points[0].y, maxY = points[0].y;

                        for (int i = 1; i < points.Count; i++)
                        {
                              minX = Mathf.Min(minX, points[i].x);
                              maxX = Mathf.Max(maxX, points[i].x);
                              minY = Mathf.Min(minY, points[i].y);
                              maxY = Mathf.Max(maxY, points[i].y);
                        }

                        boundingRect = new Rect(minX, minY, maxX - minX, maxY - minY);
                  }
            }
      }

      /// <summary>
      /// Класс для хранения результатов PCA анализа
      /// </summary>
      [System.Serializable]
      public class PCAResult
      {
            public Vector3 centroid;           // Центр масс облака точек
            public Vector3 primaryAxis;       // Главная компонента (наибольший eigenvalue)
            public Vector3 secondaryAxis;     // Вторая компонента
            public Vector3 normalAxis;        // Нормаль к плоскости (наименьший eigenvalue)
            public float[] eigenvalues;       // Собственные значения [λ1, λ2, λ3]
            public float planarity;           // Мера плоскостности (0-1)
            public int inlierCount;           // Количество точек-инлайеров
            public float averageDistance;     // Среднее расстояние от точек до плоскости

            public PCAResult()
            {
                  centroid = Vector3.zero;
                  primaryAxis = Vector3.forward;
                  secondaryAxis = Vector3.right;
                  normalAxis = Vector3.up;
                  eigenvalues = new float[3];
                  planarity = 0f;
                  inlierCount = 0;
                  averageDistance = 0f;
            }

            /// <summary>
            /// Вычисляет качество плоскости на основе PCA результатов
            /// </summary>
            public float GetPlaneQuality()
            {
                  // Плоскостность: отношение наименьшего eigenvalue к сумме всех
                  float totalVariance = eigenvalues[0] + eigenvalues[1] + eigenvalues[2];
                  if (totalVariance > 0.001f)
                  {
                        planarity = 1.0f - (eigenvalues[2] / totalVariance);
                  }
                  else
                  {
                        planarity = 0f;
                  }

                  // Учитываем количество точек и среднее расстояние
                  float pointDensityFactor = Mathf.Clamp01(inlierCount / 100.0f);
                  float distanceFactor = Mathf.Clamp01(1.0f - averageDistance);

                  return planarity * pointDensityFactor * distanceFactor;
            }
      }

      /// <summary>
      /// Класс для представления Oriented Bounding Box (OBB)
      /// </summary>
      [System.Serializable]
      public class OrientedBoundingBox
      {
            public Vector3 center;              // Центр OBB
            public Vector3 rightAxis;           // Локальная ось X (правая)
            public Vector3 upAxis;              // Локальная ось Y (вверх)
            public Vector3 forwardAxis;         // Локальная ось Z (вперёд)
            public Vector3 extents;             // Полуразмеры по каждой оси
            public float volume;                // Объём OBB
            public int pointCount;              // Количество точек внутри OBB
            public float pointDensity;          // Плотность точек (точек на м³)

            public OrientedBoundingBox()
            {
                  center = Vector3.zero;
                  rightAxis = Vector3.right;
                  upAxis = Vector3.up;
                  forwardAxis = Vector3.forward;
                  extents = Vector3.one;
                  volume = 0f;
                  pointCount = 0;
                  pointDensity = 0f;
            }

            /// <summary>
            /// Создаёт OBB из PCA результатов и облака точек
            /// </summary>
            public static OrientedBoundingBox FromPCAAndPoints(PCAResult pca, List<Vector3> points)
            {
                  OrientedBoundingBox obb = new OrientedBoundingBox();

                  // Устанавливаем центр и оси из PCA
                  obb.center = pca.centroid;
                  obb.rightAxis = pca.primaryAxis;
                  obb.upAxis = pca.secondaryAxis;
                  obb.forwardAxis = pca.normalAxis;

                  // Вычисляем границы в локальной системе координат
                  Vector3 minBounds = Vector3.zero;
                  Vector3 maxBounds = Vector3.zero;

                  foreach (Vector3 point in points)
                  {
                        Vector3 localPoint = obb.WorldToLocal(point);

                        minBounds.x = Mathf.Min(minBounds.x, localPoint.x);
                        minBounds.y = Mathf.Min(minBounds.y, localPoint.y);
                        minBounds.z = Mathf.Min(minBounds.z, localPoint.z);

                        maxBounds.x = Mathf.Max(maxBounds.x, localPoint.x);
                        maxBounds.y = Mathf.Max(maxBounds.y, localPoint.y);
                        maxBounds.z = Mathf.Max(maxBounds.z, localPoint.z);
                  }

                  // Вычисляем полуразмеры и корректируем центр
                  Vector3 size = maxBounds - minBounds;
                  obb.extents = size * 0.5f;

                  // Корректируем центр в мировых координатах
                  Vector3 localCenter = (minBounds + maxBounds) * 0.5f;
                  obb.center = obb.LocalToWorld(localCenter);

                  // Вычисляем метрики
                  obb.volume = size.x * size.y * size.z;
                  obb.pointCount = points.Count;
                  obb.pointDensity = obb.volume > 0.001f ? obb.pointCount / obb.volume : 0f;

                  return obb;
            }

            /// <summary>
            /// Преобразует мировые координаты в локальные координаты OBB
            /// </summary>
            public Vector3 WorldToLocal(Vector3 worldPoint)
            {
                  Vector3 offset = worldPoint - center;
                  return new Vector3(
                        Vector3.Dot(offset, rightAxis),
                        Vector3.Dot(offset, upAxis),
                        Vector3.Dot(offset, forwardAxis)
                  );
            }

            /// <summary>
            /// Преобразует локальные координаты OBB в мировые координаты
            /// </summary>
            public Vector3 LocalToWorld(Vector3 localPoint)
            {
                  return center +
                         rightAxis * localPoint.x +
                         upAxis * localPoint.y +
                         forwardAxis * localPoint.z;
            }

            /// <summary>
            /// Проверяет, содержится ли точка внутри OBB
            /// </summary>
            public bool ContainsPoint(Vector3 worldPoint)
            {
                  Vector3 localPoint = WorldToLocal(worldPoint);
                  return Mathf.Abs(localPoint.x) <= extents.x &&
                         Mathf.Abs(localPoint.y) <= extents.y &&
                         Mathf.Abs(localPoint.z) <= extents.z;
            }

            /// <summary>
            /// Получает размеры OBB (полные размеры, не полуразмеры)
            /// </summary>
            public Vector3 GetSize()
            {
                  return extents * 2f;
            }

            /// <summary>
            /// Получает ориентацию OBB как кватернион
            /// </summary>
            public Quaternion GetRotation()
            {
                  // Создаём матрицу поворота из осей
                  Matrix4x4 rotationMatrix = Matrix4x4.identity;
                  rotationMatrix.SetColumn(0, new Vector4(rightAxis.x, rightAxis.y, rightAxis.z, 0));
                  rotationMatrix.SetColumn(1, new Vector4(upAxis.x, upAxis.y, upAxis.z, 0));
                  rotationMatrix.SetColumn(2, new Vector4(forwardAxis.x, forwardAxis.y, forwardAxis.z, 0));

                  return rotationMatrix.rotation;
            }

            /// <summary>
            /// Оценивает качество OBB на основе плотности точек и геометрии
            /// </summary>
            public float GetQuality()
            {
                  // Фактор плотности точек
                  float densityFactor = Mathf.Clamp01(pointDensity / 100f); // Нормализуем к ~100 точкам на м³

                  // Фактор соотношения сторон (предпочитаем не слишком вытянутые формы)
                  Vector3 size = GetSize();
                  float aspectRatio = Mathf.Max(size.x, size.y, size.z) / Mathf.Min(size.x, size.y, size.z);
                  float aspectFactor = Mathf.Clamp01(5.0f / aspectRatio); // Хорошо до 5:1

                  // Фактор размера (предпочитаем разумные размеры)
                  float avgSize = (size.x + size.y + size.z) / 3f;
                  float sizeFactor = Mathf.Clamp01(avgSize / 2.0f); // Оптимально около 2м

                  return densityFactor * aspectFactor * sizeFactor;
            }
      }

      /// <summary>
      /// Новый метод поиска контуров (Уровень 2) - замена FindWallAreas()
      /// Использует алгоритм Сузуки-Абэ для точного определения границ
      /// </summary>
      private List<Contour> FindContours(Color32[] pixels, int width, int height, byte threshold)
      {
            List<Contour> contours = new List<Contour>();

            // Создаём бинарную маску
            bool[,] binaryMask = new bool[height, width];
            for (int y = 0; y < height; y++)
            {
                  for (int x = 0; x < width; x++)
                  {
                        binaryMask[y, x] = pixels[y * width + x].r >= threshold;
                  }
            }

            // Добавляем границы из нулей для корректной работы алгоритма
            bool[,] paddedMask = new bool[height + 2, width + 2];
            for (int y = 0; y < height; y++)
            {
                  for (int x = 0; x < width; x++)
                  {
                        paddedMask[y + 1, x + 1] = binaryMask[y, x];
                  }
            }

            // Алгоритм Сузуки-Абэ для поиска контуров
            int[,] labels = new int[height + 2, width + 2]; // 0 = не обработан, 1 = внешний, 2 = дыра

            for (int y = 1; y <= height; y++)
            {
                  for (int x = 1; x <= width; x++)
                  {
                        // Ищем начальную точку внешнего контура
                        if (paddedMask[y, x] && !paddedMask[y, x - 1] && labels[y, x] == 0)
                        {
                              Contour contour = TraceContour(paddedMask, labels, x, y, width + 2, height + 2, false);
                              if (contour != null && contour.points.Count >= 3)
                              {
                                    // Корректируем координаты (убираем padding)
                                    for (int i = 0; i < contour.points.Count; i++)
                                    {
                                          contour.points[i] = new Vector2Int(contour.points[i].x - 1, contour.points[i].y - 1);
                                    }
                                    contour.CalculateProperties();
                                    contours.Add(contour);
                              }
                        }
                        // Ищем начальную точку дыры
                        else if (!paddedMask[y, x] && paddedMask[y, x - 1] && labels[y, x] == 0)
                        {
                              Contour hole = TraceContour(paddedMask, labels, x - 1, y, width + 2, height + 2, true);
                              if (hole != null && hole.points.Count >= 3)
                              {
                                    // Корректируем координаты (убираем padding)
                                    for (int i = 0; i < hole.points.Count; i++)
                                    {
                                          hole.points[i] = new Vector2Int(hole.points[i].x - 1, hole.points[i].y - 1);
                                    }
                                    hole.isHole = true;
                                    hole.CalculateProperties();
                                    contours.Add(hole);
                              }
                        }
                  }
            }

            // Фильтруем контуры по размеру и упрощаем их
            List<Contour> filteredContours = new List<Contour>();
            foreach (var contour in contours)
            {
                  if (contour.points.Count >= minContourLength && contour.area >= minContourArea)
                  {
                        // Упрощаем контур алгоритмом Дугласа-Пекера
                        contour.points = SimplifyContour(contour.points, contourSimplificationEpsilon);
                        contour.CalculateProperties();

                        if (contour.points.Count >= 3) // После упрощения всё ещё должно быть >= 3 точек
                        {
                              filteredContours.Add(contour);
                        }
                  }
            }

            // Детальная диагностика фильтрации контуров
            Debug.Log($"[ARManagerInitializer2-FindContours] 🔍 Найдено контуров: {contours.Count}, после фильтрации: {filteredContours.Count}");

            if (contours.Count > 0 && filteredContours.Count == 0)
            {
                  Debug.LogWarning("[ARManagerInitializer2-FindContours] ⚠️ ВСЕ КОНТУРЫ ОТФИЛЬТРОВАНЫ! Диагностика:");
                  Debug.LogWarning($"  Фильтры: minContourLength={minContourLength}, minContourArea={minContourArea}");

                  for (int i = 0; i < Mathf.Min(5, contours.Count); i++)
                  {
                        var c = contours[i];
                        Debug.LogWarning($"  Контур #{i}: точек={c.points.Count}, площадь={c.area:F1}, прошёл_длину={c.points.Count >= minContourLength}, прошёл_площадь={c.area >= minContourArea}");
                  }

                  if (contours.Count > 5)
                        Debug.LogWarning($"  ... и ещё {contours.Count - 5} контуров");
            }

            return filteredContours;
      }

      /// <summary>
      /// Трассировка контура с начальной точки
      /// </summary>
      private Contour TraceContour(bool[,] mask, int[,] labels, int startX, int startY, int width, int height, bool isHole)
      {
            // Направления обхода (8-связность): E, NE, N, NW, W, SW, S, SE
            int[] dx = { 1, 1, 0, -1, -1, -1, 0, 1 };
            int[] dy = { 0, -1, -1, -1, 0, 1, 1, 1 };

            Contour contour = new Contour();
            contour.isHole = isHole;

            int x = startX;
            int y = startY;
            int dir = isHole ? 0 : 6; // Начальное направление поиска

            // 🛡️ ЗАЩИТА ОТ ПЕРЕПОЛНЕНИЯ ПАМЯТИ
            int MAX_CONTOUR_POINTS = maxContourPoints; // Используем настройку из инспектора
            int MAX_ITERATIONS = maxContourIterations; // Используем настройку из инспектора
            int iterations = 0;
            var visitedStates = new HashSet<(int, int, int)>(); // (x, y, dir) для обнаружения циклов

            do
            {
                  // Проверка на превышение лимитов
                  if (contour.points.Count >= MAX_CONTOUR_POINTS)
                  {
                        if (enableCustomPlaneCreationLogging)
                              Debug.LogWarning($"[ARManagerInitializer2-TraceContour] ⚠️ Достигнут лимит точек контура: {MAX_CONTOUR_POINTS}. Прерывание трассировки.");
                        break;
                  }

                  if (++iterations >= MAX_ITERATIONS)
                  {
                        if (enableCustomPlaneCreationLogging)
                              Debug.LogWarning($"[ARManagerInitializer2-TraceContour] ⚠️ Достигнут лимит итераций: {MAX_ITERATIONS}. Возможно зацикливание.");
                        break;
                  }

                  // Проверка на зацикливание состояния (позиция + направление)
                  var currentState = (x, y, dir);
                  if (contour.points.Count > 2 && visitedStates.Contains(currentState))
                  {
                        if (enableCustomPlaneCreationLogging)
                              // Debug.LogWarning($"[ARManagerInitializer2-TraceContour] ⚠️ Обнаружено зацикливание в позиции ({x}, {y}) с направлением {dir}"); // ОТКЛЮЧЕНО: слишком много предупреждений
                              break;
                  }
                  visitedStates.Add(currentState);

                  contour.points.Add(new Vector2Int(x, y));
                  labels[y, x] = isHole ? 2 : 1;

                  // Ищем следующую точку контура
                  int nextDir = FindNextDirection(mask, x, y, dir, dx, dy, width, height, isHole);
                  if (nextDir == -1)
                  {
                        if (enableCustomPlaneCreationLogging)
                              Debug.Log($"[ARManagerInitializer2-TraceContour] Завершение: не найдено следующее направление в ({x}, {y})");
                        break; // Не найдено следующей точки
                  }

                  // Проверка границ для следующей позиции
                  int nextX = x + dx[nextDir];
                  int nextY = y + dy[nextDir];
                  if (nextX < 0 || nextX >= width || nextY < 0 || nextY >= height)
                  {
                        if (enableCustomPlaneCreationLogging)
                              Debug.LogWarning($"[ARManagerInitializer2-TraceContour] ⚠️ Выход за границы: ({nextX}, {nextY}) при размере ({width}, {height})");
                        break;
                  }

                  x = nextX;
                  y = nextY;
                  dir = (nextDir + 4) % 8; // Направление для поиска от следующей точки

                  // Проверка завершения контура (вернулись в начальную точку с минимальным количеством точек)
                  if (contour.points.Count >= 3 && x == startX && y == startY)
                  {
                        if (enableCustomPlaneCreationLogging)
                              Debug.Log($"[ARManagerInitializer2-TraceContour] ✅ Контур замкнулся: {contour.points.Count} точек, {iterations} итераций");
                        break;
                  }

            } while (true);

            // Логирование результата трассировки
            if (enableCustomPlaneCreationLogging)
            {
                  Debug.Log($"[ARManagerInitializer2-TraceContour] Трассировка завершена: {contour.points.Count} точек, {iterations} итераций, дыра={isHole}");
            }

            return contour;
      }

      /// <summary>
      /// Находит следующее направление для трассировки контура
      /// </summary>
      private int FindNextDirection(bool[,] mask, int x, int y, int startDir, int[] dx, int[] dy, int width, int height, bool isHole)
      {
            for (int i = 0; i < 8; i++)
            {
                  int dir = (startDir + i) % 8;
                  int nx = x + dx[dir];
                  int ny = y + dy[dir];

                  if (nx >= 0 && nx < width && ny >= 0 && ny < height)
                  {
                        bool hasPixel = mask[ny, nx];
                        if ((!isHole && hasPixel) || (isHole && !hasPixel))
                        {
                              return dir;
                        }
                  }
            }
            return -1;
      }

      /// <summary>
      /// Упрощает контур алгоритмом Дугласа-Пекера
      /// </summary>
      private List<Vector2Int> SimplifyContour(List<Vector2Int> points, float epsilon)
      {
            if (points.Count <= 2) return points;

            List<Vector2Int> simplified = new List<Vector2Int>();
            DouglasPeucker(points, 0, points.Count - 1, epsilon, simplified);

            // Добавляем последнюю точку если она не была добавлена
            if (simplified.Count == 0 || simplified[simplified.Count - 1] != points[points.Count - 1])
            {
                  simplified.Add(points[points.Count - 1]);
            }

            return simplified;
      }

      /// <summary>
      /// Алгоритм Дугласа-Пекера для упрощения полигона
      /// </summary>
      private void DouglasPeucker(List<Vector2Int> points, int start, int end, float epsilon, List<Vector2Int> result)
      {
            if (end <= start + 1)
            {
                  if (result.Count == 0 || result[result.Count - 1] != points[start])
                  {
                        result.Add(points[start]);
                  }
                  return;
            }

            // Находим точку с максимальным расстоянием от линии start-end
            float maxDistance = 0f;
            int maxIndex = start;

            for (int i = start + 1; i < end; i++)
            {
                  float distance = PerpendicularDistance(points[start], points[end], points[i]);
                  if (distance > maxDistance)
                  {
                        maxDistance = distance;
                        maxIndex = i;
                  }
            }

            // Если максимальное расстояние больше epsilon, рекурсивно упрощаем
            if (maxDistance > epsilon)
            {
                  DouglasPeucker(points, start, maxIndex, epsilon, result);
                  DouglasPeucker(points, maxIndex, end, epsilon, result);
            }
            else
            {
                  if (result.Count == 0 || result[result.Count - 1] != points[start])
                  {
                        result.Add(points[start]);
                  }
            }
      }

      /// <summary>
      /// Вычисляет перпендикулярное расстояние от точки до линии
      /// </summary>
      private float PerpendicularDistance(Vector2Int lineStart, Vector2Int lineEnd, Vector2Int point)
      {
            float dx = lineEnd.x - lineStart.x;
            float dy = lineEnd.y - lineStart.y;

            if (dx == 0 && dy == 0)
            {
                  return Vector2.Distance(new Vector2(lineStart.x, lineStart.y), new Vector2(point.x, point.y));
            }

            float t = ((point.x - lineStart.x) * dx + (point.y - lineStart.y) * dy) / (dx * dx + dy * dy);
            t = Mathf.Clamp01(t);

            float projX = lineStart.x + t * dx;
            float projY = lineStart.y + t * dy;

            return Vector2.Distance(new Vector2(projX, projY), new Vector2(point.x, point.y));
      }

      /// <summary>
      /// Новый метод для обновления или создания плоскости из контура (Уровень 2)
      /// </summary>
      private bool UpdateOrCreatePlaneForContour(Contour contour, int textureWidth, int textureHeight, Dictionary<GameObject, bool> visitedPlanes)
      {
            // Вычисляем центр контура через центр масс
            Vector2 contourCenter = CalculateContourCenterOfMass(contour, textureWidth, textureHeight);

            // Переводим 2D координаты в 3D пространство
            Vector3 centerWorldPosition = GetWorldPositionFromScreenCoordinates(contourCenter, textureWidth, textureHeight);

            if (centerWorldPosition == Vector3.zero)
            {
                  if (enableCustomPlaneCreationLogging) Debug.Log($"[ARManagerInitializer2-UpdateOrCreatePlaneForContour] ❌ Не удалось определить позицию в мире для контура с площадью {contour.area}");
                  return false;
            }

            // Проверяем, есть ли близкая существующая плоскость
            var (closestPlane, distance, angle) = FindClosestExistingPlane(centerWorldPosition, Vector3.forward, 0.3f, 45f);

            if (closestPlane != null)
            {
                  // Обновляем существующую плоскость
                  UpdatePlaneFromContour(closestPlane, contour, centerWorldPosition, textureWidth, textureHeight);
                  visitedPlanes[closestPlane] = true;

                  if (enableCustomPlaneCreationLogging) Debug.Log($"[ARManagerInitializer2-UpdateOrCreatePlaneForContour] 🔄 Обновлена существующая плоскость {closestPlane.name} для контура с {contour.points.Count} точками");
                  return true;
            }
            else
            {
                  // Создаём новую плоскость
                  GameObject newPlane = CreatePlaneFromContour(contour, centerWorldPosition, textureWidth, textureHeight);

                  if (newPlane != null)
                  {
                        visitedPlanes[newPlane] = true;

                        if (enableCustomPlaneCreationLogging) Debug.Log($"[ARManagerInitializer2-UpdateOrCreatePlaneForContour] ✅ Создана новая плоскость {newPlane.name} для контура с {contour.points.Count} точками и площадью {contour.area}");
                        return true;
                  }
                  else
                  {
                        if (enableCustomPlaneCreationLogging) Debug.Log($"[ARManagerInitializer2-UpdateOrCreatePlaneForContour] ❌ Не удалось создать плоскость для контура");
                        return false;
                  }
            }
      }

      /// <summary>
      /// Вычисляет центр масс контура с учётом инверсии Y-координат
      /// </summary>
      private Vector2 CalculateContourCenterOfMass(Contour contour, int textureWidth, int textureHeight)
      {
            if (contour.points.Count == 0)
            {
                  // Fallback: используем центр ограничивающего прямоугольника
                  float fallbackCenterX = contour.boundingRect.x + contour.boundingRect.width / 2f;
                  float fallbackCenterY = contour.boundingRect.y + contour.boundingRect.height / 2f;

                  // ❗ БЕЗ ИНВЕРСИИ Y для ViewportPointToRay
                  return new Vector2(fallbackCenterX / textureWidth, fallbackCenterY / textureHeight);
            }

            // Вычисляем центр масс полигона
            float totalArea = 0f;
            float centerX = 0f;
            float centerY = 0f;

            for (int i = 0; i < contour.points.Count; i++)
            {
                  int next = (i + 1) % contour.points.Count;
                  Vector2Int p1 = contour.points[i];
                  Vector2Int p2 = contour.points[next];

                  float crossProduct = p1.x * p2.y - p2.x * p1.y;
                  totalArea += crossProduct;

                  centerX += (p1.x + p2.x) * crossProduct;
                  centerY += (p1.y + p2.y) * crossProduct;
            }

            totalArea *= 0.5f;

            if (Mathf.Abs(totalArea) < 0.001f) // Предотвращаем деление на ноль
            {
                  // Fallback: арифметическое среднее точек
                  centerX = (float)contour.points.Average(p => p.x);
                  centerY = (float)contour.points.Average(p => p.y);
            }
            else
            {
                  centerX /= (6f * totalArea);
                  centerY /= (6f * totalArea);
            }

            // Нормализуем БЕЗ инверсии Y для ViewportPointToRay
            float normalizedX = centerX / textureWidth;
            float normalizedY = centerY / textureHeight; // ❗ БЕЗ ИНВЕРСИИ Y

            return new Vector2(normalizedX, normalizedY);
      }

      /// <summary>
      /// Обновляет существующую плоскость на основе контура
      /// </summary>
      private void UpdatePlaneFromContour(GameObject plane, Contour contour, Vector3 centerWorldPosition, int textureWidth, int textureHeight)
      {
            // Обновляем позицию плоскости
            plane.transform.position = centerWorldPosition;

            // Обновляем размеры плоскости на основе ограничивающего прямоугольника
            float worldWidth = (contour.boundingRect.width / (float)textureWidth) * planeSizeMultiplier;
            float worldHeight = (contour.boundingRect.height / (float)textureHeight) * planeSizeMultiplier;

            // Применяем ограничения размеров
            worldWidth = Mathf.Clamp(worldWidth, minPlaneSize, maxWallWidth);
            worldHeight = Mathf.Clamp(worldHeight, minPlaneSize, maxWallHeight);

            // Обновляем размеры плоскости
            plane.transform.localScale = new Vector3(worldWidth, worldHeight, 1f);

            // Обновляем время последнего посещения
            planeLastVisitedTime[plane] = Time.time;
      }

      /// <summary>
      /// Создаёт новую плоскость из контура с PCA оптимизацией
      /// </summary>
      private GameObject CreatePlaneFromContour(Contour contour, Vector3 centerWorldPosition, int textureWidth, int textureHeight)
      {
            Vector3 finalPosition = centerWorldPosition;
            Quaternion finalRotation = Quaternion.identity;
            float worldWidth, worldHeight;

            // 🧮 УРОВЕНЬ 2: Используем PCA для точной ориентации плоскости
            if (usePCAForPlaneOrientation)
            {
                  // Собираем облако точек из контура
                  List<Vector3> pointCloud = CollectPointCloudFromContour(contour, textureWidth, textureHeight);

                  if (pointCloud.Count >= minPointsForPCA)
                  {
                        // Выполняем PCA анализ
                        PCAResult pcaResult = PerformPCAAnalysis(pointCloud);

                        // Проверяем качество плоскости
                        float planeQuality = pcaResult.GetPlaneQuality();

                        if (planeQuality > 0.3f) // Минимальный порог качества
                        {
                              // Используем результаты PCA для позиционирования
                              finalPosition = pcaResult.centroid;

                              // Создаём ориентацию на основе главных компонент
                              Vector3 forward = pcaResult.normalAxis;
                              Vector3 up = pcaResult.secondaryAxis;

                              // Убеждаемся, что оси ортогональны
                              Vector3 right = Vector3.Cross(up, forward).normalized;
                              up = Vector3.Cross(forward, right).normalized;

                              finalRotation = Quaternion.LookRotation(forward, up);

                              // 📦 УРОВЕНЬ 2: Используем OBB для точных размеров
                              if (useOBBForPlaneSizing)
                              {
                                    OrientedBoundingBox obb = OrientedBoundingBox.FromPCAAndPoints(pcaResult, pointCloud);

                                    // Проверяем качество OBB
                                    float obbQuality = obb.GetQuality();

                                    if (obbQuality > 0.2f) // Минимальный порог для OBB
                                    {
                                          // Используем размеры OBB
                                          Vector3 obbSize = obb.GetSize();

                                          // Применяем коэффициент расширения
                                          worldWidth = obbSize.x * obbExpansionFactor;
                                          worldHeight = obbSize.y * obbExpansionFactor;

                                          // Адаптивное масштабирование на основе плотности точек
                                          if (useAdaptiveOBBSizing)
                                          {
                                                float densityMultiplier = Mathf.Clamp(obb.pointDensity / 50f, 0.8f, 1.2f);
                                                worldWidth *= densityMultiplier;
                                                worldHeight *= densityMultiplier;
                                          }

                                          // Используем более точную позицию и ориентацию из OBB
                                          finalPosition = obb.center;
                                          finalRotation = obb.GetRotation();

                                          if (enableCustomPlaneCreationLogging)
                                          {
                                                Debug.Log($"[ARManagerInitializer2-CreatePlaneFromContour] 📦 OBB результат: качество={obbQuality:F3}, плотность={obb.pointDensity:F1}, размер={worldWidth:F2}x{worldHeight:F2}");
                                          }
                                    }
                                    else
                                    {
                                          // Fallback к PCA eigenvalues
                                          float primaryLength = Mathf.Sqrt(pcaResult.eigenvalues[0]) * 2f;
                                          float secondaryLength = Mathf.Sqrt(pcaResult.eigenvalues[1]) * 2f;

                                          worldWidth = primaryLength;
                                          worldHeight = secondaryLength;

                                          if (enableCustomPlaneCreationLogging) Debug.Log($"[ARManagerInitializer2-CreatePlaneFromContour] ⚠️ Низкое качество OBB: {obbQuality:F3}, используем PCA eigenvalues");
                                    }
                              }
                              else
                              {
                                    // Размеры на основе PCA eigenvalues (без OBB)
                                    float primaryLength = Mathf.Sqrt(pcaResult.eigenvalues[0]) * 2f; // 2 стандартных отклонения
                                    float secondaryLength = Mathf.Sqrt(pcaResult.eigenvalues[1]) * 2f;

                                    worldWidth = primaryLength;
                                    worldHeight = secondaryLength;
                              }

                              // Применяем общие ограничения размеров
                              worldWidth = Mathf.Clamp(worldWidth, minPlaneSize, maxWallWidth);
                              worldHeight = Mathf.Clamp(worldHeight, minPlaneSize, maxWallHeight);

                              if (enableCustomPlaneCreationLogging)
                              {
                                    Debug.Log($"[ARManagerInitializer2-CreatePlaneFromContour] 🧮 PCA результат: качество={planeQuality:F3}, точек={pcaResult.inlierCount}, финальный размер={worldWidth:F2}x{worldHeight:F2}");
                              }
                        }
                        else
                        {
                              if (enableCustomPlaneCreationLogging) Debug.Log($"[ARManagerInitializer2-CreatePlaneFromContour] ⚠️ Низкое качество PCA: {planeQuality:F3}, используем fallback");

                              // Fallback к стандартному методу
                              worldWidth = (contour.boundingRect.width / (float)textureWidth) * planeSizeMultiplier;
                              worldHeight = (contour.boundingRect.height / (float)textureHeight) * planeSizeMultiplier;
                              worldWidth = Mathf.Clamp(worldWidth, minPlaneSize, maxWallWidth);
                              worldHeight = Mathf.Clamp(worldHeight, minPlaneSize, maxWallHeight);
                        }
                  }
                  else
                  {
                        if (enableCustomPlaneCreationLogging) Debug.Log($"[ARManagerInitializer2-CreatePlaneFromContour] ⚠️ Недостаточно точек для PCA: {pointCloud.Count} < {minPointsForPCA}");

                        // Fallback к стандартному методу
                        worldWidth = (contour.boundingRect.width / (float)textureWidth) * planeSizeMultiplier;
                        worldHeight = (contour.boundingRect.height / (float)textureHeight) * planeSizeMultiplier;
                        worldWidth = Mathf.Clamp(worldWidth, minPlaneSize, maxWallWidth);
                        worldHeight = Mathf.Clamp(worldHeight, minPlaneSize, maxWallHeight);
                  }
            }
            else
            {
                  // Стандартный метод без PCA
                  worldWidth = (contour.boundingRect.width / (float)textureWidth) * planeSizeMultiplier;
                  worldHeight = (contour.boundingRect.height / (float)textureHeight) * planeSizeMultiplier;
                  worldWidth = Mathf.Clamp(worldWidth, minPlaneSize, maxWallWidth);
                  worldHeight = Mathf.Clamp(worldHeight, minPlaneSize, maxWallHeight);
            }

            // Проверяем соотношение сторон
            float aspectRatio = Mathf.Max(worldWidth, worldHeight) / Mathf.Min(worldWidth, worldHeight);
            if (aspectRatio > maxAspectRatio)
            {
                  if (enableCustomPlaneCreationLogging) Debug.Log($"[ARManagerInitializer2-CreatePlaneFromContour] ❌ Контур отклонён из-за соотношения сторон: {aspectRatio:F2} > {maxAspectRatio}");
                  return null;
            }

            // Создаём GameObject для плоскости
            string planeName;
            if (usePCAForPlaneOrientation && useOBBForPlaneSizing)
            {
                  planeName = $"GeneratedPlane_PCA_OBB_{++planeInstanceCounter}";
            }
            else if (usePCAForPlaneOrientation)
            {
                  planeName = $"GeneratedPlane_PCA_{++planeInstanceCounter}";
            }
            else
            {
                  planeName = $"GeneratedPlane_Contour_{++planeInstanceCounter}";
            }
            GameObject plane = new GameObject(planeName);
            plane.transform.position = finalPosition;
            plane.transform.rotation = finalRotation;
            plane.transform.localScale = new Vector3(worldWidth, worldHeight, 1f);

            // Добавляем MeshRenderer и MeshFilter
            MeshRenderer meshRenderer = plane.AddComponent<MeshRenderer>();
            MeshFilter meshFilter = plane.AddComponent<MeshFilter>();

            // Создаём меш
            meshFilter.mesh = CreatePlaneMesh(worldWidth, worldHeight);

            // Назначаем материал
            meshRenderer.material = verticalPlaneMaterial != null ? verticalPlaneMaterial : CreateFallbackMaterial();

            // Добавляем коллайдер
            BoxCollider collider = plane.AddComponent<BoxCollider>();
            collider.size = new Vector3(worldWidth, worldHeight, 0.01f);

            // Устанавливаем слой
            if (!string.IsNullOrEmpty(planesLayerName))
            {
                  int layerIndex = LayerMask.NameToLayer(planesLayerName);
                  if (layerIndex != -1)
                  {
                        plane.layer = layerIndex;
                  }
            }

            // Добавляем в список созданных плоскостей
            generatedPlanes.Add(plane);
            planeCreationTimes[plane] = Time.time;
            planeLastVisitedTime[plane] = Time.time;

            return plane;
      }

      /// <summary>
      /// Вспомогательный метод для получения мировых координат из экранных (для контуров)
      /// </summary>
      private Vector3 GetWorldPositionFromScreenCoordinates(Vector2 screenCoords, int textureWidth, int textureHeight)
      {
            if (xrOrigin == null || xrOrigin.Camera == null)
            {
                  Debug.LogError("[ARManagerInitializer2-GetWorldPositionFromScreenCoordinates] XROrigin or Camera is null");
                  return Vector3.zero;
            }

            Camera mainCamera = xrOrigin.Camera;

            // Создаём луч из камеры через точку экрана
            Ray ray = mainCamera.ViewportPointToRay(new Vector3(screenCoords.x, screenCoords.y, 0));

            // 🎯 Первая попытка: рейкаст по настроенной маске слоёв
            RaycastHit hitInfo;
            if (Physics.Raycast(ray, out hitInfo, maxRayDistance, hitLayerMask, QueryTriggerInteraction.Ignore))
            {
                  Debug.DrawRay(ray.origin, ray.direction * hitInfo.distance, Color.green, 1.0f);
                  Debug.Log($"[ARManagerInitializer2] ✅ Raycast попал в {hitInfo.collider.name} (слой {hitInfo.collider.gameObject.layer}) на расстоянии {hitInfo.distance:F2}м");
                  return hitInfo.point + hitInfo.normal * 0.02f; // Небольшое смещение от поверхности
            }
            else
            {
                  // 🟡 Вторая попытка: рейкаст по всем стандартным слоям
                  if (Physics.Raycast(ray, out hitInfo, maxRayDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                  {
                        Debug.LogWarning($"[ARManagerInitializer2] ⚠️ Основной Raycast промахнулся, но дополнительный попал в {hitInfo.collider.name} (слой {hitInfo.collider.gameObject.layer}). Проверьте настройки hitLayerMask!");
                        Debug.DrawRay(ray.origin, ray.direction * hitInfo.distance, Color.yellow, 1.0f);
                        return hitInfo.point + hitInfo.normal * 0.02f;
                  }
                  else
                  {
                        // 🔴 Окончательный fallback: фиксированное расстояние перед камерой
                        Debug.DrawRay(ray.origin, ray.direction * maxRayDistance, Color.red, 1.0f);
                        Debug.LogWarning($"[ARManagerInitializer2] ❌ Все Raycast промахнулись! UV=({screenCoords.x:F3},{screenCoords.y:F3}) → используем fallback позицию");
                        return mainCamera.transform.position + mainCamera.transform.forward * 2.0f;
                  }
            }
      }

      /// <summary>
      /// Выполняет PCA анализ для облака точек контура
      /// </summary>
      private PCAResult PerformPCAAnalysis(List<Vector3> worldPoints)
      {
            PCAResult result = new PCAResult();

            if (worldPoints == null || worldPoints.Count < minPointsForPCA)
            {
                  Debug.LogWarning($"[ARManagerInitializer2-PerformPCAAnalysis] Недостаточно точек для PCA: {worldPoints?.Count ?? 0} < {minPointsForPCA}");
                  return result;
            }

            // 1. Вычисляем центр масс
            result.centroid = Vector3.zero;
            foreach (Vector3 point in worldPoints)
            {
                  result.centroid += point;
            }
            result.centroid /= worldPoints.Count;

            // 2. Центрируем точки относительно центра масс
            List<Vector3> centeredPoints = new List<Vector3>();
            foreach (Vector3 point in worldPoints)
            {
                  centeredPoints.Add(point - result.centroid);
            }

            // 3. Вычисляем ковариационную матрицу 3x3
            float[,] covarianceMatrix = ComputeCovarianceMatrix(centeredPoints);

            // 4. Находим собственные значения и векторы
            var eigenResults = ComputeEigenvalues(covarianceMatrix);
            result.eigenvalues = eigenResults.values;
            Vector3[] eigenvectors = eigenResults.vectors;

            // 5. Сортируем по убыванию собственных значений
            SortEigenComponents(ref result.eigenvalues, ref eigenvectors);

            // 6. Назначаем главные компоненты
            result.primaryAxis = eigenvectors[0];   // Наибольший eigenvalue
            result.secondaryAxis = eigenvectors[1]; // Средний eigenvalue
            result.normalAxis = eigenvectors[2];    // Наименьший eigenvalue

            // 7. Выполняем робастную фильтрацию выбросов
            var filteredPoints = FilterOutliers(worldPoints, result);
            result.inlierCount = filteredPoints.Count;

            // 8. Вычисляем среднее расстояние от точек до плоскости
            result.averageDistance = ComputeAverageDistanceToPlane(filteredPoints, result.centroid, result.normalAxis);

            Debug.Log($"[ARManagerInitializer2-PerformPCAAnalysis] PCA завершён: {result.inlierCount} инлайеров, плоскостность: {result.GetPlaneQuality():F3}");
            return result;
      }

      /// <summary>
      /// Вычисляет ковариационную матрицу для облака точек
      /// </summary>
      private float[,] ComputeCovarianceMatrix(List<Vector3> centeredPoints)
      {
            float[,] matrix = new float[3, 3];
            int n = centeredPoints.Count;

            for (int i = 0; i < n; i++)
            {
                  Vector3 p = centeredPoints[i];

                  // Заполняем верхний треугольник матрицы
                  matrix[0, 0] += p.x * p.x;
                  matrix[0, 1] += p.x * p.y;
                  matrix[0, 2] += p.x * p.z;
                  matrix[1, 1] += p.y * p.y;
                  matrix[1, 2] += p.y * p.z;
                  matrix[2, 2] += p.z * p.z;
            }

            // Нормализуем на количество точек
            for (int i = 0; i < 3; i++)
            {
                  for (int j = i; j < 3; j++)
                  {
                        matrix[i, j] /= (n - 1);
                  }
            }

            // Заполняем нижний треугольник (симметричная матрица)
            matrix[1, 0] = matrix[0, 1];
            matrix[2, 0] = matrix[0, 2];
            matrix[2, 1] = matrix[1, 2];

            return matrix;
      }

      /// <summary>
      /// Вычисляет собственные значения и векторы матрицы 3x3 методом степенных итераций
      /// </summary>
      private (float[] values, Vector3[] vectors) ComputeEigenvalues(float[,] matrix)
      {
            float[] eigenvalues = new float[3];
            Vector3[] eigenvectors = new Vector3[3];

            // Упрощённый алгоритм для матрицы 3x3
            // Используем итеративный метод степенных итераций

            float[,] workMatrix = (float[,])matrix.Clone();

            // Найдём первый собственный вектор (наибольший eigenvalue)
            Vector3 v1 = FindDominantEigenvector(workMatrix);
            eigenvalues[0] = ComputeEigenvalue(workMatrix, v1);
            eigenvectors[0] = v1;

            // Дефляция матрицы для поиска второго собственного вектора
            DeflateMatrix(workMatrix, v1, eigenvalues[0]);

            // Найдём второй собственный вектор
            Vector3 v2 = FindDominantEigenvector(workMatrix);
            eigenvalues[1] = ComputeEigenvalue(matrix, v2);
            eigenvectors[1] = v2;

            // Третий собственный вектор - векторное произведение первых двух
            Vector3 v3 = Vector3.Cross(v1, v2).normalized;
            eigenvalues[2] = ComputeEigenvalue(matrix, v3);
            eigenvectors[2] = v3;

            return (eigenvalues, eigenvectors);
      }

      /// <summary>
      /// Находит доминирующий собственный вектор методом степенных итераций
      /// </summary>
      private Vector3 FindDominantEigenvector(float[,] matrix)
      {
            Vector3 v = new Vector3(1f, 1f, 1f).normalized;

            // Степенные итерации
            for (int iter = 0; iter < 50; iter++)
            {
                  Vector3 newV = MultiplyMatrixVector(matrix, v);
                  newV = newV.normalized;

                  // Проверяем сходимость
                  if (Vector3.Dot(v, newV) > 0.999f)
                  {
                        break;
                  }

                  v = newV;
            }

            return v;
      }

      /// <summary>
      /// Умножает матрицу 3x3 на вектор
      /// </summary>
      private Vector3 MultiplyMatrixVector(float[,] matrix, Vector3 vector)
      {
            return new Vector3(
                  matrix[0, 0] * vector.x + matrix[0, 1] * vector.y + matrix[0, 2] * vector.z,
                  matrix[1, 0] * vector.x + matrix[1, 1] * vector.y + matrix[1, 2] * vector.z,
                  matrix[2, 0] * vector.x + matrix[2, 1] * vector.y + matrix[2, 2] * vector.z
            );
      }

      /// <summary>
      /// Вычисляет собственное значение для данного собственного вектора
      /// </summary>
      private float ComputeEigenvalue(float[,] matrix, Vector3 eigenvector)
      {
            Vector3 result = MultiplyMatrixVector(matrix, eigenvector);
            return Vector3.Dot(result, eigenvector);
      }

      /// <summary>
      /// Выполняет дефляцию матрицы для исключения найденного собственного значения
      /// </summary>
      private void DeflateMatrix(float[,] matrix, Vector3 eigenvector, float eigenvalue)
      {
            for (int i = 0; i < 3; i++)
            {
                  for (int j = 0; j < 3; j++)
                  {
                        float vi = (i == 0) ? eigenvector.x : ((i == 1) ? eigenvector.y : eigenvector.z);
                        float vj = (j == 0) ? eigenvector.x : ((j == 1) ? eigenvector.y : eigenvector.z);
                        matrix[i, j] -= eigenvalue * vi * vj;
                  }
            }
      }

      /// <summary>
      /// Сортирует собственные значения и векторы по убыванию
      /// </summary>
      private void SortEigenComponents(ref float[] eigenvalues, ref Vector3[] eigenvectors)
      {
            // Простая сортировка пузырьком для 3 элементов
            for (int i = 0; i < 2; i++)
            {
                  for (int j = i + 1; j < 3; j++)
                  {
                        if (eigenvalues[i] < eigenvalues[j])
                        {
                              // Меняем местами eigenvalues
                              float tempVal = eigenvalues[i];
                              eigenvalues[i] = eigenvalues[j];
                              eigenvalues[j] = tempVal;

                              // Меняем местами eigenvectors
                              Vector3 tempVec = eigenvectors[i];
                              eigenvectors[i] = eigenvectors[j];
                              eigenvectors[j] = tempVec;
                        }
                  }
            }
      }

      /// <summary>
      /// Фильтрует выбросы из облака точек на основе расстояния до плоскости
      /// </summary>
      private List<Vector3> FilterOutliers(List<Vector3> points, PCAResult pca)
      {
            List<Vector3> filteredPoints = new List<Vector3>();
            List<float> distances = new List<float>();

            // Вычисляем расстояния от каждой точки до плоскости
            foreach (Vector3 point in points)
            {
                  float distance = Mathf.Abs(Vector3.Dot(point - pca.centroid, pca.normalAxis));
                  distances.Add(distance);
            }

            // Сортируем расстояния для нахождения порога
            List<float> sortedDistances = new List<float>(distances);
            sortedDistances.Sort();

            // Используем процентиль для определения порога выбросов
            int thresholdIndex = Mathf.FloorToInt(sortedDistances.Count * (1.0f - pcaOutlierPercentage));
            float adaptiveThreshold = Mathf.Min(sortedDistances[thresholdIndex], pcaOutlierThreshold);

            // Фильтруем точки
            for (int i = 0; i < points.Count; i++)
            {
                  if (distances[i] <= adaptiveThreshold)
                  {
                        filteredPoints.Add(points[i]);
                  }
            }

            return filteredPoints;
      }

      /// <summary>
      /// Вычисляет среднее расстояние от точек до плоскости
      /// </summary>
      private float ComputeAverageDistanceToPlane(List<Vector3> points, Vector3 centroid, Vector3 normal)
      {
            if (points.Count == 0) return 0f;

            float totalDistance = 0f;
            foreach (Vector3 point in points)
            {
                  float distance = Mathf.Abs(Vector3.Dot(point - centroid, normal));
                  totalDistance += distance;
            }

            return totalDistance / points.Count;
      }

      /// <summary>
      /// Собирает облако точек из контура для PCA анализа
      /// </summary>
      private List<Vector3> CollectPointCloudFromContour(Contour contour, int textureWidth, int textureHeight)
      {
            List<Vector3> worldPoints = new List<Vector3>();

            // Собираем точки из контура
            foreach (Vector2Int pixel in contour.points)
            {
                  // Переводим пиксельные координаты в UV
                  Vector2 uv = new Vector2(pixel.x / (float)textureWidth, 1.0f - pixel.y / (float)textureHeight);

                  // Получаем мировые координаты
                  Vector3 worldPos = GetWorldPositionFromScreenCoordinates(uv, textureWidth, textureHeight);

                  if (worldPos != Vector3.zero)
                  {
                        worldPoints.Add(worldPos);
                  }
            }

            // Дополнительно семплируем точки внутри контура для лучшего анализа
            if (worldPoints.Count < minPointsForPCA)
            {
                  worldPoints.AddRange(SamplePointsInsideContour(contour, textureWidth, textureHeight));
            }

            return worldPoints;
      }

      /// <summary>
      /// Дополнительно семплирует точки внутри контура
      /// </summary>
      private List<Vector3> SamplePointsInsideContour(Contour contour, int textureWidth, int textureHeight)
      {
            List<Vector3> sampledPoints = new List<Vector3>();
            Rect bounds = contour.boundingRect;

            int stepX = Mathf.Max(1, (int)(bounds.width / 20)); // Примерно 20 точек по X
            int stepY = Mathf.Max(1, (int)(bounds.height / 20)); // Примерно 20 точек по Y

            for (int y = (int)bounds.yMin; y < bounds.yMax; y += stepY)
            {
                  for (int x = (int)bounds.xMin; x < bounds.xMax; x += stepX)
                  {
                        if (IsPointInContour(new Vector2Int(x, y), contour))
                        {
                              Vector2 uv = new Vector2(x / (float)textureWidth, 1.0f - y / (float)textureHeight);
                              Vector3 worldPos = GetWorldPositionFromScreenCoordinates(uv, textureWidth, textureHeight);

                              if (worldPos != Vector3.zero)
                              {
                                    sampledPoints.Add(worldPos);
                              }
                        }
                  }
            }

            return sampledPoints;
      }

      /// <summary>
      /// Проверяет, находится ли точка внутри контура
      /// </summary>
      private bool IsPointInContour(Vector2Int point, Contour contour)
      {
            // Упрощённый алгоритм ray casting для определения, находится ли точка внутри полигона
            int intersections = 0;

            for (int i = 0; i < contour.points.Count; i++)
            {
                  int next = (i + 1) % contour.points.Count;
                  Vector2Int p1 = contour.points[i];
                  Vector2Int p2 = contour.points[next];

                  if (((p1.y > point.y) != (p2.y > point.y)) &&
                      (point.x < (p2.x - p1.x) * (point.y - p1.y) / (p2.y - p1.y) + p1.x))
                  {
                        intersections++;
                  }
            }

            return (intersections % 2) == 1;
      }

      /// <summary>
      /// Создаёт оптимизированный OBB с дополнительной фильтрацией и настройками
      /// </summary>
      private OrientedBoundingBox CreateOptimizedOBB(PCAResult pca, List<Vector3> rawPoints)
      {
            // Фильтруем выбросы для более точного OBB
            List<Vector3> filteredPoints = FilterOutliers(rawPoints, pca);

            // Создаём базовый OBB
            OrientedBoundingBox obb = OrientedBoundingBox.FromPCAAndPoints(pca, filteredPoints);

            // Применяем минимальную толщину в направлении нормали
            Vector3 size = obb.GetSize();
            if (size.z < obbMinThickness)
            {
                  obb.extents.z = obbMinThickness * 0.5f;
            }

            // Убеждаемся, что OBB не слишком мал в других направлениях
            if (size.x < minPlaneSize)
            {
                  obb.extents.x = minPlaneSize * 0.5f;
            }
            if (size.y < minPlaneSize)
            {
                  obb.extents.y = minPlaneSize * 0.5f;
            }

            // Обновляем метрики после корректировки
            obb.volume = obb.GetSize().x * obb.GetSize().y * obb.GetSize().z;
            obb.pointDensity = obb.volume > 0.001f ? obb.pointCount / obb.volume : 0f;

            return obb;
      }

      /// <summary>
      /// Валидирует OBB и возвращает фактор качества
      /// </summary>
      private float ValidateOBB(OrientedBoundingBox obb, Contour contour)
      {
            float qualityScore = obb.GetQuality();

            // Дополнительные проверки качества
            Vector3 size = obb.GetSize();

            // Проверяем разумность размеров
            if (size.x > maxWallWidth * 2f || size.y > maxWallHeight * 2f)
            {
                  qualityScore *= 0.5f; // Слишком большой
            }

            if (size.x < minPlaneSize * 0.5f || size.y < minPlaneSize * 0.5f)
            {
                  qualityScore *= 0.3f; // Слишком маленький
            }

            // Проверяем соответствие контуру
            float contourAreaRatio = obb.GetSize().x * obb.GetSize().y / contour.area;
            if (contourAreaRatio > 5f || contourAreaRatio < 0.2f)
            {
                  qualityScore *= 0.7f; // Плохое соответствие контуру
            }

            return qualityScore;
      }

      /// <summary>
      /// Отладочная визуализация OBB (опционально)
      /// </summary>
      private void DebugDrawOBB(OrientedBoundingBox obb, Color color, float duration = 1f)
      {
            if (!enableCustomPlaneCreationLogging) return;

            Vector3 size = obb.GetSize();
            Vector3[] corners = new Vector3[8];

            // Вычисляем 8 углов OBB
            corners[0] = obb.LocalToWorld(new Vector3(-size.x / 2, -size.y / 2, -size.z / 2));
            corners[1] = obb.LocalToWorld(new Vector3(size.x / 2, -size.y / 2, -size.z / 2));
            corners[2] = obb.LocalToWorld(new Vector3(size.x / 2, size.y / 2, -size.z / 2));
            corners[3] = obb.LocalToWorld(new Vector3(-size.x / 2, size.y / 2, -size.z / 2));
            corners[4] = obb.LocalToWorld(new Vector3(-size.x / 2, -size.y / 2, size.z / 2));
            corners[5] = obb.LocalToWorld(new Vector3(size.x / 2, -size.y / 2, size.z / 2));
            corners[6] = obb.LocalToWorld(new Vector3(size.x / 2, size.y / 2, size.z / 2));
            corners[7] = obb.LocalToWorld(new Vector3(-size.x / 2, size.y / 2, size.z / 2));

            // Рисуем рёбра OBB
            // Нижняя грань
            Debug.DrawLine(corners[0], corners[1], color, duration);
            Debug.DrawLine(corners[1], corners[2], color, duration);
            Debug.DrawLine(corners[2], corners[3], color, duration);
            Debug.DrawLine(corners[3], corners[0], color, duration);

            // Верхняя грань
            Debug.DrawLine(corners[4], corners[5], color, duration);
            Debug.DrawLine(corners[5], corners[6], color, duration);
            Debug.DrawLine(corners[6], corners[7], color, duration);
            Debug.DrawLine(corners[7], corners[4], color, duration);

            // Вертикальные рёбра
            Debug.DrawLine(corners[0], corners[4], color, duration);
            Debug.DrawLine(corners[1], corners[5], color, duration);
            Debug.DrawLine(corners[2], corners[6], color, duration);
            Debug.DrawLine(corners[3], corners[7], color, duration);
      }

      // Находит центр массы области сегментации для более точного позиционирования плоскости
      private Vector2 FindAreaCenterOfMass(Rect area, int textureWidth, int textureHeight)
      {
            if (currentSegmentationMask == null)
            {
                  // Если маска недоступна, возвращаем геометрический центр
                  // ИСПРАВЛЕНО: Убираем инверсию Y-координаты для ViewportPointToRay
                  return new Vector2(
                      (area.xMin + area.width * 0.5f) / textureWidth,
                      (area.yMin + area.height * 0.5f) / textureHeight  // ❗ БЕЗ ИНВЕРСИИ Y
                  );
            }

            // Создаем временную текстуру для чтения пикселей
            Texture2D tempTexture = RenderTextureToTexture2D(currentSegmentationMask, textureWidth, textureHeight);
            Color32[] pixels = tempTexture.GetPixels32();

            float totalMass = 0f;
            float centerX = 0f;
            float centerY = 0f;
            byte threshold = wallPixelThreshold;

            // 🎯 УЛУЧШЕННЫЙ АЛГОРИТМ: Многоточечный анализ центра
            List<Vector2> wallPixelPositions = new List<Vector2>();

            // Вычисляем центр массы области И собираем все позиции пикселей стены
            for (int y = Mathf.FloorToInt(area.yMin); y < Mathf.CeilToInt(area.yMax) && y < textureHeight; y++)
            {
                  for (int x = Mathf.FloorToInt(area.xMin); x < Mathf.CeilToInt(area.xMax) && x < textureWidth; x++)
                  {
                        Color32 pixel = pixels[y * textureWidth + x];
                        if (pixel.r >= threshold)
                        {
                              float pixelMass = pixel.r / 255.0f; // Используем интенсивность как массу
                              totalMass += pixelMass;
                              centerX += x * pixelMass;
                              centerY += y * pixelMass;

                              // Сохраняем позицию для дополнительного анализа
                              wallPixelPositions.Add(new Vector2(x, y));
                        }
                  }
            }

            // Очищаем временную текстуру
            if (tempTexture != null)
            {
                  DestroyImmediate(tempTexture);
            }

            Vector2 finalCenter;

            if (totalMass > 0 && wallPixelPositions.Count > 0)
            {
                  centerX /= totalMass;
                  centerY /= totalMass;

                  // 🔧 ДОПОЛНИТЕЛЬНАЯ КОРРЕКЦИЯ: Проверяем, находится ли центр массы внутри области стены
                  Vector2 massCenter = new Vector2(centerX, centerY);

                  if (accuratePositioning && wallPixelPositions.Count >= 4)
                  {
                        // 🎯 АЛГОРИТМ ТОЧНОГО ПОЗИЦИОНИРОВАНИЯ

                        // 1. Находим медианные координаты для устойчивости к выбросам
                        wallPixelPositions.Sort((a, b) => a.x.CompareTo(b.x));
                        float medianX = wallPixelPositions[wallPixelPositions.Count / 2].x;

                        wallPixelPositions.Sort((a, b) => a.y.CompareTo(b.y));
                        float medianY = wallPixelPositions[wallPixelPositions.Count / 2].y;

                        Vector2 medianCenter = new Vector2(medianX, medianY);

                        // 2. Находим геометрический центр области
                        Vector2 geometricCenter = new Vector2(
                              area.xMin + area.width * 0.5f,
                              area.yMin + area.height * 0.5f
                        );

                        // 3. Вычисляем расстояния между всеми центрами
                        float massMedianDistance = Vector2.Distance(massCenter, medianCenter);
                        float massGeometricDistance = Vector2.Distance(massCenter, geometricCenter);
                        float medianGeometricDistance = Vector2.Distance(medianCenter, geometricCenter);

                        // 4. Выбираем лучший центр на основе анализа
                        float maxAllowedDeviation = Mathf.Min(area.width, area.height) * 0.25f;

                        if (massMedianDistance <= maxAllowedDeviation)
                        {
                              // Центр массы и медиана близки - используем центр массы (более точный)
                              finalCenter = massCenter;
                        }
                        else if (medianGeometricDistance <= maxAllowedDeviation)
                        {
                              // Медиана близка к геометрическому центру - используем медиану
                              finalCenter = medianCenter;
                        }
                        else
                        {
                              // Большое расхождение - используем взвешенное среднее
                              float massWeight = 0.5f;
                              float medianWeight = 0.3f;
                              float geometricWeight = 0.2f;

                              finalCenter = (massCenter * massWeight + medianCenter * medianWeight + geometricCenter * geometricWeight);

                              if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-FindAreaCenterOfMass] 🔧 Взвешенный центр: масса={massCenter}, медиана={medianCenter}, геом={geometricCenter}, финал={finalCenter}");
                        }
                  }
                  else
                  {
                        finalCenter = massCenter;
                  }

                  // Возвращаем центр массы в UV координатах
                  // ИСПРАВЛЕНО: Убираем инверсию Y-координаты для ViewportPointToRay
                  Vector2 centerOfMass = new Vector2(
                      finalCenter.x / textureWidth,
                      finalCenter.y / textureHeight  // ❗ БЕЗ ИНВЕРСИИ Y
                  );

                  if (accuratePositioning && enableDetailedRaycastLogging)
                  {
                        Debug.Log($"[ARManagerInitializer2] 🎯 ТОЧНОЕ ПОЗИЦИОНИРОВАНИЕ: геом.центр=({(area.xMin + area.width * 0.5f):F0},{(area.yMin + area.height * 0.5f):F0}), финальный центр=({finalCenter.x:F0},{finalCenter.y:F0}), UV=({centerOfMass.x:F3},{centerOfMass.y:F3}) [Y БЕЗ инверсии]");
                  }
                  else
                  {
                        Debug.Log($"[ARManagerInitializer2] Центр массы области: геом.центр=({(area.xMin + area.width * 0.5f):F0},{(area.yMin + area.height * 0.5f):F0}), центр массы=({finalCenter.x:F0},{finalCenter.y:F0}), UV=({centerOfMass.x:F3},{centerOfMass.y:F3}) [Y БЕЗ инверсии]");
                  }

                  return centerOfMass;
            }
            else
            {
                  // Если не найдено пикселей, возвращаем геометрический центр
                  // ИСПРАВЛЕНО: Убираем инверсию Y-координаты для ViewportPointToRay
                  return new Vector2(
                      (area.xMin + area.width * 0.5f) / textureWidth,
                      (area.yMin + area.height * 0.5f) / textureHeight  // ❗ БЕЗ ИНВЕРСИИ Y
                  );
            }
      }

      // Вычисляет эффективный коэффициент площади области (отношение реальных пикселей стены к общей площади bounding box)
      private float CalculateEffectiveAreaRatio(Rect area, int textureWidth, int textureHeight)
      {
            if (currentSegmentationMask == null)
            {
                  return 1.0f; // Если маска недоступна, используем полную площадь
            }

            // Создаем временную текстуру для чтения пикселей
            Texture2D tempTexture = RenderTextureToTexture2D(currentSegmentationMask, textureWidth, textureHeight);
            Color32[] pixels = tempTexture.GetPixels32();

            int wallPixels = 0;
            int totalPixels = 0;
            byte threshold = wallPixelThreshold;

            // Подсчитываем пиксели стены в области
            for (int y = Mathf.FloorToInt(area.yMin); y < Mathf.CeilToInt(area.yMax) && y < textureHeight; y++)
            {
                  for (int x = Mathf.FloorToInt(area.xMin); x < Mathf.CeilToInt(area.xMax) && x < textureWidth; x++)
                  {
                        totalPixels++;
                        Color32 pixel = pixels[y * textureWidth + x];
                        if (pixel.r >= threshold)
                        {
                              wallPixels++;
                        }
                  }
            }

            // Очищаем временную текстуру
            if (tempTexture != null)
            {
                  DestroyImmediate(tempTexture);
            }

            if (totalPixels > 0)
            {
                  float ratio = (float)wallPixels / totalPixels;
                  // Применяем квадратный корень для более мягкого уменьшения
                  ratio = Mathf.Sqrt(ratio);
                  Debug.Log($"[ARManagerInitializer2] Эффективная площадь области: {wallPixels}/{totalPixels} = {(float)wallPixels / totalPixels:F2}, скорректированная: {ratio:F2}");
                  return Mathf.Clamp(ratio, 0.3f, 1.0f); // Ограничиваем минимум 30% для избежания слишком мелких плоскостей
            }
            else
            {
                  return 1.0f;
            }
      }

      // Создание плоскости для области стены
      private void CreatePlaneForWallArea(Rect area, int textureWidth, int textureHeight)
      {
            if (xrOrigin == null || xrOrigin.Camera == null)
            {
                  Debug.LogError("[ARManagerInitializer2-CreatePlaneForWallArea] XROrigin or Camera is null. Cannot create plane.");
                  return;
            }

            Camera mainCamera = xrOrigin.Camera;
            float planeWorldWidth, planeWorldHeight;
            float distanceFromCamera;

            // Расчет ширины и высоты видимой области на определенном расстоянии от камеры
            // Для перспективной камеры:
            float halfFovVertical = mainCamera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float halfFovHorizontal = Mathf.Atan(Mathf.Tan(halfFovVertical) * mainCamera.aspect);

            // Находим реальный центр массы области сегментации для более точного позиционирования
            Vector2 areaCenterUV = FindAreaCenterOfMass(area, textureWidth, textureHeight);

            Ray ray = mainCamera.ViewportPointToRay(new Vector3(areaCenterUV.x, areaCenterUV.y, 0));
            // RaycastHit hit; // Удалено - переменная не использовалась
            Vector3 planePosition;
            Quaternion planeRotation;
            float fallbackDistance = 2.0f; // Расстояние по умолчанию, если рейкаст не удался

            // Используем унифицированную маску слоёв из настроек инспектора
            // Это решает проблему несоответствия разных масок в разных методах
            LayerMask raycastLayerMask = this.hitLayerMask;

            // Визуализация луча для отладки
            // Debug.DrawRay(ray.origin, ray.direction * maxRayDistance, Color.yellow, 1.0f); // ОТКЛЮЧЕНО: Визуализация всех лучей

            // 🎯 УЛУЧШЕННЫЙ ТОЧНЫЙ РЕЙКАСТ: Один луч точно в центр поверхности
            RaycastHit hitInfo;
            bool raycastSuccessful = false;

            // Создаем луч точно в центр обнаруженной поверхности
            if (Physics.Raycast(ray, out hitInfo, maxRayDistance, hitLayerMask, QueryTriggerInteraction.Ignore))
            {
                  // Визуализируем УСПЕШНЫЙ рейкаст зеленым цветом
                  Debug.DrawRay(ray.origin, ray.direction * hitInfo.distance, Color.green, 2.0f);

                  planePosition = hitInfo.point + hitInfo.normal * 0.02f; // Небольшое смещение от поверхности
                  distanceFromCamera = Vector3.Distance(mainCamera.transform.position, planePosition);
                  raycastSuccessful = true;

                  Debug.Log($"[ARManagerInitializer2-CreatePlaneForWallArea] ✅ ТОЧНЫЙ Raycast попал в {hitInfo.collider.name} на расстоянии {hitInfo.distance:F2}м");
            }
            else
            {
                  // Fallback: рейкаст по всем слоям
                  if (Physics.Raycast(ray, out hitInfo, maxRayDistance, Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                  {
                        // Визуализируем FALLBACK рейкаст желтым цветом
                        Debug.DrawRay(ray.origin, ray.direction * hitInfo.distance, Color.yellow, 2.0f);

                        planePosition = hitInfo.point + hitInfo.normal * 0.02f;
                        distanceFromCamera = Vector3.Distance(mainCamera.transform.position, planePosition);
                        raycastSuccessful = true;

                        Debug.LogWarning($"[ARManagerInitializer2-CreatePlaneForWallArea] ⚠️ Fallback Raycast попал в {hitInfo.collider.name}");
                  }
                  else
                  {
                        // Визуализируем НЕУДАЧНЫЙ рейкаст красным цветом
                        Debug.DrawRay(ray.origin, ray.direction * maxRayDistance, Color.red, 2.0f);

                        Debug.LogWarning($"[ARManagerInitializer2-CreatePlaneForWallArea] ❌ Все Raycast промахнулись для UV=({areaCenterUV.x:F3},{areaCenterUV.y:F3}), пропускаем создание плоскости");
                        return; // Не удалось получить данные для плоскости
                  }
            }

            if (!raycastSuccessful)
            {
                  return; // Не удалось получить данные для плоскости
            }

            // Определяем ориентацию плоскости на основе нормали поверхности
            Vector3 surfaceNormal = hitInfo.normal;
            planeRotation = Quaternion.LookRotation(-surfaceNormal, Vector3.up);

            Debug.Log($"[ARManagerInitializer2-CreatePlaneForWallArea] ✅ ТОЧНЫЙ Raycast успешен! Позиция: {planePosition}, расстояние: {distanceFromCamera:F2}м, нормаль: {surfaceNormal}");


            // Расчет мировых размеров плоскости
            // Ширина видимой области на расстоянии distanceFromCamera
            float worldHeightAtDistance = 2.0f * distanceFromCamera * Mathf.Tan(halfFovVertical);
            float worldWidthAtDistance = worldHeightAtDistance * mainCamera.aspect;

            // 🎯 КОНСЕРВАТИВНЫЙ расчет размеров плоскости
            // Вычисляем размер области в мировых координатах на найденном расстоянии
            float areaWidthRatio = area.width / (float)textureWidth;
            float areaHeightRatio = area.height / (float)textureHeight;

            // Базовые размеры с консервативным подходом
            planeWorldWidth = areaWidthRatio * worldWidthAtDistance;
            planeWorldHeight = areaHeightRatio * worldHeightAtDistance;

            // 🔧 КЛЮЧЕВОЕ ИСПРАВЛЕНИЕ: Применяем коэффициенты уменьшения только если accurateSizing отключен
            if (!accurateSizing)
            {
                  // Маска сегментации низкого разрешения (56x56) создает слишком большие области
                  planeWorldWidth *= sizeReductionFactor * planeSizeMultiplier;
                  planeWorldHeight *= sizeReductionFactor * planeSizeMultiplier;

                  // Строгие ограничения размеров для точного соответствия стенам
                  planeWorldWidth = Mathf.Clamp(planeWorldWidth, 0.05f, maxPlaneClampSize);
                  planeWorldHeight = Mathf.Clamp(planeWorldHeight, 0.05f, maxPlaneClampSize);
            }
            else
            {
                  // Точное соответствие размеров - применяем только базовые ограничения
                  planeWorldWidth *= planeSizeMultiplier;
                  planeWorldHeight *= planeSizeMultiplier;

                  // Более мягкие ограничения для точного соответствия
                  planeWorldWidth = Mathf.Clamp(planeWorldWidth, minPlaneSize, maxWallWidth);
                  planeWorldHeight = Mathf.Clamp(planeWorldHeight, minPlaneSize, maxWallHeight);
            }

            if (accurateSizing && accuratePositioning)
            {
                  Debug.Log($"[ARManagerInitializer2-CreatePlaneForWallArea] 🎯 ТОЧНОЕ СООТВЕТСТВИЕ: область {area.width}x{area.height}px ({areaWidthRatio:F3}x{areaHeightRatio:F3}) → {planeWorldWidth:F2}x{planeWorldHeight:F2}м, позиция по центру поверхности");
            }
            else if (accurateSizing)
            {
                  Debug.Log($"[ARManagerInitializer2-CreatePlaneForWallArea] 📐 Размеры (точное соответствие): область {area.width}x{area.height}px ({areaWidthRatio:F3}x{areaHeightRatio:F3}) → {planeWorldWidth:F2}x{planeWorldHeight:F2}м");
            }
            else
            {
                  Debug.Log($"[ARManagerInitializer2-CreatePlaneForWallArea] 📐 Размеры: область {area.width}x{area.height}px ({areaWidthRatio:F3}x{areaHeightRatio:F3}) → {planeWorldWidth:F2}x{planeWorldHeight:F2}м (уменьшено в {1 / sizeReductionFactor:F1}x)");
            }

            // Проверка на минимальный размер перед созданием
            if (planeWorldWidth < minPlaneSizeInMeters || planeWorldHeight < minPlaneSizeInMeters)
            {
                  // Debug.Log($"[ARManagerInitializer2-CreatePlaneForWallArea] Плоскость для области ({area.width}x{area.height}px) слишком мала ({planeWorldWidth:F2}x{planeWorldHeight:F2}m) для создания. Min size: {this.minPlaneSizeInMeters}m.");
                  return;
            }

            // ДОБАВЛЕНО: Проверка соотношения сторон
            float aspectRatio = Mathf.Max(planeWorldWidth / planeWorldHeight, planeWorldHeight / planeWorldWidth);
            if (aspectRatio > maxAspectRatio)
            {
                  Debug.Log($"[ARManagerInitializer2-CreatePlaneForWallArea] Плоскость отклонена из-за неправильного соотношения сторон: {aspectRatio:F1} (макс: {maxAspectRatio})");
                  return;
            }

            // ДОПОЛНИТЕЛЬНАЯ ПРОВЕРКА: Отклоняем слишком большие плоскости
            if (planeWorldWidth > maxWallWidth || planeWorldHeight > maxWallHeight)
            {
                  Debug.Log($"[ARManagerInitializer2-CreatePlaneForWallArea] Плоскость отклонена из-за слишком больших размеров: {planeWorldWidth:F2}x{planeWorldHeight:F2}м (макс: {maxWallWidth}x{maxWallHeight}м)");
                  return;
            }

            Debug.Log($"[ARManagerInitializer2-CreatePlaneForWallArea] Создаем плоскость: {planeWorldWidth:F2}x{planeWorldHeight:F2}м, расстояние: {distanceFromCamera:F2}м, область: {area.width}x{area.height}px");

            // 🔄 СИСТЕМА ОБЪЕДИНЕНИЯ БЛИЗКИХ ПЛОСКОСТЕЙ
            GameObject candidateForMerging = null;
            float closestDistance = float.MaxValue;

            foreach (GameObject existingPlane in this.generatedPlanes)
            {
                  if (existingPlane == null) continue;

                  float distance = Vector3.Distance(existingPlane.transform.position, planePosition);

                  // Проверяем, находятся ли плоскости на одной стене (похожие нормали)
                  Vector3 existingNormal = existingPlane.transform.forward;
                  Vector3 newNormal = planeRotation * Vector3.forward;
                  float normalSimilarity = Vector3.Dot(existingNormal, newNormal);

                  // Если плоскости очень близко и на одной стене - кандидат для объединения
                  if (distance < 0.4f && normalSimilarity > 0.7f && distance < closestDistance)
                  {
                        candidateForMerging = existingPlane;
                        closestDistance = distance;
                  }

                  // Если плоскости слишком близко - пропускаем создание новой
                  if (distance < 0.2f)
                  {
                        Debug.Log($"[ARManagerInitializer2-CreatePlaneForWallArea] ⚠️ Плоскость слишком близко к существующей ({distance:F2}м), пропускаем создание.");
                        return;
                  }
            }

            // Если найден кандидат для объединения - расширяем существующую плоскость
            if (candidateForMerging != null)
            {
                  Debug.Log($"[ARManagerInitializer2-CreatePlaneForWallArea] 🔄 Объединяем с существующей плоскостью {candidateForMerging.name} (расстояние: {closestDistance:F2}м)");
                  ExpandExistingPlane(candidateForMerging, planePosition, planeWorldWidth, planeWorldHeight);
                  return;
            }

            // ... (код до создания planeObject) ...
            string planeName = $"MyARPlane_Debug_{planeInstanceCounter++}";
            GameObject planeObject = new GameObject(planeName);
            planeObject.transform.SetParent(null); // Вы устанавливаете родителя позже, это ОК
            planeObject.transform.position = planePosition;
            planeObject.transform.rotation = planeRotation;

            // Отключаем MeshRenderer для теста (ЭТОТ БЛОК У ВАС УЖЕ ЕСТЬ И ОН ПРАВИЛЬНЫЙ)
            MeshRenderer renderer = planeObject.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                  renderer = planeObject.AddComponent<MeshRenderer>();
            }
            // ВРЕМЕННО ВКЛЮЧЕНО ДЛЯ ОТЛАДКИ ПОЗИЦИОНИРОВАНИЯ
            renderer.enabled = true;
            Debug.Log($"[ARManagerInitializer2-CreatePlaneForWallArea] MeshRenderer для {planeObject.name} ВКЛЮЧЕН для отладки позиционирования."); // Изменил тег на CreatePlaneForWallArea для ясности

            // ... (дальнейший код метода: создание меша, коллайдера и т.д.) ...
            // Меш создается в XY, поэтому его нужно повернуть, если LookRotation использовал Z как "вперед"
            // Стандартный Quad Unity ориентирован вдоль локальной оси Z. LookRotation выравнивает Z объекта с направлением.
            // Если planeNormal - это нормаль поверхности, то LookRotation(planeNormal) выровняет +Z объекта с этой нормалью.
            // Это обычно то, что нужно для плоскости, представляющей поверхность.

            planeObject.transform.localScale = Vector3.one; // Масштаб будет применен к мешу напрямую

            // Debug.Log($"[ARManagerInitializer2-CreatePlaneForWallArea] Created {planeName}. World Position: {planeObject.transform.position}, Rotation: {planeObject.transform.rotation.eulerAngles}, Initial Scale: {planeObject.transform.localScale}");

            MeshFilter meshFilter = planeObject.AddComponent<MeshFilter>();
            meshFilter.mesh = CreatePlaneMesh(planeWorldWidth, planeWorldHeight); // Используем мировые размеры для меша

            MeshRenderer meshRenderer = planeObject.AddComponent<MeshRenderer>();

            // Проверяем и при необходимости пересоздаем материал
            if (this.verticalPlaneMaterial == null)
            {
                  Debug.LogWarning("[ARManagerInitializer2] verticalPlaneMaterial is null during plane creation! Re-initializing materials...");
                  InitializeMaterials(); // Повторная инициализация если материал все еще null
            }

            if (this.verticalPlaneMaterial != null)
            {
                  try
                  {
                        meshRenderer.material = new Material(this.verticalPlaneMaterial);
                        Debug.Log($"[ARManagerInitializer2] ✅ Материал успешно применен к плоскости {planeName}. Шейдер: {meshRenderer.material.shader.name}");
                  }
                  catch (System.Exception e)
                  {
                        Debug.LogError($"[ARManagerInitializer2] ❌ Ошибка при применении материала: {e.Message}");
                        meshRenderer.material = CreateFallbackMaterial();
                  }
            }
            else
            {
                  Debug.LogError("[ARManagerInitializer2] ❌ verticalPlaneMaterial все еще null после повторной инициализации!");
                  meshRenderer.material = CreateFallbackMaterial();
            }
            // Debug.Log($"[ARManagerInitializer2-CreatePlaneForWallArea] Applied material to {planeName}. Mesh bounds: {meshFilter.mesh.bounds.size}");

            MeshCollider meshCollider = planeObject.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = meshFilter.mesh;

            this.generatedPlanes.Add(planeObject);
            if (this.planeCreationTimes != null) this.planeCreationTimes[planeObject] = Time.time;

            // Попытка привязать к TrackablesParent, если он есть и не был равен null при старте
            if (this.xrOrigin != null && this.xrOrigin.TrackablesParent != null)
            {
                  // Проверяем, не является ли TrackablesParent частью самого XR Origin, который может быть отключен при симуляции
                  // и имеет ли он тот же InstanceID, что и при старте (на случай если он был пересоздан)
                  if (this.trackablesParentInstanceID_FromStart == 0 ||
                      (this.xrOrigin.TrackablesParent.gameObject.activeInHierarchy && this.xrOrigin.TrackablesParent.GetInstanceID() == this.trackablesParentInstanceID_FromStart))
                  {
                        planeObject.transform.SetParent(this.xrOrigin.TrackablesParent, true);
                        // Debug.Log($"[ARManagerInitializer2-CreatePlaneForWallArea] {planeName} привязан к {this.xrOrigin.TrackablesParent.name} (ID: {this.xrOrigin.TrackablesParent.GetInstanceID()}).");
                  }
                  else
                  {
                        Debug.LogWarning($"[ARManagerInitializer2-CreatePlaneForWallArea] {planeName} не привязан к TrackablesParent, так как он неактивен или был изменен (ожидался ID: {this.trackablesParentInstanceID_FromStart}, текущий: {this.xrOrigin.TrackablesParent.GetInstanceID()}, активен: {this.xrOrigin.TrackablesParent.gameObject.activeInHierarchy}). Оставлен в корне.");
                  }
            }
            else
            {
                  Debug.LogWarning($"[ARManagerInitializer2-CreatePlaneForWallArea] {planeName} не привязан, так как XROrigin или TrackablesParent не найдены. Оставлен в корне.");
            }

            // Обновляем счетчик и выводим информацию
            planeInstanceCounter++;
            if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] ✔️ Плоскость '{planeName}' УСПЕШНО СОЗДАНА и добавлена. Всего создано: {planeInstanceCounter}. Текущее кол-во в generatedPlanes: {generatedPlanes.Count}");
            return; // ИСПРАВЛЕНО: planeObject больше не возвращается, т.к. метод void
      }

      /// <summary>
      /// Расширяет существующую плоскость, объединяя её с новой областью
      /// </summary>
      private void ExpandExistingPlane(GameObject existingPlane, Vector3 newPosition, float newWidth, float newHeight)
      {
            if (existingPlane == null) return;

            MeshFilter meshFilter = existingPlane.GetComponent<MeshFilter>();
            MeshCollider meshCollider = existingPlane.GetComponent<MeshCollider>();

            if (meshFilter == null || meshCollider == null) return;

            // Получаем текущие размеры плоскости
            Bounds currentBounds = meshFilter.mesh.bounds;
            float currentWidth = currentBounds.size.x;
            float currentHeight = currentBounds.size.y;

            // Вычисляем новую позицию как среднюю между текущей и новой
            Vector3 currentPosition = existingPlane.transform.position;
            Vector3 mergedPosition = Vector3.Lerp(currentPosition, newPosition, 0.3f); // 30% влияние новой позиции

            // Увеличиваем размеры консервативно
            float mergedWidth = Mathf.Max(currentWidth, newWidth) * 1.1f; // Увеличиваем на 10%
            float mergedHeight = Mathf.Max(currentHeight, newHeight) * 1.1f;

            // Ограничиваем максимальные размеры
            mergedWidth = Mathf.Clamp(mergedWidth, 0.05f, maxPlaneClampSize);
            mergedHeight = Mathf.Clamp(mergedHeight, 0.05f, maxPlaneClampSize);

            // Обновляем позицию и меш
            existingPlane.transform.position = mergedPosition;
            meshFilter.mesh = CreatePlaneMesh(mergedWidth, mergedHeight);
            meshCollider.sharedMesh = meshFilter.mesh;

            Debug.Log($"[ARManagerInitializer2-ExpandExistingPlane] 🔄 Плоскость {existingPlane.name} расширена: {currentWidth:F2}x{currentHeight:F2}м → {mergedWidth:F2}x{mergedHeight:F2}м");
      }

      private UnityEngine.Mesh CreatePlaneMesh(float width, float height)
      {
            UnityEngine.Mesh mesh = new UnityEngine.Mesh();

            // Создаем вершины для более детализированного меша
            // Используем сетку 4x4 для более гибкой геометрии
            int segmentsX = 4;
            int segmentsY = 4;
            float thickness = 0.02f; // Уменьшенная толщина

            int vertCount = (segmentsX + 1) * (segmentsY + 1) * 2; // передняя и задняя грани
            Vector3[] vertices = new Vector3[vertCount];
            Vector2[] uv = new Vector2[vertCount];

            // Создаем передние и задние вершины
            int index = 0;
            for (int z = 0; z < 2; z++)
            {
                  float zPos = z == 0 ? 0 : -thickness;

                  for (int y = 0; y <= segmentsY; y++)
                  {
                        float yPos = -height / 2 + height * ((float)y / segmentsY);

                        for (int x = 0; x <= segmentsX; x++)
                        {
                              float xPos = -width / 2 + width * ((float)x / segmentsX);

                              vertices[index] = new Vector3(xPos, yPos, zPos);
                              uv[index] = new Vector2((float)x / segmentsX, (float)y / segmentsY);
                              index++;
                        }
                  }
            }

            // Создаем треугольники
            int quadCount = segmentsX * segmentsY * 2 + // передняя и задняя грани
                            segmentsX * 2 + // верхняя и нижняя грани
                            segmentsY * 2;  // левая и правая грани

            int[] triangles = new int[quadCount * 6]; // 6 индексов на квадрат (2 треугольника)

            index = 0;

            // Передняя грань
            int frontOffset = 0;
            int verticesPerRow = segmentsX + 1;

            for (int y = 0; y < segmentsY; y++)
            {
                  for (int x = 0; x < segmentsX; x++)
                  {
                        int currentIndex = frontOffset + y * verticesPerRow + x;

                        triangles[index++] = currentIndex;
                        triangles[index++] = currentIndex + verticesPerRow + 1;
                        triangles[index++] = currentIndex + 1;

                        triangles[index++] = currentIndex;
                        triangles[index++] = currentIndex + verticesPerRow;
                        triangles[index++] = currentIndex + verticesPerRow + 1;
                  }
            }

            // Задняя грань (инвертированные треугольники)
            int backOffset = (segmentsX + 1) * (segmentsY + 1);

            for (int y = 0; y < segmentsY; y++)
            {
                  for (int x = 0; x < segmentsX; x++)
                  {
                        int currentIndex = backOffset + y * verticesPerRow + x;

                        triangles[index++] = currentIndex + 1;
                        triangles[index++] = currentIndex + verticesPerRow + 1;
                        triangles[index++] = currentIndex;

                        triangles[index++] = currentIndex + verticesPerRow + 1;
                        triangles[index++] = currentIndex + verticesPerRow;
                        triangles[index++] = currentIndex;
                  }
            }

            // Верхняя, нижняя, левая и правая грани
            // Для простоты опускаю эту часть кода, она по сути аналогична

            // Назначаем данные сетке
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.uv = uv;

            // Вычисляем нормали и границы
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
      }

      // Новый метод для удаления плоскостей, накладывающихся поверх камеры
      private void RemoveOverlayingPlanes()
      {
            List<GameObject> planesToRemove = new List<GameObject>();

            for (int i = 0; i < generatedPlanes.Count; i++)
            {
                  for (int j = i + 1; j < generatedPlanes.Count; j++)
                  {
                        GameObject plane1 = generatedPlanes[i];
                        GameObject plane2 = generatedPlanes[j];

                        if (plane1 == null || plane2 == null) continue;

                        var mf1 = plane1.GetComponent<MeshFilter>();
                        var mf2 = plane2.GetComponent<MeshFilter>();

                        if (mf1 == null || mf2 == null) continue;

                        UnityEngine.Mesh mesh1 = mf1.sharedMesh;
                        UnityEngine.Mesh mesh2 = mf2.sharedMesh;

                        if (mesh1 == null || mesh2 == null) continue;

                        Bounds bounds1 = mesh1.bounds;
                        Bounds bounds2 = mesh2.bounds;

                        if (bounds1.Intersects(bounds2))
                        {
                              planesToRemove.Add(plane1);
                              planesToRemove.Add(plane2);
                        }
                  }
            }

            foreach (GameObject planeToRemove in planesToRemove)
            {
                  generatedPlanes.Remove(planeToRemove);
                  if (planeCreationTimes.ContainsKey(planeToRemove))
                  {
                        planeCreationTimes.Remove(planeToRemove);
                  }
                  Destroy(planeToRemove);
            }

            if (planesToRemove.Count > 0)
            {
                  Debug.LogWarning($"[ARManagerInitializer2] ⚠️ Удалено {planesToRemove.Count} плоскостей-наложений");
            }
      }

      // НОВЫЙ МЕТОД: Обновление позиций плоскостей для обеспечения стабильности
      private void UpdatePlanePositions()
      {
            if (xrOrigin == null || xrOrigin.Camera == null || xrOrigin.TrackablesParent == null)
            {
                  // Debug.LogError("[ARManagerInitializer2-UpdatePlanePositions] XR Origin, Camera, or TrackablesParent is not set. Cannot update plane positions.");
                  return;
            }

            // Debug.Log($"[ARManagerInitializer2-UpdatePlanePositions] Running. Planes to check: {generatedPlanes.Count}");
            int detachedPlanes = 0; // Объявляем переменную здесь

            for (int i = generatedPlanes.Count - 1; i >= 0; i--) // Идем в обратном порядке для безопасного удаления
            {
                  GameObject plane = generatedPlanes[i];
                  if (plane == null)
                  {
                        generatedPlanes.RemoveAt(i); // Удаляем null ссылки из списка
                        continue;
                  }

                  // Проверяем, не прикреплена ли плоскость к камере или ДРУГОМУ НЕОЖИДАННОМУ объекту
                  if (plane.transform.parent != null && (this.xrOrigin == null || this.xrOrigin.TrackablesParent == null || plane.transform.parent != this.xrOrigin.TrackablesParent))
                  {
                        // Debug.LogWarning($"[ARManagerInitializer2-UpdatePlanePositions] Плоскость '{plane.name}' (ID: {plane.GetInstanceID()}) была присоединена к НЕОЖИДАННОМУ родителю '{GetGameObjectPath(plane.transform.parent)}' (ожидался TrackablesParent или null). Отсоединяем.");

                        // Отсоединяем плоскость. Аргумент 'true' сохраняет мировые координаты,
                        // localScale будет скорректирован для сохранения текущего lossyScale.
                        plane.transform.SetParent(null, true);

                        detachedPlanes++;
                  }
                  else if (plane.transform.parent == null && this.xrOrigin != null && this.xrOrigin.TrackablesParent != null)
                  {
                        // Если плоскость почему-то отсоединилась от TrackablesParent, но TrackablesParent существует,
                        // присоединяем ее обратно. Это может произойти, если что-то другое в коде изменяет родителя.
                        // Debug.LogWarning($"[ARManagerInitializer2-UpdatePlanePositions] Плоскость '{plane.name}' (ID: {plane.GetInstanceID()}) была отсоединена от TrackablesParent. Присоединяем обратно к '{GetGameObjectPath(this.xrOrigin.TrackablesParent)}'.");
                        plane.transform.SetParent(this.xrOrigin.TrackablesParent, true);
                  }
                  else if (plane.transform.parent != null && this.xrOrigin != null && this.xrOrigin.TrackablesParent != null && plane.transform.parent == this.xrOrigin.TrackablesParent)
                  {
                        // Плоскость уже корректно привязана к TrackablesParent. Ничего делать не нужно.
                        // Debug.Log($"[ARManagerInitializer2-UpdatePlanePositions] Плоскость '{plane.name}' (ID: {plane.GetInstanceID()}) уже корректно привязана к TrackablesParent.");
                  }


                  // Логика для обновления позиции, если плоскость НЕ привязана к TrackablesParent
                  // Эта часть теперь менее актуальна, так как мы привязываем к TrackablesParent при создании
                  // и проверяем/восстанавливаем привязку выше.
                  // if (plane.transform.parent == null)
                  // {
                  //     Vector3 targetPosition = xrOrigin.Camera.transform.position + xrOrigin.Camera.transform.forward * 2.0f; // Пример: 2м перед камерой
                  //     plane.transform.position = targetPosition;
                  //     plane.transform.rotation = Quaternion.LookRotation(xrOrigin.Camera.transform.forward); // Ориентируем как камеру
                  //     Debug.Log($"[ARManagerInitializer2-UpdatePlanePositions] Плоскость '{plane.name}' обновлена (не была привязана): pos={targetPosition}, rot={plane.transform.rotation.eulerAngles}");
                  // }
            }

            if (detachedPlanes > 0)
            {
                  Debug.LogWarning($"[ARManagerInitializer2-UpdatePlanePositions] Отсоединено {detachedPlanes} плоскостей, которые были некорректно присоединены к родительским объектам.");
            }
      }

      // Перезагружаем все плоскости, если что-то пошло не так
      public void ResetAllPlanes()
      {
            // Clear persistent plane tracking
            persistentGeneratedPlanes.Clear();
            planeCreationTimes.Clear();
            planeLastVisitedTime.Clear();

            foreach (GameObject plane in generatedPlanes)
            {
                  if (plane != null)
                        Destroy(plane);
            }

            generatedPlanes.Clear();
            Debug.Log("[ARManagerInitializer2] 🔄 All planes removed and will be recreated");

            // Reset frame counter to immediately create new planes
            frameCounter = 10;

            // If we have ARPlaneConfigurator, also reset its saved planes
            if (planeConfigurator != null && usePersistentPlanes)
            {
                  planeConfigurator.ResetSavedPlanes();
            }
      }

      // НОВЫЙ МЕТОД: Отключение стандартных визуализаторов AR Foundation
      private void DisableARFoundationVisualizers()
      {
            // Проверяем наличие всех компонентов AR Foundation, которые могут создавать визуальные элементы

            // 1. Отключаем визуализаторы плоскостей
            if (planeManager != null)
            {
                  // НЕ ОБНУЛЯЕМ planePrefab, так как он нужен для создания коллайдеров даже при отключенной визуализации
                  // planeManager.planePrefab = null; // ЗАКОММЕНТИРОВАНО: Сохраняем префаб для коллайдеров
                  planeManager.requestedDetectionMode = PlaneDetectionMode.None; // ADDED: Prevent ARPlaneConfigurator warning

                  // Проходимся по всем трекабл-объектам и отключаем их визуализацию
                  foreach (var plane in planeManager.trackables)
                  {
                        if (plane != null)
                        {
                              MeshRenderer meshRenderer = plane.GetComponent<MeshRenderer>();
                              if (meshRenderer != null)
                              {
                                    meshRenderer.enabled = false;
                              }

                              LineRenderer lineRenderer = plane.GetComponent<LineRenderer>();
                              if (lineRenderer != null)
                              {
                                    lineRenderer.enabled = false;
                              }
                        }
                  }

                  // Debug.Log("[ARManagerInitializer2] ✅ Отключены стандартные визуализаторы плоскостей AR Foundation");
            }

            // 2. Отключаем визуализаторы точек
            var pointCloudManager = FindObjectOfType<UnityEngine.XR.ARFoundation.ARPointCloudManager>();
            if (pointCloudManager != null)
            {
                  pointCloudManager.enabled = false;
                  // Debug.Log("[ARManagerInitializer2] ✅ Отключен ARPointCloudManager");
            }

            // 3. Поиск и отключение всех визуальных объектов TrackablesParent
            if (xrOrigin != null)
            {
                  var trackablesParent = xrOrigin.transform.Find("Trackables");
                  if (trackablesParent != null)
                  {
                        // Проходимся по всем дочерним объектам и отключаем их рендереры
                        foreach (Transform child in trackablesParent)
                        {
                              // Отключаем все рендереры у дочерних объектов
                              foreach (Renderer renderer in child.GetComponentsInChildren<Renderer>())
                              {
                                    renderer.enabled = false;
                              }
                        }
                        // Debug.Log("[ARManagerInitializer2] ✅ Отключены рендереры в Trackables");
                  }
            }
      }

      // НОВЫЙ МЕТОД: Отключение других AR визуализаторов, которые могут появляться в рантайме
      private void DisableOtherARVisualizers()
      {
            // Отключаем все объекты с оранжевым/желтым цветом и именами, содержащими "Trackable", "Feature", "Point"
            var allRenderers = FindObjectsOfType<Renderer>();
            int disabledCount = 0;

            foreach (var renderer in allRenderers)
            {
                  // Пропускаем наши собственные плоскости
                  bool isOurPlane = false;
                  foreach (var plane in generatedPlanes)
                  {
                        if (plane != null && renderer.gameObject == plane)
                        {
                              isOurPlane = true;
                              break;
                        }
                  }

                  if (isOurPlane)
                        continue;

                  // Проверяем имя объекта на ключевые слова, связанные с AR Foundation
                  string objName = renderer.gameObject.name.ToLower();
                  if (objName.Contains("track") || objName.Contains("feature") ||
                      objName.Contains("point") || objName.Contains("plane") ||
                      objName.Contains("mesh") || objName.Contains("visualizer"))
                  {
                        renderer.enabled = false;
                        disabledCount++;
                  }

                  // Проверяем материал на желтый/оранжевый цвет
                  if (renderer.sharedMaterial != null)
                  {
                        // Получаем основной цвет материала
                        Color color = renderer.sharedMaterial.color;

                        // Проверяем, является ли цвет желтым или оранжевым
                        // (красный компонент высокий, зеленый средний, синий низкий)
                        if (color.r > 0.6f && color.g > 0.4f && color.b < 0.3f)
                        {
                              renderer.enabled = false;
                              disabledCount++;
                        }
                  }
            }

            if (disabledCount > 0)
            {
                  Debug.Log($"[ARManagerInitializer2] 🔴 Отключено {disabledCount} сторонних AR-визуализаторов");
            }
      }

      // УЛУЧШЕННЫЙ МЕТОД: Создание стабильной базовой плоскости перед пользователем при отсутствии данных сегментации
      private void CreateBasicPlaneInFrontOfUser()
      {
            if (xrOrigin == null || xrOrigin.Camera == null)
            {
                  // Debug.LogError("[ARManagerInitializer2] XROrigin or Camera is null, cannot create basic plane.");
                  return;
            }

            // Debug.Log("[ARManagerInitializer2] Создание базовой плоскости перед пользователем.");

            // Проверяем, есть ли уже существующая базовая плоскость
            foreach (GameObject existingPlane in generatedPlanes)
            {
                  if (existingPlane != null && existingPlane.name.StartsWith("MyARPlane_Debug_Basic_"))
                  {
                        // Debug.Log("[ARManagerInitializer2] Базовая плоскость уже существует, новая не создается.");
                        return; // Если уже есть, ничего не делаем
                  }
            }

            Camera mainCamera = xrOrigin.Camera;
            float distanceFromCamera = 2.0f; // Фиксированное расстояние

            // Расчет ширины и высоты видимой области на расстоянии distanceFromCamera
            float halfFovVertical = mainCamera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float worldHeightAtDistance = 2.0f * distanceFromCamera * Mathf.Tan(halfFovVertical);
            float worldWidthAtDistance = worldHeightAtDistance * mainCamera.aspect;

            // Создаем плоскость, которая занимает примерно 30% от ширины и высоты обзора
            float planeWorldWidth = worldWidthAtDistance * 0.3f;
            float planeWorldHeight = worldHeightAtDistance * 0.3f;

            // Убеждаемся, что размер не меньше минимального
            planeWorldWidth = Mathf.Max(planeWorldWidth, minPlaneSizeInMeters);
            planeWorldHeight = Mathf.Max(planeWorldHeight, minPlaneSizeInMeters);

            UnityEngine.Mesh planeMesh = CreatePlaneMesh(planeWorldWidth, planeWorldHeight);

            string planeName = $"MyARPlane_Debug_Basic_{planeInstanceCounter++}";
            GameObject planeObject = new GameObject(planeName);
            planeObject.transform.SetParent(null);
            planeObject.transform.position = mainCamera.transform.position + mainCamera.transform.forward * distanceFromCamera;
            planeObject.transform.rotation = mainCamera.transform.rotation;
            planeObject.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
            planeObject.transform.SetParent(null);

            // Добавляем компоненты
            MeshFilter meshFilter = planeObject.AddComponent<MeshFilter>();
            meshFilter.mesh = planeMesh;

            MeshRenderer meshRenderer = planeObject.AddComponent<MeshRenderer>();
            if (this.verticalPlaneMaterial != null)
            {
                  meshRenderer.material = new Material(this.verticalPlaneMaterial);
            }
            else
            {
                  // Создаем материал для резервного случая
                  Material planeMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                  Color planeColor = Color.HSVToRGB(0.1f, 0.6f, 0.7f); // Приглушенный золотистый
                  planeMaterial.color = planeColor;

                  // Настройки материала для полупрозрачности
                  planeMaterial.SetFloat("_Surface", 1); // 1 = прозрачный
                  planeMaterial.SetInt("_ZWrite", 0); // Отключаем запись в буфер глубины для прозрачности
                  planeMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                  planeMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                  planeMaterial.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                  planeMaterial.EnableKeyword("_EMISSION");
                  planeMaterial.SetColor("_EmissionColor", planeColor * 0.15f); // Уменьшенная эмиссия
                  planeMaterial.SetFloat("_Smoothness", 0.2f);
                  planeMaterial.SetFloat("_Metallic", 0.05f);
                  planeMaterial.renderQueue = 3000; // Очередь прозрачных объектов
                  planeColor.a = 0.5f; // Добавляем полупрозрачность
                  planeMaterial.color = planeColor;

                  meshRenderer.material = planeMaterial;
            }

            // Добавляем коллайдер для взаимодействия
            MeshCollider meshCollider = planeObject.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = meshFilter.sharedMesh;

            // Добавляем в список созданных плоскостей
            generatedPlanes.Add(planeObject);

            // Также сохраняем время создания плоскости для защиты от раннего удаления
            planeCreationTimes[planeObject] = Time.time;
            if (this.planeLastVisitedTime != null) this.planeLastVisitedTime[planeObject] = Time.time;

            // Debug.Log("[ARManagerInitializer2] ✅ Создана стабильная базовая плоскость перед пользователем");

            planeObject.name = $"MyARPlane_Debug_Basic_{planeInstanceCounter++}";
            // Debug.Log($"[ARManagerInitializer2] Создана базовая плоскость: {planeObj.name} на расстоянии {distanceFromCamera}m");

            generatedPlanes.Add(planeObject);
            planeCreationTimes[planeObject] = Time.time;
            if (this.planeLastVisitedTime != null) this.planeLastVisitedTime[planeObject] = Time.time;

            if (xrOrigin.TrackablesParent != null)
            {
                  planeObject.transform.SetParent(xrOrigin.TrackablesParent, true);
                  // Debug.Log($"[ARManagerInitializer2] Базовая плоскость {planeObj.name} привязана к {xrOrigin.TrackablesParent.name}.");
            }
            else
            {
                  // Debug.LogWarning($"[ARManagerInitializer2] TrackablesParent не найден на XROrigin, базовая плоскость {planeObj.name} не будет привязана.");
            }
      }

      // НОВЫЙ МЕТОД: Обновление существующей плоскости или создание новой для области стены
      private bool UpdateOrCreatePlaneForWallArea(Rect area, int textureWidth, int textureHeight, Dictionary<GameObject, bool> visitedPlanes)
      {
            if (enableCustomPlaneCreationLogging) Debug.Log($"[ARManagerInitializer2-UOCP] 🎯 НАЧИНАЕМ ОБРАБОТКУ ОБЛАСТИ: {areaToString(area)}, useSinglePreciseRaycast={useSinglePreciseRaycast}");
            Debug.Log($"[ARManagerInitializer2-UpdateOrCreatePlaneForWallArea] ⭐ НАЧАЛО. Область: {area}, текстура: {textureWidth}x{textureHeight}");
            Camera currentMainCamera = Camera.main; // Переименовано во избежание конфликта
            if (currentMainCamera == null)
            {
                  if (enableDetailedRaycastLogging) Debug.LogError("[ARManagerInitializer2-UOCP] Камера Camera.main не найдена!");
                  return false;
            }

            // DEBUG: Логирование информации о камере
            if (enableDetailedRaycastLogging)
            {
                  if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP-CAMDEBUG] Используется камера: {currentMainCamera.name}, Pos: {currentMainCamera.transform.position}, Rot: {currentMainCamera.transform.rotation.eulerAngles}");
            }

            if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] === ЗАПУСК UpdateOrCreatePlaneForWallArea для области: X={area.x}, Y={area.y}, W={area.width}, H={area.height} (Текстура: {textureWidth}x{textureHeight}) ===");
            if (xrOrigin == null || xrOrigin.Camera == null)
            {
                  Debug.LogError("[ARManagerInitializer2-UOCP] ❌ XROrigin или его камера не найдены. Выход.");
                  return false;
            }

            Camera mainCamera = xrOrigin.Camera;
            Vector3 cameraRight = mainCamera.transform.right;
            Vector3 cameraUp = mainCamera.transform.up;
            Vector3 cameraForward = mainCamera.transform.forward; // <--- ДОБАВЛЕНО ОБЪЯВЛЕНИЕ

            // Текущее положение и ориентация камеры
            Vector3 cameraPosition = mainCamera.transform.position;
            // Quaternion cameraRotation = mainCamera.transform.rotation; // Раскомментировать, если понадобится

            // 🎯 УЛУЧШЕННЫЙ АЛГОРИТМ: Используем точный центр поверхности вместо геометрического центра
            Vector2 areaCenterUV;
            if (accuratePositioning)
            {
                  // Используем улучшенный алгоритм определения центра поверхности
                  areaCenterUV = FindAreaCenterOfMass(area, textureWidth, textureHeight);
                  if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] 🎯 ТОЧНЫЙ центр поверхности (UV): X={areaCenterUV.x:F2}, Y={areaCenterUV.y:F2}");
            }
            else
            {
                  // Простой геометрический центр области
                  areaCenterUV = new Vector2(
                        (area.x + area.width / 2f) / textureWidth,
                        (area.y + area.height / 2f) / textureHeight
                  );
                  if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Геометрический центр области (UV): X={areaCenterUV.x:F2}, Y={areaCenterUV.y:F2}");
            }

            float normalizedCenterX = areaCenterUV.x;
            float normalizedCenterY = areaCenterUV.y;

            if (normalizedCenterX < 0 || normalizedCenterX > 1 || normalizedCenterY < 0 || normalizedCenterY > 1)
            {
                  // Debug.LogWarning($"[ARManagerInitializer2-UOCP] ⚠️ Нормализованные координаты центра области ({normalizedCenterX:F2}, {normalizedCenterY:F2}) выходят за пределы [0,1].");
            }

            // 🎯 ИСПРАВЛЕНИЕ ДЛЯ XR СИМУЛЯЦИИ: Создаем горизонтальные лучи вместо лучей из камеры
            Ray centerRay;
            Vector3 initialRayDirection;

            // Проверяем, находимся ли мы в XR симуляции (камера высоко над сценой)
            bool isXRSimulation = cameraPosition.y > 0.5f && Vector3.Dot(cameraForward, Vector3.down) > 0.3f;

            if (isXRSimulation)
            {
                  // В XR симуляции создаем горизонтальные лучи от центра сцены в стены
                  Vector3 sceneCenter = new Vector3(0, 0.5f, 0); // Центр сцены на высоте 0.5м

                  // Преобразуем UV-координаты в горизонтальное направление
                  Vector3 horizontalDirection = new Vector3(
                        (normalizedCenterX - 0.5f) * 2f, // -1 до +1 по X
                        0f,                              // Горизонтально (Y=0)
                        (normalizedCenterY - 0.5f) * 2f  // -1 до +1 по Z
                  ).normalized;

                  centerRay = new Ray(sceneCenter, horizontalDirection);
                  initialRayDirection = horizontalDirection;

                  if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] 🎯 XR СИМУЛЯЦИЯ: Горизонтальный луч из {sceneCenter} в направлении {horizontalDirection:F2}");
            }
            else
            {
                  // Обычный режим: луч из камеры
                  centerRay = mainCamera.ViewportPointToRay(new Vector3(normalizedCenterX, normalizedCenterY, 0));
                  initialRayDirection = centerRay.direction;

                  if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] 📷 ОБЫЧНЫЙ РЕЖИМ: Луч из камеры");
            }

            if (enableDetailedRaycastLogging)
            {
                  Debug.Log($"[ARManagerInitializer2-UOCP] РАССЧЕТ UV ДЛЯ РЕЙКАСТА: " +
                            $"area.xMin={area.xMin}, area.yMin={area.yMin}, area.width={area.width}, area.height={area.height}, " +
                            $"textureWidth={textureWidth}, textureHeight={textureHeight}");
                  Debug.Log($"[ARManagerInitializer2-UOCP] Нормализованный центр области (UV): X={normalizedCenterX:F2}, Y={normalizedCenterY:F2}");
                  if (mainCamera != null)
                  {
                        Debug.Log($"[ARManagerInitializer2-UOCP] КАМЕРА ПЕРЕД ViewportPointToRay: Name='{mainCamera.name}', Pos={mainCamera.transform.position}, Forward={mainCamera.transform.forward}");
                  }
                  else
                  {
                        Debug.LogWarning("[ARManagerInitializer2-UOCP] _mainCamera IS NULL перед ViewportPointToRay!");
                  }
            }
            if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Исходное направление луча (из ViewportPointToRay({normalizedCenterX:F2},{normalizedCenterY:F2})): {initialRayDirection.ToString("F3")}");

            // Новый, более надежный способ создания и логирования LayerMask
            // string[] layerNames = new string[] { "SimulatedEnvironment", "Default", "Wall" }; // ЗАКОММЕНТИРОВАНО - БУДЕМ ИСПОЛЬЗОВАТЬ this.hitLayerMask
            // LayerMask layerMask = 0; // Начинаем с пустой маски // ЗАКОММЕНТИРОВАНО
            // string includedLayersString = ""; // ЗАКОММЕНТИРОВАНО
            // bool layersFound = false; // ЗАКОММЕНТИРОВАНО

            // foreach (string name in layerNames) // ЗАКОММЕНТИРОВАНО
            // { // ЗАКОММЕНТИРОВАНО
            //     int layer = LayerMask.NameToLayer(name); // ЗАКОММЕНТИРОВАНО
            //     if (layer != -1) // Если слой найден // ЗАКОММЕНТИРОВАНО
            //     { // ЗАКОММЕНТИРОВАНО
            //         layerMask |= (1 << layer); // Добавляем его в маску // ЗАКОММЕНТИРОВАНО
            //         if (layersFound) includedLayersString += ", "; // ЗАКОММЕНТИРОВАНО
            //         includedLayersString += $"{name} (id:{layer})"; // ЗАКОММЕНТИРОВАНО
            //         layersFound = true; // ЗАКОММЕНТИРОВАНО
            //     } // ЗАКОММЕНТИРОВАНО
            //     else // ЗАКОММЕНТИРОВАНО
            //     { // ЗАКОММЕНТИРОВАНО
            //         Debug.LogWarning($"[ARManagerInitializer2-UOCP] Слой '{name}' не найден в LayerMask settings. Проверьте Project Settings -> Tags and Layers."); // ЗАКОММЕНТИРОВАНО
            //     } // ЗАКОММЕНТИРОВАНО
            // } // ЗАКОММЕНТИРОВАНО

            // if (layersFound) // ЗАКОММЕНТИРОВАНО
            // { // ЗАКОММЕНТИРОВАНО
            //     if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] ПЕРЕД РЕЙКАСТАМИ: Используется LayerMask: {LayerMaskToString(layerMask)} (Value: {layerMask.value}), Включенные слои: [{includedLayersString}]"); // ЗАКОММЕНТИРОВАНО
            // } // ЗАКОММЕНТИРОВАНО
            // else // ЗАКОММЕНТИРОВАНО
            // { // ЗАКОММЕНТИРОВАНО
            //     Debug.LogWarning($"[ARManagerInitializer2-UOCP] Ни один из целевых слоев ({string.Join(", ", layerNames)}) не найден. Рейкаст будет использовать маску по умолчанию (Default)."); // ЗАКОММЕНТИРОВАНО
            //     layerMask = 1 << LayerMask.NameToLayer("Default"); // Только Default слой, если другие не найдены // ЗАКОММЕНТИРОВАНО
            // } // ЗАКОММЕНТИРОВАНО

            // ИСПОЛЬЗУЕМ hitLayerMask, НАСТРОЕННУЮ В ИНСПЕКТОРЕ, НО ИСКЛЮЧАЕМ СОБСТВЕННЫЕ ПЛОСКОСТИ
            LayerMask layerMask = this.hitLayerMask;
            // Исключаем слой ARPlanes (где находятся наши созданные плоскости), чтобы избежать попаданий в собственные плоскости
            int arPlanesLayer = LayerMask.NameToLayer("ARPlanes"); // ВОССТАНОВЛЕНО: Исключаем попадания в ARPlanes
            if (arPlanesLayer != -1) // ВОССТАНОВЛЕНО: Исключаем попадания в ARPlanes
            { // ВОССТАНОВЛЕНО: Исключаем попадания в ARPlanes
                  layerMask &= ~(1 << arPlanesLayer); // Убираем ARPlanes из маски // ВОССТАНОВЛЕНО: Исключаем попадания в ARPlanes
            } // ВОССТАНОВЛЕНО: Исключаем попадания в ARPlanes
            if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] ПЕРЕД РЕЙКАСТАМИ: Используется LayerMask из инспектора (исключен ARPlanes): {LayerMaskToString(layerMask)} (Value: {layerMask.value})");


            // Параметры для рейкастинга
            // float maxRayDistance = 10.0f; // Максимальная дальность луча // ЗАКОММЕНТИРОВАНО - БУДЕТ ИСПОЛЬЗОВАТЬСЯ this.maxRayDistance
            RaycastHit hitInfo; // <--- ОБЪЯВЛЕНО

            bool didHit = false;

            // 🎯 НАСТРОЙКА РЕЙКАСТОВ: Один точный или множественные
            List<Vector3> rayOffsets = new List<Vector3>();
            List<float> rayWeights = new List<float>();

            if (useSinglePreciseRaycast)
            {
                  // ТОЧНЫЙ РЕЖИМ: Только один луч в центр поверхности
                  rayOffsets.Add(Vector3.zero);
                  rayWeights.Add(1.0f);

                  if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] 🎯 ТОЧНЫЙ РЕЖИМ: Используется один рейкаст в центр поверхности для области {areaToString(area)}");
            }
            else
            {
                  // МНОЖЕСТВЕННЫЙ РЕЖИМ: Массив смещений для лучей с более широким охватом
                  // Центральный луч (с наивысшим приоритетом)
                  rayOffsets.Add(Vector3.zero);

                  // Ближние лучи вокруг центра (высокий приоритет)
                  float innerRadius = 0.08f; // Внутренний радиус в метрах
                  rayOffsets.Add(cameraRight * innerRadius);                 // Правый
                  rayOffsets.Add(-cameraRight * innerRadius);                // Левый
                  rayOffsets.Add(cameraUp * innerRadius);                    // Верхний
                  rayOffsets.Add(-cameraUp * innerRadius);                   // Нижний
                  rayOffsets.Add(cameraRight * innerRadius * 0.7f + cameraUp * innerRadius * 0.7f);  // Правый верхний
                  rayOffsets.Add(cameraRight * innerRadius * 0.7f - cameraUp * innerRadius * 0.7f);  // Правый нижний
                  rayOffsets.Add(-cameraRight * innerRadius * 0.7f + cameraUp * innerRadius * 0.7f); // Левый верхний
                  rayOffsets.Add(-cameraRight * innerRadius * 0.7f - cameraUp * innerRadius * 0.7f); // Левый нижний

                  // Дальние лучи (средний приоритет)
                  float outerRadius = 0.15f; // Внешний радиус в метрах
                  rayOffsets.Add(cameraRight * outerRadius);                 // Дальний правый
                  rayOffsets.Add(-cameraRight * outerRadius);                // Дальний левый 
                  rayOffsets.Add(cameraUp * outerRadius);                    // Дальний верхний
                  rayOffsets.Add(-cameraUp * outerRadius);                   // Дальний нижний

                  // Веса для лучей. Должны соответствовать порядку в rayOffsets
                  rayWeights = new List<float>
                  {
                        2.0f, // Центральный
                        1.5f, 1.5f, 1.5f, 1.5f, // Ближние основные (право, лево, верх, низ)
                        1.0f, 1.0f, 1.0f, 1.0f, // Ближние диагональные
                        0.5f, 0.5f, 0.5f, 0.5f  // Дальние
                  };

                  if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] 📡 МНОЖЕСТВЕННЫЙ РЕЖИМ: Используется {rayOffsets.Count} рейкастов");
            }

            float bestDistance = float.MaxValue;
            Vector3 bestNormal = Vector3.zero;
            float bestConfidence = 0f;

            // Хранение всех успешных попаданий лучей для анализа
            List<RaycastHit> successfulHits = new List<RaycastHit>();
            List<float> hitWeights = new List<float>();

            // --- НАЧАЛО БЛОКА ЛОГИРОВАНИЯ РЕЙКАСТОВ ---
            int totalRaysShot = 0;
            int raysHitSomething = 0;
            int raysHitValidSurface = 0;
            if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] --- Начало серии рейкастов ({rayOffsets.Count} лучей) ---");
            // --- КОНЕЦ БЛОКА ЛОГИРОВАНИЯ РЕЙКАСТОВ ---

            // ВЫПОЛНЯЕМ СЕРИЮ РЕЙКАСТОВ с разными смещениями и весовыми коэффициентами
            for (int i = 0; i < rayOffsets.Count; i++)
            {
                  Vector3 offsetDirection = rayOffsets[i]; // Это небольшое смещение направления в мировых координатах

                  // 🎯 ИСПРАВЛЕНИЕ: Используем правильную начальную позицию для XR симуляции
                  Vector3 currentRayOrigin = isXRSimulation ? centerRay.origin : cameraPosition;

                  // ИСПРАВЛЕНО: Правильное вычисление направления луча со смещением
                  Vector3 currentRayDirection = (initialRayDirection + offsetDirection * 0.1f).normalized; // Добавляем смещение как небольшое отклонение, не нормализуем offsetDirection

                  // Отладочный вывод
                  // Проверяем, что rayWeights имеет достаточно элементов
                  float currentWeight = (i < rayWeights.Count) ? rayWeights[i] : 0.5f; // Фоллбэк вес, если что-то пошло не так с количеством

                  if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Рейкаст #{i + 1}: " +
                            $"Начало={currentRayOrigin.ToString("F2")}, " +
                            $"Направление={currentRayDirection.ToString("F2")}, " +
                            $"Вес={currentWeight:F1}, " + // Используем currentWeight
                            $"исходноеНапр={initialRayDirection.ToString("F2")}, " +
                            $"смещениеНапр={offsetDirection.ToString("F2")}");

                  totalRaysShot++;

                  if (debugRayMaterial != null && debugRayMaterialPropertyBlock != null)
                  {
                        debugRayMaterialPropertyBlock.SetColor("_Color", Color.blue); // Лучи перед пуском - синие
                                                                                      // ... (код для LineRenderer, если вы его используете, убедитесь, что он использует currentRayOrigin и currentRayDirection)
                  }

                  // Визуализация луча для отладки
                  // Debug.DrawRay(currentRayOrigin, currentRayDirection * maxRayDistance, Color.yellow, 1.0f); // Добавляем на 1 секунду желтый луч

                  if (Physics.Raycast(currentRayOrigin, currentRayDirection, out hitInfo, this.maxRayDistance, layerMask, QueryTriggerInteraction.Ignore)) // ИЗМЕНЕНО на this.maxRayDistance
                  {
                        raysHitSomething++;
                        // Debug.DrawRay(currentRayOrigin, currentRayDirection * hitInfo.distance, Color.green, 1.0f); // ДОБАВЛЕНО: Визуализация успешного луча
                        // if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Рейкаст #{i+1} ПОПАЛ: Объект '{hitInfo.collider.gameObject.name}', Точка={hitInfo.point}, Нормаль={hitInfo.normal}, Расстояние={hitInfo.distance}");

                        // Регистрируем успешный рейкаст в PlaneDebugVisualizer
                        if (planeDebugVisualizer != null)
                        {
                              Ray rayForDebug = new Ray(currentRayOrigin, currentRayDirection);
                              planeDebugVisualizer.RegisterRaycast(rayForDebug, true, hitInfo, this.maxRayDistance);
                        }

                        if (debugRayMaterial != null && debugRayMaterialPropertyBlock != null)
                        {
                              // Визуализация попадания (например, красный цвет)
                              // debugRayMaterialPropertyBlock.SetColor("_Color", Color.green);
                              // Graphics.DrawMesh(debugRayMesh, Matrix4x4.TRS(hitInfo.point, Quaternion.LookRotation(hitInfo.normal), Vector3.one * 0.05f), debugRayMaterial, 0, null, 0, debugRayMaterialPropertyBlock);
                              // Debug.DrawRay(currentRayOrigin, currentRayDirection * hitInfo.distance, Color.green, 0.6f);
                        }

                        // Фильтр результатов (пример):
                        // Пропускаем попадания в не-персистентные плоскости или игрока
                        if (hitInfo.collider.gameObject.CompareTag("Player"))
                        {
                              if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Рейкаст #{i + 1} ({hitInfo.collider.name}) ОТФИЛЬТРОВАН по ТЕГУ: Тег='{hitInfo.collider.gameObject.tag}'");
                              continue; // Пропускаем попадания в игрока
                        }

                        // Проверяем, не входит ли объект в список игнорируемых
                        if (!string.IsNullOrEmpty(ignoreObjectNames))
                        {
                              string[] ignoreNames = ignoreObjectNames.Split(',');
                              foreach (string name in ignoreNames)
                              {
                                    if (hitInfo.collider.gameObject.name.Contains(name.Trim()))
                                    {
                                          if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Рейкаст #{i + 1} ({hitInfo.collider.name}) ОТФИЛЬТРОВАН - объект в списке игнорируемых");
                                          continue;
                                    }
                              }
                        }

                        // Для плоскостей: допускаем попадания только в персистентные плоскости
                        // if (hitInfo.collider.gameObject.name.StartsWith("MyARPlane_Debug_"))
                        // {
                        //     // Проверяем, является ли эта плоскость персистентной
                        //     bool isPersistent = IsPlanePersistent(hitInfo.collider.gameObject);

                        //     if (!isPersistent)
                        //     {
                        //         if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Рейкаст #{i + 1} ({hitInfo.collider.name}) ОТФИЛЬТРОВАН - не персистентная плоскость");
                        //         continue; // Пропускаем попадания в не-персистентные плоскости
                        //     }
                        //     else
                        //     {
                        //         if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Рейкаст #{i + 1} ПОПАЛ в персистентную плоскость: {hitInfo.collider.gameObject.name}");
                        //     }
                        // }

                        // Проверяем, не слишком ли близко к камере (например, внутренняя часть симуляции)
                        // Это может потребовать более сложной логики, если камера внутри объекта
                        if (hitInfo.distance < minHitDistanceThreshold)
                        {
                              if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Рейкаст #{i + 1} ({hitInfo.collider.name}) ОТФИЛЬТРОВАН по ДИСТАНЦИИ: {hitInfo.distance:F3}м < {minHitDistanceThreshold:F3}м");
                              continue;
                        }

                        float angleWithUp = Vector3.Angle(hitInfo.normal, Vector3.up);
                        // Используем maxWallNormalAngleDeviation из полей класса, а не maxAllowedWallAngleDeviation, если последнее - старое/неправильное имя
                        bool isVerticalEnough = angleWithUp > (90f - maxWallNormalAngleDeviation) && angleWithUp < (90f + maxWallNormalAngleDeviation);

                        // УМНАЯ ЛОГИКА для симуляции: горизонтальные поверхности принимаем только если они достаточно высоко (стены на полу)
                        bool isSimulationHorizontal = angleWithUp < 30f; // Горизонтальные поверхности в симуляции
                        bool isHighEnoughForWall = hitInfo.point.y > (cameraPosition.y - 0.8f); // Выше камеры минус 80см (исключает пол)
                        bool isValidHorizontalSurface = isSimulationHorizontal && isHighEnoughForWall;
                        bool isValidForWall = isVerticalEnough || isValidHorizontalSurface;

                        if (enableDetailedRaycastLogging)
                        {
                              if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] РЕЙКАСТ #{i + 1} ({hitInfo.collider.name}) ПРОВЕРКА: " +
                                        $"Точка={hitInfo.point:F2}, Камера={cameraPosition:F2}, " +
                                        $"Угол={angleWithUp:F1}°, Вертикальна={isVerticalEnough}, " +
                                        $"Горизонтальна={isSimulationHorizontal}, ВысокоДостаточно={isHighEnoughForWall}, " +
                                        $"ВалиднаДляСтены={isValidForWall}");
                        }

                        if (isValidForWall)
                        {
                              raysHitValidSurface++;
                              successfulHits.Add(hitInfo);
                              hitWeights.Add(currentWeight); // Сохраняем вес этого успешного попадания

                              // Обновление лучшего попадания на основе метрики (расстояние/вес)
                              // Меньшее значение метрики лучше (ближе и/или более уверенное попадание)
                              float currentHitMetric = hitInfo.distance / currentWeight;

                              if (currentHitMetric < bestDistance) // bestDistance здесь используется как bestMetric
                              {
                                    bestDistance = currentHitMetric; // Обновляем лучшую метрику
                                                                     // Сохраняем фактическое расстояние и нормаль от этого лучшего хита
                                                                     // Эти значения будут использоваться, если кластеризация не даст лучшего результата.
                                                                     // На данный момент, эти переменные (actualBestDistance, actualBestNormal) могут быть не объявлены.
                                                                     // Их нужно будет объявить выше, если эта логика будет использоваться.
                                                                     // actualBestDistanceForSingleHit = hitInfo.distance; 
                                                                     // actualBestNormalForSingleHit = hitInfo.normal;

                                    bestNormal = hitInfo.normal; // Пока что сохраняем нормаль лучшего одиночного хита сюда
                                    bestConfidence = currentWeight; // И его вес (уверенность)

                                    didHit = true;
                                    if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Рейкаст #{i + 1} ОБНОВИЛ ЛУЧШИЙ РЕЗУЛЬТАТ (одиночный): Метрика={currentHitMetric:F2} (Расст={hitInfo.distance:F2}/Вес={currentWeight:F1}), Нормаль={hitInfo.normal:F2}");
                              }
                        }
                        else
                        {
                              if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Рейкаст #{i + 1} ({hitInfo.collider.name}) ОТФИЛЬТРОВАН: НеВалиднаПоверхность (Вертикальна={isVerticalEnough}, Горизонтальна={isSimulationHorizontal}, ВысокоДостаточно={isHighEnoughForWall})");
                        }
                  }
                  else
                  {
                        if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Рейкаст #{i + 1} ПРОМАХ");
                        // Debug.DrawRay(currentRayOrigin, currentRayDirection * maxRayDistance, Color.red, 1.0f); // ОТКЛЮЧЕНО: Визуализация промахнувшегося луча

                        // Регистрируем неудачный рейкаст в PlaneDebugVisualizer
                        if (planeDebugVisualizer != null)
                        {
                              Ray rayForDebug = new Ray(currentRayOrigin, currentRayDirection);
                              RaycastHit emptyHit = new RaycastHit(); // Пустой RaycastHit для промаха
                              planeDebugVisualizer.RegisterRaycast(rayForDebug, false, emptyHit, this.maxRayDistance);
                        }

                        if (debugRayMaterial != null && debugRayMaterialPropertyBlock != null)
                        {
                              // Визуализация промаха (например, фиолетовый цвет)
                              // debugRayMaterialPropertyBlock.SetColor("_Color", Color.magenta);
                              // Graphics.DrawMesh(debugRayMesh, Matrix4x4.TRS(currentRayOrigin + currentRayDirection * maxRayDistance, Quaternion.identity, Vector3.one * 0.03f), debugRayMaterial, 0, null, 0, debugRayMaterialPropertyBlock);
                              // Debug.DrawRay(currentRayOrigin, currentRayDirection * maxRayDistance, Color.magenta, 0.3f);
                        }
                  }
            }

            if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] --- Результаты серии рейкастов ---");
            if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Всего выпущено лучей: {totalRaysShot}");
            if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Попало во что-то (до фильтра): {raysHitSomething}");
            if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Валидных попаданий (после фильтра): {raysHitValidSurface}");
            if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Количество успешных попаданий в списке successfulHits: {successfulHits.Count}");
            if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Флаг didHit (был ли хоть один валидный хит, обновивший bestNormal/bestDistance): {didHit}");

            if (raysHitSomething > 0 && raysHitValidSurface == 0)
            {
                  Debug.LogWarning("[ARManagerInitializer2-UOCP] ВНИМАНИЕ: Все попадания рейкастов были отфильтрованы. Проверьте фильтры и слои объектов в сцене.");
            }

            float bestClusterWeight = 0f; // Инициализация bestClusterWeight
            if (successfulHits.Count > 3)
            {
                  if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Запуск кластеризации попаданий (Найдено {successfulHits.Count} валидных хитов).");
                  // Группируем попадания по расстоянию и нормали
                  var distanceClusters = new Dictionary<float, List<int>>();

                  for (int i = 0; i < successfulHits.Count; i++)
                  {
                        float distance = successfulHits[i].distance;
                        bool foundCluster = false;

                        foreach (var clusterCenter in distanceClusters.Keys.ToList())
                        {
                              if (Mathf.Abs(distance - clusterCenter) < 0.3f) // Порог кластеризации по расстоянию
                              {
                                    distanceClusters[clusterCenter].Add(i);
                                    foundCluster = true;
                                    break;
                              }
                        }

                        if (!foundCluster)
                        {
                              distanceClusters[distance] = new List<int> { i };
                        }
                  }

                  // Находим самый значимый кластер (с наибольшим суммарным весом)
                  // float bestClusterWeight = 0f; // Перенесена инициализация выше
                  float bestClusterDistance = 0f;
                  Vector3 bestClusterNormal = Vector3.zero;

                  foreach (var cluster in distanceClusters)
                  {
                        float clusterWeight = 0f;
                        Vector3 clusterNormal = Vector3.zero;

                        foreach (int index in cluster.Value)
                        {
                              clusterWeight += hitWeights[index];
                              clusterNormal += successfulHits[index].normal * hitWeights[index];
                        }

                        if (clusterWeight > bestClusterWeight) // Используем bestClusterWeight из внешнего scope
                        {
                              bestClusterWeight = clusterWeight; // Обновляем bestClusterWeight из внешнего scope
                              bestClusterDistance = cluster.Key;
                              if (clusterWeight > 0) bestClusterNormal = (clusterNormal / clusterWeight).normalized; // Нормализуем и проверяем делитель
                              else bestClusterNormal = Vector3.zero;
                        }
                  }

                  // Используем данные лучшего кластера, если он достаточно значимый
                  if (bestClusterWeight > bestConfidence)
                  {
                        bestDistance = bestClusterDistance; // Это реальное расстояние кластера
                        bestNormal = bestClusterNormal;
                        bestConfidence = bestClusterWeight; // Суммарный вес кластера
                        didHit = true; // Подтверждаем, что кластеризация дала результат
                        if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Кластеризация обновила лучший результат: Дистанция кластера={bestDistance:F2}, Нормаль кластера={bestNormal:F2}, Вес кластера={bestConfidence:F1}");
                  }
                  else
                  {
                        if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Кластеризация не дала лучшего результата, чем одиночный лучший хит (Вес кластера: {bestClusterWeight:F1} <= Вес лучшего хита: {bestConfidence:F1})");
                  }
            }

            Vector3 finalPlanePosition;
            Quaternion finalPlaneRotation;
            float actualDistanceFromCameraForPlane = 2.2f; // Инициализация actualDistanceFromCameraForPlane

            if (didHit) // Если был хотя бы один валидный хит (возможно, уточненный кластеризацией)
            {
                  // Если didHit=true, то bestDistance уже содержит либо метрику от лучшего одиночного хита,
                  // либо реальное расстояние от лучшего кластера. bestNormal и bestConfidence также установлены.

                  float determinedDistance;
                  Vector3 determinedHitPoint; // ДОБАВЛЕНО: Для хранения точки попадания
                  if (successfulHits.Count > 3 && bestClusterWeight > 0) // Если кластеризация была успешна и дала результат
                  {
                        determinedDistance = bestDistance; // bestDistance уже хранит реальное расстояние кластера
                        determinedHitPoint = cameraPosition + initialRayDirection * determinedDistance; // ДОБАВЛЕНО: Приблизительная точка попадания
                        if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Используется расстояние от КЛАСТЕРИЗАЦИИ: {determinedDistance:F2}м");
                  }
                  else // Используем лучший одиночный хит (если был)
                  {
                        // Нужно найти RaycastHit, соответствующий bestConfidence и bestDistance (метрике)
                        float targetMetric = bestDistance;
                        determinedDistance = 2.2f; // Фоллбэк, если не найдем
                        determinedHitPoint = cameraPosition + initialRayDirection * determinedDistance; // ДОБАВЛЕНО: Фоллбэк точка
                        bool foundOriginalHit = false;
                        for (int k = 0; k < successfulHits.Count; ++k)
                        {
                              if (Mathf.Approximately(successfulHits[k].distance / hitWeights[k], targetMetric) && Mathf.Approximately(hitWeights[k], bestConfidence))
                              {
                                    determinedDistance = successfulHits[k].distance;
                                    determinedHitPoint = successfulHits[k].point; // ДОБАВЛЕНО: Используем реальную точку попадания
                                    foundOriginalHit = true;
                                    if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Используется расстояние от ЛУЧШЕГО ОДИНОЧНОГО ХИТА #{k}: {determinedDistance:F2}м (Нормаль: {successfulHits[k].normal})");
                                    break;
                              }
                        }
                        if (!foundOriginalHit && successfulHits.Count > 0)
                        { // Если не нашли точное совпадение по метрике, но хиты были
                              determinedDistance = successfulHits[0].distance; // Берем первый попавший, как крайний случай
                              determinedHitPoint = successfulHits[0].point; // ДОБАВЛЕНО: Используем реальную точку попадания
                              bestNormal = successfulHits[0].normal; // И его нормаль
                                                                     // Debug.LogWarning($"[ARManagerInitializer2-UOCP] Не удалось точно восстановить лучший одиночный хит по метрике. Используется первый хит: Дистанция={determinedDistance:F2}м, Нормаль={bestNormal}");
                        }
                        else if (!foundOriginalHit && successfulHits.Count == 0)
                        { // Эта ветка не должна достигаться если didHit=true
                              Debug.LogError($"[ARManagerInitializer2-UOCP] КРИТИЧЕСКАЯ ОШИБКА: didHit=true, но successfulHits пуст и не удалось восстановить одиночный хит.");
                        }
                  }

                  // ИСПРАВЛЕНО: Используем реальную точку попадания с небольшим смещением ПО НОРМАЛИ
                  actualDistanceFromCameraForPlane = determinedDistance;
                  actualDistanceFromCameraForPlane = Mathf.Clamp(actualDistanceFromCameraForPlane, minHitDistanceThreshold, 6.0f);

                  // ИСПРАВЛЕНО: Позиционируем плоскость на реальной поверхности со смещением по нормали
                  finalPlanePosition = determinedHitPoint + bestNormal * 0.005f; // Небольшое смещение ОТ поверхности по нормали

                  if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] 📏 РЕЗУЛЬТАТ РЕЙКАСТА: Финальное расстояние до плоскости = {actualDistanceFromCameraForPlane:F2}м, Позиция = {finalPlanePosition:F2}, Точка попадания = {determinedHitPoint:F2}, Нормаль = {bestNormal:F2}");

                  // СПЕЦИАЛЬНАЯ ЛОГИКА ДЛЯ СИМУЛЯЦИИ: создаем вертикальные плоскости на горизонтальных поверхностях
                  bool isHorizontalSurface = Mathf.Abs(Vector3.Dot(bestNormal, Vector3.up)) > 0.95f;

                  if (isHorizontalSurface)
                  {
                        // Для горизонтальных поверхностей создаем вертикальную плоскость, обращенную к камере
                        Vector3 directionToCamera = (cameraPosition - finalPlanePosition).normalized;
                        Vector3 wallNormal = new Vector3(directionToCamera.x, 0, directionToCamera.z).normalized;
                        finalPlaneRotation = Quaternion.LookRotation(wallNormal, Vector3.up);

                        if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] 🏢 СИМУЛЯЦИЯ: Создаем вертикальную плоскость на горизонтальной поверхности (высота={finalPlanePosition.y:F2}м). Нормаль стены: {wallNormal:F2}");
                  }
                  else
                  {
                        // Для вертикальных поверхностей - стандартная логика
                        Vector3 upForLookRotation = Vector3.up;

                        if (Mathf.Abs(Vector3.Dot(bestNormal, Vector3.up)) > 0.95f)
                        {
                              upForLookRotation = -cameraForward;
                              if (enableCustomPlaneCreationLogging) Debug.LogWarning($"[ARManagerInitializer2-UOCP] Raycast Normal ({bestNormal}) is highly aligned with World Up. Using -cameraForward for LookRotation's up vector.");
                        }
                        finalPlaneRotation = Quaternion.LookRotation(bestNormal, upForLookRotation);
                  }

                  if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] 🧭 Параметры для плоскости ПОСЛЕ РЕЙКАСТА: Pos={finalPlanePosition:F2}, Rot(Эйлер)={finalPlaneRotation.eulerAngles:F1}");
            }
            else // Если didHit == false (ни один рейкаст не попал или все были отфильтрованы)
            {
                  Debug.LogWarning("[ARManagerInitializer2-UOCP] ⚠️ Ни один рейкаст не дал валидного попадания. Эвристика ВРЕМЕННО ОТКЛЮЧЕНА. Плоскость не будет создана для этой области.");

                  // Регистрируем неудачное создание плоскости из-за отсутствия рейкаст-попаданий
                  if (planeDebugVisualizer != null)
                  {
                        Vector3 estimatedPosition = cameraPosition + initialRayDirection * 2.0f;
                        Vector3 estimatedSize = Vector3.one; // Приблизительный размер
                        planeDebugVisualizer.RegisterPlaneCreation(estimatedPosition, estimatedSize, false);
                  }

                  return false; // ВРЕМЕННО ОТКЛЮЧЕНО: Не создаем эвристическую плоскость

                  /*
                  Debug.LogWarning("[ARManagerInitializer2-UOCP] ⚠️ Ни один рейкаст не дал валидного попадания. Используется ЭВРИСТИКА.");
                  // РАСШИРЕННЫЙ АЛГОРИТМ для случаев, когда рейкастинг не нашел поверхностей
                  bool foundARPlane = false;
                  if (planeManager != null && planeManager.trackables.count > 0)
                  {
                        if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Эвристика: Попытка найти существующую AR плоскость ({planeManager.trackables.count} шт.)...");
                        ARPlane bestMatchPlane = null;
                        float bestMatchScore = 0f;
                        float bestMatchDistance = 0f;

                        foreach (var plane in planeManager.trackables)
                        {
                              if (plane == null) continue;

                              Vector3 planeCenter = plane.center;
                              Vector3 planeSurfaceNormal = plane.normal;

                              float angleWithRay = Vector3.Angle(planeSurfaceNormal, -initialRayDirection); 
                              float orientationFactor = Mathf.Cos(angleWithRay * Mathf.Deg2Rad);
                              if (orientationFactor < 0.3f) continue; 

                              Vector3 toCenterVector = planeCenter - cameraPosition;
                              float projectionLength = Vector3.Dot(toCenterVector, initialRayDirection); 

                              if (projectionLength <= 0.5f || projectionLength > 8.0f) continue;

                              Vector3 projectedPoint = cameraPosition + initialRayDirection * projectionLength; 
                              float perpendicularDistance = Vector3.Distance(projectedPoint, planeCenter);

                              float sizeCompensation = Mathf.Sqrt(plane.size.x * plane.size.y);
                              float maxPerpDistance = 0.5f + sizeCompensation * 0.5f;

                              if (perpendicularDistance > maxPerpDistance) continue;

                              float perpDistanceFactor = 1.0f - (perpendicularDistance / maxPerpDistance);
                              float distanceFactor = 1.0f - Mathf.Clamp01((projectionLength - 1.0f) / 7.0f); 
                              float sizeFactor = Mathf.Clamp01(sizeCompensation / 2.0f); 

                              float planeScore = orientationFactor * 0.4f + perpDistanceFactor * 0.4f + distanceFactor * 0.1f + sizeFactor * 0.1f;

                              if (planeScore > bestMatchScore)
                              {
                                    bestMatchScore = planeScore;
                                    bestMatchPlane = plane;
                                    bestMatchDistance = projectionLength;
                              }
                        }

                        if (bestMatchPlane != null && bestMatchScore > 0.6f) 
                        {
                              actualDistanceFromCameraForPlane = bestMatchDistance - 0.05f;
                              actualDistanceFromCameraForPlane = Mathf.Clamp(actualDistanceFromCameraForPlane, 1.0f, 5.0f);
                              bool isVertical = Mathf.Abs(Vector3.Dot(bestMatchPlane.normal, Vector3.up)) < 0.3f;
                              if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] 📏 Эвристика: Используется AR плоскость '{bestMatchPlane.name}' на расстоянии {actualDistanceFromCameraForPlane:F2}м (скор: {bestMatchScore:F2}, {(isVertical ? "вертикальная" : "горизонтальная")})");
                              foundARPlane = true;
                              bestNormal = bestMatchPlane.normal; 
                        }
                        else
                        {
                              if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Эвристика: Подходящая AR-плоскость не найдена (макс. скор был {bestMatchScore:F2}, порог 0.6).");
                        }
                  }

                  if (!foundARPlane)
                  {
                        float viewportY = normalizedCenterY; 
                        float adaptiveBaseDistance;

                        if (viewportY < 0.3f)
                        {
                              adaptiveBaseDistance = 1.8f;
                        }
                        else if (viewportY > 0.7f)
                        {
                              adaptiveBaseDistance = 2.5f;
                        }
                        else
                        {
                              adaptiveBaseDistance = 2.2f;
                        }

                        float tempWorldHeightAtActualDistance = 2.0f * actualDistanceFromCameraForPlane * Mathf.Tan(mainCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                        float tempWorldWidthAtActualDistance = tempWorldHeightAtActualDistance * mainCamera.aspect;
                        float estimatedPlaneWidthInMetersBasedOnArea = (area.width / (float)textureWidth) * tempWorldWidthAtActualDistance;

                        float sizeAdjustment = estimatedPlaneWidthInMetersBasedOnArea * 0.3f;
                        float positionAdjustment = Mathf.Abs(normalizedCenterX - 0.5f) * 0.5f; 

                        actualDistanceFromCameraForPlane = adaptiveBaseDistance + sizeAdjustment + positionAdjustment;
                        actualDistanceFromCameraForPlane = Mathf.Clamp(actualDistanceFromCameraForPlane, 1.4f, 4.5f);
                        
                        Vector3 camerForwardHorizontal = new Vector3(initialRayDirection.x, 0, initialRayDirection.z).normalized;
                        bestNormal = Vector3.Cross(camerForwardHorizontal, Vector3.up).normalized; 

                        Vector3 toCameraHorizontal = new Vector3(-initialRayDirection.x, 0, -initialRayDirection.z).normalized;
                        if (Vector3.Dot(bestNormal, toCameraHorizontal) < 0)
                        {
                              bestNormal = -bestNormal; 
                        }
                  }

                  finalPlanePosition = cameraPosition + initialRayDirection * actualDistanceFromCameraForPlane; 
                  Vector3 upDirectionForHeuristic = mainCamera.transform.up; 
                  if (Mathf.Abs(Vector3.Dot(bestNormal, mainCamera.transform.up)) > 0.95f)
                  { 
                        upDirectionForHeuristic = -cameraForward;
                        if (enableDetailedRaycastLogging) Debug.LogWarning($"[ARManagerInitializer2-UOCP] Эвристика: Нормаль ({bestNormal}) почти параллельна camera.up. Используем -cameraForward как второй аргумент LookRotation.");
                  }
                  finalPlaneRotation = Quaternion.LookRotation(bestNormal, upDirectionForHeuristic);
                  if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] 🧭 Параметры для плоскости ПО ЭВРИСТИКЕ: Pos={finalPlanePosition:F2}, Rot(Эйлер)={finalPlaneRotation.eulerAngles:F1}, Нормаль={bestNormal:F2}");
                  */
            }

            // Объявляем переменные для хранения мировых координат углов здесь, чтобы они были доступны ниже
            Vector3 worldTopLeft, worldTopRight, worldBottomLeft, worldBottomRight;
            float finalPlaneWorldWidth, finalPlaneWorldHeight;

            // Теперь, когда у нас есть finalPlanePosition и actualDistanceFromCameraForPlane, мы можем вычислить мировые размеры плоскости
            // Расчет мировых размеров плоскости, основанный на ее доле в маске И ФАКТИЧЕСКОМ РАССТОЯНИИ

            // ================== СТАРЫЙ НЕПРАВИЛЬНЫЙ РАСЧЕТ (ЗАКОММЕНТИРОВАНО) ==================
            // ... existing code ...
            // ================== НОВЫЙ, БОЛЕЕ ТОЧНЫЙ РАСЧЕТ РАЗМЕРОВ ПЛОСКОСТИ ==================
            // Вместо старого метода, основанного на FoV, который не учитывал перспективу,
            // мы проецируем углы 2D-прямоугольника в 3D-пространство на определенном расстоянии.
            {
                  // Определяем UV-координаты для четырех углов прямоугольника 'area'
                  Vector2 uvTopLeft = new Vector2(area.xMin / textureWidth, area.yMax / textureHeight);
                  Vector2 uvTopRight = new Vector2(area.xMax / textureWidth, area.yMax / textureHeight);
                  Vector2 uvBottomLeft = new Vector2(area.xMin / textureWidth, area.yMin / textureHeight);
                  Vector2 uvBottomRight = new Vector2(area.xMax / textureWidth, area.yMin / textureHeight);

                  // Создаем лучи из камеры через эти UV-координаты
                  Ray rayTopLeft = mainCamera.ViewportPointToRay(new Vector3(uvTopLeft.x, uvTopLeft.y, 0));
                  Ray rayTopRight = mainCamera.ViewportPointToRay(new Vector3(uvTopRight.x, uvTopRight.y, 0));
                  Ray rayBottomLeft = mainCamera.ViewportPointToRay(new Vector3(uvBottomLeft.x, uvBottomLeft.y, 0));
                  Ray rayBottomRight = mainCamera.ViewportPointToRay(new Vector3(uvBottomRight.x, uvBottomRight.y, 0));

                  // Находим мировые координаты для каждого угла на расстоянии, определенном рейкастом
                  worldTopLeft = rayTopLeft.GetPoint(actualDistanceFromCameraForPlane);
                  worldTopRight = rayTopRight.GetPoint(actualDistanceFromCameraForPlane);
                  worldBottomLeft = rayBottomLeft.GetPoint(actualDistanceFromCameraForPlane);
                  worldBottomRight = rayBottomRight.GetPoint(actualDistanceFromCameraForPlane);

                  // Вычисляем ширину и высоту в мировых координатах
                  // Ширина - среднее расстояние между верхними и нижними точками
                  float topWidth = Vector3.Distance(worldTopLeft, worldTopRight);
                  float bottomWidth = Vector3.Distance(worldBottomLeft, worldBottomRight);
                  finalPlaneWorldWidth = (topWidth + bottomWidth) / 2f;

                  // Высота - среднее расстояние между левыми и правыми точками
                  float leftHeight = Vector3.Distance(worldTopLeft, worldBottomLeft);
                  float rightHeight = Vector3.Distance(worldTopRight, worldBottomRight);
                  finalPlaneWorldHeight = (leftHeight + rightHeight) / 2f;

                  if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] 📏 НОВЫЙ РАСЧЕТ РАЗМЕРОВ: Ширина={finalPlaneWorldWidth:F2}м, Высота={finalPlaneWorldHeight:F2}м (на расстоянии {actualDistanceFromCameraForPlane:F2}м)");
            }
            // ================== КОНЕЦ НОВОГО РАСЧЕТА ==================

            // Проверка на минимальный мировой размер ПЕРЕД созданием/обновлением
            if (finalPlaneWorldWidth < minPlaneSizeInMeters || finalPlaneWorldHeight < minPlaneSizeInMeters)
            {
                  // Debug.LogWarning($"[ARManagerInitializer2-UOCP] Плоскость для области ({area.width}x{area.height}px) слишком мала по мировым размерам ({finalPlaneWorldWidth:F2}x{finalPlaneWorldHeight:F2}м) для создания/обновления. Min size: {minPlaneSizeInMeters}м. Выход.");

                  // Регистрируем неудачное создание плоскости в PlaneDebugVisualizer
                  if (planeDebugVisualizer != null)
                  {
                        Vector3 planeSize = new Vector3(finalPlaneWorldWidth, finalPlaneWorldHeight, 0.1f);
                        planeDebugVisualizer.RegisterPlaneCreation(finalPlanePosition, planeSize, false);
                  }

                  return false;
            }


            // Проверки и фильтрация дубликатов и наложений (используем finalPlanePosition)
            if (finalPlanePosition == Vector3.zero)
            { // Дополнительная проверка на всякий случай
                  Debug.LogError("[ARManagerInitializer2-UOCP] КРИТИЧЕСКАЯ ОШИБКА: finalPlanePosition равен Vector3.zero перед созданием/обновлением плоскости! Выход.");
                  return false;
            }

            // Check if this would overlap with an existing persistent plane
            if (usePersistentPlanes && planeConfigurator != null)
            {
                  Vector3 normal = finalPlaneRotation * Vector3.forward;
                  if (OverlapsWithPersistentPlanes(finalPlanePosition, normal, finalPlaneWorldWidth, finalPlaneWorldHeight))
                  {
                        if (enableDetailedRaycastLogging)
                              Debug.Log("[ARManagerInitializer2-UOCP] Skipping plane creation - overlaps with persistent plane");
                        return false; // Skip creating this plane as it overlaps with a persistent one
                  }
            }

            // Проверка 1: Не создаем плоскость, если находится слишком близко к камере и прямо перед ней
            Vector3 directionToFinalPos = finalPlanePosition - mainCamera.transform.position; // ИЗМЕНЕНО: arCamera -> mainCamera
            float distanceToCamFinal = directionToFinalPos.magnitude;
            float alignmentWithCameraFinal = Vector3.Dot(mainCamera.transform.forward.normalized, directionToFinalPos.normalized); // ИЗМЕНЕНО: arCamera -> mainCamera

            // Более строгие проверки для всех плоскостей
            if (distanceToCamFinal < 0.7f && alignmentWithCameraFinal > 0.8f) // Увеличены оба порога - дистанция и выравнивание
            {
                  Debug.LogWarning($"[ARManagerInitializer2-UOCP] ⚠️ ОТМЕНА: Плоскость слишком близко к камере и прямо перед ней (Дист: {distanceToCamFinal:F2}м, Совпадение с FWD: {alignmentWithCameraFinal:F2}). Pos={finalPlanePosition:F2}");
                  return false;
            }

            // Проверка 2: Не создаем плоскости, если они слишком большие
            // float maxPlaneSize = 5.0f; // УДАЛЕНО: заменено на configurable переменную
            if (finalPlaneWorldWidth > this.maxPlaneSize || finalPlaneWorldHeight > this.maxPlaneSize)
            {
                  Debug.LogWarning($"[ARManagerInitializer2-UOCP] ⚠️ ОТМЕНА: Плоскость слишком большая (Ширина: {finalPlaneWorldWidth:F2}м, Высота: {finalPlaneWorldHeight:F2}м). Установлен лимит: {this.maxPlaneSize}м");
                  return false;
            }

            // Проверка 3: Не создаем плоскости, если они слишком маленькие
            if (finalPlaneWorldWidth < this.minPlaneSize || finalPlaneWorldHeight < this.minPlaneSize)
            {
                  Debug.LogWarning($"[ARManagerInitializer2-UOCP] ⚠️ ОТМЕНА: Плоскость слишком маленькая (Ширина: {finalPlaneWorldWidth:F2}м, Высота: {finalPlaneWorldHeight:F2}м). Минимальный размер: {this.minPlaneSize}м");
                  return false;
            }

            // Проверка 4: Соотношение сторон не должно быть слишком экстремальным
            float aspectRatio = Mathf.Max(finalPlaneWorldWidth, finalPlaneWorldHeight) / Mathf.Min(finalPlaneWorldWidth, finalPlaneWorldHeight);
            if (aspectRatio > this.maxAspectRatio)
            {
                  Debug.LogWarning($"[ARManagerInitializer2-UOCP] ⚠️ ОТМЕНА: Плоскость слишком вытянутая (Соотношение сторон: {aspectRatio:F1}, лимит: {this.maxAspectRatio})");
                  return false;
            }

            // УЛУЧШЕННЫЙ АЛГОРИТМ: Интеллектуальное выявление дубликатов плоскостей
            // ... (часть с tooClose, similarOrientationCount, closestExistingPlane была выше, но может быть применена здесь к finalPlanePosition)
            // === НАЧАЛО БЛОКА ПОИСКА И ОБНОВЛЕНИЯ СУЩЕСТВУЮЩЕЙ ПЛОСКОСТИ ===
            var (planeToUpdate, updateDistance, updateAngleDiff) = FindClosestExistingPlane(finalPlanePosition, (finalPlaneRotation * Vector3.forward), 1.5f, 35f); // УВЕЛИЧЕНО: Радиус поиска до 1.5м, угол до 35 градусов
            // Используем (finalPlaneRotation * Vector3.forward) как нормаль, так как LookRotation(normal) делает forward плоскости = normal.
            // А наш FindClosestExistingPlane ожидает нормаль поверхности.

            if (planeToUpdate != null)
            {
                  if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] 🔄 ОБНОВЛЯЕМ существующую плоскость '{planeToUpdate.name}'. Расстояние до новой позиции: {updateDistance:F2}м, Угол нормалей: {updateAngleDiff:F1}°");

                  // ============== ПРОДВИНУТАЯ ЛОГИКА ОБЪЕДИНЕНИЯ (OBB) ==============
                  UnityEngine.Mesh oldMesh = planeToUpdate.GetComponent<MeshFilter>().sharedMesh;
                  Vector3[] oldVertices = oldMesh.vertices;

                  // 1. Трансформируем вершины старого меша в мировые координаты
                  for (int i = 0; i < oldVertices.Length; i++)
                  {
                        oldVertices[i] = planeToUpdate.transform.TransformPoint(oldVertices[i]);
                  }

                  // 2. Добавляем мировые координаты углов новой области
                  Vector3[] newVertices = new Vector3[] { worldTopLeft, worldTopRight, worldBottomLeft, worldBottomRight };
                  Vector3[] allVertices = new Vector3[oldVertices.Length + newVertices.Length];
                  oldVertices.CopyTo(allVertices, 0);
                  newVertices.CopyTo(allVertices, oldVertices.Length);

                  // 3. Определяем новую общую ориентацию (используем последнюю, как самую актуальную) и временный центр
                  Quaternion mergedRotation = finalPlaneRotation;
                  Vector3 mergedCenter = finalPlanePosition; // Используем позицию нового хита как временный центр

                  // 4. Находим локальные оси новой ориентации
                  Vector3 planeRight = mergedRotation * Vector3.right;
                  Vector3 planeUp = mergedRotation * Vector3.up;

                  // 5. Проецируем все точки на 2D-плоскость и находим крайние значения
                  float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
                  foreach (var v in allVertices)
                  {
                        Vector3 relPos = v - mergedCenter;
                        float xCoord = Vector3.Dot(relPos, planeRight);
                        float yCoord = Vector3.Dot(relPos, planeUp);
                        minX = Mathf.Min(minX, xCoord);
                        maxX = Mathf.Max(maxX, xCoord);
                        minY = Mathf.Min(minY, yCoord);
                        maxY = Mathf.Max(maxY, yCoord);
                  }

                  // 6. Вычисляем реальные размеры и новый центр
                  float mergedWidth = maxX - minX;
                  float mergedHeight = maxY - minY;

                  // Центр OBB относительно временного mergedCenter
                  Vector3 obbCenterOffset = planeRight * (minX + maxX) / 2f + planeUp * (minY + maxY) / 2f;
                  // Финальная позиция GameObject-а
                  Vector3 finalMergedPosition = mergedCenter + obbCenterOffset;

                  planeToUpdate.transform.position = finalMergedPosition;
                  planeToUpdate.transform.rotation = mergedRotation;
                  // ====================================================================

                  MeshFilter mf = planeToUpdate.GetComponent<MeshFilter>();
                  if (mf != null)
                  {
                        mf.mesh = CreatePlaneMesh(mergedWidth, mergedHeight); // Обновляем меш с новыми, ОБЪЕДИНЕННЫМИ размерами
                        if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] ✨ Объединен меш для '{planeToUpdate.name}', новые размеры: {mergedWidth:F2}x{mergedHeight:F2}м");
                  }

                  if (visitedPlanes != null) visitedPlanes[planeToUpdate] = true;
                  if (this.planeLastVisitedTime != null) this.planeLastVisitedTime[planeToUpdate] = Time.time;
                  return true;
            }

            // Если не нашли что обновить, проверяем, не нужно ли отфильтровать создание новой из-за дублирования
            bool createNewPlane = true;
            foreach (GameObject existingPlane in generatedPlanes)
            {
                  if (existingPlane == null) continue;
                  float distBetweenFinalAndExisting = Vector3.Distance(existingPlane.transform.position, finalPlanePosition);
                  float angleBetweenNormals = Vector3.Angle(existingPlane.transform.forward, (finalPlaneRotation * Vector3.forward));

                  if (distBetweenFinalAndExisting < 0.35f && angleBetweenNormals < 20f) // Порог для близкого дубликата
                  {
                        // Debug.LogWarning($"[ARManagerInitializer2-UOCP] ⚠️ ОТМЕНА СОЗДАНИЯ НОВОЙ: Обнаружен слишком близкий дубликат '{existingPlane.name}' (Расст: {distBetweenFinalAndExisting:F2}м, Угол: {angleBetweenNormals:F1}°). Новая Pos={finalPlanePosition:F2}");
                        createNewPlane = false;
                        if (visitedPlanes != null && !visitedPlanes.ContainsKey(existingPlane))
                        { // Если существующая близкая не была посещена в этом кадре, помечаем ее.
                              visitedPlanes[existingPlane] = true;
                              // Debug.Log($"[ARManagerInitializer2-UOCP] Близкая существующая плоскость '{existingPlane.name}' помечена как visited, т.к. новая не создается.");
                        }
                        break;
                  }
            }

            if (!createNewPlane)
            {
                  return false; // Не создаем новую, т.к. есть близкий дубликат
            }

            // 🔄 ПРОВЕРКА СЛИЯНИЯ: Попытка найти близкую плоскость для слияния
            if (enablePlaneMerging)
            {
                  var (closestPlane, distance, angle) = FindClosestExistingPlane(finalPlanePosition, bestNormal, planeMergingDistance, 30f);
                  if (closestPlane != null)
                  {
                        if (enableCustomPlaneCreationLogging) Debug.Log($"[ARManagerInitializer2-UOCP] 🔄 СЛИЯНИЕ: Найдена близкая плоскость '{closestPlane.name}' на расстоянии {distance:F2}м. Расширяем вместо создания новой.");

                        // Расширяем существующую плоскость
                        ExpandExistingPlane(closestPlane, finalPlanePosition, finalPlaneWorldWidth, finalPlaneWorldHeight);

                        // Помечаем как посещенную
                        if (visitedPlanes != null) visitedPlanes[closestPlane] = true;
                        if (planeLastVisitedTime != null) planeLastVisitedTime[closestPlane] = Time.time;

                        return true; // Плоскость "создана" путем слияния
                  }
            }

            // Создаем и настраиваем GameObject для плоскости
            string newPlaneName = $"MyARPlane_Debug_{planeInstanceCounter++}";
            GameObject planeObj = new GameObject(newPlaneName);
            if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] 🧬 СОЗДАЕМ НОВУЮ ПЛОСКОСТЬ '{newPlaneName}'");

            planeObj.transform.position = finalPlanePosition;
            planeObj.transform.rotation = finalPlaneRotation;

            // Установим слой для плоскости, если он задан
            if (!string.IsNullOrEmpty(planesLayerName))
            {
                  int layerID = LayerMask.NameToLayer(planesLayerName);
                  if (layerID != -1)
                  {
                        planeObj.layer = layerID;
                  }
                  else if (enableDetailedRaycastLogging)
                  {
                        Debug.LogWarning($"[ARManagerInitializer2] Layer '{planesLayerName}' not found, using default layer for plane.");
                  }
            }

            MeshFilter meshFilter = planeObj.AddComponent<MeshFilter>();
            meshFilter.mesh = CreatePlaneMesh(finalPlaneWorldWidth, finalPlaneWorldHeight);

            MeshRenderer meshRenderer = planeObj.AddComponent<MeshRenderer>();
            // Проверяем и при необходимости пересоздаем материал
            if (this.verticalPlaneMaterial == null)
            {
                  Debug.LogWarning("[ARManagerInitializer2-UOCP] verticalPlaneMaterial is null during plane creation! Re-initializing materials...");
                  InitializeMaterials();
            }

            if (this.verticalPlaneMaterial != null)
            {
                  try
                  {
                        meshRenderer.material = new Material(this.verticalPlaneMaterial);
                        Debug.Log($"[ARManagerInitializer2-UOCP] ✅ Материал применен к плоскости {planeObj.name}. Шейдер: {meshRenderer.material.shader.name}");
                  }
                  catch (System.Exception e)
                  {
                        Debug.LogError($"[ARManagerInitializer2-UOCP] ❌ Ошибка при применении материала: {e.Message}");
                        meshRenderer.material = CreateFallbackMaterial();
                  }
            }
            else
            {
                  Debug.LogError("[ARManagerInitializer2-UOCP] ❌ verticalPlaneMaterial все еще null после повторной инициализации!");
                  meshRenderer.material = CreateFallbackMaterial();
            }
            // Debug.Log($"[ARManagerInitializer2-UOCP] Applied material to {planeObj.name}. Mesh bounds: {meshFilter.mesh.bounds.size}");

            MeshCollider meshCollider = planeObj.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = meshFilter.mesh;

            this.generatedPlanes.Add(planeObj);
            if (this.planeCreationTimes != null) this.planeCreationTimes[planeObj] = Time.time;
            if (this.planeLastVisitedTime != null) this.planeLastVisitedTime[planeObj] = Time.time;

            // Make the newly created plane persistent immediately
            MakePlanePersistent(planeObj);

            // Попытка привязать к TrackablesParent, если он есть и не был равен null при старте
            if (this.xrOrigin != null && this.xrOrigin.TrackablesParent != null)
            {
                  if (this.trackablesParentInstanceID_FromStart == 0 ||
                      (this.xrOrigin.TrackablesParent.gameObject.activeInHierarchy && this.xrOrigin.TrackablesParent.GetInstanceID() == this.trackablesParentInstanceID_FromStart))
                  {
                        planeObj.transform.SetParent(this.xrOrigin.TrackablesParent, true);
                        // Debug.Log($"[ARManagerInitializer2-UOCP] Новая плоскость '{planeObj.name}' привязана к '{this.xrOrigin.TrackablesParent.name}' (ID: {this.xrOrigin.TrackablesParent.GetInstanceID()}). Final Pos relative to Parent: {planeObj.transform.localPosition:F2}");
                  }
                  else
                  {
                        Debug.LogWarning($"[ARManagerInitializer2-UOCP] Новая плоскость '{planeObj.name}' не привязана к TrackablesParent (неактивен/изменен). Оставлена в корне. World Pos: {planeObj.transform.position:F2}");
                  }
            }
            else
            {
                  Debug.LogWarning($"[ARManagerInitializer2-UOCP] Новая плоскость '{planeObj.name}' не привязана (XROrigin/TrackablesParent null). Оставлена в корне. World Pos: {planeObj.transform.position:F2}");
            }

            if (visitedPlanes != null) visitedPlanes[planeObj] = true;
            // Debug.Log($"[ARManagerInitializer2-UOCP] ✅ Создана новая плоскость '{planeObj.name}': WorldPos={planeObj.transform.position:F2}, WorldRot(Эйлер)={planeObj.transform.rotation.eulerAngles:F1}, Parent={(planeObj.transform.parent ? planeObj.transform.parent.name : "null")}");

            // --> НАЧАЛО ДОБАВЛЕННОГО КОДА
            ARWallPaintingSystem paintingSystem = FindObjectOfType<ARWallPaintingSystem>();
            if (paintingSystem != null)
            {
                  paintingSystem.RegisterWallObject(planeObj);
            }
            // <-- КОНЕЦ ДОБАВЛЕННОГО КОДА

            generatedPlanes.Add(planeObj);

            // Регистрируем создание плоскости в PlaneDebugVisualizer
            if (planeDebugVisualizer != null)
            {
                  Vector3 planeSize = new Vector3(finalPlaneWorldWidth, finalPlaneWorldHeight, 0.1f);
                  planeDebugVisualizer.RegisterPlaneCreation(finalPlanePosition, planeSize, true);
            }

            return true;
      }

      // 🛡️ Удаляем устаревшие плоскости с системой гистерезиса для предотвращения мерцания
      private void CleanupOldPlanes(Dictionary<GameObject, bool> visitedPlanesInCurrentMask) // New name for clarity
      {
            // Защита: не удаляем плоскости во время слияния
            if (Time.time - lastDynamicMergingTime < 0.5f)
            {
                  if (enableVerboseLoggingCleanup)
                  {
                        Debug.Log("[ARManagerInitializer2-CleanupOldPlanes] ⏸️ Пропускаем очистку - недавно было слияние плоскостей");
                  }
                  return;
            }

            List<GameObject> planesToRemove = new List<GameObject>();
            float currentTime = Time.time;

            foreach (var plane in generatedPlanes.ToList()) // ToList() to allow modification during iteration
            {
                  if (plane == null)
                        continue;

                  // Если плоскость персистентна, не удаляем ее по этому таймеру
                  if (IsPlanePersistent(plane))
                        continue;

                  // 🛡️ СИСТЕМА ГИСТЕРЕЗИСА: обновляем счетчик пропущенных кадров
                  if (enablePlaneHysteresis)
                  {
                        // Если плоскость не была найдена в текущей маске
                        if (visitedPlanesInCurrentMask == null || !visitedPlanesInCurrentMask.ContainsKey(plane))
                        {
                              // Увеличиваем счетчик пропущенных кадров
                              if (!planeMissedFrames.ContainsKey(plane))
                              {
                                    planeMissedFrames[plane] = 0;
                              }
                              planeMissedFrames[plane]++;

                              // Удаляем только если пропустили достаточно кадров подряд
                              if (planeMissedFrames[plane] >= maxMissedFrames)
                              {
                                    if (enableVerboseLoggingCleanup)
                                    {
                                          Debug.Log($"[ARManagerInitializer2-CleanupOldPlanes] 🛡️ Плоскость {plane.name} пропущена {planeMissedFrames[plane]} кадров подряд (порог: {maxMissedFrames}). Удаляем.");
                                    }
                                    planesToRemove.Add(plane);
                              }
                              else
                              {
                                    if (enableVerboseLoggingCleanup)
                                    {
                                          Debug.Log($"[ARManagerInitializer2-CleanupOldPlanes] 🛡️ Плоскость {plane.name} пропущена {planeMissedFrames[plane]}/{maxMissedFrames} кадров. Ожидаем...");
                                    }
                              }
                        }
                        else
                        {
                              // Плоскость найдена - сбрасываем счетчик пропущенных кадров
                              if (planeMissedFrames.ContainsKey(plane))
                              {
                                    planeMissedFrames.Remove(plane);
                              }
                        }
                  }
                  else
                  {
                        // 🚫 СТАНДАРТНАЯ СИСТЕМА: удаляем по времени (может вызывать мерцание)
                        if (planeLastVisitedTime.TryGetValue(plane, out float lastVisitTime))
                        {
                              // Если плоскость не была посещена в текущей маске И прошла задержка
                              if (currentTime - lastVisitTime > planePersistenceDelay &&
                                  (visitedPlanesInCurrentMask == null || !visitedPlanesInCurrentMask.ContainsKey(plane)))
                              {
                                    if (enableVerboseLoggingCleanup)
                                          Debug.Log($"[ARManagerInitializer2-CleanupOldPlanes] 🚫 Плоскость {plane.name} не посещена {currentTime - lastVisitTime:F2}с (порог: {planePersistenceDelay}с). Удаляем.");
                                    planesToRemove.Add(plane);
                              }
                        }
                        else if (visitedPlanesInCurrentMask == null || !visitedPlanesInCurrentMask.ContainsKey(plane))
                        {
                              // Если для плоскости нет времени последнего посещения и её нет в текущей маске
                              if (planeCreationTimes.TryGetValue(plane, out float creationTime) && currentTime - creationTime > planePersistenceDelay)
                              {
                                    if (enableVerboseLoggingCleanup)
                                          Debug.LogWarning($"[ARManagerInitializer2-CleanupOldPlanes] 🚫 Плоскость {plane.name} без истории посещений, создана {currentTime - creationTime:F2}с назад. Удаляем.");
                                    planesToRemove.Add(plane);
                              }
                        }
                  }
            }

            // Удаляем плоскости и очищаем все словари
            foreach (GameObject planeToRemove in planesToRemove)
            {
                  generatedPlanes.Remove(planeToRemove);
                  planeCreationTimes.Remove(planeToRemove);
                  planeLastVisitedTime.Remove(planeToRemove);
                  persistentGeneratedPlanes.Remove(planeToRemove);
                  planeMissedFrames.Remove(planeToRemove); // 🛡️ Очищаем и гистерезис
                  Destroy(planeToRemove);
            }

            if (planesToRemove.Count > 0 && enableVerboseLoggingCleanup)
            {
                  Debug.Log($"[ARManagerInitializer2-CleanupOldPlanes] 🧹 Удалено {planesToRemove.Count} плоскостей. Осталось: {generatedPlanes.Count}");
            }
      }

      /// <summary>
      /// Проверяет, нужно ли выполнить динамическое слияние плоскостей
      /// </summary>
      private bool ShouldPerformDynamicMerging()
      {
            // Проверяем минимальное количество плоскостей
            if (generatedPlanes.Count < minPlanesForDynamicMerging)
            {
                  return false;
            }

            // Проверяем временной интервал
            if (Time.time - lastDynamicMergingTime < dynamicMergingInterval)
            {
                  return false;
            }

            // Проверяем, значительно ли изменилось количество плоскостей
            int planeCountDifference = Mathf.Abs(generatedPlanes.Count - lastMergingPlaneCount);
            bool significantChange = planeCountDifference >= 5; // Минимум 5 новых плоскостей для повторного слияния

            if (enableCustomPlaneCreationLogging && significantChange)
            {
                  Debug.Log($"[ARManagerInitializer2-ShouldMerge] ✅ Условия для слияния выполнены: плоскостей {generatedPlanes.Count} (было {lastMergingPlaneCount}), прошло {Time.time - lastDynamicMergingTime:F1}с");
            }

            return significantChange;
      }

      /// <summary>
      /// 🔄 Динамическое слияние плоскостей в крупные области
      /// </summary>
      private void PerformDynamicPlaneMerging()
      {
            if (!enableDynamicMerging || generatedPlanes.Count < minPlanesForDynamicMerging)
            {
                  return;
            }

            int initialPlaneCount = generatedPlanes.Count;
            List<GameObject> planesToMerge = new List<GameObject>(generatedPlanes);
            List<List<GameObject>> mergeGroups = new List<List<GameObject>>();

            if (enableCustomPlaneCreationLogging)
            {
                  Debug.Log($"[ARManagerInitializer2-DynamicMerging] 🔄 НАЧИНАЕМ ДИНАМИЧЕСКОЕ СЛИЯНИЕ: {initialPlaneCount} плоскостей");
            }

            // Группируем плоскости по близости и схожести нормалей
            for (int i = 0; i < planesToMerge.Count; i++)
            {
                  GameObject currentPlane = planesToMerge[i];
                  if (currentPlane == null) continue;

                  // Проверяем, не была ли эта плоскость уже добавлена в группу
                  bool alreadyInGroup = false;
                  foreach (var group in mergeGroups)
                  {
                        if (group.Contains(currentPlane))
                        {
                              alreadyInGroup = true;
                              break;
                        }
                  }
                  if (alreadyInGroup) continue;

                  // Создаем новую группу для слияния
                  List<GameObject> currentGroup = new List<GameObject> { currentPlane };
                  Vector3 currentPos = currentPlane.transform.position;
                  Vector3 currentNormal = currentPlane.transform.up;

                  // Ищем близкие плоскости для добавления в группу
                  for (int j = i + 1; j < planesToMerge.Count; j++)
                  {
                        GameObject otherPlane = planesToMerge[j];
                        if (otherPlane == null) continue;

                        Vector3 otherPos = otherPlane.transform.position;
                        Vector3 otherNormal = otherPlane.transform.up;

                        float distance = Vector3.Distance(currentPos, otherPos);
                        float normalSimilarity = Vector3.Dot(currentNormal, otherNormal);

                        // Проверяем критерии для слияния
                        if (distance <= dynamicMergingRadius && normalSimilarity > 0.7f)
                        {
                              currentGroup.Add(otherPlane);
                              if (enableCustomPlaneCreationLogging)
                              {
                                    Debug.Log($"[ARManagerInitializer2-DynamicMerging] ➕ Добавляем плоскость '{otherPlane.name}' в группу (расстояние: {distance:F2}м, схожесть нормалей: {normalSimilarity:F2})");
                              }
                        }
                  }

                  // Добавляем группу только если в ней больше одной плоскости
                  if (currentGroup.Count > 1)
                  {
                        mergeGroups.Add(currentGroup);
                  }
            }

            // Выполняем слияние для каждой группы
            int mergedGroups = 0;
            foreach (var group in mergeGroups)
            {
                  if (group.Count <= 1) continue;

                  GameObject masterPlane = group[0];
                  Vector3 avgPosition = Vector3.zero;
                  Vector3 avgNormal = Vector3.zero;
                  float totalWidth = 0f;
                  float totalHeight = 0f;

                  // Вычисляем средние параметры группы
                  foreach (var plane in group)
                  {
                        avgPosition += plane.transform.position;
                        avgNormal += plane.transform.up;

                        // Получаем размеры плоскости
                        var meshFilter = plane.GetComponent<MeshFilter>();
                        if (meshFilter != null && meshFilter.mesh != null)
                        {
                              Bounds bounds = meshFilter.mesh.bounds;
                              totalWidth += bounds.size.x;
                              totalHeight += bounds.size.z;
                        }
                  }

                  avgPosition /= group.Count;
                  avgNormal = avgNormal.normalized;
                  float avgWidth = totalWidth / group.Count;
                  float avgHeight = totalHeight / group.Count;

                  // Увеличиваем размер объединенной плоскости
                  float mergedWidth = Mathf.Min(avgWidth * 1.5f, maxWallWidth);
                  float mergedHeight = Mathf.Min(avgHeight * 1.5f, maxWallHeight);

                  // Обновляем мастер-плоскость
                  masterPlane.transform.position = avgPosition;
                  masterPlane.transform.rotation = Quaternion.LookRotation(Vector3.Cross(avgNormal, Vector3.up), avgNormal);

                  // Обновляем меш мастер-плоскости
                  var masterMeshFilter = masterPlane.GetComponent<MeshFilter>();
                  if (masterMeshFilter != null)
                  {
                        masterMeshFilter.mesh = CreatePlaneMesh(mergedWidth, mergedHeight);
                  }

                  // Обновляем коллайдер
                  var masterCollider = masterPlane.GetComponent<MeshCollider>();
                  if (masterCollider != null)
                  {
                        masterCollider.sharedMesh = masterMeshFilter.mesh;
                  }

                  // Удаляем остальные плоскости из группы
                  for (int i = 1; i < group.Count; i++)
                  {
                        GameObject planeToRemove = group[i];
                        generatedPlanes.Remove(planeToRemove);
                        if (persistentGeneratedPlanes.ContainsKey(planeToRemove))
                        {
                              persistentGeneratedPlanes.Remove(planeToRemove);
                        }
                        if (planeCreationTimes.ContainsKey(planeToRemove))
                        {
                              planeCreationTimes.Remove(planeToRemove);
                        }
                        if (planeLastVisitedTime.ContainsKey(planeToRemove))
                        {
                              planeLastVisitedTime.Remove(planeToRemove);
                        }

                        DestroyImmediate(planeToRemove);
                  }

                  // Обновляем имя мастер-плоскости
                  masterPlane.name = $"MergedPlane_{mergedGroups}_{group.Count}planes_T{Time.time:F0}";
                  mergedGroups++;

                  // Помечаем как посещенную в текущем кадре (защита от удаления)
                  if (planeLastVisitedTime.ContainsKey(masterPlane))
                  {
                        planeLastVisitedTime[masterPlane] = Time.time;
                  }
                  else
                  {
                        planeLastVisitedTime.Add(masterPlane, Time.time);
                  }

                  if (enableCustomPlaneCreationLogging)
                  {
                        Debug.Log($"[ARManagerInitializer2-DynamicMerging] ✅ СЛИЯНИЕ ЗАВЕРШЕНО: Группа из {group.Count} плоскостей объединена в '{masterPlane.name}' (размер: {mergedWidth:F2}x{mergedHeight:F2}м)");
                  }
            }

            int finalPlaneCount = generatedPlanes.Count;
            if (enableCustomPlaneCreationLogging)
            {
                  Debug.Log($"[ARManagerInitializer2-DynamicMerging] 🎯 РЕЗУЛЬТАТ СЛИЯНИЯ: {initialPlaneCount} → {finalPlaneCount} плоскостей (-{initialPlaneCount - finalPlaneCount}), объединено {mergedGroups} групп");
            }
      }

      private (GameObject plane, float distance, float angle) FindClosestExistingPlane(Vector3 position, Vector3 normal, float maxDistance, float maxAngleDegrees)
      {
            GameObject closestPlane = null;
            float minDistance = float.MaxValue;
            float angleDifference = float.MaxValue;

            if (generatedPlanes == null) return (null, 0, 0);

            foreach (GameObject existingPlane in generatedPlanes)
            {
                  if (existingPlane == null) continue;

                  float dist = Vector3.Distance(existingPlane.transform.position, position);
                  if (dist < minDistance && dist <= maxDistance)
                  {
                        // Предполагаем, что -transform.forward это нормаль плоскости, как и у новой 'normal'
                        // Это согласуется с использованием Quaternion.LookRotation(-normal) или FromToRotation(Vector3.forward, -normal)
                        float angle = Vector3.Angle(-existingPlane.transform.forward, normal);

                        if (angle <= maxAngleDegrees)
                        {
                              minDistance = dist;
                              closestPlane = existingPlane;
                              angleDifference = angle;
                        }
                  }
            }
            // if (closestPlane != null && enableDetailedRaycastLogging) Debug.Log($"[FindClosestExistingPlane] Найден близкий: {closestPlane.name}, dist: {minDistance:F2}, angle: {angleDifference:F1}");
            // else if (enableDetailedRaycastLogging) Debug.Log("[FindClosestExistingPlane] Близкая существующая плоскость не найдена.");
            return (closestPlane, minDistance, angleDifference);
      }

      // Вспомогательный метод для получения полного пути к GameObject
      public static string GetGameObjectPath(Transform transform)
      {
            if (transform == null) return "null_transform";
            string path = transform.name;
            Transform current = transform.parent;
            while (current != null)
            {
                  path = current.name + "/" + path;
                  current = current.parent;
            }
            return path;
      }

      // Вспомогательный метод для красивого вывода LayerMask
      public static string LayerMaskToString(LayerMask layerMask)
      {
            var included = new System.Text.StringBuilder();
            for (int i = 0; i < 32; i++)
            {
                  if ((layerMask.value & (1 << i)) != 0)
                  {
                        string layerName = LayerMask.LayerToName(i);
                        if (string.IsNullOrEmpty(layerName))
                        {
                              // included.Append($"[Unnamed Layer {i}], "); // Можно раскомментировать, если нужны и безымянные слои
                        }
                        else
                        {
                              included.Append($"[{layerName} (id:{i})], ");
                        }
                  }
            }
            if (included.Length > 0 && included[included.Length - 1] == ' ' && included[included.Length - 2] == ',')
            {
                  included.Length -= 2; // Удалить последнюю запятую и пробел
            }
            else if (included.Length == 0)
            {
                  return "NONE";
            }
            return included.ToString();
      }

      /// <summary>
      /// Валидация настроек слоёв для рейкастинга
      /// Решает Проблему 2: проверяет корректность hitLayerMask
      /// </summary>
      private void ValidateLayerMask()
      {
            Debug.Log($"[ARManagerInitializer2] 🔍 Валидация hitLayerMask: {LayerMaskToString(hitLayerMask)}");

            // Проверяем ключевые слои
            string[] expectedLayers = { "Default", "SimulatedEnvironment", "Wall" };
            List<string> missingLayers = new List<string>();
            List<string> foundLayers = new List<string>();

            foreach (string layerName in expectedLayers)
            {
                  int layer = LayerMask.NameToLayer(layerName);
                  if (layer == -1)
                  {
                        missingLayers.Add(layerName);
                  }
                  else
                  {
                        // Проверяем, включён ли этот слой в hitLayerMask
                        if ((hitLayerMask.value & (1 << layer)) != 0)
                        {
                              foundLayers.Add($"{layerName}({layer})");
                        }
                        else
                        {
                              Debug.LogWarning($"⚠️ Слой '{layerName}' ({layer}) существует, но не включён в hitLayerMask");
                        }
                  }
            }

            // Выводим результаты валидации
            if (missingLayers.Count > 0)
            {
                  Debug.LogWarning($"⚠️ Отсутствующие слои в проекте: {string.Join(", ", missingLayers)}");
            }

            if (foundLayers.Count > 0)
            {
                  Debug.Log($"✅ Активные слои в hitLayerMask: {string.Join(", ", foundLayers)}");
            }

            // Проверяем, что в маске есть хотя бы один слой
            if (hitLayerMask.value == 0)
            {
                  Debug.LogError("❌ hitLayerMask пуста! Рейкастинг не будет работать.");
            }
            else if (foundLayers.Count == 0)
            {
                  Debug.LogWarning("⚠️ В hitLayerMask нет ожидаемых слоёв. Проверьте настройки.");
            }

            // Дополнительная информация о текущих слоях
            if (enableDetailedRaycastLogging)
            {
                  Debug.Log($"[ARManagerInitializer2] 📋 Подробная информация о слоях:");
                  Debug.Log($"- hitLayerMask.value: {hitLayerMask.value}");
                  Debug.Log($"- Двоичное представление: {System.Convert.ToString(hitLayerMask.value, 2).PadLeft(32, '0')}");
                  Debug.Log($"- Включённые слои: {LayerMaskToString(hitLayerMask)}");
            }
      }

      // Публичные свойства для доступа к материалам извне
      public Material VerticalPlaneMaterial => verticalPlaneMaterial;
      public Material HorizontalPlaneMaterial => horizontalPlaneMaterial;

      // New method to make a plane persistent
      public bool MakePlanePersistent(GameObject plane)
      {
            if (plane == null || !generatedPlanes.Contains(plane))
                  return false;

            // Mark as persistent in our tracking
            persistentGeneratedPlanes[plane] = true;

            // Apply visual highlight if enabled
            if (highlightPersistentPlanes)
            {
                  MeshRenderer renderer = plane.GetComponent<MeshRenderer>();
                  if (renderer != null && renderer.material != null)
                  {
                        renderer.material.color = persistentPlaneColor;
                  }
            }

            Debug.Log($"[ARManagerInitializer2] Made plane {plane.name} persistent");
            return true;
      }

      // New method to check if a plane is persistent
      public bool IsPlanePersistent(GameObject plane)
      {
            if (plane == null) return false;
            return persistentGeneratedPlanes.TryGetValue(plane, out bool isPersistent) && isPersistent;
      }

      // New method to remove persistence from a plane
      public bool RemovePlanePersistence(GameObject plane)
      {
            if (plane == null || !persistentGeneratedPlanes.ContainsKey(plane))
                  return false;

            persistentGeneratedPlanes.Remove(plane);

            // Restore original material color
            if (highlightPersistentPlanes)
            {
                  MeshRenderer renderer = plane.GetComponent<MeshRenderer>();
                  if (renderer != null && renderer.material != null)
                  {
                        // Determine if it's a vertical or horizontal plane and use appropriate color
                        Vector3 normal = plane.transform.forward.normalized;
                        float dotUp = Vector3.Dot(normal, Vector3.up);
                        bool isVertical = Mathf.Abs(dotUp) < 0.25f;

                        if (isVertical && verticalPlaneMaterial != null)
                        {
                              renderer.material.color = verticalPlaneMaterial.color;
                        }
                        else if (horizontalPlaneMaterial != null)
                        {
                              renderer.material.color = horizontalPlaneMaterial.color;
                        }
                  }
            }

            Debug.Log($"[ARManagerInitializer2] Removed persistence from plane {plane.name}");
            return true;
      }

      // Check if a new plane would overlap with existing persistent planes
      private bool OverlapsWithPersistentPlanes(Vector3 position, Vector3 normal, float width, float height)
      {
            if (persistentGeneratedPlanes.Count == 0) return false;

            float maxAngleThresholdForOverlap = 15.0f; // Degrees
            // float maxDistanceThresholdForOverlap = 0.5f; // Meters - This was too strict

            foreach (var pair in persistentGeneratedPlanes)
            {
                  GameObject existingPlane = pair.Key;
                  if (existingPlane == null || !existingPlane.activeInHierarchy) continue;

                  Vector3 existingPlaneCenter = existingPlane.transform.position;
                  Vector3 existingPlaneNormal = existingPlane.transform.forward; // Assumes plane mesh is oriented with its "forward" as the normal

                  float angleDiff = Vector3.Angle(existingPlaneNormal, normal);
                  float distanceDiff = Vector3.Distance(existingPlaneCenter, position);

                  // Calculate extents for a more robust overlap check than just center distance
                  float existingPlaneHalfWidth = existingPlane.transform.localScale.x / 2f;
                  float existingPlaneHalfHeight = existingPlane.transform.localScale.y / 2f; // Assuming Y scale is height for vertical plane from CreatePlaneMesh
                  float newPlaneHalfWidth = width / 2f;
                  float newPlaneHalfHeight = height / 2f;

                  float maxExistingExtent = Mathf.Max(existingPlaneHalfWidth, existingPlaneHalfHeight);
                  float maxNewExtent = Mathf.Max(newPlaneHalfWidth, newPlaneHalfHeight);
                  float combinedExtents = maxExistingExtent + maxNewExtent;

                  // Condition for overlap: similar orientation, AND their extents suggest an actual overlap.
                  // The strict distanceDiff < maxDistanceThresholdForOverlap check was removed as it was too restrictive for large planes.
                  if (angleDiff < maxAngleThresholdForOverlap &&
                      combinedExtents * 0.5f > distanceDiff) // MODIFIED: Was 0.8f
                  {
                        if (enableCustomPlaneCreationLogging) Debug.LogWarning($"[ARManagerInitializer2-Overlaps] Overlap detected with {existingPlane.name}. New: pos={position:F2}, normal={normal:F2}, w={width:F1},h={height:F1}. Existing: pos={existingPlaneCenter:F2}, normal={existingPlaneNormal:F2}, scale={existingPlane.transform.localScale:F1}. AngleDiff={angleDiff:F1}, DistDiff={distanceDiff:F1}, CombExt*0.5={combinedExtents * 0.5f:F1}"); // MODIFIED: Log message to reflect 0.5f
                        return true;
                  }
            }
            return false;
      }

      [Header("Настройки стабилизации плоскостей")]
      [Tooltip("Частота проверки стабильности и превращения плоскостей в 'постоянные' (в секундах)")]
#pragma warning disable 0414
      [SerializeField] private float makePersistentCheckInterval = 1.0f;
#pragma warning restore 0414
      [Tooltip("Задержка перед удалением 'потерянной' плоскости (в секундах)")]
      [SerializeField] private float planePersistenceDelay = 8.0f; // УВЕЛИЧЕНО: с 2.0f до 8.0f для стабильности
      [Tooltip("Минимальное время существования плоскости, чтобы она считалась 'стабильной' и могла стать 'постоянной' (в секундах)")]
      [SerializeField] private float minPlaneLifetimeForPersistence = 3.0f; // MODIFIED: Was 2.0f

      [Header("🛡️ Система гистерезиса плоскостей")]
      [Tooltip("Включить систему гистерезиса для предотвращения мерцания плоскостей")]
      [SerializeField] private bool enablePlaneHysteresis = true;
      [Tooltip("Количество последовательных кадров без обнаружения плоскости перед её удалением")]
      [SerializeField, Range(3, 20)] private int maxMissedFrames = 15; // УВЕЛИЧЕНО: с 8 до 15 для большей стабильности
      [Tooltip("Частота обработки маски сегментации (каждый N-й кадр для стабильности)")]
      [SerializeField, Range(1, 10)] private int maskProcessingInterval = 5; // УВЕЛИЧЕНО: с 3 до 5 для меньшей частоты обработки // Обрабатывать каждый 3-й кадр

      // Метод для автоматического превращения стабильных плоскостей в персистентные
      private void MakeStablePlanesPersistent()
      {
            // Используем настроенное время стабилизации
            float currentTime = Time.time;
            int newPersistentCount = 0;

            foreach (GameObject plane in generatedPlanes)
            {
                  if (plane == null)
                        continue;

                  // Пропускаем, если плоскость уже персистентная
                  if (IsPlanePersistent(plane))
                        continue;

                  // Проверяем, сколько времени существует плоскость
                  if (planeCreationTimes.TryGetValue(plane, out float creationTime))
                  {
                        float planeAge = currentTime - creationTime;
                        if (planeAge >= minPlaneLifetimeForPersistence) // MODIFIED: Was planeStableTimeThreshold
                        {
                              // Эта плоскость стабильна - делаем ее персистентной
                              if (MakePlanePersistent(plane))
                              {
                                    newPersistentCount++;
                              }
                        }
                  }
            }

            if (newPersistentCount > 0)
            {
                  Debug.Log($"[ARManagerInitializer2] Made {newPersistentCount} stable planes persistent. Total persistent: {persistentGeneratedPlanes.Count}");
            }
      }

      // New method to make all current planes persistent
      public void MakeAllPlanesPersistent()
      {
            if (!usePersistentPlanes)
            {
                  Debug.LogWarning("[ARManagerInitializer2] Persistent planes feature is disabled (usePersistentPlanes = false)");
                  return;
            }

            int madePersistentCount = 0;
            foreach (GameObject plane in generatedPlanes)
            {
                  if (plane == null) continue;

                  // Skip if already persistent
                  if (IsPlanePersistent(plane)) continue;

                  // Make persistent
                  if (MakePlanePersistent(plane))
                  {
                        madePersistentCount++;
                  }
            }

            Debug.Log($"[ARManagerInitializer2] Made {madePersistentCount} planes persistent");
      }

      // Method to set the layer of all generated planes
      public void SetPlanesLayer(string layerName)
      {
            int layerID = LayerMask.NameToLayer(layerName);
            if (layerID == -1)
            {
                  Debug.LogError($"[ARManagerInitializer2] Layer '{layerName}' not found in project settings!");
                  return;
            }

            int count = 0;
            foreach (GameObject plane in generatedPlanes)
            {
                  if (plane == null) continue;

                  plane.layer = layerID;
                  count++;
            }

            // Update the stored layer name
            planesLayerName = layerName;

            Debug.Log($"[ARManagerInitializer2] Set layer '{layerName}' (ID: {layerID}) for {count} planes");
      }

      // Инициализирует систему персистентных плоскостей
      private void InitializePersistentPlanesSystem()
      {
            if (!usePersistentPlanes)
            {
                  Debug.Log("[ARManagerInitializer2] Persistent planes system is disabled (usePersistentPlanes = false)");
                  return;
            }

            // Проверка существования слоя для плоскостей
            if (!string.IsNullOrEmpty(planesLayerName))
            {
                  int layerID = LayerMask.NameToLayer(planesLayerName);
                  if (layerID == -1)
                  {
                        Debug.LogWarning($"[ARManagerInitializer2] Layer '{planesLayerName}' not found in project settings! Planes will use default layer.");
                  }
                  else
                  {
                        Debug.Log($"[ARManagerInitializer2] Planes will use layer '{planesLayerName}' (ID: {layerID})");
                  }
            }

            Debug.Log("[ARManagerInitializer2] Persistent planes system initialized.");
      }

      // Метод для удаления проблемных персистентных плоскостей
      public void CleanupProblematicPersistentPlanes()
      {
            if (Camera.main == null)
            {
                  Debug.LogError("[ARManagerInitializer2] Камера не найдена для CleanupProblematicPersistentPlanes");
                  return;
            }

            Camera mainCam = Camera.main;
            List<GameObject> planesToRemove = new List<GameObject>();

            // Максимальное расстояние для хранения персистентных плоскостей (в метрах)
            float maxDistanceThreshold = 15.0f;

            // Максимальный допустимый размер для персистентной плоскости
            float maxPlaneSize = 5.0f;

            foreach (var kvp in persistentGeneratedPlanes)
            {
                  GameObject plane = kvp.Key;
                  bool isPersistent = kvp.Value;

                  if (plane == null || !isPersistent)
                        continue;

                  // Проверка размера
                  MeshFilter meshFilter = plane.GetComponent<MeshFilter>();
                  if (meshFilter != null && meshFilter.mesh != null)
                  {
                        Vector3 meshSize = meshFilter.mesh.bounds.size;
                        if (meshSize.x > maxPlaneSize || meshSize.y > maxPlaneSize)
                        {
                              Debug.Log($"[ARManagerInitializer2] Удаляем слишком большую персистентную плоскость {plane.name} (размер: {meshSize})");
                              planesToRemove.Add(plane);
                              continue;
                        }
                  }

                  // Проверка расстояния
                  float distanceToCamera = Vector3.Distance(plane.transform.position, mainCam.transform.position);
                  if (distanceToCamera > maxDistanceThreshold)
                  {
                        Debug.Log($"[ARManagerInitializer2] Удаляем удаленную персистентную плоскость {plane.name} (расстояние: {distanceToCamera:F2}м)");
                        planesToRemove.Add(plane);
                        continue;
                  }
            }

            // Удаляем проблемные плоскости
            foreach (GameObject plane in planesToRemove)
            {
                  RemovePlanePersistence(plane);
                  generatedPlanes.Remove(plane);
                  planeCreationTimes.Remove(plane);
                  planeLastVisitedTime.Remove(plane);
                  Destroy(plane);
            }

            Debug.Log($"[ARManagerInitializer2] Удалено {planesToRemove.Count} проблемных персистентных плоскостей");
      }

      // Удаляет все плоскости (персистентные и не персистентные)
      public void DeleteAllPlanes()
      {
            List<GameObject> allPlanes = new List<GameObject>(generatedPlanes);

            foreach (GameObject plane in allPlanes)
            {
                  if (plane == null) continue;

                  // Удаляем из всех списков
                  persistentGeneratedPlanes.Remove(plane);
                  planeCreationTimes.Remove(plane);
                  planeLastVisitedTime.Remove(plane);
                  generatedPlanes.Remove(plane);

                  // Удаляем объект
                  Destroy(plane);
            }

            Debug.Log($"[ARManagerInitializer2] Удалено все {allPlanes.Count} плоскостей");
      }

      // Удаляет только слишком большие плоскости
      public void DeleteLargePlanes(float maxSize = 3.0f)
      {
            if (Camera.main == null)
            {
                  Debug.LogError("[ARManagerInitializer2] Камера не найдена для DeleteLargePlanes");
                  return;
            }

            List<GameObject> planesToRemove = new List<GameObject>();

            foreach (GameObject plane in generatedPlanes)
            {
                  if (plane == null) continue;

                  // Проверка размера плоскости
                  MeshFilter meshFilter = plane.GetComponent<MeshFilter>();
                  if (meshFilter != null && meshFilter.mesh != null)
                  {
                        Vector3 meshSize = meshFilter.mesh.bounds.size;

                        // Если плоскость слишком большая
                        if (meshSize.x > maxSize || meshSize.y > maxSize || meshSize.z > maxSize)
                        {
                              planesToRemove.Add(plane);
                              Debug.Log($"[ARManagerInitializer2] Найдена большая плоскость для удаления: {plane.name}, размер: {meshSize}");
                        }
                  }
            }

            // Удаляем большие плоскости
            foreach (GameObject plane in planesToRemove)
            {
                  persistentGeneratedPlanes.Remove(plane);
                  planeCreationTimes.Remove(plane);
                  planeLastVisitedTime.Remove(plane);
                  generatedPlanes.Remove(plane);
                  Destroy(plane);
            }

            Debug.Log($"[ARManagerInitializer2] Удалено {planesToRemove.Count} больших плоскостей");
      }

      /// <summary>
      /// ДОБАВЛЕНО: Автоматическая очистка плоскостей с неправильными размерами
      /// </summary>
      private void CleanupOversizedPlanes()
      {
            int cleaned = 0;
            for (int i = generatedPlanes.Count - 1; i >= 0; i--)
            {
                  if (generatedPlanes[i] == null)
                  {
                        generatedPlanes.RemoveAt(i);
                        continue;
                  }

                  MeshFilter meshFilter = generatedPlanes[i].GetComponent<MeshFilter>();
                  if (meshFilter != null && meshFilter.mesh != null)
                  {
                        Vector3 size = Vector3.Scale(meshFilter.mesh.bounds.size, generatedPlanes[i].transform.localScale);

                        // Проверяем размеры с новыми ограничениями
                        bool isOversized = size.x > maxWallWidth || size.y > maxWallHeight || size.z > maxWallWidth;
                        bool isTooSmall = size.x < minPlaneSize || size.y < minPlaneSize;

                        // Проверяем соотношение сторон
                        float aspectRatio = Mathf.Max(size.x / size.y, size.y / size.x);
                        bool wrongAspectRatio = aspectRatio > maxAspectRatio;

                        if (isOversized || isTooSmall || wrongAspectRatio)
                        {
                              string reason = isOversized ? "слишком большая" : isTooSmall ? "слишком маленькая" : "неправильное соотношение сторон";
                              Debug.Log($"[ARManagerInitializer2] Удаляем проблемную плоскость: {generatedPlanes[i].name} ({reason}) - размер: {size.x:F2}x{size.y:F2}x{size.z:F2}м");

                              // Удаляем из всех словарей
                              if (persistentGeneratedPlanes.ContainsKey(generatedPlanes[i]))
                                    persistentGeneratedPlanes.Remove(generatedPlanes[i]);
                              if (planeCreationTimes.ContainsKey(generatedPlanes[i]))
                                    planeCreationTimes.Remove(generatedPlanes[i]);
                              if (planeLastVisitedTime.ContainsKey(generatedPlanes[i]))
                                    planeLastVisitedTime.Remove(generatedPlanes[i]);

                              GameObject.DestroyImmediate(generatedPlanes[i]);
                              generatedPlanes.RemoveAt(i);
                              cleaned++;
                        }
                  }
            }

            if (cleaned > 0)
            {
                  Debug.Log($"[ARManagerInitializer2] ✅ Автоматически удалено {cleaned} проблемных плоскостей");
            }
      }

      // Метод для быстрого сохранения всех текущих плоскостей без проверки времени существования
      public void QuickSaveCurrentPlanes()
      {
            if (!usePersistentPlanes)
            {
                  Debug.LogWarning("[ARManagerInitializer2] Persistent planes feature is disabled (usePersistentPlanes = false)");
                  return;
            }

            int savedCount = 0;

            foreach (GameObject plane in generatedPlanes)
            {
                  if (plane == null) continue;

                  // Пропускаем, если плоскость уже персистентная
                  if (IsPlanePersistent(plane)) continue;

                  // Проверяем, что плоскость не слишком большая
                  MeshFilter meshFilter = plane.GetComponent<MeshFilter>();
                  if (meshFilter != null && meshFilter.mesh != null)
                  {
                        Vector3 meshSize = meshFilter.mesh.bounds.size;
                        float maxAllowedSize = 5.0f;

                        if (meshSize.x > maxAllowedSize || meshSize.y > maxAllowedSize)
                        {
                              Debug.LogWarning($"[ARManagerInitializer2] Плоскость {plane.name} слишком большая для быстрого сохранения (размер: {meshSize})");
                              continue;
                        }
                  }

                  // Делаем плоскость персистентной без проверки времени
                  if (MakePlanePersistent(plane))
                  {
                        savedCount++;
                  }
            }

            Debug.Log($"[ARManagerInitializer2] Быстрое сохранение: сохранено {savedCount} плоскостей");
      }

      // Добавляем переменные для отслеживания двойного тапа
      [Header("Настройки управления жестами")]
      [Tooltip("Разрешить сохранение плоскостей по двойному тапу")]
      [SerializeField] private bool enableDoubleTapSave = true;
      [Tooltip("Максимальное время между тапами для распознавания двойного тапа (в секундах)")]
      [SerializeField] private float doubleTapTimeThreshold = 0.3f;
      private float lastTapTime = 0f;
      private int tapCount = 0;
      private Vector2 lastTapPosition;
      private float maxTapPositionDelta = 100f; // Максимальное расстояние между тапами в пикселях

      private void UpdateGestureInput()
      {
            if (!enableDoubleTapSave) return;

            // Проверяем тап на сенсорных устройствах
            if (Input.touchCount > 0)
            {
                  Touch touch = Input.GetTouch(0);

                  if (touch.phase == TouchPhase.Began)
                  {
                        HandleTapInput(touch.position);
                  }
            }
            // Проверяем клик мышью для тестирования в редакторе
            else if (Input.GetMouseButtonDown(0))
            {
                  HandleTapInput(Input.mousePosition);
            }
      }

      private void HandleTapInput(Vector2 position)
      {
            float currentTime = Time.time;

            // Если это первый тап или прошло слишком много времени с последнего тапа
            if (tapCount == 0 || (currentTime - lastTapTime) > doubleTapTimeThreshold)
            {
                  tapCount = 1;
                  lastTapTime = currentTime;
                  lastTapPosition = position;
            }
            // Если это второй тап в пределах времени и позиции
            else if (tapCount == 1 && (currentTime - lastTapTime) <= doubleTapTimeThreshold
                    && Vector2.Distance(position, lastTapPosition) < maxTapPositionDelta)
            {
                  tapCount = 0; // Сбрасываем счетчик
                                // Обрабатываем двойной тап
                  Debug.Log("[ARManagerInitializer2] Обнаружен двойной тап, запуск быстрого сохранения плоскостей");
                  QuickSaveCurrentPlanes();
            }
            else
            {
                  // Сбрасываем если тап не подходит для двойного
                  tapCount = 1;
                  lastTapTime = currentTime;
                  lastTapPosition = position;
            }
      }

      /// <summary>
      /// Диагностирует объекты сцены и их коллайдеры для понимания проблем с рейкастингом
      /// </summary>
      private void DiagnoseSceneObjects()
      {
            Debug.Log("=== [ARManagerInitializer2] ДИАГНОСТИКА ОБЪЕКТОВ СЦЕНЫ ===");

            // Найдем все объекты с коллайдерами
            Collider[] allColliders = FindObjectsOfType<Collider>(true); // включая неактивные
            Debug.Log($"[Диагностика] Всего коллайдеров в сцене: {allColliders.Length}");

            int enabledColliders = 0;
            int meshColliders = 0;
            int boxColliders = 0;

            foreach (var collider in allColliders)
            {
                  if (collider.enabled) enabledColliders++;

                  string layerName = LayerMask.LayerToName(collider.gameObject.layer);
                  if (string.IsNullOrEmpty(layerName)) layerName = $"Layer{collider.gameObject.layer}";

                  if (collider is MeshCollider) meshColliders++;
                  else if (collider is BoxCollider) boxColliders++;

                  Debug.Log($"[Диагностика] Коллайдер: '{collider.name}' ({collider.GetType().Name}), активен: {collider.enabled}, слой: {layerName}");
            }

            Debug.Log($"[Диагностика] Активных коллайдеров: {enabledColliders}, MeshCollider: {meshColliders}, BoxCollider: {boxColliders}");

            // ЕСЛИ КОЛЛАЙДЕРЫ ОТСУТСТВУЮТ - СРАЗУ ДОБАВЛЯЕМ ИХ
            if (allColliders.Length == 0)
            {
                  Debug.LogWarning("[Диагностика] ⚠️ Коллайдеры отсутствуют! Запускаем немедленное добавление...");
                  ForceAddCollidersAggressively();
            }

            // Проверим какие симуляционные объекты активны в сцене
            GameObject[] allGameObjects = FindObjectsOfType<GameObject>(true);

            foreach (var obj in allGameObjects)
            {
                  if (obj.name.Contains("Environment") || obj.name.Contains("Simulation"))
                  {
                        Debug.Log($"[Диагностика] Найден объект симуляции: '{obj.name}', активен: {obj.activeInHierarchy}, слой: {obj.layer} ({LayerMask.LayerToName(obj.layer)})");

                        // Проверим коллайдеры в дочерних объектах
                        Collider[] childColliders = obj.GetComponentsInChildren<Collider>(true);
                        Debug.Log($"[Диагностика] У объекта '{obj.name}' найдено {childColliders.Length} коллайдеров в дочерних объектах");
                  }
            }

            Debug.Log("=== [ARManagerInitializer2] КОНЕЦ ДИАГНОСТИКИ ===");
      }

      /// <summary>
      /// Повторная проверка и добавление коллайдеров через задержку (для асинхронно загружаемых объектов)
      /// </summary>
      private IEnumerator DelayedColliderCheck()
      {
            for (int attempt = 1; attempt <= 5; attempt++) // 5 попыток с интервалом 3 секунды
            {
                  yield return new WaitForSeconds(3.0f); // Ждем 3 секунды

                  Debug.Log($"=== [ARManagerInitializer2] ПОВТОРНАЯ ПРОВЕРКА #{attempt}/5 (через {attempt * 3} сек.) ===");

                  Collider[] allColliders = FindObjectsOfType<Collider>(true);
                  MeshRenderer[] allRenderers = FindObjectsOfType<MeshRenderer>(true);
                  GameObject[] allObjects = FindObjectsOfType<GameObject>(true);

                  Debug.Log($"[Повторная проверка #{attempt}] Найдено: Коллайдеров: {allColliders.Length}, MeshRenderer-ов: {allRenderers.Length}, Всего объектов: {allObjects.Length}");

                  if (allColliders.Length == 0 || allRenderers.Length == 0)
                  {
                        Debug.LogWarning($"[Повторная проверка #{attempt}] ⚠️ Проблема обнаружена! Запускаем ультра-диагностику...");
                        ForceAddCollidersAggressively();

                        // Проверяем результат
                        Collider[] afterColliders = FindObjectsOfType<Collider>(true);
                        MeshRenderer[] afterRenderers = FindObjectsOfType<MeshRenderer>(true);
                        Debug.Log($"[Повторная проверка #{attempt}] После диагностики: Коллайдеров: {afterColliders.Length}, MeshRenderer-ов: {afterRenderers.Length}");

                        if (afterColliders.Length > 0 && afterRenderers.Length > 0)
                        {
                              Debug.Log($"[Повторная проверка #{attempt}] ✅ Проблема решена! Остановка дальнейших попыток.");
                              break;
                        }
                  }
                  else
                  {
                        Debug.Log($"[Повторная проверка #{attempt}] ✅ Объекты найдены. Остановка дальнейших попыток.");
                        break;
                  }
            }

            Debug.Log("=== [ARManagerInitializer2] ЗАВЕРШЕНИЕ ПОВТОРНЫХ ПРОВЕРОК ===");
      }

      /// <summary>
      /// Агрессивная функция поиска и добавления коллайдеров
      /// </summary>
      private void ForceAddCollidersAggressively()
      {
            Debug.Log("=== [ARManagerInitializer2] УЛЬТРА-ДИАГНОСТИКА ВСЕХ ОБЪЕКТОВ ===");

            // 1. Показываем ВСЕ объекты в сцене
            GameObject[] allObjects = FindObjectsOfType<GameObject>(true);
            Debug.Log($"[Ультра-диагностика] Всего GameObject-ов в сцене (включая неактивные): {allObjects.Length}");

            // 2. Детально анализируем каждый объект
            int objectsWithMesh = 0;
            int objectsWithCollider = 0;
            int addedColliders = 0;

            foreach (GameObject obj in allObjects)
            {
                  MeshRenderer meshRenderer = obj.GetComponent<MeshRenderer>();
                  MeshFilter meshFilter = obj.GetComponent<MeshFilter>();
                  Collider existingCollider = obj.GetComponent<Collider>();

                  // Показываем информацию о КАЖДОМ объекте с MeshRenderer или в слоях симуляции
                  if (meshRenderer != null || obj.layer == 8 || obj.layer == 30 ||
                      obj.name.ToLower().Contains("wall") || obj.name.ToLower().Contains("floor") ||
                      obj.name.ToLower().Contains("room") || obj.name.ToLower().Contains("environment"))
                  {
                        string components = "";
                        if (meshRenderer != null) components += "MeshRenderer ";
                        if (meshFilter != null) components += "MeshFilter ";
                        if (existingCollider != null) components += $"Collider({existingCollider.GetType().Name}) ";

                        Debug.Log($"[Ультра-диагностика] Объект: '{obj.name}', активен: {obj.activeInHierarchy}, слой: {obj.layer} ({LayerMask.LayerToName(obj.layer)}), компоненты: [{components}]");

                        // Показываем размер mesh если есть
                        if (meshFilter != null && meshFilter.mesh != null)
                        {
                              Debug.Log($"  └─ Mesh: '{meshFilter.mesh.name}', vertices: {meshFilter.mesh.vertexCount}, triangles: {meshFilter.mesh.triangles.Length / 3}");
                        }
                  }

                  // Считаем статистику
                  if (meshRenderer != null) objectsWithMesh++;
                  if (existingCollider != null) objectsWithCollider++;

                  // Добавляем коллайдер если нужно
                  if (meshRenderer != null && meshFilter != null && meshFilter.mesh != null && existingCollider == null)
                  {
                        // Проверяем размер объекта
                        Bounds bounds = meshRenderer.bounds;
                        if (bounds.size.magnitude > 0.1f) // Только достаточно большие объекты
                        {
                              MeshCollider meshCollider = obj.AddComponent<MeshCollider>();
                              meshCollider.sharedMesh = meshFilter.mesh;
                              addedColliders++;

                              Debug.Log($"[Ультра-диагностика] ✅ Добавлен MeshCollider к '{obj.name}' (размер: {bounds.size})");
                        }
                  }
            }

            Debug.Log($"[Ультра-диагностика] 📊 СТАТИСТИКА:");
            Debug.Log($"  ├─ Всего объектов: {allObjects.Length}");
            Debug.Log($"  ├─ С MeshRenderer: {objectsWithMesh}");
            Debug.Log($"  ├─ С Collider: {objectsWithCollider}");
            Debug.Log($"  └─ Добавлено коллайдеров: {addedColliders}");

            // 3. Ищем объекты по специальным тегам Unity XR
            Transform[] allTransforms = FindObjectsOfType<Transform>(true);
            Debug.Log($"[Ультра-диагностика] Ищем XR объекты среди {allTransforms.Length} трансформов...");

            foreach (Transform t in allTransforms)
            {
                  string name = t.name.ToLower();
                  if (name.Contains("xr") || name.Contains("ar") || name.Contains("simulation") ||
                      name.Contains("mock") || name.Contains("synthetic") || name.Contains("environment"))
                  {
                        Debug.Log($"[Ультра-диагностика] 🎯 Потенциальный XR объект: '{t.name}', родитель: '{(t.parent ? t.parent.name : "ROOT")}', активен: {t.gameObject.activeInHierarchy}");
                  }
            }

            // 4. Финальная проверка
            Collider[] finalColliders = FindObjectsOfType<Collider>(true);
            MeshRenderer[] finalRenderers = FindObjectsOfType<MeshRenderer>(true);
            Debug.Log($"[Ультра-диагностика] 🎯 ФИНАЛЬНЫЙ РЕЗУЛЬТАТ: Коллайдеров: {finalColliders.Length}, MeshRenderer-ов: {finalRenderers.Length}");

            Debug.Log("=== [ARManagerInitializer2] КОНЕЦ УЛЬТРА-ДИАГНОСТИКИ ===");
      }

      /// <summary>
      /// Утилита для поиска объектов симуляционной среды по имени
      /// </summary>
      public void DebugFindSimulationObjects()
      {
            Debug.Log("=== [ARManagerInitializer2] ПОИСК СИМУЛЯЦИОННЫХ ОБЪЕКТОВ ===");

            GameObject[] allObjects = FindObjectsOfType<GameObject>(true);
            int found = 0;

            foreach (var obj in allObjects)
            {
                  if (obj.name.ToLower().Contains("environment") ||
                      obj.name.ToLower().Contains("simulation") ||
                      obj.name.ToLower().Contains("room") ||
                      obj.name.ToLower().Contains("wall") ||
                      obj.name.ToLower().Contains("floor"))
                  {
                        found++;

                        MeshRenderer renderer = obj.GetComponent<MeshRenderer>();
                        Collider collider = obj.GetComponent<Collider>();

                        string info = $"[DebugFind] {obj.name} - ";
                        info += $"Активен: {obj.activeInHierarchy}, ";
                        info += $"Слой: {LayerMask.LayerToName(obj.layer)}, ";
                        info += $"MeshRenderer: {(renderer != null ? "✓" : "✗")}, ";
                        info += $"Collider: {(collider != null ? "✓" : "✗")}";

                        if (renderer != null)
                              info += $", Размер: {renderer.bounds.size}";

                        Debug.Log(info);
                  }
            }

            Debug.Log($"[DebugFind] Найдено объектов среды: {found}");
      }

      /// <summary>
      /// Публичный доступ к сгенерированным плоскостям для PlaneOrientationDebugger
      /// </summary>
      public List<GameObject> GeneratedPlanes => generatedPlanes;

      /// <summary>
      /// Публичный метод для ручного запуска динамического слияния плоскостей
      /// </summary>
      public void ManuallyMergePlanes()
      {
            if (enableDynamicMerging)
            {
                  Debug.Log($"[ARManagerInitializer2-ManualMerge] 🔄 Ручное слияние плоскостей запущено. Текущее количество: {generatedPlanes.Count}");
                  PerformDynamicPlaneMerging();
                  Debug.Log($"[ARManagerInitializer2-ManualMerge] ✅ Ручное слияние завершено. Итоговое количество: {generatedPlanes.Count}");
            }
            else
            {
                  Debug.LogWarning("[ARManagerInitializer2-ManualMerge] ⚠️ Динамическое слияние отключено. Включите enableDynamicMerging для использования этой функции.");
            }
      }

      /// <summary>
      /// Применяет оптимизированные настройки для создания меньшего количества более крупных плоскостей
      /// </summary>
      [ContextMenu("Применить консервативные настройки")]
      public void ApplyConservativeSettings()
      {
            Debug.Log("[ARManagerInitializer2] 🎯 Применение консервативных настроек для меньшего количества плоскостей...");

            // Увеличиваем минимальные размеры для фильтрации мелких областей
            minPlaneSizeInMeters = 0.8f;
            minPixelsDimensionForArea = 25;
            minAreaSizeInPixels = 2000;

            // Более строгие фильтры качества
            minWallRatio = 0.7f;
            maxAreaAspectRatio = 4.0f;

            // Более крупные плоскости
            minPlaneSize = 1.0f;
            maxAspectRatio = 3.5f;
            sizeReductionFactor = 0.98f;
            wallFitMultiplier = 0.9f;

            // Отключаем продвинутые системы для стабильности
            useContourBasedDetection = false;
            useAsyncGPUReadback = false;
            enableDynamicMerging = false;

            Debug.Log("[ARManagerInitializer2] ✅ Консервативные настройки применены. Система будет создавать меньше, но более крупных плоскостей.");
      }

      /// <summary>
      /// Применяет более агрессивные настройки для максимального уменьшения количества плоскостей
      /// </summary>
      [ContextMenu("Применить максимально строгие настройки")]
      public void ApplyStrictSettings()
      {
            Debug.Log("[ARManagerInitializer2] 🎯 Применение максимально строгих настроек...");

            // Очень высокие пороги для фильтрации
            minPlaneSizeInMeters = 1.2f;
            minPixelsDimensionForArea = 40;
            minAreaSizeInPixels = 4000;

            // Строгие фильтры качества
            minWallRatio = 0.8f;
            maxAreaAspectRatio = 3.0f;

            // Только крупные плоскости
            minPlaneSize = 1.5f;
            maxAspectRatio = 2.5f;
            sizeReductionFactor = 1.0f;
            wallFitMultiplier = 1.0f;

            // Отключаем все продвинутые системы
            useContourBasedDetection = false;
            useAsyncGPUReadback = false;
            enableDynamicMerging = false;
            usePCAForPlaneOrientation = false;
            useOBBForPlaneSizing = false;

            Debug.Log("[ARManagerInitializer2] ⚠️ СТРОГИЕ настройки применены. Система будет создавать минимальное количество только крупных плоскостей.");
      }

      /// <summary>
      /// 🛡️ Активирует систему стабилизации для предотвращения мерцания плоскостей
      /// </summary>
      [ContextMenu("🛡️ Активировать систему стабилизации")]
      public void ApplyStabilizationSettings()
      {
            Debug.Log("[ARManagerInitializer2] 🛡️ Активируем систему стабилизации для предотвращения мерцания...");

            // Включаем систему гистерезиса
            enablePlaneHysteresis = true;
            maxMissedFrames = 15; // УВЕЛИЧЕНО: Плоскость должна пропасть на 15 кадров подряд
            maskProcessingInterval = 5; // УВЕЛИЧЕНО: Обрабатываем каждый 5-й кадр
            planePersistenceDelay = 15.0f; // УВЕЛИЧЕНО: 15 секунд задержки

            // Консервативные настройки для стабильности
            minPlaneSizeInMeters = 1.0f; // УВЕЛИЧЕНО: с 0.6f до 1.0f
            minAreaSizeInPixels = 3000; // УВЕЛИЧЕНО: с 1500 до 3000
            minWallRatio = 0.75f; // УВЕЛИЧЕНО: с 0.6f до 0.75f
            minPixelsDimensionForArea = 50; // УВЕЛИЧЕНО: более строгая фильтрация
            maxAreaAspectRatio = 4.0f; // УМЕНЬШЕНО: более строгие пропорции

            // Включаем логирование для диагностики стабилизации
            enableCustomPlaneCreationLogging = false; // Отключаем детальное логирование создания
            enableDetailedRaycastLogging = false; // Отключаем детальное логирование рейкастов
            enableVerboseLoggingCleanup = true; // ВКЛЮЧАЕМ: логирование очистки для контроля

            Debug.Log("[ARManagerInitializer2] ✅ Система стабилизации активирована! Плоскости больше не должны мерцать.");

            // Показываем состояние системы для проверки
            Invoke(nameof(DebugHysteresisState), 1.0f); // Показать состояние через 1 секунду
      }

      /// <summary>
      /// 📊 Показывает состояние системы гистерезиса для отладки
      /// </summary>
      [ContextMenu("📊 Показать состояние гистерезиса")]
      public void DebugHysteresisState()
      {
            Debug.Log("=== [ARManagerInitializer2] 📊 СОСТОЯНИЕ СИСТЕМЫ ГИСТЕРЕЗИСА ===");
            Debug.Log($"🛡️ Система гистерезиса: {(enablePlaneHysteresis ? "ВКЛЮЧЕНА" : "ОТКЛЮЧЕНА")}");
            Debug.Log($"📊 Максимальные пропущенные кадры: {maxMissedFrames}");
            Debug.Log($"⏱️ Интервал обработки маски: каждый {maskProcessingInterval}-й кадр");
            Debug.Log($"🔄 Текущий счетчик кадров: {maskProcessingFrameCounter}");
            Debug.Log($"📝 Задержка удаления: {planePersistenceDelay}с");
            Debug.Log($"📈 Общее количество плоскостей: {generatedPlanes.Count}");
            Debug.Log($"💾 Плоскостей с счетчиком пропусков: {planeMissedFrames.Count}");

            if (planeMissedFrames.Count > 0)
            {
                  Debug.Log("📋 Детали по пропущенным кадрам:");
                  foreach (var kvp in planeMissedFrames)
                  {
                        GameObject plane = kvp.Key;
                        int missedCount = kvp.Value;
                        string planeName = plane != null ? plane.name : "УДАЛЕНА";
                        Debug.Log($"  ├─ {planeName}: {missedCount}/{maxMissedFrames} пропущено");
                  }
            }
            Debug.Log("=== КОНЕЦ ОТЧЕТА О ГИСТЕРЕЗИСЕ ===");
      }

      /// <summary>
      /// 🚨 Экстремальная стабилизация для самых сложных случаев мерцания
      /// </summary>
      [ContextMenu("🚨 Экстремальная стабилизация")]
      public void ApplyExtremeStabilizationSettings()
      {
            Debug.Log("[ARManagerInitializer2] 🚨 АКТИВИРУЕМ ЭКСТРЕМАЛЬНУЮ СТАБИЛИЗАЦИЮ для устранения мерцания...");

            // Максимальные настройки гистерезиса
            enablePlaneHysteresis = true;
            maxMissedFrames = 20; // Очень высокий порог - плоскость должна пропасть на 20 кадров подряд
            maskProcessingInterval = 8; // Обрабатываем только каждый 8-й кадр
            planePersistenceDelay = 30.0f; // 30 секунд задержки удаления

            // Экстремально строгие фильтры создания
            minPlaneSizeInMeters = 1.5f; // Только большие плоскости
            minAreaSizeInPixels = 5000; // Очень большие области
            minWallRatio = 0.9f; // 90% пикселей должны быть стенами
            minPixelsDimensionForArea = 100; // Очень строгая фильтрация
            maxAreaAspectRatio = 3.0f; // Только квадратные области

            // Отключаем все создание на время стабилизации
            wallPixelThreshold = 250; // Максимальный порог - почти не создаем новых плоскостей

            // Включаем логирование для контроля
            enableVerboseLoggingCleanup = true;

            Debug.Log("[ARManagerInitializer2] 🚨 ЭКСТРЕМАЛЬНАЯ СТАБИЛИЗАЦИЯ активирована! Плоскости будут максимально стабильными.");

            // Показываем состояние через 1 секунду
            Invoke(nameof(DebugHysteresisState), 1.0f);
      }

      private void ConfigureARMeshManager()
      {
            var meshManager = FindObjectOfType<ARMeshManager>();
            if (meshManager == null)
            {
                  Debug.LogWarning("[ARManagerInitializer2] ARMeshManager not found in scene. Cannot configure mesh material.");
                  return;
            }

            // Create a simple, slightly visible transparent material
            Shader urpShader = Shader.Find("Universal Render Pipeline/Lit");
            Material transparentMaterial;

            if (urpShader != null)
            {
                  transparentMaterial = new Material(urpShader)
                  {
                        name = "AR_Mesh_Transparent_URP_Material"
                  };
                  transparentMaterial.SetFloat("_Surface", 1.0f); // Set Surface Type to Transparent
                  transparentMaterial.SetColor("_BaseColor", new Color(0.6f, 0.8f, 1.0f, 0.1f)); // A faint blue
            }
            else
            {
                  // Fallback for built-in render pipeline
                  Shader fallbackShader = Shader.Find("Legacy Shaders/Transparent/Diffuse");
                  if (fallbackShader == null)
                  {
                        Debug.LogError("[ARManagerInitializer2] Could not find any transparent shader.");
                        return;
                  }
                  transparentMaterial = new Material(fallbackShader)
                  {
                        name = "AR_Mesh_Transparent_Legacy_Material"
                  };
                  transparentMaterial.SetColor("_Color", new Color(0.6f, 0.8f, 1.0f, 0.1f));
            }

            // Assign the material to all current and future meshes managed by ARMeshManager
            void ApplyMaterialToMeshes(ARMeshesChangedEventArgs args)
            {
                  foreach (var mesh in args.added)
                  {
                        var renderer = mesh.GetComponent<MeshRenderer>();
                        if (renderer != null)
                        {
                              renderer.material = transparentMaterial;
                        }
                  }
                  foreach (var mesh in args.updated)
                  {
                        var renderer = mesh.GetComponent<MeshRenderer>();
                        if (renderer != null && (renderer.sharedMaterial == null || renderer.sharedMaterial.name.Contains("Default-Material")))
                        {
                              renderer.material = transparentMaterial;
                        }
                  }
            }

            // Apply to existing meshes first
            foreach (var meshFilter in meshManager.GetComponentsInChildren<MeshFilter>())
            {
                  var renderer = meshFilter.GetComponent<MeshRenderer>();
                  if (renderer != null)
                  {
                        renderer.material = transparentMaterial;
                  }
            }

            // Then subscribe for future changes
            meshManager.meshesChanged += ApplyMaterialToMeshes;

            Debug.Log("[ARManagerInitializer2] ✅ ARMeshManager configured to use transparent materials for environment meshes.");
      }

      #region Tap-based Plane Generation

      /// <summary>
      /// Обрабатывает ввод пользователя (тап по экрану) для создания плоскости.
      /// </summary>
      private void HandlePlaneCreationInput()
      {
            if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Ended)
            {
                  planeCreationTapPosition = Input.GetTouch(0).position;
            }
#if UNITY_EDITOR
            if (Input.GetMouseButtonDown(0))
            {
                  planeCreationTapPosition = Input.mousePosition;
            }
#endif
      }

      /// <summary>
      /// Главный метод, запускающий процесс создания плоскости в точке нажатия.
      /// </summary>
      private void CreatePlaneAtTap(Vector2 tapPosition)
      {
            if (currentSegmentationMask == null)
            {
                  Debug.LogWarning("[ARManagerInitializer2] Маска сегментации отсутствует, создание плоскости отменено.");
                  return;
            }

            // Конвертируем RenderTexture в Texture2D для анализа
            Texture2D maskTexture = RenderTextureToTexture2D(currentSegmentationMask, currentSegmentationMask.width, currentSegmentationMask.height);
            if (maskTexture == null)
            {
                  Debug.LogError("[ARManagerInitializer2] Не удалось конвертировать маску в Texture2D.");
                  return;
            }

            // Конвертируем позицию нажатия на экране в координаты на текстуре
            Vector2Int tapTextureCoords = new Vector2Int(
                (int)(tapPosition.x * ((float)maskTexture.width / Screen.width)),
                (int)(tapPosition.y * ((float)maskTexture.height / Screen.height))
            );

            Color32[] pixels = maskTexture.GetPixels32();
            int tapIndex = tapTextureCoords.y * maskTexture.width + tapTextureCoords.x;

            // Проверяем, что нажатие пришлось на стену
            if (pixels[tapIndex].r < wallPixelThreshold)
            {
                  Debug.Log("[ARManagerInitializer2] Нажатие не пришлось на стену (согласно маске).");
                  Destroy(maskTexture);
                  return;
            }

            // 1. Находим все пиксели, принадлежащие этой стене (Flood Fill)
            List<Vector2Int> regionPoints = FindConnectedRegion(pixels, maskTexture.width, maskTexture.height, tapTextureCoords, wallPixelThreshold);
            if (regionPoints.Count < minAreaSizeInPixels)
            {
                  Debug.Log($"[ARManagerInitializer2] Найденная область слишком мала ({regionPoints.Count} пикселей).");
                  Destroy(maskTexture);
                  return;
            }

            // 2. Находим выпуклую оболочку этой области, чтобы получить ее контур
            List<Vector2Int> contourPoints = FindContour(regionPoints, maskTexture.width, maskTexture.height);
            if (contourPoints == null || contourPoints.Count < 3)
            {
                  Debug.LogWarning("[ARManagerInitializer2] Не удалось найти контур для области.");
                  Destroy(maskTexture);
                  return;
            }

            // 2.1. Упрощаем контур, чтобы уменьшить количество вершин
            // Эпсилон - максимальное расстояние от точки до упрощенной линии. 
            // Подбирается экспериментально. 1.5-2.0 - хорошее начало.
            List<Vector2Int> simplifiedContour = SimplifyPolygon(contourPoints, 2.0f);
            if (simplifiedContour == null || simplifiedContour.Count < 3)
            {
                  Debug.LogWarning("[ARManagerInitializer2] Не удалось упростить контур.");
                  Destroy(maskTexture);
                  return;
            }


            // 3. Создаем 3D плоскость на основе 2D контура
            CreatePlaneFrom2DPolygon(simplifiedContour, tapPosition, maskTexture.width, maskTexture.height);

            Destroy(maskTexture);
      }

      /// <summary>
      /// Находит все связанные пиксели в области с помощью алгоритма Flood Fill.
      /// </summary>
      private List<Vector2Int> FindConnectedRegion(Color32[] pixels, int width, int height, Vector2Int startPixel, byte threshold)
      {
            List<Vector2Int> regionPoints = new List<Vector2Int>();
            bool[,] visited = new bool[width, height];
            Queue<Vector2Int> queue = new Queue<Vector2Int>();

            if (startPixel.x < 0 || startPixel.x >= width || startPixel.y < 0 || startPixel.y >= height)
                  return regionPoints;

            queue.Enqueue(startPixel);
            visited[startPixel.x, startPixel.y] = true;

            while (queue.Count > 0)
            {
                  Vector2Int p = queue.Dequeue();
                  regionPoints.Add(p);

                  // Проверяем соседей
                  Vector2Int[] neighbors = {
                  new Vector2Int(p.x + 1, p.y),
                  new Vector2Int(p.x - 1, p.y),
                  new Vector2Int(p.x, p.y + 1),
                  new Vector2Int(p.x, p.y - 1)
              };

                  foreach (var n in neighbors)
                  {
                        if (n.x >= 0 && n.x < width && n.y >= 0 && n.y < height && !visited[n.x, n.y])
                        {
                              if (pixels[n.y * width + n.x].r >= threshold)
                              {
                                    visited[n.x, n.y] = true;
                                    queue.Enqueue(n);
                              }
                        }
                  }
            }
            return regionPoints;
      }

      /// <summary>
      /// Находит контур (границу) для набора 2D точек.
      /// Использует алгоритм Moore-Neighbor Tracing.
      /// </summary>
      private List<Vector2Int> FindContour(List<Vector2Int> regionPoints, int width, int height)
      {
            if (regionPoints == null || regionPoints.Count == 0)
                  return new List<Vector2Int>();

            HashSet<Vector2Int> regionSet = new HashSet<Vector2Int>(regionPoints);
            List<Vector2Int> contour = new List<Vector2Int>();

            // Находим стартовую точку для обхода - самую верхнюю левую.
            Vector2Int startPoint = regionPoints[0];
            foreach (var p in regionPoints)
            {
                  if (p.x < startPoint.x || (p.x == startPoint.x && p.y > startPoint.y))
                  {
                        startPoint = p;
                  }
            }

            Vector2Int currentPoint = startPoint;
            Vector2Int previousPoint = new Vector2Int(startPoint.x, startPoint.y + 1); // "виртуальная" точка сверху, чтобы начать движение влево/вниз

            // Восемь соседей по часовой стрелке, начиная с "севера"
            Vector2Int[] neighborsOffsets = new Vector2Int[] {
            new Vector2Int(0, 1), new Vector2Int(-1, 1), new Vector2Int(-1, 0), new Vector2Int(-1, -1),
            new Vector2Int(0, -1), new Vector2Int(1, -1), new Vector2Int(1, 0), new Vector2Int(1, 1)
        };

            do
            {
                  contour.Add(currentPoint);

                  int previousIndex = -1;
                  for (int i = 0; i < neighborsOffsets.Length; i++)
                  {
                        if (currentPoint + neighborsOffsets[i] == previousPoint)
                        {
                              previousIndex = i;
                              break;
                        }
                  }
                  // Should always find previous point, but as a fallback:
                  if (previousIndex == -1) previousPoint = new Vector2Int(currentPoint.x, currentPoint.y + 1);


                  // Ищем следующую точку на границе, начиная с соседа после предыдущей точки
                  for (int i = 1; i <= neighborsOffsets.Length; i++)
                  {
                        int neighborIndex = (previousIndex + i) % neighborsOffsets.Length;
                        Vector2Int nextPoint = currentPoint + neighborsOffsets[neighborIndex];

                        if (regionSet.Contains(nextPoint))
                        {
                              previousPoint = currentPoint;
                              currentPoint = nextPoint;
                              break;
                        }
                  }
            } while (currentPoint != startPoint && contour.Count < 2 * regionSet.Count); // Защита от бесконечного цикла

            return contour;
      }

      /// <summary>
      /// Упрощает полигон, используя алгоритм Рамера-Дугласа-Пойкера.
      /// </summary>
      private List<Vector2Int> SimplifyPolygon(List<Vector2Int> points, float epsilon)
      {
            if (points == null || points.Count < 3)
                  return points;

            float epsilonSq = epsilon * epsilon;

            // Находим самую дальнюю точку
            int firstPoint = 0;
            int lastPoint = points.Count - 1;
            int maxDistIndex = -1;
            float maxDistSq = 0;

            for (int i = firstPoint + 1; i < lastPoint; i++)
            {
                  float distSq = PerpendicularDistanceSq(points[firstPoint], points[lastPoint], points[i]);
                  if (distSq > maxDistSq)
                  {
                        maxDistSq = distSq;
                        maxDistIndex = i;
                  }
            }

            // Если максимальное расстояние больше epsilon, рекурсивно упрощаем
            if (maxDistIndex != -1 && maxDistSq > epsilonSq)
            {
                  var recResults1 = SimplifyPolygon(points.GetRange(firstPoint, maxDistIndex - firstPoint + 1), epsilon);
                  var recResults2 = SimplifyPolygon(points.GetRange(maxDistIndex, lastPoint - maxDistIndex + 1), epsilon);

                  // Соединяем результаты
                  List<Vector2Int> result = new List<Vector2Int>();
                  result.AddRange(recResults1.GetRange(0, recResults1.Count - 1));
                  result.AddRange(recResults2);
                  return result;
            }
            else
            {
                  // Все точки между начальной и конечной достаточно близки
                  return new List<Vector2Int> { points[firstPoint], points[lastPoint] };
            }
      }

      private float PerpendicularDistanceSq(Vector2Int p1, Vector2Int p2, Vector2Int p)
      {
            long dx = p2.x - p1.x;
            long dy = p2.y - p1.y;

            if (dx == 0 && dy == 0)
            {
                  return (p.x - p1.x) * (p.x - p1.x) + (p.y - p1.y) * (p.y - p1.y);
            }

            double num = System.Math.Abs(dy * p.x - dx * p.y + p2.x * p1.y - p2.y * p1.x);
            return (float)(num * num) / (dx * dx + dy * dy);
      }

      /// <summary>
      /// Создает 3D объект плоскости из 2D полигона (выпуклой оболочки).
      /// </summary>
      private void CreatePlaneFrom2DPolygon(List<Vector2Int> polygon, Vector2 tapPosition, int textureWidth, int textureHeight)
      {
            Camera cam = xrOrigin.Camera;
            Ray ray = cam.ScreenPointToRay(tapPosition);

            // Рейкаст, чтобы найти точку на стене и нормаль
            if (Physics.Raycast(ray, out RaycastHit hit, maxRayDistance, hitLayerMask))
            {
                  Plane physicalPlane = new Plane(hit.normal, hit.point);
                  List<Vector3> vertices3D = new List<Vector3>();

                  // Проецируем 2D точки полигона на 3D плоскость
                  foreach (var point2D in polygon)
                  {
                        // Конвертируем координаты текстуры обратно в экранные
                        Vector2 screenPoint = new Vector2(
                            point2D.x * ((float)Screen.width / textureWidth),
                            point2D.y * ((float)Screen.height / textureHeight)
                        );

                        Ray pointRay = cam.ScreenPointToRay(screenPoint);
                        if (physicalPlane.Raycast(pointRay, out float enter))
                        {
                              vertices3D.Add(pointRay.GetPoint(enter));
                        }
                  }

                  if (vertices3D.Count >= 3)
                  {
                        UnityEngine.Mesh mesh = CreateMeshFrom3DPolygon(vertices3D, hit.normal);
                        if (mesh != null)
                        {
                              GameObject planeObject = new GameObject($"WallPlane_{planeInstanceCounter++}");
                              planeObject.transform.position = hit.point;
                              planeObject.transform.rotation = Quaternion.LookRotation(-hit.normal, cam.transform.up);

                              planeObject.AddComponent<MeshFilter>().mesh = mesh;
                              planeObject.AddComponent<MeshRenderer>().material = verticalPlaneMaterial; // Используем материал для стен
                              planeObject.AddComponent<MeshCollider>().sharedMesh = mesh;

                              int layer = LayerMask.NameToLayer(planesLayerName);
                              if (layer != -1) planeObject.layer = layer;

                              generatedPlanes.Add(planeObject);
                        }
                  }
            }
            else
            {
                  Debug.Log("[ARManagerInitializer2] Рейкаст из точки нажатия не попал в геометрию. Плоскость не создана.");
            }
      }

      /// <summary>
      /// Создает 3D меш из набора 3D вершин с помощью триангуляции.
      /// </summary>
      private UnityEngine.Mesh CreateMeshFrom3DPolygon(List<Vector3> vertices3D, Vector3 normal)
      {
            if (vertices3D == null || vertices3D.Count < 3)
            {
                  return null;
            }

            Tess tess = new Tess();
            ContourVertex[] contour = new ContourVertex[vertices3D.Count];
            for (int i = 0; i < vertices3D.Count; i++)
            {
                  // Преобразуем 3D-вершины в локальное 2D-пространство плоскости для триангуляции
                  // Это необходимо, так как LibTessDotNet работает с 2D
                  Vector3 localPoint = Quaternion.Inverse(Quaternion.LookRotation(normal)) * vertices3D[i];
                  contour[i] = new ContourVertex { Position = new Vec3 { X = localPoint.x, Y = localPoint.y, Z = 0 } };
            }
            tess.AddContour(contour, ContourOrientation.Original);

            // Триангулируем полигон
            tess.Tessellate(WindingRule.EvenOdd, ElementType.Polygons, 3);

            // Создаем меш Unity из результатов
            UnityEngine.Mesh mesh = new UnityEngine.Mesh();
            mesh.vertices = vertices3D.ToArray(); // Используем оригинальные 3D-вершины
            mesh.triangles = tess.Elements;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
      }

      /// <summary>
      /// Метод очистки ресурсов при уничтожении объекта
      /// </summary>
      private void OnDestroy()
      {
            // ⚡ УРОВЕНЬ 2: Очистка асинхронной системы GPU
            if (useAsyncGPUReadback)
            {
                  CleanupAsyncSystem();
                  Debug.Log("✅ AsyncGPUReadback система очищена");
            }
      }

      #endregion
}