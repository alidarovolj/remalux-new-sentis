using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Компонент для отображения ошибок загрузки ML моделей с понятным интерфейсом
/// </summary>
public class ModelLoadErrorUI : MonoBehaviour
{
      [Header("UI References")]
      [SerializeField] private GameObject errorPanel;
      [SerializeField] private TextMeshProUGUI titleText;
      [SerializeField] private TextMeshProUGUI errorMessageText;
      [SerializeField] private TextMeshProUGUI reasonsTitleText;
      [SerializeField] private TextMeshProUGUI reasonsText;
      [SerializeField] private TextMeshProUGUI recommendationsTitleText;
      [SerializeField] private TextMeshProUGUI recommendationsText;
      [SerializeField] private Button okButton;

      [Header("Icons")]
      [SerializeField] private Image iconImage;
      [SerializeField] private Sprite errorIcon;

      [Header("Settings")]
      [SerializeField] private float autoHideDelay = 0f; // 0 = не скрывать автоматически

      private static ModelLoadErrorUI instance;

      public static ModelLoadErrorUI Instance
      {
            get
            {
                  if (instance == null)
                  {
                        // Попытка найти существующий экземпляр
                        instance = FindObjectOfType<ModelLoadErrorUI>();

                        // Если экземпляр не найден, создаем новый
                        if (instance == null)
                        {
                              GameObject errorUIObject = new GameObject("ModelLoadErrorUI");
                              instance = errorUIObject.AddComponent<ModelLoadErrorUI>();
                        }
                  }
                  return instance;
            }
      }

      private void Awake()
      {
            if (instance == null)
            {
                  instance = this;

                  // Проверяем, является ли объект корневым
                  if (transform.parent != null)
                  {
                        // Отсоединяем от родителя перед вызовом DontDestroyOnLoad
                        transform.SetParent(null);
                  }

                  DontDestroyOnLoad(gameObject);
            }
            else if (instance != this)
            {
                  Destroy(gameObject);
                  return;
            }

            // Убедимся, что панель скрыта при запуске
            HideError();

            // Если панель и компоненты не назначены, создаем их динамически
            if (errorPanel == null)
            {
                  CreateErrorUI();
            }
      }

      /// <summary>
      /// Динамически создает UI для отображения ошибок
      /// </summary>
      private void CreateErrorUI()
      {
            // Создаем Canvas, если его нет на сцене
            Canvas canvas = FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                  GameObject canvasObject = new GameObject("ErrorCanvas");
                  canvas = canvasObject.AddComponent<Canvas>();
                  canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                  canvasObject.AddComponent<CanvasScaler>();
                  canvasObject.AddComponent<GraphicRaycaster>();
            }

            // Создаем панель ошибки
            errorPanel = new GameObject("ErrorPanel");
            errorPanel.transform.SetParent(canvas.transform, false);

            RectTransform panelRect = errorPanel.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.sizeDelta = new Vector2(600, 700);
            panelRect.anchoredPosition = Vector2.zero;

            Image panelImage = errorPanel.AddComponent<Image>();
            panelImage.color = new Color(0.1f, 0.1f, 0.1f, 0.95f);

            // Добавляем иконку
            GameObject iconObject = new GameObject("Icon");
            iconObject.transform.SetParent(errorPanel.transform, false);
            RectTransform iconRect = iconObject.AddComponent<RectTransform>();
            iconRect.anchorMin = new Vector2(0.5f, 1);
            iconRect.anchorMax = new Vector2(0.5f, 1);
            iconRect.pivot = new Vector2(0.5f, 1);
            iconRect.sizeDelta = new Vector2(100, 100);
            iconRect.anchoredPosition = new Vector2(0, -70);
            iconImage = iconObject.AddComponent<Image>();

            // Пытаемся загрузить встроенную иконку или создаем программно
            errorIcon = ErrorIconProvider.GetErrorIcon();
            iconImage.sprite = errorIcon;

            // Создаем заголовок
            GameObject titleObject = new GameObject("Title");
            titleObject.transform.SetParent(errorPanel.transform, false);
            RectTransform titleRect = titleObject.AddComponent<RectTransform>();
            titleRect.anchorMin = new Vector2(0, 1);
            titleRect.anchorMax = new Vector2(1, 1);
            titleRect.pivot = new Vector2(0.5f, 1);
            titleRect.sizeDelta = new Vector2(0, 50);
            titleRect.anchoredPosition = new Vector2(0, -180);
            titleText = titleObject.AddComponent<TextMeshProUGUI>();
            titleText.fontSize = 32;
            titleText.alignment = TextAlignmentOptions.Center;
            titleText.color = Color.white;

            // Создаем текст ошибки
            GameObject messageObject = new GameObject("ErrorMessage");
            messageObject.transform.SetParent(errorPanel.transform, false);
            RectTransform messageRect = messageObject.AddComponent<RectTransform>();
            messageRect.anchorMin = new Vector2(0, 1);
            messageRect.anchorMax = new Vector2(1, 1);
            messageRect.pivot = new Vector2(0.5f, 1);
            messageRect.sizeDelta = new Vector2(40, 70);
            messageRect.anchoredPosition = new Vector2(0, -230);
            errorMessageText = messageObject.AddComponent<TextMeshProUGUI>();
            errorMessageText.fontSize = 24;
            errorMessageText.alignment = TextAlignmentOptions.Center;
            errorMessageText.color = Color.white;

            // Создаем заголовок причин
            GameObject reasonsTitleObject = new GameObject("ReasonsTitle");
            reasonsTitleObject.transform.SetParent(errorPanel.transform, false);
            RectTransform reasonsTitleRect = reasonsTitleObject.AddComponent<RectTransform>();
            reasonsTitleRect.anchorMin = new Vector2(0, 1);
            reasonsTitleRect.anchorMax = new Vector2(1, 1);
            reasonsTitleRect.pivot = new Vector2(0.5f, 1);
            reasonsTitleRect.sizeDelta = new Vector2(0, 30);
            reasonsTitleRect.anchoredPosition = new Vector2(0, -310);
            reasonsTitleText = reasonsTitleObject.AddComponent<TextMeshProUGUI>();
            reasonsTitleText.fontSize = 24;
            reasonsTitleText.alignment = TextAlignmentOptions.Center;
            reasonsTitleText.color = Color.white;

            // Создаем список причин
            GameObject reasonsObject = new GameObject("Reasons");
            reasonsObject.transform.SetParent(errorPanel.transform, false);
            RectTransform reasonsRect = reasonsObject.AddComponent<RectTransform>();
            reasonsRect.anchorMin = new Vector2(0, 1);
            reasonsRect.anchorMax = new Vector2(1, 1);
            reasonsRect.pivot = new Vector2(0.5f, 1);
            reasonsRect.sizeDelta = new Vector2(40, 120);
            reasonsRect.anchoredPosition = new Vector2(0, -360);
            reasonsText = reasonsObject.AddComponent<TextMeshProUGUI>();
            reasonsText.fontSize = 20;
            reasonsText.alignment = TextAlignmentOptions.Center;
            reasonsText.color = Color.white;

            // Создаем заголовок рекомендаций
            GameObject recomTitleObject = new GameObject("RecommendationsTitle");
            recomTitleObject.transform.SetParent(errorPanel.transform, false);
            RectTransform recomTitleRect = recomTitleObject.AddComponent<RectTransform>();
            recomTitleRect.anchorMin = new Vector2(0, 1);
            recomTitleRect.anchorMax = new Vector2(1, 1);
            recomTitleRect.pivot = new Vector2(0.5f, 1);
            recomTitleRect.sizeDelta = new Vector2(0, 30);
            recomTitleRect.anchoredPosition = new Vector2(0, -480);
            recommendationsTitleText = recomTitleObject.AddComponent<TextMeshProUGUI>();
            recommendationsTitleText.fontSize = 24;
            recommendationsTitleText.alignment = TextAlignmentOptions.Center;
            recommendationsTitleText.color = Color.white;

            // Создаем список рекомендаций
            GameObject recomObject = new GameObject("Recommendations");
            recomObject.transform.SetParent(errorPanel.transform, false);
            RectTransform recomRect = recomObject.AddComponent<RectTransform>();
            recomRect.anchorMin = new Vector2(0, 1);
            recomRect.anchorMax = new Vector2(1, 1);
            recomRect.pivot = new Vector2(0.5f, 1);
            recomRect.sizeDelta = new Vector2(40, 120);
            recomRect.anchoredPosition = new Vector2(0, -530);
            recommendationsText = recomObject.AddComponent<TextMeshProUGUI>();
            recommendationsText.fontSize = 20;
            recommendationsText.alignment = TextAlignmentOptions.Center;
            recommendationsText.color = Color.white;

            // Создаем кнопку OK
            GameObject buttonObject = new GameObject("OKButton");
            buttonObject.transform.SetParent(errorPanel.transform, false);
            RectTransform buttonRect = buttonObject.AddComponent<RectTransform>();
            buttonRect.anchorMin = new Vector2(0.5f, 0);
            buttonRect.anchorMax = new Vector2(0.5f, 0);
            buttonRect.pivot = new Vector2(0.5f, 0);
            buttonRect.sizeDelta = new Vector2(300, 60);
            buttonRect.anchoredPosition = new Vector2(0, 40);

            Image buttonImage = buttonObject.AddComponent<Image>();
            buttonImage.color = new Color(0.2f, 0.4f, 0.8f, 1);

            okButton = buttonObject.AddComponent<Button>();
            okButton.targetGraphic = buttonImage;

            GameObject buttonTextObject = new GameObject("Text");
            buttonTextObject.transform.SetParent(buttonObject.transform, false);
            RectTransform buttonTextRect = buttonTextObject.AddComponent<RectTransform>();
            buttonTextRect.anchorMin = Vector2.zero;
            buttonTextRect.anchorMax = Vector2.one;
            buttonTextRect.offsetMin = Vector2.zero;
            buttonTextRect.offsetMax = Vector2.zero;

            TextMeshProUGUI buttonText = buttonTextObject.AddComponent<TextMeshProUGUI>();
            buttonText.text = "OK";
            buttonText.fontSize = 24;
            buttonText.alignment = TextAlignmentOptions.Center;
            buttonText.color = Color.white;

            // Настройка обработчика кнопки
            okButton.onClick.AddListener(HideError);

            // Скрываем панель
            errorPanel.SetActive(false);
      }

      /// <summary>
      /// Отображает ошибку загрузки модели с пользовательским сообщением и рекомендациями
      /// </summary>
      /// <param name="errorMessage">Сообщение об ошибке</param>
      public void ShowModelLoadError(string errorMessage)
      {
            if (errorPanel == null) CreateErrorUI();

            // Заполняем тексты
            titleText.text = "Ошибка загрузки модели";
            errorMessageText.text = errorMessage;

            reasonsTitleText.text = "Возможные причины:";
            reasonsText.text = "- Модель слишком большая\n- Неподдерживаемый формат модели\n- Отсутствует Unity Sentis или другая ML библиотека";

            recommendationsTitleText.text = "Рекомендации:";
            recommendationsText.text = "- Используйте модели размером до 50MB\n- Убедитесь, что установлен пакет Unity Sentis\n- Проверьте формат модели (ONNX opset 7-15)";

            // Убедимся, что иконка назначена
            if (iconImage != null && errorIcon == null)
            {
                  errorIcon = ErrorIconProvider.GetErrorIcon();
                  iconImage.sprite = errorIcon;
            }

            // Показываем панель
            errorPanel.SetActive(true);

            // Если задана задержка автоматического скрытия, запускаем таймер
            if (autoHideDelay > 0)
            {
                  Invoke(nameof(HideError), autoHideDelay);
            }
      }

      /// <summary>
      /// Скрывает панель ошибки
      /// </summary>
      public void HideError()
      {
            if (errorPanel != null)
            {
                  errorPanel.SetActive(false);
            }
      }
}