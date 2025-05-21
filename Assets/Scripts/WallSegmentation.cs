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

    [Header("Настройки сегментации")]
    [Tooltip("Индекс класса стены в модели")][SerializeField] private int wallClassIndex = 0;     // Стена
    [Tooltip("Индекс класса пола в модели")][SerializeField] private int floorClassIndex = -1; // Пол (если есть, иначе -1)
    [Tooltip("Порог вероятности для определения стены")][SerializeField, Range(0.0001f, 1.0f)] private float wallConfidence = 0.5f; // Изменено минимальное значение с 0.01f на 0.0001f
    [Tooltip("Порог вероятности для определения пола")][SerializeField, Range(0.01f, 1.0f)] private float floorConfidence = 0.5f;
    [Tooltip("Обнаруживать также горизонтальные поверхности (пол)")] public bool detectFloor = false;

    [Tooltip("Разрешение входного изображения")]
    public Vector2Int inputResolution = new Vector2Int(320, 320);

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
    public float segmentationConfidenceThreshold = 0.01f; // Временно очень низкий порог для теста

    [Tooltip("Порог вероятности для определения пола")]
    public float floorConfidenceThreshold = 0.5f;    // Или другое значение по умолчанию

    [Tooltip("Путь к файлу модели (.sentis или .onnx) в StreamingAssets")] public string modelPath = "";

    [Tooltip("Предпочитаемый бэкенд для исполнения модели (0 = CPU, 1 = GPUCompute)")]
    public int selectedBackend = 0; // 0 = CPU, 1 = GPUCompute (через BackendType)

    [Header("Настройки материалов и отладки маски")] // Новый заголовок для инспектора
    [SerializeField]
    [Tooltip("Материал, используемый для преобразования выхода модели в маску сегментации.")]
    private Material segmentationMaterial; // Добавлено поле

    [SerializeField]
    [Tooltip("Путь для сохранения отладочных изображений маски (относительно Assets). Оставьте пустым, чтобы не сохранять.")]
    private string debugMaskSavePath = "DebugMasks"; // Добавлено поле с значением по умолчанию

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
    }
    [Tooltip("Флаги для детальной отладки различных частей системы")]
    public DebugFlags debugFlags = DebugFlags.None;

    [Tooltip("Сохранять отладочные маски в указанный путь")] // Добавлено
    public bool saveDebugMask = false; // Добавлено

    private bool isProcessing = false; // Флаг, показывающий, что идет обработка сегментации

    [Tooltip("Путь для сохранения отладочных изображений")]
    public string debugSavePath = "SegmentationMasks";

    [Tooltip("Использовать симуляцию сегментации, если не удалось получить изображение с камеры")]
    public bool useSimulationIfCameraFails = true;

    [Tooltip("Счетчик неудачных попыток получения изображения перед активацией симуляции")]
    public int maxConsecutiveFailsBeforeSimulation = 5;

    private Worker engine;
    private Model runtimeModel;
    private Worker worker;
    private Texture2D cameraTexture;
    private bool isModelInitialized = false;
    private bool isInitializing = false;
    private string lastErrorMessage = null;
    private bool isInitializationFailed = false;
    private int consecutiveFailCount = 0;
    private bool usingSimulatedSegmentation = false;

    [System.NonSerialized]
    private static int debugMaskCounter = 0; // Помечаем атрибутом, чтобы показать, что мы знаем о неиспользовании

    // События для уведомления других компонентов
    public delegate void ModelInitializedHandler();
    public event ModelInitializedHandler OnModelInitialized;

    // Событие, вызываемое при обновлении маски сегментации
    public delegate void SegmentationMaskUpdatedHandler(RenderTexture mask);
    public event SegmentationMaskUpdatedHandler OnSegmentationMaskUpdated;

    // Публичные свойства для проверки состояния
    public bool IsModelInitialized => isModelInitialized;
    public bool IsInitializing => isInitializing;
    public string LastErrorMessage => lastErrorMessage;
    public bool IsInitializationFailed => isInitializationFailed;

    // 1. Добавляем объявление переменной model
    private Model model;

    // Добавляем приватное поле для ARCameraManager
    private ARCameraManager arCameraManager;

    // Поля для стабилизации маски сегментации
    private RenderTexture lastSuccessfulMask;
    private bool hasValidMask = false;
    private float lastValidMaskTime = 0f;
    private int stableFrameCount = 0;
    private const int REQUIRED_STABLE_FRAMES = 2; // Уменьшено с 3 до 2 для более быстрой реакции

    // Параметры сглаживания маски для улучшения визуального качества
    [Header("Настройки качества маски")]
    [Tooltip("Применять сглаживание к маске сегментации")]
    public bool applyMaskSmoothing = true;
    [Tooltip("Значение размытия для сглаживания маски (в пикселях)")]
    [Range(1, 10)]
    public int maskBlurSize = 3;
    [Tooltip("Повышать резкость краев на маске")]
    public bool enhanceEdges = true;
    [Tooltip("Повышать контраст маски")]
    public bool enhanceContrast = true;
    [Tooltip("Множитель контраста")]
    [Range(1f, 3f)]
    public float contrastMultiplier = 1.5f;

    // Добавляем оптимизированный пул текстур для уменьшения аллокаций памяти
    private class TexturePool
    {
        private Dictionary<Vector2Int, List<RenderTexture>> availableTextures = new Dictionary<Vector2Int, List<RenderTexture>>();
        private Dictionary<Vector2Int, List<RenderTexture>> inUseTextures = new Dictionary<Vector2Int, List<RenderTexture>>();
        private Dictionary<int, Vector2Int> textureToSize = new Dictionary<int, Vector2Int>();
        private RenderTextureFormat defaultFormat;

        // Добавляем конструктор, принимающий формат
        public TexturePool(RenderTextureFormat format = RenderTextureFormat.ARGB32)
        {
            defaultFormat = format;
        }

        // Получить текстуру из пула или создать новую
        public RenderTexture GetTexture(int width, int height, RenderTextureFormat format = RenderTextureFormat.ARGB32)
        {
            // Используем переданный формат или значение по умолчанию из конструктора
            RenderTextureFormat textureFormat = format != RenderTextureFormat.ARGB32 ? format : defaultFormat;

            Vector2Int size = new Vector2Int(width, height);

            // Проверяем, есть ли доступные текстуры такого размера
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

            // Создаем новую текстуру если нет доступных
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

        // Вернуть текстуру в пул
        public void ReleaseTexture(RenderTexture texture)
        {
            if (texture == null) return;

            int id = texture.GetInstanceID();

            if (!textureToSize.ContainsKey(id))
            {
                // Это не наша текстура, просто уничтожаем
                RenderTexture.ReleaseTemporary(texture);
                return;
            }

            Vector2Int size = textureToSize[id];

            if (inUseTextures.ContainsKey(size))
            {
                inUseTextures[size].Remove(texture);
            }

            if (!availableTextures.ContainsKey(size))
            {
                availableTextures[size] = new List<RenderTexture>();
            }

            availableTextures[size].Add(texture);
        }

        // Очистить все текстуры в пуле (используется при выходе или смене сцены)
        public void ClearAll()
        {
            // Очищаем доступные текстуры
            foreach (var sizeGroup in availableTextures)
            {
                foreach (var texture in sizeGroup.Value)
                {
                    if (texture != null && texture.IsCreated())
                    {
                        texture.Release();
                        UnityEngine.Object.Destroy(texture);
                    }
                }
            }

            // Очищаем используемые текстуры
            foreach (var sizeGroup in inUseTextures)
            {
                foreach (var texture in sizeGroup.Value)
                {
                    if (texture != null && texture.IsCreated())
                    {
                        texture.Release();
                        UnityEngine.Object.Destroy(texture);
                    }
                }
            }

            availableTextures.Clear();
            inUseTextures.Clear();
            textureToSize.Clear();
        }
    }

    // Пул текстур для оптимизации работы с памятью
    private TexturePool texturePool;

    // Триггер события обновления маски
    // Триггер события обновления маски
    private void TriggerSegmentationMaskUpdatedEvent(RenderTexture mask)
    {
        // Вызываем событие, если есть подписчики
        if (OnSegmentationMaskUpdated != null)
        {
            OnSegmentationMaskUpdated.Invoke(mask);
            Debug.Log($"[WallSegmentation] Событие OnSegmentationMaskUpdated вызвано");
        }
        else
        {
            Debug.LogWarning($"[WallSegmentation] ⚠️ Нет подписчиков на событие OnSegmentationMaskUpdated");
        }
    }

    // Вызываем событие при создании маски сегментации с улучшениями
    private void OnMaskCreated(RenderTexture mask)
    {
        if (mask == null)
            return;

        // Создаем стабильное и улучшенное представление маски
        RenderTexture enhancedMask = ProcessMaskForStabilityAndVisualization(mask);

        // Вызываем событие с улучшенной маской
        TriggerSegmentationMaskUpdatedEvent(enhancedMask);

        // Очищаем усиленную маску, так как TriggerSegmentationMaskUpdatedEvent должен был сделать свою копию
        // НЕ используем ReleaseTemporary, так как не все текстуры созданы через GetTemporary
        if (enhancedMask != mask && enhancedMask != lastSuccessfulMask)
        {
            // Просто отпускаем ссылку на текстуру, сборщик мусора сам её освободит
            // Это безопаснее, чем вызывать ReleaseTemporary для не-временных текстур
        }
    }

    /// <summary>
    /// Обрабатывает маску для стабилизации и визуального улучшения
    /// </summary>
    private RenderTexture ProcessMaskForStabilityAndVisualization(RenderTexture currentMask)
    {
        if (currentMask == null || !currentMask.IsCreated())
        {
            Debug.LogWarning("[WallSegmentation] Получена пустая или невалидная маска в ProcessMaskForStabilityAndVisualization");
            return null;
        }

        // Временная текстура для обработки
        RenderTexture tempMask = texturePool.GetTexture(currentMask.width, currentMask.height);
        RenderTexture resultMask = null;

        try
        {
            // Копируем входную маску во временную
            Graphics.Blit(currentMask, tempMask);

            // Анализируем качество маски
            float maskQuality = AnalyzeMaskQuality(tempMask);

            // Применяем пост-обработку с учетом качества
            resultMask = ApplyPostProcessing(tempMask, maskQuality);

            return resultMask;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[WallSegmentation] Ошибка в ProcessMaskForStabilityAndVisualization: {e.Message}\n{e.StackTrace}");

            // В случае ошибки освобождаем resultMask, если он был создан
            if (resultMask != null)
            {
                texturePool.ReleaseTexture(resultMask);
            }

            return currentMask;
        }
        finally
        {
            // Возвращаем временную текстуру в пул
            texturePool.ReleaseTexture(tempMask);
        }
    }

    /// <summary>
    /// Применяет пост-обработку к маске с учетом качества
    /// </summary>
    private RenderTexture ApplyPostProcessing(RenderTexture inputMask, float quality)
    {
        // Создаем результирующую текстуру
        RenderTexture resultMask = texturePool.GetTexture(inputMask.width, inputMask.height);

        // Применяем улучшение маски
        RenderTexture enhancedMask = EnhanceSegmentationMask(inputMask);

        // Копируем результат в выходную текстуру
        Graphics.Blit(enhancedMask, resultMask);

        return resultMask;
    }

    /// <summary>
    /// Анализирует качество маски (доля значимых пикселей)
    /// </summary>
    private float AnalyzeMaskQuality(RenderTexture mask)
    {
        if (mask == null || !mask.IsCreated())
            return 0f;

        // Создаем временную текстуру для анализа
        Texture2D tempTexture = new Texture2D(mask.width, mask.height, TextureFormat.RGBA32, false);
        RenderTexture previousRT = RenderTexture.active;
        RenderTexture.active = mask;

        // Считываем пиксели
        tempTexture.ReadPixels(new Rect(0, 0, mask.width, mask.height), 0, 0);
        tempTexture.Apply();
        RenderTexture.active = previousRT;

        // Анализируем качество (доля ненулевых красных пикселей для стен)
        Color[] pixels = tempTexture.GetPixels();
        int significantPixels = 0;

        foreach (Color pixel in pixels)
        {
            // Проверяем, является ли пиксель значимым (красный канал для стен)
            if (pixel.r > 0.5f) // Значительное значение красного канала
            {
                significantPixels++;
            }
        }

        // Освобождаем ресурсы
        Destroy(tempTexture);

        // Возвращаем долю значимых пикселей
        return (float)significantPixels / pixels.Length;
    }

    /// <summary>
    /// Освобождает ресурсы при уничтожении объекта
    /// </summary>
    private void OnDestroy()
    {
        Debug.Log("[WallSegmentation-OnDestroy] Очистка ресурсов Sentis...");

        // Отписываемся от событий
        Debug.Log("[WallSegmentation-OnDestroy] Отписка от событий AR...");

        // Освобождаем текстуру сегментации
        if (segmentationMaskTexture != null)
        {
            segmentationMaskTexture.Release();
            Debug.Log("[WallSegmentation-OnDestroy] Освобождена текстура сегментации");
        }

        // Освобождаем также lastSuccessfulMask
        if (lastSuccessfulMask != null)
        {
            lastSuccessfulMask.Release();
            Debug.Log("[WallSegmentation-OnDestroy] Освобождена текстура lastSuccessfulMask");
        }

        // Освобождаем материал
        if (segmentationMaterial != null)
        {
            Destroy(segmentationMaterial);
            Debug.Log("[WallSegmentation-OnDestroy] Освобожден сегментационный материал");
        }

        // Освобождаем текстуру изображения
        if (cameraTexture != null)
        {
            Destroy(cameraTexture);
            Debug.Log("[WallSegmentation-OnDestroy] Освобождена камера");
        }

        // Освобождаем ML ресурсы
        DisposeEngine();

        Debug.Log("[WallSegmentation-OnDestroy] Ресурсы успешно очищены");

        // Очищаем пул текстур
        if (texturePool != null)
        {
            texturePool.ClearAll();
            Debug.Log("[WallSegmentation-OnDestroy] Очищен пул текстур");
        }
    }

    /// <summary>
    /// Освобождает ресурсы ML движка
    /// </summary>
    private void DisposeEngine()
    {
        Debug.Log("[WallSegmentation-DisposeEngine] Освобождение ресурсов движка Sentis...");

        // Освобождаем worker
        if (worker != null)
        {
            try
            {
                worker.Dispose();
                worker = null;
                Debug.Log("[WallSegmentation-DisposeEngine] Worker успешно освобожден");
            }
            catch (Exception e)
            {
                Debug.LogError($"[WallSegmentation-DisposeEngine] Ошибка при освобождении Worker: {e.Message}");
            }
        }

        // Освобождаем движок
        if (engine != null)
        {
            try
            {
                engine.Dispose();
                engine = null;
                Debug.Log("[WallSegmentation-DisposeEngine] Engine успешно освобожден");
            }
            catch (Exception e)
            {
                Debug.LogError($"[WallSegmentation-DisposeEngine] Ошибка при освобождении Engine: {e.Message}");
            }
        }

        // Освобождаем модель
        if (runtimeModel != null)
        {
            try
            {
                if (runtimeModel is IDisposable disposableModel)
                {
                    disposableModel.Dispose();
                }
                runtimeModel = null;
            }
            catch (Exception e)
            {
                Debug.LogError($"[WallSegmentation-DisposeEngine] Ошибка при освобождении Model: {e.Message}");
            }
        }
    }

    /// <summary>
    /// Инициализирует компонент при запуске
    /// </summary>
    private void Start()
    {
        Debug.Log("[WallSegmentation] ➡️ Start() вызван. Начало инициализации...");

        // Устанавливаем значения по умолчанию
        isModelInitialized = false;
        isInitializing = false;
        isInitializationFailed = false;
        lastErrorMessage = null;
        consecutiveFailCount = 0;

        // Инициализируем пул текстур
        texturePool = new TexturePool(RenderTextureFormat.ARGB32);

        // Если маска не инициализирована, создаем ее
        if (segmentationMaskTexture == null)
        {
            segmentationMaskTexture = new RenderTexture(inputResolution.x / 4, inputResolution.y / 4, 0, RenderTextureFormat.ARGB32);
            segmentationMaskTexture.enableRandomWrite = true;
            segmentationMaskTexture.Create();
            Debug.Log("[WallSegmentation] ✅ Создана новая segmentationMaskTexture (" + segmentationMaskTexture.width + "x" + segmentationMaskTexture.height + ")");
        }

        // Создаем текстуру для камеры, если нужно
        if (cameraTexture == null)
        {
            cameraTexture = new Texture2D(inputResolution.x, inputResolution.y, TextureFormat.RGBA32, false);
            Debug.Log("[WallSegmentation] ✅ Создана cameraTexture (" + cameraTexture.width + "x" + cameraTexture.height + ")");
        }

        // Инициализируем материал для постобработки, если его нет
        if (segmentationMaterial == null)
        {
            try
            {
                Shader shader = Shader.Find("Hidden/SegmentationPostProcess");
                if (shader != null)
                {
                    segmentationMaterial = new Material(shader);
                    Debug.Log("[WallSegmentation] ✅ Создан материал с шейдером SegmentationPostProcess");
                }
                else
                {
                    Debug.LogWarning("[WallSegmentation] ⚠️ Шейдер SegmentationPostProcess не найден");
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"[WallSegmentation] ❌ Ошибка при создании материала: {e.Message}");
            }
        }

        // Начинаем инициализацию модели
        Debug.Log("[WallSegmentation] 🔄 Запуск безопасной загрузки модели (через корутину)");
        StartCoroutine(InitializeSegmentation());

        // После старта выводим состояние компонента для отладки через 2 секунды
        Invoke("DumpCurrentState", 2f);

        Debug.Log("[WallSegmentation] ✅ Start() завершен");
    }

    /// <summary>
    /// Выводит текущее состояние компонента с задержкой
    /// </summary>
    private IEnumerator DelayedStateDump()
    {
        yield return new WaitForSeconds(2f);
        DumpCurrentState();
    }

    /// <summary>
    /// Выводит текущее состояние компонента
    /// </summary>
    public void DumpCurrentState()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("==== СОСТОЯНИЕ КОМПОНЕНТА WALL SEGMENTATION ====");
        sb.AppendLine($"Модель инициализирована: {isModelInitialized}");
        sb.AppendLine($"Идет инициализация: {isInitializing}");
        sb.AppendLine($"Ошибка инициализации: {isInitializationFailed}");
        sb.AppendLine($"Последняя ошибка: {lastErrorMessage ?? "нет"}");
        sb.AppendLine($"Счетчик последовательных ошибок: {consecutiveFailCount}");
        sb.AppendLine($"Используется симуляция: {usingSimulatedSegmentation}");

        if (segmentationMaskTexture != null)
        {
            sb.AppendLine($"Текстура маски: {segmentationMaskTexture.width}x{segmentationMaskTexture.height} ({segmentationMaskTexture.format})");
        }
        else
        {
            sb.AppendLine("Текстура маски: не создана");
        }

        if (cameraTexture != null)
        {
            sb.AppendLine($"Текстура камеры: {cameraTexture.width}x{cameraTexture.height} ({cameraTexture.format})");
        }
        else
        {
            sb.AppendLine("Текстура камеры: не создана");
        }

        if (engine != null)
        {
            sb.AppendLine($"Engine: {engine.GetType().FullName}");
        }
        else
        {
            sb.AppendLine("Engine: не создан");
        }

        if (worker != null)
        {
            sb.AppendLine($"Worker: {worker.GetType().FullName}");
        }
        else
        {
            sb.AppendLine("Worker: не создан");
        }

        Debug.Log(sb.ToString());
    }

    private IEnumerator InitializeSegmentation()
    {
        bool shouldLogInit = debugFlags.HasFlag(DebugFlags.Initialization);
        if (shouldLogInit) Debug.Log("[WallSegmentation-InitializeSegmentation] Начинаем безопасную загрузку модели...");
        isInitializing = true;
        isModelInitialized = false;
        isInitializationFailed = false;
        lastErrorMessage = null;

        consecutiveFailCount = 0;

        if (SentisInitializer.Instance != null)
        {
            if (shouldLogInit) Debug.Log("[WallSegmentation-InitializeSegmentation] Ожидание SentisInitializer...");
            if (!SentisInitializer.IsInitialized)
            {
                yield return SentisInitializer.Instance.InitializeAsync();
            }
            if (shouldLogInit) Debug.Log("[WallSegmentation-InitializeSegmentation] SentisInitializer завершил работу (или уже был инициализирован).");
        }
        else
        {
            if (shouldLogInit) Debug.LogWarning("[WallSegmentation-InitializeSegmentation] SentisInitializer.Instance не найден. Продолжаем без него.");
        }

        // Освобождаем предыдущий экземпляр движка
        if (worker != null || runtimeModel != null)
        {
            if (shouldLogInit) Debug.Log("[WallSegmentation-InitializeSegmentation] Обнаружен существующий worker или runtimeModel. Вызов DisposeEngine...");
            DisposeEngine(); // DisposeEngine должен корректно обработать runtimeModel и worker
        }

        string fullPathToModel = "";
        Model loadedModel = null;

        try
        {
            if (modelAsset is Unity.Sentis.ModelAsset sentisModelAsset) // Проверяем тип и кастуем
            {
                if (shouldLogInit) Debug.Log($"[WallSegmentation-InitializeSegmentation] Загрузка модели из Sentis.ModelAsset: {sentisModelAsset.name}");
                // В Sentis 2.x ModelAsset может содержать непосредственно модель или байты
                // Попробуем сначала получить модель напрямую, если она там есть (может быть внутренним полем)
                // Если нет, то ModelLoader.Load(ModelAsset) должен работать
                try
                {
                    // Пытаемся получить модель через рефлексию, если поле 'model' существует, но не публично
                    var modelField = typeof(Unity.Sentis.ModelAsset).GetField("model", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
                    if (modelField != null && modelField.GetValue(sentisModelAsset) is Model directModel)
                    {
                        loadedModel = directModel;
                        if (shouldLogInit) Debug.Log("[WallSegmentation-InitializeSegmentation] Модель получена напрямую из Sentis.ModelAsset.model (через рефлексию).");
                    }
                }
                catch (Exception reflectEx)
                {
                    if (shouldLogInit) Debug.LogWarning($"[WallSegmentation-InitializeSegmentation] Не удалось получить модель через рефлексию из ModelAsset: {reflectEx.Message}");
                }

                if (loadedModel == null)
                {
                    // Стандартный способ загрузки из ModelAsset, если он есть
                    loadedModel = ModelLoader.Load(sentisModelAsset);
                    if (shouldLogInit && loadedModel != null) Debug.Log("[WallSegmentation-InitializeSegmentation] Модель загружена через ModelLoader.Load(Sentis.ModelAsset).");
                }
            }
            else if (modelAsset != null && !(modelAsset is Unity.Sentis.ModelAsset))
            {
                if (shouldLogInit) Debug.LogWarning($"[WallSegmentation-InitializeSegmentation] modelAsset ({modelAsset.name}, тип: {modelAsset.GetType()}) не является Unity.Sentis.ModelAsset. Попытка загрузки по пути this.modelPath.");
            }

            // Если модель не загружена из ModelAsset, пробуем по пути
            if (loadedModel == null && !string.IsNullOrEmpty(this.modelPath))
            {
                fullPathToModel = GetFullPathToModel(this.modelPath);
                if (shouldLogInit) Debug.Log($"[WallSegmentation-InitializeSegmentation] Загрузка модели из пути: {fullPathToModel}");
                if (string.IsNullOrEmpty(fullPathToModel))
                {
                    lastErrorMessage = "Путь к файлу модели недействителен или пуст (после GetFullPathToModel).";
                    Debug.LogError($"[WallSegmentation-InitializeSegmentation] ❌ {lastErrorMessage}");
                    isInitializationFailed = true;
                    yield break;
                }
                loadedModel = ModelLoader.Load(fullPathToModel);
                if (shouldLogInit && loadedModel != null) Debug.Log("[WallSegmentation-InitializeSegmentation] Модель загружена по пути через ModelLoader.Load(path).");
            }
            else if (loadedModel == null)
            {
                lastErrorMessage = "ModelAsset не является корректным Sentis.ModelAsset ИЛИ modelPath не указан/недействителен.";
                Debug.LogError($"[WallSegmentation-InitializeSegmentation] ❌ {lastErrorMessage}");
                isInitializationFailed = true;
                yield break;
            }

            if (loadedModel == null)
            {
                lastErrorMessage = $"Не удалось загрузить модель ни из ModelAsset, ни по пути: {(modelAsset != null ? modelAsset.name : fullPathToModel)}.";
                Debug.LogError($"[WallSegmentation-InitializeSegmentation] ❌ {lastErrorMessage}");
                isInitializationFailed = true;
                yield break;
            }

            this.runtimeModel = loadedModel;
            if (shouldLogInit) Debug.Log($"[WallSegmentation-InitializeSegmentation] Модель успешно загружена и присвоена this.runtimeModel. (this.runtimeModel is null: {this.runtimeModel == null})");

            BackendType backend = (BackendType)this.selectedBackend;
            if (shouldLogInit) Debug.Log($"[WallSegmentation-InitializeSegmentation] Создание Worker с бэкендом: {backend} для модели {(this.runtimeModel != null ? "OK" : "NULL")}");

            // Используем конструктор Worker, если WorkerFactory недоступен напрямую
            // this.worker = WorkerFactory.CreateWorker(backend, this.runtimeModel); 
            this.worker = new Worker(this.runtimeModel, backend);

            if (this.worker == null)
            {
                lastErrorMessage = "Не удалось создать Worker.";
                Debug.LogError($"[WallSegmentation-InitializeSegmentation] ❌ {lastErrorMessage}");
                isInitializationFailed = true;
                // Освобождаем модель, только если она не пришла из ModelAsset, который может использоваться где-то еще
                // и если Dispose() для Model существует (убрали пока)
                // if (this.runtimeModel != null && (modelAsset == null || !(modelAsset is Unity.Sentis.ModelAsset))) 
                // { this.runtimeModel.Dispose(); }
                this.runtimeModel = null;
                yield break;
            }
            if (shouldLogInit) Debug.Log($"[WallSegmentation-InitializeSegmentation] Worker успешно создан и присвоен this.worker. (this.worker is null: {this.worker == null})");

            if (shouldLogInit && this.worker != null)
            {
                Debug.Log($"[WallSegmentation-InitializeSegmentation] Worker backend type: {backend} (используемый при создании)");
            }

            bool finalCheckOk = this.runtimeModel != null && this.worker != null;
            if (shouldLogInit) Debug.Log($"[WallSegmentation-InitializeSegmentation] Финальная проверка перед isModelInitialized = true: runtimeModel set: {this.runtimeModel != null}, worker set: {this.worker != null}. Результат: {finalCheckOk}");

            if (finalCheckOk)
            {
                isModelInitialized = true;
                if (shouldLogInit) Debug.Log($"[WallSegmentation-InitializeSegmentation] ✅ Сегментация стен успешно инициализирована. isModelInitialized = {isModelInitialized}");
                OnModelInitialized?.Invoke();
            }
            else
            {
                lastErrorMessage = "Финальная проверка перед установкой isModelInitialized провалена: runtimeModel или worker все еще null.";
                Debug.LogError($"[WallSegmentation-InitializeSegmentation] ❌ {lastErrorMessage}");
                isInitializationFailed = true;
                if (this.worker != null) this.worker.Dispose();
                // if (this.runtimeModel != null && (modelAsset == null || !(modelAsset is Unity.Sentis.ModelAsset))) 
                // { this.runtimeModel.Dispose(); }
                this.worker = null;
                this.runtimeModel = null;
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[WallSegmentation-InitializeSegmentation] ❌ Ошибка при инициализации модели: {e.Message}\n{e.StackTrace}");
            isInitializationFailed = true;
            lastErrorMessage = e.Message;
            // yield break; // Убрали yield break, чтобы isInitializing установился в false
        }
        finally // Добавляем блок finally
        {
            isInitializing = false; // Устанавливаем флаг в false здесь
            if (shouldLogInit) Debug.Log($"[WallSegmentation-InitializeSegmentation] Процесс инициализации завершен. isInitializing = {isInitializing}");
        }
    }

    /// <summary>
    /// Инициализация модели напрямую (может вызвать краш при больших моделях)
    /// </summary>
    public void InitializeModelDirect()
    {
        if (isModelInitialized) return;

        Debug.Log("Инициализация модели (синхронно)");

        if (modelAsset == null)
        {
            Debug.LogError("ModelAsset не назначен! Сегментация стен не будет работать.");
            return;
        }

        try
        {
            // Загружаем модель с помощью рефлексии
            var modelLoaderType = Type.GetType("Unity.Sentis.ModelLoader, Unity.Sentis");
            if (modelLoaderType == null)
            {
                throw new Exception("Тип Unity.Sentis.ModelLoader не найден. Проверьте, установлен ли пакет Sentis.");
            }

            var loadMethod = modelLoaderType.GetMethod("Load", new Type[] { modelAsset.GetType() });
            if (loadMethod == null)
            {
                // Попробуем найти метод Load, который принимает ModelAsset
                Type modelAssetType = Type.GetType("Unity.Sentis.ModelAsset, Unity.Sentis");
                if (modelAssetType != null && modelAsset.GetType() == modelAssetType)
                {
                    loadMethod = modelLoaderType.GetMethod("Load", new Type[] { modelAssetType });
                }

                if (loadMethod == null)
                {
                    throw new Exception($"Метод Load не найден в ModelLoader для типа {modelAsset.GetType().Name}. Убедитесь, что modelAsset является ModelAsset или путем к файлу ONNX/Sentis.");
                }
            }

            // Загружаем модель и сохраняем в обоих полях
            object loadedModelObj = loadMethod.Invoke(null, new object[] { modelAsset });
            if (loadedModelObj == null)
            {
                throw new Exception("Модель загружена, но результат null");
            }

            Model loadedModel = loadedModelObj as Model;
            if (loadedModel == null)
            {
                throw new Exception("Не удалось привести загруженную модель к типу Unity.Sentis.Model");
            }

            // Сохраняем ссылку на модель в обоих полях
            this.model = loadedModel;
            this.runtimeModel = loadedModel;

            // Создаем исполнителя напрямую, без рефлексии
            BackendType backendType = preferredBackend == 0 ? BackendType.CPU : BackendType.GPUCompute;

            // Проверяем, есть ли у нас ссылка на старый worker и освобождаем его, если есть
            if (this.worker != null)
            {
                this.worker.Dispose();
                this.worker = null;
            }
            if (this.engine != null) // engine - это наш новый worker
            {
                this.engine.Dispose();
            }

            Debug.Log($"Создаем Worker с параметрами: модель типа {this.runtimeModel.GetType().FullName}, backendType={backendType}");
            this.engine = new Worker(this.runtimeModel, backendType); // Используем this.engine
            this.worker = this.engine; // Также присваиваем this.worker для совместимости, если он где-то используется напрямую

            // Выведем диагностику для Worker
            Debug.Log($"Worker создан, тип: {this.engine.GetType().FullName}");
            Debug.Log("Доступные методы Worker:");
            var methods = this.engine.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance);
            foreach (var method in methods)
            {
                if (method.GetParameters().Length == 0)
                {
                    Debug.Log($"- {method.Name} (без параметров)");
                }
            }

            isModelInitialized = true;
            Debug.Log($"Модель успешно инициализирована с бэкендом: {backendType}");
        }
        catch (Exception e)
        {
            Debug.LogError($"Ошибка при инициализации модели: {e.Message}");
            throw; // Пробрасываем исключение для обработки вызывающим кодом
        }
    }

    /// <summary>
    /// Инициализирует сегментацию стен
    /// </summary>
    public void InitializeSegmentation(UnityEngine.Object newModelAsset = null)
    {
        // Если предоставлена новая модель, обновляем
        if (newModelAsset != null && newModelAsset != modelAsset)
        {
            // Освобождаем текущую модель, если она инициализирована
            if (isModelInitialized)
            {
                DisposeEngine();
                engine = null;
                runtimeModel = null;
                isModelInitialized = false;
            }

            // Устанавливаем новую модель
            modelAsset = newModelAsset;
        }

        // Если модель не инициализирована и не идет процесс инициализации, запускаем
        if (!isModelInitialized && !isInitializing)
        {
            if (useSafeModelLoading)
            {
                StartCoroutine(InitializeSegmentation());
            }
            else
            {
                try
                {
                    InitializeModelDirect();
                }
                catch (Exception e)
                {
                    Debug.LogError($"Ошибка при инициализации модели: {e.Message}");
                }
            }
        }
    }

    /// <summary>
    /// Проверяет, выполняется ли код в режиме XR Simulation
    /// </summary>
    private bool IsRunningInXRSimulation()
    {
        // Если флаг установлен вручную, всегда возвращаем true
        if (forceXRSimulationCapture)
        {
            return true;
        }

        // Проверка на имя сцены
        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (sceneName.Contains("Simulation") || sceneName.Contains("XR"))
        {
            return true;
        }

        // Проверка на редактор Unity
#if UNITY_EDITOR
        // Проверяем наличие компонентов симуляции в сцене
        var simulationObjects = FindObjectsOfType<MonoBehaviour>()
            .Where(mb => mb.GetType().Name.Contains("Simulation"))
            .ToArray();

        if (simulationObjects.Length > 0)
        {
            return true;
        }

        // Проверяем, есть ли объект "XRSimulationEnvironment" в сцене
        var simEnvObjects = FindObjectsOfType<Transform>()
            .Where(t => t.name.Contains("XRSimulation"))
            .ToArray();

        if (simEnvObjects.Length > 0)
        {
            return true;
        }

        // Проверяем активен ли XR Simulation в настройках проекта (через рефлексию)
        try
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var type in assembly.GetTypes())
                {
                    if (type.Name.Contains("Simulation") && type.Name.Contains("Provider"))
                    {
                        return true;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"Ошибка при поиске провайдеров симуляции: {ex.Message}");
        }
#endif

        // Если у нас AR Session, но не на реальном устройстве, это вероятно симуляция
#if UNITY_EDITOR
        if (FindObjectOfType<ARSession>() != null)
        {
            return true;
        }
#endif

        return false;
    }

    private Texture2D GetCameraTexture()
    {
        if (arSessionManager == null)
        {
            return null;
        }

        // Check AR session state - access it statically
        if (ARSession.state != ARSessionState.SessionTracking)
        {
            return null;
        }

        // Проверяем, работаем ли мы в режиме XR Simulation
        bool isSimulation = IsRunningInXRSimulation();

        // В режиме симуляции сразу используем альтернативный метод
        if (isSimulation)
        {
            Texture2D result = GetCameraTextureFromSimulation();
            if (result == null)
            {
                Debug.LogError("[WallSegmentation-GetCameraTexture] ❌ GetCameraTextureFromSimulation вернул null");
            }
            return result;
        }

        // Получаем ARCameraManager
        ARCameraManager cameraManager = ARCameraManager;
        if (cameraManager == null)
        {
            Debug.LogWarning("[WallSegmentation-GetCameraTexture] ARCameraManager не найден");
            return GetCameraTextureFromSimulation();
        }

        // Пытаемся получить изображение через ARCameraManager
        if (cameraManager.TryAcquireLatestCpuImage(out XRCpuImage image))
        {
            using (image)
            {
                // Преобразуем XRCpuImage в Texture2D с нужным разрешением
                var conversionParams = new XRCpuImage.ConversionParams
                {
                    inputRect = new RectInt(0, 0, image.width, image.height),
                    outputDimensions = new Vector2Int(inputResolution.x, inputResolution.y),
                    outputFormat = TextureFormat.RGBA32,
                    transformation = XRCpuImage.Transformation.MirrorY
                };

                try
                {
                    // Получаем данные изображения
                    var rawTextureData = new NativeArray<byte>(inputResolution.x * inputResolution.y * 4, Allocator.Temp);

                    // Proper conversion with NativeArray
                    image.Convert(conversionParams, rawTextureData);

                    cameraTexture.LoadRawTextureData(rawTextureData);
                    cameraTexture.Apply();
                    rawTextureData.Dispose();

                    return cameraTexture;
                }
                catch (Exception e)
                {
                    Debug.LogError($"Ошибка при конвертации изображения с камеры: {e.Message}");
                    return null;
                }
            }
        }
        else
        {
            // Лог выводится только раз в 30 кадров
            if (Time.frameCount % 30 == 0)
            {
                Debug.LogWarning("[WallSegmentation-GetCameraTexture] TryAcquireLatestCpuImage не удалось получить изображение. Используем альтернативный метод для XR Simulation.");
            }

            // Альтернативный метод для XR Simulation - получение изображения с камеры напрямую
            return GetCameraTextureFromSimulation();
        }
    }

    /// <summary>
    /// Альтернативный метод получения текстуры с камеры для XR Simulation
    /// </summary>
    private Texture2D GetCameraTextureFromSimulation()
    {
        // Получаем камеру через ARCameraManager (если есть)
        Camera arCamera = null;
        if (arSessionManager != null && arSessionManager.TryGetComponent<Camera>(out Camera cam))
        {
            arCamera = cam;
        }

        // Если не нашли камеру через ARCameraManager, ищем через XROrigin
        if (arCamera == null && xrOrigin != null)
        {
            arCamera = xrOrigin.Camera;
        }

        // Если до сих пор нет камеры, ищем любую Camera с тегом MainCamera
        if (arCamera == null)
        {
            arCamera = Camera.main;
        }

        // Если всё ещё нет камеры, ищем специальную SimulationCamera
        if (arCamera == null)
        {
            arCamera = GameObject.FindObjectsOfType<Camera>().FirstOrDefault(c => c.name.Contains("Simulation"));
        }

        // Если камеры по-прежнему нет, показываем ошибку и возвращаем null
        if (arCamera == null)
        {
            // Лог только раз в 60 кадров
            if (Time.frameCount % 60 == 0)
            {
                Debug.LogError("[WallSegmentation-GetCameraTextureFromSimulation] ❌ Не удалось найти камеру для получения изображения");
            }
            return null;
        }

        // Сохраняем текущий culling mask и очистку
        int originalCullingMask = arCamera.cullingMask;
        CameraClearFlags originalClearFlags = arCamera.clearFlags;
        Color originalBackgroundColor = arCamera.backgroundColor; // Сохраняем цвет фона


        // Временно устанавливаем параметры для рендеринга
        // Исключаем слой UI (по умолчанию слой 5)
        int uiLayer = LayerMask.NameToLayer("UI");
        int layersToExclude = 0;
        if (uiLayer != -1)
        {
            layersToExclude |= (1 << uiLayer);
        }
        // При необходимости здесь можно добавить другие слои для исключения
        // int anotherLayerToExclude = LayerMask.NameToLayer("YourLayerName");
        // if (anotherLayerToExclude != -1) {
        //     layersToExclude |= (1 << anotherLayerToExclude);
        // }

        arCamera.cullingMask = ~layersToExclude; // Рендерить все, КРОМЕ исключенных слоев
        arCamera.clearFlags = CameraClearFlags.SolidColor;
        arCamera.backgroundColor = Color.clear; // Используем прозрачный фон

        // Выполняем рендеринг // Этот вызов Render() здесь может быть не нужен, если следующий Render() с targetTexture покрывает все.
        // arCamera.Render(); 

        // Создаем RenderTexture для захвата изображения
        RenderTexture rt = RenderTexture.GetTemporary(inputResolution.x, inputResolution.y, 24, RenderTextureFormat.ARGB32);
        RenderTexture prevRT = RenderTexture.active;
        RenderTexture.active = rt;
        arCamera.targetTexture = rt;

        // Рендерим с установленной targetTexture
        arCamera.Render();

        // Копируем изображение в нашу текстуру
        if (cameraTexture == null)
        {
            Debug.LogWarning("[WallSegmentation-GetCameraTextureFromSimulation] ⚠️ cameraTexture была null, создаем новую");
            cameraTexture = new Texture2D(inputResolution.x, inputResolution.y, TextureFormat.RGBA32, false);
        }

        cameraTexture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        cameraTexture.Apply();

        // Восстанавливаем состояние камеры и RenderTexture
        arCamera.targetTexture = null;
        RenderTexture.active = prevRT;
        RenderTexture.ReleaseTemporary(rt);

        // Восстанавливаем оригинальные параметры камеры
        arCamera.cullingMask = originalCullingMask;
        arCamera.clearFlags = originalClearFlags;
        arCamera.backgroundColor = originalBackgroundColor;

        // Добавляем дамп состояния, чтобы увидеть, в каком состоянии система
        // this.DumpCurrentState(); // Раскомментируйте для отладки, если необходимо

        return cameraTexture;
    }

    /// <summary>
    /// Создает временную камеру для использования в режиме симуляции
    /// </summary>
    private Camera CreateTemporaryCamera()
    {
        try
        {
            // Создаем временный GameObject для камеры
            GameObject cameraObj = new GameObject("TemporarySimulationCamera");

            // Добавляем компонент камеры
            Camera camera = cameraObj.AddComponent<Camera>();

            // Настраиваем параметры камеры
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.cullingMask = -1; // Все слои
            camera.fieldOfView = 60f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 100f;

            // Позиционируем камеру чтобы она смотрела вперед
            cameraObj.transform.position = new Vector3(0, 1.6f, 0); // Примерная высота глаз
            cameraObj.transform.rotation = Quaternion.identity;

            // Добавляем в родительский объект, если он есть
            if (xrOrigin != null)
            {
                cameraObj.transform.SetParent(xrOrigin.transform, false);
            }

            Debug.Log($"[CreateTemporaryCamera] Создана временная камера для симуляции");
            return camera;
        }
        catch (Exception e)
        {
            Debug.LogError($"[CreateTemporaryCamera] Ошибка при создании временной камеры: {e.Message}");
            return null;
        }
    }

    /// <summary>
    /// Выполняет сегментацию с использованием ML модели через рефлексию
    /// </summary>
    private void PerformSegmentation(Texture2D inputTexture)
    {
        bool shouldLogExec = debugFlags.HasFlag(DebugFlags.ExecutionFlow);
        bool shouldLogDetailedExec = debugFlags.HasFlag(DebugFlags.DetailedExecution);

        // if (shouldLogExec) Debug.Log($"[WallSegmentation-PerformSegmentation] Запуск сегментации с текстурой: {(inputTexture == null ? "NULL" : $"{inputTexture.width}x{inputTexture.height}")}");

        if (isProcessing)
        {
            // if (shouldLogExec) Debug.LogWarning("[WallSegmentation-PerformSegmentation] ⚠️ Процесс сегментации уже запущен, новый запуск отменен.");
            return;
        }

        if (!isModelInitialized || worker == null || runtimeModel == null)
        {
            lastErrorMessage = "Модель не инициализирована или worker/runtimeModel не доступны.";
            // if (shouldLogExec) Debug.LogError($"[WallSegmentation-PerformSegmentation] ❌ {lastErrorMessage}");
            isProcessing = false;

            if (!isModelInitialized && !isInitializing)
            {
                // if (shouldLogExec) Debug.LogWarning("[WallSegmentation-PerformSegmentation] ⚠️ Модель не инициализирована, попытка повторной инициализации...");
                StartCoroutine(InitializeSegmentation());
            }
            return;
        }

        isProcessing = true;
        // if (shouldLogDetailedExec) Debug.Log($"[WallSegmentation-PerformSegmentation] Установлен флаг isProcessing = true. Input Texture: {(inputTexture != null ? "OK" : "NULL")}");

        Tensor<float> inputTensor = null;
        try
        {
            // Убедимся, что текстура имеет нужные размеры, если это необходимо для CreateTensorFromPixels
            // Например, если CreateTensorFromPixels ожидает конкретный размер.
            // Если CreateTensorFromPixels сам обрабатывает изменение размера, этот блок можно упростить/удалить.
            if (inputTexture.width != inputResolution.x || inputTexture.height != inputResolution.y)
            {
                if (shouldLogDetailedExec) Debug.Log($"[WallSegmentation-PerformSegmentation] 🔄 Изменяем размер входной текстуры с {inputTexture.width}x{inputTexture.height} на {inputResolution.x}x{inputResolution.y}");
                RenderTexture tempRT = RenderTexture.GetTemporary(inputResolution.x, inputResolution.y, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(inputTexture, tempRT);
                Texture2D resizedTexture = new Texture2D(inputResolution.x, inputResolution.y, TextureFormat.RGBA32, false);
                RenderTexture.active = tempRT;
                resizedTexture.ReadPixels(new Rect(0, 0, inputResolution.x, inputResolution.y), 0, 0);
                resizedTexture.Apply();
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(tempRT);

                // Если inputTexture была временной (например, из GetCameraTextureFromSimulation), её нужно уничтожить, если она больше не нужна
                // if (inputTexture != cameraTexture) Destroy(inputTexture); // Осторожно с этим, если inputTexture - это cameraTexture
                inputTexture = resizedTexture; // Используем измененную текстуру
            }

            inputTensor = CreateTensorFromPixels(inputTexture); // Предполагаем, что inputTexture теперь правильного размера

            // Если inputTexture была создана как resizedTexture, ее нужно уничтожить после создания тензора
            if (inputTexture.name.StartsWith("ResizedTexture_")) // Пример проверки, если вы так именуете их
            {
                Destroy(inputTexture);
            }


            if (inputTensor == null)
            {
                lastErrorMessage = "Не удалось создать входной тензор из текстуры.";
                Debug.LogError($"[WallSegmentation-PerformSegmentation] ❌ {lastErrorMessage}");
                isProcessing = false;
                return;
            }
            // if (shouldLogDetailedExec) Debug.Log($"[WallSegmentation-PerformSegmentation] Входной тензор создан: {inputTensor.shape}");

            // Запускаем корутину для выполнения и обработки
            StartCoroutine(ExecuteModelAndProcessResultCoroutine(inputTensor));
        }
        catch (Exception e)
        {
            lastErrorMessage = $"Ошибка при подготовке к сегментации или запуске корутины: {e.Message}";
            Debug.LogError($"[WallSegmentation-PerformSegmentation] ❌ {lastErrorMessage} \nStackTrace: {e.StackTrace}");
            RenderSimpleMask();
            OnSegmentationMaskUpdated?.Invoke(segmentationMaskTexture);
            isProcessing = false;
            if (inputTensor != null) inputTensor.Dispose();
        }
        // inputTensor будет освобожден в корутине
    }

    private IEnumerator ExecuteModelAndProcessResultCoroutine(Tensor<float> inputTensor)
    {
        bool shouldLogExec = debugFlags.HasFlag(DebugFlags.ExecutionFlow);
        bool shouldLogDetailedExec = debugFlags.HasFlag(DebugFlags.DetailedExecution);
        bool shouldLogTensorProc = debugFlags.HasFlag(DebugFlags.TensorProcessing);

        // if (shouldLogDetailedExec) Debug.Log($"[WallSegmentation-ExecuteModelAndProcessResultCoroutine] Корутина запущена с тензором {inputTensor.shape}.");

        if (worker == null || runtimeModel == null)
        {
            lastErrorMessage = "Worker или runtimeModel не инициализирован в корутине.";
            Debug.LogError($"[WallSegmentation-ExecuteModelAndProcessResultCoroutine] ❌ {lastErrorMessage}");
            RenderSimpleMask();
            isProcessing = false;
            inputTensor.Dispose();
            yield break;
        }

        try
        {
            if (runtimeModel.inputs.Count > 0)
            {
                // Установка входа перед Schedule(), если inputTensor передается в корутину
                worker.SetInput(runtimeModel.inputs[0].name, inputTensor);
                // if (shouldLogDetailedExec) Debug.Log($"[WallSegmentation-ExecuteModelAndProcessResultCoroutine] ✅ Входной тензор '{runtimeModel.inputs[0].name}' установлен.");
                worker.Schedule(); // Запуск без аргументов, так как вход установлен через SetInput
                // if (shouldLogDetailedExec) Debug.Log("[WallSegmentation-ExecuteModelAndProcessResultCoroutine] ▶️ worker.Schedule() вызван.");
            }
            else
            {
                lastErrorMessage = "Модель не имеет определенных входов.";
                Debug.LogError($"[WallSegmentation-ExecuteModelAndProcessResultCoroutine] ❌ {lastErrorMessage}");
                RenderSimpleMask();
                isProcessing = false;
                inputTensor.Dispose();
                yield break;
            }

            // worker.Execute(); // Заменено на Schedule()
            // if (shouldLogDetailedExec) Debug.Log("[WallSegmentation-ExecuteModelAndProcessResultCoroutine] ▶️ worker.Execute() вызван и завершен (синхронно).");

            if (runtimeModel.outputs.Count == 0)
            {
                lastErrorMessage = "Модель не имеет определенных выходов.";
                Debug.LogError($"[WallSegmentation-ExecuteModelAndProcessResultCoroutine] ❌ {lastErrorMessage}");
                RenderSimpleMask();
                isProcessing = false;
                inputTensor.Dispose();
                yield break;
            }

            string outputName = runtimeModel.outputs[0].name;
            Tensor peekedBaseTensor = worker.PeekOutput(outputName);

            if (peekedBaseTensor == null)
            {
                lastErrorMessage = $"PeekOutput вернул null для выхода '{outputName}'.";
                Debug.LogError($"[WallSegmentation-ExecuteModelAndProcessResultCoroutine] ❌ {lastErrorMessage}");
                RenderSimpleMask();
                isProcessing = false;
                inputTensor.Dispose();
                yield break;
            }

            // if (shouldLogTensorProc) Debug.Log($"[WallSegmentation-ExecuteModelAndProcessResultCoroutine] Peeked output tensor (base): {peekedBaseTensor.shape}");

            // Дожидаемся завершения операций на тензоре
            peekedBaseTensor.CompleteAllPendingOperations();
            // if (shouldLogTensorProc) Debug.Log($"[WallSegmentation-ExecuteModelAndProcessResultCoroutine] ✅ peekedBaseTensor.CompleteAllPendingOperations() вызван.");

            Tensor<float> peekedTensorFloat = peekedBaseTensor as Tensor<float>;

            if (peekedTensorFloat == null)
            {
                lastErrorMessage = $"Не удалось преобразовать выходной тензор '{outputName}' в Tensor<float>. Фактический тип: {peekedBaseTensor.GetType().FullName}";
                Debug.LogError($"[WallSegmentation-ExecuteModelAndProcessResultCoroutine] ❌ {lastErrorMessage}");
                RenderSimpleMask();
                isProcessing = false;
                inputTensor.Dispose();
                yield break;
            }

            // if (shouldLogTensorProc) Debug.Log($"[WallSegmentation-ExecuteModelAndProcessResultCoroutine] Output tensor '{outputName}' успешно преобразован в Tensor<float>. Форма: {peekedTensorFloat.shape}");

            TensorShape outputShape = peekedTensorFloat.shape;
            if (outputShape.length == 0)
            {
                lastErrorMessage = $"Выходной тензор '{outputName}' имеет нулевую длину формы: {outputShape}.";
                Debug.LogError($"[WallSegmentation-ExecuteModelAndProcessResultCoroutine] ❌ {lastErrorMessage}");
                RenderSimpleMask();
                isProcessing = false;
                inputTensor.Dispose();
                yield break;
            }

            float[] dataArray = null;
            try
            {
                // Заменяем ToReadOnlyArray() на DownloadToArray()
                dataArray = peekedTensorFloat.DownloadToArray();
                // if (shouldLogTensorProc) Debug.Log($"[WallSegmentation-ExecuteModelAndProcessResultCoroutine] ✅ Данные из Tensor<float> скопированы в dataArray через DownloadToArray(). Длина: {dataArray?.Length}");
            }
            catch (Exception ex)
            {
                lastErrorMessage = $"Ошибка при вызове DownloadToArray() для тензора '{outputName}': {ex.Message}";
                Debug.LogError($"[WallSegmentation-ExecuteModelAndProcessResultCoroutine] ❌ {lastErrorMessage} \nStackTrace: {ex.StackTrace}");
                RenderSimpleMask();
                isProcessing = false;
                inputTensor.Dispose();
                yield break;
            }

            if (dataArray == null)
            {
                lastErrorMessage = $"DownloadToArray() для тензора '{outputName}' вернул null.";
                Debug.LogError($"[WallSegmentation-ExecuteModelAndProcessResultCoroutine] ❌ {lastErrorMessage}");
                RenderSimpleMask();
                isProcessing = false;
                inputTensor.Dispose();
                yield break;
            }

            ProcessSegmentationResult(dataArray, outputShape);
        }
        catch (Exception ex)
        {
            lastErrorMessage = $"Ошибка в корутине ExecuteModelAndProcessResultCoroutine: {ex.Message}";
            Debug.LogError($"[WallSegmentation-ExecuteModelAndProcessResultCoroutine] ❌ {lastErrorMessage}\n" + ex.StackTrace);
            RenderSimpleMask();
        }
        finally
        {
            if (inputTensor != null)
            {
                inputTensor.Dispose();
                // if (shouldLogDetailedExec) Debug.Log("[WallSegmentation-ExecuteModelAndProcessResultCoroutine] 🧹 Входной тензор освобожден.");
            }
            isProcessing = false;
            // if (shouldLogDetailedExec) Debug.Log("[WallSegmentation-ExecuteModelAndProcessResultCoroutine] Установлен флаг isProcessing = false (finally). Корутина завершена.");
        }
    }

    /// <summary>
    /// Отрисовывает простую заглушку для маски сегментации
    /// </summary>
    private void RenderSimpleMask()
    {
        Debug.Log("Создаем заглушку для маски сегментации");

        // Проверяем, что segmentationMaskTexture существует и готова
        if (segmentationMaskTexture == null || !segmentationMaskTexture.IsCreated())
        {
            Debug.LogWarning("SegmentationMaskTexture не готова для заглушки. Создаем новую текстуру.");

            // Освобождаем старую текстуру, если она существует
            if (segmentationMaskTexture != null)
            {
                segmentationMaskTexture.Release();
                Destroy(segmentationMaskTexture);
            }

            // Создаем новую текстуру с нужными параметрами
            segmentationMaskTexture = new RenderTexture(inputResolution.x, inputResolution.y, 0, RenderTextureFormat.ARGB32);
            segmentationMaskTexture.enableRandomWrite = true;
            segmentationMaskTexture.Create();

            Debug.Log($"Создана новая RenderTexture для заглушки {segmentationMaskTexture.width}x{segmentationMaskTexture.height}");
        }

        try
        {
            // Создаем пустую текстуру для рендеринга
            Texture2D simpleMask = new Texture2D(segmentationMaskTexture.width, segmentationMaskTexture.height, TextureFormat.RGBA32, false);

            // Заполняем текстуру
            Color32[] pixels = new Color32[simpleMask.width * simpleMask.height];
            for (int i = 0; i < pixels.Length; i++)
            {
                // Заполняем середину экрана "стеной" для визуализации
                int x = i % simpleMask.width;
                int y = i / simpleMask.width;

                // Определяем, находится ли пиксель в центральной области
                bool isCenter =
                    x > simpleMask.width * 0.3f &&
                    x < simpleMask.width * 0.7f &&
                    y > simpleMask.height * 0.3f &&
                    y < simpleMask.height * 0.7f;

                // Если в центре - это "стена", иначе фон, делаем обе области полупрозрачными
                pixels[i] = isCenter ? new Color32(255, 255, 255, 100) : new Color32(0, 0, 0, 0);
            }

            simpleMask.SetPixels32(pixels);
            simpleMask.Apply();

            // Копируем в RenderTexture
            RenderTexture previousRT = RenderTexture.active;
            RenderTexture.active = segmentationMaskTexture;
            GL.Clear(true, true, Color.clear); // Используем прозрачный цвет
            Graphics.Blit(simpleMask, segmentationMaskTexture);
            RenderTexture.active = previousRT;

            // Уничтожаем временный объект
            Destroy(simpleMask);

            Debug.Log("Заглушка маски отрисована с прозрачностью");
        }
        catch (Exception e)
        {
            Debug.LogError($"Ошибка при создании заглушки маски: {e.Message}");

            // Резервный вариант - попытка сгенерировать маску графически
            try
            {
                RenderTexture previousRT = RenderTexture.active;
                RenderTexture.active = segmentationMaskTexture;
                GL.Clear(true, true, Color.clear);

                // Создаем простой материал для рисования
                Material simpleMaterial = new Material(Shader.Find("Unlit/Transparent"));
                if (simpleMaterial != null)
                {
                    simpleMaterial.color = new Color(1f, 1f, 1f, 0.5f);

                    // Используем простой Graphics.DrawTexture
                    float centerWidth = segmentationMaskTexture.width * 0.4f;
                    float centerHeight = segmentationMaskTexture.height * 0.4f;
                    float centerX = (segmentationMaskTexture.width - centerWidth) / 2;
                    float centerY = (segmentationMaskTexture.height - centerHeight) / 2;

                    Graphics.DrawTexture(
                        new Rect(centerX, centerY, centerWidth, centerHeight),
                        Texture2D.whiteTexture,
                        simpleMaterial);
                }
                else
                {
                    // Если не удалось создать материал, рисуем просто белый центр
                    GL.PushMatrix();
                    GL.LoadOrtho();
                    GL.Begin(GL.QUADS);
                    GL.Color(new Color(1f, 1f, 1f, 0.5f));
                    GL.Vertex3(0.3f, 0.3f, 0);
                    GL.Vertex3(0.7f, 0.3f, 0);
                    GL.Vertex3(0.7f, 0.7f, 0);
                    GL.Vertex3(0.3f, 0.7f, 0);
                    GL.End();
                    GL.PopMatrix();
                }

                RenderTexture.active = previousRT;

                Debug.Log("Простая заглушка маски создана через графический API");
            }
            catch (Exception finalError)
            {
                Debug.LogError($"Критическая ошибка при создании маски сегментации: {finalError.Message}");
            }
        }
    }

    /// <summary>
    /// Метод для проверки, находится ли тензор в ожидающем состоянии
    /// </summary>
    private bool CheckTensorPending(Tensor<float> tensor)
    {
        try
        {
            // Простой способ проверить, доступны ли данные тензора
            // Если данные недоступны, будет выброшено исключение "Tensor data is still pending"
            if (tensor == null || tensor.shape.length == 0) return true;
            var temp = tensor[0]; // Пробуем получить первый элемент
            return false; // Если дошли до этой строки, значит данные доступны
        }
        catch (Exception ex)
        {
            // Если получили исключение с сообщением о pending, значит тензор не готов
            return ex.Message.Contains("pending");
        }
    }

    /// <summary>
    /// Вычисляет индекс для доступа к данным тензора в линеаризованном массиве
    /// </summary>
    private int IndexFromCoordinates(int batch, int channel, int height, int width,
                    int batchSize, int numChannels, int imgHeight, int imgWidth)
    {
        return batch * numChannels * imgHeight * imgWidth + channel * imgHeight * imgWidth + height * imgWidth + width;
    }

    /// <summary>
    /// Анализирует структуру тензора и выводит информацию о его значениях
    /// </summary>
    private void AnalyzeTensorData(float[] tensorData, int batch, int classes, int height, int width)
    {
        if (tensorData == null || tensorData.Length == 0)
        {
            Debug.LogWarning("[AnalyzeTensorData] Тензор пуст или null");
            return;
        }

        // Проверяем только часть тензора, чтобы не замедлять выполнение
        int sampleX = width / 2;
        int sampleY = height / 2;

        Debug.Log($"[AnalyzeTensorData] Анализ значений тензора ({batch}x{classes}x{height}x{width}), всего {tensorData.Length} элементов");

        // Выводим класс с максимальной вероятностью для центрального пикселя
        int maxClassIndex = -1;
        float maxClassValue = float.MinValue;

        // Значения для топ-5 классов
        Dictionary<int, float> topClasses = new Dictionary<int, float>();

        // Проходим по всем классам для центрального пикселя
        for (int c = 0; c < classes; c++)
        {
            int index = IndexFromCoordinates(0, c, sampleY, sampleX, batch, classes, height, width);
            float value = tensorData[index];

            // Сохраняем топ классы
            if (topClasses.Count < 5 || value > topClasses.Values.Min())
            {
                topClasses[c] = value;
                if (topClasses.Count > 5)
                {
                    // Удаляем класс с минимальным значением
                    int minClass = topClasses.OrderBy(pair => pair.Value).First().Key;
                    topClasses.Remove(minClass);
                }
            }

            // Ищем класс с максимальной вероятностью
            if (value > maxClassValue)
            {
                maxClassValue = value;
                maxClassIndex = c;
            }
        }

        // Выводим информацию о максимальном классе
        Debug.Log($"[AnalyzeTensorData] Центральный пиксель ({sampleX},{sampleY}): максимальный класс = {maxClassIndex}, значение = {maxClassValue}");

        // Выводим топ-5 классов
        Debug.Log("[AnalyzeTensorData] Топ-5 классов для центрального пикселя:");
        foreach (var pair in topClasses.OrderByDescending(p => p.Value))
        {
            Debug.Log($"  - Класс {pair.Key}: {pair.Value}");
        }

        // Проверяем значения для заданных classIndex стены и пола
        if (wallClassIndex >= 0 && wallClassIndex < classes)
        {
            int wallIndex = IndexFromCoordinates(0, wallClassIndex, sampleY, sampleX, batch, classes, height, width);
            float wallValue = tensorData[wallIndex];
            Debug.Log($"[AnalyzeTensorData] Центральный пиксель: класс стены (индекс {wallClassIndex}) = {wallValue}");
        }

        if (floorClassIndex >= 0 && floorClassIndex < classes)
        {
            int floorIndex = IndexFromCoordinates(0, floorClassIndex, sampleY, sampleX, batch, classes, height, width);
            float floorValue = tensorData[floorIndex];
            Debug.Log($"[AnalyzeTensorData] Центральный пиксель: класс пола (индекс {floorClassIndex}) = {floorValue}");
        }
    }

    /// <summary>
    /// Выполняет упрощенную симуляцию сегментации
    /// </summary>
    private void SimulateTensorData(ref float[] tensorData, int numClasses, int height, int width)
    {
        Debug.Log("[WallSegmentation-SimulateTensorData] ℹ️ Созданы симулированные данные тензора");

        System.Random random = new System.Random();
        int wallClass = wallClassIndex;
        int floorClass = floorClassIndex;

        // Заполняем тензор симулированными данными
        for (int b = 0; b < 1; b++)
        {
            for (int h = 0; h < height; h++)
            {
                for (int w = 0; w < width; w++)
                {
                    // Простая логика симуляции - выделяем стены по краям и в центре
                    bool isWall = (h < height * 0.2f) || (h > height * 0.8f) ||
                                  (w < width * 0.2f) || (w > width * 0.8f) ||
                                  (Math.Abs(h - height / 2) < height * 0.1f && w > width * 0.3f && w < width * 0.7f);

                    bool isFloor = !isWall && (h > height * 0.5f);

                    // Задаем вероятности для классов
                    for (int c = 0; c < numClasses; c++)
                    {
                        int idx = IndexFromCoordinates(b, c, h, w, 1, numClasses, height, width);

                        // По умолчанию вероятность низкая
                        tensorData[idx] = 0.01f + (float)random.NextDouble() * 0.03f;

                        // Для целевых классов задаем высокую вероятность
                        if ((c == wallClass && isWall) || (c == floorClass && isFloor))
                        {
                            // Стены и пол имеют более высокую вероятность
                            tensorData[idx] = 0.7f + (float)random.NextDouble() * 0.25f;
                        }
                    }
                }
            }
        }

        // Проводим анализ тензора для отладки
        AnalyzeTensorData(tensorData, 0, numClasses, height, width);
    }

    /// <summary>
    /// Анализирует структуру модели
    /// </summary>
    private void AnalyzeModelStructure()
    {
        if (model == null)
        {
            Debug.LogWarning("[WallSegmentation-AnalyzeModelStructure] Модель не инициализирована");
            return;
        }

        Debug.Log($"[WallSegmentation-AnalyzeModelStructure] Анализ структуры модели типа {model.GetType().Name}");

        // Дополнительный код анализа структуры модели может быть добавлен здесь
    }

    /// <summary>
    /// Выводит информацию о модели
    /// </summary>
    private void LogModelInfo()
    {
        if (model == null)
        {
            Debug.LogWarning("[WallSegmentation-LogModelInfo] Модель не инициализирована");
            return;
        }

        Debug.Log($"[WallSegmentation-LogModelInfo] Информация о модели: Тип = {model.GetType().FullName}");

        // Дополнительная информация о модели может быть добавлена здесь
    }

    /// <summary>
    /// Создает тензор в формате для XR симуляции
    /// </summary>
    private Tensor<float> TryCreateXRSimulationTensor(Texture2D inputTexture)
    {
        try
        {
            // Реализация создания тензора для XR симуляции
            Color[] pixels = inputTexture.GetPixels(); // Получаем цвета пикселей (R,G,B в диапазоне [0,1])

            // Параметры нормализации ImageNet
            float[] mean = new float[] { 0.485f, 0.456f, 0.406f };
            float[] std = new float[] { 0.229f, 0.224f, 0.225f };

            int height = inputTexture.height;
            int width = inputTexture.width;
            // Данные для тензора в формате NCHW (NumChannels x Height x Width)
            float[] pixelsData = new float[3 * height * width];

            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Color pixel = pixels[y * width + x];

                    // Нормализация и запись в массив pixelsData в порядке NCHW
                    // Канал R
                    pixelsData[(0 * height * width) + (y * width) + x] = (pixel.r - mean[0]) / std[0];
                    // Канал G
                    pixelsData[(1 * height * width) + (y * width) + x] = (pixel.g - mean[1]) / std[1];
                    // Канал B
                    pixelsData[(2 * height * width) + (y * width) + x] = (pixel.b - mean[2]) / std[2];
                }
            }

            try
            {
                // Используем правильный TensorShape из Unity.Sentis
                return new Tensor<float>(
                    new Unity.Sentis.TensorShape(1, 3, inputTexture.height, inputTexture.width),
                    pixelsData
                );
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[TryCreateXRSimulationTensor] Не удалось создать тензор: {ex.Message}");

                // Пробуем альтернативный способ создания тензора при помощи рефлексии
                Type tensorType = typeof(Tensor<float>);
                var constructors = tensorType.GetConstructors();
                foreach (var ctor in constructors)
                {
                    var parameters = ctor.GetParameters();
                    if (parameters.Length == 2 &&
                        parameters[0].ParameterType == typeof(Unity.Sentis.TensorShape) &&
                        parameters[1].ParameterType == typeof(float[]))
                    {
                        try
                        {
                            return (Tensor<float>)ctor.Invoke(new object[]
                            {
                                new Unity.Sentis.TensorShape(1, 3, inputTexture.height, inputTexture.width),
                                pixelsData
                            });
                        }
                        catch (Exception innerEx)
                        {
                            Debug.LogWarning($"[TryCreateXRSimulationTensor] Ошибка при создании тензора через рефлексию: {innerEx.Message}");
                        }
                    }
                }

                return null;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[WallSegmentation-TryCreateXRSimulationTensor] Ошибка: {ex.Message}");
            return null;
        }
    }

    private Tensor<float> CreateNCHWTensorFromPixels(Texture2D inputTexture)
    {
        return TryCreateXRSimulationTensor(inputTexture);
    }

    private Tensor<float> CreateTensorFromPixels(Texture2D inputTexture)
    {
        return TryCreateXRSimulationTensor(inputTexture);
    }

    private Tensor<float> CreateSingleChannelTensor(Texture2D inputTexture)
    {
        try
        {
            // Создаем одноканальный тензор
            float[] pixelsData = new float[inputTexture.width * inputTexture.height];

            // Преобразуем пиксели в тензор, используя только яркость
            Color[] pixels = inputTexture.GetPixels();
            for (int i = 0; i < pixels.Length; i++)
            {
                pixelsData[i] = pixels[i].grayscale;
            }

            try
            {
                // Используем правильный TensorShape из Unity.Sentis
                return new Tensor<float>(
                    new Unity.Sentis.TensorShape(1, 1, inputTexture.height, inputTexture.width),
                    pixelsData
                );
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[CreateSingleChannelTensor] Не удалось создать тензор: {ex.Message}");

                // Пробуем альтернативный способ создания тензора при помощи рефлексии
                Type tensorType = typeof(Tensor<float>);
                var constructors = tensorType.GetConstructors();
                foreach (var ctor in constructors)
                {
                    var parameters = ctor.GetParameters();
                    if (parameters.Length == 2 &&
                        parameters[0].ParameterType == typeof(Unity.Sentis.TensorShape) &&
                        parameters[1].ParameterType == typeof(float[]))
                    {
                        try
                        {
                            return (Tensor<float>)ctor.Invoke(new object[]
                            {
                                new Unity.Sentis.TensorShape(1, 1, inputTexture.height, inputTexture.width),
                                pixelsData
                            });
                        }
                        catch (Exception innerEx)
                        {
                            Debug.LogWarning($"[CreateSingleChannelTensor] Ошибка при создании тензора через рефлексию: {innerEx.Message}");
                        }
                    }
                }

                return null;
            }
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[CreateSingleChannelTensor] Ошибка: {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// Сохраняет отладочную маску
    /// </summary>
    private void SaveDebugMask()
    {
        // Реализация сохранения отладочной маски
        Debug.Log("[WallSegmentation-SaveDebugMask] Сохранение отладочной маски #" + debugMaskCounter);

        try
        {
            // Создаем директорию, если ее нет
            string dirPath = Path.Combine(Application.persistentDataPath, debugSavePath);
            if (!Directory.Exists(dirPath))
            {
                Directory.CreateDirectory(dirPath);
            }

            // Сохраняем текущее изображение с камеры
            if (cameraTexture != null)
            {
                string cameraFilePath = Path.Combine(dirPath, $"camera_frame_{debugMaskCounter}.png");
                byte[] cameraBytes = cameraTexture.EncodeToPNG();
                File.WriteAllBytes(cameraFilePath, cameraBytes);
                Debug.Log($"[WallSegmentation-SaveDebugMask] ✅ Сохранено изображение с камеры: {cameraFilePath}");
            }

            // Сохраняем текущую маску сегментации
            if (segmentationMaskTexture != null && segmentationMaskTexture.IsCreated())
            {
                RenderTexture prevRT = RenderTexture.active;
                RenderTexture.active = segmentationMaskTexture;

                Texture2D maskCopy = new Texture2D(segmentationMaskTexture.width, segmentationMaskTexture.height, TextureFormat.RGBA32, false);
                maskCopy.ReadPixels(new Rect(0, 0, segmentationMaskTexture.width, segmentationMaskTexture.height), 0, 0);
                maskCopy.Apply();

                RenderTexture.active = prevRT;

                string maskFilePath = Path.Combine(dirPath, $"segmentation_mask_{debugMaskCounter}.png");
                byte[] maskBytes = maskCopy.EncodeToPNG();
                File.WriteAllBytes(maskFilePath, maskBytes);

                Destroy(maskCopy);
                Debug.Log($"[WallSegmentation-SaveDebugMask] ✅ Сохранена маска сегментации: {maskFilePath}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[WallSegmentation-SaveDebugMask] ❌ Ошибка при сохранении отладочных данных: {ex.Message}");
        }
    }

    /// <summary>
    /// Обновление каждый кадр - выполняем сегментацию с использованием текущего изображения с камеры
    /// </summary>
    private void Update()
    {
        bool flowLog = debugFlags.HasFlag(DebugFlags.ExecutionFlow);
        bool detailedLog = debugFlags.HasFlag(DebugFlags.DetailedExecution);

        // if (flowLog && Time.frameCount % 60 == 0) // Логируем не каждый кадр, чтобы не засорять консоль
        // {
        // Debug.Log($"[WallSegmentation-Update-STATUS] Frame: {Time.frameCount}, " +
        // $"isModelInitialized: {isModelInitialized}, " +
        // $"isInitializing: {isInitializing}, " +
        // $"isProcessing: {isProcessing}, " +
        // $"worker is null: {(worker == null)}, " +
        // $"runtimeModel is null: {(runtimeModel == null)}");
        // }

        if (isInitializationFailed)
        {
            // Debug.LogWarning("[WallSegmentation-Update] ⚠️ Модель не инициализирована. Попытка автоматической инициализации...");
            StartCoroutine(InitializeSegmentation());
            return;
        }

        if (!isModelInitialized || ARCameraManager == null)
        {
            // Если модель не инициализирована, но прошло значительное время, попробуем инициализировать ее снова
            if (Time.frameCount % 100 == 0 && !isInitializing && !isModelInitialized)
            {
                // Debug.LogWarning("[WallSegmentation-Update] ⚠️ Модель не инициализирована. Попытка автоматической инициализации...");
                StartCoroutine(InitializeSegmentation());
            }
            return;
        }

        // Получаем изображение с камеры
        bool usingSimulation = false;
        Texture2D cameraPixels = GetCameraTexture();

        if (cameraPixels == null)
        {
            consecutiveFailCount++;
            usingSimulation = true;

            // if (Time.frameCount % 50 == 0) {
            // Debug.LogWarning($"[WallSegmentation-Update] ⚠️ Не удалось получить изображение с камеры (попыток: {consecutiveFailCount})");
            // }

            // Проверяем нужно ли использовать симуляцию
            if (useSimulationIfNoCamera && consecutiveFailCount >= failureThresholdForSimulation)
            {
                if (!usingSimulatedSegmentation)
                {
                    // Debug.Log($"[WallSegmentation-Update] 🔄 Включение режима симуляции после {consecutiveFailCount} неудачных попыток");
                    usingSimulatedSegmentation = true;
                }

                cameraPixels = GetCameraTextureFromSimulation();
                // if (cameraPixels != null)
                // {
                // if (Time.frameCount % 100 == 0) {
                // Debug.Log($"[WallSegmentation-Update] ℹ️ Используется симуляция изображения с камеры (режим: {(usingSimulation ? "симуляция" : "реальное изображение")})");
                // }
                // }
            }
        }
        else
        {
            // Сбрасываем счетчик неудач, если удалось получить изображение
            consecutiveFailCount = 0;
            usingSimulatedSegmentation = false;
            // if (Time.frameCount % 500 == 0) {
            // Debug.Log($"[WallSegmentation-Update] ✅ Получено изображение с камеры (режим: {(usingSimulation ? "симуляция" : "реальное изображение")})");
            // }
        }

        // Если не удалось получить изображение даже из симуляции, выходим
        if (cameraPixels == null)
        {
            // if (Time.frameCount % 100 == 0) {
            // Debug.LogWarning("[WallSegmentation-Update] ❌ Не удалось получить изображение ни с камеры, ни из симуляции");
            // }
            return;
        }

        // Выполняем сегментацию с использованием полученного изображения
        PerformSegmentation(cameraPixels);
    }

    /// <summary>
    /// Пытается получить данные из тензора используя TensorExtensions
    /// </summary>
    private bool TryGetTensorData(Tensor<float> tensor, out float[] data)
    {
        if (tensor == null)
        {
            data = null;
            Debug.LogError("[WallSegmentation-TryGetTensorData] ❌ Тензор равен null.");
            return false;
        }

        try
        {
            // tensor.MakeReadable(); // Этот метод недоступен

            var shape = tensor.shape;
            int batch = shape[0];
            int channels = shape[1];
            int height = shape[2];
            int width = shape[3];

            long length = tensor.shape.length;
            data = new float[length];

            Debug.Log($"[WallSegmentation-TryGetTensorData] Попытка чтения тензора формы ({batch}, {channels}, {height}, {width}). Общая длина: {length}");

            // Прямой доступ к данным, если это возможно (зависит от реализации Tensor<T>)
            // Это наиболее вероятный способ, если ToReadOnlyArray и MakeReadable недоступны.
            // Предполагается, что Tensor<float> реализует доступ по индексу.
            // Это предположение может быть неверным.

            int dataIndex = 0;
            for (int b = 0; b < batch; ++b)
            {
                for (int y = 0; y < height; ++y) // Обычный порядок: сначала высота, потом ширина, потом каналы
                {
                    for (int x = 0; x < width; ++x)
                    {
                        for (int c = 0; c < channels; ++c)
                        {
                            // Порядок индексов может быть другим: b, c, h, w или b, h, w, c
                            // Пробуем самый распространенный вариант: batch, height, width, channels
                            // Или, если это выход модели классификации/сегментации, то batch, channels, height, width
                            try
                            {
                                data[dataIndex++] = tensor[b, c, y, x]; // Предполагаемый порядок: B, C, H, W
                            }
                            catch (Exception e)
                            {
                                // Если прямой доступ вызывает ошибку, логируем и выходим
                                Debug.LogError($"[WallSegmentation-TryGetTensorData] ❌ Ошибка при доступе к элементу тензора [{b},{c},{y},{x}]: {e.Message}");
                                data = null;
                                return false;
                            }
                        }
                    }
                }
            }

            if (dataIndex != length)
            {
                Debug.LogWarning($"[WallSegmentation-TryGetTensorData] ⚠️ Прочитано {dataIndex} элементов, ожидалось {length}. Возможна ошибка в логике чтения.");
                // Можно вернуть false или продолжить с частично прочитанными данными, в зависимости от требований
            }

            Debug.Log($"[WallSegmentation-TryGetTensorData] ✅ Данные тензора успешно прочитаны (прочитано {dataIndex} элементов).");
            return true;
        }
        catch (Exception e)
        {
            Debug.LogError($"[WallSegmentation-TryGetTensorData] ❌ Исключение при попытке получить данные из тензора: {e.Message}\\n{e.StackTrace}");
            data = null;
            return false;
        }
    }

    private bool TryGetDenseTensorData(Tensor<float> tensor, out float[] data)
    {
        // Пока оставляем заглушку для DenseTensor, фокусируемся на базовом Tensor<float>
        data = null;
        Debug.LogWarning("[WallSegmentation-TryGetDenseTensorData] ЗАГЛУШКА: Метод всегда возвращает false, данные тензора не читаются.");
        return false;
    }

    private string GetFullPathToModel(string relativePath)
    {
        if (string.IsNullOrEmpty(relativePath))
        {
            Debug.LogError("[WallSegmentation-GetFullPathToModel] Относительный путь к модели пуст.");
            return null;
        }

        string fullPath = System.IO.Path.Combine(Application.streamingAssetsPath, relativePath);
        if (!System.IO.File.Exists(fullPath))
        {
            Debug.LogWarning($"[WallSegmentation-GetFullPathToModel] Файл модели не найден по пути: {fullPath}");
            // Попытка найти с другим регистром или расширением может быть добавлена здесь, если необходимо
            return null; // Возвращаем null, если файл не найден, чтобы инициализация обработала это
        }
        return fullPath;
    }

    // Вспомогательная сигмоидная функция
    private static float Sigmoid(float value)
    {
        return 1.0f / (1.0f + Mathf.Exp(-value));
    }

    // Новый метод для обработки результатов сегментации
    private void ProcessSegmentationResult(float[] dataArray, TensorShape outputShape)
    {
        bool shouldLogTensorProc = (debugFlags & DebugFlags.TensorProcessing) != 0;
        if (shouldLogTensorProc)
            Debug.Log($"[WallSegmentation-ProcessSegmentationResult] НАЧАЛО. outputShape: {outputShape}, wallClassIndex: {wallClassIndex} (conf: {wallConfidence}), floorClassIndex: {floorClassIndex} (conf: {floorConfidence}), detectFloor: {detectFloor}");

        if (dataArray == null || dataArray.Length == 0)
        {
            if (shouldLogTensorProc)
                Debug.LogError("[WallSegmentation-ProcessSegmentationResult] ❌ Входной массив dataArray ПУСТ или NULL.");
            RenderSimpleMask(); // Попытка отрисовать заглушку
            OnMaskCreated(segmentationMaskTexture); // Отправляем заглушку дальше
            return;
        }

        // outputShape должен быть [batch, classes, height, width]
        int batchSize = outputShape[0];
        int numClasses = outputShape[1];
        int height = outputShape[2];
        int width = outputShape[3];

        if (shouldLogTensorProc)
            Debug.Log($"[WallSegmentation-ProcessSegmentationResult] Размеры: batch={batchSize}, classes={numClasses}, height={height}, width={width}");

        if (batchSize != 1)
        {
            if (shouldLogTensorProc)
                Debug.LogWarning($"[WallSegmentation-ProcessSegmentationResult] ⚠️ Размер батча {batchSize} не равен 1. Обрабатывается только первый элемент.");
        }

        if (wallClassIndex < 0 || wallClassIndex >= numClasses)
        {
            if (shouldLogTensorProc)
                Debug.LogError($"[WallSegmentation-ProcessSegmentationResult] ❌ wallClassIndex ({wallClassIndex}) выходит за пределы допустимых классов ({numClasses}). Обнаружение стен НЕ БУДЕТ работать.");
        }
        if (detectFloor && (floorClassIndex < 0 || floorClassIndex >= numClasses))
        {
            if (shouldLogTensorProc)
                Debug.LogWarning($"[WallSegmentation-ProcessSegmentationResult] ⚠️ floorClassIndex ({floorClassIndex}) выходит за пределы допустимых классов ({numClasses}). Обнаружение пола может не работать.");
        }


        if (segmentationMaskTexture == null || segmentationMaskTexture.width != width || segmentationMaskTexture.height != height || segmentationMaskTexture.format != RenderTextureFormat.ARGB32)
        {
            if (segmentationMaskTexture != null)
            {
                if (shouldLogTensorProc) Debug.Log($"[WallSegmentation-ProcessSegmentationResult] Освобождаем старую segmentationMaskTexture ({segmentationMaskTexture.width}x{segmentationMaskTexture.height}, {segmentationMaskTexture.format})");
                segmentationMaskTexture.Release();
                Destroy(segmentationMaskTexture);
            }
            segmentationMaskTexture = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
            segmentationMaskTexture.enableRandomWrite = true; // Важно для Compute Shaders, но и здесь не помешает
            segmentationMaskTexture.Create();
            if (shouldLogTensorProc)
                Debug.Log($"[WallSegmentation-ProcessSegmentationResult] ✅ Создана или пересоздана segmentationMaskTexture ({width}x{height}, ARGB32).");
        }

        Texture2D tempMaskTexture = new Texture2D(width, height, TextureFormat.RGBA32, false); // ИСПРАВЛЕНО: Возвращено создание Texture2D
        Color32[] pixelColors = new Color32[width * height];

        int pixelsDetectedAsWall = 0;
        int pixelsDetectedAsFloor = 0;
        int wallDataIndexErrors = 0;
        int floorDataIndexErrors = 0;

        // ОТЛАДКА: Проверим первые несколько значений из dataArray
        if (shouldLogTensorProc && dataArray.Length > 0)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            sb.Append("[WallSegmentation-ProcessSegmentationResult] Первые ~10 значений dataArray: ");
            for (int i = 0; i < Mathf.Min(dataArray.Length, 10 * numClasses); i += numClasses) // Шаг numClasses, чтобы посмотреть на разные пиксели
            {
                int currentPixelIndex = i / numClasses;
                float currentWallScore = 0f;
                float currentFloorScore = 0f;
                bool wallScoreAvailable = false;
                bool floorScoreAvailable = false;

                if (wallClassIndex >=0 && wallClassIndex < numClasses && (i+wallClassIndex) < dataArray.Length)
                {
                   currentWallScore = dataArray[i+wallClassIndex];
                   wallScoreAvailable = true;
                   sb.Append($"px{currentPixelIndex}_wall({wallClassIndex}):{currentWallScore:F3} ");
                }
                if (detectFloor && floorClassIndex >=0 && floorClassIndex < numClasses && (i+floorClassIndex) < dataArray.Length)
                {
                   currentFloorScore = dataArray[i+floorClassIndex];
                   floorScoreAvailable = true;
                   sb.Append($"px{currentPixelIndex}_floor({floorClassIndex}):{currentFloorScore:F3} ");
                }

                float wallProbability = wallScoreAvailable ? Sigmoid(currentWallScore) : 0f;
                float floorProbability = detectFloor && floorScoreAvailable ? Sigmoid(currentFloorScore) : 0f;
                
                // Добавляем вероятности в ту же строку лога
                sb.Append($"wallProb:{wallProbability:F4} floorProb:{floorProbability:F4} ");
            }
            Debug.Log(sb.ToString());
        }


        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte rChannelValue = 0;
                byte gChannelValue = 0;
                byte bChannelValue = 0; // Для отладки можно что-то сюда писать
                byte aChannelValue = 0; // По умолчанию полностью прозрачный

                // Обработка стен (красный канал)
                if (wallClassIndex >= 0 && wallClassIndex < numClasses)
                {
                    // Индекс в плоском массиве dataArray. Данные идут сначала все значения для одного класса, потом для следующего.
                    // Т.е. [class0_pixel0, class0_pixel1, ..., class1_pixel0, class1_pixel1, ...]
                    int wallDataIndex = (wallClassIndex * height * width) + (y * width) + x;
                    if (wallDataIndex < dataArray.Length)
                    {
                        float wallLogit = dataArray[wallDataIndex];
                        // ПРЕОБРАЗОВАНИЕ: Если модель уже выдает вероятность (0..1), то sigmoid не нужен.
                        // Если модель выдает логиты (сырые значения), то нужен sigmoid.
                        float wallProbability = Sigmoid(wallLogit);
                        // float wallProbability = wallLogit; // Если уже вероятность

                        if (shouldLogTensorProc && y < 2 && x < 5) // Логируем только для нескольких первых пикселей для примера
                        {
                            Debug.Log($"[ProcessSegmentationResult] Pixel({x},{y}) wallProb: {wallProbability:F4}");
                        }

                        if (wallProbability > wallConfidence)
                        {
                            rChannelValue = 255;
                            aChannelValue = 255; // Делаем непрозрачным, если есть стена
                            pixelsDetectedAsWall++;
                        }
                    }
                    else if (wallDataIndexErrors == 0) // Логируем ошибку индекса только один раз
                    {
                        Debug.LogError($"[WallSegmentation-ProcessSegmentationResult] ❌ wallDataIndex ({wallDataIndex}) выходит за пределы dataArray ({dataArray.Length}). Больше не логгируем.");
                        wallDataIndexErrors++;
                    }
                }

                // Обработка пола (зеленый канал)
                if (detectFloor && floorClassIndex >= 0 && floorClassIndex < numClasses)
                {
                    int floorDataIndex = (floorClassIndex * height * width) + (y * width) + x;
                    if (floorDataIndex < dataArray.Length)
                    {
                        float floorLogit = dataArray[floorDataIndex];
                        // float floorProbability = 1.0f / (1.0f + Mathf.Exp(-floorLogit)); // Sigmoid
                        float floorProbability = Sigmoid(floorLogit); // Sigmoid

                        if (shouldLogTensorProc && y < 2 && x < 5) // Логируем только для нескольких первых пикселей для примера
                        {
                             Debug.Log($"[ProcessSegmentationResult] Pixel({x},{y}) floorProb: {floorProbability:F4}");
                        }

                        if (floorProbability > floorConfidence)
                        {
                            gChannelValue = 255;
                            if (rChannelValue == 0) aChannelValue = 255; // Делаем непрозрачным, если есть пол (и нет стены)
                            pixelsDetectedAsFloor++;
                        }
                    }
                    else if (floorDataIndexErrors == 0) // Логируем ошибку индекса только один раз
                    {
                        Debug.LogError($"[WallSegmentation-ProcessSegmentationResult] ❌ floorDataIndex ({floorDataIndex}) выходит за пределы dataArray ({dataArray.Length}). Больше не логгируем.");
                        floorDataIndexErrors++;
                    }
                }
                pixelColors[y * width + x] = new Color32(rChannelValue, gChannelValue, bChannelValue, aChannelValue);
            }
        }

        if (shouldLogTensorProc)
        {
            Debug.Log($"[WallSegmentation-ProcessSegmentationResult] Статистика пикселей: Стены={pixelsDetectedAsWall}, Пол={pixelsDetectedAsFloor} (из {width * height} всего). Ошибок индекса стен: {wallDataIndexErrors}, пола: {floorDataIndexErrors}");
            // ОТЛАДКА: Проверим несколько пикселей из pixelColors
            if (pixelColors.Length > 0 && pixelsDetectedAsWall > 0)
            {
                System.Text.StringBuilder sbColor = new System.Text.StringBuilder();
                sbColor.Append("[WallSegmentation-ProcessSegmentationResult] Первые ~5 обработанных пикселей (RGBA): ");
                for (int i = 0; i < Mathf.Min(pixelColors.Length, 5); i++)
                {
                    sbColor.Append($"({pixelColors[i].r},{pixelColors[i].g},{pixelColors[i].b},{pixelColors[i].a}) ");
                }
                Debug.Log(sbColor.ToString());
            }
        }

        tempMaskTexture.SetPixels32(pixelColors);
        tempMaskTexture.Apply();

        RenderTexture prevRT = RenderTexture.active;
        RenderTexture.active = segmentationMaskTexture;
        GL.Clear(true, true, Color.clear); // Очищаем RenderTexture перед копированием в нее
        Graphics.Blit(tempMaskTexture, segmentationMaskTexture);
        RenderTexture.active = prevRT;

        Destroy(tempMaskTexture); // ИСПРАВЛЕНО: Возвращено Destroy для Texture2D

        if (shouldLogTensorProc)
            Debug.Log($"[WallSegmentation-ProcessSegmentationResult] ✅ Маска обновлена в segmentationMaskTexture. Вызов OnMaskCreated.");

#if REAL_MASK_PROCESSING_DISABLED
        Debug.LogWarning("[WallSegmentation-ProcessSegmentationResult] REAL_MASK_PROCESSING_DISABLED активен. Реальная маска не будет использована.");
        RenderSimpleMask(); // Если мы хотим показать заглушку вместо реальной маски
#endif

        OnMaskCreated(segmentationMaskTexture); // Передаем обновленную маску дальше
    }

    private void SaveTextureAsPNG(RenderTexture rt, string directoryPath, string fileName)
    {
        // ... existing code ...
    }

    /// <summary>
    /// Применяет улучшения к маске сегментации для повышения визуального качества
    /// </summary>
    private RenderTexture EnhanceSegmentationMask(RenderTexture inputMask)
    {
        if (inputMask == null || !inputMask.IsCreated())
            return inputMask;

        // Если улучшения отключены, возвращаем исходную маску
        if (!applyMaskSmoothing && !enhanceEdges && !enhanceContrast)
            return inputMask;

        // Создаем временные текстуры для обработки
        RenderTexture tempRT1 = RenderTexture.GetTemporary(inputMask.width, inputMask.height, 0, inputMask.format);
        RenderTexture tempRT2 = RenderTexture.GetTemporary(inputMask.width, inputMask.height, 0, inputMask.format);

        try
        {
            // Проверяем, доступен ли материал для эффектов постобработки
            if (segmentationMaterial == null)
            {
                try
                {
                    // Пытаемся создать материал на лету
                    Shader shader = Shader.Find("Hidden/SegmentationPostProcess");
                    if (shader != null)
                    {
                        segmentationMaterial = new Material(shader);
                        Debug.Log("[WallSegmentation] ✅ Создан новый материал для постобработки");
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[WallSegmentation] ⚠️ Не удалось создать материал на лету: {e.Message}");
                }
            }

            // Копируем исходную маску во временную текстуру
            Graphics.Blit(inputMask, tempRT1);

            // Применяем постобработку с использованием шейдера, если он доступен
            if (segmentationMaterial != null)
            {
                // Применяем размытие по Гауссу (Pass 1)
                if (applyMaskSmoothing)
                {
                    segmentationMaterial.SetFloat("_BlurSize", maskBlurSize);
                    Graphics.Blit(tempRT1, tempRT2, segmentationMaterial, 1); // Pass 1: Blur
                    // Меняем местами текстуры (результат в tempRT1)
                    RenderTexture temp = tempRT1;
                    tempRT1 = tempRT2;
                    tempRT2 = temp;
                }

                // Повышаем резкость (Pass 2)
                if (enhanceEdges)
                {
                    Graphics.Blit(tempRT1, tempRT2, segmentationMaterial, 2); // Pass 2: Sharpen
                    // Меняем местами текстуры (результат в tempRT1)
                    RenderTexture temp = tempRT1;
                    tempRT1 = tempRT2;
                    tempRT2 = temp;
                }

                // Повышаем контраст (Pass 3)
                if (enhanceContrast)
                {
                    segmentationMaterial.SetFloat("_Contrast", contrastMultiplier);
                    Graphics.Blit(tempRT1, tempRT2, segmentationMaterial, 3); // Pass 3: Contrast
                    // Меняем местами текстуры (результат в tempRT1)
                    RenderTexture temp = tempRT1;
                    tempRT1 = tempRT2;
                    tempRT2 = temp;
                }
            }
            else
            {
                // Если шейдер недоступен, используем индивидуальные методы
                // Применяем сглаживание, если оно включено
                if (applyMaskSmoothing)
                {
                    ApplyGaussianBlur(tempRT1, tempRT2, maskBlurSize);
                    // Меняем местами текстуры (результат в tempRT1)
                    RenderTexture temp = tempRT1;
                    tempRT1 = tempRT2;
                    tempRT2 = temp;
                }

                // Повышаем резкость краев, если включено
                if (enhanceEdges)
                {
                    ApplySharpen(tempRT1, tempRT2);
                    // Меняем местами текстуры (результат в tempRT1)
                    RenderTexture temp = tempRT1;
                    tempRT1 = tempRT2;
                    tempRT2 = temp;
                }

                // Повышаем контраст, если включено
                if (enhanceContrast)
                {
                    ApplyContrast(tempRT1, tempRT2, contrastMultiplier);
                    // Меняем местами текстуры (результат в tempRT1)
                    RenderTexture temp = tempRT1;
                    tempRT1 = tempRT2;
                    tempRT2 = temp;
                }
            }

            // Важно: не создаем новую текстуру каждый раз, это приводит к утечкам
            // Вместо этого модифицируем входную текстуру и возвращаем её
            Graphics.Blit(tempRT1, inputMask);
            return inputMask;
        }
        catch (Exception e)
        {
            Debug.LogError($"[WallSegmentation] ❌ Ошибка при улучшении маски: {e.Message}");
            // В случае ошибки просто вернуть исходную маску
            return inputMask;
        }
        finally
        {
            // Освобождаем временные текстуры
            RenderTexture.ReleaseTemporary(tempRT1);
            RenderTexture.ReleaseTemporary(tempRT2);
        }
    }

    /// <summary>
    /// Применяет размытие по Гауссу к текстуре
    /// </summary>
    private void ApplyGaussianBlur(RenderTexture source, RenderTexture destination, int blurSize)
    {
        try
        {
            // Используем Material для размытия, если он есть
            if (segmentationMaterial != null)
            {
                // Сохраняем оригинальное значение ключевого слова
                bool originalValue = segmentationMaterial.IsKeywordEnabled("_GAUSSIAN_BLUR");

                // Активируем ключевое слово для размытия
                segmentationMaterial.EnableKeyword("_GAUSSIAN_BLUR");
                segmentationMaterial.SetFloat("_BlurSize", blurSize);

                // Применяем шейдер
                Graphics.Blit(source, destination, segmentationMaterial);

                // Восстанавливаем оригинальное значение
                if (!originalValue)
                    segmentationMaterial.DisableKeyword("_GAUSSIAN_BLUR");
            }
            else
            {
                // Если нет материала, просто копируем источник в назначение
                Graphics.Blit(source, destination);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[WallSegmentation] Ошибка при применении размытия: {e.Message}. Используем простое копирование.");
            Graphics.Blit(source, destination);
        }
    }

    /// <summary>
    /// Повышает резкость текстуры для выделения краев
    /// </summary>
    private void ApplySharpen(RenderTexture source, RenderTexture destination)
    {
        try
        {
            // Используем Material для повышения резкости, если он есть
            if (segmentationMaterial != null)
            {
                // Сохраняем оригинальное значение ключевого слова
                bool originalValue = segmentationMaterial.IsKeywordEnabled("_SHARPEN");

                // Активируем ключевое слово для повышения резкости
                segmentationMaterial.EnableKeyword("_SHARPEN");

                // Применяем шейдер
                Graphics.Blit(source, destination, segmentationMaterial);

                // Восстанавливаем оригинальное значение
                if (!originalValue)
                    segmentationMaterial.DisableKeyword("_SHARPEN");
            }
            else
            {
                // Если нет материала, просто копируем источник в назначение
                Graphics.Blit(source, destination);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[WallSegmentation] Ошибка при повышении резкости: {e.Message}. Используем простое копирование.");
            Graphics.Blit(source, destination);
        }
    }

    /// <summary>
    /// Повышает контраст текстуры
    /// </summary>
    private void ApplyContrast(RenderTexture source, RenderTexture destination, float contrast)
    {
        try
        {
            // Используем Material для повышения контраста, если он есть
            if (segmentationMaterial != null)
            {
                // Сохраняем оригинальное значение ключевого слова
                bool originalValue = segmentationMaterial.IsKeywordEnabled("_CONTRAST");

                // Активируем ключевое слово для повышения контраста
                segmentationMaterial.EnableKeyword("_CONTRAST");
                segmentationMaterial.SetFloat("_Contrast", contrast);

                // Применяем шейдер
                Graphics.Blit(source, destination, segmentationMaterial);

                // Восстанавливаем оригинальное значение
                if (!originalValue)
                    segmentationMaterial.DisableKeyword("_CONTRAST");
            }
            else
            {
                // Если нет материала, просто копируем источник в назначение
                Graphics.Blit(source, destination);
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[WallSegmentation] Ошибка при повышении контраста: {e.Message}. Используем простое копирование.");
            Graphics.Blit(source, destination);
        }
    }
}