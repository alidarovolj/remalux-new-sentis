using UnityEngine;
using System.Collections;
using System;
using System.Linq;
using System.Reflection;
using UnityEngine.UI; // Для доступа к компонентам UI
using UnityEngine.Networking;
using System.IO;
using Unity.Sentis;

/// <summary>
/// Компонент для безопасной загрузки моделей WallSegmentation.
/// Предотвращает краш Unity, который может произойти при прямом назначении модели.
/// </summary>
[RequireComponent(typeof(WallSegmentation))]
public class WallSegmentationModelLoader : MonoBehaviour
{
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
      private object loadedModelInstance = null;

      private MonoBehaviour sentisInitializer;
      private bool triedToInitializeSentis = false;
      private bool isSentisInitialized = false;

      // Создаем делегаты для событий загрузки модели
      public delegate void ModelLoadedHandler(object model);
      public delegate void ModelLoadErrorHandler(string errorMessage);

      // События для оповещения о загрузке модели
      public event ModelLoadedHandler OnModelLoaded;
      public event ModelLoadErrorHandler OnModelLoadError;

      private void Start()
      {
            wallSegmentation = GetComponent<WallSegmentation>();
            if (wallSegmentation == null)
            {
                  Debug.LogError("WallSegmentationModelLoader: Не найден компонент WallSegmentation!");
                  return;
            }

            // Инициализируем Sentis перед загрузкой, если включена опция
            if (initializeSentisBeforeLoading)
            {
                  StartCoroutine(EnsureSentisInitialized());
            }

            // Автоматически загружаем модель при старте
            if (loadOnStart)
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
                  // Проверяем значение флага и используем его
                  if (isSentisInitialized)
                  {
                        Debug.Log("Unity Sentis уже инициализирован, пропускаем инициализацию");
                  }
                  else
                  {
                        Debug.LogWarning("Попытка инициализации Unity Sentis уже была выполнена, но закончилась неудачей");
                  }
                  yield break;
            }

            triedToInitializeSentis = true;
            Debug.Log("Инициализация Unity Sentis...");

            // Ищем SentisInitializer через рефлексию
            Type sentisInitializerType = Type.GetType("SentisInitializer");
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
                                    isSentisInitialized = true;
                              }
                        }
                  }
            }
            else
            {
                  Debug.LogWarning("WallSegmentationModelLoader: SentisInitializer не найден. Установите SentisInitializer.cs в проект для улучшения стабильности.");
            }

            // Проверяем успешность инициализации через рефлексию
            if (!isSentisInitialized)
            {
                  // Пробуем проверить статический IsInitialized через рефлексию
                  try
                  {
                        if (sentisInitializerType != null)
                        {
                              PropertyInfo isInitializedProperty = sentisInitializerType.GetProperty("IsInitialized",
                                    BindingFlags.Public | BindingFlags.Static);

                              if (isInitializedProperty != null)
                              {
                                    bool isInitialized = (bool)isInitializedProperty.GetValue(null);
                                    if (isInitialized)
                                    {
                                          Debug.Log("Sentis инициализирован через SentisInitializer.IsInitialized");
                                          isSentisInitialized = true;
                                    }
                              }
                        }
                  }
                  catch (Exception e)
                  {
                        Debug.LogWarning($"Ошибка при проверке SentisInitializer.IsInitialized: {e.Message}");
                  }
            }

            // Если всё ещё не инициализирован, пробуем другие методы
            if (!isSentisInitialized)
            {
                  Debug.Log("Пробуем альтернативные способы инициализации Sentis...");

                  // Проверяем наличие Unity.Sentis.Model через рефлексию
                  var sentisModelType = Type.GetType("Unity.Sentis.Model, Unity.Sentis");
                  if (sentisModelType != null)
                  {
                        Debug.Log("Тип Unity.Sentis.Model найден, считаем Sentis инициализированным");
                        isSentisInitialized = true;
                  }
            }

            yield return null;
      }

      /// <summary>
      /// Загружает модель асинхронно и безопасно
      /// </summary>
      public IEnumerator LoadModel()
      {
            Debug.Log("WallSegmentationModelLoader: Начинаем загрузку модели из StreamingAssets");

            // Проверяем инициализацию Sentis
            if (initializeSentisBeforeLoading && !isSentisInitialized && !triedToInitializeSentis)
            {
                  Debug.Log("Инициализируем Sentis перед загрузкой модели...");
                  var initCoroutine = EnsureSentisInitialized();
                  yield return initCoroutine;
            }

            // Попробуем загрузить сначала .sentis файл, затем .onnx
            string[] fileExtensionsToTry = new string[] { ".sentis", ".onnx" };
            bool modelLoaded = false;

            foreach (string extension in fileExtensionsToTry)
            {
                  string fileName = "model" + extension;
                  string modelPath = Path.Combine(Application.streamingAssetsPath, fileName);
                  string modelUrl = PathToUrl(modelPath);

                  Debug.Log($"Загружаем модель из: {modelUrl}");

                  // Проверяем существование файла локально - это работает только для некоторых платформ
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
                        Model model = null;

                        // Выбираем метод загрузки в зависимости от расширения
                        if (extension == ".sentis")
                        {
                              // Пробуем напрямую через ModelLoader для .sentis
                              using (var ms = new MemoryStream(modelData))
                              {
                                    model = ModelLoader.Load(ms);
                              }
                        }
                        else // .onnx
                        {
                              // Пробуем разные методы для загрузки ONNX
                              using (var ms = new MemoryStream(modelData))
                              {
                                    try
                                    {
                                          model = ModelLoader.Load(ms);
                                          if (model != null)
                                          {
                                                Debug.Log("ONNX модель успешно загружена через ModelLoader.Load(Stream)");
                                          }
                                    }
                                    catch (Exception e)
                                    {
                                          Debug.LogWarning($"Не удалось загрузить ONNX через ModelLoader.Load(Stream): {e.Message}");

                                          // Пробуем сначала сохранить во временный файл
                                          string tempFilePath = Path.Combine(Application.temporaryCachePath, "temp_model.onnx");
                                          try
                                          {
                                                File.WriteAllBytes(tempFilePath, modelData);
                                                model = ModelLoader.Load(tempFilePath);
                                                if (model != null)
                                                {
                                                      Debug.Log("ONNX модель успешно загружена через временный файл");
                                                }
                                          }
                                          catch (Exception e2)
                                          {
                                                Debug.LogWarning($"Не удалось загрузить ONNX через временный файл: {e2.Message}");
                                          }
                                          finally
                                          {
                                                // Удаляем временный файл
                                                if (File.Exists(tempFilePath))
                                                {
                                                      File.Delete(tempFilePath);
                                                }
                                          }
                                    }
                              }
                        }

                        if (model != null)
                        {
                              loadedModelInstance = model;
                              Debug.Log($"Модель успешно загружена через ModelLoader в формате {extension}");
                              isModelLoaded = true;
                              modelLoaded = true;
                              break; // Выходим из цикла, модель загружена
                        }
                        else
                        {
                              Debug.LogError($"Ошибка загрузки модели через ModelLoader - вернулся null для {extension}");
                        }
                  }
                  catch (Exception e)
                  {
                        Debug.LogError($"Исключение при загрузке модели {extension}: {e.Message}");
                  }
            }

            if (!modelLoaded)
            {
                  Debug.LogError("WallSegmentationModelLoader: Не удалось загрузить модель ни в одном формате");

                  // Создаем экземпляр ModelSerializer для преобразования модели
                  var serializer = gameObject.AddComponent<ModelSerializer>();
                  serializer.onnxModelPath = Path.Combine(Application.streamingAssetsPath, "model.onnx");
                  serializer.outputPath = Path.Combine(Application.streamingAssetsPath, "model.sentis");
                  serializer.SerializeModel();

                  yield return new WaitForSeconds(1); // Ждем немного для завершения сериализации

                  // Пробуем загрузить заново
                  string sentisPath = Path.Combine(Application.streamingAssetsPath, "model.sentis");
                  if (File.Exists(sentisPath))
                  {
                        try
                        {
                              Debug.Log("Пробуем загрузить свежесозданную Sentis модель...");
                              var model = ModelLoader.Load(sentisPath);
                              if (model != null)
                              {
                                    loadedModelInstance = model;
                                    Debug.Log("Sentis модель успешно создана и загружена!");
                                    isModelLoaded = true;
                              }
                        }
                        catch (Exception e)
                        {
                              Debug.LogError($"Не удалось загрузить свежесозданную Sentis модель: {e.Message}");
                        }
                  }
            }

            // Финализируем операцию загрузки
            if (isModelLoaded)
            {
                  Debug.Log("WallSegmentationModelLoader: Модель успешно загружена и готова к использованию");
                  if (OnModelLoaded != null) OnModelLoaded.Invoke(loadedModelInstance);
            }
            else
            {
                  string error = "Ошибка загрузки модели: не удалось загрузить ни в одном формате";
                  Debug.LogError($"WallSegmentationModelLoader: {error}");
                  if (OnModelLoadError != null) OnModelLoadError.Invoke(error);
            }
      }

      // Вспомогательная функция для преобразования пути в URL
      private string PathToUrl(string path)
      {
            if (path.StartsWith("http://") || path.StartsWith("https://") || path.StartsWith("file://"))
            {
                  return path;
            }

            // Для пути, начинающегося с буквы диска на Windows (например, C:/)
            if (path.Length > 2 && path[1] == ':' && path[2] == '/')
            {
                  return "file:///" + path.Replace('\\', '/');
            }

            // Для Unix-подобных путей (/path/to/file)
            if (path.StartsWith("/"))
            {
                  return "file://" + path;
            }

            // Для относительных путей (встречается редко в этом контексте)
            return "file://" + Path.GetFullPath(path).Replace('\\', '/');
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
      private void HandleModelLoaded(bool success, object model, string errorMessage)
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
                  string modelName = loadedModelInstance != null ? loadedModelInstance.GetType().Name : "неизвестная модель";

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
                            loadedModelInstance != null ? loadedModelInstance.GetType().Name : "неизвестная модель",
                            loadedModelInstance != null ? loadedModelInstance.GetType().Name : "неизвестный тип",
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
            if (!isModelLoading && !isModelLoaded)
            {
                  StartCoroutine(LoadModel());
            }
      }
}