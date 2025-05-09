using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// Диалоговое окно для отображения ошибок при загрузке моделей машинного обучения
/// </summary>
public class ModelLoadErrorDialog : MonoBehaviour
{
      [Header("UI Компоненты")]
      [SerializeField] private Text titleText;
      [SerializeField] private Text errorMessageText;
      [SerializeField] private Text modelInfoText;
      [SerializeField] private Text recommendationText;
      [SerializeField] private Button closeButton;
      [SerializeField] private GameObject dialogPanel;

      [Header("Тексты")]
      [SerializeField] private string defaultTitle = "Ошибка загрузки модели";
      [SerializeField]
      private string defaultRecommendation =
          "Рекомендации:\n" +
          "- Используйте модели меньшего размера (до 50МБ)\n" +
          "- Убедитесь, что установлен пакет Unity Sentis\n" +
          "- Проверьте формат ONNX (поддерживаются opset 7-15)";

      // Синглтон для простого доступа
      private static ModelLoadErrorDialog _instance;
      public static ModelLoadErrorDialog Instance
      {
            get
            {
                  if (_instance == null)
                  {
                        _instance = FindObjectOfType<ModelLoadErrorDialog>();

                        if (_instance == null)
                        {
                              GameObject obj = new GameObject("ModelLoadErrorDialog");
                              _instance = obj.AddComponent<ModelLoadErrorDialog>();
                              DontDestroyOnLoad(obj);
                        }
                  }
                  return _instance;
            }
      }

      private void Awake()
      {
            // Инициализация синглтона
            if (_instance == null)
            {
                  _instance = this;
                  DontDestroyOnLoad(gameObject);
            }
            else if (_instance != this)
            {
                  Destroy(gameObject);
                  return;
            }

            // Начальная настройка
            if (dialogPanel != null)
            {
                  dialogPanel.SetActive(false);
            }

            // Настройка кнопки закрытия
            if (closeButton != null)
            {
                  closeButton.onClick.AddListener(CloseDialog);
            }
      }

      private void Start()
      {
            // Если компоненты не заданы в инспекторе, пытаемся найти их автоматически
            if (dialogPanel == null)
            {
                  dialogPanel = transform.Find("DialogPanel")?.gameObject;
                  if (dialogPanel == null && transform.childCount > 0)
                  {
                        dialogPanel = transform.GetChild(0).gameObject;
                  }
            }

            if (titleText == null && dialogPanel != null)
            {
                  titleText = dialogPanel.transform.Find("TitleText")?.GetComponent<Text>();
            }

            if (errorMessageText == null && dialogPanel != null)
            {
                  errorMessageText = dialogPanel.transform.Find("ErrorMessageText")?.GetComponent<Text>();
            }

            if (modelInfoText == null && dialogPanel != null)
            {
                  modelInfoText = dialogPanel.transform.Find("ModelInfoText")?.GetComponent<Text>();
            }

            if (recommendationText == null && dialogPanel != null)
            {
                  recommendationText = dialogPanel.transform.Find("RecommendationText")?.GetComponent<Text>();
            }

            if (closeButton == null && dialogPanel != null)
            {
                  closeButton = dialogPanel.transform.Find("CloseButton")?.GetComponent<Button>();
                  if (closeButton != null)
                  {
                        closeButton.onClick.AddListener(CloseDialog);
                  }
            }

            // Скрываем диалог при старте
            if (dialogPanel != null)
            {
                  dialogPanel.SetActive(false);
            }
      }

      /// <summary>
      /// Показывает сообщение об ошибке загрузки модели с информацией о модели
      /// </summary>
      /// <param name="modelInfo">Информация о модели и ошибке</param>
      public void ShowModelLoadError(ModelErrorInfo modelInfo)
      {
            if (modelInfo == null)
            {
                  Debug.LogError("ModelInfo не может быть null");
                  return;
            }

            // Устанавливаем тексты
            if (titleText != null)
            {
                  titleText.text = defaultTitle;
            }

            if (errorMessageText != null)
            {
                  errorMessageText.text = modelInfo.errorMessage;
            }

            if (modelInfoText != null)
            {
                  modelInfoText.text = $"Модель: {modelInfo.modelName}\nТип: {modelInfo.modelType}";
            }

            if (recommendationText != null)
            {
                  recommendationText.text = string.IsNullOrEmpty(modelInfo.recommendation)
                      ? defaultRecommendation
                      : modelInfo.recommendation;
            }

            // Показываем диалог
            if (dialogPanel != null)
            {
                  dialogPanel.SetActive(true);
            }
            else
            {
                  // Если нет UI, используем OnGUI
                  StartCoroutine(ShowFallbackErrorDialog(modelInfo));
            }
      }

      /// <summary>
      /// Запасной вариант отображения ошибки с помощью OnGUI
      /// </summary>
      private IEnumerator ShowFallbackErrorDialog(ModelErrorInfo modelInfo)
      {
            // Флаг для отслеживания состояния диалога
            bool isDialogOpen = true;
            ModelErrorInfo currentInfo = modelInfo;

            // Сохраняем кешированную версию OnGUI делегата
            System.Action<ModelErrorInfo, bool> drawDialogAction = DrawErrorDialog;

            // Включаем отображение
            showGUIDialog = true;
            errorDialogInfo = currentInfo;

            // Ожидаем закрытия диалога
            while (showGUIDialog && isDialogOpen)
            {
                  yield return null;
            }

            // Выключаем отображение
            showGUIDialog = false;
            errorDialogInfo = null;
      }

      // Флаги и данные для OnGUI отображения
      private bool showGUIDialog = false;
      private ModelErrorInfo errorDialogInfo = null;

      private void OnGUI()
      {
            if (showGUIDialog && errorDialogInfo != null)
            {
                  DrawErrorDialog(errorDialogInfo, true);
            }
      }

      private void DrawErrorDialog(ModelErrorInfo info, bool showCloseButton)
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
            GUI.Box(new Rect(x, y, windowWidth, windowHeight), defaultTitle, windowStyle);

            // Отображаем сообщение об ошибке
            GUI.Label(new Rect(x + 20, y + 40, windowWidth - 40, 60), info.errorMessage, textStyle);

            // Информация о модели
            GUI.Label(new Rect(x + 20, y + 100, windowWidth - 40, 40),
                  $"Модель: {info.modelName}\nТип: {info.modelType}", textStyle);

            // Рекомендации
            GUI.Label(new Rect(x + 20, y + 150, windowWidth - 40, 100),
                  string.IsNullOrEmpty(info.recommendation) ? defaultRecommendation : info.recommendation, textStyle);

            // Кнопка OK
            if (showCloseButton && GUI.Button(new Rect(x + (windowWidth - 100) / 2, y + windowHeight - 50, 100, 30), "OK", buttonStyle))
            {
                  showGUIDialog = false;
            }
      }

      /// <summary>
      /// Закрывает диалоговое окно
      /// </summary>
      public void CloseDialog()
      {
            if (dialogPanel != null)
            {
                  dialogPanel.SetActive(false);
            }
      }

      /// <summary>
      /// Показывает простое сообщение об ошибке
      /// </summary>
      public void ShowError(string errorMessage)
      {
            ModelErrorInfo info = new ModelErrorInfo(
                "Неизвестная модель",
                "Неизвестный тип",
                errorMessage,
                defaultRecommendation);

            ShowModelLoadError(info);
      }
}