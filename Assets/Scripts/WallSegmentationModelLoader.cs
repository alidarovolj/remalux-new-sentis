using UnityEngine;
using System.Collections;
using System;

/// <summary>
/// Вспомогательный класс для WallSegmentation, который поможет безопасно загружать модели
/// и предотвращать краши Unity при работе с несовместимыми моделями.
/// </summary>
public class WallSegmentationModelLoader : MonoBehaviour
{
      // Ссылка на исходный WallSegmentation компонент
      private Component wallSegmentation;

      // Последняя ошибка при загрузке модели
      private string lastErrorMessage = "";

      // Статус загрузки модели
      private bool isModelLoading = false;
      private bool isModelLoaded = false;

      // Максимальное время загрузки в секундах
      [SerializeField] private float loadingTimeout = 10f;

      private void Awake()
      {
            // Находим компонент WallSegmentation на том же объекте
            wallSegmentation = GetComponent(FindTypeByName("WallSegmentation"));

            if (wallSegmentation == null)
            {
                  Debug.LogError("WallSegmentationModelLoader должен быть прикреплен к тому же GameObject, что и WallSegmentation");
                  this.enabled = false;
                  return;
            }
      }

      private void Start()
      {
            // Начинаем безопасную загрузку модели
            StartCoroutine(SafeLoadModel());
      }

      private System.Type FindTypeByName(string typeName)
      {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                  var type = assembly.GetType(typeName);
                  if (type != null) return type;

                  foreach (var t in assembly.GetTypes())
                  {
                        if (t.Name == typeName) return t;
                  }
            }
            return null;
      }

      private IEnumerator SafeLoadModel()
      {
            // Получаем поле modelAsset из WallSegmentation
            var modelAssetField = wallSegmentation.GetType().GetField("modelAsset",
                System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (modelAssetField == null)
            {
                  Debug.LogError("Не удалось найти поле modelAsset в WallSegmentation");
                  yield break;
            }

            // Получаем текущую модель
            var modelAsset = modelAssetField.GetValue(wallSegmentation) as UnityEngine.Object;
            if (modelAsset == null)
            {
                  Debug.LogError("ModelAsset не назначен в WallSegmentation");
                  yield break;
            }

            Debug.Log($"WallSegmentationModelLoader: Начинаем безопасную загрузку модели {modelAsset.name}");

            // Устанавливаем статус загрузки
            isModelLoading = true;
            isModelLoaded = false;
            lastErrorMessage = "";

            // Пытаемся загрузить модель в отдельном потоке
            yield return StartCoroutine(SafeModelLoader.LoadModelAsync(modelAsset, OnModelLoaded));
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
                        try
                        {
                              // Вызываем метод инициализации, передавая загруженную модель
                              initMethod.Invoke(wallSegmentation, new object[] { model });
                              Debug.Log("WallSegmentationModelLoader: Инициализация модели выполнена успешно");
                        }
                        catch (Exception e)
                        {
                              Debug.LogError($"WallSegmentationModelLoader: Ошибка при инициализации модели: {e.Message}");
                              isModelLoaded = false;
                              lastErrorMessage = $"Ошибка инициализации: {e.Message}";

                              // Получаем модель для передачи информации о ней
                              var modelAsset = GetModelAsset();

                              // Отображаем ошибку в UI с информацией о модели
                              ShowErrorUI($"Не удалось инициализировать модель: {e.Message}", modelAsset);
                        }
                  }
                  else
                  {
                        Debug.LogWarning("WallSegmentationModelLoader: Не удалось найти метод InitializeSegmentation");
                  }
            }
            else
            {
                  // Логируем ошибку
                  Debug.LogError($"WallSegmentationModelLoader: Ошибка загрузки модели: {errorMessage}");
                  lastErrorMessage = errorMessage;

                  // Получаем моделассет для передачи информации о нем
                  var modelAsset = GetModelAsset();

                  // Отображаем ошибку в UI с передачей информации о модели
                  ShowErrorUI(errorMessage, modelAsset);
            }
      }

      /// <summary>
      /// Получает модель из WallSegmentation компонента
      /// </summary>
      private UnityEngine.Object GetModelAsset()
      {
            if (wallSegmentation == null) return null;

            try
            {
                  var modelAssetField = wallSegmentation.GetType().GetField("modelAsset",
                      System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic |
                      System.Reflection.BindingFlags.Instance);

                  if (modelAssetField != null)
                  {
                        return modelAssetField.GetValue(wallSegmentation) as UnityEngine.Object;
                  }
            }
            catch (Exception ex)
            {
                  Debug.LogError($"WallSegmentationModelLoader: Ошибка при получении модели: {ex.Message}");
            }

            return null;
      }

      /// <summary>
      /// Отображает ошибку загрузки модели с дополнительной информацией о модели
      /// </summary>
      private void ShowErrorUI(string errorMessage, UnityEngine.Object modelAsset = null)
      {
            try
            {
                  // Пытаемся найти DialogInitializer и показать ошибку
                  if (DialogInitializer.instance != null)
                  {
                        DialogInitializer.ShowModelLoadError(errorMessage, modelAsset);
                        return;
                  }

                  // Если DialogInitializer не найден, пробуем использовать прямой вызов
                  var dialogType = FindTypeByName("ModelLoadErrorDialog");
                  if (dialogType != null)
                  {
                        var instanceProperty = dialogType.GetProperty("Instance",
                            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

                        if (instanceProperty != null)
                        {
                              var dialogInstance = instanceProperty.GetValue(null);
                              if (dialogInstance != null)
                              {
                                    var showErrorMethod = dialogType.GetMethod("ShowError");
                                    if (showErrorMethod != null)
                                    {
                                          string enhancedError = errorMessage;
                                          if (modelAsset != null)
                                          {
                                                enhancedError += "\n\nИнформация о модели: " +
                                                      SafeModelLoader.GetRuntimeModelInfo(modelAsset);
                                          }

                                          showErrorMethod.Invoke(dialogInstance, new object[] { enhancedError });
                                          return;
                                    }
                              }
                        }
                  }

                  // Крайний случай: используем стандартный редакторский диалог в редакторе Unity
#if UNITY_EDITOR
                  UnityEditor.EditorUtility.DisplayDialog("Ошибка загрузки модели",
                        $"{errorMessage}\n\n" +
                        "Возможные причины:\n" +
                        "- Модель слишком большая\n" +
                        "- Неподдерживаемый формат модели\n" +
                        "- Отсутствует Unity Sentis или другая ML библиотека\n\n" +
                        "Рекомендации:\n" +
                        "- Используйте модели размером до 50MB\n" +
                        "- Убедитесь, что установлен пакет Unity Sentis\n" +
                        "- Проверьте формат модели (ONNX opset 7-15)",
                        "OK");
#endif
            }
            catch (System.Exception e)
            {
                  Debug.LogError($"Ошибка при показе UI: {e.Message}");

                  // Используем OnGUI как крайнюю меру
                  lastErrorMessage = errorMessage;
            }
      }

      private void OnGUI()
      {
            // Отображаем ошибку загрузки модели, если она есть
            if (!string.IsNullOrEmpty(lastErrorMessage) && !isModelLoaded)
            {
                  // Стиль для окна ошибки
                  GUIStyle windowStyle = new GUIStyle(GUI.skin.window);
                  windowStyle.normal.textColor = Color.white;
                  windowStyle.fontSize = 14;

                  // Стиль для текста
                  GUIStyle textStyle = new GUIStyle(GUI.skin.label);
                  textStyle.normal.textColor = Color.white;
                  textStyle.fontSize = 14;
                  textStyle.wordWrap = true;

                  // Стиль для кнопки
                  GUIStyle buttonStyle = new GUIStyle(GUI.skin.button);
                  buttonStyle.fontSize = 14;

                  // Размеры окна
                  int windowWidth = 500;
                  int windowHeight = 300;

                  // Рассчитываем центр экрана
                  int x = (Screen.width - windowWidth) / 2;
                  int y = (Screen.height - windowHeight) / 2;

                  // Рисуем окно
                  GUI.Box(new Rect(x, y, windowWidth, windowHeight), "Ошибка загрузки модели", windowStyle);

                  // Отображаем сообщение об ошибке
                  GUI.Label(new Rect(x + 20, y + 40, windowWidth - 40, 60), lastErrorMessage, textStyle);

                  // Отображаем возможные причины
                  GUI.Label(new Rect(x + 20, y + 100, windowWidth - 40, 30), "Возможные причины:", textStyle);
                  GUI.Label(new Rect(x + 20, y + 130, windowWidth - 40, 80),
                        "- Модель слишком большая\n" +
                        "- Неподдерживаемый формат модели\n" +
                        "- Отсутствует Unity Sentis", textStyle);

                  // Кнопка ОК
                  if (GUI.Button(new Rect(x + (windowWidth - 100) / 2, y + windowHeight - 50, 100, 30), "OK", buttonStyle))
                  {
                        // Очищаем сообщение об ошибке, чтобы скрыть окно
                        lastErrorMessage = null;
                  }
            }
      }
}