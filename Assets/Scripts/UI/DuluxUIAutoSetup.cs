using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Автоматический установщик UI для Dulux Visualizer
/// Создает весь необходимый UI одним кликом
/// </summary>
public class DuluxUIAutoSetup : MonoBehaviour
{
      [Header("Настройки UI")]
      [SerializeField] private bool createCanvasIfNotExists = true;
      [SerializeField] private bool autoFindComponents = true;

      [ContextMenu("🎨 Создать Dulux UI")]
      public void CreateDuluxUI()
      {
            Debug.Log("[DuluxUIAutoSetup] 🚀 Начинаю создание Dulux UI...");

            // 1. Найти или создать Canvas
            Canvas canvas = FindOrCreateCanvas();

            // 2. Создать основную панель
            GameObject mainPanel = CreateMainPanel(canvas);

            // 3. Создать ColorPicker UI
            ColorPickerUI colorPickerUI = CreateColorPickerUI(mainPanel);

            // 4. Создать WallPaintingSystem
            WallPaintingSystem wallPaintingSystem = CreateWallPaintingSystem();

            // 5. Связать компоненты
            LinkComponents(colorPickerUI, wallPaintingSystem);

            Debug.Log("[DuluxUIAutoSetup] ✅ Dulux UI успешно создан!");
            Debug.Log("[DuluxUIAutoSetup] 📱 Нажмите кнопку '🎨 Цвета' чтобы открыть палитру");
      }

      private Canvas FindOrCreateCanvas()
      {
            Canvas canvas = FindObjectOfType<Canvas>();

            if (canvas == null && createCanvasIfNotExists)
            {
                  GameObject canvasObj = new GameObject("DuluxCanvas");
                  canvas = canvasObj.AddComponent<Canvas>();
                  canvas.renderMode = RenderMode.ScreenSpaceOverlay;

                  // Добавляем CanvasScaler
                  CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
                  scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                  scaler.referenceResolution = new Vector2(1920, 1080);

                  // Добавляем GraphicRaycaster
                  canvasObj.AddComponent<GraphicRaycaster>();

                  Debug.Log("[DuluxUIAutoSetup] ✅ Canvas создан");
            }

            return canvas;
      }

      private GameObject CreateMainPanel(Canvas canvas)
      {
            GameObject mainPanel = new GameObject("DuluxMainPanel");
            mainPanel.transform.SetParent(canvas.transform, false);

            // Добавляем RectTransform
            RectTransform rectTransform = mainPanel.AddComponent<RectTransform>();
            rectTransform.anchorMin = Vector2.zero;
            rectTransform.anchorMax = Vector2.one;
            rectTransform.offsetMin = Vector2.zero;
            rectTransform.offsetMax = Vector2.zero;

            return mainPanel;
      }

      private ColorPickerUI CreateColorPickerUI(GameObject parent)
      {
            // Создаем главный объект ColorPickerUI
            GameObject colorPickerObj = new GameObject("ColorPickerUI");
            colorPickerObj.transform.SetParent(parent.transform, false);

            ColorPickerUI colorPickerUI = colorPickerObj.AddComponent<ColorPickerUI>();

            // Создаем кнопку переключения (в правом нижнем углу)
            GameObject toggleButton = CreateToggleButton(colorPickerObj);

            // Создаем панель с цветами (изначально скрыта)
            GameObject colorPanel = CreateColorPanel(colorPickerObj);

            // Создаем контейнер для кнопок цветов
            GameObject buttonsContainer = CreateButtonsContainer(colorPanel);

            // Создаем текст выбранного цвета
            GameObject selectedText = CreateSelectedColorText(colorPanel);

            // Создаем кнопки управления
            GameObject applyButton = CreateActionButton(colorPanel, "Применить", "ApplyButton");
            GameObject resetButton = CreateActionButton(colorPanel, "Сброс", "ResetButton");

            // Связываем компоненты через Reflection (поскольку поля private)
            SetPrivateField(colorPickerUI, "toggleColorPickerButton", toggleButton.GetComponent<Button>());
            SetPrivateField(colorPickerUI, "colorPickerPanel", colorPanel);
            SetPrivateField(colorPickerUI, "colorButtonsContainer", buttonsContainer.transform);
            SetPrivateField(colorPickerUI, "applyColorButton", applyButton.GetComponent<Button>());
            SetPrivateField(colorPickerUI, "resetColorButton", resetButton.GetComponent<Button>());
            SetPrivateField(colorPickerUI, "selectedColorText", selectedText.GetComponent<TextMeshProUGUI>());

            // Создаем простой префаб кнопки программно (без .prefab файла)
            GameObject buttonPrefab = CreateSimpleButtonPrefab();
            SetPrivateField(colorPickerUI, "colorButtonPrefab", buttonPrefab);

            Debug.Log("[DuluxUIAutoSetup] ✅ ColorPickerUI создан");
            return colorPickerUI;
      }

      private GameObject CreateToggleButton(GameObject parent)
      {
            GameObject button = CreateUIButton(parent, "🎨 Цвета", "ToggleColorPickerButton");

            // Позиционируем в правом нижнем углу
            RectTransform rectTransform = button.GetComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(1, 0);
            rectTransform.anchorMax = new Vector2(1, 0);
            rectTransform.anchoredPosition = new Vector2(-70, 70);
            rectTransform.sizeDelta = new Vector2(120, 50);

            return button;
      }

      private GameObject CreateColorPanel(GameObject parent)
      {
            GameObject panel = new GameObject("ColorPickerPanel");
            panel.transform.SetParent(parent.transform, false);

            // Добавляем Image для фона
            Image image = panel.AddComponent<Image>();
            image.color = new Color(0.1f, 0.1f, 0.1f, 0.9f);

            // Позиционируем по центру
            RectTransform rectTransform = panel.GetComponent<RectTransform>();
            rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            rectTransform.anchoredPosition = Vector2.zero;
            rectTransform.sizeDelta = new Vector2(400, 300);

            // Добавляем VerticalLayoutGroup
            VerticalLayoutGroup layout = panel.AddComponent<VerticalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 20;
            layout.padding = new RectOffset(20, 20, 20, 20);

            // Изначально скрываем панель
            panel.SetActive(false);

            return panel;
      }

      private GameObject CreateButtonsContainer(GameObject parent)
      {
            GameObject container = new GameObject("ColorButtonsContainer");
            container.transform.SetParent(parent.transform, false);

            // Добавляем HorizontalLayoutGroup
            HorizontalLayoutGroup layout = container.AddComponent<HorizontalLayoutGroup>();
            layout.childAlignment = TextAnchor.MiddleCenter;
            layout.spacing = 10;
            layout.childForceExpandWidth = false;
            layout.childForceExpandHeight = false;

            // Добавляем ContentSizeFitter
            ContentSizeFitter fitter = container.AddComponent<ContentSizeFitter>();
            fitter.horizontalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            return container;
      }

      private GameObject CreateSelectedColorText(GameObject parent)
      {
            GameObject textObj = new GameObject("SelectedColorText");
            textObj.transform.SetParent(parent.transform, false);

            TextMeshProUGUI text = textObj.AddComponent<TextMeshProUGUI>();
            text.text = "Выбран: Белый";
            text.fontSize = 18;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Center;

            return textObj;
      }

      private GameObject CreateActionButton(GameObject parent, string buttonText, string name)
      {
            GameObject button = CreateUIButton(parent, buttonText, name);

            // Настраиваем размер
            RectTransform rectTransform = button.GetComponent<RectTransform>();
            rectTransform.sizeDelta = new Vector2(120, 40);

            return button;
      }

      private GameObject CreateSimpleButtonPrefab()
      {
            // Создаем простую кнопку программно без префаба
            GameObject buttonObj = new GameObject("SimpleColorButton");

            // Добавляем RectTransform
            RectTransform rectTransform = buttonObj.AddComponent<RectTransform>();
            rectTransform.sizeDelta = new Vector2(50, 50);

            // Добавляем Image
            Image image = buttonObj.AddComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 1f);

            // Добавляем Button
            Button button = buttonObj.AddComponent<Button>();
            button.targetGraphic = image;

            Debug.Log("[DuluxUIAutoSetup] ✅ Простой префаб кнопки создан программно");
            return buttonObj;
      }

      private GameObject CreateUIButton(GameObject parent, string text, string name)
      {
            GameObject buttonObj = new GameObject(name);
            buttonObj.transform.SetParent(parent.transform, false);

            // Добавляем Image
            Image image = buttonObj.AddComponent<Image>();
            image.color = new Color(0.2f, 0.6f, 1f, 1f);

            // Добавляем Button
            Button button = buttonObj.AddComponent<Button>();
            button.targetGraphic = image;

            // Создаем текст кнопки
            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(buttonObj.transform, false);

            TextMeshProUGUI textComponent = textObj.AddComponent<TextMeshProUGUI>();
            textComponent.text = text;
            textComponent.fontSize = 16;
            textComponent.color = Color.white;
            textComponent.alignment = TextAlignmentOptions.Center;

            // Позиционируем текст
            RectTransform textRect = textObj.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            return buttonObj;
      }

      private WallPaintingSystem CreateWallPaintingSystem()
      {
            GameObject wallPaintingObj = new GameObject("WallPaintingSystem");
            WallPaintingSystem wallPaintingSystem = wallPaintingObj.AddComponent<WallPaintingSystem>();

            Debug.Log("[DuluxUIAutoSetup] ✅ WallPaintingSystem создан");
            return wallPaintingSystem;
      }

      private void LinkComponents(ColorPickerUI colorPickerUI, WallPaintingSystem wallPaintingSystem)
      {
            if (autoFindComponents)
            {
                  // Связываем WallPaintingSystem с ColorPickerUI
                  SetPrivateField(wallPaintingSystem, "colorPickerUI", colorPickerUI);

                  // Ищем ARManagerInitializer2
                  ARManagerInitializer2 arManager = FindObjectOfType<ARManagerInitializer2>();
                  if (arManager != null)
                  {
                        SetPrivateField(wallPaintingSystem, "arManager", arManager);
                        Debug.Log("[DuluxUIAutoSetup] ✅ ARManagerInitializer2 найден и связан");
                  }
                  else
                  {
                        Debug.LogWarning("[DuluxUIAutoSetup] ⚠️ ARManagerInitializer2 не найден. Установите вручную в Inspector.");
                  }
            }

            Debug.Log("[DuluxUIAutoSetup] ✅ Компоненты связаны");
      }

      private void SetPrivateField(object obj, string fieldName, object value)
      {
            var field = obj.GetType().GetField(fieldName,
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Instance);

            if (field != null)
            {
                  field.SetValue(obj, value);
            }
            else
            {
                  Debug.LogWarning($"[DuluxUIAutoSetup] ⚠️ Поле '{fieldName}' не найдено в {obj.GetType().Name}");
            }
      }

      [ContextMenu("🧹 Удалить Dulux UI")]
      public void CleanupDuluxUI()
      {
            // Находим и удаляем созданные объекты
            GameObject[] objectsToDelete = {
            GameObject.Find("DuluxCanvas"),
            GameObject.Find("DuluxMainPanel"),
            GameObject.Find("WallPaintingSystem")
        };

            foreach (GameObject obj in objectsToDelete)
            {
                  if (obj != null)
                  {
                        DestroyImmediate(obj);
                  }
            }

            Debug.Log("[DuluxUIAutoSetup] 🧹 Dulux UI удален");
      }
}