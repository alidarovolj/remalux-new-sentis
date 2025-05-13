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

/// <summary>
/// Компонент для сегментации стен с использованием ML модели в Unity Sentis.
/// Обновлен для безопасной загрузки моделей, предотвращающей краш Unity.
/// </summary>
public class WallSegmentation : MonoBehaviour
{
    [Header("Настройки ML модели")]
    [Tooltip("ONNX модель для сегментации стен")]
    public UnityEngine.Object modelAsset;

    // Поле для хранения загруженного экземпляра модели Unity.Sentis.Model
    // Это поле не сериализуется, но может быть установлено через API/рефлексию
    [NonSerialized]
    public Model model;

    [Tooltip("Предпочитаемый бэкенд для исполнения модели (0 = CPU, 1 = GPUCompute)")]
    [Range(0, 1)]
    public int preferredBackend = 0;

    [Tooltip("Использовать безопасную асинхронную загрузку модели")]
    public bool useSafeLoading = true;

    [Tooltip("Тайм-аут загрузки модели в секундах")]
    public float modelLoadTimeout = 30f;

    [Tooltip("Принудительно использовать метод захвата изображения для XR Simulation")]
    public bool forceXRSimulationCaptureMethod = false;

    [Header("Настройки сегментации")]
    [Tooltip("Индекс класса 'стена' в выходе модели")]
    public int wallClassIndex = 1;

    [Tooltip("Порог вероятности для определения стены")]
    [Range(0.0f, 1.0f)]
    public float wallThreshold = 0.5f;

    [Tooltip("Разрешение входного изображения")]
    public Vector2Int inputResolution = new Vector2Int(320, 320);

    [Header("Компоненты")]
    [Tooltip("Ссылка на ARCameraManager")]
    public ARCameraManager arCameraManager;

    [Tooltip("Ссылка на ARSessionManager")]
    public ARSessionManager arSessionManager;

    [Tooltip("Ссылка на XROrigin")]
    public XROrigin xrOrigin;

    [Tooltip("Текстура для вывода маски сегментации")]
    public RenderTexture segmentationMaskTexture;

    [Header("Отладка")]
    [Tooltip("Сохранять маску сегментации в отдельную текстуру")]
    public bool saveDebugMask = false;

    [Tooltip("Путь для сохранения отладочных изображений")]
    public string debugSavePath = "SegmentationDebug";

    [Tooltip("Использовать симуляцию сегментации, если не удалось получить изображение с камеры")]
    public bool useSimulatedSegmentationAsFallback = true;

    [Tooltip("Счетчик неудачных попыток получения изображения перед активацией симуляции")]
    public int failCountBeforeSimulation = 10;

    // Приватные поля для работы с моделью через рефлексию
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
    // Add debugMaskCounter as a class field instead of local static variable
    private static int debugMaskCounter = 0;

    // Делегат для события инициализации модели
    public delegate void ModelInitializedHandler();
    public event ModelInitializedHandler OnModelInitialized;

    // Для связи с WallSegmentationModelLoader
    public bool IsModelInitialized => isModelInitialized;
    public bool IsInitializing => isInitializing;
    public string LastErrorMessage => lastErrorMessage;
    public bool IsInitializationFailed => isInitializationFailed;

    private void Start()
    {
        // Проверяем компоненты
        if (arCameraManager == null)
        {
            arCameraManager = FindObjectOfType<ARCameraManager>();
            if (arCameraManager == null)
            {
                Debug.LogError("ARCameraManager не найден в сцене!");
                return;
            }
        }

        // Ищем XROrigin, если она не назначена
        if (xrOrigin == null)
        {
            xrOrigin = FindObjectOfType<XROrigin>();

            // Если XROrigin не найден напрямую, пробуем получить из ARSessionManager
            if (xrOrigin == null && arSessionManager != null)
            {
                xrOrigin = arSessionManager.xrOrigin;
                Debug.Log("XROrigin получен из ARSessionManager");
            }

            if (xrOrigin == null)
            {
                Debug.LogWarning("XROrigin не найден в сцене. Это может повлиять на работу в режиме XR Simulation.");
            }
        }

        // Создаем сегментационную маску, если она не назначена
        if (segmentationMaskTexture == null)
        {
            // Создаем маску с правильным разрешением выхода модели (80x80) согласно integration_guide
            segmentationMaskTexture = new RenderTexture(80, 80, 0, RenderTextureFormat.RFloat);
            segmentationMaskTexture.enableRandomWrite = true;
            segmentationMaskTexture.Create();
        }

        // Создаем текстуру для камеры
        cameraTexture = new Texture2D(inputResolution.x, inputResolution.y, TextureFormat.RGBA32, false);

        if (useSafeLoading)
        {
            StartCoroutine(InitializeSegmentation());
        }
        else
        {
            InitializeModelDirect();
        }
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

                // Получаем BackendType.CPU (0)
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
                    if (OnModelInitialized != null) OnModelInitialized.Invoke();
                }
                else
                {
                    Debug.LogError("Не удалось создать worker для модели");
                    isModelInitialized = false;
                    isInitializationFailed = true;
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

            this.engine = new Worker(this.runtimeModel, backendType); // Используем this.engine
            this.worker = this.engine; // Также присваиваем this.worker для совместимости, если он где-то используется напрямую

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
            if (useSafeLoading)
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

    private void Update()
    {
        Debug.Log("[Update] Method called.");
        // Если модель не инициализирована, не выполняем сегментацию
        if (!isModelInitialized || engine == null) return;

        // Получаем изображение с камеры
        Texture2D cameraTex = GetCameraTexture();
        if (cameraTex == null)
        {
            // Добавлен лог: Пропускаем кадр, т.к. нет изображения с камеры
            Debug.LogWarning("[Update] Пропуск кадра: GetCameraTexture() вернул null");

            // Увеличиваем счетчик неудачных попыток
            consecutiveFailCount++;

            // Проверяем, нужно ли активировать симуляцию сегментации
            if (useSimulatedSegmentationAsFallback && consecutiveFailCount >= failCountBeforeSimulation && !usingSimulatedSegmentation)
            {
                Debug.LogWarning($"[Update] После {consecutiveFailCount} неудачных попыток активирована симуляция сегментации");
                usingSimulatedSegmentation = true;
                CreateSimulatedSegmentationMask();
            }

            return; // Выходим, если нет текстуры
        }

        // Сбрасываем счетчик неудач и флаг симуляции, если получили изображение
        consecutiveFailCount = 0;
        if (usingSimulatedSegmentation)
        {
            Debug.Log("[Update] Вернулись к нормальной работе после использования симуляции");
            usingSimulatedSegmentation = false;
        }

        // Добавлен лог: Изображение с камеры получено
        Debug.Log("[Update] Изображение с камеры получено, вызываем PerformSegmentation");

        // Отключаем сохранение отладочных масок после первых нескольких кадров, чтобы не перегружать диск
        if (saveDebugMask && debugMaskCounter > 10)
        {
            Debug.Log("Автоматическое отключение сохранения отладочных масок для повышения производительности");
            saveDebugMask = false;
        }
        else if (saveDebugMask)
        {
            debugMaskCounter++;
        }

        // Выполняем сегментацию
        PerformSegmentation(cameraTex);
    }

    /// <summary>
    /// Проверяет, выполняется ли код в режиме XR Simulation
    /// </summary>
    private bool IsRunningInXRSimulation()
    {
        // Если флаг установлен вручную, всегда возвращаем true
        if (forceXRSimulationCaptureMethod)
        {
            Debug.Log("Использование метода XR Simulation включено вручную через forceXRSimulationCaptureMethod");
            return true;
        }

        // Проверка на имя сцены
        string sceneName = UnityEngine.SceneManagement.SceneManager.GetActiveScene().name;
        if (sceneName.Contains("Simulation") || sceneName.Contains("XR"))
        {
            Debug.Log($"Обнаружен режим XR Simulation (имя сцены содержит 'Simulation' или 'XR': {sceneName})");
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
            Debug.Log("Обнаружен режим XR Simulation (найдены компоненты симуляции)");
            return true;
        }

        // Проверяем, есть ли объект "XRSimulationEnvironment" в сцене
        var simEnvObjects = FindObjectsOfType<Transform>()
            .Where(t => t.name.Contains("XRSimulation"))
            .ToArray();

        if (simEnvObjects.Length > 0)
        {
            Debug.Log("Обнаружен режим XR Simulation (найден объект XRSimulationEnvironment)");
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
                        Debug.Log($"Обнаружен провайдер XR Simulation: {type.FullName}");
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
            Debug.Log("Обнаружен режим XR Simulation (ARSession в редакторе Unity)");
            return true;
        }
#endif

        return false;
    }

    private Texture2D GetCameraTexture()
    {
        if (arCameraManager == null)
        {
            Debug.LogError("[GetCameraTexture] arCameraManager is null!");
            return null;
        }

        // Check AR session state - access it statically
        if (ARSession.state != ARSessionState.SessionTracking)
        {
            Debug.LogWarning($"[GetCameraTexture] AR сессия не в режиме отслеживания. Текущий статус: {ARSession.state}");
            return null;
        }

        // Проверяем, работаем ли мы в режиме XR Simulation
        bool isSimulation = IsRunningInXRSimulation();

        // В режиме симуляции сразу используем альтернативный метод
        if (isSimulation)
        {
            Debug.Log("[GetCameraTexture] Работа в режиме XR Simulation, используем прямой захват с камеры");
            return GetCameraTextureFromSimulation();
        }

        // Пытаемся получить изображение стандартным способом через ARCameraManager
        if (arCameraManager.TryAcquireLatestCpuImage(out XRCpuImage image))
        {
            Debug.Log("[GetCameraTexture] CPU изображение успешно получено (ID: " + image.GetHashCode() + ")");

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
            Debug.LogWarning("[GetCameraTexture] TryAcquireLatestCpuImage не удалось получить изображение. Используем альтернативный метод для XR Simulation.");

            // Альтернативный метод для XR Simulation - получение изображения с камеры напрямую
            return GetCameraTextureFromSimulation();
        }
    }

    /// <summary>
    /// Альтернативный метод получения текстуры с камеры для XR Simulation
    /// </summary>
    private Texture2D GetCameraTextureFromSimulation()
    {
        // Сначала пробуем получить текстуру напрямую из XR Simulation, если доступна
        Texture2D simulationTexture = TryGetSimulationCameraTexture();
        if (simulationTexture != null)
        {
            Debug.Log("[GetCameraTextureFromSimulation] Получена текстура напрямую из XR Simulation");
            return simulationTexture;
        }

        // Получаем камеру из XROrigin, если она доступна
        Camera arCamera = null;

        // Попытка 1: Получить камеру из ARCameraManager
        if (arCameraManager != null)
        {
            arCamera = arCameraManager.GetComponent<Camera>();
            Debug.Log("[GetCameraTextureFromSimulation] Найдена камера через ARCameraManager");
        }

        // Попытка 2: Получить камеру из XROrigin
        if (arCamera == null && xrOrigin != null)
        {
            arCamera = xrOrigin.Camera;
            Debug.Log("[GetCameraTextureFromSimulation] Найдена камера через XROrigin");
        }

        // Попытка 3: Поиск камеры с тегом MainCamera или по имени "AR Camera"
        if (arCamera == null)
        {
            arCamera = Camera.main;
            if (arCamera != null)
                Debug.Log("[GetCameraTextureFromSimulation] Найдена Main Camera");
        }

        // Попытка 4: Получить любую активную камеру в сцене
        if (arCamera == null || !arCamera.gameObject.activeInHierarchy || !arCamera.enabled)
        {
            Camera[] allCameras = FindObjectsOfType<Camera>();
            foreach (Camera cam in allCameras)
            {
                if (cam.gameObject.activeInHierarchy && cam.enabled)
                {
                    arCamera = cam;
                    Debug.Log($"[GetCameraTextureFromSimulation] Найдена активная камера: {cam.name}");
                    break;
                }
            }

            // Если все еще нет активной камеры, возьмем любую камеру и активируем ее
            if (arCamera == null && allCameras.Length > 0)
            {
                arCamera = allCameras[0];
                arCamera.gameObject.SetActive(true);
                arCamera.enabled = true;
                Debug.Log($"[GetCameraTextureFromSimulation] Активирована камера: {arCamera.name}");
            }
        }

        if (arCamera == null)
        {
            Debug.LogWarning("[GetCameraTextureFromSimulation] Не удалось найти ни одну камеру в сцене. Создаем временную камеру.");
            arCamera = CreateTemporaryCamera();

            if (arCamera == null)
            {
                Debug.LogError("[GetCameraTextureFromSimulation] Не удалось создать временную камеру.");
                return null;
            }
        }

        // Проверяем, активна ли камера и активируем ее при необходимости
        if (!arCamera.gameObject.activeInHierarchy)
        {
            Debug.LogWarning("[GetCameraTextureFromSimulation] Активируем неактивную камеру.");
            arCamera.gameObject.SetActive(true);
        }

        if (!arCamera.enabled)
        {
            Debug.LogWarning("[GetCameraTextureFromSimulation] Включаем отключенную камеру.");
            arCamera.enabled = true;
        }

        try
        {
            // Создаем временную RenderTexture с нужным разрешением
            RenderTexture tempRT = RenderTexture.GetTemporary(inputResolution.x, inputResolution.y, 24);
            RenderTexture prevRT = arCamera.targetTexture;

            // Переключаем камеру на временную текстуру
            arCamera.targetTexture = tempRT;

            // Сохраняем текущий culling mask и очистку
            int originalCullingMask = arCamera.cullingMask;
            CameraClearFlags originalClearFlags = arCamera.clearFlags;

            // Временно устанавливаем параметры для рендеринга всего
            arCamera.cullingMask = -1; // Все слои
            arCamera.clearFlags = CameraClearFlags.SolidColor;

            // Выполняем рендеринг
            arCamera.Render();

            // Восстанавливаем оригинальные параметры
            arCamera.cullingMask = originalCullingMask;
            arCamera.clearFlags = originalClearFlags;

            // Восстанавливаем исходную текстуру камеры
            arCamera.targetTexture = prevRT;

            // Активируем временную текстуру
            RenderTexture.active = tempRT;

            // Создаем текстуру, если она не существует
            if (cameraTexture == null)
            {
                cameraTexture = new Texture2D(inputResolution.x, inputResolution.y, TextureFormat.RGBA32, false);
            }

            // Считываем пиксели с активной текстуры
            cameraTexture.ReadPixels(new Rect(0, 0, inputResolution.x, inputResolution.y), 0, 0);
            cameraTexture.Apply();

            // Освобождаем ресурсы
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(tempRT);

            Debug.Log("[GetCameraTextureFromSimulation] Изображение с камеры успешно получено через XR Simulation.");
            return cameraTexture;
        }
        catch (Exception e)
        {
            Debug.LogError($"[GetCameraTextureFromSimulation] Ошибка при получении изображения с камеры: {e.Message}");
            return null;
        }
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
        try
        {
            // Убедимся, что текстура имеет нужные размеры
            if (inputTexture.width != inputResolution.x || inputTexture.height != inputResolution.y)
            {
                Debug.Log($"Изменяем размер входной текстуры с {inputTexture.width}x{inputTexture.height} на {inputResolution.x}x{inputResolution.y}");

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

            // Специальная обработка для XR Simulation
            if (IsRunningInXRSimulation())
            {
                var simulationTensor = TryCreateXRSimulationTensor(inputTexture);
                if (simulationTensor != null)
                {
                    Debug.Log("Используем специальный тензор для XR Simulation!");
                    ExecuteModelAndProcessResult(simulationTensor);
                    return;
                }
            }

            // В первую очередь пробуем использовать SentisCompat
            Type sentisCompatType = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                sentisCompatType = assembly.GetType("SentisCompat");
                if (sentisCompatType != null) break;
            }

            if (sentisCompatType != null)
            {
                // Если SentisCompat доступен, используем его методы
                var textureToTensorMethod = sentisCompatType.GetMethod("TextureToTensor",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new Type[] { typeof(Texture2D) },
                    null);

                if (textureToTensorMethod != null)
                {
                    object inputTensorCompat = textureToTensorMethod.Invoke(null, new object[] { inputTexture });
                    if (inputTensorCompat != null)
                    {
                        ExecuteModelAndProcessResult(inputTensorCompat);
                        return;
                    }
                    else
                    {
                        Debug.LogWarning("Не удалось создать тензор через SentisCompat");
                    }
                }
            }

            // Если SentisCompat не сработал, пробуем стандартный API
            Type textureConverterType = Type.GetType("Unity.Sentis.TextureConverter, Unity.Sentis");
            if (textureConverterType == null)
            {
                Debug.LogError("Тип Unity.Sentis.TextureConverter не найден");
                RenderSimpleMask();
                return;
            }

            // Прямой подход с TextureConverter
            Tensor<float> inputTensor = null;
            try
            {
                // Прямой вызов API Sentis 2.x
                // Исправление: явно указываем каналы (3 для RGB)
                TextureTransform transform = new TextureTransform()
                    .SetDimensions(inputResolution.x, inputResolution.y, 3); // Явно указываем 3 канала (RGB)

                // Создаем или переиспользуем тензор
                if (inputTensor == null)
                {
                    TensorShape inputShape = new TensorShape(1, inputResolution.y, inputResolution.x, 3);
                    inputTensor = new Tensor<float>(inputShape);
                }

                // Для отладки логируем размеры текстуры
                Debug.Log($"Преобразование текстуры: {inputTexture.width}x{inputTexture.height}, формат: {inputTexture.format}");

                // Используем модифицированную трансформацию
                TextureConverter.ToTensor(inputTexture, inputTensor, transform);

                Debug.Log("Успешно создан тензор через TextureConverter!");
                ExecuteModelAndProcessResult(inputTensor);
                return;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Не удалось создать тензор через TextureConverter: {e.Message}");

                // Попробуем альтернативный подход с другими параметрами трансформации
                try
                {
                    Debug.Log("Пробуем альтернативный подход с TextureConverter...");

                    // Создаем тензор с размерами NCHW
                    TensorShape compatShape = new TensorShape(1, 3, inputResolution.y, inputResolution.x);
                    Tensor<float> compatTensor = new Tensor<float>(compatShape);

                    // Используем альтернативные параметры
                    TextureTransform alternativeTransform = new TextureTransform()
                        .SetDimensions(inputResolution.x, inputResolution.y, -1); // -1 для автоопределения

                    TextureConverter.ToTensor(inputTexture, compatTensor, alternativeTransform);

                    Debug.Log("Альтернативный подход с TextureConverter успешен!");
                    ExecuteModelAndProcessResult(compatTensor);
                    return;
                }
                catch (Exception altEx)
                {
                    Debug.LogWarning($"Альтернативный подход тоже не сработал: {altEx.Message}");
                }

                // Пробуем наши методы ручного создания тензоров
                try
                {
                    Debug.Log("Пробуем создать NHWC тензор напрямую из пикселей...");
                    var pixelTensor = CreateTensorFromPixels(inputTexture);
                    if (pixelTensor != null)
                    {
                        Debug.Log("Успешно создан NHWC тензор из пикселей!");
                        ExecuteModelAndProcessResult(pixelTensor);
                        return;
                    }
                }
                catch (Exception pixelEx)
                {
                    Debug.LogWarning($"Не удалось создать NHWC тензор из пикселей: {pixelEx.Message}");
                }

                // Пробуем NCHW формат
                try
                {
                    Debug.Log("Пробуем создать NCHW тензор из пикселей...");
                    var nchwTensor = CreateNCHWTensorFromPixels(inputTexture);
                    if (nchwTensor != null)
                    {
                        Debug.Log("Успешно создан NCHW тензор из пикселей!");
                        ExecuteModelAndProcessResult(nchwTensor);
                        return;
                    }
                }
                catch (Exception nchwEx)
                {
                    Debug.LogWarning($"Не удалось создать NCHW тензор из пикселей: {nchwEx.Message}");
                }

                // Пробуем одноканальный тензор (для моделей, работающих с grayscale)
                try
                {
                    Debug.Log("Пробуем создать одноканальный тензор...");
                    var grayTensor = CreateSingleChannelTensor(inputTexture);
                    if (grayTensor != null)
                    {
                        Debug.Log("Успешно создан одноканальный тензор!");
                        ExecuteModelAndProcessResult(grayTensor);
                        return;
                    }
                }
                catch (Exception grayEx)
                {
                    Debug.LogWarning($"Не удалось создать одноканальный тензор: {grayEx.Message}");
                }

                // Попытка рефлексии как последний вариант
                var toTensorMethod = textureConverterType.GetMethod("ToTensor", new Type[] { typeof(Texture) });
                if (toTensorMethod != null)
                {
                    Debug.Log("Используем метод ToTensor через рефлексию");
                    try
                    {
                        object inputTensorObj = toTensorMethod.Invoke(null, new object[] { inputTexture });
                        if (inputTensorObj != null)
                        {
                            ExecuteModelAndProcessResult(inputTensorObj);
                            return;
                        }
                    }
                    catch (Exception reflectionEx)
                    {
                        Debug.LogWarning($"Ошибка при вызове ToTensor через рефлексию: {reflectionEx.Message}");
                    }
                }
            }

            // Если не удалось создать тензор ни одним из доступных методов, используем заглушку
            Debug.LogWarning("Не удалось создать тензор ни одним из доступных методов, используем заглушку");
            RenderSimpleMask();
        }
        catch (Exception e)
        {
            Debug.LogError($"Ошибка при выполнении сегментации: {e.Message}\nStackTrace: {e.StackTrace}");
            RenderSimpleMask();
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
    /// Выполняет модель и обрабатывает результат
    /// </summary>
    private void ExecuteModelAndProcessResult(object inputTensor)
    {
        if (inputTensor == null) return;

        // List to track any additional tensors we create that need disposal
        List<IDisposable> tensorsToDispose = new List<IDisposable>();

        try
        {
            Debug.Log("=== ExecuteModelAndProcessResult с использованием API Sentis 2.1.2.0 ===");

            // 1. Устанавливаем входной тензор через SetInput
            try
            {
                engine.SetInput(0, inputTensor as Tensor); // Используем прямой вызов SetInput
                Debug.Log("Входной тензор успешно установлен через SetInput(0, tensor)");
            }
            catch (Exception e)
            {
                Debug.LogError($"Ошибка при установке входного тензора: {e.Message}");
                return;
            }

            // 2. Планируем выполнение с помощью Schedule
            try
            {
                engine.Schedule(); // Вызываем метод Schedule для запуска вычисления
                Debug.Log("Выполнение запланировано через Schedule()");
            }
            catch (Exception e)
            {
                Debug.LogError($"Ошибка при вызове Schedule: {e.Message}");

                // Пробуем альтернативную версию Schedule с явным указанием входа
                try
                {
                    engine.Schedule(inputTensor as Tensor);
                    Debug.Log("Выполнение запланировано через Schedule(tensor)");
                }
                catch (Exception e2)
                {
                    Debug.LogError($"Ошибка при вызове Schedule(tensor): {e2.Message}");
                    return;
                }
            }

            // 3. Важно: Ждем завершения выполнения всех операций
            try
            {
                // Сначала пробуем через WaitForCompletion
                MethodInfo waitForCompletionMethod = engine.GetType().GetMethod("WaitForCompletion");
                if (waitForCompletionMethod != null)
                {
                    waitForCompletionMethod.Invoke(engine, null);
                    Debug.Log("Выполнение завершено через WaitForCompletion");
                }
                else
                {
                    // Пробуем через CompleteDependencies
                    MethodInfo completeDependenciesMethod = engine.GetType().GetMethod("CompleteDependencies");
                    if (completeDependenciesMethod != null)
                    {
                        completeDependenciesMethod.Invoke(engine, null);
                        Debug.Log("Выполнение завершено через CompleteDependencies");
                    }
                    else
                    {
                        // Используем ScheduleIterable как запасной вариант
                        IEnumerator iterator = engine.ScheduleIterable();

                        // Ждем завершения итератора
                        while (iterator.MoveNext())
                        {
                            // Продолжаем ожидание
                        }
                        Debug.Log("Выполнение завершено через ScheduleIterable");
                    }
                }

                // Дополнительное ожидание для гарантии завершения вычислений (особенно на GPU)
                // Это может помочь избежать ошибки "Tensor data is still pending"
                System.Threading.Thread.Sleep(10);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Ошибка при ожидании завершения: {e.Message}. Пробуем продолжить.");
            }

            // 4. Получаем выходной тензор через PeekOutput
            Tensor outputTensor = null;
            try
            {
                outputTensor = engine.PeekOutput(); // Используем прямой вызов PeekOutput
                Debug.Log("Выходной тензор успешно получен через PeekOutput()");

                // Проверяем, есть ли метод для синхронизации данных тензора
                MethodInfo syncMethod = outputTensor.GetType().GetMethod("Sync", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (syncMethod != null)
                {
                    syncMethod.Invoke(outputTensor, null);
                    Debug.Log("Тензор синхронизирован через Sync()");
                }

                // Пробуем альтернативный способ синхронизации данных для Sentis 2.1.2
                try
                {
                    var syncData = outputTensor.GetType().GetMethod("SyncData",
                        BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (syncData != null)
                    {
                        syncData.Invoke(outputTensor, null);
                        Debug.Log("Тензор синхронизирован через SyncData()");
                    }
                }
                catch (Exception)
                {
                    // Игнорируем ошибки при попытке вызова SyncData
                }

                // Дополнительное ожидание после синхронизации
                System.Threading.Thread.Sleep(5);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Ошибка при вызове PeekOutput(): {e.Message}");

                try
                {
                    // Последняя попытка - через PeekOutput с индексом
                    outputTensor = engine.PeekOutput(0);
                    Debug.Log("Выходной тензор успешно получен через PeekOutput(0)");

                    // Проверяем, есть ли метод для синхронизации данных тензора
                    MethodInfo syncMethod = outputTensor.GetType().GetMethod("Sync", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (syncMethod != null)
                    {
                        syncMethod.Invoke(outputTensor, null);
                        Debug.Log("Тензор синхронизирован через Sync()");
                    }

                    // Дополнительное ожидание
                    System.Threading.Thread.Sleep(5);
                }
                catch (Exception e2)
                {
                    Debug.LogError($"Ошибка при вызове PeekOutput(0): {e2.Message}");
                    return;
                }
            }

            if (outputTensor == null)
            {
                Debug.LogError("Не удалось получить выходной тензор");
                return;
            }

            // Если не удалось синхронизировать данные, создаем симуляцию
            Tensor<float> simulationTensor = null;
            if (outputTensor != null)
            {
                try
                {
                    // Быстрая проверка - попытка доступа к значению
                    // Если данные не готовы, будет исключение
                    if (outputTensor is Tensor<float> floatTensor)
                    {
                        float test = floatTensor[0, 0, 0, 0]; // Проверяем доступность данных
                        Debug.Log("Проверка доступа к тензору успешна");
                    }
                    else
                    {
                        // Если тензор не является Tensor<float>, нужна другая стратегия
                        // Попробуем использовать симуляцию
                        Debug.LogWarning("Выходной тензор не является Tensor<float>, используем симуляцию");
                        simulationTensor = CreateSimulatedTensor(outputTensor.shape);
                        tensorsToDispose.Add(simulationTensor);
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"Данные тензора не готовы: {e.Message}. Используем симуляцию.");
                    simulationTensor = CreateSimulatedTensor(outputTensor.shape);
                    tensorsToDispose.Add(simulationTensor);
                }
            }

            // 5. Обрабатываем результат
            Debug.Log($"Обрабатываем выходной тензор типа {outputTensor.GetType().FullName}");
            if (simulationTensor != null)
            {
                ProcessSegmentationResult(simulationTensor);
            }
            else
            {
                ProcessSegmentationResult(outputTensor);
            }

            // ВАЖНО: НЕ освобождаем outputTensor здесь, т.к. он принадлежит движку и будет освобожден им
            // PeekOutput возвращает ссылку на внутренний тензор движка
        }
        catch (Exception e)
        {
            Debug.LogError($"Общая ошибка при обработке модели: {e.Message}\nStackTrace: {e.StackTrace}");
        }
        finally
        {
            // 6. Освобождаем входной тензор, если он реализует IDisposable и не передан извне
            // Проверяем не принадлежит ли он Engine
            if (inputTensor is IDisposable disposable && !(inputTensor == engine.PeekOutput())) // Заменяем PeekConstants на более простую проверку
            {
                disposable.Dispose();
                Debug.Log("Входной тензор освобожден");
            }

            // Освобождаем все созданные тензоры
            foreach (var tensor in tensorsToDispose)
            {
                try
                {
                    tensor.Dispose();
                }
                catch (Exception ex)
                {
                    Debug.LogWarning($"Ошибка при освобождении тензора: {ex.Message}");
                }
            }
            tensorsToDispose.Clear();
        }
    }

    /// <summary>
    /// Создает тензор-заглушку с основными стенами в центре кадра для тестирования
    /// </summary>
    private Tensor<float> CreateSimulatedTensor(TensorShape shape)
    {
        Tensor<float> tensor = null;
        try
        {
            int batchSize = shape[0];
            int dim1 = shape[1];
            int dim2 = shape[2];
            int dim3 = 1;

            if (shape.rank > 3)
            {
                dim3 = shape[3];
            }

            // Если форма не соответствует ожидаемой, адаптируемся
            int channels = 0, height = 0, width = 0;
            bool isNCHW = false;

            // Адаптивное определение формата данных в зависимости от размеров 
            if (shape.rank == 4 && dim1 == 150)
            {
                // Это формат (1, 150, h, w) или (1, 150, h, 1) с классами сегментации
                channels = dim1;  // 150 классов
                height = dim2;    // высота
                width = dim3 > 1 ? dim3 : dim2;  // ширина (если dim3=1, делаем квадратным)
                isNCHW = true;
            }
            else if (shape.rank == 4 && dim3 == 150)
            {
                // Формат (1, h, w, 150) с классами сегментации
                channels = dim3;  // 150 классов
                height = dim1;    // высота
                width = dim2;     // ширина
                isNCHW = false;
            }
            else
            {
                // Просто используем размеры как есть
                isNCHW = dim1 < dim2;
                if (isNCHW)
                {
                    channels = dim1;
                    height = dim2;
                    width = dim3;
                }
                else
                {
                    channels = dim3;
                    height = dim1;
                    width = dim2;
                }
            }

            Debug.Log($"Создаем симуляцию тензора с размерами [{batchSize}, {channels}, {height}, {width}], формат {(isNCHW ? "NCHW" : "NHWC")}");

            // Создаем массив данных нужного размера
            float[] data = new float[shape.length];

            // Получаем текущее направление камеры для более реалистичного паттерна стен
            Vector3 cameraForward = Vector3.forward;
            float cameraRotationY = 0f;

            if (arCameraManager != null && arCameraManager.transform != null)
            {
                cameraForward = arCameraManager.transform.forward;
                cameraRotationY = arCameraManager.transform.eulerAngles.y;
            }
            else if (Camera.main != null)
            {
                cameraForward = Camera.main.transform.forward;
                cameraRotationY = Camera.main.transform.eulerAngles.y;
            }

            // Определяем, в каком направлении камера смотрит (примерно)
            bool lookingNorth = (cameraRotationY > 315 || cameraRotationY < 45);
            bool lookingEast = (cameraRotationY >= 45 && cameraRotationY < 135);
            bool lookingSouth = (cameraRotationY >= 135 && cameraRotationY < 225);
            bool lookingWest = (cameraRotationY >= 225 && cameraRotationY < 315);

            // Для ADE20K модели с 150 классами:
            int wallClassIndex = 1;   // wall в ADE20K
            // int buildingClassIndex = 2;  // building - unused
            int floorClassIndex = 3;  // floor
            int doorClassIndex = 11;  // door
            int windowClassIndex = 8;  // window

            // Заполняем все каналы соответствующими значениями
            if (isNCHW)
            {
                // Формат NCHW - (batch, channels, height, width)
                for (int h = 0; h < height; h++)
                {
                    for (int w = 0; w < width; w++)
                    {
                        // Нормализованные координаты (0.0 - 1.0)
                        float normalizedX = w / (float)width;
                        float normalizedY = h / (float)height;

                        // Определяем различные зоны для симуляции сегментации
                        bool isLeftWall = normalizedX < 0.1f;
                        bool isRightWall = normalizedX > 0.9f;
                        bool isTopWall = normalizedY < 0.1f;
                        bool isBottomWall = normalizedY > 0.9f;
                        bool isDoorway = false;

                        // Создаем "дверной проем" в стене в направлении взгляда
                        if (lookingNorth && isBottomWall && normalizedX > 0.4f && normalizedX < 0.6f)
                            isDoorway = true;
                        else if (lookingEast && isLeftWall && normalizedY > 0.4f && normalizedY < 0.6f)
                            isDoorway = true;
                        else if (lookingSouth && isTopWall && normalizedX > 0.4f && normalizedX < 0.6f)
                            isDoorway = true;
                        else if (lookingWest && isRightWall && normalizedY > 0.4f && normalizedY < 0.6f)
                            isDoorway = true;

                        // Проверяем принадлежность к стене, двери или окну
                        bool isWall = (isLeftWall || isRightWall || isTopWall || isBottomWall) && !isDoorway;
                        bool isDoor = isDoorway;
                        bool isWindow = false;

                        // Добавляем окна в стенах
                        if (isWall && !isDoor)
                        {
                            // Левая стена
                            if (isLeftWall && normalizedY > 0.2f && normalizedY < 0.4f)
                                isWindow = true;
                            // Правая стена
                            if (isRightWall && normalizedY > 0.2f && normalizedY < 0.4f)
                                isWindow = true;
                            // Верхняя стена
                            if (isTopWall && normalizedX > 0.2f && normalizedX < 0.4f)
                                isWindow = true;
                            // Нижняя стена
                            if (isBottomWall && normalizedX > 0.2f && normalizedX < 0.4f)
                                isWindow = true;
                        }

                        // Если это стена, то ставим высокое значение в канале стен
                        if (isWall && !isWindow)
                        {
                            // Устанавливаем значение для класса стены
                            int index = ((0 * channels + wallClassIndex) * height + h) * width + w;
                            if (index < data.Length)
                            {
                                data[index] = 0.95f + UnityEngine.Random.Range(-0.05f, 0.05f);
                            }
                        }
                        // Если это окно, ставим значение в канал окон
                        else if (isWindow)
                        {
                            // Класс окна
                            int index = ((0 * channels + windowClassIndex) * height + h) * width + w;
                            if (index < data.Length)
                            {
                                data[index] = 0.90f + UnityEngine.Random.Range(-0.05f, 0.05f);
                            }
                        }
                        // Если это дверь, ставим значение в канал дверей
                        else if (isDoor)
                        {
                            // Класс двери
                            int index = ((0 * channels + doorClassIndex) * height + h) * width + w;
                            if (index < data.Length)
                            {
                                data[index] = 0.85f + UnityEngine.Random.Range(-0.05f, 0.05f);
                            }
                        }
                        // Для остальных пикселей - значение для пола
                        else
                        {
                            // Класс пола
                            int index = ((0 * channels + floorClassIndex) * height + h) * width + w;
                            if (index < data.Length)
                            {
                                data[index] = 0.80f + UnityEngine.Random.Range(-0.05f, 0.05f);
                            }
                        }
                    }
                }
            }
            else
            {
                // Формат NHWC - обработка при необходимости
                // По аналогии с NCHW, но с другим расположением каналов
                Debug.Log("Симуляция для формата NHWC не реализована, используем базовый подход");

                // Заполняем все нулями, кроме канала стен
                for (int i = 0; i < data.Length; i++)
                {
                    data[i] = 0.05f;  // Базовое значение для всех каналов
                }

                // Добавляем стены в соответствующих местах
                for (int h = 0; h < height; h++)
                {
                    for (int w = 0; w < width; w++)
                    {
                        float normalizedX = w / (float)width;
                        float normalizedY = h / (float)height;

                        // Простая логика для стен по периметру
                        bool isWall = normalizedX < 0.1f || normalizedX > 0.9f ||
                                     normalizedY < 0.1f || normalizedY > 0.9f;

                        if (isWall)
                        {
                            // Для NHWC индекс вычисляется по-другому
                            int index = ((0 * height + h) * width + w) * channels + wallClassIndex;
                            if (index < data.Length)
                            {
                                data[index] = 0.95f;
                            }
                        }
                    }
                }
            }

            // Создаем тензор из данных
            tensor = new Tensor<float>(shape, data);

            Debug.Log("Тензор успешно создан из массива данных");
            return tensor;
        }
        catch (Exception e)
        {
            Debug.LogError($"Ошибка при создании симуляции тензора: {e.Message}");

            // Безопасное освобождение ресурсов при ошибке
            if (tensor != null)
            {
                try
                {
                    tensor.Dispose();
                }
                catch (Exception disposeEx)
                {
                    Debug.LogWarning($"Ошибка при освобождении тензора: {disposeEx.Message}");
                }
            }

            // Создаем простой тензор с базовыми значениями
            float[] fallbackData = new float[shape.length];
            // Заполняем середину значением 1.0 для класса стены
            int centerIndex = fallbackData.Length / 2;
            if (centerIndex < fallbackData.Length)
            {
                fallbackData[centerIndex] = 1.0f;
            }

            // Создаем новый тензор в случае ошибки
            return new Tensor<float>(shape, fallbackData);
        }
    }

    /// <summary>
    /// Обрабатывает результат сегментации и обновляет маску
    /// </summary>
    private void ProcessSegmentationResult(object outputTensorObj)
    {
        Debug.Log("[ProcessSegmentationResult] Вызван.");

        // Список для отслеживания тензоров, которые нужно освободить
        List<IDisposable> tensorsToDispose = new List<IDisposable>();

        try
        {
            // Проверяем, что segmentationMaskTexture существует и готова
            if (segmentationMaskTexture == null || !segmentationMaskTexture.IsCreated())
            {
                Debug.LogWarning("SegmentationMaskTexture не готова для записи. Создаем новую текстуру.");

                // Освобождаем старую текстуру, если она существует
                if (segmentationMaskTexture != null)
                {
                    segmentationMaskTexture.Release();
                    Destroy(segmentationMaskTexture);
                }

                // Создаем новую текстуру с нужными параметрами - используем рекомендуемый размер из integration_guide 
                segmentationMaskTexture = new RenderTexture(80, 80, 0, RenderTextureFormat.ARGB32);
                segmentationMaskTexture.enableRandomWrite = true;
                segmentationMaskTexture.Create();

                if (!segmentationMaskTexture.IsCreated())
                {
                    Debug.LogError("Не удалось создать RenderTexture. Используем заглушку.");
                    RenderSimpleMask();
                    return;
                }

                Debug.Log($"Создана новая RenderTexture {segmentationMaskTexture.width}x{segmentationMaskTexture.height}");
            }

            // Пытаемся безопасно привести к разным типам тензора
            Tensor<float> outputTensorFloat = null;
            Tensor genericTensor = null;

            // Проверяем тип тензора и безопасно приводим к нужному типу
            if (outputTensorObj is Tensor<float>)
            {
                outputTensorFloat = outputTensorObj as Tensor<float>;
                genericTensor = outputTensorObj as Tensor;
                Debug.Log("[ProcessSegmentationResult] Успешно приведено к Tensor<float>.");
            }
            else if (outputTensorObj is Tensor)
            {
                genericTensor = outputTensorObj as Tensor;

                // Пробуем преобразовать к Tensor<float> если это возможно
                try
                {
                    // Копируем данные в новый тензор типа Tensor<float>
                    TensorShape shape = genericTensor.shape;
                    outputTensorFloat = new Tensor<float>(shape);
                    // Этот тензор нужно будет освободить
                    tensorsToDispose.Add(outputTensorFloat);

                    // Попытаемся скопировать данные с помощью рефлексии
                    Debug.Log($"[ProcessSegmentationResult] Приводим Tensor к Tensor<float> для формы {shape}");

                    // Если доступны методы копирования, используем их
                    bool dataCopied = false;

                    // Вариант 1: Если у источника есть метод ReadData с правильной сигнатурой
                    MethodInfo readDataMethod = genericTensor.GetType().GetMethod("ReadData",
                        BindingFlags.Public | BindingFlags.Instance);

                    if (readDataMethod != null && !dataCopied)
                    {
                        try
                        {
                            // Получаем данные из тензора
                            float[] data = new float[shape.length];
                            readDataMethod.Invoke(genericTensor, new object[] { data });

                            // Заполняем наш Tensor<float>
                            // Рассчитываем координаты вручную в зависимости от ранга тензора
                            if (shape.rank == 4)
                            {
                                int dim0 = shape[0];
                                int dim1 = shape[1];
                                int dim2 = shape[2];
                                int dim3 = shape[3];

                                for (int i = 0; i < shape.length; i++)
                                {
                                    // Вычисляем координаты из линейного индекса
                                    int d3 = i % dim3;
                                    int d2 = (i / dim3) % dim2;
                                    int d1 = (i / (dim3 * dim2)) % dim1;
                                    int d0 = i / (dim3 * dim2 * dim1);

                                    // Устанавливаем значение по координатам
                                    outputTensorFloat[d0, d1, d2, d3] = data[i];
                                }
                            }
                            else if (shape.rank == 3)
                            {
                                int dim0 = shape[0];
                                int dim1 = shape[1];
                                int dim2 = shape[2];

                                for (int i = 0; i < shape.length; i++)
                                {
                                    // Вычисляем координаты из линейного индекса
                                    int d2 = i % dim2;
                                    int d1 = (i / dim2) % dim1;
                                    int d0 = i / (dim2 * dim1);

                                    // Устанавливаем значение по координатам
                                    outputTensorFloat[d0, d1, d2] = data[i];
                                }
                            }
                            else if (shape.rank == 2)
                            {
                                int dim0 = shape[0];
                                int dim1 = shape[1];

                                for (int i = 0; i < shape.length; i++)
                                {
                                    // Вычисляем координаты из линейного индекса
                                    int d1 = i % dim1;
                                    int d0 = i / dim1;

                                    // Устанавливаем значение по координатам
                                    outputTensorFloat[d0, d1] = data[i];
                                }
                            }
                            else if (shape.rank == 1)
                            {
                                for (int i = 0; i < shape.length; i++)
                                {
                                    outputTensorFloat[i] = data[i];
                                }
                            }
                            else
                            {
                                Debug.LogWarning($"[ProcessSegmentationResult] Неподдерживаемый ранг тензора: {shape.rank}");
                                throw new Exception("Неподдерживаемый ранг тензора");
                            }

                            dataCopied = true;
                            Debug.Log("[ProcessSegmentationResult] Данные скопированы через ReadData.");
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"[ProcessSegmentationResult] Не удалось скопировать данные через ReadData: {e.Message}");
                        }
                    }

                    // Если не удалось скопировать, используем симуляцию
                    if (!dataCopied)
                    {
                        Debug.LogWarning("[ProcessSegmentationResult] Не удалось преобразовать Tensor в Tensor<float>. Используем симуляцию.");

                        // Освобождаем неиспользуемый тензор
                        outputTensorFloat.Dispose();
                        tensorsToDispose.Remove(outputTensorFloat);

                        // Пробуем создать Tensor<float> напрямую с заглушечными данными
                        try
                        {
                            // Создаем упрощенный тензор с простым паттерном данных
                            float[] simData = new float[shape.length];
                            for (int i = 0; i < shape.length; i++)
                            {
                                // Заполняем центральную область простым паттерном для стен
                                // В центре ставим 1.0, по краям 0.0, плавный градиент между ними

                                // В тензоре NCHW, допустим что первые два измерения - batch и channels
                                // Третье и четвертое - высота и ширина, на них и создаем паттерн
                                if (shape.rank == 4)
                                {
                                    int dim0 = shape[0];
                                    int dim1 = shape[1];
                                    int dim2 = shape[2]; // высота
                                    int dim3 = shape[3]; // ширина

                                    // Координаты текущей ячейки
                                    int d3 = i % dim3;
                                    int d2 = (i / dim3) % dim2;
                                    int d1 = (i / (dim3 * dim2)) % dim1;
                                    int d0 = i / (dim3 * dim2 * dim1);

                                    // Пробуем заполнить только один канал (тот, что указан как wallClassIndex)
                                    // Если wallClassIndex некорректный, используем канал 0
                                    int targetChannel = wallClassIndex;
                                    if (targetChannel < 0 || targetChannel >= dim1)
                                    {
                                        targetChannel = 0;
                                    }

                                    if (d1 == targetChannel)
                                    {
                                        // Нормализованные координаты в пространстве высота/ширина
                                        float normX = d3 / (float)dim3;
                                        float normY = d2 / (float)dim2;

                                        // Расстояние от центра (0.5, 0.5) - от 0.0 до ~0.7
                                        float distFromCenter = Mathf.Sqrt((normX - 0.5f) * (normX - 0.5f) +
                                                                         (normY - 0.5f) * (normY - 0.5f));

                                        // Значение 1.0 в центре, 0.0 по краям с плавным переходом
                                        if (distFromCenter < 0.2f)
                                        {
                                            // Центральная область - стены с высокой вероятностью
                                            simData[i] = 1.0f;
                                        }
                                        else if (distFromCenter < 0.4f)
                                        {
                                            // Промежуточная область - градиент
                                            simData[i] = Mathf.Lerp(1.0f, 0.0f, (distFromCenter - 0.2f) / 0.2f);
                                        }
                                        else
                                        {
                                            // Внешняя область - не стены
                                            simData[i] = 0.0f;
                                        }
                                    }
                                    else
                                    {
                                        // Для остальных каналов нули
                                        simData[i] = 0.0f;
                                    }
                                }
                                else if (shape.rank <= 3)
                                {
                                    // Для тензоров меньшего ранга создаем простой паттерн
                                    // в центральной области
                                    int totalCells = shape.length;
                                    float normalizedPos = i / (float)totalCells;

                                    // Значение выше в середине массива, ниже по краям
                                    float value = 0.0f;

                                    if (normalizedPos > 0.3f && normalizedPos < 0.7f)
                                    {
                                        value = 1.0f;
                                    }
                                    else if (normalizedPos > 0.2f && normalizedPos < 0.8f)
                                    {
                                        value = 0.6f;
                                    }

                                    simData[i] = value;
                                }
                            }

                            // Создаем тензор из данных
                            outputTensorFloat = new Tensor<float>(shape, simData);
                            tensorsToDispose.Add(outputTensorFloat);
                            dataCopied = true;
                            Debug.Log("[ProcessSegmentationResult] Создан синтетический тензор с заглушечными данными.");
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"[ProcessSegmentationResult] Не удалось создать заглушечный тензор: {e.Message}");
                            // Продолжаем и используем CreateSimulatedTensor как последний вариант
                        }

                        // Если всё ещё не удалось, используем CreateSimulatedTensor
                        if (!dataCopied)
                        {
                            outputTensorFloat = CreateSimulatedTensor(shape);
                            if (outputTensorFloat != null)
                                tensorsToDispose.Add(outputTensorFloat);
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.LogError($"[ProcessSegmentationResult] Ошибка при преобразовании тензора: {e.Message}");

                    // Освобождаем созданные до ошибки тензоры
                    foreach (var disposable in tensorsToDispose)
                    {
                        try { disposable.Dispose(); } catch { }
                    }
                    tensorsToDispose.Clear();

                    // Создаем искусственный тензор для отображения
                    outputTensorFloat = CreateSimulatedTensor(genericTensor.shape);
                    if (outputTensorFloat != null)
                        tensorsToDispose.Add(outputTensorFloat);
                }
            }
            else
            {
                Debug.LogError($"[ProcessSegmentationResult] Выходной тензор имеет неожиданный тип: {outputTensorObj?.GetType().FullName ?? "null"}");
                RenderSimpleMask();
                return;
            }

            // Проверяем, что успешно получили тензор
            if (outputTensorFloat == null)
            {
                Debug.LogError("[ProcessSegmentationResult] Не удалось получить тензор для обработки");
                RenderSimpleMask();
                return;
            }

            try
            {
                // Вызываем новый метод для обработки
                ProcessTensorManual(outputTensorFloat);

                // Отладочное сохранение маски, если включено
                if (saveDebugMask)
                {
                    SaveDebugMask();
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Ошибка при ручной обработке результата сегментации: {e.Message}\nStackTrace: {e.StackTrace}");
                RenderSimpleMask(); // Используем заглушку в случае ошибки
            }
            finally
            {
                // Освобождаем все локально созданные тензоры
                foreach (var disposable in tensorsToDispose)
                {
                    try
                    {
                        disposable.Dispose();
                    }
                    catch (Exception ex)
                    {
                        Debug.LogWarning($"Ошибка при освобождении тензора: {ex.Message}");
                    }
                }
                tensorsToDispose.Clear();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"[ProcessSegmentationResult] Общая ошибка: {e.Message}");
            RenderSimpleMask();

            // Освобождаем тензоры в случае исключения
            foreach (var disposable in tensorsToDispose)
            {
                try { disposable.Dispose(); } catch { }
            }
        }
    }

    /// <summary>
    /// Вручную обрабатывает тензор сегментации и записывает результат в segmentationMaskTexture.
    /// </summary>
    private void ProcessTensorManual(Tensor<float> tensor)
    {
        Debug.Log("[ProcessTensorManual] Вызван.");

        // Получаем размеры тензора
        // Предполагаем формат NHWC [batch, height, width, channels] или NCHW [batch, channels, height, width]
        // Sentis обычно использует NCHW для выходов моделей сегментации
        TensorShape shape = tensor.shape;
        int batch = shape[0];
        int dim1 = shape[1]; // Может быть channels или height
        int dim2 = shape[2]; // Может быть height или width
        int dim3 = 1; // По умолчанию 1, если shape.rank < 4

        if (shape.rank > 3)
        {
            dim3 = shape[3]; // Может быть width или channels
        }

        // Добавлен детальный лог формы
        Debug.Log($"[ProcessTensorManual] Обработка тензора с формой: {shape}. Batch={batch}, Dim1={dim1}, Dim2={dim2}, Dim3={dim3}. Target Class={wallClassIndex}, Threshold={wallThreshold}");

        // Адаптивное определение формата данных в зависимости от размеров тензора
        bool isNCHW = false;
        int channels = 0, height = 0, width = 0;

        // Специальная обработка для тензора формата (1, 150, 56, 1) или (1, 150, 80, 80)
        if (shape.rank == 4 && dim1 == 150)
        {
            // Для формы (1, 150, 56, 1) или подобных от модели, где 150 это классы
            channels = dim1;  // 150 классов
            height = dim2;    // 56 или 80
            width = dim3 > 1 ? dim3 : dim2;  // Если dim3=1, используем dim2 и для ширины (квадратная форма)
            isNCHW = true;    // Формат NCHW
            Debug.Log($"[ProcessTensorManual] Обнаружен формат тензора NCHW с {channels} классами");
        }
        else if (shape.rank == 4 && dim3 == 150)
        {
            // Для формы (1, h, w, 150), где 150 это классы в NHWC формате
            height = dim1;
            width = dim2;
            channels = dim3;  // 150 классов
            isNCHW = false;   // Формат NHWC
            Debug.Log($"[ProcessTensorManual] Обнаружен формат тензора NHWC с {channels} классами");
        }
        else
        {
            // Определяем по размерам, какая форма вероятнее
            isNCHW = dim1 < dim2; // Обычно каналов меньше, чем высота

            if (isNCHW)
            {
                channels = dim1;
                height = dim2;
                width = dim3;
            }
            else
            {
                // NHWC формат
                height = dim1;
                width = dim2;
                channels = dim3;
            }
        }

        // Проверяем валидность wallClassIndex для модели с 150 классами
        if (wallClassIndex < 0 || wallClassIndex >= channels)
        {
            int defaultWallClassIndex = 0;
            // Классы для ADE20K (используемые в моделях сегментации с 150 классами):
            // Класс 1: стена (wall)
            // Класс 2: здание (building)
            // Класс 11: дверь (door)
            // Класс 8: окно (window)

            if (channels == 150)
            {
                defaultWallClassIndex = 1; // wall в ADE20K
            }

            Debug.LogWarning($"Неверный WallClassIndex ({wallClassIndex}). Должен быть между 0 и {channels - 1}. Используем класс {defaultWallClassIndex}.");
            wallClassIndex = defaultWallClassIndex;
        }

        // Проверяем соответствие размеров текстуры и тензора
        if (segmentationMaskTexture.width != width || segmentationMaskTexture.height != height)
        {
            Debug.LogWarning($"Размер RenderTexture ({segmentationMaskTexture.width}x{segmentationMaskTexture.height}) не совпадает с размером выхода модели ({width}x{height}). Пересоздаем текстуру.");

            // Освобождаем существующую текстуру
            segmentationMaskTexture.Release();

            // Создаем новую с правильными размерами
            segmentationMaskTexture = new RenderTexture(width, height, 0, RenderTextureFormat.RFloat);
            segmentationMaskTexture.enableRandomWrite = true;
            segmentationMaskTexture.Create();

            Debug.Log($"Создана новая RenderTexture {width}x{height} для соответствия выходу модели");
        }

        Debug.Log("[ProcessTensorManual] Попытка получить данные тензора...");

        try
        {
            // Создаем массив пикселей для временной текстуры
            // Texture2D работает с Color, но Color32 может быть быстрее
            Color32[] pixels = new Color32[segmentationMaskTexture.width * segmentationMaskTexture.height];

            // Вычисляем коэффициенты масштабирования, если размеры не совпадают
            float scaleX = width / (float)segmentationMaskTexture.width;
            float scaleY = height / (float)segmentationMaskTexture.height;

            // Если тензор состоит только из одного канала, обрабатываем его особым образом
            // Адаптируемся под форму тензора из логов: (1, 150, 56, 1)
            bool isSingleChannel = channels == 1 || (shape.length == 1 * 150 * 56 * 1);

            if (isSingleChannel)
            {
                Debug.Log("[ProcessTensorManual] Обрабатываем одноканальный тензор. Форма:" + shape);

                // Для тензора (1, 150, 56, 1) используем особую логику
                // Номера каналов в таком тензоре неясны, пробуем просто брать максимальные значения

                for (int y = 0; y < segmentationMaskTexture.height; y++)
                {
                    for (int x = 0; x < segmentationMaskTexture.width; x++)
                    {
                        int pixelIndex = y * segmentationMaskTexture.width + x;

                        // Вычисляем соответствующие координаты в тензоре
                        int tensorY = Mathf.Min((int)(y * scaleY), height - 1);
                        int tensorX = Mathf.Min((int)(x * scaleX), width - 1);

                        float probability = 0;

                        // Для упрощения обработки тензора с формой (1, 150, 56, 1)
                        // Предполагаем, что это набор классов или вероятностей для пикселей
                        // Берем значение для координаты [0, tensorY, tensorX, 0]

                        try
                        {
                            // Получаем вероятность для данного пикселя
                            if (shape.rank == 4 && shape[3] == 1)
                            {
                                // (1, height, width, 1) - используем только высоту и ширину
                                probability = tensor[0, tensorY, tensorX, 0];
                            }
                            else if (shape.rank == 4 && shape[1] == 1)
                            {
                                // (1, 1, height, width) - формат NCHW с одним каналом
                                probability = tensor[0, 0, tensorY, tensorX];
                            }
                            else if (shape.rank == 3)
                            {
                                // (1, height, width) - только 3-мерный тензор
                                probability = tensor[0, tensorY, tensorX];
                            }
                            else
                            {
                                // Форма (1, 150, 56, 1) - особая логика
                                // 150 может быть количеством классов, пробуем найти максимальное значение

                                if (shape[1] == 150 && shape[2] == 56 && shape[3] == 1)
                                {
                                    // Ищем максимальное значение по всем классам
                                    float maxProb = 0;
                                    int foundClass = -1;

                                    // Специально ищем класс стены (wallClassIndex, обычно 1 для ADE20K)
                                    try
                                    {
                                        // Масштабируем координаты в пространство 56x1
                                        int scaledX = Mathf.Min((int)(tensorX * (56f / segmentationMaskTexture.width)), 55);

                                        // Проверяем непосредственно класс стены
                                        float wallValue = tensor[0, wallClassIndex, scaledX, 0];

                                        // Если значение класса стены достаточно высокое, используем его
                                        if (wallValue >= wallThreshold)
                                        {
                                            probability = wallValue;
                                        }
                                        // Иначе пробуем найти другой класс с максимальной вероятностью
                                        else
                                        {
                                            for (int c = 0; c < shape[1]; c++)
                                            {
                                                float value = tensor[0, c, scaledX, 0];
                                                if (value > maxProb)
                                                {
                                                    maxProb = value;
                                                    foundClass = c;
                                                }
                                            }

                                            // Используем найденный максимальный класс, если он выше порога
                                            if (maxProb >= wallThreshold)
                                            {
                                                probability = maxProb;
                                                Debug.Log($"Найден доминирующий класс {foundClass} с вероятностью {maxProb:F2}");
                                            }
                                        }
                                    }
                                    catch (Exception e)
                                    {
                                        Debug.LogError($"Ошибка при обработке тензора (1,150,56,1): {e.Message}");
                                        probability = 0.5f;
                                    }
                                }
                                else if (shape[1] == 150 && shape[2] == 1 && shape[3] == 56)
                                {
                                    // Здесь может быть другая форма с теми же значениями
                                    int scaledX = Mathf.Min((int)(tensorX * (56f / segmentationMaskTexture.width)), 55);
                                    float maxProb = 0;
                                    for (int c = 0; c < shape[1]; c++)
                                    {
                                        float value = tensor[0, c, 0, scaledX];
                                        if (value > maxProb)
                                        {
                                            maxProb = value;
                                        }
                                    }
                                    probability = maxProb;
                                }
                                else
                                {
                                    probability = 0.5f; // Значение по умолчанию, если форма не распознана
                                }
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogError($"Ошибка при получении значения тензора для координат [0,{tensorY},{tensorX},0]: {e.Message}");
                            probability = 0.5f;
                        }

                        // Применяем порог
                        byte maskValue = (probability >= wallThreshold) ? (byte)255 : (byte)0;

                        // Записываем значение в пиксель
                        pixels[y * segmentationMaskTexture.width + x] = new Color32(maskValue, maskValue, maskValue, 255);
                    }
                }
            }
            else
            {
                // Обычная обработка многоканального тензора
                for (int y = 0; y < segmentationMaskTexture.height; y++)
                {
                    for (int x = 0; x < segmentationMaskTexture.width; x++)
                    {
                        int pixelIndex = y * segmentationMaskTexture.width + x;

                        // Вычисляем соответствующие координаты в тензоре
                        int tensorY = Mathf.Min((int)(y * scaleY), height - 1);
                        int tensorX = Mathf.Min((int)(x * scaleX), width - 1);

                        float probability = 0;

                        try
                        {
                            // Прямой доступ к значению вероятности с обработкой NCHW/NHWC
                            if (isNCHW)
                            {
                                probability = tensor[0, wallClassIndex, tensorY, tensorX]; // NCHW
                            }
                            else
                            {
                                probability = tensor[0, tensorY, tensorX, wallClassIndex]; // NHWC
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogError($"Ошибка при получении значения тензора: {e.Message}");
                            probability = 0.5f;
                        }

                        // Применяем порог
                        byte maskValue = (probability >= wallThreshold) ? (byte)255 : (byte)0;

                        // Записываем значение в пиксель
                        pixels[pixelIndex] = new Color32(maskValue, maskValue, maskValue, 255);
                    }
                }
            }

            // Создаем временную Texture2D
            Texture2D tempMaskTexture = new Texture2D(segmentationMaskTexture.width, segmentationMaskTexture.height, TextureFormat.RGBA32, false);
            tempMaskTexture.SetPixels32(pixels);
            tempMaskTexture.Apply();

            // Копируем результат в RenderTexture
            RenderTexture previousRT = RenderTexture.active;
            RenderTexture.active = segmentationMaskTexture;
            GL.Clear(true, true, Color.clear); // Очищаем перед копированием
            Graphics.Blit(tempMaskTexture, segmentationMaskTexture);
            RenderTexture.active = previousRT;

            // Уничтожаем временную текстуру
            Destroy(tempMaskTexture);

            Debug.Log("[ProcessTensorManual] Обработка тензора завершена, маска обновлена.");
        }
        catch (Exception e)
        {
            Debug.LogError($"[ProcessTensorManual] Общая ошибка при обработке тензора: {e.Message}\nStackTrace: {e.StackTrace}");
            throw; // Пробрасываем исключение для обработки родительским методом
        }
    }

    /// <summary>
    /// Сохраняет текущую маску сегментации для отладки
    /// </summary>
    private void SaveDebugMask()
    {
        if (segmentationMaskTexture == null)
        {
            Debug.LogWarning("[SaveDebugMask] segmentationMaskTexture is null, cannot save.");
            return;
        }

        try
        {
            // Создаем временную Texture2D для копирования данных из RenderTexture
            Texture2D tempTex = new Texture2D(segmentationMaskTexture.width, segmentationMaskTexture.height, TextureFormat.RGBA32, false);
            RenderTexture previousRT = RenderTexture.active;
            RenderTexture.active = segmentationMaskTexture;
            tempTex.ReadPixels(new Rect(0, 0, segmentationMaskTexture.width, segmentationMaskTexture.height), 0, 0);
            tempTex.Apply();
            RenderTexture.active = previousRT;

            // Кодируем в PNG
            byte[] bytes = tempTex.EncodeToPNG();
            Destroy(tempTex); // Уничтожаем временную текстуру

            // Создаем папку, если её нет
            string fullPathDir = Path.Combine(Application.persistentDataPath, debugSavePath);
            if (!Directory.Exists(fullPathDir))
            {
                Directory.CreateDirectory(fullPathDir);
            }

            // Генерируем имя файла с временной меткой
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmssfff");
            string fileName = $"DebugMask_{timestamp}.png";
            string fullFilePath = Path.Combine(fullPathDir, fileName);

            // Сохраняем файл
            File.WriteAllBytes(fullFilePath, bytes);
            Debug.Log($"[SaveDebugMask] Отладочная маска сохранена в: {fullFilePath}");
        }
        catch (Exception e)
        {
            Debug.LogError($"[SaveDebugMask] Ошибка при сохранении отладочной маски: {e.Message}");
        }
    }

    /// <summary>
    /// Безопасно освобождает ресурсы движка через рефлексию
    /// </summary>
    private void DisposeEngine()
    {
        // Освобождаем ресурсы движка (Worker)
        if (engine != null)
        {
            engine.Dispose();
            engine = null;
        }
        if (worker != null) // Также освобождаем worker, если он используется отдельно
        {
            worker.Dispose();
            worker = null;
        }
    }

    private void OnDestroy()
    {
        Debug.Log("[OnDestroy] Cleaning up resources...");

        // Освобождаем ресурсы
        DisposeEngine();
        engine = null;
        runtimeModel = null;

        // Освобождаем текстуры
        if (cameraTexture != null)
        {
            Destroy(cameraTexture);
            cameraTexture = null;
        }

        // Освобождаем RenderTexture
        if (segmentationMaskTexture != null)
        {
            segmentationMaskTexture.Release();
            Destroy(segmentationMaskTexture);
            segmentationMaskTexture = null;
        }

        // Вызываем сборщик мусора для освобождения неуправляемых ресурсов
        System.GC.Collect();
    }

    /// <summary>
    /// Публичный метод для назначения модели из внешнего кода
    /// </summary>
    public void SetModel(UnityEngine.Object newModel)
    {
        if (newModel != modelAsset)
        {
            modelAsset = newModel;

            // Если мы уже проинициализированы, перезагружаем модель
            if (isModelInitialized)
            {
                DisposeEngine();
                engine = null;
                runtimeModel = null;
                isModelInitialized = false;

                // Запускаем инициализацию с новой моделью
                InitializeSegmentation();
            }
        }
    }

    // Метод для создания тестовой маски сегментации (для отладки)
    public void CreateSimulatedSegmentationMask()
    {
        Debug.Log("Создание симуляции маски сегментации для тестирования");

        // Создаем или переиспользуем текстуру
        if (segmentationMaskTexture == null)
        {
            segmentationMaskTexture = new RenderTexture(224, 224, 0, RenderTextureFormat.RFloat);
            segmentationMaskTexture.enableRandomWrite = true;
            segmentationMaskTexture.Create();
            Debug.Log($"Создана новая текстура для симуляции {segmentationMaskTexture.width}x{segmentationMaskTexture.height}");
        }

        // Создаем временную текстуру для рисования
        Texture2D tempTexture = new Texture2D(segmentationMaskTexture.width, segmentationMaskTexture.height,
            TextureFormat.RGBA32, false);

        // Получаем ориентацию камеры, если возможно
        Quaternion cameraRotation = Quaternion.identity;
        Vector3 cameraPosition = Vector3.zero;

        if (arCameraManager != null && arCameraManager.transform != null)
        {
            cameraRotation = arCameraManager.transform.rotation;
            cameraPosition = arCameraManager.transform.position;
        }
        else if (xrOrigin != null && xrOrigin.Camera != null)
        {
            cameraRotation = xrOrigin.Camera.transform.rotation;
            cameraPosition = xrOrigin.Camera.transform.position;
        }

        // Определяем где "стены" на основе позиции и направления камеры
        Vector3 forward = cameraRotation * Vector3.forward;
        Vector3 right = cameraRotation * Vector3.right;
        Vector3 up = cameraRotation * Vector3.up;

        // Задаем параметры "стен" в виртуальном пространстве
        // float wallDistance = 3.0f; // 3 метра от камеры - unused
        // float wallWidth = 5.0f;    // 5 метров ширина - unused
        // float wallHeight = 3.0f;   // 3 метра высота - unused

        // Создаем простой узор для имитации стен
        Color32[] pixels = new Color32[tempTexture.width * tempTexture.height];

        // Заполняем всё нулями (нет стен)
        for (int i = 0; i < pixels.Length; i++)
        {
            pixels[i] = new Color32(0, 0, 0, 0);
        }

        // Определяем размер и положение "стены" в зависимости от положения камеры
        int wallStartX = tempTexture.width / 8;
        int wallEndX = tempTexture.width - wallStartX;
        int wallStartY = tempTexture.height / 6;
        int wallEndY = tempTexture.height - wallStartY;

        // Наклоняем стену в зависимости от поворота камеры
        float xTilt = cameraRotation.eulerAngles.x / 90.0f - 0.5f;  // -0.5 до 0.5
        float yTilt = cameraRotation.eulerAngles.y / 180.0f - 0.5f; // -0.5 до 0.5

        for (int y = 0; y < tempTexture.height; y++)
        {
            for (int x = 0; x < tempTexture.width; x++)
            {
                // Нормализованные координаты от -1 до 1
                float normalizedX = (x / (float)tempTexture.width) * 2 - 1;
                float normalizedY = (y / (float)tempTexture.height) * 2 - 1;

                // Добавляем наклон в зависимости от поворота камеры
                normalizedX += yTilt;
                normalizedY += xTilt;

                // Проверяем, находится ли пиксель в области "стены"
                bool isWall = false;

                // Центральная стена
                if (x >= wallStartX && x <= wallEndX && y >= wallStartY && y <= wallEndY)
                {
                    // Добавляем шум для реалистичности
                    float noise = Mathf.PerlinNoise(x * 0.1f, y * 0.1f) * 0.2f;

                    // Градиент яркости от центра к краям
                    float centerDistX = Mathf.Abs(normalizedX);
                    float centerDistY = Mathf.Abs(normalizedY);
                    float centerDist = Mathf.Sqrt(centerDistX * centerDistX + centerDistY * centerDistY);

                    // Чем ближе к центру, тем ярче
                    float intensity = Mathf.Clamp01(1.0f - centerDist + noise);

                    // Пороговое значение для определения стены
                    isWall = intensity > wallThreshold;

                    if (isWall)
                    {
                        // Используем индекс класса стены и яркость для обозначения уверенности
                        byte intensity8bit = (byte)(intensity * 255);
                        pixels[y * tempTexture.width + x] = new Color32(intensity8bit, intensity8bit, intensity8bit, 255);
                    }
                }

                // Добавляем немного шума для углов
                if (!isWall && Mathf.PerlinNoise(x * 0.2f, y * 0.2f) > 0.93f)
                {
                    byte intensity = (byte)(Mathf.PerlinNoise(x * 0.3f, y * 0.3f) * 120);
                    pixels[y * tempTexture.width + x] = new Color32(intensity, intensity, intensity, 255);
                }
            }
        }

        // Применяем пиксели к текстуре
        tempTexture.SetPixels32(pixels);
        tempTexture.Apply();

        // Копируем содержимое в RenderTexture
        RenderTexture prevActive = RenderTexture.active;
        RenderTexture.active = segmentationMaskTexture;
        Graphics.Blit(tempTexture, segmentationMaskTexture);
        RenderTexture.active = prevActive;

        // Удаляем временную текстуру
        Destroy(tempTexture);

        Debug.Log("Симуляция маски сегментации создана успешно");
    }

    /// <summary>
    /// Пытается получить текстуру камеры напрямую из компонентов XR Simulation
    /// </summary>
    private Texture2D TryGetSimulationCameraTexture()
    {
        try
        {
            // Ищем компоненты симуляции, которые могут содержать текстуру камеры
            MonoBehaviour[] simulationComponents = FindObjectsOfType<MonoBehaviour>();

            foreach (MonoBehaviour component in simulationComponents)
            {
                Type type = component.GetType();

                // Ищем компоненты, связанные с симуляцией
                if (type.Name.Contains("Simulation") && type.Name.Contains("Provider"))
                {
                    // Пытаемся получить поле с текстурой через рефлексию
                    FieldInfo[] fields = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                    foreach (FieldInfo field in fields)
                    {
                        if (field.FieldType == typeof(Texture2D) || field.FieldType.IsSubclassOf(typeof(Texture)))
                        {
                            Texture texture = field.GetValue(component) as Texture;
                            if (texture != null)
                            {
                                Debug.Log($"[TryGetSimulationCameraTexture] Найдена текстура в {type.Name}.{field.Name}");

                                // Конвертируем в Texture2D, если это другой тип текстуры
                                if (texture is Texture2D)
                                {
                                    return texture as Texture2D;
                                }
                                else
                                {
                                    // Копируем содержимое в новую Texture2D
                                    RenderTexture tempRT = RenderTexture.GetTemporary(
                                        texture.width, texture.height, 0, RenderTextureFormat.ARGB32);

                                    Graphics.Blit(texture, tempRT);
                                    RenderTexture.active = tempRT;

                                    Texture2D result = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
                                    result.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                                    result.Apply();

                                    RenderTexture.active = null;
                                    RenderTexture.ReleaseTemporary(tempRT);

                                    return result;
                                }
                            }
                        }
                    }

                    // Ищем свойства с текстурой
                    PropertyInfo[] properties = type.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                    foreach (PropertyInfo property in properties)
                    {
                        if (property.PropertyType == typeof(Texture2D) ||
                            (property.PropertyType.IsGenericType && property.PropertyType.GetGenericTypeDefinition() == typeof(Texture)))
                        {
                            try
                            {
                                Texture texture = property.GetValue(component) as Texture;
                                if (texture != null)
                                {
                                    Debug.Log($"[TryGetSimulationCameraTexture] Найдена текстура в свойстве {type.Name}.{property.Name}");

                                    // Конвертируем в Texture2D если нужно
                                    if (texture is Texture2D)
                                    {
                                        return texture as Texture2D;
                                    }
                                    else
                                    {
                                        RenderTexture tempRT = RenderTexture.GetTemporary(
                                            texture.width, texture.height, 0, RenderTextureFormat.ARGB32);

                                        Graphics.Blit(texture, tempRT);
                                        RenderTexture.active = tempRT;

                                        Texture2D result = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
                                        result.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
                                        result.Apply();

                                        RenderTexture.active = null;
                                        RenderTexture.ReleaseTemporary(tempRT);

                                        return result;
                                    }
                                }
                            }
                            catch (Exception) { /* Игнорируем исключения при попытке чтения свойств */ }
                        }
                    }
                }
            }

            // Ищем текстуру у ARCameraBackground
            if (arCameraManager != null)
            {
                var cameraBackground = arCameraManager.GetComponent<ARCameraBackground>();
                if (cameraBackground != null && cameraBackground.material != null)
                {
                    // Получаем текстуру из материала ARCameraBackground
                    Texture backgroundTexture = cameraBackground.material.mainTexture;
                    if (backgroundTexture != null)
                    {
                        Debug.Log("[TryGetSimulationCameraTexture] Найдена текстура в ARCameraBackground");

                        // Конвертируем в Texture2D если не является ею
                        if (backgroundTexture is Texture2D)
                        {
                            return backgroundTexture as Texture2D;
                        }
                        else
                        {
                            RenderTexture tempRT = RenderTexture.GetTemporary(
                                backgroundTexture.width, backgroundTexture.height, 0, RenderTextureFormat.ARGB32);

                            Graphics.Blit(backgroundTexture, tempRT);
                            RenderTexture.active = tempRT;

                            Texture2D result = new Texture2D(backgroundTexture.width, backgroundTexture.height, TextureFormat.RGBA32, false);
                            result.ReadPixels(new Rect(0, 0, backgroundTexture.width, backgroundTexture.height), 0, 0);
                            result.Apply();

                            RenderTexture.active = null;
                            RenderTexture.ReleaseTemporary(tempRT);

                            return result;
                        }
                    }
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogWarning($"[TryGetSimulationCameraTexture] Ошибка при попытке получить текстуру напрямую: {e.Message}");
        }

        return null;
    }

    /// <summary>
    /// Создает тензор напрямую из пиксельных данных текстуры
    /// </summary>
    private Tensor<float> CreateTensorFromPixels(Texture2D texture)
    {
        Tensor<float> tensor = null;
        try
        {
            Debug.Log($"Создание тензора напрямую из пикселей текстуры {texture.width}x{texture.height}");

            // Получаем пиксели из текстуры
            Color32[] pixels = texture.GetPixels32();

            // Создаем тензор в формате [1, height, width, 3] (NHWC)
            TensorShape shape = new TensorShape(1, texture.height, texture.width, 3);

            // Создаем массив данных для тензора
            float[] tensorData = new float[shape.length];

            // Заполняем массив данными из пикселей
            for (int y = 0; y < texture.height; y++)
            {
                for (int x = 0; x < texture.width; x++)
                {
                    int pixelIndex = y * texture.width + x;
                    Color32 pixel = pixels[pixelIndex];

                    // Нормализуем значения до диапазона [0, 1]
                    float r = pixel.r / 255.0f;
                    float g = pixel.g / 255.0f;
                    float b = pixel.b / 255.0f;

                    // Для NHWC формата:
                    int tensorIndex = 0 * (texture.height * texture.width * 3) + // batch index = 0
                                  y * (texture.width * 3) +                 // height position
                                  x * 3;                                    // width position * channels

                    // Записываем значения RGB в массив данных
                    tensorData[tensorIndex + 0] = r; // R
                    tensorData[tensorIndex + 1] = g; // G
                    tensorData[tensorIndex + 2] = b; // B
                }
            }

            // Создаем тензор из массива данных
            tensor = new Tensor<float>(shape, tensorData);

            Debug.Log("Тензор успешно создан из пиксельных данных");
            return tensor;
        }
        catch (Exception e)
        {
            Debug.LogError($"Ошибка при создании тензора из пикселей: {e.Message}");

            // Освобождаем ресурсы в случае ошибки
            if (tensor != null)
            {
                try
                {
                    tensor.Dispose();
                }
                catch (Exception disposeEx)
                {
                    Debug.LogWarning($"Ошибка при освобождении тензора: {disposeEx.Message}");
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Создает тензор в формате NCHW (подходит для некоторых моделей)
    /// </summary>
    private Tensor<float> CreateNCHWTensorFromPixels(Texture2D texture)
    {
        Tensor<float> tensor = null;
        try
        {
            Debug.Log($"Создание NCHW тензора из пикселей текстуры {texture.width}x{texture.height}");

            // Получаем пиксели из текстуры
            Color32[] pixels = texture.GetPixels32();

            // Создаем тензор в формате [1, 3, height, width] (NCHW)
            TensorShape shape = new TensorShape(1, 3, texture.height, texture.width);

            // Создаем массив данных для тензора
            float[] tensorData = new float[shape.length];

            // Заполняем массив данными из пикселей в формате NCHW
            for (int y = 0; y < texture.height; y++)
            {
                for (int x = 0; x < texture.width; x++)
                {
                    int pixelIndex = y * texture.width + x;
                    Color32 pixel = pixels[pixelIndex];

                    // Нормализуем значения до диапазона [0, 1]
                    float r = pixel.r / 255.0f;
                    float g = pixel.g / 255.0f;
                    float b = pixel.b / 255.0f;

                    // Для NCHW формата:
                    // Красный канал (R)
                    tensorData[0 * (3 * texture.height * texture.width) +
                              0 * (texture.height * texture.width) +
                              y * texture.width + x] = r;

                    // Зеленый канал (G)
                    tensorData[0 * (3 * texture.height * texture.width) +
                              1 * (texture.height * texture.width) +
                              y * texture.width + x] = g;

                    // Синий канал (B)
                    tensorData[0 * (3 * texture.height * texture.width) +
                              2 * (texture.height * texture.width) +
                              y * texture.width + x] = b;
                }
            }

            // Создаем тензор из массива данных
            tensor = new Tensor<float>(shape, tensorData);

            Debug.Log("NCHW Тензор успешно создан из пиксельных данных");
            return tensor;
        }
        catch (Exception e)
        {
            Debug.LogError($"Ошибка при создании NCHW тензора из пикселей: {e.Message}");

            // Освобождаем ресурсы в случае ошибки
            if (tensor != null)
            {
                try
                {
                    tensor.Dispose();
                }
                catch (Exception disposeEx)
                {
                    Debug.LogWarning($"Ошибка при освобождении тензора: {disposeEx.Message}");
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Создает простой тензор с одним каналом (для простых моделей сегментации)
    /// </summary>
    private Tensor<float> CreateSingleChannelTensor(Texture2D texture, bool normalize = true)
    {
        Tensor<float> tensor = null;
        try
        {
            Debug.Log($"Создание одноканального тензора из текстуры {texture.width}x{texture.height}");

            // Получаем пиксели из текстуры
            Color32[] pixels = texture.GetPixels32();

            // Создаем тензор с одним каналом [1, height, width, 1]
            TensorShape shape = new TensorShape(1, texture.height, texture.width, 1);
            float[] tensorData = new float[shape.length];

            // Заполняем массив данными из пикселей, используя только яркость (grayscale)
            for (int y = 0; y < texture.height; y++)
            {
                for (int x = 0; x < texture.width; x++)
                {
                    int pixelIndex = y * texture.width + x;
                    Color32 pixel = pixels[pixelIndex];

                    // Преобразуем RGB в яркость (grayscale)
                    float grayscale = (0.299f * pixel.r + 0.587f * pixel.g + 0.114f * pixel.b);

                    // Нормализуем, если требуется
                    if (normalize)
                    {
                        grayscale /= 255.0f;
                    }

                    // Индекс в массиве тензора
                    int tensorIndex = 0 * (texture.height * texture.width) + // batch
                                      y * texture.width +                   // height
                                      x;                                    // width

                    tensorData[tensorIndex] = grayscale;
                }
            }

            // Создаем тензор из массива данных
            tensor = new Tensor<float>(shape, tensorData);

            Debug.Log("Одноканальный тензор успешно создан");
            return tensor;
        }
        catch (Exception e)
        {
            Debug.LogError($"Ошибка при создании одноканального тензора: {e.Message}");

            // Освобождаем ресурсы в случае ошибки
            if (tensor != null)
            {
                try
                {
                    tensor.Dispose();
                }
                catch (Exception disposeEx)
                {
                    Debug.LogWarning($"Ошибка при освобождении тензора: {disposeEx.Message}");
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Пытается создать тензор специально для XR Simulation
    /// </summary>
    private Tensor<float> TryCreateXRSimulationTensor(Texture2D texture)
    {
        Tensor<float> tensor = null;
        try
        {
            Debug.Log("Создание специального тензора для XR Simulation...");

            // Проверяем, есть ли компонент XREnvironment на сцене
            var xrEnvironmentObject = GameObject.Find("XRSimulationEnvironment");
            if (xrEnvironmentObject != null)
            {
                Debug.Log("Найден объект XRSimulationEnvironment для XR Simulation");
            }

            // Ищем RenderTexture от XR Simulation напрямую
            Type simulationCameraType = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                foreach (var type in assembly.GetTypes())
                {
                    if (type.Name.Contains("SimulationCamera") ||
                        type.Name.Contains("CameraTextureProvider"))
                    {
                        simulationCameraType = type;
                        Debug.Log($"Найден тип XR Simulation: {type.FullName}");
                        break;
                    }
                }
                if (simulationCameraType != null) break;
            }

            // Создаем специальный тензор для XR Simulation на основе текущего изображения
            // Изображение из XR Simulation содержит розовые/магента стены

            // Создаем одноканальный тензор
            TensorShape simulationShape = new TensorShape(1, inputResolution.y, inputResolution.x, 1);
            float[] tensorData = new float[simulationShape.length];

            // Получаем пиксели из текстуры
            Color32[] pixels = texture.GetPixels32();

            // Усовершенствованная логика определения стен в XR Simulation
            for (int y = 0; y < texture.height; y++)
            {
                for (int x = 0; x < texture.width; x++)
                {
                    int pixelIndex = y * texture.width + x;
                    int tensorIndex = 0 * (texture.height * texture.width) + y * texture.width + x;

                    Color32 pixel = pixels[pixelIndex];

                    // Определение розового/магента цвета стен (более точная логика)
                    // В XR Simulation стены обычно имеют ярко-розовый/магента цвет
                    bool isPink = (pixel.r > 180 && pixel.g < 140 && pixel.b > 180) || // Магента
                                 (pixel.r > 200 && pixel.g < 80 && pixel.b > 200) ||   // Яркая магента
                                 (pixel.r > 200 && pixel.g < 100 && pixel.b > 150);    // Розовый

                    // Добавляем проверку на белый цвет (тоже может быть стенами)
                    bool isWhite = (pixel.r > 230 && pixel.g > 230 && pixel.b > 230);

                    // Рассчитываем розовость (magenta-ness)
                    float magentaStrength = 0f;
                    if (isPink)
                    {
                        // Оцениваем "розовость" пикселя от 0.8 до 1.0
                        float rStrength = pixel.r / 255f;
                        float gWeakness = 1.0f - (pixel.g / 255f);
                        float bStrength = pixel.b / 255f;

                        // Вес для розового: много красного, мало зеленого, много синего
                        magentaStrength = (rStrength * 0.4f + gWeakness * 0.4f + bStrength * 0.2f);
                        magentaStrength = Mathf.Clamp(magentaStrength * 1.25f, 0.8f, 1.0f); // Усиливаем значение
                    }
                    else if (isWhite)
                    {
                        // Белые стены с чуть менее высокой вероятностью
                        magentaStrength = 0.75f;
                    }

                    // Применяем размытие - учитываем соседние пиксели для сглаживания
                    if (magentaStrength > 0.1f && x > 1 && x < texture.width - 2 && y > 1 && y < texture.height - 2)
                    {
                        // Проверяем наличие розового в соседних пикселях для уменьшения шума
                        int pinkNeighbors = 0;

                        // Проверяем 8 соседних пикселей
                        for (int dy = -1; dy <= 1; dy++)
                        {
                            for (int dx = -1; dx <= 1; dx++)
                            {
                                if (dx == 0 && dy == 0) continue; // Пропускаем центральный

                                int nx = x + dx;
                                int ny = y + dy;
                                int nIndex = ny * texture.width + nx;

                                if (nIndex >= 0 && nIndex < pixels.Length)
                                {
                                    Color32 nPixel = pixels[nIndex];
                                    if ((nPixel.r > 180 && nPixel.g < 140 && nPixel.b > 180) ||
                                        (nPixel.r > 230 && nPixel.g > 230 && nPixel.b > 230))
                                    {
                                        pinkNeighbors++;
                                    }
                                }
                            }
                        }

                        // Если мало розовых соседей, это скорее всего шум - уменьшаем значение
                        if (pinkNeighbors < 3)
                        {
                            magentaStrength *= 0.5f;
                        }
                        // Если много розовых соседей, усиливаем значение для лучшей сегментации
                        else if (pinkNeighbors >= 5)
                        {
                            magentaStrength = Mathf.Min(magentaStrength * 1.2f, 1.0f);
                        }
                    }

                    tensorData[tensorIndex] = magentaStrength;
                }
            }

            // Применяем медианный фильтр для удаления шума
            float[] filtered = new float[tensorData.Length];
            Array.Copy(tensorData, filtered, tensorData.Length);

            // Простой медианный фильтр (3x3) для уменьшения шума
            for (int y = 1; y < texture.height - 1; y++)
            {
                for (int x = 1; x < texture.width - 1; x++)
                {
                    List<float> neighbors = new List<float>(9);

                    for (int dy = -1; dy <= 1; dy++)
                    {
                        for (int dx = -1; dx <= 1; dx++)
                        {
                            int nx = x + dx;
                            int ny = y + dy;
                            int index = 0 * (texture.height * texture.width) + ny * texture.width + nx;
                            neighbors.Add(tensorData[index]);
                        }
                    }

                    neighbors.Sort();
                    filtered[0 * (texture.height * texture.width) + y * texture.width + x] = neighbors[4]; // Медиана из 9 значений
                }
            }

            // Создаем тензор из отфильтрованных данных
            tensor = new Tensor<float>(simulationShape, filtered);

            Debug.Log("Специальный тензор для XR Simulation успешно создан");
            return tensor;
        }
        catch (Exception e)
        {
            Debug.LogWarning($"Ошибка при создании специального тензора для XR Simulation: {e.Message}");
            if (tensor != null)
            {
                tensor.Dispose();
            }
            return null;
        }
    }

    // This method might contain one of the errors about byte[] to IntPtr conversion
    private void ProcessCPUImage(XRCpuImage image)
    {
        if (image.valid)
        {
            var conversionParams = new XRCpuImage.ConversionParams
            {
                inputRect = new RectInt(0, 0, image.width, image.height),
                outputDimensions = new Vector2Int(inputResolution.x, inputResolution.y),
                outputFormat = TextureFormat.RGBA32,
                transformation = XRCpuImage.Transformation.MirrorY
            };

            // Using NativeArray instead of byte[] for proper conversion
            var rawTextureData = new NativeArray<byte>(inputResolution.x * inputResolution.y * 4, Allocator.Temp);

            try
            {
                // Use the standard Convert method with NativeArray
                image.Convert(conversionParams, rawTextureData);

                // Update the texture
                if (cameraTexture == null)
                {
                    cameraTexture = new Texture2D(inputResolution.x, inputResolution.y, TextureFormat.RGBA32, false);
                }

                cameraTexture.LoadRawTextureData(rawTextureData);
                cameraTexture.Apply();
            }
            finally
            {
                // Always dispose of the NativeArray
                if (rawTextureData.IsCreated)
                    rawTextureData.Dispose();
            }
        }
    }

    /// <summary>
    /// Возвращает текущую маску сегментации для внешнего использования
    /// </summary>
    public Texture GetSegmentationMaskTexture()
    {
        return segmentationMaskTexture;
    }

    /// <summary>
    /// Возвращает размер текущей маски сегментации
    /// </summary>
    public Vector2Int GetSegmentationMaskSize()
    {
        if (segmentationMaskTexture != null)
        {
            return new Vector2Int(segmentationMaskTexture.width, segmentationMaskTexture.height);
        }
        return Vector2Int.zero;
    }

    // Keep the stub for InitializeWebcam
    private void InitializeWebcam()
    {
        Debug.LogWarning("WebCam functionality has been removed.");
    }

    // Add stub methods for the removed webcam methods
    public void SetWebcamMode(bool enable, string deviceName = "")
    {
        Debug.LogWarning("WebCam functionality has been removed.");
    }

    public string[] GetAvailableWebcams()
    {
        Debug.LogWarning("WebCam functionality has been removed.");
        return new string[0];
    }
}