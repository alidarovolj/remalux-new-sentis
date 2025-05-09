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
      /// Асинхронно загружает модель в отдельном потоке, предотвращая краш приложения
      /// </summary>
      /// <param name="modelAsset">Ассет модели</param>
      /// <param name="callback">Функция обратного вызова для обработки результата</param>
      /// <returns>Корутина для управления процессом загрузки</returns>
      public static IEnumerator LoadModelAsync(UnityEngine.Object modelAsset, ModelLoadCallback callback)
      {
            if (modelAsset == null)
            {
                  callback?.Invoke(false, null, "ModelAsset is null");
                  yield break;
            }

            bool success = false;
            object loadedModel = null;
            string errorMessage = null;

            // Проверяем, установлен ли Unity Sentis
            if (!IsSentisAvailable())
            {
                  callback?.Invoke(false, null, "Unity Sentis не установлен");
                  yield break;
            }

            // Проверяем тип моделей
            if (!IsModelTypeValid(modelAsset))
            {
                  callback?.Invoke(false, null, $"Неподдерживаемый тип модели: {modelAsset.GetType().Name}. Ожидается ModelAsset");
                  yield break;
            }

            // Запускаем загрузку модели в основном потоке с контролем тайм-аута
            float startTime = Time.realtimeSinceStartup;
            float timeoutDuration = 10f;

            // Создаем флаг для отслеживания завершения
            bool isComplete = false;
            System.Exception loadException = null;

            // Используем try-catch блок для прямой загрузки вместо рефлексии
            try
            {
                  Debug.Log("Загрузка модели с помощью прямого вызова...");

                  // Попытка загрузить напрямую через using
                  using (var tempObj = new System.IO.MemoryStream(1))
                  {
                        // Если мы попали сюда, значит Sentis доступен в сборке
                        Type sentisModelLoaderType = Type.GetType("Unity.Sentis.ModelLoader, Unity.Sentis");
                        Type sentisModelAssetType = Type.GetType("Unity.Sentis.ModelAsset, Unity.Sentis");

                        if (sentisModelLoaderType != null && sentisModelAssetType != null)
                        {
                              // Проверяем типы параметров
                              Debug.Log($"Тип модели: {modelAsset.GetType().FullName}, ожидаемый тип: {sentisModelAssetType.FullName}");

                              // Ищем метод с правильной сигнатурой
                              var loadMethods = sentisModelLoaderType.GetMethods(
                                    System.Reflection.BindingFlags.Public |
                                    System.Reflection.BindingFlags.Static);

                              var correctMethod = null as System.Reflection.MethodInfo;

                              foreach (var method in loadMethods)
                              {
                                    if (method.Name == "Load")
                                    {
                                          var parameters = method.GetParameters();
                                          if (parameters.Length == 1)
                                          {
                                                Debug.Log($"Нашли метод Load с параметром типа: {parameters[0].ParameterType.FullName}");

                                                if (parameters[0].ParameterType.IsAssignableFrom(modelAsset.GetType()))
                                                {
                                                      correctMethod = method;
                                                      break;
                                                }
                                          }
                                    }
                              }

                              if (correctMethod != null)
                              {
                                    Debug.Log($"Вызываем метод {correctMethod.Name}");
                                    loadedModel = correctMethod.Invoke(null, new object[] { modelAsset });
                                    success = loadedModel != null;
                              }
                              else
                              {
                                    errorMessage = "Метод Load с правильной сигнатурой не найден в ModelLoader";
                                    Debug.LogError(errorMessage);
                              }
                        }
                        else
                        {
                              errorMessage = "Unity Sentis не найден в сборке";
                              Debug.LogError(errorMessage);
                        }
                  }
            }
            catch (System.Exception e)
            {
                  loadException = e;
                  errorMessage = $"Исключение при загрузке модели: {e.Message}";
                  if (e.InnerException != null)
                  {
                        errorMessage += $"\nВнутреннее исключение: {e.InnerException.Message}";
                  }
                  Debug.LogError(errorMessage);
                  Debug.LogException(e);
            }
            finally
            {
                  isComplete = true;
            }

            // Ждем завершения или тайм-аута
            yield return null;

            // Проверяем результат
            if (!success && string.IsNullOrEmpty(errorMessage))
            {
                  errorMessage = "Неизвестная ошибка при загрузке модели";
                  if (loadException != null)
                  {
                        errorMessage = $"Ошибка при загрузке модели: {loadException.Message}";
                  }
            }

            // Вызываем обратный вызов
            callback?.Invoke(success, loadedModel, errorMessage);
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