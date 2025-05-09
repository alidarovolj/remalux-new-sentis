using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using System; // For Exception usage

/// <summary>
/// Класс для представления информации об ошибке загрузки модели
/// </summary>
[System.Serializable]
public class ModelErrorInfo
{
      public string modelName;
      public string modelType;
      public string errorMessage;
      public string recommendation;

      public ModelErrorInfo(string modelName, string modelType, string errorMessage, string recommendation)
      {
            this.modelName = modelName;
            this.modelType = modelType;
            this.errorMessage = errorMessage;
            this.recommendation = recommendation;
      }
}

/// <summary>
/// Инициализатор диалоговых окон и системы уведомлений для AR компонентов.
/// Предоставляет статические методы для показа сообщений об ошибках и предупреждений.
/// </summary>
public class DialogInitializer : MonoBehaviour
{
      // Синглтон для доступа к инициализатору
      public static DialogInitializer instance;

      [Header("Компоненты UI")]
      [SerializeField] private GameObject errorDialogPanel;
      [SerializeField] private Text errorTitleText;
      [SerializeField] private Text errorMessageText;
      [SerializeField] private Text errorDetailsText;
      [SerializeField] private Button errorCloseButton;

      [Header("Настройки")]
      [SerializeField] private float autoHideDelay = 0f; // 0 = не прятать автоматически

      private void Awake()
      {
            // Настройка синглтона
            if (instance != null && instance != this)
            {
                  Destroy(gameObject);
                  return;
            }

            instance = this;

            // Не уничтожать при загрузке сцены
            DontDestroyOnLoad(gameObject);

            // Инициализация компонентов UI при старте, если они не заданы
            InitializeUIComponents();
      }

      /// <summary>
      /// Инициализация компонентов UI диалогов
      /// </summary>
      private void InitializeUIComponents()
      {
            // Если диалоговая панель не назначена, ищем её в иерархии
            if (errorDialogPanel == null)
            {
                  // Пытаемся найти по имени
                  Transform dialogTransform = transform.Find("ErrorDialog");
                  if (dialogTransform != null)
                  {
                        errorDialogPanel = dialogTransform.gameObject;
                  }
                  else
                  {
                        // Создаем диалоговое окно программно
                        errorDialogPanel = CreateErrorDialogUI();
                  }
            }

            // Скрываем диалог по умолчанию
            if (errorDialogPanel != null)
            {
                  errorDialogPanel.SetActive(false);
            }
      }

      /// <summary>
      /// Создает UI для диалога с ошибкой
      /// </summary>
      private GameObject CreateErrorDialogUI()
      {
            // Создаем панель
            GameObject panel = new GameObject("ErrorDialog");
            panel.transform.SetParent(transform);

            RectTransform rectTransform = panel.AddComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.pivot = new Vector2(0.5f, 0.5f);
            rectTransform.sizeDelta = new Vector2(600, 400);

            // Добавляем канвас, если находимся в корне
            if (transform.GetComponent<Canvas>() == null)
            {
                  Canvas canvas = panel.AddComponent<Canvas>();
                  canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                  panel.AddComponent<CanvasScaler>();
                  panel.AddComponent<GraphicRaycaster>();
            }

            // Добавляем фон
            Image background = panel.AddComponent<Image>();
            background.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);

            // Создаем текст заголовка
            GameObject titleObj = new GameObject("TitleText");
            titleObj.transform.SetParent(panel.transform);
            RectTransform titleRect = titleObj.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0, 1);
            titleRect.anchorMax = new Vector2(1, 1);
            titleRect.pivot = new Vector2(0.5f, 1);
            titleRect.anchoredPosition = new Vector2(0, -20);
            titleRect.sizeDelta = new Vector2(0, 60);

            errorTitleText = titleObj.AddComponent<Text>();
            errorTitleText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            errorTitleText.fontSize = 24;
            errorTitleText.alignment = TextAnchor.MiddleCenter;
            errorTitleText.color = Color.white;
            errorTitleText.text = "Ошибка";

            // Создаем текст сообщения
            GameObject messageObj = new GameObject("MessageText");
            messageObj.transform.SetParent(panel.transform);
            RectTransform messageRect = messageObj.AddComponent<RectTransform>();
            messageRect.anchorMin = new Vector2(0, 0.5f);
            messageRect.anchorMax = new Vector2(1, 0.5f);
            messageRect.pivot = new Vector2(0.5f, 0.5f);
            messageRect.anchoredPosition = new Vector2(0, 0);
            messageRect.sizeDelta = new Vector2(-40, 100);

            errorMessageText = messageObj.AddComponent<Text>();
            errorMessageText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            errorMessageText.fontSize = 18;
            errorMessageText.alignment = TextAnchor.MiddleCenter;
            errorMessageText.color = Color.white;
            errorMessageText.text = "Произошла ошибка при загрузке.";

            // Создаем текст деталей
            GameObject detailsObj = new GameObject("DetailsText");
            detailsObj.transform.SetParent(panel.transform);
            RectTransform detailsRect = detailsObj.AddComponent<RectTransform>();
            detailsRect.anchorMin = new Vector2(0, 0);
            detailsRect.anchorMax = new Vector2(1, 0.4f);
            detailsRect.pivot = new Vector2(0.5f, 0);
            detailsRect.anchoredPosition = new Vector2(0, 20);
            detailsRect.sizeDelta = new Vector2(-40, 0);

            errorDetailsText = detailsObj.AddComponent<Text>();
            errorDetailsText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            errorDetailsText.fontSize = 14;
            errorDetailsText.alignment = TextAnchor.UpperLeft;
            errorDetailsText.color = new Color(0.8f, 0.8f, 0.8f);
            errorDetailsText.text = "Дополнительная информация об ошибке";

            // Создаем кнопку закрытия
            GameObject buttonObj = new GameObject("CloseButton");
            buttonObj.transform.SetParent(panel.transform);
            RectTransform buttonRect = buttonObj.AddComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0.5f, 0);
            buttonRect.anchorMax = new Vector2(0.5f, 0);
            buttonRect.pivot = new Vector2(0.5f, 0);
            buttonRect.anchoredPosition = new Vector2(0, 20);
            buttonRect.sizeDelta = new Vector2(150, 50);

            Image buttonImage = buttonObj.AddComponent<Image>();
            buttonImage.color = new Color(0.3f, 0.3f, 0.3f);

            errorCloseButton = buttonObj.AddComponent<Button>();
            errorCloseButton.targetGraphic = buttonImage;

            GameObject buttonText = new GameObject("Text");
            buttonText.transform.SetParent(buttonObj.transform);
            RectTransform buttonTextRect = buttonText.AddComponent<RectTransform>();
            buttonTextRect.anchorMin = Vector2.zero;
            buttonTextRect.anchorMax = Vector2.one;
            buttonTextRect.offsetMin = Vector2.zero;
            buttonTextRect.offsetMax = Vector2.zero;

            Text closeText = buttonText.AddComponent<Text>();
            closeText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            closeText.fontSize = 18;
            closeText.alignment = TextAnchor.MiddleCenter;
            closeText.color = Color.white;
            closeText.text = "Закрыть";

            // Добавляем обработчик кнопки закрытия
            errorCloseButton.onClick.AddListener(() => HideErrorDialog());

            return panel;
      }

      /// <summary>
      /// Показывает диалоговое окно с ошибкой загрузки модели
      /// </summary>
      /// <param name="errorInfo">Информация об ошибке</param>
      private void ShowErrorDialog(ModelErrorInfo errorInfo)
      {
            // Проверяем, что панель существует
            if (errorDialogPanel == null)
            {
                  Debug.LogError("Диалоговая панель не найдена");
                  return;
            }

            // Настраиваем текст диалога
            if (errorTitleText != null)
            {
                  errorTitleText.text = "Ошибка загрузки модели";
            }

            if (errorMessageText != null)
            {
                  errorMessageText.text = $"Не удалось загрузить модель: {errorInfo.modelName}";
            }

            if (errorDetailsText != null)
            {
                  // Формируем детальное сообщение
                  string details = $"Модель: {errorInfo.modelName}\n" +
                      $"Тип: {errorInfo.modelType}\n\n" +
                      $"Ошибка: {errorInfo.errorMessage}\n\n" +
                      $"Рекомендация: {errorInfo.recommendation}";

                  errorDetailsText.text = details;
            }

            // Настраиваем кнопку закрытия
            if (errorCloseButton != null)
            {
                  // Удаляем все имеющиеся обработчики
                  errorCloseButton.onClick.RemoveAllListeners();
                  // Добавляем новый обработчик
                  errorCloseButton.onClick.AddListener(HideErrorDialog);
            }

            // Показываем диалог
            errorDialogPanel.SetActive(true);

            // Если задержка автоскрытия больше 0, запускаем корутину
            if (autoHideDelay > 0)
            {
                  StartCoroutine(AutoHideDialog(autoHideDelay));
            }
      }

      /// <summary>
      /// Скрывает диалоговое окно с ошибкой
      /// </summary>
      public void HideErrorDialog()
      {
            if (errorDialogPanel != null)
            {
                  errorDialogPanel.SetActive(false);
            }
      }

      /// <summary>
      /// Автоматически скрывает диалог через указанное время
      /// </summary>
      private IEnumerator AutoHideDialog(float delay)
      {
            yield return new WaitForSeconds(delay);
            HideErrorDialog();
      }

      /// <summary>
      /// Показывает диалоговое окно с ошибкой загрузки модели
      /// </summary>
      /// <param name="errorInfo">Информация об ошибке загрузки модели</param>
      public static void ShowModelLoadError(ModelErrorInfo errorInfo)
      {
            if (instance != null)
            {
                  instance.ShowErrorDialog(errorInfo);
            }
            else
            {
                  // Если инициализатор не найден, выводим ошибку в консоль
                  Debug.LogError($"Ошибка загрузки модели: {errorInfo.modelName} - {errorInfo.errorMessage}");
            }
      }
}