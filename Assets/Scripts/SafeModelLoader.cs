using UnityEngine;
using System.Collections;
using System;

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

            // Создаем отдельный поток для загрузки модели
            System.Threading.Thread loaderThread = new System.Threading.Thread(() =>
            {
                  try
                  {
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

            // Ждем завершения загрузки с учетом тайм-аута
            while (!isComplete && Time.time - startTime < timeoutSeconds)
            {
                  yield return null;
            }

            // Проверяем результат загрузки
            if (!isComplete)
            {
                  // Загрузка не завершилась вовремя, прерываем поток
                  try
                  {
                        loaderThread.Abort();
                  }
                  catch (Exception e)
                  {
                        Debug.LogError($"Ошибка при прерывании потока загрузки: {e.Message}");
                  }

                  callback?.Invoke(false, null, $"Превышен тайм-аут загрузки модели ({timeoutSeconds} сек)");
                  yield break;
            }

            // Проверяем наличие ошибок
            if (loadException != null)
            {
                  callback?.Invoke(false, null, $"Ошибка при загрузке модели: {loadException.Message}");
                  yield break;
            }

            // Проверяем, что модель была успешно загружена
            if (loadedModel == null)
            {
                  callback?.Invoke(false, null, "Модель загружена, но результат null");
                  yield break;
            }

            // Успешная загрузка
            callback?.Invoke(true, loadedModel, null);
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
                  // Определяем оптимальный бэкенд
                  int actualBackend = DetermineOptimalBackend(preferredBackend);
                  Debug.Log($"Используем бэкенд с индексом: {actualBackend}");

                  // Используем рефлексию для создания Worker
                  var workerFactoryType = Type.GetType("Unity.Sentis.WorkerFactory, Unity.Sentis");
                  if (workerFactoryType != null)
                  {
                        var createMethod = workerFactoryType.GetMethod("CreateWorker",
                              new Type[] { model.GetType(), typeof(int) });

                        if (createMethod != null)
                        {
                              var worker = createMethod.Invoke(null, new object[] { model, actualBackend });
                              Debug.Log("Worker успешно создан");
                              return worker;
                        }
                        else
                        {
                              Debug.LogError("Метод CreateWorker не найден в WorkerFactory");
                        }
                  }
                  else
                  {
                        Debug.LogError("Тип Unity.Sentis.WorkerFactory не найден");
                  }
            }
            catch (Exception e)
            {
                  Debug.LogError($"Ошибка при создании Worker: {e.Message}");
            }

            return null;
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
      /// Проверяет, установлен ли Unity Sentis
      /// </summary>
      /// <returns>true, если Unity Sentis доступен</returns>
      public static bool IsSentisAvailable()
      {
            var modelLoaderType = Type.GetType("Unity.Sentis.ModelLoader, Unity.Sentis");
            return modelLoaderType != null;
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
}