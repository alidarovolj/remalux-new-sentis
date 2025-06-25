using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro;

/// <summary>
/// UI система для выбора цветов покраски стен (аналог Dulux Visualizer)
/// </summary>
public class ColorPickerUI : MonoBehaviour
{
      [Header("UI Компоненты")]
      [SerializeField] private GameObject colorPickerPanel;
      [SerializeField] private Button toggleColorPickerButton;
      [SerializeField] private Button applyColorButton;
      [SerializeField] private Button resetColorButton;
      [SerializeField] private TextMeshProUGUI selectedColorText;

      [Header("Цветовая палитра")]
      [SerializeField] private Transform colorButtonsContainer;
      [SerializeField] private GameObject colorButtonPrefab; // Используйте Assets/Prefabs/UI/SimpleColorButton.prefab

      [Header("Предустановленные цвета")]
      [SerializeField]
      private ColorPreset[] colorPresets = new ColorPreset[]
      {
        new ColorPreset("Белый", Color.white),
        new ColorPreset("Светло-серый", new Color(0.9f, 0.9f, 0.9f)),
        new ColorPreset("Кремовый", new Color(1f, 0.98f, 0.9f)),
        new ColorPreset("Бежевый", new Color(0.96f, 0.96f, 0.86f)),
        new ColorPreset("Светло-голубой", new Color(0.8f, 0.9f, 1f)),
        new ColorPreset("Мятный", new Color(0.8f, 1f, 0.9f)),
        new ColorPreset("Персиковый", new Color(1f, 0.9f, 0.8f)),
        new ColorPreset("Лавандовый", new Color(0.9f, 0.8f, 1f)),
        new ColorPreset("Светло-желтый", new Color(1f, 1f, 0.8f)),
        new ColorPreset("Розовый", new Color(1f, 0.8f, 0.9f)),
        new ColorPreset("Серый", new Color(0.7f, 0.7f, 0.7f)),
        new ColorPreset("Темно-серый", new Color(0.5f, 0.5f, 0.5f))
      };

      [Header("Компоненты системы")]
      [SerializeField] private ARManagerInitializer2 arManager;
      [SerializeField] private WallPaintingSystem wallPaintingSystem;

      // Текущий выбранный цвет
      private Color currentSelectedColor = Color.white;
      private string currentColorName = "Белый";

      // События
      public System.Action<Color, string> OnColorSelected;
      public System.Action<Color> OnColorApplied;

      [System.Serializable]
      public class ColorPreset
      {
            public string name;
            public Color color;

            public ColorPreset(string name, Color color)
            {
                  this.name = name;
                  this.color = color;
            }
      }

      private void Start()
      {
            InitializeUI();
            CreateColorButtons();

            // Устанавливаем начальный цвет
            SelectColor(colorPresets[0].color, colorPresets[0].name);
      }

      private void InitializeUI()
      {
            // Настраиваем кнопки
            if (toggleColorPickerButton != null)
            {
                  toggleColorPickerButton.onClick.AddListener(ToggleColorPicker);
            }

            if (applyColorButton != null)
            {
                  applyColorButton.onClick.AddListener(ApplySelectedColor);
            }

            if (resetColorButton != null)
            {
                  resetColorButton.onClick.AddListener(ResetToDefaultColor);
            }

            // Изначально панель скрыта
            if (colorPickerPanel != null)
            {
                  colorPickerPanel.SetActive(false);
            }

            // Поиск компонентов если не назначены
            if (arManager == null)
            {
                  arManager = FindObjectOfType<ARManagerInitializer2>();
            }

            if (wallPaintingSystem == null)
            {
                  wallPaintingSystem = FindObjectOfType<WallPaintingSystem>();
            }

            Debug.Log("[ColorPickerUI] ✅ UI инициализирован");
      }

      private void CreateColorButtons()
      {
            if (colorButtonsContainer == null || colorButtonPrefab == null)
            {
                  Debug.LogError("[ColorPickerUI] Не настроены контейнер или префаб для цветовых кнопок!");
                  return;
            }

            foreach (var preset in colorPresets)
            {
                  GameObject buttonObj = Instantiate(colorButtonPrefab, colorButtonsContainer);
                  Button button = buttonObj.GetComponent<Button>();
                  Image buttonImage = buttonObj.GetComponent<Image>();

                  if (button == null || buttonImage == null)
                  {
                        Debug.LogError($"[ColorPickerUI] Префаб кнопки должен содержать Button и Image компоненты!");
                        continue;
                  }

                  // Настраиваем внешний вид кнопки
                  buttonImage.color = preset.color;

                  // Добавляем обводку для контраста
                  Outline outline = buttonObj.GetComponent<Outline>();
                  if (outline == null)
                  {
                        outline = buttonObj.AddComponent<Outline>();
                  }
                  outline.effectColor = Color.black;
                  outline.effectDistance = new Vector2(2, 2);

                  // Настраиваем действие кнопки
                  string colorName = preset.name;
                  Color color = preset.color;
                  button.onClick.AddListener(() => SelectColor(color, colorName));

                  // Простые кнопки без текста - цвет говорит сам за себя
                  // TextMeshProUGUI buttonText = buttonObj.GetComponentInChildren<TextMeshProUGUI>();
                  // if (buttonText != null)
                  // {
                  //     buttonText.text = preset.name;
                  //     buttonText.color = GetContrastColor(preset.color);
                  //     buttonText.fontSize = 10;
                  // }
            }

            Debug.Log($"[ColorPickerUI] ✅ Создано {colorPresets.Length} цветовых кнопок");
      }

      private Color GetContrastColor(Color backgroundColor)
      {
            // Вычисляем яркость цвета и возвращаем черный или белый для контраста
            float brightness = (backgroundColor.r * 0.299f + backgroundColor.g * 0.587f + backgroundColor.b * 0.114f);
            return brightness > 0.5f ? Color.black : Color.white;
      }

      private void SelectColor(Color color, string colorName)
      {
            currentSelectedColor = color;
            currentColorName = colorName;

            // Обновляем UI
            if (selectedColorText != null)
            {
                  selectedColorText.text = $"Выбран: {colorName}";
                  selectedColorText.color = GetContrastColor(Color.white);
            }

            // Вызываем событие
            OnColorSelected?.Invoke(color, colorName);

            Debug.Log($"[ColorPickerUI] 🎨 Выбран цвет: {colorName} ({color})");
      }

      private void ToggleColorPicker()
      {
            if (colorPickerPanel != null)
            {
                  bool isActive = colorPickerPanel.activeSelf;
                  colorPickerPanel.SetActive(!isActive);

                  Debug.Log($"[ColorPickerUI] 🎨 Палитра цветов {(!isActive ? "открыта" : "закрыта")}");
            }
      }

      private void ApplySelectedColor()
      {
            if (arManager == null)
            {
                  Debug.LogError("[ColorPickerUI] ARManagerInitializer2 не найден!");
                  return;
            }

            // Применяем цвет ко всем существующим плоскостям
            ApplyColorToAllPlanes(currentSelectedColor);

            // Вызываем событие
            OnColorApplied?.Invoke(currentSelectedColor);

            Debug.Log($"[ColorPickerUI] ✅ Цвет '{currentColorName}' применен ко всем стенам");

            // Автоматически закрываем палитру после применения
            if (colorPickerPanel != null)
            {
                  colorPickerPanel.SetActive(false);
            }
      }

      private void ApplyColorToAllPlanes(Color color)
      {
            if (arManager?.GeneratedPlanes == null) return;

            int appliedCount = 0;

            foreach (GameObject plane in arManager.GeneratedPlanes)
            {
                  if (plane == null) continue;

                  MeshRenderer renderer = plane.GetComponent<MeshRenderer>();
                  if (renderer != null)
                  {
                        // Создаем новый материал на основе существующего
                        Material newMaterial = new Material(renderer.material);
                        newMaterial.color = color;

                        // Если материал прозрачный, сохраняем альфа
                        if (newMaterial.HasProperty("_Alpha"))
                        {
                              Color materialColor = color;
                              materialColor.a = newMaterial.GetFloat("_Alpha");
                              newMaterial.color = materialColor;
                        }

                        renderer.material = newMaterial;
                        appliedCount++;
                  }
            }

            Debug.Log($"[ColorPickerUI] 🎨 Цвет применен к {appliedCount} плоскостям");
      }

      private void ResetToDefaultColor()
      {
            SelectColor(Color.white, "Белый");
            ApplySelectedColor();
            Debug.Log("[ColorPickerUI] 🔄 Сброшен к белому цвету");
      }

      // Публичные методы для внешнего использования
      public void SetCurrentColor(Color color, string colorName = "Пользовательский")
      {
            SelectColor(color, colorName);
      }

      public Color GetCurrentColor()
      {
            return currentSelectedColor;
      }

      public string GetCurrentColorName()
      {
            return currentColorName;
      }

      private void OnDestroy()
      {
            // Очищаем слушатели событий
            if (toggleColorPickerButton != null)
            {
                  toggleColorPickerButton.onClick.RemoveAllListeners();
            }
            if (applyColorButton != null)
            {
                  applyColorButton.onClick.RemoveAllListeners();
            }
            if (resetColorButton != null)
            {
                  resetColorButton.onClick.RemoveAllListeners();
            }
      }
}