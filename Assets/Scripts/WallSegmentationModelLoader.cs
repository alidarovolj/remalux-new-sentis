using UnityEngine;
using System.Collections;
using System;
using System.Linq;
using System.Reflection;
using UnityEngine.UI; // Для доступа к компонентам UI

/// <summary>
/// Компонент для безопасной загрузки моделей WallSegmentation.
/// Предотвращает краш Unity, который может произойти при прямом назначении модели.
/// </summary>
[RequireComponent(typeof(WallSegmentation))]
public class WallSegmentationModelLoader : MonoBehaviour
{
      [Tooltip("Объект модели ONNX для сегментации стен")]
      public UnityEngine.Object modelAsset;

      [Tooltip("Максимальное время ожидания загрузки модели в секундах")]
      public float loadTimeout = 30f;

      [Tooltip("Автоматически загружать модель при старте")]
      public bool loadOnStart = true;

      [Tooltip("Предпочитаемый бэкенд (0 = CPU, 1 = GPUCompute)")]
      public int preferredBackend = 0;

      [Tooltip("Явно инициализировать Sentis перед загрузкой")]
      public bool initializeSentisBeforeLoading = true;

      private WallSegmentation wallSegmentation;
      private bool isModelLoading = false;
      private bool isModelLoaded = false;
      private string lastErrorMessage = null;
      private object loadedModelInstance = null; // Здесь будем хранить экземпляр загруженной модели

      // Используем MonoBehaviour вместо конкретного типа, чтобы избежать зависимости
      private MonoBehaviour sentisInitializer;
      private bool triedToInitializeSentis = false;
      private bool isSentisInitialized = false;

      private void Start()
      {
            // Получаем ссылку на компонент WallSegmentation
            wallSegmentation = GetComponent<WallSegmentation>();
            if (wallSegmentation == null)
            {
                  Debug.LogError("WallSegmentationModelLoader: Компонент WallSegmentation не найден на этом объекте.");
                  return;
            }

            // Явная инициализация Sentis через инициализатор (без прямой зависимости)
            if (initializeSentisBeforeLoading)
            {
                  // Ищем SentisInitializer в сцене без прямой ссылки
                  Type sentisInitializerType = null;
                  foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                  {
                        sentisInitializerType = assembly.GetType("SentisInitializer");
                        if (sentisInitializerType != null) break;
                  }

                  if (sentisInitializerType != null)
                  {
                        // Пытаемся получить экземпляр через свойство Instance
                        PropertyInfo instanceProperty = sentisInitializerType.GetProperty("Instance",
                              BindingFlags.Public | BindingFlags.Static);

                        if (instanceProperty != null)
                        {
                              sentisInitializer = instanceProperty.GetValue(null) as MonoBehaviour;

                              if (sentisInitializer != null)
                              {
                                    // Вызываем метод Initialize
                                    MethodInfo initMethod = sentisInitializerType.GetMethod("Initialize");
                                    if (initMethod != null)
                                    {
                                          initMethod.Invoke(sentisInitializer, null);
                                          Debug.Log("WallSegmentationModelLoader: Unity Sentis инициализирован через SentisInitializer");
                                    }
                              }
                        }
                  }
                  else
                  {
                        Debug.LogWarning("WallSegmentationModelLoader: SentisInitializer не найден. Установите SentisInitializer.cs в проект для улучшения стабильности.");
                  }
            }

            // Автоматически загружаем модель при старте
            if (loadOnStart && modelAsset != null)
            {
                  StartCoroutine(LoadModel());
            }
      }

      /// <summary>
      /// Находит или создает инициализатор Sentis через рефлексию
      /// </summary>
      private IEnumerator EnsureSentisInitialized()
      {
            if (triedToInitializeSentis)
            {
                  // Уже пробовали инициализировать
                  yield break;
            }

            triedToInitializeSentis = true;
            Debug.Log("Инициализация Unity Sentis...");

            // Извлекаем логику инициализации в отдельный метод, чтобы избежать yield внутри try-catch
            InitializeSentisSafe();

            // Даем время на завершение инициализации
            yield return new WaitForSeconds(0.5f);

            Debug.Log($"Статус инициализации Sentis: {(isSentisInitialized ? "Успешно" : "Не инициализирован")}");
      }

      /// <summary>
      /// Безопасно инициализирует Sentis без использования yield
      /// </summary>
      private void InitializeSentisSafe()
      {
            try
            {
                  // Находим тип SentisInitializer через рефлексию
                  Type sentisInitializerType = null;
                  foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                  {
                        sentisInitializerType = assembly.GetType("SentisInitializer");
                        if (sentisInitializerType != null) break;
                  }

                  // Если тип найден, ищем экземпляр или создаем новый
                  if (sentisInitializerType != null)
                  {
                        // Сначала пытаемся найти существующий экземпляр через статическое свойство Instance
                        PropertyInfo instanceProperty = sentisInitializerType.GetProperty("Instance",
                              BindingFlags.Public | BindingFlags.Static);

                        if (instanceProperty != null)
                        {
                              object instance = instanceProperty.GetValue(null);
                              if (instance != null)
                              {
                                    sentisInitializer = instance as MonoBehaviour;
                                    Debug.Log("Найден существующий экземпляр SentisInitializer");
                              }
                        }

                        // Если не нашли через свойство Instance, ищем через FindObjectOfType
                        if (sentisInitializer == null)
                        {
                              // Используем рефлексию для вызова FindObjectOfType
                              MethodInfo findObjectMethod = typeof(UnityEngine.Object).GetMethod("FindObjectOfType",
                                    new Type[] { });

                              if (findObjectMethod != null)
                              {
                                    // Создаем обобщенный метод для нужного типа
                                    MethodInfo genericMethod = findObjectMethod.MakeGenericMethod(sentisInitializerType);
                                    object found = genericMethod.Invoke(null, null);

                                    if (found != null)
                                    {
                                          sentisInitializer = found as MonoBehaviour;
                                          Debug.Log("Найден SentisInitializer через FindObjectOfType");
                                    }
                              }
                        }

                        // Если всё еще не нашли, создаем новый
                        if (sentisInitializer == null)
                        {
                              GameObject initializerObj = new GameObject("Sentis Initializer");
                              sentisInitializer = initializerObj.AddComponent(sentisInitializerType) as MonoBehaviour;

                              if (sentisInitializer != null)
                              {
                                    Debug.Log("Создан новый экземпляр SentisInitializer");
                              }
                        }

                        // Проверяем статус инициализации через свойство IsInitialized
                        PropertyInfo isInitializedProperty = sentisInitializerType.GetProperty("IsInitialized",
                              BindingFlags.Public | BindingFlags.Static);

                        if (isInitializedProperty != null)
                        {
                              // Проверяем текущее значение
                              isSentisInitialized = (bool)isInitializedProperty.GetValue(null);

                              // Если не инициализирован, вызываем метод инициализации
                              if (!isSentisInitialized && sentisInitializer != null)
                              {
                                    // Ищем метод Initialize
                                    MethodInfo initMethod = sentisInitializerType.GetMethod("Initialize");
                                    if (initMethod != null)
                                    {
                                          initMethod.Invoke(sentisInitializer, null);
                                          Debug.Log("Вызван метод инициализации SentisInitializer");
                                    }
                              }
                        }
                  }
                  else
                  {
                        Debug.LogWarning("Тип SentisInitializer не найден. Установлен ли SentisInitializer.cs в проекте?");
                  }
            }
            catch (Exception e)
            {
                  Debug.LogError($"Ошибка при инициализации Sentis: {e.Message}");
                  if (e.InnerException != null)
                  {
                        Debug.LogError($"Внутреннее исключение: {e.InnerException.Message}");
                  }
            }
      }

      /// <summary>
      /// Загружает модель асинхронно и безопасно
      /// </summary>
      public IEnumerator LoadModel()
      {
            if (isModelLoading) yield break;
            if (isModelLoaded) yield break;

            if (modelAsset == null)
            {
                  Debug.LogError("WallSegmentationModelLoader: Модель не назначена.");
                  yield break;
            }

            isModelLoading = true;
            lastErrorMessage = null;

            Debug.Log($"WallSegmentationModelLoader: Начинаем загрузку модели {modelAsset.name}");

            // Инициализируем Sentis перед загрузкой, если включена опция
            if (initializeSentisBeforeLoading)
            {
                  yield return StartCoroutine(EnsureSentisInitialized());

                  if (!isSentisInitialized)
                  {
                        Debug.LogWarning("Sentis не удалось инициализировать, но продолжим загрузку модели");
                  }
            }

            // Проверяем тип модели
            string modelExtension = "";
            if (modelAsset.name.Contains("."))
            {
                  modelExtension = modelAsset.name.Substring(modelAsset.name.LastIndexOf(".")).ToLower();
            }

            // Для диагностики выводим информацию о типе модели
            Debug.Log($"WallSegmentationModelLoader: Тип модели {modelAsset.GetType().Name}, расширение: {modelExtension}");

            // Запускаем диагностику Sentis
            RunSentisDiagnostics();

            // Указываем, что начинаем загрузку модели
            yield return null; // Ждем один кадр, чтобы интерфейс мог обновиться

            object model = null;
            bool success = false;
            string errorMessage = "";

            try
            {
                  // Загружаем модель с помощью SentisCompat
                  Type sentisCompatType = Type.GetType("SentisCompat");

                  if (sentisCompatType != null)
                  {
                        // Загружаем через SentisCompat
                        MethodInfo loadModelMethod = sentisCompatType.GetMethod("LoadModel");
                        if (loadModelMethod != null)
                        {
                              Debug.Log($"WallSegmentationModelLoader: Загружаем модель через SentisCompat.LoadModel()");
                              model = loadModelMethod.Invoke(null, new object[] { modelAsset });

                              if (model != null)
                              {
                                    Debug.Log($"WallSegmentationModelLoader: Модель успешно загружена через SentisCompat.LoadModel()");
                                    success = true;
                                    loadedModelInstance = model; // Сохраняем экземпляр модели
                              }
                              else
                              {
                                    errorMessage = "Ошибка загрузки модели через SentisCompat.LoadModel() - вернулся null";
                                    Debug.LogError(errorMessage);
                              }
                        }
                        else
                        {
                              errorMessage = "Метод SentisCompat.LoadModel не найден";
                              Debug.LogError(errorMessage);
                        }
                  }
                  else
                  {
                        // Альтернативный способ, поиск ModelLoader напрямую
                        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                        {
                              if (assembly.GetName().Name == "Unity.Sentis")
                              {
                                    Type modelLoaderType = assembly.GetType("Unity.Sentis.ModelLoader");

                                    if (modelLoaderType != null)
                                    {
                                          // Пробуем загрузить модель через ModelLoader.Load
                                          Debug.Log($"WallSegmentationModelLoader: Загружаем модель через Unity.Sentis.ModelLoader.Load()");

                                          var loadMethod = modelLoaderType.GetMethod("Load", new Type[] { modelAsset.GetType() });
                                          if (loadMethod != null)
                                          {
                                                model = loadMethod.Invoke(null, new object[] { modelAsset });

                                                if (model != null)
                                                {
                                                      Debug.Log($"WallSegmentationModelLoader: Модель успешно загружена через Unity.Sentis.ModelLoader.Load()");
                                                      success = true;
                                                      loadedModelInstance = model; // Сохраняем экземпляр модели
                                                }
                                                else
                                                {
                                                      errorMessage = "Ошибка загрузки модели через Unity.Sentis.ModelLoader.Load() - вернулся null";
                                                      Debug.LogError(errorMessage);
                                                }
                                          }
                                          else
                                          {
                                                errorMessage = "Метод Unity.Sentis.ModelLoader.Load не найден для типа " + modelAsset.GetType().Name;
                                                Debug.LogError(errorMessage);
                                          }
                                    }
                                    else
                                    {
                                          errorMessage = "Тип Unity.Sentis.ModelLoader не найден";
                                          Debug.LogError(errorMessage);
                                    }

                                    break;
                              }
                        }
                  }

                  // Проверяем результат
                  if (success && model != null)
                  {
                        // Если у нас активирован компонент WallSegmentation, обновляем его
                        if (wallSegmentation != null)
                        {
                              // Важное изменение: НЕ устанавливаем напрямую modelAsset, а вместо этого используем
                              // поле model в компоненте WallSegmentation, если оно существует
                              Debug.Log("WallSegmentationModelLoader: Устанавливаем загруженную модель в WallSegmentation");

                              // Ищем поле 'model' в WallSegmentation (не modelAsset)
                              var modelField = wallSegmentation.GetType().GetField("model",
                                    BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                              if (modelField != null)
                              {
                                    // Устанавливаем загруженную модель напрямую в поле 'model'
                                    modelField.SetValue(wallSegmentation, model);
                                    Debug.Log("WallSegmentationModelLoader: Модель успешно установлена в поле 'model'");
                              }
                              else
                              {
                                    // Если поле 'model' не найдено, используем альтернативные методы
                                    Debug.LogWarning("WallSegmentationModelLoader: Поле 'model' не найдено, пробуем альтернативные подходы");

                                    // Пробуем найти свойство для установки модели
                                    var modelProperty = wallSegmentation.GetType().GetProperty("Model",
                                          BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                                    if (modelProperty != null && modelProperty.CanWrite)
                                    {
                                          modelProperty.SetValue(wallSegmentation, model);
                                          Debug.Log("WallSegmentationModelLoader: Модель успешно установлена через свойство 'Model'");
                                    }
                                    else
                                    {
                                          // Если нет прямого способа установить модель, ищем метод для ее назначения
                                          var setModelMethod = wallSegmentation.GetType().GetMethod("SetModel",
                                                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

                                          if (setModelMethod != null)
                                          {
                                                setModelMethod.Invoke(wallSegmentation, new[] { model });
                                                Debug.Log("WallSegmentationModelLoader: Модель успешно установлена через метод 'SetModel'");
                                          }
                                          else
                                          {
                                                Debug.LogWarning("WallSegmentationModelLoader: Не найден способ установить модель. WallSegmentation должен иметь поле 'model', свойство 'Model' или метод 'SetModel'");
                                          }
                                    }
                              }

                              // Обновляем бэкенд
                              SetPrivateField(wallSegmentation, "preferredBackend", preferredBackend);

                              // Вызываем метод инициализации модели, если такой есть
                              MethodInfo initMethod = wallSegmentation.GetType().GetMethod("InitializeModel",
                                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);

                              if (initMethod != null)
                              {
                                    Debug.Log("WallSegmentationModelLoader: Вызываем метод InitializeModel");
                                    initMethod.Invoke(wallSegmentation, null);
                              }
                              else
                              {
                                    Debug.Log("WallSegmentationModelLoader: Метод InitializeModel не найден, устанавливаем только модель");
                              }

                              // Вызываем отложенное создание Worker, если есть такой метод
                              MethodInfo createWorkerMethod = wallSegmentation.GetType().GetMethod("CreateWorkerDelayed",
                                    BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);

                              if (createWorkerMethod != null)
                              {
                                    Debug.Log("WallSegmentationModelLoader: Запускаем отложенное создание Worker");
                                    createWorkerMethod.Invoke(wallSegmentation, null);
                              }
                        }

                        isModelLoaded = true;
                        OnModelLoaded(true, model, null);
                  }
                  else
                  {
                        if (string.IsNullOrEmpty(errorMessage))
                        {
                              errorMessage = "Не удалось загрузить модель по неизвестной причине";
                        }

                        OnModelLoaded(false, null, errorMessage);
                  }
            }
            catch (Exception e)
            {
                  string exceptionMessage = e.Message;
                  if (e.InnerException != null)
                  {
                        exceptionMessage += " | Inner: " + e.InnerException.Message;
                  }

                  Debug.LogError($"WallSegmentationModelLoader: Исключение при загрузке модели: {exceptionMessage}");

                  // Показываем подробное сообщение об ошибке в диалоге
                  errorMessage = $"Ошибка при загрузке модели: {exceptionMessage}";
                  OnModelLoaded(false, null, errorMessage);
            }
            finally
            {
                  isModelLoading = false;
            }
      }

      /// <summary>
      /// Запускает диагностику Sentis через SafeModelLoader
      /// </summary>
      private void RunSentisDiagnostics()
      {
            try
            {
                  // Пытаемся запустить диагностику через sentisInitializer если он есть
                  if (sentisInitializer != null)
                  {
                        // Пытаемся найти метод TestSentisModel через рефлексию
                        MethodInfo testMethod = sentisInitializer.GetType().GetMethod("TestSentisModel");
                        if (testMethod != null)
                        {
                              // Метод возвращает IEnumerator, запускаем его через StartCoroutine
                              var testCoroutine = testMethod.Invoke(sentisInitializer, null) as IEnumerator;
                              if (testCoroutine != null)
                              {
                                    StartCoroutine(testCoroutine);
                                    Debug.Log("Запущена диагностика через SentisInitializer.TestSentisModel");
                                    return;
                              }
                        }
                  }

                  // Если не удалось запустить через SentisInitializer, используем SafeModelLoader
                  // Используем рефлексию для вызова метода DiagnoseSentisStatus
                  Type safeModelLoaderType = Type.GetType("SafeModelLoader");
                  if (safeModelLoaderType != null)
                  {
                        MethodInfo diagnosticsMethod = safeModelLoaderType.GetMethod("DiagnoseSentisStatus",
                              BindingFlags.Public | BindingFlags.Static);

                        if (diagnosticsMethod != null)
                        {
                              diagnosticsMethod.Invoke(null, null);
                              Debug.Log("Запущена диагностика через SafeModelLoader.DiagnoseSentisStatus");
                        }
                        else
                        {
                              Debug.LogWarning("Не удалось найти метод диагностики Sentis");
                        }
                  }
                  else
                  {
                        Debug.LogWarning("Тип SafeModelLoader не найден. Диагностика невозможна.");
                  }
            }
            catch (Exception e)
            {
                  Debug.LogError($"Ошибка при запуске диагностики Sentis: {e.Message}");
            }
      }

      /// <summary>
      /// Устанавливает значение приватного поля объекта через рефлексию
      /// </summary>
      private void SetPrivateField(object target, string fieldName, object value)
      {
            var field = target.GetType().GetField(fieldName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

            if (field != null)
            {
                  field.SetValue(target, value);
            }
            else
            {
                  Debug.LogError($"Поле '{fieldName}' не найдено в типе {target.GetType().Name}");
            }
      }

      /// <summary>
      /// Вызывается когда модель загружена или произошла ошибка
      /// </summary>
      /// <param name="success">Успешно ли загружена модель</param>
      /// <param name="model">Загруженная модель или null</param>
      /// <param name="errorMessage">Сообщение об ошибке или null</param>
      private void OnModelLoaded(bool success, object model, string errorMessage)
      {
            isModelLoaded = success;

            if (success)
            {
                  Debug.Log("WallSegmentationModelLoader: Модель успешно загружена!");
            }
            else
            {
                  lastErrorMessage = errorMessage;
                  Debug.LogError($"WallSegmentationModelLoader: Ошибка при загрузке модели: {errorMessage}");

                  // Если есть сообщение об ошибке, показываем его в диалоге
                  if (!string.IsNullOrEmpty(errorMessage))
                  {
                        ShowErrorInDialog(errorMessage);
                  }
            }
      }

      /// <summary>
      /// Показывает сообщение об ошибке в диалоговом окне
      /// </summary>
      private void ShowErrorInDialog(string errorMessage)
      {
            // Проверяем есть ли DialogManager
            var dialogManager = FindObjectOfType<DialogManager>();

            if (dialogManager != null)
            {
                  // Получаем информацию о модели
                  string modelName = modelAsset != null ? modelAsset.name : "неизвестная модель";

                  // Вызываем метод показа ошибки
                  dialogManager.ShowModelLoadError(modelName, errorMessage);
            }
            else
            {
                  // Проверяем есть ли класс DialogInitializer (для обратной совместимости)
                  var dialogInitializer = FindObjectOfType<DialogInitializer>();

                  if (dialogInitializer != null)
                  {
                        // Создаем объект с информацией об ошибке, используя полное имя класса
                        var errorInfo = new ModelErrorInfo(
                            modelAsset != null ? modelAsset.name : "неизвестная модель",
                            modelAsset != null ? modelAsset.GetType().Name : "неизвестный тип",
                            errorMessage,
                            "Убедитесь, что модель совместима с Unity Sentis"
                        );

                        // Вызываем метод показа ошибки через рефлексию для обеспечения обратной совместимости
                        MethodInfo showErrorMethod = typeof(DialogInitializer).GetMethod("ShowModelLoadError",
                              BindingFlags.Public | BindingFlags.Static);

                        if (showErrorMethod != null)
                        {
                              showErrorMethod.Invoke(null, new object[] { errorInfo });
                        }
                  }
                  else
                  {
                        Debug.LogWarning("DialogManager или DialogInitializer не найден в сцене. Сообщение об ошибке не будет показано.");

                        // Запасной вариант - показать ошибку в консоли в формате, который легко заметить
                        Debug.LogError("==========================================");
                        Debug.LogError($"ОШИБКА ЗАГРУЗКИ МОДЕЛИ: {errorMessage}");
                        Debug.LogError("==========================================");
                  }
            }
      }

      /// <summary>
      /// Публичный метод для получения загруженного экземпляра модели
      /// </summary>
      public object GetLoadedModel()
      {
            return loadedModelInstance;
      }

      /// <summary>
      /// Публичный метод для проверки состояния загрузки
      /// </summary>
      public bool IsModelLoaded => isModelLoaded;

      /// <summary>
      /// Публичный метод для получения последней ошибки
      /// </summary>
      public string LastErrorMessage => lastErrorMessage;

      /// <summary>
      /// Публичный метод для принудительной загрузки модели
      /// </summary>
      public void LoadModelNow()
      {
            if (!isModelLoading && !isModelLoaded && modelAsset != null)
            {
                  StartCoroutine(LoadModel());
            }
      }
}