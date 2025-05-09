using UnityEngine;
using System.Collections;
using System.Reflection;
using System;
using System.Linq;

/// <summary>
/// Компонент для явной инициализации Unity Sentis
/// и тестирования его работоспособности
/// </summary>
public class SentisInitializer : MonoBehaviour
{
      private static SentisInitializer _instance;
      public static SentisInitializer Instance
      {
            get
            {
                  if (_instance == null)
                  {
                        _instance = FindObjectOfType<SentisInitializer>();

                        if (_instance == null)
                        {
                              GameObject obj = new GameObject("Sentis Initializer");
                              _instance = obj.AddComponent<SentisInitializer>();
                              DontDestroyOnLoad(obj);
                        }
                  }
                  return _instance;
            }
      }

      private static bool _isInitialized = false;
      private static bool _isInitializing = false;
      private static float _initStartTime = 0f;

      /// <summary>
      /// Показывает, был ли Sentis успешно инициализирован
      /// </summary>
      public static bool IsInitialized => _isInitialized;

      /// <summary>
      /// Показывает, находится ли Sentis в процессе инициализации
      /// </summary>
      public static bool IsInitializing => _isInitializing;

      [Tooltip("Автоматически инициализировать при старте")]
      public bool initializeOnStart = true;

      [Tooltip("Тестовая ONNX модель маленького размера")]
      public UnityEngine.Object testModel;

      [Tooltip("Выполнить тест модели при старте")]
      public bool testModelOnStart = false;

      [Tooltip("Максимальное время в секундах ожидания инициализации")]
      public float initializationTimeout = 30f;

      [Tooltip("Использовать кортуину для асинхронной загрузки")]
      public bool useAsyncInitialization = true;

      private void Awake()
      {
            if (_instance != null && _instance != this)
            {
                  Destroy(gameObject);
                  return;
            }

            _instance = this;
            DontDestroyOnLoad(gameObject);
      }

      private void Start()
      {
            if (initializeOnStart)
            {
                  if (useAsyncInitialization)
                  {
                        StartCoroutine(InitializeAsync());
                  }
                  else
                  {
                        Initialize();
                  }

                  if (testModelOnStart && testModel != null)
                  {
                        StartCoroutine(TestSentisModel());
                  }
            }
      }

      /// <summary>
      /// Асинхронно инициализирует Unity Sentis через корутину
      /// </summary>
      private IEnumerator InitializeAsync()
      {
            if (_isInitialized)
            {
                  Debug.Log("SentisInitializer: Unity Sentis уже инициализирован");
                  yield break;
            }

            if (_isInitializing)
            {
                  Debug.Log("SentisInitializer: Unity Sentis уже находится в процессе инициализации");
                  yield break;
            }

            _isInitializing = true;
            _initStartTime = Time.realtimeSinceStartup;

            Debug.Log("SentisInitializer: Начинаю асинхронную инициализацию Unity Sentis...");

            // Основная инициализация
            bool success = false;
            Exception lastException = null;

            // Попытка 1: Стандартная инициализация
            try
            {
                  Initialize();
                  success = _isInitialized;
            }
            catch (Exception e)
            {
                  lastException = e;
                  Debug.LogWarning($"SentisInitializer: Первая попытка инициализации не удалась: {e.Message}");
            }

            // Если не удалось инициализировать с первой попытки,
            // пробуем альтернативные методы с небольшими паузами
            if (!success)
            {
                  // Попытка 2: Ждем кадр и пробуем снова
                  yield return null;

                  try
                  {
                        Initialize();
                        success = _isInitialized;
                  }
                  catch (Exception e)
                  {
                        lastException = e;
                        Debug.LogWarning($"SentisInitializer: Вторая попытка инициализации не удалась: {e.Message}");
                  }

                  // Попытка 3: Ждем немного дольше и пробуем еще раз
                  if (!success)
                  {
                        yield return new WaitForSeconds(0.5f);

                        try
                        {
                              // Ранее вызывали PreloadSentisAssemblies и yield return null внутри try-catch
                              // что вызывало ошибку CS1626
                              PreloadSentisAssemblies();

                              // Вызываем Initialize вне yield
                              Initialize();
                              success = _isInitialized;
                        }
                        catch (Exception e)
                        {
                              lastException = e;
                              Debug.LogWarning($"SentisInitializer: Третья попытка инициализации не удалась: {e.Message}");
                        }

                        // Yield вынесен за пределы try-catch
                        yield return null;
                  }
            }

            _isInitializing = false;

            if (!success)
            {
                  Debug.LogError("SentisInitializer: Не удалось инициализировать Unity Sentis асинхронно после нескольких попыток");
                  if (lastException != null)
                  {
                        Debug.LogError($"SentisInitializer: Последнее исключение: {lastException.Message}");
                        if (lastException.InnerException != null)
                        {
                              Debug.LogError($"SentisInitializer: Внутреннее исключение: {lastException.InnerException.Message}");
                        }
                  }
            }
            else
            {
                  Debug.Log("SentisInitializer: Unity Sentis успешно инициализирован асинхронно");
            }
      }

      /// <summary>
      /// Предварительно загружает сборки Sentis
      /// </summary>
      private void PreloadSentisAssemblies()
      {
            Debug.Log("SentisInitializer: Предварительная загрузка сборок Sentis...");

            try
            {
                  // Принудительно загружаем основные сборки Sentis
                  Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();

                  // Список имен сборок Sentis, которые нужно загрузить
                  string[] sentisAssemblyNames = new string[] {
                        "Unity.Sentis",
                        "Unity.Sentis.ONNX",
                        "Unity.Sentis.MacBLAS",
                        "Unity.Sentis.iOSBLAS"
                  };

                  foreach (string assemblyName in sentisAssemblyNames)
                  {
                        bool found = false;
                        foreach (Assembly assembly in assemblies)
                        {
                              if (assembly.GetName().Name == assemblyName)
                              {
                                    found = true;
                                    Debug.Log($"SentisInitializer: Сборка {assemblyName} уже загружена, версия {assembly.GetName().Version}");

                                    // Пробуем предварительно загрузить некоторые типы из сборки
                                    if (assemblyName == "Unity.Sentis")
                                    {
                                          var types = new string[] {
                                                "Unity.Sentis.ModelLoader",
                                                "Unity.Sentis.Model",
                                                "Unity.Sentis.Worker",
                                                "Unity.Sentis.WorkerFactory",
                                                "Unity.Sentis.BackendType",
                                                "Unity.Sentis.TextureConverter",
                                                "Unity.Sentis.Tensor"
                                          };

                                          foreach (var typeName in types)
                                          {
                                                try
                                                {
                                                      var type = assembly.GetType(typeName);
                                                      Debug.Log($"SentisInitializer: Тип {typeName} предварительно загружен: {(type != null ? "✓" : "✗")}");
                                                }
                                                catch (Exception e)
                                                {
                                                      Debug.LogWarning($"SentisInitializer: Ошибка при предзагрузке типа {typeName}: {e.Message}");
                                                }
                                          }
                                    }

                                    break;
                              }
                        }

                        if (!found)
                        {
                              Debug.LogWarning($"SentisInitializer: Сборка {assemblyName} не найдена");
                        }
                  }
            }
            catch (Exception e)
            {
                  Debug.LogError($"SentisInitializer: Ошибка при предзагрузке сборок: {e.Message}");
            }
      }

      /// <summary>
      /// Инициализирует Unity Sentis для использования
      /// </summary>
      public void Initialize()
      {
            // Если уже инициализирован, просто выходим
            if (_isInitialized)
            {
                  Debug.Log("SentisInitializer: Unity Sentis уже инициализирован");
                  return;
            }

            // Если уже идет процесс инициализации, проверяем таймаут
            if (_isInitializing)
            {
                  float currentTime = Time.realtimeSinceStartup;
                  if (currentTime - _initStartTime > initializationTimeout)
                  {
                        Debug.LogWarning($"SentisInitializer: Превышено время ожидания инициализации ({initializationTimeout} секунд)");
                        _isInitializing = false;
                  }
                  else
                  {
                        Debug.Log("SentisInitializer: Unity Sentis уже инициализируется");
                        return;
                  }
            }

            _isInitializing = true;
            _initStartTime = Time.realtimeSinceStartup;

            Debug.Log("SentisInitializer: Инициализация Unity Sentis...");

            try
            {
                  // Выполняем предварительную диагностику
                  // Проверяем наличие SentisCompat
                  Type sentisCompatType = Type.GetType("SentisCompat");
                  if (sentisCompatType != null)
                  {
                        // Вызываем DiagnosticReport через рефлексию
                        MethodInfo diagnosticMethod = sentisCompatType.GetMethod("DiagnosticReport", BindingFlags.Public | BindingFlags.Static);
                        if (diagnosticMethod != null)
                        {
                              diagnosticMethod.Invoke(null, null);
                        }
                  }
                  else
                  {
                        Debug.Log("SentisInitializer: SentisCompat не найден, выполняем встроенную диагностику");
                        // Дальше выполняется встроенная диагностика...
                  }

                  // Проверяем наличие сборки Unity.Sentis
                  var sentisAssembly = AppDomain.CurrentDomain.GetAssemblies()
                      .FirstOrDefault(a => a.GetName().Name == "Unity.Sentis");

                  if (sentisAssembly == null)
                  {
                        Debug.LogError("SentisInitializer: Сборка Unity.Sentis не найдена. Установите пакет Unity Sentis");
                        _isInitializing = false;
                        return;
                  }

                  // Выполняем ЯВНУЮ инициализацию Sentis
                  // Этот блок кода гарантирует загрузку всех необходимых Sentis типов

                  // 1. Пробуем загрузить основные типы Sentis - это вызовет их инициализацию
                  Type modelLoaderType = sentisAssembly.GetType("Unity.Sentis.ModelLoader");
                  Type modelType = sentisAssembly.GetType("Unity.Sentis.Model");
                  Type modelAssetType = sentisAssembly.GetType("Unity.Sentis.ModelAsset");
                  Type workerType = sentisAssembly.GetType("Unity.Sentis.Worker");
                  Type workerFactoryType = sentisAssembly.GetType("Unity.Sentis.WorkerFactory");
                  Type opsType = sentisAssembly.GetType("Unity.Sentis.Ops");
                  Type textureConverterType = sentisAssembly.GetType("Unity.Sentis.TextureConverter");

                  int typesFound = 0;
                  if (modelLoaderType != null) typesFound++;
                  if (modelType != null) typesFound++;
                  if (modelAssetType != null) typesFound++;
                  if (workerType != null || workerFactoryType != null) typesFound++;
                  if (textureConverterType != null) typesFound++;

                  // 2. Пробуем загружать специальные типы для ONNX импорта
                  Type onnxModelType = null;
                  try
                  {
                        // Пытаемся найти сборку Unity.Sentis.ONNX
                        var onnxAssembly = AppDomain.CurrentDomain.GetAssemblies()
                            .FirstOrDefault(a => a.GetName().Name == "Unity.Sentis.ONNX");

                        if (onnxAssembly != null)
                        {
                              onnxModelType = onnxAssembly.GetType("Unity.Sentis.ONNX.ONNXModelConverter");
                              if (onnxModelType == null)
                              {
                                    // Пробуем искать в основной сборке Sentis
                                    onnxModelType = sentisAssembly.GetType("Unity.Sentis.ONNX.ONNXModelConverter");
                              }
                        }
                        else
                        {
                              // Пробуем искать в основной сборке Sentis
                              onnxModelType = sentisAssembly.GetType("Unity.Sentis.ONNX.ONNXModelConverter");
                        }
                  }
                  catch (Exception e)
                  {
                        Debug.LogWarning($"SentisInitializer: Ошибка при поиске типов ONNX: {e.Message}");
                  }

                  if (onnxModelType != null) typesFound++;

                  // 3. Выполняем загрузку типов бэкендов
                  Type backendType = sentisAssembly.GetType("Unity.Sentis.BackendType");
                  if (backendType != null)
                  {
                        typesFound++;
                        // Перечисляем все значения enum чтобы убедиться, что они загружены
                        var values = Enum.GetValues(backendType);
                        Debug.Log($"SentisInitializer: Загружены BackendType значения: {values.Length}");
                  }

                  // 4. Для Sentis 2.1.2 проверяем наличие типов TextureConverter и его методов
                  if (textureConverterType != null)
                  {
                        try
                        {
                              var methods = textureConverterType.GetMethods(BindingFlags.Public | BindingFlags.Static);
                              var textureMethods = methods.Where(m =>
                                    m.Name == "TextureToTensor" ||
                                    m.Name == "ToTensor" ||
                                    m.Name == "RenderToTexture").ToArray();

                              if (textureMethods.Length > 0)
                              {
                                    Debug.Log($"SentisInitializer: Найдено {textureMethods.Length} методов TextureConverter");
                                    typesFound++;
                              }
                        }
                        catch (Exception e)
                        {
                              Debug.LogWarning($"SentisInitializer: Ошибка при проверке методов TextureConverter: {e.Message}");
                        }
                  }

                  // Проверяем результаты
                  int requiredTypesCount = 6; // Минимальное количество типов для работы
                  if (typesFound >= requiredTypesCount)
                  {
                        _isInitialized = true;
                        Debug.Log($"SentisInitializer: Unity Sentis успешно инициализирован (загружено {typesFound}/{requiredTypesCount} типов)");
                  }
                  else
                  {
                        Debug.LogWarning($"SentisInitializer: Unity Sentis загружен частично (найдено только {typesFound}/{requiredTypesCount} типов)");
                        _isInitialized = false;
                  }
            }
            catch (Exception e)
            {
                  Debug.LogError($"SentisInitializer: Ошибка при инициализации Sentis: {e.Message}");
                  if (e.InnerException != null)
                  {
                        Debug.LogError($"SentisInitializer: Внутреннее исключение: {e.InnerException.Message}");
                  }
                  _isInitialized = false;
            }
            finally
            {
                  _isInitializing = false;
            }
      }

      /// <summary>
      /// Тестирует работу Sentis на простой модели
      /// </summary>
      public IEnumerator TestSentisModel()
      {
            Debug.Log("SentisInitializer: Запуск теста Unity Sentis...");

            UnityEngine.Object modelToTest = testModel;

            // Если модель не указана, попробуем найти её автоматически
            if (modelToTest == null)
            {
                  Debug.Log("SentisInitializer: Тестовая модель не указана, пытаемся найти автоматически...");

                  // Поиск в Resources
                  var resourceModels = Resources.FindObjectsOfTypeAll<UnityEngine.Object>()
                      .Where(obj => obj.name.EndsWith(".onnx"));

                  if (resourceModels.Any())
                  {
                        modelToTest = resourceModels.First();
                        Debug.Log($"SentisInitializer: Найдена модель в Resources: {modelToTest.name}");
                  }
                  else
                  {
                        Debug.LogWarning("SentisInitializer: Не удалось найти тестовую модель. Укажите модель вручную.");
                        yield break;
                  }
            }

            // Если Sentis ещё не инициализирован, делаем это сейчас
            if (!_isInitialized)
            {
                  if (useAsyncInitialization)
                  {
                        yield return StartCoroutine(InitializeAsync());
                  }
                  else
                  {
                        Initialize();
                  }

                  // Даем время на инициализацию
                  yield return new WaitForSeconds(0.5f);

                  if (!_isInitialized)
                  {
                        Debug.LogError("SentisInitializer: Не удалось инициализировать Sentis для теста");
                        yield break;
                  }
            }

            try
            {
                  // Загрузка модели
                  Debug.Log($"SentisInitializer: Загрузка тестовой модели {modelToTest.name}...");
                  object model = null;

                  // Проверяем наличие SentisCompat
                  Type sentisCompatType = Type.GetType("SentisCompat");
                  if (sentisCompatType != null)
                  {
                        // Вызываем LoadModel через рефлексию
                        MethodInfo loadModelMethod = sentisCompatType.GetMethod("LoadModel", BindingFlags.Public | BindingFlags.Static);
                        if (loadModelMethod != null)
                        {
                              model = loadModelMethod.Invoke(null, new object[] { modelToTest });
                        }
                  }
                  else
                  {
                        // Если SentisCompat недоступен, используем прямой вызов через рефлексию
                        var sentisAssembly = AppDomain.CurrentDomain.GetAssemblies()
                            .FirstOrDefault(a => a.GetName().Name == "Unity.Sentis");

                        if (sentisAssembly != null)
                        {
                              var modelLoaderType = sentisAssembly.GetType("Unity.Sentis.ModelLoader");
                              if (modelLoaderType != null)
                              {
                                    var loadMethod = modelLoaderType.GetMethod("Load", new[] { modelToTest.GetType() });
                                    if (loadMethod != null)
                                    {
                                          model = loadMethod.Invoke(null, new object[] { modelToTest });
                                    }
                              }
                        }
                  }

                  if (model == null)
                  {
                        Debug.LogError("SentisInitializer: Не удалось загрузить тестовую модель");
                        yield break;
                  }

                  Debug.Log($"SentisInitializer: Модель успешно загружена, создаем Worker...");

                  // Создание Worker
                  object worker = null;

                  // Пробуем через SentisCompat
                  if (sentisCompatType != null)
                  {
                        MethodInfo createWorkerMethod = sentisCompatType.GetMethod("CreateWorker", BindingFlags.Public | BindingFlags.Static);
                        if (createWorkerMethod != null)
                        {
                              worker = createWorkerMethod.Invoke(null, new object[] { model, 0 }); // 0 = CPU backend
                        }
                  }
                  else
                  {
                        // Прямой вызов через рефлексию
                        var sentisAssembly = AppDomain.CurrentDomain.GetAssemblies()
                            .FirstOrDefault(a => a.GetName().Name == "Unity.Sentis");

                        if (sentisAssembly != null)
                        {
                              // Проверяем наличие Worker (новый API) или WorkerFactory (старый API)
                              var workerType = sentisAssembly.GetType("Unity.Sentis.Worker");
                              var workerFactoryType = sentisAssembly.GetType("Unity.Sentis.WorkerFactory");

                              if (workerType != null)
                              {
                                    // Новый API (Worker)
                                    var backendType = sentisAssembly.GetType("Unity.Sentis.BackendType");
                                    if (backendType != null)
                                    {
                                          var backendEnum = Enum.ToObject(backendType, 0); // 0 = CPU

                                          var constructor = workerType.GetConstructor(new[] { model.GetType(), backendType });
                                          if (constructor != null)
                                          {
                                                worker = constructor.Invoke(new[] { model, backendEnum });
                                          }
                                    }
                              }
                              else if (workerFactoryType != null)
                              {
                                    // Старый API (WorkerFactory)
                                    var createWorkerMethod = workerFactoryType.GetMethod("CreateWorker",
                                          new[] { model.GetType() });
                                    if (createWorkerMethod != null)
                                    {
                                          worker = createWorkerMethod.Invoke(null, new[] { model });
                                    }
                              }
                        }
                  }

                  if (worker == null)
                  {
                        Debug.LogError("SentisInitializer: Не удалось создать Worker для тестовой модели");
                        yield break;
                  }

                  Debug.Log("SentisInitializer: Worker успешно создан. Тест пройден!");

                  // Освобождаем ресурсы Worker
                  try
                  {
                        var workerType = worker.GetType();
                        var disposeMethod = workerType.GetMethod("Dispose");
                        if (disposeMethod != null)
                        {
                              disposeMethod.Invoke(worker, null);
                              Debug.Log("SentisInitializer: Worker успешно освобожден");
                        }
                  }
                  catch (Exception e)
                  {
                        Debug.LogWarning($"SentisInitializer: Не удалось освободить Worker: {e.Message}");
                  }
            }
            catch (Exception e)
            {
                  Debug.LogError($"SentisInitializer: Ошибка при тестировании Sentis: {e.Message}");
                  if (e.InnerException != null)
                  {
                        Debug.LogError($"SentisInitializer: Внутреннее исключение: {e.InnerException.Message}");
                  }
            }
      }
}