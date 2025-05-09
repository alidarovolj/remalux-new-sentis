using UnityEngine;
using System.Collections;
using System;
using System.Threading;
using System.Linq;

/// <summary>
/// Утилита для безопасной загрузки моделей машинного обучения в рантайме.
/// Предотвращает краши приложения при проблемах с моделями.
/// </summary>
public static class SafeModelLoader
{
      /// <summary>
      /// Делегат для обработки результатов загрузки модели
      /// </summary>
      /// <param name="success">Успешность загрузки</param>
      /// <param name="model">Загруженная модель или null при ошибке</param>
      /// <param name="errorMessage">Сообщение об ошибке или null при успехе</param>
      public delegate void ModelLoadCallback(bool success, object model, string errorMessage);

      /// <summary>
      /// Асинхронно загружает модель машинного обучения с указанным тайм-аутом
      /// </summary>
      /// <param name="modelAsset">Ассет модели для загрузки</param>
      /// <param name="timeoutSeconds">Максимальное время ожидания загрузки в секундах</param>
      /// <param name="callback">Обратный вызов по завершении загрузки</param>
      public static IEnumerator LoadModelAsync(UnityEngine.Object modelAsset, float timeoutSeconds, ModelLoadCallback callback)
      {
            if (modelAsset == null)
            {
                  callback?.Invoke(false, null, "Ассет модели не указан (null)");
                  yield break;
            }

            // Информация о модели для отладки
            string modelInfo = $"Тип: {modelAsset.GetType().Name}, Имя: {modelAsset.name}";
            Debug.Log($"Начинаем безопасную загрузку модели: {modelInfo}");

            // Параметры для контроля загрузки
            bool isComplete = false;
            object loadedModel = null;
            Exception loadException = null;
            float startTime = Time.time;

            // Создаем CancellationTokenSource для безопасного прерывания потока
            using (CancellationTokenSource cts = new CancellationTokenSource())
            {
                  // Создаем отдельный поток для загрузки модели
                  System.Threading.Thread loaderThread = new System.Threading.Thread(() =>
                  {
                        try
                        {
                              // Проверка отмены операции
                              if (cts.Token.IsCancellationRequested)
                              {
                                    loadException = new OperationCanceledException("Операция загрузки модели была отменена");
                                    isComplete = true;
                                    return;
                              }

                              // Пытаемся загрузить модель, используя рефлексию, чтобы не зависеть от конкретного API
                              // (это позволяет работать с разными версиями Sentis и другими ML фреймворками)
                              var loaderType = Type.GetType("Unity.Sentis.ModelLoader, Unity.Sentis");
                              if (loaderType != null)
                              {
                                    var loadMethod = loaderType.GetMethod("Load", new Type[] { modelAsset.GetType() });
                                    if (loadMethod != null)
                                    {
                                          loadedModel = loadMethod.Invoke(null, new object[] { modelAsset });
                                          Debug.Log("Модель успешно загружена через рефлексию");
                                    }
                                    else
                                    {
                                          loadException = new Exception($"Метод Load не найден в ModelLoader для типа {modelAsset.GetType().Name}");
                                    }
                              }
                              else
                              {
                                    loadException = new Exception("Тип Unity.Sentis.ModelLoader не найден. Убедитесь, что Unity Sentis установлен");
                              }
                        }
                        catch (Exception ex)
                        {
                              // Сохраняем исключение для обработки в основном потоке
                              loadException = ex;
                              Debug.LogError($"Ошибка при загрузке модели: {ex.Message}");
                        }
                        finally
                        {
                              isComplete = true;
                        }
                  });
                  loaderThread.IsBackground = true; // Устанавливаем фоновый поток, чтобы он завершался при выходе из приложения

                  // Запускаем поток загрузки
                  try
                  {
                        loaderThread.Start();
                  }
                  catch (Exception e)
                  {
                        Debug.LogError($"Ошибка при запуске потока загрузки: {e.Message}");
                        callback?.Invoke(false, null, $"Ошибка при запуске потока загрузки: {e.Message}");
                        yield break;
                  }

                  // Ждем завершения с тайм-аутом
                  while (!isComplete && Time.time - startTime < timeoutSeconds)
                  {
                        yield return null;
                  }

                  // Обрабатываем результаты
                  if (!isComplete)
                  {
                        // Если превышен тайм-аут, отменяем операцию через CancellationToken
                        cts.Cancel();

                        // Ждем небольшой промежуток времени, чтобы дать потоку возможность корректно завершиться
                        float waitStartTime = Time.time;
                        while (!isComplete && Time.time - waitStartTime < 1.0f) // Ждем максимум 1 секунду
                        {
                              yield return null;
                        }

                        // Если поток все еще не завершился, создаем сообщение о тайм-ауте
                        if (!isComplete)
                        {
                              Debug.LogWarning($"Поток загрузки не ответил на запрос отмены в течение 1 секунды");
                              // Не используем Thread.Abort(), вместо этого просто считаем операцию отмененной
                        }

                        loadException = new TimeoutException($"Загрузка модели превысила тайм-аут {timeoutSeconds}с");
                  }
            } // Конец using для CancellationTokenSource

            // Формируем результат
            if (loadException != null)
            {
                  callback?.Invoke(false, null, $"Ошибка при загрузке модели: {loadException.Message}");
            }
            else if (loadedModel == null)
            {
                  callback?.Invoke(false, null, "Модель загружена, но результат null");
            }
            else
            {
                  callback?.Invoke(true, loadedModel, null);
            }
      }

      /// <summary>
      /// Безопасно создает исполнителя (Worker) для модели, с выбором оптимального бэкенда
      /// </summary>
      /// <param name="model">Загруженная модель</param>
      /// <param name="preferredBackend">Предпочитаемый бэкенд</param>
      /// <returns>Worker для модели или null при ошибке</returns>
      public static object CreateWorkerSafely(object model, int preferredBackend)
      {
            try
            {
                  // Проверяем наличие Sentis в первую очередь
                  if (!IsSentisAvailable())
                  {
                        Debug.LogError("Unity Sentis не установлен или не инициализирован корректно");
                        return null;
                  }

                  // Определяем оптимальный бэкенд
                  int actualBackend = DetermineOptimalBackend(preferredBackend);
                  Debug.Log($"Используем бэкенд с индексом: {actualBackend}");

                  // Находим Assembly с Sentis
                  System.Reflection.Assembly sentisAssembly = null;
                  foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                  {
                        if (assembly.GetName().Name == "Unity.Sentis")
                        {
                              sentisAssembly = assembly;
                              break;
                        }
                  }

                  if (sentisAssembly == null)
                  {
                        Debug.LogError("Сборка Unity.Sentis не найдена");
                        return null;
                  }

                  // Сначала проверяем наличие Worker (новая версия API 2.1.x)
                  var workerType = sentisAssembly.GetType("Unity.Sentis.Worker");
                  if (workerType != null)
                  {
                        Debug.Log("Обнаружен новый API Unity.Sentis.Worker (версия 2.1.x)");

                        // Находим тип BackendType для передачи в конструктор
                        var backendType = sentisAssembly.GetType("Unity.Sentis.BackendType");
                        if (backendType == null)
                        {
                              Debug.LogError("Тип Unity.Sentis.BackendType не найден");
                              return null;
                        }

                        // Создаем параметр BackendType из int
                        object backendEnum = Enum.ToObject(backendType, actualBackend);

                        // Ищем конструктор Worker с двумя параметрами (модель и бэкенд)
                        var constructor = workerType.GetConstructor(new[] { model.GetType(), backendType });
                        if (constructor != null)
                        {
                              var worker = constructor.Invoke(new[] { model, backendEnum });
                              Debug.Log($"Worker создан с новым API: {worker.GetType().Name}");
                              return worker;
                        }
                        else
                        {
                              Debug.LogError($"Не найден подходящий конструктор Worker({model.GetType().Name}, {backendType.Name})");
                        }
                  }

                  // Проверяем наличие WorkerFactory (старая версия API до 2.1.x)
                  var workerFactoryType = sentisAssembly.GetType("Unity.Sentis.WorkerFactory");
                  if (workerFactoryType == null)
                  {
                        // Вывод диагностики, что не найден ни Worker, ни WorkerFactory
                        Debug.LogError("Не найдены ни Unity.Sentis.Worker, ни Unity.Sentis.WorkerFactory");

                        // Попробуем найти альтернативные реализации Worker
                        var workerImplementations = sentisAssembly.GetTypes()
                              .Where(t => t.Name.Contains("Worker") && !t.IsInterface && !t.IsAbstract)
                              .ToArray();

                        if (workerImplementations.Length > 0)
                        {
                              Debug.LogWarning($"Найдены альтернативные типы Worker: {string.Join(", ", workerImplementations.Select(t => t.Name))}");

                              // Выводим информацию о конструкторах
                              foreach (var type in workerImplementations)
                              {
                                    var constructors = type.GetConstructors();
                                    Debug.Log($"Конструкторы {type.Name}: {constructors.Length}");
                                    foreach (var ctor in constructors)
                                    {
                                          var parameters = ctor.GetParameters();
                                          Debug.Log($"- Конструктор с параметрами: {string.Join(", ", parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"))}");
                                    }
                              }
                        }
                        return null;
                  }

                  // Стандартный сценарий для WorkerFactory (старая версия API)
                  var workerFactoryMethods = workerFactoryType.GetMethods()
                        .Where(m => m.Name == "CreateWorker")
                        .ToArray();

                  if (workerFactoryMethods.Length == 0)
                  {
                        Debug.LogError("Методы CreateWorker не найдены в WorkerFactory");
                        return null;
                  }

                  Debug.Log($"Найдено методов CreateWorker: {workerFactoryMethods.Length}");

                  // Сначала пробуем вызвать наиболее подходящий метод с заданным бэкендом
                  foreach (var method in workerFactoryMethods)
                  {
                        var parameters = method.GetParameters();
                        if (parameters.Length == 2 &&
                            parameters[0].ParameterType.IsAssignableFrom(model.GetType()) &&
                            parameters[1].ParameterType.IsEnum)
                        {
                              try
                              {
                                    // Создаем параметр для бэкенда
                                    object backendEnum = Enum.ToObject(parameters[1].ParameterType, actualBackend);
                                    var worker = method.Invoke(null, new[] { model, backendEnum });
                                    Debug.Log($"Worker создан с методом CreateWorker: {worker?.GetType()?.Name ?? "null"}");
                                    return worker;
                              }
                              catch (Exception e)
                              {
                                    Debug.LogWarning($"Не удалось создать Worker с заданным бэкендом: {e.Message}");
                              }
                        }
                  }

                  // Если не удалось создать с заданным бэкендом, пробуем без указания бэкенда
                  foreach (var method in workerFactoryMethods)
                  {
                        var parameters = method.GetParameters();
                        if (parameters.Length == 1 && parameters[0].ParameterType.IsAssignableFrom(model.GetType()))
                        {
                              try
                              {
                                    var worker = method.Invoke(null, new[] { model });
                                    Debug.Log($"Worker создан с методом CreateWorker (без бэкенда): {worker?.GetType()?.Name ?? "null"}");
                                    return worker;
                              }
                              catch (Exception e)
                              {
                                    Debug.LogError($"Не удалось создать Worker: {e.Message}");
                              }
                        }
                  }

                  Debug.LogError("Не удалось найти подходящий метод CreateWorker");
                  return null;
            }
            catch (Exception e)
            {
                  Debug.LogError($"Ошибка при создании Worker: {e.Message}");
                  if (e.InnerException != null)
                  {
                        Debug.LogError($"Внутреннее исключение: {e.InnerException.Message}");
                  }
                  return null;
            }
      }

      /// <summary>
      /// Определяет оптимальный бэкенд для текущей платформы
      /// </summary>
      private static int DetermineOptimalBackend(int preferredBackend)
      {
            // 0 = CPU, 1 = GPUCompute, 2 = GPUPixel (примерные значения для Unity Sentis)
            const int CPU_BACKEND = 0;
            const int GPU_COMPUTE_BACKEND = 1;

            // Проверяем поддержку Compute-шейдеров
            if (preferredBackend == GPU_COMPUTE_BACKEND && !SystemInfo.supportsComputeShaders)
            {
                  Debug.LogWarning("GPUCompute не поддерживается, используем CPU бэкенд");
                  return CPU_BACKEND;
            }

            // На мобильных платформах с ограниченной памятью лучше использовать CPU
            if (preferredBackend == GPU_COMPUTE_BACKEND &&
                  (Application.platform == RuntimePlatform.Android ||
                  Application.platform == RuntimePlatform.IPhonePlayer))
            {
                  if (SystemInfo.systemMemorySize < 3000) // Меньше 3GB RAM
                  {
                        Debug.LogWarning("Мало системной памяти, используем CPU бэкенд");
                        return CPU_BACKEND;
                  }
            }

            // При отладке предпочитаем CPU как более стабильный вариант
            if (Debug.isDebugBuild && Application.isEditor)
            {
                  return CPU_BACKEND;
            }

            return preferredBackend;
      }

#if UNITY_EDITOR
      /// <summary>
      /// Получает информацию о модели (размер, формат) - только для Editor
      /// </summary>
      /// <param name="modelAsset">Ассет модели</param>
      /// <returns>Строка с информацией о модели</returns>
      public static string GetModelInfo(UnityEngine.Object modelAsset)
      {
            if (modelAsset == null) return "ModelAsset is null";

            try
            {
                  string modelPath = UnityEditor.AssetDatabase.GetAssetPath(modelAsset);
                  if (string.IsNullOrEmpty(modelPath)) return "Unknown model (no path)";

                  var fileInfo = new System.IO.FileInfo(modelPath);
                  if (!fileInfo.Exists) return "Model file not found";

                  long fileSizeBytes = fileInfo.Length;
                  float fileSizeMB = fileSizeBytes / (1024f * 1024f);

                  string format = System.IO.Path.GetExtension(modelPath).ToLower();
                  string fileName = System.IO.Path.GetFileName(modelPath);

                  return $"{fileName} ({format}, {fileSizeMB:F2} MB)";
            }
            catch (Exception e)
            {
                  return $"Error getting model info: {e.Message}";
            }
      }
#endif

      /// <summary>
      /// Возвращает информацию о модели в рантайме
      /// </summary>
      /// <param name="modelAsset">Ассет модели</param>
      /// <returns>Строка с информацией о модели</returns>
      public static string GetRuntimeModelInfo(UnityEngine.Object modelAsset)
      {
            if (modelAsset == null) return "ModelAsset не указан";

            try
            {
                  return $"Модель: {modelAsset.name} (тип: {modelAsset.GetType().Name})";
            }
            catch (Exception e)
            {
                  return $"Ошибка получения информации о модели: {e.Message}";
            }
      }

      /// <summary>
      /// Проверяет, доступен ли пакет Unity Sentis в проекте
      /// </summary>
      public static bool IsSentisAvailable()
      {
            try
            {
                  // Проверяем наличие Assembly Unity.Sentis
                  var sentisAssembly = System.AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(a => a.GetName().Name == "Unity.Sentis");

                  if (sentisAssembly == null)
                  {
                        Debug.LogWarning("Сборка Unity.Sentis не найдена");

                        // Попробуем найти через Type.GetType
                        var modelType = Type.GetType("Unity.Sentis.Model, Unity.Sentis");
                        if (modelType == null)
                        {
                              Debug.LogWarning("Тип Unity.Sentis.Model не найден");
                              return false;
                        }
                  }

                  return true;
            }
            catch
            {
                  return false;
            }
      }

      /// <summary>
      /// Проверяет, является ли модель правильного типа для Sentis
      /// </summary>
      /// <param name="modelAsset">Ассет модели</param>
      /// <returns>true, если тип модели поддерживается</returns>
      public static bool IsModelTypeValid(UnityEngine.Object modelAsset)
      {
            if (modelAsset == null) return false;

            // Проверяем, соответствует ли тип модели ожидаемому типу Sentis.ModelAsset
            var sentisModelAssetType = Type.GetType("Unity.Sentis.ModelAsset, Unity.Sentis");
            if (sentisModelAssetType == null) return false;

            return sentisModelAssetType.IsAssignableFrom(modelAsset.GetType());
      }

      /// <summary>
      /// Выводит диагностическую информацию о состоянии Unity Sentis
      /// </summary>
      public static void DiagnoseSentisStatus()
      {
            Debug.Log("=== ДИАГНОСТИКА UNITY SENTIS ===");

            try
            {
                  // Проверяем наличие Assembly Unity.Sentis
                  var sentisAssembly = System.AppDomain.CurrentDomain.GetAssemblies()
                        .FirstOrDefault(a => a.GetName().Name == "Unity.Sentis");

                  if (sentisAssembly != null)
                  {
                        Debug.Log($"✅ Сборка Unity.Sentis найдена: {sentisAssembly.GetName().Version}");

                        // Проверяем наличие основных типов
                        var modelType = sentisAssembly.GetType("Unity.Sentis.Model");
                        var modelLoaderType = sentisAssembly.GetType("Unity.Sentis.ModelLoader");
                        var workerFactoryType = sentisAssembly.GetType("Unity.Sentis.WorkerFactory");

                        Debug.Log($"Тип Model найден: {(modelType != null ? "✅ Да" : "❌ Нет")}");
                        Debug.Log($"Тип ModelLoader найден: {(modelLoaderType != null ? "✅ Да" : "❌ Нет")}");
                        Debug.Log($"Тип WorkerFactory найден: {(workerFactoryType != null ? "✅ Да" : "❌ Нет")}");

                        // Если WorkerFactory найден, изучаем его методы
                        if (workerFactoryType != null)
                        {
                              var createWorkerMethods = workerFactoryType.GetMethods()
                                    .Where(m => m.Name == "CreateWorker")
                                    .ToArray();

                              Debug.Log($"Методов CreateWorker: {createWorkerMethods.Length}");

                              foreach (var method in createWorkerMethods)
                              {
                                    var parameters = method.GetParameters();
                                    Debug.Log($"- Метод с параметрами: {string.Join(", ", parameters.Select(p => p.ParameterType.Name))}");
                              }
                        }

                        // Ищем другие интересные типы
                        var workerTypes = sentisAssembly.GetTypes()
                              .Where(t => t.Name.Contains("Worker") && !t.IsInterface && !t.IsAbstract)
                              .ToArray();

                        Debug.Log($"Найдено конкретных типов Worker: {workerTypes.Length}");
                        if (workerTypes.Length > 0)
                        {
                              foreach (var worker in workerTypes)
                              {
                                    Debug.Log($"- {worker.Name}");
                              }
                        }
                  }
                  else
                  {
                        Debug.LogError("❌ Сборка Unity.Sentis не найдена");

                        // Пробуем через Type.GetType
                        var modelType = Type.GetType("Unity.Sentis.Model, Unity.Sentis");
                        Debug.Log($"Поиск через Type.GetType: {(modelType != null ? "✅ Успешно" : "❌ Не найден")}");
                  }

                  // Проверяем версию в package.json
                  Debug.Log("Поиск информации в package.json...");
                  string packageManifestPath = "Packages/manifest.json";
                  if (System.IO.File.Exists(packageManifestPath))
                  {
                        string manifest = System.IO.File.ReadAllText(packageManifestPath);
                        if (manifest.Contains("com.unity.sentis"))
                        {
                              int index = manifest.IndexOf("com.unity.sentis");
                              int versionStart = manifest.IndexOf(":", index) + 1;
                              int versionEnd = manifest.IndexOf(",", versionStart);
                              if (versionEnd == -1) versionEnd = manifest.IndexOf("}", versionStart);

                              if (versionStart > 0 && versionEnd > versionStart)
                              {
                                    string version = manifest.Substring(versionStart, versionEnd - versionStart).Trim();
                                    Debug.Log($"✅ Sentis в manifest.json: {version}");
                              }
                        }
                        else
                        {
                              Debug.LogError("❌ Sentis не найден в manifest.json");
                        }
                  }
                  else
                  {
                        Debug.LogWarning("⚠️ Файл manifest.json не найден");
                  }

                  // Проверяем системные требования
                  Debug.Log($"Compute Shaders поддерживаются: {(SystemInfo.supportsComputeShaders ? "✅ Да" : "❌ Нет")}");
                  Debug.Log($"Доступная системная память: {SystemInfo.systemMemorySize} MB");
                  Debug.Log($"Архитектура процессора: {SystemInfo.processorType}");
                  Debug.Log($"Количество ядер процессора: {SystemInfo.processorCount}");
                  Debug.Log($"Платформа: {Application.platform}");
                  Debug.Log($"Версия Unity: {Application.unityVersion}");
            }
            catch (Exception e)
            {
                  Debug.LogError($"❌ Ошибка при диагностике Sentis: {e.Message}");
                  if (e.InnerException != null)
                  {
                        Debug.LogError($"Внутреннее исключение: {e.InnerException.Message}");
                  }
            }

            Debug.Log("=== ДИАГНОСТИКА ЗАВЕРШЕНА ===");
      }
}