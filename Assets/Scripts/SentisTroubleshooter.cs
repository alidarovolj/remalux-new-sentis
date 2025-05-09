using UnityEngine;
using System.Collections;
using System.Reflection;
using System;
using System.Linq;
using System.Collections.Generic;

/// <summary>
/// Утилита для устранения неполадок с Unity Sentis
/// </summary>
public class SentisTroubleshooter : MonoBehaviour
{
      [Header("Настройки диагностики")]
      [Tooltip("Выполнить полную диагностику при запуске")]
      public bool runDiagnosticOnStart = true;

      [Tooltip("Автоматически исправлять найденные проблемы")]
      public bool autoFixIssues = true;

      [Tooltip("Время ожидания между тестами (сек)")]
      public float testDelay = 0.5f;

      [Header("Тестовый ассет модели")]
      [Tooltip("Небольшая ONNX модель для тестирования Sentis")]
      public UnityEngine.Object testModel;

      [Header("Статус")]
      [SerializeField] private bool sentisInstalled = false;
      [SerializeField] private string sentisVersion = "Не определена";
      [SerializeField] private bool sentisInitialized = false;
      [SerializeField] private bool modelLoadSucceeded = false;
      [SerializeField] private bool workerCreationSucceeded = false;
      [SerializeField] private List<string> foundIssues = new List<string>();
      [SerializeField] private List<string> appliedFixes = new List<string>();

      private void Start()
      {
            if (runDiagnosticOnStart)
            {
                  StartCoroutine(RunFullDiagnostic());
            }
      }

      /// <summary>
      /// Запускает полную диагностику Sentis
      /// </summary>
      public IEnumerator RunFullDiagnostic()
      {
            Debug.Log("SentisTroubleshooter: Запуск полной диагностики Unity Sentis...");

            foundIssues.Clear();
            appliedFixes.Clear();

            // Шаг 1: Проверка наличия Sentis
            CheckSentisInstalled();
            yield return new WaitForSeconds(testDelay);

            // Если Sentis не установлен, выходим
            if (!sentisInstalled)
            {
                  Debug.LogError("SentisTroubleshooter: Unity Sentis не установлен. Необходимо установить пакет в Package Manager.");
                  foundIssues.Add("Unity Sentis не установлен");
                  yield break;
            }

            // Шаг 2: Проверка инициализации Sentis
            CheckSentisInitialized();

            // Если Sentis не инициализирован, пробуем исправить
            if (!sentisInitialized && autoFixIssues)
            {
                  Debug.Log("SentisTroubleshooter: Attempt to initialize Sentis");
                  yield return StartCoroutine(InitializeSentis());
            }

            yield return new WaitForSeconds(testDelay);

            // Шаг 3: Проверка загрузки моделей
            yield return StartCoroutine(TestModelLoading());

            yield return new WaitForSeconds(testDelay);

            // Шаг 4: Проверка создания Worker
            if (modelLoadSucceeded)
            {
                  yield return StartCoroutine(TestWorkerCreation());
            }

            // Выводим итоговый отчет
            Debug.Log("==== Отчет о диагностике Unity Sentis ====");
            Debug.Log($"Версия Sentis: {sentisVersion}");
            Debug.Log($"Sentis установлен: {sentisInstalled}");
            Debug.Log($"Sentis инициализирован: {sentisInitialized}");
            Debug.Log($"Загрузка модели: {(modelLoadSucceeded ? "Успешно" : "Ошибка")}");
            Debug.Log($"Создание Worker: {(workerCreationSucceeded ? "Успешно" : "Ошибка")}");

            if (foundIssues.Count > 0)
            {
                  Debug.Log("Найденные проблемы:");
                  foreach (var issue in foundIssues)
                  {
                        Debug.Log($"- {issue}");
                  }
            }
            else
            {
                  Debug.Log("Проблем не обнаружено, Unity Sentis работает корректно!");
            }

            if (appliedFixes.Count > 0)
            {
                  Debug.Log("Примененные исправления:");
                  foreach (var fix in appliedFixes)
                  {
                        Debug.Log($"- {fix}");
                  }
            }

            Debug.Log("========================================");
      }

      /// <summary>
      /// Проверяет наличие установленного Sentis
      /// </summary>
      private void CheckSentisInstalled()
      {
            Debug.Log("SentisTroubleshooter: Проверка наличия Unity Sentis...");

            // Ищем сборку Unity.Sentis
            Assembly sentisAssembly = null;
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                  if (assembly.GetName().Name == "Unity.Sentis")
                  {
                        sentisAssembly = assembly;
                        break;
                  }
            }

            sentisInstalled = sentisAssembly != null;

            if (sentisInstalled)
            {
                  sentisVersion = sentisAssembly.GetName().Version.ToString();
                  Debug.Log($"SentisTroubleshooter: Unity Sentis установлен, версия {sentisVersion}");

                  // Проверяем наличие критически важных типов
                  bool allTypesFound = true;

                  Type modelLoaderType = sentisAssembly.GetType("Unity.Sentis.ModelLoader");
                  Type modelType = sentisAssembly.GetType("Unity.Sentis.Model");
                  Type workerType = sentisAssembly.GetType("Unity.Sentis.Worker");
                  Type workerFactoryType = sentisAssembly.GetType("Unity.Sentis.WorkerFactory");

                  if (modelLoaderType == null)
                  {
                        foundIssues.Add("Тип Unity.Sentis.ModelLoader не найден");
                        allTypesFound = false;
                  }

                  if (modelType == null)
                  {
                        foundIssues.Add("Тип Unity.Sentis.Model не найден");
                        allTypesFound = false;
                  }

                  if (workerType == null && workerFactoryType == null)
                  {
                        foundIssues.Add("Не найдены типы Unity.Sentis.Worker и Unity.Sentis.WorkerFactory");
                        allTypesFound = false;
                  }

                  if (!allTypesFound)
                  {
                        Debug.LogWarning("SentisTroubleshooter: Некоторые важные типы Sentis не найдены");
                  }
            }
            else
            {
                  Debug.LogError("SentisTroubleshooter: Unity Sentis не установлен");
                  foundIssues.Add("Unity Sentis не установлен");
            }
      }

      /// <summary>
      /// Проверяет, инициализирован ли Sentis
      /// </summary>
      private void CheckSentisInitialized()
      {
            Debug.Log("SentisTroubleshooter: Проверка инициализации Unity Sentis...");

            // Проверяем наличие SentisInitializer
            var sentisInitializer = FindObjectOfType<MonoBehaviour>();
            Type sentisInitializerType = null;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                  sentisInitializerType = assembly.GetType("SentisInitializer");
                  if (sentisInitializerType != null) break;
            }

            if (sentisInitializerType != null)
            {
                  var initializerComponent = FindObjectOfType(sentisInitializerType) as MonoBehaviour;

                  if (initializerComponent != null)
                  {
                        // Проверяем свойство IsInitialized
                        PropertyInfo isInitializedProperty = sentisInitializerType.GetProperty("IsInitialized",
                            BindingFlags.Public | BindingFlags.Static);

                        if (isInitializedProperty != null)
                        {
                              sentisInitialized = (bool)isInitializedProperty.GetValue(null);

                              if (sentisInitialized)
                              {
                                    Debug.Log("SentisTroubleshooter: Unity Sentis инициализирован через SentisInitializer");
                              }
                              else
                              {
                                    Debug.LogWarning("SentisTroubleshooter: SentisInitializer есть, но Sentis не инициализирован");
                                    foundIssues.Add("SentisInitializer существует, но Sentis не инициализирован");
                              }
                        }
                        else
                        {
                              Debug.LogWarning("SentisTroubleshooter: Не найдено свойство IsInitialized в SentisInitializer");
                              foundIssues.Add("Некорректный SentisInitializer (нет свойства IsInitialized)");
                        }
                  }
                  else
                  {
                        Debug.LogWarning("SentisTroubleshooter: SentisInitializer не найден в сцене");
                        foundIssues.Add("SentisInitializer не найден в сцене");
                  }
            }
            else
            {
                  Debug.LogWarning("SentisTroubleshooter: Тип SentisInitializer не найден");
                  foundIssues.Add("Тип SentisInitializer не найден");
            }
      }

      /// <summary>
      /// Инициализирует Sentis
      /// </summary>
      private IEnumerator InitializeSentis()
      {
            Debug.Log("SentisTroubleshooter: Инициализация Unity Sentis...");

            // Проверяем наличие SentisInitializer
            Type sentisInitializerType = null;

            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                  sentisInitializerType = assembly.GetType("SentisInitializer");
                  if (sentisInitializerType != null) break;
            }

            if (sentisInitializerType != null)
            {
                  var initializerComponent = FindObjectOfType(sentisInitializerType) as MonoBehaviour;

                  if (initializerComponent == null)
                  {
                        // Создаем SentisInitializer
                        GameObject obj = new GameObject("Sentis Initializer");
                        initializerComponent = obj.AddComponent(sentisInitializerType) as MonoBehaviour;

                        if (initializerComponent != null)
                        {
                              Debug.Log("SentisTroubleshooter: Создан новый SentisInitializer");
                              appliedFixes.Add("Создан новый SentisInitializer");
                        }
                  }

                  if (initializerComponent != null)
                  {
                        // Вызываем метод Initialize
                        MethodInfo initMethod = sentisInitializerType.GetMethod("Initialize");
                        if (initMethod != null)
                        {
                              initMethod.Invoke(initializerComponent, null);
                              Debug.Log("SentisTroubleshooter: Вызван метод Initialize у SentisInitializer");
                              appliedFixes.Add("Вызван метод Initialize у SentisInitializer");
                        }

                        // Ждем секунду для полной инициализации
                        yield return new WaitForSeconds(1f);

                        // Проверяем результат инициализации
                        PropertyInfo isInitializedProperty = sentisInitializerType.GetProperty("IsInitialized",
                            BindingFlags.Public | BindingFlags.Static);

                        if (isInitializedProperty != null)
                        {
                              sentisInitialized = (bool)isInitializedProperty.GetValue(null);

                              if (sentisInitialized)
                              {
                                    Debug.Log("SentisTroubleshooter: Unity Sentis успешно инициализирован");
                              }
                              else
                              {
                                    Debug.LogWarning("SentisTroubleshooter: Не удалось инициализировать Unity Sentis");
                                    foundIssues.Add("Не удалось инициализировать Unity Sentis");
                              }
                        }
                  }
            }
            else
            {
                  // Нет SentisInitializer, создаем заглушку
                  Debug.LogWarning("SentisTroubleshooter: Тип SentisInitializer не найден, используется встроенная инициализация");
                  yield return StartCoroutine(InitializeSentisManually());
            }
      }

      /// <summary>
      /// Выполняет ручную инициализацию Sentis без SentisInitializer
      /// </summary>
      private IEnumerator InitializeSentisManually()
      {
            Debug.Log("SentisTroubleshooter: Выполняем ручную инициализацию Unity Sentis...");

            try
            {
                  // Получаем сборку Sentis
                  Assembly sentisAssembly = null;
                  foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                  {
                        if (assembly.GetName().Name == "Unity.Sentis")
                        {
                              sentisAssembly = assembly;
                              break;
                        }
                  }

                  if (sentisAssembly != null)
                  {
                        // Загружаем основные типы для инициализации
                        Type modelLoaderType = sentisAssembly.GetType("Unity.Sentis.ModelLoader");
                        Type modelType = sentisAssembly.GetType("Unity.Sentis.Model");
                        Type workerType = sentisAssembly.GetType("Unity.Sentis.Worker");
                        Type workerFactoryType = sentisAssembly.GetType("Unity.Sentis.WorkerFactory");
                        Type backendType = sentisAssembly.GetType("Unity.Sentis.BackendType");

                        if (backendType != null)
                        {
                              // Перечисляем значения enum BackendType, чтобы инициализировать их
                              var values = Enum.GetValues(backendType);
                              Debug.Log($"SentisTroubleshooter: Загружено {values.Length} значений BackendType");
                        }

                        // Ждем немного для завершения инициализации - вынесем за пределы try
                        // yield return new WaitForSeconds(0.5f);

                        // Проверяем доступность критически важных типов
                        bool success = (modelLoaderType != null && modelType != null &&
                                       (workerType != null || workerFactoryType != null));

                        sentisInitialized = success;

                        if (success)
                        {
                              Debug.Log("SentisTroubleshooter: Unity Sentis ручная инициализация успешна");
                              appliedFixes.Add("Выполнена ручная инициализация Unity Sentis");
                        }
                        else
                        {
                              Debug.LogWarning("SentisTroubleshooter: Ручная инициализация Unity Sentis не удалась");
                              foundIssues.Add("Ручная инициализация Unity Sentis не удалась");
                        }
                  }
            }
            catch (Exception e)
            {
                  Debug.LogError($"SentisTroubleshooter: Ошибка при ручной инициализации Sentis: {e.Message}");
                  foundIssues.Add($"Ошибка при ручной инициализации: {e.Message}");
            }

            // Перемещаем yield за пределы try-catch
            yield return new WaitForSeconds(0.5f);
      }

      /// <summary>
      /// Тестирует загрузку модели
      /// </summary>
      private IEnumerator TestModelLoading()
      {
            Debug.Log("SentisTroubleshooter: Тестирование загрузки модели...");

            if (testModel == null)
            {
                  Debug.LogWarning("SentisTroubleshooter: Тестовая модель не задана, поиск модели...");

                  // Пытаемся найти модель ONNX в проекте
                  var foundModels = Resources.FindObjectsOfTypeAll<UnityEngine.Object>()
                      .Where(obj => obj.name.EndsWith(".onnx"))
                      .ToArray();

                  if (foundModels.Length > 0)
                  {
                        testModel = foundModels[0];
                        Debug.Log($"SentisTroubleshooter: Найдена модель {testModel.name}");
                  }
                  else
                  {
                        Debug.LogError("SentisTroubleshooter: Не найдено ни одной модели ONNX");
                        foundIssues.Add("Не найдено ни одной модели ONNX в проекте");
                        modelLoadSucceeded = false;
                        yield break;
                  }
            }

            // Пробуем загрузить модель
            object loadedModel = null;

            try
            {
                  // Проверяем наличие SentisCompat
                  Type sentisCompatType = null;
                  foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                  {
                        sentisCompatType = assembly.GetType("SentisCompat");
                        if (sentisCompatType != null) break;
                  }

                  if (sentisCompatType != null)
                  {
                        // Загружаем через SentisCompat
                        MethodInfo loadModelMethod = sentisCompatType.GetMethod("LoadModel");
                        if (loadModelMethod != null)
                        {
                              Debug.Log("SentisTroubleshooter: Загрузка модели через SentisCompat.LoadModel()");
                              loadedModel = loadModelMethod.Invoke(null, new object[] { testModel });
                        }
                  }
                  else
                  {
                        // Загружаем напрямую
                        Assembly sentisAssembly = null;
                        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                        {
                              if (assembly.GetName().Name == "Unity.Sentis")
                              {
                                    sentisAssembly = assembly;
                                    break;
                              }
                        }

                        if (sentisAssembly != null)
                        {
                              Type modelLoaderType = sentisAssembly.GetType("Unity.Sentis.ModelLoader");
                              if (modelLoaderType != null)
                              {
                                    var loadMethod = modelLoaderType.GetMethod("Load", new[] { testModel.GetType() });
                                    if (loadMethod != null)
                                    {
                                          Debug.Log("SentisTroubleshooter: Загрузка модели через Unity.Sentis.ModelLoader.Load()");
                                          loadedModel = loadMethod.Invoke(null, new object[] { testModel });
                                    }
                              }
                        }
                  }

                  modelLoadSucceeded = (loadedModel != null);

                  if (modelLoadSucceeded)
                  {
                        Debug.Log($"SentisTroubleshooter: Модель успешно загружена, тип: {loadedModel.GetType().Name}");
                  }
                  else
                  {
                        Debug.LogError("SentisTroubleshooter: Не удалось загрузить модель");
                        foundIssues.Add("Не удалось загрузить модель");
                  }
            }
            catch (Exception e)
            {
                  modelLoadSucceeded = false;
                  Debug.LogError($"SentisTroubleshooter: Ошибка при загрузке модели: {e.Message}");
                  foundIssues.Add($"Ошибка при загрузке модели: {e.Message}");

                  if (e.InnerException != null)
                  {
                        Debug.LogError($"SentisTroubleshooter: Внутреннее исключение: {e.InnerException.Message}");
                  }
            }
      }

      /// <summary>
      /// Тестирует создание Worker
      /// </summary>
      private IEnumerator TestWorkerCreation()
      {
            if (!modelLoadSucceeded)
            {
                  Debug.LogError("SentisTroubleshooter: Невозможно протестировать создание Worker, модель не загружена");
                  yield break;
            }

            Debug.Log("SentisTroubleshooter: Тестирование создания Worker...");

            // Загружаем модель еще раз
            object loadedModel = null;

            try
            {
                  // Проверяем наличие SentisCompat
                  Type sentisCompatType = null;
                  foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                  {
                        sentisCompatType = assembly.GetType("SentisCompat");
                        if (sentisCompatType != null) break;
                  }

                  if (sentisCompatType != null)
                  {
                        // Загружаем через SentisCompat
                        MethodInfo loadModelMethod = sentisCompatType.GetMethod("LoadModel");
                        if (loadModelMethod != null)
                        {
                              loadedModel = loadModelMethod.Invoke(null, new object[] { testModel });
                        }

                        if (loadedModel != null)
                        {
                              // Создаем Worker
                              MethodInfo createWorkerMethod = sentisCompatType.GetMethod("CreateWorker");
                              if (createWorkerMethod != null)
                              {
                                    Debug.Log("SentisTroubleshooter: Создание Worker через SentisCompat.CreateWorker()");
                                    object worker = createWorkerMethod.Invoke(null, new object[] { loadedModel, 0 });

                                    workerCreationSucceeded = (worker != null);

                                    if (workerCreationSucceeded)
                                    {
                                          Debug.Log($"SentisTroubleshooter: Worker успешно создан, тип: {worker.GetType().Name}");

                                          // Освобождаем ресурсы Worker
                                          try
                                          {
                                                var disposeMethod = worker.GetType().GetMethod("Dispose");
                                                if (disposeMethod != null)
                                                {
                                                      disposeMethod.Invoke(worker, null);
                                                }
                                          }
                                          catch { }
                                    }
                                    else
                                    {
                                          Debug.LogError("SentisTroubleshooter: Не удалось создать Worker");
                                          foundIssues.Add("Не удалось создать Worker");
                                    }
                              }
                        }
                  }
                  else
                  {
                        Debug.LogWarning("SentisTroubleshooter: SentisCompat не найден, тестирование Worker пропущено");
                        foundIssues.Add("SentisCompat не найден, тестирование Worker пропущено");
                  }
            }
            catch (Exception e)
            {
                  workerCreationSucceeded = false;
                  Debug.LogError($"SentisTroubleshooter: Ошибка при создании Worker: {e.Message}");
                  foundIssues.Add($"Ошибка при создании Worker: {e.Message}");

                  if (e.InnerException != null)
                  {
                        Debug.LogError($"SentisTroubleshooter: Внутреннее исключение: {e.InnerException.Message}");
                  }
            }
      }

      /// <summary>
      /// Вызывает RunFullDiagnostic через кнопку в инспекторе
      /// </summary>
      public void RunDiagnosticFromInspector()
      {
            StartCoroutine(RunFullDiagnostic());
      }
}