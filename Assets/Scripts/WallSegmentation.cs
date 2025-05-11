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
    public object model;

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
    public UnityEngine.Object arSessionManager;

    [Tooltip("Текстура для вывода маски сегментации")]
    public RenderTexture segmentationMaskTexture;

    [Header("Отладка")]
    [Tooltip("Сохранять маску сегментации в отдельную текстуру")]
    public bool saveDebugMask = false;

    [Tooltip("Путь для сохранения отладочных изображений")]
    public string debugSavePath = "SegmentationDebug";

    // Приватные поля для работы с моделью через рефлексию
    private object engine;
    private object runtimeModel;
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
            var sentisInitializer = FindObjectOfType<SentisInitializer>();
            if (sentisInitializer == null)
            {
                sentisInitializer = gameObject.AddComponent<SentisInitializer>();
            }
            var initAsync = sentisInitializer.InitializeAsync();
            yield return initAsync;
        }

        // Проверяем, есть ли уже загруженная модель в ModelLoader
        var modelLoader = FindObjectOfType<WallSegmentationModelLoader>();
        if (modelLoader != null && modelLoader.IsModelLoaded)
        {
            model = modelLoader.GetLoadedModel();
            if (model != null)
            {
                Debug.Log("Использую модель, уже загруженную в WallSegmentationModelLoader");
                isModelInitialized = true;
                if (OnModelInitialized != null) OnModelInitialized.Invoke();
                yield break;
            }
        }

        // Пробуем загрузить модель самостоятельно
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

        // Если модель все еще не загружена, пробуем создать ее
        if (!modelLoaded)
        {
            Debug.LogWarning("Не удалось загрузить модель ни в одном формате. Пробуем создать и сериализовать модель...");

            // Создаем экземпляр ModelSerializer для преобразования модели
            var serializer = gameObject.AddComponent<ModelSerializer>();
            if (serializer != null)
            {
                serializer.onnxModelPath = Path.Combine(Application.streamingAssetsPath, "model.onnx");
                serializer.outputPath = Path.Combine(Application.streamingAssetsPath, "model.sentis");

                // Запускаем сериализацию и ждем
                var serializeCoroutine = serializer.SerializeModelWithTimeout();
                yield return serializeCoroutine;

                yield return new WaitForSeconds(1);

                // Попробуем загрузить свежесозданную модель
                string sentisPath = Path.Combine(Application.streamingAssetsPath, "model.sentis");
                if (File.Exists(sentisPath))
                {
                    try
                    {
                        model = ModelLoader.Load(sentisPath);
                        if (model != null)
                        {
                            Debug.Log("Модель Sentis успешно создана и загружена!");
                            isModelInitialized = true;
                            modelLoaded = true;
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogError($"Не удалось загрузить свежесозданную Sentis модель: {e.Message}");
                    }
                }
            }
        }

        // Инициализируем worker если модель загружена
        if (isModelInitialized && model != null)
        {
            try
            {
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
                    if (OnModelInitialized != null) OnModelInitialized.Invoke();
                }
                else
                {
                    Debug.LogError("Не удалось создать worker для модели");
                    isModelInitialized = false;
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Ошибка при создании worker: {e.Message}");
                isModelInitialized = false;
            }
        }
        else
        {
            Debug.LogError("Не удалось инициализировать модель: Модель не загрузилась ни в одном формате");
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
                throw new Exception($"Метод Load не найден в ModelLoader для типа {modelAsset.GetType().Name}");
            }

            // Загружаем модель и сохраняем в обоих полях
            object loadedModel = loadMethod.Invoke(null, new object[] { modelAsset });
            if (loadedModel == null)
            {
                throw new Exception("Модель загружена, но результат null");
            }

            // Сохраняем ссылку на модель в обоих полях
            this.model = loadedModel;
            this.runtimeModel = loadedModel;

            // Создаем исполнителя с помощью рефлексии
            var workerFactoryType = Type.GetType("Unity.Sentis.WorkerFactory, Unity.Sentis");
            if (workerFactoryType == null)
            {
                throw new Exception("Тип Unity.Sentis.WorkerFactory не найден");
            }

            // Пробуем оба порядка параметров (для совместимости с разными версиями Sentis)
            MethodInfo createMethod = workerFactoryType.GetMethod("CreateWorker",
                new Type[] { runtimeModel.GetType(), typeof(int) });

            if (createMethod == null)
            {
                createMethod = workerFactoryType.GetMethod("CreateWorker",
                    new Type[] { typeof(int), runtimeModel.GetType() });

                if (createMethod == null)
                {
                    throw new Exception("Метод CreateWorker не найден в WorkerFactory");
                }

                // Вызываем с порядком (backend, model)
                engine = createMethod.Invoke(null, new object[] { preferredBackend, runtimeModel });
            }
            else
            {
                // Вызываем с порядком (model, backend)
                engine = createMethod.Invoke(null, new object[] { runtimeModel, preferredBackend });
            }

            isModelInitialized = true;
            Debug.Log("Модель успешно инициализирована");
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
        // Если модель не инициализирована, не выполняем сегментацию
        if (!isModelInitialized || engine == null) return;

        // Получаем изображение с камеры
        Texture2D cameraTex = GetCameraTexture();
        if (cameraTex == null) return;

        // Выполняем сегментацию
        PerformSegmentation(cameraTex);
    }

    /// <summary>
    /// Получает текстуру с камеры и преобразует её в нужное разрешение
    /// </summary>
    private Texture2D GetCameraTexture()
    {
        if (arCameraManager == null || !arCameraManager.TryAcquireLatestCpuImage(out XRCpuImage image))
        {
            return null;
        }

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
                    object inputTensor = textureToTensorMethod.Invoke(null, new object[] { inputTexture });
                    if (inputTensor != null)
                    {
                        ExecuteModelAndProcessResult(inputTensor);
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

            // Пробуем метод ToTensor (существующий в различных версиях Sentis)
            var toTensorMethod = textureConverterType.GetMethod("ToTensor", new Type[] { typeof(Texture) });
            if (toTensorMethod != null)
            {
                Debug.Log("Используем метод ToTensor с параметром Texture");
                object inputTensor = toTensorMethod.Invoke(null, new object[] { inputTexture });
                if (inputTensor != null)
                {
                    ExecuteModelAndProcessResult(inputTensor);
                    return;
                }
            }

            // Пробуем метод TextureToTensor (Sentis 2.1.x+)
            var textureToTensorNewMethod = textureConverterType.GetMethod("TextureToTensor", new Type[] { typeof(Texture) });
            if (textureToTensorNewMethod != null)
            {
                Debug.Log("Используем метод TextureToTensor с параметром Texture");
                object inputTensor = textureToTensorNewMethod.Invoke(null, new object[] { inputTexture });
                if (inputTensor != null)
                {
                    ExecuteModelAndProcessResult(inputTensor);
                    return;
                }
            }

            // Если не удалось создать тензор ни одним из методов, используем заглушку
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
    /// Обрабатывает результат сегментации и обновляет маску, используя рефлексию
    /// </summary>
    private void ProcessSegmentationResult(object outputTensor)
    {
        try
        {
            // Получаем ранг тензора через рефлексию
            var tensorType = outputTensor.GetType();
            var shapeProperty = tensorType.GetProperty("shape");
            if (shapeProperty == null)
            {
                Debug.LogError("Свойство shape не найдено в Tensor");
                return;
            }

            var shape = shapeProperty.GetValue(outputTensor);
            var rankProperty = shape.GetType().GetProperty("rank");
            if (rankProperty == null)
            {
                Debug.LogError("Свойство rank не найдено в TensorShape");
                return;
            }

            int outputRank = (int)rankProperty.GetValue(shape);
            Debug.Log($"Обработка тензора с рангом {outputRank}");

            // Находим тип TextureConverter
            Type textureConverterType = Type.GetType("Unity.Sentis.TextureConverter, Unity.Sentis");
            if (textureConverterType == null)
            {
                Debug.LogError("Тип Unity.Sentis.TextureConverter не найден");
                return;
            }

            // Попытка использовать SentisCompat для оптимизации для конкретной версии
            bool renderedUsingCompat = false;
            Type sentisCompatType = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                sentisCompatType = assembly.GetType("SentisCompat");
                if (sentisCompatType != null) break;
            }

            if (sentisCompatType != null)
            {
                var renderToTextureMethod = sentisCompatType.GetMethod("RenderTensorToTexture",
                    BindingFlags.Public | BindingFlags.Static);

                if (renderToTextureMethod != null)
                {
                    try
                    {
                        bool success = (bool)renderToTextureMethod.Invoke(null, new object[] { outputTensor, segmentationMaskTexture });
                        renderedUsingCompat = success;

                        if (success)
                        {
                            Debug.Log("Маска успешно отрисована через SentisCompat");
                            return;
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"Ошибка при попытке рендеринга через SentisCompat: {e.Message}");
                        // Продолжаем стандартными методами при ошибке
                    }
                }
            }

            // Если не получилось через SentisCompat, пробуем стандартные методы
            if (!renderedUsingCompat)
            {
                // Находим метод RenderToTexture для текущей версии Sentis
                var methods = textureConverterType.GetMethods()
                    .Where(m => m.Name == "RenderToTexture" && m.GetParameters().Length >= 2)
                    .ToArray();

                if (methods.Length > 0)
                {
                    Debug.Log($"Найдено {methods.Length} методов RenderToTexture");

                    bool rendered = false;
                    foreach (var method in methods)
                    {
                        try
                        {
                            var parameters = method.GetParameters();

                            // Проверяем сигнатуру
                            string parameterTypes = string.Join(", ", parameters.Select(p => p.ParameterType.Name));
                            Debug.Log($"Пробуем метод RenderToTexture({parameterTypes})");

                            if (parameters.Length == 2)
                            {
                                // RenderToTexture(tensor, texture)
                                method.Invoke(null, new object[] { outputTensor, segmentationMaskTexture });
                                rendered = true;
                                Debug.Log("Маска успешно отрисована через RenderToTexture (2 параметра)");
                                break;
                            }
                            else if (parameters.Length == 3)
                            {
                                // Создаем TextureTransform (default)
                                Type textureTransformType = Type.GetType("Unity.Sentis.TextureTransform, Unity.Sentis");
                                if (textureTransformType == null)
                                {
                                    Debug.LogError("Тип Unity.Sentis.TextureTransform не найден");
                                    continue;
                                }

                                object textureTransform = Activator.CreateInstance(textureTransformType);
                                method.Invoke(null, new object[] { outputTensor, segmentationMaskTexture, textureTransform });
                                rendered = true;
                                Debug.Log("Маска успешно отрисована через RenderToTexture (3 параметра)");
                                break;
                            }
                        }
                        catch (Exception e)
                        {
                            Debug.LogWarning($"Ошибка при вызове метода RenderToTexture: {e.Message}");
                        }
                    }

                    if (!rendered)
                    {
                        Debug.LogError("Не удалось отрисовать маску через имеющиеся методы RenderToTexture");
                        // Здесь можно добавить ручное копирование данных из тензора в текстуру при необходимости
                    }
                }
                else
                {
                    Debug.LogError("Методы RenderToTexture не найдены в TextureConverter");
                }
            }

            // Отладочное сохранение маски, если включено
            if (saveDebugMask)
            {
                SaveDebugMask();
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"Ошибка при обработке результата сегментации: {e.Message}\nStackTrace: {e.StackTrace}");
        }
    }

    /// <summary>
    /// Сохраняет текущую маску сегментации для отладки
    /// </summary>
    private void SaveDebugMask()
    {
        // Реализация сохранения отладочной текстуры
    }

    /// <summary>
    /// Безопасно освобождает ресурсы движка через рефлексию
    /// </summary>
    private void DisposeEngine()
    {
        if (engine != null)
        {
            try
            {
                // Проверяем, реализует ли engine интерфейс IDisposable
                if (engine is IDisposable disposable)
                {
                    disposable.Dispose();
                }
                else
                {
                    // Пытаемся найти метод Dispose через рефлексию
                    var disposeMethod = engine.GetType().GetMethod("Dispose");
                    if (disposeMethod != null)
                    {
                        disposeMethod.Invoke(engine, null);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogError($"Ошибка при освобождении ресурсов Engine: {e.Message}");
            }
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