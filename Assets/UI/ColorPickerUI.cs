using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;

/// <summary>
/// Интерфейс для выбора цвета перекраски стен
/// </summary>
public class ColorPickerUI : MonoBehaviour
{
    [Header("Компоненты")]
    [SerializeField] private WallPaintEffect wallPaintEffect;
    [SerializeField] private Slider blendFactorSlider;
    [SerializeField] private Toggle useMaskToggle;
    
    [Header("Палитра цветов")]
    [SerializeField] private List<ColorButton> colorButtons = new List<ColorButton>();
    [SerializeField] private GameObject colorButtonPrefab;
    [SerializeField] private Transform colorButtonsContainer;
    
    [Header("Предустановленные цвета")]
    [SerializeField] private List<Color> predefinedColors = new List<Color>
    {
        new Color(0.9f, 0.9f, 0.9f), // Белый
        new Color(0.8f, 0.8f, 0.8f), // Светло-серый
        new Color(0.6f, 0.6f, 0.6f), // Серый
        new Color(0.95f, 0.95f, 0.8f), // Кремовый
        new Color(0.9f, 0.8f, 0.7f), // Бежевый
        new Color(0.95f, 0.87f, 0.7f), // Персиковый
        new Color(0.8f, 0.6f, 0.5f), // Терракотовый
        new Color(0.7f, 0.8f, 0.9f), // Голубой
        new Color(0.5f, 0.7f, 0.9f), // Синий
        new Color(0.6f, 0.9f, 0.6f), // Зеленый
        new Color(0.9f, 0.6f, 0.6f), // Розовый
        new Color(0.7f, 0.5f, 0.7f)  // Фиолетовый
    };
    
    private void Start()
    {
        // Если компонент WallPaintEffect не указан, пытаемся найти его в сцене
        if (wallPaintEffect == null)
        {
            wallPaintEffect = FindObjectOfType<WallPaintEffect>();
            if (wallPaintEffect == null)
            {
                Debug.LogError("WallPaintEffect не найден! Компонент ColorPickerUI не будет работать корректно.");
                return;
            }
        }
        
        // Настраиваем слайдер интенсивности, если он доступен
        if (blendFactorSlider != null)
        {
            // Установка начального значения из WallPaintEffect 
            // и настройка обработчика события
            blendFactorSlider.value = 0.5f; // Значение по умолчанию
            blendFactorSlider.onValueChanged.AddListener(OnBlendFactorChanged);
        }
        
        // Настраиваем переключатель использования маски
        if (useMaskToggle != null)
        {
            useMaskToggle.isOn = true; // Значение по умолчанию
            useMaskToggle.onValueChanged.AddListener(OnUseMaskChanged);
        }
        
        // Создаём кнопки цветов из предустановленных
        if (colorButtonPrefab != null && colorButtonsContainer != null)
        {
            CreateColorButtons();
        }
    }
    
    private void CreateColorButtons()
    {
        // Очищаем существующие кнопки, если они есть
        foreach (Transform child in colorButtonsContainer)
        {
            Destroy(child.gameObject);
        }
        colorButtons.Clear();
        
        // Создаём новые кнопки для каждого предустановленного цвета
        foreach (Color color in predefinedColors)
        {
            GameObject buttonObj = Instantiate(colorButtonPrefab, colorButtonsContainer);
            ColorButton colorButton = buttonObj.GetComponent<ColorButton>();
            
            if (colorButton != null)
            {
                colorButton.SetColor(color);
                colorButton.OnColorSelected += SelectColor;
                colorButtons.Add(colorButton);
            }
        }
    }
    
    public void SelectColor(Color color)
    {
        if (wallPaintEffect != null)
        {
            wallPaintEffect.SetPaintColor(color);
        }
    }
    
    private void OnBlendFactorChanged(float value)
    {
        if (wallPaintEffect != null)
        {
            wallPaintEffect.SetBlendFactor(value);
        }
    }
    
    private void OnUseMaskChanged(bool isOn)
    {
        if (wallPaintEffect != null)
        {
            wallPaintEffect.SetUseMask(isOn);
        }
    }
    
    private void OnDestroy()
    {
        // Отписываемся от всех событий
        if (blendFactorSlider != null)
        {
            blendFactorSlider.onValueChanged.RemoveListener(OnBlendFactorChanged);
        }
        
        if (useMaskToggle != null)
        {
            useMaskToggle.onValueChanged.RemoveListener(OnUseMaskChanged);
        }
        
        foreach (ColorButton button in colorButtons)
        {
            if (button != null)
            {
                button.OnColorSelected -= SelectColor;
            }
        }
    }
} 