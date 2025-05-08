using UnityEngine;
using UnityEngine.UI;
using System;

/// <summary>
/// Представляет отдельную кнопку цвета в палитре
/// </summary>
public class ColorButton : MonoBehaviour
{
    [SerializeField] private Image colorImage;
    [SerializeField] private Button button;
    [SerializeField] private Image selectionOutline;
    
    private Color color;
    public event Action<Color> OnColorSelected;
    
    private void Awake()
    {
        // Если компоненты не назначены, пытаемся найти их
        if (colorImage == null)
            colorImage = GetComponent<Image>();
        
        if (button == null)
            button = GetComponent<Button>();
        
        // Настраиваем обработчик нажатия
        if (button != null)
            button.onClick.AddListener(OnButtonClicked);
    }
    
    /// <summary>
    /// Устанавливает цвет кнопки
    /// </summary>
    public void SetColor(Color newColor)
    {
        color = newColor;
        if (colorImage != null)
        {
            colorImage.color = color;
        }
    }
    
    /// <summary>
    /// Обработчик нажатия на кнопку
    /// </summary>
    private void OnButtonClicked()
    {
        OnColorSelected?.Invoke(color);
        
        // Можно добавить визуальное выделение выбранной кнопки
        if (selectionOutline != null)
        {
            selectionOutline.enabled = true;
        }
    }
    
    /// <summary>
    /// Сбрасывает выделение кнопки
    /// </summary>
    public void ResetSelection()
    {
        if (selectionOutline != null)
        {
            selectionOutline.enabled = false;
        }
    }
    
    private void OnDestroy()
    {
        // Отписываемся от событий
        if (button != null)
        {
            button.onClick.RemoveListener(OnButtonClicked);
        }
    }
} 