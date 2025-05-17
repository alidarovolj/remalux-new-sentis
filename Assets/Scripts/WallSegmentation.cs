using UnityEngine;
using System.Collections;
using UnityEngine.XR.ARFoundation;
using System;
using System.IO;
using UnityEngine.XR.ARSubsystems;
using Unity.Collections;
using System.Reflection;
using System.Linq;
using UnityEngine.Networking;
using Unity.Sentis;
using Unity.XR.CoreUtils;
using System.Collections.Generic;
// Add necessary import for unsafe code
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Unity.Collections.LowLevel.Unsafe;
using UnityEngine.Rendering;
using UnityEngine.UI;
// Используем просто Unity.Sentis без псевдонима
// using Unity.Sentis.TensorShape = Unity.Sentis.TensorShape;

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
    [Tooltip("Индекс класса стены в модели")]
    public int wallClassIndex = 2;

    [Tooltip("Индекс класса пола в модели")]
    public int floorClassIndex = 12;

    [Tooltip("Порог вероятности для определения стены")]
    public float wallThreshold = 0.3f;

    [Tooltip("Порог вероятности для определения пола")]
    public float floorThreshold = 0.3f;

    [Tooltip("Обнаруживать также горизонтальные поверхности (пол)")]
    public bool detectFloor = true;

    [Tooltip("Разрешение входного изображения")]
    public Vector2Int inputResolution = new Vector2Int(320, 320);
    
    [Tooltip("Использовать симуляцию, если не удаётся получить изображение с камеры")]
    public bool useSimulationIfNoCamera = true;
    
    [Tooltip("Количество неудачных попыток получения изображения перед включением симуляции")]
    public int failureThresholdForSimulation = 10;

    [Header("Компоненты")]
    [Tooltip("Ссылка на ARSessionManager")]
    public ARSessionManager arSessionManager;

    [Tooltip("Ссылка на XROrigin")]
    public XROrigin xrOrigin;

    [Tooltip("Текстура для вывода маски сегментации")]
    public RenderTexture segmentationMaskTexture;

    // Свойства для получения AR компонентов
    public ARSessionManager ARSessionManager {
        get {
            if (arSessionManager == null) {
                arSessionManager = FindObjectOfType<ARSessionManager>();
            }
            return arSessionManager;
        }
        set {
            arSessionManager = value;
        }
    }

    public XROrigin XROrigin {
        get {
            if (xrOrigin == null) {
                xrOrigin = FindObjectOfType<XROrigin>();
            }
            return xrOrigin;
        }
        set {
            xrOrigin = value;
        }
    }

    public ARCameraManager ARCameraManager {
        get {
            if (XROrigin != null && XROrigin.Camera != null) {
                return XROrigin.Camera.GetComponent<ARCameraManager>();
            }
            return FindObjectOfType<ARCameraManager>();
        }
        set {
            // Тут мы можем сохранить ссылку на ARCameraManager, если нужно
            // Но так как в getter мы его получаем динамически, создадим приватное поле
            arCameraManager = value;
        }
    }

    [Header("Отладка")]
    public bool saveDebugMask = false;

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
    private const int REQUIRED_STABLE_FRAMES = 3; // Количество стабильных кадров для принятия новой маски
    
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
    
    // Вызываем событие при создании маски сегментации
    private void OnMaskCreated(RenderTexture mask)
    {
        if (mask != null)
        {
            TriggerSegmentationMaskUpdatedEvent(mask);
        }
    }

    /// <summary>
    /// Освобождает ресурсы при уничтожении объекта
    /// </summary>
    private void OnDestroy()
    {
        Debug.Log("[WallSegmentation-OnDestroy] Очистка ресурсов Sentis...");

        // Отписываемся от событий
        Debug.Log("[WallSegmentation-OnDestroy] Отписка от событий AR...");

        // Освобождаем текстуру
        if (segmentationMaskTexture != null)
        {
            segmentationMaskTexture.Release();
            Debug.Log("[WallSegmentation-OnDestroy] Освобождена текстура сегментации");
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
        Debug.Log("Начинаем безопасную загрузку модели...");

        // Проверяем, инициализирован ли Sentis
        if (!SentisInitializer.IsInitialized)
        {
            Debug.Log("Инициализируем Unity Sentis...");
            // var sentisInitializer = FindObjectOfType<SentisInitializer>(); // Больше не ищем так
            // if (sentisInitializer == null)
            // {
            // sentisInitializer = gameObject.AddComponent<SentisInitializer>(); // Больше не добавляем компонент сюда
            // }

            // Используем статический экземпляр SentisInitializer,
            // который сам позаботится о своем создании как корневого объекта
            var sentisInitializerInstance = SentisInitializer.Instance;
            if (!SentisInitializer.IsInitialized) // Проверяем снова, т.к. Instance мог запустить инициализацию
            {
                var initAsync = sentisInitializerInstance.InitializeAsync();
                yield return initAsync;
            }
        }

        // Проверяем, есть ли уже загруженная модель в ModelLoader
        var modelLoader = FindObjectOfType<WallSegmentationModelLoader>();
        if (modelLoader != null && modelLoader.IsModelLoaded)
        {
            model = modelLoader.GetLoadedModel() as Model;
            if (model != null)
            {
                Debug.Log("Использую модель, уже загруженную в WallSegmentationModelLoader");
                runtimeModel = model; // Устанавливаем runtimeModel
                isModelInitialized = true;
            }
        }

        // Если ARSessionManager не назначен, ищем его
        if (arSessionManager == null)
        {
            arSessionManager = FindObjectOfType<ARSessionManager>();
            if (arSessionManager != null && xrOrigin == null)
            {
                xrOrigin = arSessionManager.xrOrigin;
                Debug.Log("ARSessionManager найден и XROrigin получен из него");
            }
        }

        // Если модель НЕ была получена от загрузчика, пробуем загрузить самостоятельно
        if (!isModelInitialized)
        {
            // Пробуем оба формата (.sentis и .onnx)
            string[] fileExtensionsToTry = new string[] { ".sentis", ".onnx" };
            bool modelLoaded = false;

            foreach (string extension in fileExtensionsToTry)
            {
                string fileName = "model" + extension;
                string modelPath = Path.Combine(Application.streamingAssetsPath, fileName);
                string modelUrl = extension == ".sentis" ?
                    "file://" + modelPath.Replace('\\', '/') :
                    "file://" + modelPath.Replace('\\', '/');

                Debug.Log($"Загружаем модель из: {modelUrl}");

                // Проверяем существование файла
                bool fileExists = File.Exists(modelPath);
                if (!fileExists && !modelUrl.StartsWith("http"))
                {
                    Debug.LogWarning($"Файл модели не найден: {modelPath}, пропускаем");
                    continue;
                }

                UnityWebRequest www = UnityWebRequest.Get(modelUrl);
                yield return www.SendWebRequest();

                if (www.result != UnityWebRequest.Result.Success)
                {
                    Debug.LogWarning($"Не удалось загрузить {fileName}: {www.error}");
                    continue;
                }

                byte[] modelData = www.downloadHandler.data;
                Debug.Log($"Модель успешно загружена, размер: {modelData.Length} байт");

                try
                {
                    // Загружаем модель в зависимости от типа файла
                    if (extension == ".sentis")
                    {
                        using (var ms = new MemoryStream(modelData))
                        {
                            var loadedModel = ModelLoader.Load(ms);
                            if (loadedModel != null)
                            {
                                model = loadedModel;
                                runtimeModel = loadedModel; // Устанавливаем runtimeModel
                                Debug.Log("Модель успешно загружена через ModelLoader (.sentis)");
                                isModelInitialized = true;
                                modelLoaded = true;
                                break;
                            }
                        }
                    }
                    else // .onnx
                    {
                        using (var ms = new MemoryStream(modelData))
                        {
                            try
                            {
                                var loadedModel = ModelLoader.Load(ms);
                                if (loadedModel != null)
                                {
                                    model = loadedModel;
                                    runtimeModel = loadedModel; // Устанавливаем runtimeModel
                                    Debug.Log("Модель успешно загружена через ModelLoader (.onnx)");
                                    isModelInitialized = true;
                                    modelLoaded = true;
                                    break;
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning($"Не удалось загрузить ONNX через Stream: {ex.Message}");

                                // Пробуем через временный файл
                                string tempFilePath = Path.Combine(Application.temporaryCachePath, "temp_model.onnx");
                                try
                                {
                                    File.WriteAllBytes(tempFilePath, modelData);
                                    var tempModel = ModelLoader.Load(tempFilePath);
                                    if (tempModel != null)
                                    {
                                        model = tempModel;
                                        runtimeModel = tempModel; // Устанавливаем runtimeModel
                                        Debug.Log("Модель успешно загружена через временный файл");
                                        isModelInitialized = true;
                                        modelLoaded = true;
                                        break;
                                    }
                                }
                                catch (Exception e)
                                {
                                    Debug.LogWarning($"Ошибка загрузки через временный файл: {e.Message}");
                                }
                                finally
                                {
                                    if (File.Exists(tempFilePath))
                                    {
                                        File.Delete(tempFilePath);
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"Исключение при загрузке модели {extension}: {e.Message}");
                }
            }

            // Если модель все еще не загружена после попыток загрузки
            if (!modelLoaded)
            {
                Debug.LogError("Не удалось загрузить модель ни в одном формате и не найдена в ModelLoader.");
                isInitializationFailed = true; // Помечаем ошибку
                yield break; // Выходим, если модель так и не получена
            }
        }

        // ----- Создание Worker ----- 
        // Этот блок теперь выполняется ВСЕГДА, если isModelInitialized = true (неважно, откуда модель)
        if (isModelInitialized && model != null)
        {
            try
            {
                // Проверяем старый worker и освобождаем
                DisposeEngine();

                // Создаем worker для модели через прямой конструктор Worker
                Type workerType = Type.GetType("Unity.Sentis.Worker, Unity.Sentis");
                if (workerType == null)
                {
                    Debug.LogError("Тип Unity.Sentis.Worker не найден");
                    isModelInitialized = false;
                    yield break;
                }

                // Получаем BackendType.CPU (0) или BackendType.GPUCompute (1)
                Type backendType = Type.GetType("Unity.Sentis.BackendType, Unity.Sentis");
                // Если BackendType не найден, попробуем DeviceType
                Type deviceType = null;
                if (backendType == null)
                {
                    deviceType = Type.GetType("Unity.Sentis.DeviceType, Unity.Sentis");
                    if (deviceType == null)
                    {
                        Debug.LogError("Ни BackendType, ни DeviceType не найдены");
                        isModelInitialized = false;
                        yield break;
                    }
                }

                // Пробуем также найти класс WorkerFactory, который используется в новых версиях Sentis
                Type workerFactoryType = Type.GetType("Unity.Sentis.WorkerFactory, Unity.Sentis");
                bool useWorkerFactory = false;
                
                if (workerFactoryType != null)
                {
                    Debug.Log("Найден WorkerFactory, будем использовать его для создания Worker");
                    useWorkerFactory = true;
                    
                    try
                    {
                        // Попробуем использовать WorkerFactory.CreateWorker
                        // 1. Найдем подходящий статический метод
                        var createWorkerMethod = workerFactoryType.GetMethod("CreateWorker", 
                            BindingFlags.Public | BindingFlags.Static, 
                            null, 
                            new Type[] { model.GetType(), backendType ?? deviceType }, 
                            null);
                            
                        if (createWorkerMethod != null)
                        {
                            object typeEnum = backendType != null ? 
                                Enum.ToObject(backendType, preferredBackend) : 
                                Enum.ToObject(deviceType, preferredBackend);
                            
                            worker = createWorkerMethod.Invoke(null, new object[] { model, typeEnum }) as Worker;
                            
                            if (worker != null)
                            {
                                Debug.Log("Worker успешно создан через WorkerFactory.CreateWorker");
                                this.engine = this.worker;
                                isModelInitialized = true;
                                if (OnModelInitialized != null) OnModelInitialized.Invoke();
                                yield break; // Успешно, выходим из метода
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"Не удалось создать Worker через WorkerFactory: {e.Message}. Пробуем альтернативные методы.");
                        useWorkerFactory = false;
                    }
                }

                // Если WorkerFactory не помог или его нет, используем прямые конструкторы
                if (!useWorkerFactory)
                {
                // Проверяем есть ли конструктор с Model и BackendType
                ConstructorInfo constructor = null;
                object typeEnum = null;

                if (backendType != null)
                {
                    constructor = workerType.GetConstructor(new Type[] { model.GetType(), backendType });
                    typeEnum = Enum.ToObject(backendType, preferredBackend); // используем preferredBackend
                }

                // Если конструктор с BackendType не найден, пробуем с DeviceType
                if (constructor == null && deviceType != null)
                {
                    constructor = workerType.GetConstructor(new Type[] { model.GetType(), deviceType });
                    typeEnum = Enum.ToObject(deviceType, preferredBackend); // используем preferredBackend как DeviceType
                }

                if (constructor == null)
                {
                    Debug.LogError("Подходящий конструктор Worker не найден");
                    isModelInitialized = false;
                    yield break;
                }

                // Создаем worker через найденный конструктор
                object workerInstance = constructor.Invoke(new object[] { model, typeEnum });
                worker = workerInstance as Worker;

                if (worker != null)
                {
                    Debug.Log("Worker успешно создан через конструктор Worker");
                    this.engine = this.worker;
                    isModelInitialized = true; // Устанавливаем флаг в true после успешного создания worker
                        
                        // Анализируем доступные методы для выявления правильного API
                        var methods = worker.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance);
                        Debug.Log($"Доступные методы Worker ({methods.Length}):");
                        
                        bool hasExecute = false;
                        bool hasExecuteAndWait = false;
                        bool hasPeekOutput = false;
                        bool hasCopyOutput = false;
                        
                        List<string> methodList = new List<string>();
                        
                        foreach (var method in methods)
                        {
                            if (methodList.Count < 10) {
                                methodList.Add(method.Name);
                            }
                            
                            if (method.Name == "Execute") hasExecute = true;
                            if (method.Name == "ExecuteAndWaitForCompletion") hasExecuteAndWait = true;
                            if (method.Name == "PeekOutput") hasPeekOutput = true;
                            if (method.Name == "CopyOutput") hasCopyOutput = true;
                        }
                        
                        Debug.Log($"Первые методы Worker: {string.Join(", ", methodList)}");
                        Debug.Log($"Проверка ключевых методов: Execute={hasExecute}, ExecuteAndWaitForCompletion={hasExecuteAndWait}, PeekOutput={hasPeekOutput}, CopyOutput={hasCopyOutput}");
                        
                    if (OnModelInitialized != null) OnModelInitialized.Invoke();
                }
                else
                {
                    Debug.LogError("Не удалось создать worker для модели");
                    isModelInitialized = false;
                    isInitializationFailed = true;
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Ошибка при создании worker: {e.Message}");
                isModelInitialized = false;
                isInitializationFailed = true;
            }
        }
        else // Сюда попадаем, если модель = null или isModelInitialized = false
        {
            Debug.LogError("Не удалось инициализировать модель: Модель не была успешно загружена или получена.");
            isInitializationFailed = true;
        }

        // 6. Все проверки пройдены, модель инициализирована успешно
        isModelInitialized = true;
        isInitializing = false;
        isInitializationFailed = false;
        lastErrorMessage = null;
        
        Debug.Log("Сегментация стен успешно инициализирована");
        
        // Анализируем структуру модели для отладки
        AnalyzeModelStructure();
        
        // Логируем информацию о модели
        LogModelInfo();
        
        // Вызываем событие инициализации
        OnModelInitialized?.Invoke();
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
            return true; // Удален лог
        }

        // Проверка на имя сцены
        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (sceneName.Contains("Simulation") || sceneName.Contains("XR"))
        {
            return true; // Удален лог
        }

        // Проверка на редактор Unity
#if UNITY_EDITOR
        // Проверяем наличие компонентов симуляции в сцене
        var simulationObjects = FindObjectsOfType<MonoBehaviour>()
            .Where(mb => mb.GetType().Name.Contains("Simulation"))
            .ToArray();

        if (simulationObjects.Length > 0)
        {
            return true; // Удален лог
        }

        // Проверяем, есть ли объект "XRSimulationEnvironment" в сцене
        var simEnvObjects = FindObjectsOfType<Transform>()
            .Where(t => t.name.Contains("XRSimulation"))
            .ToArray();

        if (simEnvObjects.Length > 0)
        {
            return true; // Удален лог
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
                        return true; // Удален лог
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
            return true; // Удален лог
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
            if (result == null) {
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
            if (Time.frameCount % 30 == 0) {
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
            if (Time.frameCount % 60 == 0) {
                Debug.LogError("[WallSegmentation-GetCameraTextureFromSimulation] ❌ Не удалось найти камеру для получения изображения");
            }
            return null;
        }
        
        // Сохраняем текущий culling mask и очистку
        int originalCullingMask = arCamera.cullingMask;
        CameraClearFlags originalClearFlags = arCamera.clearFlags;
        
        
        // Временно устанавливаем параметры для рендеринга всего
        arCamera.cullingMask = -1; // Все слои
        arCamera.clearFlags = CameraClearFlags.SolidColor;
        
        
        // Выполняем рендеринг
        arCamera.Render();
        
        // Восстанавливаем оригинальные параметры очистки
        arCamera.clearFlags = originalClearFlags;
        
        // Проверяем, был ли оригинальный cullingMask установлен на Everything (-1)
        // Если нет, оставляем Everything (-1), иначе восстанавливаем
        if (originalCullingMask != -1)
        {
        }
        else 
        {
            // Восстанавливаем оригинальный cullingMask
            arCamera.cullingMask = originalCullingMask;
        }
        
        // Создаем RenderTexture для захвата изображения
        RenderTexture rt = RenderTexture.GetTemporary(inputResolution.x, inputResolution.y, 24);
        RenderTexture prevRT = RenderTexture.active;
        RenderTexture.active = rt;
        arCamera.targetTexture = rt;
        
        // Повторно рендерим с правильными параметрами
        arCamera.Render();
        
        // Копируем изображение в нашу текстуру
        if (cameraTexture == null)
        {
            Debug.LogWarning("[WallSegmentation-GetCameraTextureFromSimulation] ⚠️ cameraTexture была null, создаем новую");
            cameraTexture = new Texture2D(inputResolution.x, inputResolution.y, TextureFormat.RGBA32, false);
        }
        
        cameraTexture.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        cameraTexture.Apply();
        
        // Восстанавливаем состояние
        arCamera.targetTexture = null;
        RenderTexture.active = prevRT;
        RenderTexture.ReleaseTemporary(rt);
        
        
        // Добавляем дамп состояния, чтобы увидеть, в каком состоянии система
        this.DumpCurrentState();
        
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
        // Логирование только раз в 50 кадров
        bool shouldLog = Time.frameCount % 50 == 0;
        
        if (shouldLog) {
        Debug.Log($"[WallSegmentation-PerformSegmentation] 🔄 Начало сегментации, размер входного изображения: {inputTexture.width}x{inputTexture.height}");
        }
        
        // Переменная для хранения созданного тензора, для последующего освобождения ресурсов
        Tensor<float> inputTensor = null;
        bool segmentationSuccess = false;
        
        try
        {
            // Убедимся, что текстура имеет нужные размеры
            if (inputTexture.width != inputResolution.x || inputTexture.height != inputResolution.y)
            {
                Debug.Log($"[WallSegmentation-PerformSegmentation] 🔄 Изменяем размер входной текстуры с {inputTexture.width}x{inputTexture.height} на {inputResolution.x}x{inputResolution.y}");

                // Создаем временную RenderTexture для изменения размера
                RenderTexture tempRT = RenderTexture.GetTemporary(inputResolution.x, inputResolution.y, 0, RenderTextureFormat.ARGB32);
                Graphics.Blit(inputTexture, tempRT);

                // Создаем новую текстуру нужного размера
                Texture2D resizedTexture = new Texture2D(inputResolution.x, inputResolution.y, TextureFormat.RGBA32, false);
                RenderTexture.active = tempRT;
                resizedTexture.ReadPixels(new Rect(0, 0, inputResolution.x, inputResolution.y), 0, 0);
                resizedTexture.Apply();

                // Освобождаем временную текстуру
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(tempRT);

                // Используем измененную текстуру вместо оригинальной
                inputTexture = resizedTexture;
            }

            // ВАЖНО: Сначала сохраняем отладочные изображения, если нужно
            if (saveDebugMask)
            {
                Debug.Log("[WallSegmentation-PerformSegmentation] 🔄 Сохраняем входное изображение для отладки");
                SaveDebugMask();
            }

            // Удален частый лог о создании тензора
            
            // Проверяем каждый способ создания тензора, пока один не сработает
            // Попытка 1: XR симуляция, если доступна
            inputTensor = TryCreateXRSimulationTensor(inputTexture);
            if (inputTensor != null)
            {
                if (shouldLog) {
                Debug.Log("[WallSegmentation-PerformSegmentation] ✅ Успешно создан тензор через XR симуляцию");
                }
                ExecuteModelAndProcessResult(inputTensor);
                // Не освобождаем тензор здесь - это будет сделано в ExecuteModelAndProcessResult
                inputTensor = null;
                segmentationSuccess = true;
                return;
            }

            // Попытка 2: 4-канальный NCHW тензор 
            if (inputTensor == null)
            {
                if (shouldLog) {
                Debug.Log("[WallSegmentation-PerformSegmentation] 🔄 Пробуем создать NCHW тензор...");
                }
                inputTensor = CreateNCHWTensorFromPixels(inputTexture);
                if (inputTensor != null)
                {
                    if (shouldLog) {
                    Debug.Log("[WallSegmentation-PerformSegmentation] ✅ Успешно создан NCHW тензор");
                    }
                    ExecuteModelAndProcessResult(inputTensor);
                    // Не освобождаем тензор здесь - это будет сделано в ExecuteModelAndProcessResult
                    inputTensor = null;
                    segmentationSuccess = true;
                    return;
                }
            }

            // Попытка 3: Обычный 3-канальный тензор
            if (inputTensor == null)
            {
                if (shouldLog) {
                Debug.Log("[WallSegmentation-PerformSegmentation] 🔄 Пробуем создать обычный RGB тензор...");
                }
                inputTensor = CreateTensorFromPixels(inputTexture);
                if (inputTensor != null)
                {
                    if (shouldLog) {
                    Debug.Log("[WallSegmentation-PerformSegmentation] ✅ Успешно создан RGB тензор");
                    }
                    ExecuteModelAndProcessResult(inputTensor);
                    // Не освобождаем тензор здесь - это будет сделано в ExecuteModelAndProcessResult
                    inputTensor = null;
                    segmentationSuccess = true;
                    return;
                }
            }

            // Попытка 4: одноканальный тензор для некоторых моделей
            if (inputTensor == null)
            {
                if (shouldLog) {
                Debug.Log("[WallSegmentation-PerformSegmentation] 🔄 Пробуем создать одноканальный тензор...");
                }
                inputTensor = CreateSingleChannelTensor(inputTexture);
                if (inputTensor != null)
                {
                    if (shouldLog) {
                    Debug.Log("[WallSegmentation-PerformSegmentation] ✅ Успешно создан одноканальный тензор");
                    }
                    ExecuteModelAndProcessResult(inputTensor);
                    // Не освобождаем тензор здесь - это будет сделано в ExecuteModelAndProcessResult
                    inputTensor = null;
                    segmentationSuccess = true;
                    return;
                }
            }

            // Если все попытки не удались, отображаем упрощенную маску
            if (!segmentationSuccess) {
            Debug.LogWarning("[WallSegmentation-PerformSegmentation] ⚠️ Все попытки создания тензора не удались! Используем упрощенную сегментацию.");
            RenderSimpleMask();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[WallSegmentation-PerformSegmentation] ❌ Ошибка при выполнении сегментации: {e.Message}\n{e.StackTrace}");
            // При ошибке создаем простую маску
            RenderSimpleMask();
        }
        finally
        {
            // Освобождаем ресурсы тензора, если он не был обработан
            if (inputTensor != null)
            {
                try
                {
                    inputTensor.Dispose();
                    Debug.Log("[WallSegmentation-PerformSegmentation] 🧹 Освобождены ресурсы неиспользованного тензора");
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"[WallSegmentation-PerformSegmentation] ⚠️ Ошибка при освобождении ресурсов тензора: {ex.Message}");
                }
            }
            
            // Освобождаем ресурсы измененной текстуры, если она была создана
            if (inputTexture != null && inputTexture != cameraTexture)
            {
                Destroy(inputTexture);
            }
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
    /// Выполняет модель с заданным входным тензором и обрабатывает результат
    /// </summary>
    private void ExecuteModelAndProcessResult(object inputTensorObj)
    {
        try
        {
            if (inputTensorObj == null)
            {
                Debug.LogWarning("[WallSegmentation-ExecuteModelAndProcessResult] ⚠️ Входной тензор равен null");
                return;
            }

            // Определяем shouldLog только раз в 50 кадров чтобы уменьшить частоту логирования
            bool shouldLog = Time.frameCount % 50 == 0;
            
        if (shouldLog) {
                Debug.Log("[WallSegmentation-ExecuteModelAndProcessResult] 🔄 Запуск модели и обработка результата");
            Debug.Log("[WallSegmentation-ExecuteModelAndProcessResult] 🔄 Подготовка к выполнению модели");
        }

            // Безопасно преобразуем входной тензор в правильный тип
            var inputTensor = inputTensorObj as Tensor<float>;

            if (inputTensor == null)
            {
                Debug.LogWarning("[WallSegmentation-ExecuteModelAndProcessResult] ⚠️ Не удалось преобразовать входной объект в тензор");
            return;
        }

            // Выводим размер входного тензора для диагностики
            if (shouldLog) {
                Debug.Log($"[WallSegmentation-ExecuteModelAndProcessResult] ✓ Форма входного тензора: {inputTensor.shape}");
            }

            // Если движок не инициализирован, выходим из метода
            if (worker == null)
            {
                Debug.LogError("[WallSegmentation-ExecuteModelAndProcessResult] ❌ Движок (worker) не инициализирован");
                return;
            }

            // Устанавливаем входной тензор
            string inputName = "input";
            bool setInputSuccess = false;
            
            try
            {
                // Заменяем вызов с неоднозначным поиском на более точный поиск по сигнатуре
                var setInputMethods = worker.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .Where(m => m.Name == "SetInput")
                    .ToArray();
                
                if (shouldLog) {
                    Debug.Log($"[WallSegmentation-ExecuteModelAndProcessResult] Найдено {setInputMethods.Length} перегрузок метода SetInput");
                }
                
                // Перебираем все методы SetInput и находим тот, который принимает (string, Tensor<float>)
                foreach (var method in setInputMethods)
                {
                    var parameters = method.GetParameters();
                    if (parameters.Length == 2)
                    {
                    if (shouldLog) {
                            Debug.Log($"[WallSegmentation-ExecuteModelAndProcessResult] Проверяем перегрузку SetInput с параметрами: {parameters[0].ParameterType.Name}, {parameters[1].ParameterType.Name}");
                        }
                        
                        // Проверяем, подходит ли метод для наших типов
                        if (parameters[0].ParameterType == typeof(string) && 
                            parameters[1].ParameterType.IsAssignableFrom(inputTensor.GetType()))
                        {
                            try
                            {
                                method.Invoke(worker, new object[] { inputName, inputTensor });
                                setInputSuccess = true;
                
                if (shouldLog) {
                                    Debug.Log($"[WallSegmentation-ExecuteModelAndProcessResult] ✅ Успешно установлен входной тензор через SetInput({parameters[0].ParameterType.Name}, {parameters[1].ParameterType.Name})");
                }
                                break;
            }
            catch (Exception ex)
            {
                                Debug.LogWarning($"[WallSegmentation-ExecuteModelAndProcessResult] ⚠️ Ошибка вызова SetInput({parameters[0].ParameterType.Name}, {parameters[1].ParameterType.Name}): {ex.Message}");
                            }
                        }
                        // Проверяем вариант с перегрузкой (int, Tensor<float>)
                        else if (parameters[0].ParameterType == typeof(int) && 
                                parameters[1].ParameterType.IsAssignableFrom(inputTensor.GetType()))
                        {
                            try
                            {
                                method.Invoke(worker, new object[] { 0, inputTensor }); // Используем индекс 0 вместо имени
                                setInputSuccess = true;
                                
                                if (shouldLog) {
                                    Debug.Log($"[WallSegmentation-ExecuteModelAndProcessResult] ✅ Успешно установлен входной тензор через SetInput({parameters[0].ParameterType.Name}, {parameters[1].ParameterType.Name})");
                                }
                                break;
                            }
                            catch (Exception ex)
                            {
                                Debug.LogWarning($"[WallSegmentation-ExecuteModelAndProcessResult] ⚠️ Ошибка вызова SetInput({parameters[0].ParameterType.Name}, {parameters[1].ParameterType.Name}): {ex.Message}");
                            }
                        }
                    }
                }
                
                if (!setInputSuccess)
                {
                    Debug.LogError("[WallSegmentation-ExecuteModelAndProcessResult] ❌ Не удалось установить входной тензор. Ни одна перегрузка SetInput не подошла.");
                    RenderSimpleMask();
                    return;
                }
                    }
                    catch (Exception ex)
                    {
                Debug.LogError($"[WallSegmentation-ExecuteModelAndProcessResult] ❌ Ошибка при установке входного тензора: {ex.Message}");
                RenderSimpleMask();
                return;
            }

            // Выполняем модель с помощью метода Schedule
            try
            {
            if (shouldLog) {
                    Debug.Log("[WallSegmentation-ExecuteModelAndProcessResult] 🔄 Запускаем модель через Schedule");
                }

                // В версии Unity Sentis 2.1.2 используется метод Schedule вместо Execute
                var scheduleMethods = worker.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                    .Where(m => m.Name == "Schedule")
                    .ToArray();
                
                bool scheduleSuccess = false;
                
                if (shouldLog) {
                    Debug.Log($"[WallSegmentation-ExecuteModelAndProcessResult] Найдено {scheduleMethods.Length} методов Schedule");
                }
                
                // Сначала пробуем метод Schedule без параметров
                foreach (var method in scheduleMethods)
                {
                    var parameters = method.GetParameters();
                    if (parameters.Length == 0)
                    {
                        try
                        {
                            method.Invoke(worker, null);
                            scheduleSuccess = true;
                            
                            if (shouldLog) {
                                Debug.Log("[WallSegmentation-ExecuteModelAndProcessResult] ✅ Модель успешно запланирована через Schedule()");
                            }
                            break;
                        }
                        catch (Exception ex)
                {
                    if (shouldLog) {
                                Debug.LogWarning($"[WallSegmentation-ExecuteModelAndProcessResult] ⚠️ Ошибка при вызове Schedule без параметров: {ex.Message}");
                            }
                        }
                    }
                }
                
                // Если не удалось, пробуем с разными параметрами
                if (!scheduleSuccess)
                {
                    foreach (var method in scheduleMethods)
                    {
                        var parameters = method.GetParameters();
                        
                        if (parameters.Length == 1)
                        {
                            if (shouldLog) {
                                Debug.Log($"[WallSegmentation-ExecuteModelAndProcessResult] Проверяем метод Schedule с параметром типа {parameters[0].ParameterType.Name}");
                            }
                            
                            // Для метода Schedule(string) - передаем имя выходного тензора
                            if (parameters[0].ParameterType == typeof(string))
                            {
                                try {
                                    method.Invoke(worker, new object[] { "output" });
                                    scheduleSuccess = true;
                                    
                                    if (shouldLog) {
                                        Debug.Log("[WallSegmentation-ExecuteModelAndProcessResult] ✅ Модель успешно запланирована через Schedule(\"output\")");
                                    }
                                    break;
                                }
                                catch (Exception ex) {
                                    if (shouldLog) {
                                        Debug.LogWarning($"[WallSegmentation-ExecuteModelAndProcessResult] ⚠️ Ошибка при вызове Schedule(string): {ex.Message}");
                                    }
                                }
                            }
                            // Для метода Schedule(int) - передаем индекс выходного тензора
                            else if (parameters[0].ParameterType == typeof(int))
                            {
                                try {
                                    method.Invoke(worker, new object[] { 0 });
                                    scheduleSuccess = true;
                                    
                    if (shouldLog) {
                                        Debug.Log("[WallSegmentation-ExecuteModelAndProcessResult] ✅ Модель успешно запланирована через Schedule(0)");
                                    }
                                    break;
                                }
                                catch (Exception ex) {
                                    if (shouldLog) {
                                        Debug.LogWarning($"[WallSegmentation-ExecuteModelAndProcessResult] ⚠️ Ошибка при вызове Schedule(int): {ex.Message}");
                                    }
                                }
                            }
                        }
                    }
                }
                
                if (!scheduleSuccess)
                {
                    Debug.LogError("[WallSegmentation-ExecuteModelAndProcessResult] ❌ Не удалось запланировать выполнение модели. Ни один метод Schedule не подошел.");
                    RenderSimpleMask();
                    return;
                }
                
                // После Schedule нужно дождаться выполнения
                try {
                    // Проверяем наличие метода WaitForCompletion
                    var waitMethod = worker.GetType().GetMethod("WaitForCompletion", BindingFlags.Instance | BindingFlags.Public);
                    if (waitMethod != null)
                    {
                        waitMethod.Invoke(worker, null);
                        if (shouldLog) {
                            Debug.Log("[WallSegmentation-ExecuteModelAndProcessResult] ✅ WaitForCompletion успешно выполнен");
                        }
                    }
                    else
                    {
                        // Если нет метода WaitForCompletion, пробуем использовать свойство completed
                        var completedProperty = worker.GetType().GetProperty("completed", BindingFlags.Instance | BindingFlags.Public);
                        if (completedProperty != null)
                        {
                            int maxWaitIterations = 50;
                            for (int i = 0; i < maxWaitIterations; i++)
                            {
                                bool completed = (bool)completedProperty.GetValue(worker);
                                if (completed) break;
                                
                                // Небольшая задержка
                                System.Threading.Thread.Sleep(10);
                            }
                            
                                    if (shouldLog) {
                                Debug.Log("[WallSegmentation-ExecuteModelAndProcessResult] ✅ Ожидание выполнения через свойство completed");
                            }
                        }
                    }
                }
                catch (Exception ex) {
                    if (shouldLog) {
                        Debug.LogWarning($"[WallSegmentation-ExecuteModelAndProcessResult] ⚠️ Ошибка при ожидании выполнения: {ex.Message}");
                    }
                }
                
                if (shouldLog) {
                    Debug.Log("[WallSegmentation-ExecuteModelAndProcessResult] ✅ Модель успешно выполнена");
                }
            }
            catch (Exception ex)
            {
                Debug.LogError($"[WallSegmentation-ExecuteModelAndProcessResult] ❌ Ошибка при выполнении модели: {ex.Message}");
                RenderSimpleMask();
                return;
            }

            // Получаем результат выполнения модели
            try
            {
                // Получаем выходной тензор
                string outputName = "output"; 
                var outputTensorObj = null as object;
                
                // Выводим список выходов модели, если доступны
                try {
                    var outputsProperty = worker.GetType().GetProperty("outputs");
                    if (outputsProperty != null)
                    {
                        var outputs = outputsProperty.GetValue(worker);
                        if (outputs != null)
                        {
                            // Пробуем получить имена выходов через рефлексию
                            try {
                                var namesProperty = outputs.GetType().GetProperty("names");
                                if (namesProperty != null)
                                {
                                    var names = namesProperty.GetValue(outputs) as System.Collections.IEnumerable;
                                    if (names != null)
                                    {
                                        List<string> namesList = new List<string>();
                                        foreach (var name in names)
                                        {
                                            namesList.Add(name.ToString());
                                        }
                                        Debug.Log($"[WallSegmentation-ExecuteModelAndProcessResult] Доступные выходы модели: {string.Join(", ", namesList)}");
                                        
                                        // Если есть выходы, используем первый как имя выходного тензора
                                        if (namesList.Count > 0)
                                        {
                                            outputName = namesList[0];
                                            Debug.Log($"[WallSegmentation-ExecuteModelAndProcessResult] Используем первый выход модели: {outputName}");
                                        }
                                    }
                                }
                            } catch (Exception ex) {
                                Debug.LogWarning($"[WallSegmentation-ExecuteModelAndProcessResult] Не удалось получить имена выходов: {ex.Message}");
                            }
                            
                            // Пробуем получить список выходов модели через Count
                            try {
                                var countProperty = outputs.GetType().GetProperty("Count");
                                if (countProperty != null)
                                {
                                    int count = (int)countProperty.GetValue(outputs);
                                    Debug.Log($"[WallSegmentation-ExecuteModelAndProcessResult] Количество выходов: {count}");
                                }
                            } catch (Exception ex) {
                                Debug.LogWarning($"[WallSegmentation-ExecuteModelAndProcessResult] Не удалось получить количество выходов: {ex.Message}");
                            }
                        }
                    }
                } catch (Exception ex) {
                    Debug.LogWarning($"[WallSegmentation-ExecuteModelAndProcessResult] Не удалось проанализировать выходы модели: {ex.Message}");
                }
                
                // Метод 1: через метод PeekOutput
                try {
                    var peekOutputMethods = worker.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public)
                        .Where(m => m.Name == "PeekOutput" && m.GetParameters().Length == 1)
                        .ToArray();
                        
                    if (peekOutputMethods.Length > 0)
                    {
                        Debug.Log($"[WallSegmentation-ExecuteModelAndProcessResult] Найдено {peekOutputMethods.Length} перегрузок метода PeekOutput");
                        
                        foreach (var method in peekOutputMethods)
            {
                try
                {
                                var parameters = method.GetParameters();
                                if (parameters.Length == 1 && parameters[0].ParameterType == typeof(string))
                                {
                                    outputTensorObj = method.Invoke(worker, new object[] { outputName });
                                    if (outputTensorObj != null)
                                    {
                                        Debug.Log($"[WallSegmentation-ExecuteModelAndProcessResult] ✅ Получен выходной тензор через PeekOutput(\"{outputName}\")");
                                        break;
                                    }
                                }
                                else if (parameters.Length == 1 && parameters[0].ParameterType == typeof(int))
                                {
                                    outputTensorObj = method.Invoke(worker, new object[] { 0 });
                                    if (outputTensorObj != null)
                                    {
                                        Debug.Log($"[WallSegmentation-ExecuteModelAndProcessResult] ✅ Получен выходной тензор через PeekOutput(0)");
                                        break;
                                    }
            }
        }
        catch (Exception ex)
        {
                                Debug.LogWarning($"[WallSegmentation-ExecuteModelAndProcessResult] ⚠️ Ошибка вызова PeekOutput: {ex.Message}");
                            }
                        }
                    }
                } catch (Exception ex) {
                    Debug.LogWarning($"[WallSegmentation-ExecuteModelAndProcessResult] ⚠️ Ошибка при поиске метода PeekOutput: {ex.Message}");
                }
                
                // Метод 2: через свойство outputs и индексатор
                if (outputTensorObj == null) {
                    try {
                        var outputs = worker.GetType().GetProperty("outputs")?.GetValue(worker);
                        if (outputs != null) {
                            // Проверяем доступные методы и свойства outputs
                            var outputsType = outputs.GetType();
                            var methods = outputsType.GetMethods(BindingFlags.Instance | BindingFlags.Public);
                            
                            // Получаем индексатор для доступа к элементам через имя или индекс
                            var stringIndexer = outputsType.GetProperties()
                                .FirstOrDefault(p => p.GetIndexParameters().Length == 1 && 
                                                  p.GetIndexParameters()[0].ParameterType == typeof(string));
                                                 
                            var intIndexer = outputsType.GetProperties()
                                .FirstOrDefault(p => p.GetIndexParameters().Length == 1 && 
                                                  p.GetIndexParameters()[0].ParameterType == typeof(int));
                                                      
                            if (stringIndexer != null) {
                                try {
                                    outputTensorObj = stringIndexer.GetValue(outputs, new object[] { outputName });
                                    if (outputTensorObj != null) {
                                        Debug.Log($"[WallSegmentation-ExecuteModelAndProcessResult] ✅ Получен выходной тензор через outputs[\"{outputName}\"]");
                                    }
                                } catch (Exception ex) {
                                    Debug.LogWarning($"[WallSegmentation-ExecuteModelAndProcessResult] ⚠️ Ошибка при доступе к outputs[string]: {ex.Message}");
                                }
                            }
                            
                            if (outputTensorObj == null && intIndexer != null) {
                                try {
                                    outputTensorObj = intIndexer.GetValue(outputs, new object[] { 0 });
                                    if (outputTensorObj != null) {
                                        Debug.Log($"[WallSegmentation-ExecuteModelAndProcessResult] ✅ Получен выходной тензор через outputs[0]");
                                    }
                                } catch (Exception ex) {
                                    Debug.LogWarning($"[WallSegmentation-ExecuteModelAndProcessResult] ⚠️ Ошибка при доступе к outputs[int]: {ex.Message}");
                                }
                            }
                            
                            // Проверяем метод TryGetValue
                            var tryGetValueMethod = methods.FirstOrDefault(m => m.Name == "TryGetValue" && 
                                                                           m.GetParameters().Length == 2 &&
                                                                           m.GetParameters()[0].ParameterType == typeof(string));
                            if (outputTensorObj == null && tryGetValueMethod != null) {
                                try {
                                    object[] parameters = new object[] { outputName, null };
                                    bool success = (bool)tryGetValueMethod.Invoke(outputs, parameters);
                                    if (success) {
                                        outputTensorObj = parameters[1]; // Второй параметр - out параметр
                                        Debug.Log($"[WallSegmentation-ExecuteModelAndProcessResult] ✅ Получен выходной тензор через TryGetValue");
                                    }
                                } catch (Exception ex) {
                                    Debug.LogWarning($"[WallSegmentation-ExecuteModelAndProcessResult] ⚠️ Ошибка при вызове TryGetValue: {ex.Message}");
                                }
                            }
                        }
                    } catch (Exception ex) {
                        Debug.LogWarning($"[WallSegmentation-ExecuteModelAndProcessResult] ⚠️ Не удалось получить выход через outputs: {ex.Message}");
                    }
                }
                
                // Метод 3: через CopyOutput
                if (outputTensorObj == null) {
                    try {
                        var methods = worker.GetType().GetMethods(BindingFlags.Instance | BindingFlags.Public);
                        var copyOutputMethod = methods.FirstOrDefault(m => (m.Name == "CopyOutput" || m.Name == "FetchOutput") &&
                                                                        m.GetParameters().Length == 2);
                        if (copyOutputMethod != null) {
                            // Создаем тензор для копирования
                            Type tensorType = typeof(Tensor<float>);
                            var tensor = Activator.CreateInstance(tensorType);
                            
                            // Вызываем метод
                            var result = copyOutputMethod.Invoke(worker, new object[] { outputName, tensor });
                            outputTensorObj = result ?? tensor;
                            
                            if (outputTensorObj != null) {
                                Debug.Log($"[WallSegmentation-ExecuteModelAndProcessResult] ✅ Получен выходной тензор через {copyOutputMethod.Name}");
                            }
                        }
                    } catch (Exception ex) {
                        Debug.LogWarning($"[WallSegmentation-ExecuteModelAndProcessResult] ⚠️ Не удалось получить выход через CopyOutput: {ex.Message}");
                    }
                }

                // Проверяем, получили ли мы выходной тензор
                if (outputTensorObj == null)
                {
                    Debug.LogError("[WallSegmentation-ExecuteModelAndProcessResult] ❌ Не удалось получить выходной тензор");
                    RenderSimpleMask(); // Создаем простую маску, если не удалось получить результат
                    return;
                }

                Debug.Log($"[WallSegmentation-ExecuteModelAndProcessResult] ✅ Успешно получен выходной тензор типа {outputTensorObj.GetType().Name}");

                // Обрабатываем результат сегментации
                ProcessSegmentationResult(outputTensorObj);
                }
                catch (Exception ex)
                {
                Debug.LogError($"[WallSegmentation-ExecuteModelAndProcessResult] ❌ Ошибка при получении выходного тензора: {ex.Message}\n{ex.StackTrace}");
                RenderSimpleMask();
                }
            }
        catch (Exception ex)
        {
            Debug.LogError($"[WallSegmentation-ExecuteModelAndProcessResult] ❌ Необработанное исключение: {ex.Message}\n{ex.StackTrace}");
            RenderSimpleMask();
        }
    }

    /// <summary>
    /// Обрабатывает результат сегментации из выходного тензора модели
    /// </summary>
    private void ProcessSegmentationResult(object outputTensorObj)
    {
        if (outputTensorObj == null)
        {
            Debug.LogError("[WallSegmentation-ProcessSegmentationResult] ❌ Выходной тензор равен null");
            RenderSimpleMask();
            return;
        }

        Debug.Log($"[WallSegmentation-ProcessSegmentationResult] Обработка результата сегментации типа {outputTensorObj.GetType().Name}");

        try
        {
            // Преобразуем выходной тензор в нужный тип
            var outputTensor = outputTensorObj as Tensor<float>;
            
            if (outputTensor == null)
            {
                Debug.LogError("[WallSegmentation-ProcessSegmentationResult] ❌ Не удалось преобразовать выходной объект в тензор");
                RenderSimpleMask();
                return;
            }

            // Обрабатываем результат сегментации вручную
            ProcessTensorManual(outputTensor);
            
            // Увеличиваем счетчик отладочных масок при их сохранении
            if (saveDebugMask)
            {
                debugMaskCounter++;
            }
        }
        catch (Exception ex)
        {
            Debug.LogError($"[WallSegmentation-ProcessSegmentationResult] ❌ Ошибка при обработке результата: {ex.Message}");
            RenderSimpleMask();
        }
    }

    /// <summary>
    /// Обрабатывает тензор segmentation вручную для создания маски
    /// </summary>
    private void ProcessTensorManual(Tensor<float> tensor)
    {
        if (tensor == null)
        {
            Debug.LogError("[WallSegmentation-ProcessTensorManual] ❌ Тензор равен null");
            RenderSimpleMask();
            return;
        }

        Debug.Log($"[WallSegmentation-ProcessTensorManual] 🔄 Обработка тензора формы {tensor.shape}");

        // Получаем размеры тензора
        int batch = tensor.shape[0];
        int classes = tensor.shape[1];
        int height = tensor.shape[2];
        int width = tensor.shape[3];

        Debug.Log($"[WallSegmentation-ProcessTensorManual] Размеры выходного тензора: batch={batch}, classes={classes}, h={height}, w={width}");

        // Попытка получить данные тензора различными способами
            float[] tensorData = null;
        bool gotData = false;

        try
        {
            // Попытка 0: Используем TensorExtensions (новый метод)
            if (TryGetTensorData(tensor, out tensorData))
            {
                Debug.Log($"[WallSegmentation-ProcessTensorManual] ✅ Получены данные тензора через TensorExtensions: {tensorData.Length} элементов");
                gotData = true;
            }

            // Попытка 0.5: Используем DenseTensor напрямую
            if (!gotData && TryGetDenseTensorData(tensor, out tensorData))
            {
                Debug.Log($"[WallSegmentation-ProcessTensorManual] ✅ Получены данные через DenseTensor: {tensorData.Length} элементов");
                gotData = true;
            }

            // Попытка 1: Используем ToReadOnlyArray, если доступен
            if (!gotData)
                    {
                        var toArrayMethod = tensor.GetType().GetMethod("ToReadOnlyArray", BindingFlags.Instance | BindingFlags.Public);
                        if (toArrayMethod != null)
                        {
                    var result = toArrayMethod.Invoke(tensor, null);
                    if (result is float[] data)
                    {
                        tensorData = data;
                        Debug.Log($"[WallSegmentation-ProcessTensorManual] ✅ Получены данные тензора через ToReadOnlyArray: {tensorData.Length} элементов");
                        gotData = true;
                    }
                }
            }

            // Попытка 2: Используем свойство Data, если доступно
            if (!gotData)
            {
                var dataProperty = tensor.GetType().GetProperty("Data", BindingFlags.Instance | BindingFlags.Public);
                if (dataProperty != null)
                {
                    var result = dataProperty.GetValue(tensor);
                    if (result is float[] data)
                    {
                        tensorData = data;
                        Debug.Log($"[WallSegmentation-ProcessTensorManual] ✅ Получены данные тензора через свойство Data: {tensorData.Length} элементов");
                        gotData = true;
                    }
                }
            }

            // Попытка 3: Создаем массив и копируем в него данные
            if (!gotData)
            {
                // Пробуем использовать метод CopyTo
                var copyToMethod = tensor.GetType().GetMethod("CopyTo", BindingFlags.Instance | BindingFlags.Public);
                if (copyToMethod != null)
                {
                    int tensorSize = batch * classes * height * width;
                    tensorData = new float[tensorSize];
                    
                    // Проверяем, требует ли метод CopyTo аргументов
                    ParameterInfo[] parameters = copyToMethod.GetParameters();
                    if (parameters.Length == 1 && parameters[0].ParameterType == typeof(float[]))
                    {
                        copyToMethod.Invoke(tensor, new object[] { tensorData });
                        Debug.Log($"[WallSegmentation-ProcessTensorManual] ✅ Данные скопированы через CopyTo: {tensorData.Length} элементов");
                        gotData = true;
                    }
                }
            }

            // Попытка 4: Используем метод GetValue для поэлементного считывания
            if (!gotData)
            {
                var getValueMethod = tensor.GetType().GetMethod("GetValue", BindingFlags.Instance | BindingFlags.Public);
                if (getValueMethod != null)
                {
                    int tensorSize = batch * classes * height * width;
                    float[] newTensorData = new float[tensorSize];
                    bool canUseGetValue = false;
                    
                    // Проверяем сигнатуру метода GetValue
                    var parameters = getValueMethod.GetParameters();
                    if (parameters.Length > 0 && parameters.All(p => p.ParameterType == typeof(int)))
                    {
                        canUseGetValue = true;
                    }
                    
                    if (canUseGetValue)
                    {
                        // Тестируем, работает ли GetValue
                        try
                        {
                            object[] indices = new object[4] { 0, 0, 0, 0 };
                            getValueMethod.Invoke(tensor, indices);
                            
                            // Если мы добрались сюда, значит GetValue работает
                            Debug.Log($"[WallSegmentation-ProcessTensorManual] Данные будут считаны поэлементно через GetValue (медленно)");
                            
                            // Считываем только часть данных (это может быть очень медленно)
                            int sampleSize = Math.Min(1000, tensorSize);
                            for (int i = 0; i < sampleSize; i++)
                            {
                                int idx_b = i / (classes * height * width);
                                int remainder = i % (classes * height * width);
                                int idx_c = remainder / (height * width);
                                remainder = remainder % (height * width);
                                int idx_h = remainder / width;
                                int idx_w = remainder % width;
                                
                                object[] args = new object[4] { idx_b, idx_c, idx_h, idx_w };
                                object result = getValueMethod.Invoke(tensor, args);
                                if (result is float value)
                                {
                                    newTensorData[i] = value;
                                }
                            }
                            
                            tensorData = newTensorData;
                            Debug.Log($"[WallSegmentation-ProcessTensorManual] ✅ Считано {sampleSize} элементов через GetValue");
                            gotData = true;
                    }
                    catch (Exception ex)
                    {
                            Debug.LogWarning($"[WallSegmentation-ProcessTensorManual] Не удалось использовать GetValue: {ex.Message}");
                        }
                    }
                    }
                }
            }
            catch (Exception ex)
            {
            Debug.LogError($"[WallSegmentation-ProcessTensorManual] ❌ Ошибка при получении данных тензора: {ex.Message}\n{ex.StackTrace}");
            }

        // Если не удалось получить данные, используем предыдущую маску или симуляцию
        if (!gotData || tensorData == null)
        {
            Debug.LogWarning("[WallSegmentation-ProcessTensorManual] ⚠️ Не удалось получить данные тензора. Используем симуляцию");
            
            // УЛУЧШЕНИЕ: Если у нас есть предыдущая валидная маска, используем её вместо симуляции
            if (hasValidMask && lastSuccessfulMask != null && lastSuccessfulMask.IsCreated() && 
                (Time.time - lastValidMaskTime) < 5.0f) // Используем предыдущую маску не старше 5 секунд
            {
                Debug.Log("[WallSegmentation-ProcessTensorManual] ℹ️ Используем сохраненную валидную маску вместо симуляции");
                
                // Вызываем событие с существующей маской
                TriggerSegmentationMaskUpdatedEvent(lastSuccessfulMask);
                return;
            }
            
            // Создаем симулированные данные
            tensorData = new float[batch * classes * height * width];
            SimulateTensorData(ref tensorData, classes, height, width);
        }

        // Проверяем, что тензор имеет ожидаемое количество классов
        if (classes < Math.Max(wallClassIndex, floorClassIndex) + 1) {
            Debug.LogWarning($"[WallSegmentation-ProcessTensorManual] ⚠️ Модель выдает {classes} классов, но индексы стен/пола ({wallClassIndex}/{floorClassIndex}) выходят за эти пределы");
            // Продолжаем выполнение, так как индексы могут быть неправильно настроены
        }

        if (segmentationMaskTexture == null || segmentationMaskTexture.width != width || segmentationMaskTexture.height != height)
        {
            if (segmentationMaskTexture != null)
            {
                RenderTexture.ReleaseTemporary(segmentationMaskTexture);
            }
            segmentationMaskTexture = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
            segmentationMaskTexture.enableRandomWrite = true;
            segmentationMaskTexture.Create();
            Debug.Log($"[WallSegmentation-ProcessTensorManual] 🔄 Создана новая segmentationMaskTexture ({width}x{height})");
        }

        // Определяем флаг логирования - только раз в 50 кадров, чтобы не спамить логи
        bool shouldLog = Time.frameCount % 50 == 0;
        bool usingSim = false;  // Переименовано с usingSimulation
            
            // Вычисляем статистику для отладки
            float wallProbMin = 1.0f;
            float wallProbMax = 0.0f;
            float wallProbSum = 0.0f;
            int wallPixelsCount = 0;

        // Анализируем тензор только каждые 150 кадров, чтобы не замедлять приложение
        if (Time.frameCount % 150 == 0) {
            AnalyzeTensorData(tensorData, batch, classes, height, width);
        }

        // Создаем текстуру для сегментации и заполняем ее данными
        Color32[] pixels = new Color32[width * height];

            // Проходим по всем пикселям маски
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    // Индекс пикселя в выходном массиве
                    int pixelIndex = y * width + x;
                    
                    // Определяем вероятности для каждого класса
                    float maxProb = float.MinValue;
                    int maxClass = -1;
                    
                    // Находим класс с максимальной вероятностью
                    for (int c = 0; c < classes; c++)
                    {
                        int tensorIndex = IndexFromCoordinates(0, c, y, x, batch, classes, height, width);
                        float prob = tensorData[tensorIndex];
                        
                        if (prob > maxProb)
                        {
                            maxProb = prob;
                            maxClass = c;
                        }
                    }
                    
                    // Извлекаем вероятности для стен и пола
                    float wallProb = 0;
                    float floorProb = 0;
                    
                    // Проверяем, находится ли индекс класса стены в пределах размера массива
                    if (wallClassIndex >= 0 && wallClassIndex < classes)
                    {
                        int wallIndex = IndexFromCoordinates(0, wallClassIndex, y, x, batch, classes, height, width);
                        if (wallIndex < tensorData.Length)
                        {
                            wallProb = tensorData[wallIndex];
                        }
                    }
                    
                    // Проверяем, находится ли индекс класса пола в пределах размера массива
                    if (floorClassIndex >= 0 && floorClassIndex < classes)
                    {
                        int floorIndex = IndexFromCoordinates(0, floorClassIndex, y, x, batch, classes, height, width);
                        if (floorIndex < tensorData.Length)
                        {
                            floorProb = tensorData[floorIndex];
                        }
                    }
                    
                    // Применяем пороги вероятности
                    bool isWall = wallProb >= wallThreshold;
                    bool isFloor = floorProb >= floorThreshold;
                    
                    // Устанавливаем цвет пикселя в зависимости от класса
                    Color32 pixelColor = new Color32(0, 0, 0, 0); // Прозрачный по умолчанию
                    
                    if (isWall)
                    {
                        pixelColor = new Color32(255, 0, 0, 255); // Красный для стен
                        wallPixelsCount++;
                        
                        // Обновляем статистику
                        wallProbMin = Mathf.Min(wallProbMin, wallProb);
                        wallProbMax = Mathf.Max(wallProbMax, wallProb);
                        wallProbSum += wallProb;
                    }
                    else if (detectFloor && isFloor)
                    {
                        pixelColor = new Color32(0, 0, 255, 255); // Синий для пола
                    }
                    
                    pixels[pixelIndex] = pixelColor;
                }
            }
            
            // Отображаем статистику
            if (wallPixelsCount > 0)
            {
                float wallProbAvg = wallProbSum / wallPixelsCount;
                Debug.Log($"[WallSegmentation-ProcessTensorManual] 📊 Статистика: найдено {wallPixelsCount}/{width*height} пикселей стены. " +
                          $"Вероятности стены: min={wallProbMin:F3}, max={wallProbMax:F3}, avg={wallProbAvg:F3}");
            }
            else if (usingSim)  // Используем переменную
            {
                Debug.Log("[WallSegmentation-ProcessTensorManual] 📊 Используются симулированные данные для маски");
            }
            
            // УЛУЧШЕНИЕ: Проверяем стабильность результата
            bool isStableFrame = wallPixelsCount > (width * height * 0.05f); // Минимум 5% пикселей должны быть стенами
            
            if (isStableFrame)
            {
                stableFrameCount++;
            }
            else
            {
                stableFrameCount = 0;
            }
            
            // Создаем текстуру для результата
            Texture2D resultTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
            resultTexture.SetPixels32(pixels);
            resultTexture.Apply();
            
            // УЛУЧШЕНИЕ: Применяем маску только если это стабильный кадр или первая успешная маска
            if (stableFrameCount >= REQUIRED_STABLE_FRAMES || (!hasValidMask && isStableFrame))
            {
                // Копируем результат в RenderTexture для вывода
                Graphics.Blit(resultTexture, segmentationMaskTexture);
                
                // УЛУЧШЕНИЕ: Сохраняем успешную маску
                if (lastSuccessfulMask == null || !lastSuccessfulMask.IsCreated())
                {
                    lastSuccessfulMask = new RenderTexture(width, height, 0, RenderTextureFormat.ARGB32);
                    lastSuccessfulMask.enableRandomWrite = true;
                    lastSuccessfulMask.Create();
                }
                
                // Копируем текущую маску как успешную
                Graphics.Blit(segmentationMaskTexture, lastSuccessfulMask);
                hasValidMask = true;
                lastValidMaskTime = Time.time;
                
                // Вызываем событие обновления маски
                if (OnSegmentationMaskUpdated != null)
                {
                    int subscribersCount = OnSegmentationMaskUpdated.GetInvocationList().Length;
                    Debug.Log($"[WallSegmentation-ProcessTensorManual] 📣 Вызываем событие OnSegmentationMaskUpdated с новой стабильной маской {segmentationMaskTexture.width}x{segmentationMaskTexture.height}, подписчиков: {subscribersCount}");
                    OnSegmentationMaskUpdated.Invoke(segmentationMaskTexture);
                }
                
                Debug.Log($"[WallSegmentation-ProcessTensorManual] ✅ Стабильная маска сегментации обновлена (размер {width}x{height})");
            }
            else if (hasValidMask && lastSuccessfulMask != null)
            {
                // Используем предыдущую успешную маску
                Debug.Log($"[WallSegmentation-ProcessTensorManual] ⚠️ Текущий кадр нестабилен ({stableFrameCount}/{REQUIRED_STABLE_FRAMES}), используем предыдущую маску");
                
                // Вызываем событие с предыдущей стабильной маской
                if (OnSegmentationMaskUpdated != null)
                {
                    int subscribersCount = OnSegmentationMaskUpdated.GetInvocationList().Length;
                    Debug.Log($"[WallSegmentation-ProcessTensorManual] 📣 Вызываем событие OnSegmentationMaskUpdated с сохраненной маской, подписчиков: {subscribersCount}");
                    OnSegmentationMaskUpdated.Invoke(lastSuccessfulMask);
                }
            }
            else
            {
                // Если нет предыдущей маски, применяем текущую несмотря на нестабильность
                Graphics.Blit(resultTexture, segmentationMaskTexture);
                
                // Вызываем событие обновления маски
                if (OnSegmentationMaskUpdated != null)
                {
                    int subscribersCount = OnSegmentationMaskUpdated.GetInvocationList().Length;
                    Debug.Log($"[WallSegmentation-ProcessTensorManual] 📣 Вызываем событие OnSegmentationMaskUpdated с нестабильной маской {segmentationMaskTexture.width}x{segmentationMaskTexture.height}, подписчиков: {subscribersCount}");
                    OnSegmentationMaskUpdated.Invoke(segmentationMaskTexture);
                }
                else
                {
                    Debug.LogWarning("[WallSegmentation-ProcessTensorManual] ⚠️ Нет подписчиков на событие OnSegmentationMaskUpdated!");
                }
                
                Debug.Log($"[WallSegmentation-ProcessTensorManual] ⚠️ Используем нестабильную маску (размер {width}x{height})");
            }
            
            // Уничтожаем временную текстуру
            Destroy(resultTexture);
    }

    /// <summary>
    /// Проверяет, доступен ли тензор для чтения напрямую
    /// </summary>
    private bool IsTensorAccessible(Tensor<float> tensor)
    {
        if (tensor == null)
        {
            return false;
        }
        
        try
        {
            // Метод 1: Пробуем получить данные через ToReadOnlyArray
            var toArrayMethod = tensor.GetType().GetMethod("ToReadOnlyArray", BindingFlags.Instance | BindingFlags.Public);
            if (toArrayMethod != null)
            {
                return true;
            }

            // Метод 2: Проверяем наличие свойства Data
            var dataProperty = tensor.GetType().GetProperty("Data", BindingFlags.Instance | BindingFlags.Public);
            if (dataProperty != null)
            {
                return true;
            }

            // Метод 3: Проверяем наличие метода AsReadOnlySpan
            var asSpanMethod = tensor.GetType().GetMethod("AsReadOnlySpan", BindingFlags.Instance | BindingFlags.Public);
            if (asSpanMethod != null)
            {
                return true;
            }

            // Метод 4: Проверяем наличие метода CopyTo
            var copyToMethod = tensor.GetType().GetMethod("CopyTo", BindingFlags.Instance | BindingFlags.Public);
            if (copyToMethod != null)
            {
                return true;
            }

            // Если ни один из методов не найден, тензор не доступен для чтения
            return false;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[WallSegmentation-IsTensorAccessible] Ошибка при проверке доступности тензора: {ex.Message}");
            return false;
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
            float[] pixelsData = new float[inputTexture.width * inputTexture.height * 3];
            
            // Преобразуем пиксели в тензор
            Color[] pixels = inputTexture.GetPixels();
            for (int i = 0; i < pixels.Length; i++)
            {
                pixelsData[i * 3 + 0] = pixels[i].r;
                pixelsData[i * 3 + 1] = pixels[i].g;
                pixelsData[i * 3 + 2] = pixels[i].b;
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
        // Периодически выводим информацию о состоянии работы WallSegmentation
        if (Time.frameCount % 300 == 0)
        {
            Debug.Log($"[WallSegmentation-Update] 📊 Статус: инициализирована={isModelInitialized}, ARCamera={ARCameraManager != null}, кадр={Time.frameCount}");
        }
        
        if (!isModelInitialized || ARCameraManager == null)
        {
            // Если модель не инициализирована, но прошло значительное время, попробуем инициализировать ее снова
            if (Time.frameCount % 100 == 0 && !isInitializing && !isModelInitialized)
            {
                Debug.LogWarning("[WallSegmentation-Update] ⚠️ Модель не инициализирована. Попытка автоматической инициализации...");
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
            
            if (Time.frameCount % 50 == 0) {
                Debug.LogWarning($"[WallSegmentation-Update] ⚠️ Не удалось получить изображение с камеры (попыток: {consecutiveFailCount})");
            }
            
            // Проверяем нужно ли использовать симуляцию
            if (useSimulationIfNoCamera && consecutiveFailCount >= failureThresholdForSimulation) 
            {
                if (!usingSimulatedSegmentation) {
                    Debug.Log($"[WallSegmentation-Update] 🔄 Включение режима симуляции после {consecutiveFailCount} неудачных попыток");
                    usingSimulatedSegmentation = true;
                }
                
                cameraPixels = GetCameraTextureFromSimulation();
                if (cameraPixels != null)
                {
                    if (Time.frameCount % 100 == 0) {
                        Debug.Log($"[WallSegmentation-Update] ℹ️ Используется симуляция изображения с камеры (режим: {(usingSimulation ? "симуляция" : "реальное изображение")})");
                    }
                }
            }
        }
        else
        {
            // Сбрасываем счетчик неудач, если удалось получить изображение
            consecutiveFailCount = 0;
            usingSimulatedSegmentation = false;
            if (Time.frameCount % 500 == 0) {
                Debug.Log($"[WallSegmentation-Update] ✅ Получено изображение с камеры (режим: {(usingSimulation ? "симуляция" : "реальное изображение")})");
            }
        }
        
        // Если не удалось получить изображение даже из симуляции, выходим
        if (cameraPixels == null)
        {
            if (Time.frameCount % 100 == 0) {
                Debug.LogWarning("[WallSegmentation-Update] ❌ Не удалось получить изображение ни с камеры, ни из симуляции");
            }
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
        data = null;
        if (tensor == null) return false;

        try
        {
            // Пытаемся найти статический класс TensorExtensions в Unity.Sentis
            Type tensorExtensionsType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.FullName == "Unity.Sentis.TensorExtensions");

            if (tensorExtensionsType != null)
            {
                Debug.Log($"[WallSegmentation] ✅ Найден класс TensorExtensions");

                // Проверяем метод AsReadOnlySpan (новый в Sentis 2.1.x)
                var asReadOnlySpanMethod = tensorExtensionsType.GetMethod("AsReadOnlySpan", 
                    BindingFlags.Public | BindingFlags.Static, 
                    null, 
                    new Type[] { typeof(Tensor<float>) }, 
                    null);

                if (asReadOnlySpanMethod != null)
                {
                    Debug.Log($"[WallSegmentation] ✅ Найден метод AsReadOnlySpan");
                    
                    // Вызываем метод AsReadOnlySpan
                    var span = asReadOnlySpanMethod.Invoke(null, new object[] { tensor });
                    
                    // Если результат не null, создаем массив из ReadOnlySpan
                    if (span != null)
                    {
                        // Получаем тип ReadOnlySpan<float>
                        Type spanType = span.GetType();
                        
                        // Узнаем свойство Length в ReadOnlySpan<float>
                        PropertyInfo lengthProperty = spanType.GetProperty("Length");
                        if (lengthProperty != null)
                        {
                            int length = (int)lengthProperty.GetValue(span);
                            Debug.Log($"[WallSegmentation] ✅ Получен Span длиной {length}");
                            
                            // Создаем массив нужной длины
                            data = new float[length];
                            
                            // Находим метод CopyTo в ReadOnlySpan<float>
                            MethodInfo spanCopyToMethod = spanType.GetMethod("CopyTo");
                            if (spanCopyToMethod != null)
                            {
                                // Создаем Span<float> из нашего массива
                                Type spanOfTType = AppDomain.CurrentDomain.GetAssemblies()
                                    .SelectMany(a => a.GetTypes())
                                    .FirstOrDefault(t => t.FullName == "System.Span`1");
                                    
                                if (spanOfTType != null)
                                {
                                    Type spanOfFloatType = spanOfTType.MakeGenericType(typeof(float));
                                    
                                    // Создаем Span<float> из нашего массива
                                    ConstructorInfo spanConstructor = spanOfFloatType.GetConstructor(
                                        new Type[] { typeof(float[]) });
                                        
                                    if (spanConstructor != null)
                                    {
                                        object destSpan = spanConstructor.Invoke(new object[] { data });
                                        
                                        // Копируем данные
                                        spanCopyToMethod.Invoke(span, new[] { destSpan });
                                        
                                        Debug.Log($"[WallSegmentation] ✅ Данные скопированы в массив через Span");
                        return true;
                    }
                }
            }
                            
                            // Если не удалось скопировать через CopyTo, пробуем поэлементно
                            // Находим индексатор в ReadOnlySpan<float>
                            PropertyInfo indexerProperty = spanType.GetProperty("Item");
                            if (indexerProperty != null)
                            {
                                for (int i = 0; i < length; i++)
                                {
                                    data[i] = (float)indexerProperty.GetValue(span, new object[] { i });
                                }
                                Debug.Log($"[WallSegmentation] ✅ Данные скопированы в массив поэлементно");
                                return true;
                            }
                        }
                    }
                }

                // Ищем метод AsFloats или подобные методы
                var asFloatsMethod = tensorExtensionsType.GetMethod("AsFloats", 
                    BindingFlags.Public | BindingFlags.Static, 
                    null, 
                    new Type[] { typeof(Tensor<float>) }, 
                    null);

                if (asFloatsMethod != null)
                {
                    Debug.Log($"[WallSegmentation] ✅ Найден метод AsFloats");
                    data = (float[])asFloatsMethod.Invoke(null, new object[] { tensor });
                    return data != null;
                }
                
                // Пытаемся найти другие подходящие методы, возвращающие массив float
                var otherMethods = tensorExtensionsType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                    .Where(m => m.ReturnType == typeof(float[]) && 
                           m.GetParameters().Length == 1 && 
                           m.GetParameters()[0].ParameterType == typeof(Tensor<float>))
                    .ToList();
                
                foreach (var method in otherMethods)
                {
                    Debug.Log($"[WallSegmentation] Пробуем метод {method.Name}");
                    try
                    {
                        data = (float[])method.Invoke(null, new object[] { tensor });
                        if (data != null)
                        {
                            Debug.Log($"[WallSegmentation] ✅ Успешно получены данные через метод {method.Name}");
                    return true;
                }
            }
                    catch (Exception ex)
            {
                        Debug.LogWarning($"[WallSegmentation] Ошибка при вызове метода {method.Name}: {ex.Message}");
                    }
                }
            }
            
            return false;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[WallSegmentation] Ошибка при получении данных тензора через TensorExtensions: {ex.Message}\n{ex.StackTrace}");
            return false;
        }
    }

    /// <summary>
    /// Пытается получить данные из тензора через динамическое определение типа DenseTensor
    /// </summary>
    private bool TryGetDenseTensorData(Tensor<float> tensor, out float[] data)
    {
        data = null;
        if (tensor == null) return false;

        try
        {
            // Проверяем, является ли тензор DenseTensor (в Unity Sentis 2.1.x)
            Type denseTensorType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.FullName == "Unity.Sentis.DenseTensor`1");
            
            if (denseTensorType != null)
            {
                Type specificDenseTensorType = denseTensorType.MakeGenericType(typeof(float));
                Debug.Log($"[WallSegmentation] ✅ Найден тип DenseTensor<float>");
                
                // Проверяем, является ли входной тензор DenseTensor<float>
                if (specificDenseTensorType.IsInstanceOfType(tensor))
                {
                    Debug.Log($"[WallSegmentation] ✅ Тензор действительно является DenseTensor<float>");
                    
                    // Ищем свойство Buffer в DenseTensor<float>
                    PropertyInfo bufferProperty = specificDenseTensorType.GetProperty("Buffer", 
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                        
                    if (bufferProperty != null)
                    {
                        // Получаем буфер данных
                        var buffer = bufferProperty.GetValue(tensor);
                        
                        if (buffer is float[] floatBuffer)
                        {
                            data = floatBuffer;
                            Debug.Log($"[WallSegmentation] ✅ Получены данные через DenseTensor.Buffer: {data.Length} элементов");
                            return true;
                        }
                        else if (buffer != null)
                        {
                            // Возможно, это ReadOnlySpan<float> или другой тип
                            Type bufferType = buffer.GetType();
                            Debug.Log($"[WallSegmentation] Буфер имеет тип {bufferType.FullName}");
                            
                            // Если это ReadOnlySpan<float>, попробуем преобразовать его в массив
                            if (bufferType.FullName.Contains("ReadOnlySpan") || 
                                bufferType.FullName.Contains("Span"))
                            {
                                // Получаем длину
                                PropertyInfo lengthProperty = bufferType.GetProperty("Length");
                                if (lengthProperty != null)
                                {
                                    int length = (int)lengthProperty.GetValue(buffer);
                                    data = new float[length];
                                    
                                    // Пробуем метод CopyTo
                                    MethodInfo bufferCopyToMethod = bufferType.GetMethod("CopyTo");
                                    if (bufferCopyToMethod != null)
                                    {
                                        // Создаем Span<float> из нашего массива
                                        Type spanOfTType = AppDomain.CurrentDomain.GetAssemblies()
                                            .SelectMany(a => a.GetTypes())
                                            .FirstOrDefault(t => t.FullName == "System.Span`1");
                                            
                                        if (spanOfTType != null)
                                        {
                                            Type spanOfFloatType = spanOfTType.MakeGenericType(typeof(float));
                                            
                                            // Создаем Span<float> из нашего массива
                                            ConstructorInfo spanConstructor = spanOfFloatType.GetConstructor(
                                                new Type[] { typeof(float[]) });
                                                
                                            if (spanConstructor != null)
                                            {
                                                object destSpan = spanConstructor.Invoke(new object[] { data });
                                                
                                                // Копируем данные
                                                bufferCopyToMethod.Invoke(buffer, new[] { destSpan });
                                                
                                                Debug.Log($"[WallSegmentation] ✅ Данные скопированы из буфера в массив через Span");
                                                return true;
                                            }
                                        }
                                    }
                                    
                                    // Если CopyTo не сработал, пробуем поэлементно через индексатор
                                    PropertyInfo indexerProperty = bufferType.GetProperty("Item");
                                    if (indexerProperty != null)
                                    {
                                        for (int i = 0; i < length; i++)
                                        {
                                            data[i] = (float)indexerProperty.GetValue(buffer, new object[] { i });
                                        }
                                        Debug.Log($"[WallSegmentation] ✅ Данные скопированы из буфера в массив поэлементно");
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                    
                    // Если не удалось получить данные через Buffer, пробуем через метод ToArray
                    MethodInfo toArrayMethod = specificDenseTensorType.GetMethod("ToArray", 
                        BindingFlags.Instance | BindingFlags.Public);
                        
                    if (toArrayMethod != null)
                    {
                        var result = toArrayMethod.Invoke(tensor, null);
                        if (result is float[] floatArray)
                        {
                            data = floatArray;
                            Debug.Log($"[WallSegmentation] ✅ Получены данные через DenseTensor.ToArray: {data.Length} элементов");
                            return true;
                        }
                    }
                    
                    // Пробуем через метод CopyTo (если он принимает массив float[])
                    MethodInfo tensorCopyToMethod = specificDenseTensorType.GetMethod("CopyTo", 
                        BindingFlags.Instance | BindingFlags.Public,
                        null,
                        new Type[] { typeof(float[]) },
                        null);
                        
                    if (tensorCopyToMethod != null)
                    {
                        // Создаем массив нужного размера
                        int tensorSize = tensor.shape.length > 0 ? tensor.shape[0] : 0;
                        for (int i = 1; i < tensor.shape.length; i++)
                        {
                            tensorSize *= tensor.shape[i];
                        }
                        
                        if (tensorSize > 0)
                        {
                            data = new float[tensorSize];
                            tensorCopyToMethod.Invoke(tensor, new object[] { data });
                            Debug.Log($"[WallSegmentation] ✅ Получены данные через DenseTensor.CopyTo: {data.Length} элементов");
                            return true;
                        }
                    }
                }
                else
                {
                    Debug.Log($"[WallSegmentation] Тензор не является DenseTensor<float>, а имеет тип {tensor.GetType().FullName}");
                }
            }
            else
            {
                Debug.Log("[WallSegmentation] Тип DenseTensor<T> не найден");
            }
            
            return false;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"[WallSegmentation] Ошибка при получении данных через DenseTensor: {ex.Message}\n{ex.StackTrace}");
            return false;
        }
    }
}