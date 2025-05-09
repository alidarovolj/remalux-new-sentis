using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System;

/// <summary>
/// Менеджер диалоговых окон для отображения ошибок и уведомлений в AR приложении
/// </summary>
public class DialogManager : MonoBehaviour
{
      // Префаб для диалога, если есть
      [SerializeField] private GameObject dialogPrefab;

      // Синглтон для глобального доступа
      public static DialogManager instance;

      private GameObject dialogInstance;
      private MonoBehaviour dialogInitializer;

      private void Awake()
      {
            // Настройка синглтона
            if (instance != null && instance != this)
            {
                  Destroy(gameObject);
                  return;
            }

            instance = this;
            DontDestroyOnLoad(gameObject);

            // Создаем диалоговое окно при старте
            StartCoroutine(InitializeDialog());
      }

      /// <summary>
      /// Инициализирует диалоговое окно
      /// </summary>
      private IEnumerator InitializeDialog()
      {
            yield return null; // Ждем один кадр

            // Если префаб не задан, создаем объект программно
            if (dialogPrefab == null)
            {
                  // Создаем новый GameObject
                  dialogInstance = new GameObject("ErrorDialog");
                  dialogInstance.transform.SetParent(transform);

                  // Добавляем необходимые UI компоненты
                  Canvas canvas = dialogInstance.AddComponent<Canvas>();
                  canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                  canvas.sortingOrder = 100; // Выше остальных UI

                  dialogInstance.AddComponent<CanvasScaler>();
                  dialogInstance.AddComponent<GraphicRaycaster>();

                  // Создаем панель для диалога
                  GameObject panel = new GameObject("Panel");
                  panel.transform.SetParent(dialogInstance.transform);

                  RectTransform panelRect = panel.AddComponent<RectTransform>();
                  panelRect.anchorMin = new Vector2(0.5f, 0.5f);
                  panelRect.anchorMax = new Vector2(0.5f, 0.5f);
                  panelRect.pivot = new Vector2(0.5f, 0.5f);
                  panelRect.sizeDelta = new Vector2(600, 400);

                  // Добавляем фон панели
                  Image panelImage = panel.AddComponent<Image>();
                  panelImage.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);

                  // Создаем заголовок
                  GameObject titleObj = new GameObject("Title");
                  titleObj.transform.SetParent(panel.transform);

                  RectTransform titleRect = titleObj.AddComponent<RectTransform>();
                  titleRect.anchorMin = new Vector2(0, 1);
                  titleRect.anchorMax = new Vector2(1, 1);
                  titleRect.pivot = new Vector2(0.5f, 1);
                  titleRect.sizeDelta = new Vector2(0, 60);
                  titleRect.anchoredPosition = Vector2.zero;

                  Text titleText = titleObj.AddComponent<Text>();
                  titleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                  titleText.fontSize = 24;
                  titleText.alignment = TextAnchor.MiddleCenter;
                  titleText.color = Color.white;
                  titleText.text = "Ошибка";

                  // Добавляем другие компоненты для диалога...
                  // (Упрощенная версия для примера)

                  // Скрываем диалог до первого использования
                  dialogInstance.SetActive(false);
            }
            else
            {
                  // Инстанцируем диалог из префаба
                  dialogInstance = Instantiate(dialogPrefab, transform);
                  dialogInstance.SetActive(false);
            }

            // Находим компонент DialogInitializer, если он уже есть
            dialogInitializer = dialogInstance.GetComponent("DialogInitializer") as MonoBehaviour;

            if (dialogInitializer == null)
            {
                  // Пытаемся найти тип DialogInitializer через рефлексию
                  Type dialogType = Type.GetType("DialogInitializer");

                  if (dialogType == null)
                  {
                        // Поиск во всех сборках
                        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                        {
                              dialogType = assembly.GetType("DialogInitializer");
                              if (dialogType != null) break;
                        }
                  }

                  if (dialogType != null)
                  {
                        // Добавляем компонент
                        dialogInitializer = dialogInstance.AddComponent(dialogType) as MonoBehaviour;
                        Debug.Log("DialogManager: DialogInitializer создан");
                  }
                  else
                  {
                        Debug.LogWarning("DialogManager: Тип DialogInitializer не найден");
                  }
            }
      }

      /// <summary>
      /// Показывает диалог с ошибкой
      /// </summary>
      public void ShowErrorDialog(string title, string message)
      {
            if (dialogInstance != null)
            {
                  // Находим и обновляем текст заголовка
                  Text titleText = dialogInstance.GetComponentInChildren<Text>();
                  if (titleText != null)
                  {
                        titleText.text = title;
                  }

                  // Показываем диалог
                  dialogInstance.SetActive(true);

                  Debug.LogError($"DialogManager: {title} - {message}");
            }
      }

      /// <summary>
      /// Скрывает диалог
      /// </summary>
      public void HideDialog()
      {
            if (dialogInstance != null)
            {
                  dialogInstance.SetActive(false);
            }
      }

      /// <summary>
      /// Показывает сообщение об ошибке загрузки модели
      /// </summary>
      public void ShowModelLoadError(string modelName, string errorMessage)
      {
            ShowErrorDialog("Ошибка загрузки модели",
                $"Не удалось загрузить модель {modelName}.\n\nОшибка: {errorMessage}");
      }
}