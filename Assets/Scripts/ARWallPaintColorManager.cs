using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// Менеджер цветов краски для AR Wall Painting System
/// Управляет выбором цвета и применением к AR плоскостям в реальном времени
/// Приоритет 2: Реалистичное нанесение краски
/// </summary>
public class ARWallPaintColorManager : MonoBehaviour
{
      [Header("Цвета Краски")]
      [SerializeField] private Color currentPaintColor = new Color(0.8f, 0.4f, 0.2f, 0.6f);
      [SerializeField] private float blendFactor = 0.7f;

      [Header("Предустановленные Цвета")]
      [SerializeField]
      private Color[] presetColors = {
        new Color(0.8f, 0.4f, 0.2f, 0.6f), // Коричневый
        new Color(0.2f, 0.6f, 0.9f, 0.6f), // Синий
        new Color(0.9f, 0.2f, 0.3f, 0.6f), // Красный
        new Color(0.3f, 0.8f, 0.3f, 0.6f), // Зеленый
        new Color(0.9f, 0.8f, 0.2f, 0.6f), // Желтый
        new Color(0.7f, 0.3f, 0.8f, 0.6f), // Фиолетовый
        new Color(0.9f, 0.5f, 0.1f, 0.6f), // Оранжевый
        new Color(0.2f, 0.2f, 0.2f, 0.6f), // Темно-серый
    };

      [Header("UI Элементы")]
      [SerializeField] private Button[] colorButtons;
      [SerializeField] private Slider transparencySlider;
      [SerializeField] private Image currentColorPreview;
      [SerializeField] private Text colorInfoText;

      [Header("Связанные Системы")]
      [SerializeField] private ARManagerInitializer2 arManagerInitializer;

      // События
      public System.Action<Color, float> OnColorChanged;

      private void Start()
      {
            InitializeColorManager();
            SetupUI();

            // Автоматически найдем ARManagerInitializer2 если не назначен
            if (arManagerInitializer == null)
            {
                  arManagerInitializer = FindObjectOfType<ARManagerInitializer2>();
            }

            // Применяем начальный цвет
            ApplyColorToAllPlanes();
      }

      private void InitializeColorManager()
      {
            Debug.Log("[ARWallPaintColorManager] Инициализация системы управления цветами краски");

            // Убеждаемся что у нас есть все необходимые цвета
            if (presetColors.Length == 0)
            {
                  presetColors = new Color[] { currentPaintColor };
            }
      }

      private void SetupUI()
      {
            // Настраиваем кнопки цветов
            for (int i = 0; i < colorButtons.Length && i < presetColors.Length; i++)
            {
                  var buttonIndex = i; // Замыкание для корректной работы
                  var button = colorButtons[i];
                  var color = presetColors[i];

                  // Устанавливаем цвет кнопки
                  var buttonImage = button.GetComponent<Image>();
                  if (buttonImage != null)
                  {
                        buttonImage.color = new Color(color.r, color.g, color.b, 1.0f); // Убираем прозрачность для UI
                  }

                  // Добавляем обработчик нажатия
                  button.onClick.AddListener(() => SelectColor(buttonIndex));
            }

            // Настраиваем слайдер прозрачности
            if (transparencySlider != null)
            {
                  transparencySlider.value = blendFactor;
                  transparencySlider.onValueChanged.AddListener(OnTransparencyChanged);
            }

            // Обновляем превью цвета
            UpdateColorPreview();
      }

      public void SelectColor(int colorIndex)
      {
            if (colorIndex >= 0 && colorIndex < presetColors.Length)
            {
                  currentPaintColor = presetColors[colorIndex];
                  // Сохраняем текущую прозрачность
                  currentPaintColor.a = blendFactor;

                  UpdateColorPreview();
                  ApplyColorToAllPlanes();

                  Debug.Log($"[ARWallPaintColorManager] 🎨 Выбран цвет: {currentPaintColor} (индекс {colorIndex})");
            }
      }

      public void OnTransparencyChanged(float newTransparency)
      {
            blendFactor = newTransparency;
            currentPaintColor.a = blendFactor;

            UpdateColorPreview();
            ApplyColorToAllPlanes();

            Debug.Log($"[ARWallPaintColorManager] 🔧 Изменена прозрачность: {blendFactor:F2}");
      }

      private void UpdateColorPreview()
      {
            if (currentColorPreview != null)
            {
                  currentColorPreview.color = new Color(currentPaintColor.r, currentPaintColor.g, currentPaintColor.b, 1.0f);
            }

            if (colorInfoText != null)
            {
                  colorInfoText.text = $"RGB: {(int)(currentPaintColor.r * 255)}, {(int)(currentPaintColor.g * 255)}, {(int)(currentPaintColor.b * 255)}\nПрозрачность: {(blendFactor * 100):F0}%";
            }
      }

      private void ApplyColorToAllPlanes()
      {
            if (arManagerInitializer == null) return;

            // Получаем все созданные плоскости
            var generatedPlanes = GetGeneratedPlanes();
            int updatedCount = 0;

            foreach (GameObject plane in generatedPlanes)
            {
                  if (plane == null) continue;

                  MeshRenderer renderer = plane.GetComponent<MeshRenderer>();
                  if (renderer != null && renderer.material != null)
                  {
                        Material material = renderer.material;

                        // Обновляем параметры краски
                        if (material.HasProperty("_PaintColor"))
                        {
                              material.SetColor("_PaintColor", currentPaintColor);
                        }

                        if (material.HasProperty("_BlendFactor"))
                        {
                              material.SetFloat("_BlendFactor", blendFactor);
                        }

                        updatedCount++;
                  }
            }

            // Уведомляем подписчиков об изменении цвета
            OnColorChanged?.Invoke(currentPaintColor, blendFactor);

            if (updatedCount > 0)
            {
                  Debug.Log($"[ARWallPaintColorManager] 🎨 Цвет применен к {updatedCount} плоскостям");
            }
      }

      private List<GameObject> GetGeneratedPlanes()
      {
            // Используем рефлексию для доступа к приватному полю generatedPlanes
            var field = typeof(ARManagerInitializer2).GetField("generatedPlanes",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);

            if (field != null && arManagerInitializer != null)
            {
                  return (List<GameObject>)field.GetValue(arManagerInitializer);
            }

            return new List<GameObject>();
      }

      // Публичные методы для внешнего управления
      public void SetColor(Color newColor)
      {
            currentPaintColor = newColor;
            UpdateColorPreview();
            ApplyColorToAllPlanes();
      }

      public void SetTransparency(float newTransparency)
      {
            blendFactor = Mathf.Clamp01(newTransparency);
            currentPaintColor.a = blendFactor;

            if (transparencySlider != null)
            {
                  transparencySlider.value = blendFactor;
            }

            UpdateColorPreview();
            ApplyColorToAllPlanes();
      }

      public Color GetCurrentColor()
      {
            return currentPaintColor;
      }

      public float GetCurrentTransparency()
      {
            return blendFactor;
      }

      // Context Menu методы для отладки
      [ContextMenu("Apply Red Paint")]
      public void ApplyRedPaint()
      {
            SetColor(new Color(0.9f, 0.2f, 0.2f, blendFactor));
      }

      [ContextMenu("Apply Blue Paint")]
      public void ApplyBluePaint()
      {
            SetColor(new Color(0.2f, 0.4f, 0.9f, blendFactor));
      }

      [ContextMenu("Apply Green Paint")]
      public void ApplyGreenPaint()
      {
            SetColor(new Color(0.2f, 0.8f, 0.3f, blendFactor));
      }

      [ContextMenu("Increase Transparency")]
      public void IncreaseTransparency()
      {
            SetTransparency(blendFactor + 0.1f);
      }

      [ContextMenu("Decrease Transparency")]
      public void DecreaseTransparency()
      {
            SetTransparency(blendFactor - 0.1f);
      }
}