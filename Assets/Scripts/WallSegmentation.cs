using Unity.Sentis;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Unity.Collections;
using System;
using System.Collections;

public class WallSegmentation : MonoBehaviour
{
    [SerializeField] private ModelAsset modelAsset;
    [SerializeField] private ARCameraManager arCameraManager;
    [SerializeField] private RenderTexture segmentationMaskTexture;
    [SerializeField] private ARSessionManager arSessionManager;

    // Индекс класса "стена" в модели сегментации
    [SerializeField] private int wallClassIndex = 0; // В наборе ADE20K "стена" имеет индекс 0

    // Настройки инициализации
    [SerializeField] private float initDelay = 0.5f; // Задержка инициализации для iOS
    [SerializeField] private int skipFrames = 6; // Пропуск кадров для производительности

    private Model runtimeModel;
    private Worker engine;
    private Tensor<float> inputTensor;
    private Texture2D tempTexture;

    // Константы для модели
    private const int INPUT_WIDTH = 320;
    private const int INPUT_HEIGHT = 320;
    private const int INPUT_CHANNELS = 3;
    private const int OUTPUT_WIDTH = 80;
    private const int OUTPUT_HEIGHT = 80;
    private const int NUM_CLASSES = 150;

    // Для простой постобработки
    private Texture2D wallMaskTexture;
    private Color32[] wallMaskColors;

    // Флаг инициализации
    private bool isInitialized = false;
    private bool shouldInitialize = false; // Flag to track when AR session is ready
    private int frameCounter = 0;

    // Публичное свойство для доступа к маске сегментации стен
    public RenderTexture SegmentationMaskTexture
    {
        get
        {
            // Проверяем, что текстура существует и правильно инициализирована
            if (segmentationMaskTexture == null)
            {
                CreateMaskTexture();
            }
            else if (!segmentationMaskTexture.IsCreated())
            {
                segmentationMaskTexture.Create();
            }
            return segmentationMaskTexture;
        }
    }

    // Публичное свойство для проверки инициализации компонента
    public bool IsInitialized => isInitialized && runtimeModel != null && engine != null;

    private void CreateMaskTexture()
    {
        if (segmentationMaskTexture != null && segmentationMaskTexture.IsCreated())
        {
            segmentationMaskTexture.Release();
        }

        segmentationMaskTexture = new RenderTexture(OUTPUT_WIDTH, OUTPUT_HEIGHT, 0, RenderTextureFormat.RFloat);
        segmentationMaskTexture.enableRandomWrite = true;
        segmentationMaskTexture.Create();
        Debug.Log("SegmentationMaskTexture created or recreated successfully");
    }

    private void Start()
    {
        // Находим ARSessionManager, если он не задан
        if (arSessionManager == null)
        {
            arSessionManager = FindObjectOfType<ARSessionManager>();
        }

        // Находим ARCameraManager, если он не задан
        if (arCameraManager == null)
        {
            if (Camera.main != null)
            {
                arCameraManager = Camera.main.GetComponent<ARCameraManager>();
            }

            if (arCameraManager == null)
            {
                var arCameras = FindObjectsOfType<ARCameraManager>();
                if (arCameras.Length > 0)
                {
                    arCameraManager = arCameras[0];
                    Debug.Log("WallSegmentation: найден ARCameraManager");
                }
            }
        }

        // Проверяем наличие модели
        if (modelAsset == null)
        {
            Debug.LogError("ModelAsset is not assigned in the inspector. Disabling script.");
            this.enabled = false;
            return;
        }

        // На iOS ждем инициализации AR сессии
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
            Debug.Log("WallSegmentation ждет инициализации AR сессии");
        }
#else
        // В редакторе или на других платформах инициализируем сразу
        InitializeSentisModel();
#endif
    }

    // Ответ на сообщение от ARSessionManager
    public void OnARSessionInitialized()
    {
        Debug.Log("WallSegmentation получил уведомление о готовности AR сессии");
        shouldInitialize = true;

#if UNITY_IOS && !UNITY_EDITOR
        StartCoroutine(InitializeWithDelay());
#else
        if (!isInitialized)
        {
            InitializeSentisModel();
        }
#endif
    }

    // Метод с задержкой для iOS
    private IEnumerator InitializeWithDelay()
    {
        // Ждем один кадр, чтобы убедиться что AR сессия инициализирована
        yield return null;

        // Дополнительная задержка для iOS
        Debug.Log("WallSegmentation: ожидание перед инициализацией...");
        yield return new WaitForSeconds(initDelay);

        // Теперь инициализируем модель
        InitializeSentisModel();
    }

    private void InitializeSentisModel()
    {
        if (isInitialized) return; // Избегаем повторной инициализации

        try
        {
            Debug.Log("WallSegmentation: начало инициализации модели Sentis");
            // Инициализация модели Sentis
            runtimeModel = ModelLoader.Load(modelAsset);

            // Предпочитаем GPU для мобильных устройств
#if UNITY_IOS || UNITY_ANDROID
            engine = new Worker(runtimeModel, BackendType.GPUCompute);
#else
            engine = WorkerFactory.CreateWorker(BackendType.GPUCompute, runtimeModel);
#endif

            // Создаем входной тензор заранее
            inputTensor = new Tensor<float>(new TensorShape(1, INPUT_CHANNELS, INPUT_HEIGHT, INPUT_WIDTH));

            // Создаем временную текстуру для преобразования изображения камеры
            tempTexture = new Texture2D(INPUT_WIDTH, INPUT_HEIGHT, TextureFormat.RGB24, false);

            // Создаем выходную текстуру для маски, если она не назначена
            if (segmentationMaskTexture == null)
            {
                CreateMaskTexture();
            }
            else if (!segmentationMaskTexture.IsCreated())
            {
                segmentationMaskTexture.Create();
            }

            // Инициализация маски стен
            wallMaskTexture = new Texture2D(OUTPUT_WIDTH, OUTPUT_HEIGHT, TextureFormat.R8, false);
            wallMaskColors = new Color32[OUTPUT_WIDTH * OUTPUT_HEIGHT];

            // Заполняем маску нулями
            for (int i = 0; i < wallMaskColors.Length; i++)
            {
                wallMaskColors[i] = new Color32(0, 0, 0, 255);
            }
            wallMaskTexture.SetPixels32(wallMaskColors);
            wallMaskTexture.Apply();

            // Копируем пустую маску в RenderTexture
            Graphics.Blit(wallMaskTexture, segmentationMaskTexture);

            // Устанавливаем флаг инициализации
            isInitialized = true;

            Debug.Log("WallSegmentation initialized successfully");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Error initializing WallSegmentation: {e.Message}");
            this.enabled = false;
        }
    }

    private void OnDestroy()
    {
        engine?.Dispose();
        inputTensor?.Dispose();

        if (tempTexture != null)
        {
            Destroy(tempTexture);
        }

        if (segmentationMaskTexture != null)
        {
            if (segmentationMaskTexture.IsCreated()) segmentationMaskTexture.Release();
            Destroy(segmentationMaskTexture);
        }

        if (wallMaskTexture != null)
        {
            Destroy(wallMaskTexture);
        }
    }

    void Update()
    {
        // Проверяем готовность AR сессии (только для iOS)
#if UNITY_IOS && !UNITY_EDITOR
        if (!shouldInitialize && arSessionManager != null)
        {
            if (arSessionManager.IsSessionInitialized())
            {
                shouldInitialize = true;
                if (!isInitialized)
                {
                    StartCoroutine(InitializeWithDelay());
                }
            }
            return; // Не продолжаем выполнение Update, пока AR сессия не готова
        }
#endif

        if (!isInitialized || engine == null) return;

        // Пропускаем кадры для производительности
        frameCounter++;
        if (frameCounter % skipFrames != 0) return;

        // Проверяем наличие ARCameraManager
        if (arCameraManager == null)
        {
            // Пытаемся найти ARCameraManager
            if (Camera.main != null)
            {
                arCameraManager = Camera.main.GetComponent<ARCameraManager>();
                if (arCameraManager == null) return;
            }
            else
            {
                return;
            }
        }

        // Пытаемся получить изображение с камеры
        if (!arCameraManager.TryAcquireLatestCpuImage(out XRCpuImage cpuImage))
        {
            return;
        }

        using (cpuImage)
        {
            // Конвертируем XRCpuImage в формат, совместимый с моделью
            var conversionParams = new XRCpuImage.ConversionParams
            {
                inputRect = new RectInt(0, 0, cpuImage.width, cpuImage.height),
                outputDimensions = new Vector2Int(INPUT_WIDTH, INPUT_HEIGHT),
                outputFormat = TextureFormat.RGB24,
                transformation = XRCpuImage.Transformation.MirrorY
            };

            // Выделяем массив для данных изображения
            var rawTextureData = new NativeArray<byte>(INPUT_WIDTH * INPUT_HEIGHT * 3, Allocator.Temp);

            // Конвертируем изображение
            cpuImage.Convert(conversionParams, rawTextureData);

            // Загружаем данные в текстуру
            tempTexture.LoadRawTextureData(rawTextureData);
            tempTexture.Apply();
            rawTextureData.Dispose();

            // Преобразуем текстуру в тензор для входа модели, используя актуальный API
            var transform = new TextureTransform()
                .SetDimensions(INPUT_WIDTH, INPUT_HEIGHT, INPUT_CHANNELS);
            TextureConverter.ToTensor(tempTexture, inputTensor, transform);

            // Создаем нормализованный тензор
            using (var normalizedTensor = new Tensor<float>(inputTensor.shape))
            {
                // Создаем тензор с нормализованными значениями (деление на 255)
                for (int b = 0; b < 1; b++) // batch size = 1
                {
                    for (int c = 0; c < INPUT_CHANNELS; c++)
                    {
                        for (int h = 0; h < INPUT_HEIGHT; h++)
                        {
                            for (int w = 0; w < INPUT_WIDTH; w++)
                            {
                                // Нормализуем значения, деля на 255
                                // Используем ReadbackAndClone для получения тензора с CPU-данными
                                float value = inputTensor.ReadbackAndClone()[b, c, h, w];
                                normalizedTensor[b, c, h, w] = value / 255.0f;
                            }
                        }
                    }
                }

                // Запускаем инференс
                engine.Schedule(normalizedTensor);

                // Получаем выходной тензор
                var outputTensor = engine.PeekOutput("logits") as Tensor<float>;

                if (outputTensor != null)
                {
                    // Обрабатываем выходной тензор для получения маски сегментации стен
                    ProcessSegmentationOutput(outputTensor);
                }
            }
        }
    }

    private void ProcessSegmentationOutput(Tensor<float> outputTensor)
    {
        // Загружаем данные тензора на CPU для обработки
        var outputCPU = outputTensor.ReadbackAndClone();
        var shape = outputCPU.shape;

        // Проверка формы тензора
        if (shape.rank != 4 || shape[0] != 1 || shape[1] != NUM_CLASSES ||
            shape[2] != OUTPUT_HEIGHT || shape[3] != OUTPUT_WIDTH)
        {
            Debug.LogError($"Неожиданная форма выходного тензора: {shape}");
            return;
        }

        // Находим argmax и создаем бинарную маску стен
        for (int y = 0; y < OUTPUT_HEIGHT; y++)
        {
            for (int x = 0; x < OUTPUT_WIDTH; x++)
            {
                int maxClass = 0;
                float maxValue = outputCPU[0, 0, y, x];

                // Находим класс с максимальным значением (argmax)
                for (int c = 1; c < NUM_CLASSES; c++)
                {
                    float value = outputCPU[0, c, y, x];
                    if (value > maxValue)
                    {
                        maxValue = value;
                        maxClass = c;
                    }
                }

                // Устанавливаем значение в маске: 255 для стены, 0 для не стены
                byte maskValue = (byte)((maxClass == wallClassIndex) ? 255 : 0);
                int pixelIndex = y * OUTPUT_WIDTH + x;
                wallMaskColors[pixelIndex] = new Color32(maskValue, maskValue, maskValue, 255);
            }
        }

        // Очищаем временный тензор
        outputCPU.Dispose();

        // Обновляем текстуру маски
        wallMaskTexture.SetPixels32(wallMaskColors);
        wallMaskTexture.Apply();

        // Копируем в RenderTexture для использования в шейдерах
        Graphics.Blit(wallMaskTexture, segmentationMaskTexture);
    }
}