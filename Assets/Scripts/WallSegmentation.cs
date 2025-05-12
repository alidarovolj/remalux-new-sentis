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

    [Header("Настройки сегментации")]
    [Tooltip("Индекс класса 'стена' в выходе модели")]
    public int wallClassIndex = 1;

    [Tooltip("Порог вероятности для определения стены")]
    [Range(0.0f, 1.0f)]
    public float wallThreshold = 0.5f;

    [Tooltip("Разрешение входного изображения")]
    public Vector2Int inputResolution = new Vector2Int(224, 224);

    [Header("Компоненты")]
    [Tooltip("Ссылка на ARCameraManager")]
    public ARCameraManager arCameraManager;

    [Tooltip("Ссылка на ARSessionManager")]
    public ARSessionManager arSessionManager;

    [Tooltip("Текстура для вывода маски сегментации")]
    public RenderTexture segmentationMaskTexture;

    [Header("Отладка")]
    [Tooltip("Сохранять маску сегментации в отдельную текстуру")]
    public bool saveDebugMask = false;

    [Tooltip("Путь для сохранения отладочных изображений")]
    public string debugSavePath = "SegmentationDebug";

    // Приватные поля для работы с моделью через рефлексию
    private Worker engine;
    private Model runtimeModel;
    private Worker worker;
    private Texture2D cameraTexture;
    private bool isModelInitialized = false;
    private bool isInitializing = false;
    private string lastErrorMessage = null;
    private bool isInitializationFailed = false;

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

        // Создаем сегментационную маску, если она не назначена
        if (segmentationMaskTexture == null)
        {
            segmentationMaskTexture = new RenderTexture(inputResolution.x, inputResolution.y, 0, RenderTextureFormat.RFloat);
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
            return; // Выходим, если нет текстуры
        }
        // Добавлен лог: Изображение с камеры получено
        Debug.Log("[Update] Изображение с камеры получено, вызываем PerformSegmentation");

        // Выполняем сегментацию
        PerformSegmentation(cameraTex);
    }

    /// <summary>
    /// Получает текстуру с камеры и преобразует её в нужное разрешение
    /// </summary>
    private Texture2D GetCameraTexture()
    {
        if (arCameraManager == null) 
        {
             Debug.LogError("[GetCameraTexture] arCameraManager is null!");
             return null;
        }

        if (!arCameraManager.TryAcquireLatestCpuImage(out XRCpuImage image))
        {
            // Добавлен лог: Не удалось получить CPU изображение
             Debug.LogWarning("[GetCameraTexture] TryAcquireLatestCpuImage не удалось получить изображение.");
            return null;
        }
        // Добавлен лог: CPU изображение получено
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

    /// <summary>
    /// Выполняет сегментацию с использованием ML модели через рефлексию
    /// </summary>
    private void PerformSegmentation(Texture2D inputTexture)
    {
        try
        {
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

            // Вместо рефлексии здесь, будем использовать прямой вызов TextureConverter.ToTensor
            // если предыдущий TextureToTensor из SentisCompat не сработал

            Tensor<float> inputTensor = null; // Заменен TensorFloat на Tensor<float>
            try
            {
                // Прямой вызов API Sentis 2.x
                // Важно: inputResolution должно соответствовать ожиданиям модели (например, 512x512)
                // TextureTransform используется для указания размеров и других параметров преобразования.
                TextureTransform transform = new TextureTransform().SetDimensions(inputResolution.x, inputResolution.y);

                // Создаем или переиспользуем тензор
                // Вместо `tensor = TextureConverter.ToTensor(inputTexture, transform);`
                // используем новый API:
                if (inputTensor == null) // Создаем, если еще не создан
                {
                    // Определяем форму тензора: [batch, height, width, channels]
                    // Для TextureConverter обычно ожидается [1, height, width, 3] или [1, height, width, 4]
                    // Каналы (3 для RGB, 4 для RGBA) зависят от того, как TextureConverter обрабатывает текстуру.
                    // Обычно это 3, но лучше проверить документацию или экспериментировать.
                    // Пока что предположим 3 канала, т.к. модель сегментации часто работает с RGB.
                    TensorShape inputShape = new TensorShape(1, inputResolution.y, inputResolution.x, 3);
                    inputTensor = new Tensor<float>(inputShape);
                }

                TextureConverter.ToTensor(inputTexture, inputTensor, transform); // Новый вызов API

                // ExecuteModelAndProcessResult ожидает object, поэтому передаем тензор как object
                // В идеале, ExecuteModelAndProcessResult тоже должен быть типизирован.
                ExecuteModelAndProcessResult(inputTensor);
                return;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Не удалось создать тензор через TextureConverter.ToTensor (Sentis 2.x API): {e.Message}. Попытка fallback через рефлексию...");
                // Попытка рефлексии (менее предпочтительно)
                var toTensorMethod = textureConverterType.GetMethod("ToTensor", new Type[] { typeof(Texture) });
                if (toTensorMethod != null)
                {
                    Debug.Log("Используем метод ToTensor с параметром Texture (через рефлексию)");
                    object inputTensorObj = toTensorMethod.Invoke(null, new object[] { inputTexture });
                    if (inputTensorObj != null)
                    {
                        ExecuteModelAndProcessResult(inputTensorObj);
                        return;
                    }
                }

                var textureToTensorNewMethod = textureConverterType.GetMethod("TextureToTensor", new Type[] { typeof(Texture) });
                if (textureToTensorNewMethod != null)
                {
                    Debug.Log("Используем метод TextureToTensor с параметром Texture (через рефлексию)");
                    object inputTensorObj = textureToTensorNewMethod.Invoke(null, new object[] { inputTexture });
                    if (inputTensorObj != null)
                    {
                        ExecuteModelAndProcessResult(inputTensorObj);
                        return;
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
        // Если есть маска сегментации, заполняем её простыми данными
        if (segmentationMaskTexture != null)
        {
            Debug.Log("Создаем заглушку для маски сегментации");

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
        else
        {
            Debug.LogError("Маска сегментации не определена (segmentationMaskTexture == null)");
        }
    }

    /// <summary>
    /// Выполняет модель и обрабатывает результат
    /// </summary>
    private void ExecuteModelAndProcessResult(object inputTensor)
    {
        if (inputTensor == null) return;

        try
        {
            // Находим метод Execute в IWorker
            var executeMethod = engine.GetType().GetMethod("Execute");
            if (executeMethod == null)
            {
                Debug.LogError("Метод Execute не найден в IWorker");
                return;
            }

            // Выполняем модель
            executeMethod.Invoke(engine, new object[] { inputTensor });

            // Получаем результат
            var peekOutputMethod = engine.GetType().GetMethod("PeekOutput");
            if (peekOutputMethod == null)
            {
                Debug.LogError("Метод PeekOutput не найден в IWorker");
                return;
            }

            object outputTensor = peekOutputMethod.Invoke(engine, null);
            if (outputTensor == null)
            {
                Debug.LogError("Выходной тензор не был получен");
                return;
            }

            // Обрабатываем результат
            ProcessSegmentationResult(outputTensor);

            // Освобождаем входной тензор, если он реализует IDisposable
            if (inputTensor is IDisposable disposable)
            {
                disposable.Dispose();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Ошибка при обработке модели: {e.Message}\nStackTrace: {e.StackTrace}");
        }
    }

    /// <summary>
    /// Обрабатывает результат сегментации и обновляет маску
    /// </summary>
    private void ProcessSegmentationResult(object outputTensorObj)
    {
        Debug.Log("[ProcessSegmentationResult] Вызван.");

        // Проверяем, что segmentationMaskTexture существует и готова
        if (segmentationMaskTexture == null || !segmentationMaskTexture.IsCreated())
        {
            Debug.LogError("SegmentationMaskTexture не готова для записи.");
            return;
        }

        // Пытаемся привести к типу TensorFloat (или Tensor<float>)
        Tensor<float> outputTensor = outputTensorObj as Tensor<float>;
        if (outputTensor == null)
        {
            Debug.LogError($"[ProcessSegmentationResult] Не удалось привести выходной тензор к типу Tensor<float>. Фактический тип: {outputTensorObj?.GetType().FullName ?? "null"}");
            // Опционально: можно отрисовать пустую маску или использовать RenderSimpleMask
            RenderSimpleMask(); 
            return;
        }
        Debug.Log("[ProcessSegmentationResult] Успешно приведено к Tensor<float>.");

        try
        {
            // Вызываем новый метод для обработки
            ProcessTensorManual(outputTensor);

             // Отладочное сохранение маски, если включено
            if (saveDebugMask)
            {
                SaveDebugMask(); // Нужно убедиться, что эта функция реализована
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Ошибка при ручной обработке результата сегментации: {e.Message}\nStackTrace: {e.StackTrace}");
            RenderSimpleMask(); // Используем заглушку в случае ошибки
        }
        finally
        {
             // Освобождаем тензор СРАЗУ после использования
             // Важно: PeekOutput не передает владение, Dispose здесь не нужен,
             // тензор будет управляться Worker-ом. Если бы мы использовали Execute и забирали тензор,
             // то Dispose был бы нужен. Оставим это пока так, но надо помнить.
             // outputTensor?.Dispose(); // Пока не вызываем Dispose для PeekOutput
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
        int channels = shape[1]; // Количество классов (или 3 для NHWC)
        int height = shape[2]; // (или 1 для NHWC)
        int width = shape[3]; // (или 2 для NHWC)

        // Добавлен детальный лог формы
        Debug.Log($"[ProcessTensorManual] Обработка тензора с формой: {shape}. Batch={batch}, Channels={channels}, Height={height}, Width={width}. Target Class={wallClassIndex}, Threshold={wallThreshold}");

        // Определяем формат (простая эвристика по соотношению сторон, может быть неточной)
        bool isNCHW = channels < height && channels < width; // Предполагаем NCHW если каналов меньше чем H/W
        if (!isNCHW) { 
             // Если похоже на NHWC, переназначаем переменные (требует проверки!)
             // height = shape[1]; 
             // width = shape[2];
             // channels = shape[3];
             Debug.LogWarning("[ProcessTensorManual] Форма тензора похожа на NHWC. Текущая логика предполагает NCHW. Результат может быть неверным.");
             // ВАЖНО: Если формат NHWC, логика доступа к данным ниже должна быть изменена!
        }

        // Проверяем валидность wallClassIndex
        if (wallClassIndex < 0 || wallClassIndex >= channels)
        {
            Debug.LogError($"Неверный WallClassIndex ({wallClassIndex}). Должен быть между 0 и {channels - 1}. Используем заглушку.");
            RenderSimpleMask();
            return;
        }
        
        // Проверяем соответствие размеров текстуры и тензора
        if (segmentationMaskTexture.width != width || segmentationMaskTexture.height != height)
        {
             Debug.LogWarning($"Размер RenderTexture ({segmentationMaskTexture.width}x{segmentationMaskTexture.height}) не совпадает с размером выхода модели ({width}x{height}). Маска может быть искажена. Проверьте настройки inputResolution и модель.");
             // Можно либо пересоздать RenderTexture, либо попробовать масштабировать (сложнее). 
             // Пока продолжим, но результат может быть неверным.
        }

        // <-- Добавлен лог перед доступом к данным
        Debug.Log("[ProcessTensorManual] Попытка получить данные тензора...");

        // Создаем массив пикселей для временной текстуры
        // Texture2D работает с Color, но Color32 может быть быстрее
        Color32[] pixels = new Color32[width * height];

        // Заполняем массив пикселей на основе выбранного класса и порога
        // Доступ к данным напрямую через индексатор [batch, channel, y, x]
        // int baseIndexOffset = wallClassIndex * height * width; // Больше не нужен для прямого доступа

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {                
                int pixelIndex = y * width + x; // Индекс в массиве pixels (и для координат x, y)
                // int dataIndex = baseIndexOffset + pixelIndex; // Больше не нужен

                // if (dataIndex < 0 || dataIndex >= tensorData.Length) { // Проверка больше не нужна, т.к. нет tensorData
                //     Debug.LogError($"Индекс данных ({dataIndex}) вне диапазона [0..{tensorData.Length - 1}] для x={x}, y={y}");
                //     continue; // Пропустить этот пиксель
                // }

                // Прямой доступ к значению вероятности
                float probability = tensor[0, wallClassIndex, y, x]; // Используем индексатор
                
                // Применяем порог
                byte maskValue = (probability >= wallThreshold) ? (byte)255 : (byte)0;

                // Записываем значение в пиксель (R=G=B=maskValue, A=255)
                // Шейдер читает только R канал, но запишем во все для наглядности
                pixels[pixelIndex] = new Color32(maskValue, maskValue, maskValue, 255);
            }
        }

        // Создаем временную Texture2D
        // Используем формат RFloat или R8 для одноканальной маски, если шейдер сможет это прочитать.
        // RGBA32 более совместимый формат.
        Texture2D tempMaskTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);
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

        // Debug.Log("Ручная обработка тензора завершена, маска обновлена.");
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
        // Освобождаем ресурсы
        DisposeEngine();
        engine = null;
        runtimeModel = null;
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

        // Создаем простой узор для имитации стен
        Color32[] pixels = new Color32[tempTexture.width * tempTexture.height];
        for (int y = 0; y < tempTexture.height; y++)
        {
            for (int x = 0; x < tempTexture.width; x++)
            {
                int index = y * tempTexture.width + x;

                // Создаем маску с рамкой по краям (имитация стен)
                bool isWall = false;

                // Левая и правая стены
                if (x < tempTexture.width * 0.2f || x > tempTexture.width * 0.8f)
                    isWall = true;

                // Верхняя и нижняя стены  
                if (y < tempTexture.height * 0.2f || y > tempTexture.height * 0.8f)
                    isWall = true;

                // Добавляем немного случайности для теста
                if (UnityEngine.Random.value < 0.05f)
                    isWall = !isWall;

                // Белый для стен, прозрачный для остального
                pixels[index] = isWall ? new Color32(255, 255, 255, 255) : new Color32(0, 0, 0, 0);
            }
        }

        tempTexture.SetPixels32(pixels);
        tempTexture.Apply();

        // Копируем в RenderTexture
        RenderTexture previousRT = RenderTexture.active;
        RenderTexture.active = segmentationMaskTexture;
        Graphics.Blit(tempTexture, segmentationMaskTexture);
        RenderTexture.active = previousRT;

        // Помечаем модель как инициализированную
        isModelInitialized = true;

        // Уничтожаем временную текстуру
        Destroy(tempTexture);

        Debug.Log("Симуляция маски сегментации успешно создана и активирована");
    }
}