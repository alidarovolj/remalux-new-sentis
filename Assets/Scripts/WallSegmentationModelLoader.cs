using UnityEngine;
using System.Collections;
using System;

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

      private WallSegmentation wallSegmentation;
      private bool isModelLoading = false;
      private bool isModelLoaded = false;
      private string lastErrorMessage = null;

      private void Start()
      {
            // Получаем ссылку на компонент WallSegmentation
            wallSegmentation = GetComponent<WallSegmentation>();
            if (wallSegmentation == null)
            {
                  Debug.LogError("WallSegmentationModelLoader: Компонент WallSegmentation не найден на этом объекте.");
                  return;
            }

            // Автоматически загружаем модель при старте
            if (loadOnStart && modelAsset != null)
            {
                  StartCoroutine(LoadModel());
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

            // Используем SafeModelLoader для безопасной загрузки
            yield return StartCoroutine(SafeModelLoader.LoadModelAsync(
                modelAsset,
                loadTimeout,
                OnModelLoaded));
      }

      private void OnModelLoaded(bool success, object model, string errorMessage)
      {
            isModelLoading = false;
            isModelLoaded = success;

            if (success)
            {
                  Debug.Log($"WallSegmentationModelLoader: Модель успешно загружена");

                  // Найти метод InitializeSegmentation и вызвать его
                  var initMethod = wallSegmentation.GetType().GetMethod("InitializeSegmentation",
                      System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

                  if (initMethod != null)
                  {
                        // Создаем исполнитель через безопасную обертку
                        var worker = SafeModelLoader.CreateWorkerSafely(model, preferredBackend);

                        if (worker != null)
                        {
                              // Устанавливаем модель в компонент WallSegmentation
                              SetPrivateField(wallSegmentation, "runtimeModel", model);
                              SetPrivateField(wallSegmentation, "engine", worker);
                              SetPrivateField(wallSegmentation, "isModelInitialized", true);

                              Debug.Log("WallSegmentation успешно инициализирован с моделью");
                        }
                        else
                        {
                              Debug.LogError("Не удалось создать Worker для модели");
                              ShowErrorInDialog("Не удалось создать Worker для модели. Возможно, выбран неподдерживаемый бэкенд или недостаточно памяти.");
                        }
                  }
                  else
                  {
                        Debug.LogError("Метод InitializeSegmentation не найден в WallSegmentation");
                  }
            }
            else
            {
                  lastErrorMessage = errorMessage;
                  Debug.LogError($"WallSegmentationModelLoader: Ошибка загрузки модели: {errorMessage}");

                  // Покажем понятное сообщение пользователю
                  ShowErrorInDialog(errorMessage);
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
      /// Показывает сообщение об ошибке в диалоговом окне
      /// </summary>
      private void ShowErrorInDialog(string errorMessage)
      {
            // Проверяем есть ли класс DialogInitializer
            if (DialogInitializer.instance != null)
            {
                  // Вызываем статический метод через рефлексию для показа ошибки
                  var showErrorMethod = typeof(DialogInitializer).GetMethod("ShowModelLoadError",
                      System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

                  if (showErrorMethod != null)
                  {
                        // Получаем информацию о модели
                        string modelName = modelAsset != null ? modelAsset.name : "неизвестная модель";
                        string modelType = modelAsset != null ? modelAsset.GetType().Name : "неизвестный тип";

                        // Подготавливаем данные о модели
                        var modelInfo = new ModelLoadErrorInfo(
                            modelName,
                            modelType,
                            errorMessage,
                            "Убедитесь, что модель совместима с Unity Sentis. Рекомендуется использовать модели ONNX размером до 50МБ."
                        );

                        // Вызываем метод показа ошибки
                        showErrorMethod.Invoke(null, new object[] { modelInfo });
                  }
                  else
                  {
                        Debug.LogError("Метод ShowModelLoadError не найден в DialogInitializer");
                  }
            }
            else
            {
                  Debug.LogWarning("DialogInitializer не найден в сцене. Сообщение об ошибке не будет показано.");
            }
      }

      /// <summary>
      /// Публичный метод для проверки состояния загрузки
      /// </summary>
      public bool IsModelLoaded => isModelLoaded;

      /// <summary>
      /// Публичный метод для получения последней ошибки
      /// </summary>
      public string LastErrorMessage => lastErrorMessage;
}

/// <summary>
/// Структура для хранения информации об ошибке загрузки модели
/// </summary>
[System.Serializable]
public class ModelLoadErrorInfo
{
      public string modelName;
      public string modelType;
      public string errorMessage;
      public string recommendation;

      public ModelLoadErrorInfo(string modelName, string modelType, string errorMessage, string recommendation)
      {
            this.modelName = modelName;
            this.modelType = modelType;
            this.errorMessage = errorMessage;
            this.recommendation = recommendation;
      }
}