using UnityEngine;
using UnityEngine.Events;
using System.Collections.Generic;

/// <summary>
/// Компонент для обработки выбора цвета для стен в AR
/// </summary>
public class ColorPickTarget : MonoBehaviour
{
    [System.Serializable]
    public class ColorChangedEvent : UnityEvent<Color> { }

    public ColorChangedEvent onColorChanged = new ColorChangedEvent();

    [Header("Настройка цветов")]
    [SerializeField] private Color currentColor = new Color(1.0f, 0.5f, 0.2f, 0.5f);
    [SerializeField] private List<Color> recentColors = new List<Color>();
    [SerializeField] private int maxRecentColors = 10;

    private MeshRenderer meshRenderer;

    private void Awake()
    {
        meshRenderer = GetComponent<MeshRenderer>();
        if (meshRenderer == null)
        {
            Debug.LogError("[ColorPickTarget] Не найден MeshRenderer");
            return;
        }
        
        // Применяем начальный цвет
        ApplyColor(currentColor);
    }

    /// <summary>
    /// Изменить цвет плоскости
    /// </summary>
    public void SetColor(Color newColor)
    {
        // Сохраняем прозрачность
        newColor.a = currentColor.a;
        
        // Применяем цвет к материалу
        ApplyColor(newColor);
        
        // Сохраняем в список недавних цветов
        AddToRecentColors(newColor);
        
        // Оповещаем о смене цвета
        onColorChanged.Invoke(newColor);
        
        Debug.Log($"[ColorPickTarget] Цвет изменен на {newColor}");
    }

    /// <summary>
    /// Применяет цвет к материалу
    /// </summary>
    private void ApplyColor(Color color)
    {
        if (meshRenderer != null && meshRenderer.material != null)
        {
            currentColor = color;
            meshRenderer.material.color = color;
        }
    }

    /// <summary>
    /// Добавляет цвет в список недавних цветов
    /// </summary>
    private void AddToRecentColors(Color color)
    {
        // Удаляем этот цвет, если он уже есть в списке
        recentColors.Remove(color);
        
        // Добавляем цвет в начало списка
        recentColors.Insert(0, color);
        
        // Ограничиваем количество элементов
        if (recentColors.Count > maxRecentColors)
        {
            recentColors.RemoveAt(recentColors.Count - 1);
        }
    }

    /// <summary>
    /// Возвращает список недавних цветов
    /// </summary>
    public List<Color> GetRecentColors()
    {
        return recentColors;
    }

    /// <summary>
    /// Изменяет прозрачность материала
    /// </summary>
    public void SetAlpha(float alpha)
    {
        Color color = currentColor;
        color.a = Mathf.Clamp01(alpha);
        ApplyColor(color);
    }
} 