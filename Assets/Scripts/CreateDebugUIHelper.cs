using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Вспомогательный класс для создания UI для отладки AR-приложения
/// </summary>
public static class CreateDebugUIHelper
{
      /// <summary>
      /// Создает дебаг-текст на экране
      /// </summary>
      public static Text CreateDebugText(string name = "ARPlaneDebugText", bool addBackground = true)
      {
            // Проверяем наличие Canvas в сцене
            Canvas canvas = Object.FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                  // Создаем Canvas
                  GameObject canvasObj = new GameObject("DebugCanvas");
                  canvas = canvasObj.AddComponent<Canvas>();
                  canvas.renderMode = RenderMode.ScreenSpaceOverlay;

                  // Добавляем CanvasScaler
                  CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
                  scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                  scaler.referenceResolution = new Vector2(1080, 1920);

                  // Добавляем GraphicRaycaster
                  canvasObj.AddComponent<GraphicRaycaster>();
            }

            // Создаем объект для текста
            GameObject textObj = new GameObject(name);
            textObj.transform.SetParent(canvas.transform, false);

            // Настраиваем RectTransform
            RectTransform rectTransform = textObj.AddComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0, 1);
            rectTransform.anchorMax = new Vector2(1, 1);
            rectTransform.pivot = new Vector2(0.5f, 1);
            rectTransform.anchoredPosition = new Vector2(0, -10);
            rectTransform.sizeDelta = new Vector2(0, 200);

            // Добавляем и настраиваем Text
            Text debugText = textObj.AddComponent<Text>();
            debugText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            debugText.fontSize = 24;
            debugText.color = Color.white;
            debugText.alignment = TextAnchor.UpperLeft;
            debugText.text = "Debug Text";

            if (addBackground)
            {
                  // Добавляем фон для лучшей читаемости
                  GameObject bgObj = new GameObject("Background");
                  bgObj.transform.SetParent(textObj.transform, false);

                  RectTransform bgRect = bgObj.AddComponent<RectTransform>();
                  bgRect.anchorMin = Vector2.zero;
                  bgRect.anchorMax = Vector2.one;
                  bgRect.sizeDelta = Vector2.zero;

                  Image bgImage = bgObj.AddComponent<Image>();
                  bgImage.color = new Color(0, 0, 0, 0.5f);

                  // Перемещаем фон под текст в иерархии, чтобы текст был поверх
                  bgObj.transform.SetAsFirstSibling();
            }

            Debug.Log($"Создан UI-элемент для отладки: {name}");

            return debugText;
      }

      /// <summary>
      /// Создает кнопку на экране
      /// </summary>
      public static Button CreateButton(string text, Vector2 position, Vector2 size, System.Action onClick)
      {
            // Проверяем наличие Canvas в сцене
            Canvas canvas = Object.FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                  // Создаем Canvas
                  GameObject canvasObj = new GameObject("DebugCanvas");
                  canvas = canvasObj.AddComponent<Canvas>();
                  canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                  canvasObj.AddComponent<CanvasScaler>();
                  canvasObj.AddComponent<GraphicRaycaster>();
            }

            // Создаем кнопку
            GameObject buttonObj = new GameObject(text + "Button");
            buttonObj.transform.SetParent(canvas.transform, false);

            // Настраиваем RectTransform
            RectTransform rectTransform = buttonObj.AddComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0, 0);
            rectTransform.anchorMax = new Vector2(0, 0);
            rectTransform.pivot = new Vector2(0, 0);
            rectTransform.anchoredPosition = position;
            rectTransform.sizeDelta = size;

            // Добавляем изображение для фона кнопки
            Image image = buttonObj.AddComponent<Image>();
            image.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);

            // Добавляем компонент кнопки
            Button button = buttonObj.AddComponent<Button>();
            button.targetGraphic = image;

            // Добавляем текст кнопки
            GameObject textObj = new GameObject("ButtonText");
            textObj.transform.SetParent(buttonObj.transform, false);

            RectTransform textRectTransform = textObj.AddComponent<RectTransform>();
            textRectTransform.anchorMin = Vector2.zero;
            textRectTransform.anchorMax = Vector2.one;
            textRectTransform.sizeDelta = Vector2.zero;

            Text buttonText = textObj.AddComponent<Text>();
            buttonText.text = text;
            buttonText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            buttonText.fontSize = 18;
            buttonText.alignment = TextAnchor.MiddleCenter;
            buttonText.color = Color.white;

            // Добавляем обработчик нажатия
            if (onClick != null)
            {
                  button.onClick.AddListener(() => onClick());
            }

            Debug.Log($"Создана кнопка UI: {text}");

            return button;
      }
}