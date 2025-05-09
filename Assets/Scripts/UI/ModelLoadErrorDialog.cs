using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// Упрощенный компонент для отображения ошибок загрузки модели
/// </summary>
public class ModelLoadErrorDialog : MonoBehaviour
{
      // Текстовые сообщения по умолчанию
      private const string DEFAULT_TITLE = "Ошибка загрузки модели";
      private const string DEFAULT_REASONS = "- Модель слишком большая\n- Неподдерживаемый формат модели\n- Отсутствует Unity Sentis или другая ML библиотека";
      private const string DEFAULT_RECOMMENDATIONS = "- Используйте модели размером до 50MB\n- Убедитесь, что установлен пакет Unity Sentis\n- Проверьте формат модели (ONNX opset 7-15)";

      private static ModelLoadErrorDialog instance;
      private GUIStyle titleStyle;
      private GUIStyle textStyle;
      private GUIStyle buttonStyle;
      private Texture2D iconTexture;
      private bool isVisible = false;
      private string errorMessage = "";

      // Singleton доступ
      public static ModelLoadErrorDialog Instance
      {
            get
            {
                  if (instance == null)
                  {
                        GameObject obj = new GameObject("ModelLoadErrorDialog");
                        instance = obj.AddComponent<ModelLoadErrorDialog>();
                        DontDestroyOnLoad(obj);
                  }
                  return instance;
            }
      }

      private void Awake()
      {
            if (instance == null)
            {
                  instance = this;
                  DontDestroyOnLoad(gameObject);
                  InitStyles();
            }
            else if (instance != this)
            {
                  Destroy(gameObject);
            }
      }

      private void InitStyles()
      {
            // Создаем стили для GUI
            titleStyle = new GUIStyle();
            titleStyle.fontSize = 24;
            titleStyle.normal.textColor = Color.white;
            titleStyle.alignment = TextAnchor.MiddleCenter;
            titleStyle.fontStyle = FontStyle.Bold;

            textStyle = new GUIStyle();
            textStyle.fontSize = 18;
            textStyle.normal.textColor = Color.white;
            textStyle.alignment = TextAnchor.MiddleLeft;
            textStyle.wordWrap = true;

            buttonStyle = new GUIStyle(GUI.skin.button);
            buttonStyle.fontSize = 18;
            buttonStyle.normal.textColor = Color.white;

            // Создаем иконку
            iconTexture = ErrorIconProvider.GetErrorIcon().texture;
      }

      /// <summary>
      /// Показывает диалог ошибки
      /// </summary>
      /// <param name="message">Сообщение об ошибке</param>
      public void ShowError(string message)
      {
            errorMessage = message;
            isVisible = true;
      }

      /// <summary>
      /// Скрывает диалог ошибки
      /// </summary>
      public void HideError()
      {
            isVisible = false;
      }

      private void OnGUI()
      {
            if (!isVisible) return;

            // Размеры диалога
            float dialogWidth = 600;
            float dialogHeight = 500;

            // Позиция диалога по центру экрана
            float x = (Screen.width - dialogWidth) / 2;
            float y = (Screen.height - dialogHeight) / 2;

            // Рисуем фон диалога
            Rect dialogRect = new Rect(x, y, dialogWidth, dialogHeight);
            GUI.Box(dialogRect, "");
            GUI.color = new Color(0.1f, 0.1f, 0.1f, 0.95f);
            GUI.DrawTexture(dialogRect, Texture2D.whiteTexture);
            GUI.color = Color.white;

            // Рисуем иконку
            float iconSize = 80;
            Rect iconRect = new Rect(x + (dialogWidth - iconSize) / 2, y + 20, iconSize, iconSize);
            GUI.DrawTexture(iconRect, iconTexture);

            // Рисуем заголовок
            Rect titleRect = new Rect(x, y + 110, dialogWidth, 40);
            GUI.Label(titleRect, DEFAULT_TITLE, titleStyle);

            // Рисуем сообщение об ошибке
            Rect messageRect = new Rect(x + 20, y + 150, dialogWidth - 40, 60);
            GUI.Label(messageRect, errorMessage, textStyle);

            // Рисуем заголовок "Возможные причины"
            Rect reasonsTitleRect = new Rect(x + 20, y + 210, dialogWidth - 40, 30);
            titleStyle.fontSize = 20;
            GUI.Label(reasonsTitleRect, "Возможные причины:", titleStyle);

            // Рисуем список причин
            Rect reasonsRect = new Rect(x + 40, y + 240, dialogWidth - 80, 90);
            GUI.Label(reasonsRect, DEFAULT_REASONS, textStyle);

            // Рисуем заголовок "Рекомендации"
            Rect recTitleRect = new Rect(x + 20, y + 330, dialogWidth - 40, 30);
            GUI.Label(recTitleRect, "Рекомендации:", titleStyle);

            // Рисуем список рекомендаций
            Rect recRect = new Rect(x + 40, y + 360, dialogWidth - 80, 90);
            GUI.Label(recRect, DEFAULT_RECOMMENDATIONS, textStyle);

            // Рисуем кнопку OK
            Rect buttonRect = new Rect(x + (dialogWidth - 200) / 2, y + dialogHeight - 50, 200, 40);
            if (GUI.Button(buttonRect, "OK", buttonStyle))
            {
                  HideError();
            }
      }
}