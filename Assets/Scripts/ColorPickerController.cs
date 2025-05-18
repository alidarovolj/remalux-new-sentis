using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.Interaction.Toolkit.AR;
using Unity.XR.CoreUtils;

/// <summary>
/// Контроллер выбора цвета для AR стен в стиле Dulux Visualizer
/// </summary>
public class ColorPickerController : MonoBehaviour
{
    [Header("Настройки UI")]
    [SerializeField] private GameObject colorPickerPanel;
    [SerializeField] private Transform recentColorsContainer;
    [SerializeField] private GameObject colorButtonPrefab;
    [SerializeField] private int gridColumns = 6;
    
    [Header("Палитра цветов")]
    [SerializeField] private List<Color> predefinedColors = new List<Color>();
    
    [Header("AR компоненты")]
    [SerializeField] private float maxRaycastDistance = 10f;
    [SerializeField] private LayerMask wallLayerMask = -1;
    
    private ColorPickTarget currentTarget;
    private List<Color> recentColors = new List<Color>();
    private Camera arCamera;
    private XROrigin arSessionOrigin;

    private void Awake()
    {
        arSessionOrigin = FindObjectOfType<XROrigin>();
        if (arSessionOrigin != null)
        {
            arCamera = arSessionOrigin.Camera;
        }
        else
        {
            Debug.LogError("XROrigin не найден в сцене!");
        }

        // Инициализируем UI цветов
        InitializePredefinedColors();
        UpdateRecentColorsUI();
        
        // Скрываем панель выбора по умолчанию
        if (colorPickerPanel != null)
        {
            colorPickerPanel.SetActive(false);
        }
    }
    
    private void Update()
    {
        // Проверка касания для выбора стены
        if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
        {
            HandleTouch(Input.GetTouch(0).position);
        }
    }
    
    /// <summary>
    /// Обрабатывает касание экрана для выбора стены
    /// </summary>
    private void HandleTouch(Vector2 touchPosition)
    {
        // Проверяем, не касание ли это UI элементов
        if (UnityEngine.EventSystems.EventSystem.current != null && 
            UnityEngine.EventSystems.EventSystem.current.IsPointerOverGameObject(0))
        {
            return;
        }
        
        // Выпускаем луч из точки касания
        Ray ray = arCamera.ScreenPointToRay(touchPosition);
        RaycastHit hit;
        
        if (Physics.Raycast(ray, out hit, maxRaycastDistance, wallLayerMask))
        {
            // Проверяем, есть ли на объекте компонент ColorPickTarget
            ColorPickTarget target = hit.collider.GetComponent<ColorPickTarget>();
            if (target != null)
            {
                // Выбираем этот объект как текущую цель
                SelectTarget(target);
                
                // Показываем панель выбора цвета
                ShowColorPicker();
            }
        }
    }
    
    /// <summary>
    /// Выбирает целевой объект для изменения цвета
    /// </summary>
    private void SelectTarget(ColorPickTarget target)
    {
        currentTarget = target;
        Debug.Log($"[ColorPickerController] Выбрана цель: {target.gameObject.name}");
    }
    
    /// <summary>
    /// Показывает панель выбора цвета
    /// </summary>
    private void ShowColorPicker()
    {
        if (colorPickerPanel != null)
        {
            colorPickerPanel.SetActive(true);
            
            // Обновляем недавние цвета, если есть текущая цель
            if (currentTarget != null)
            {
                List<Color> targetColors = currentTarget.GetRecentColors();
                if (targetColors.Count > 0)
                {
                    recentColors = new List<Color>(targetColors);
                    UpdateRecentColorsUI();
                }
            }
        }
    }
    
    /// <summary>
    /// Скрывает панель выбора цвета
    /// </summary>
    public void HideColorPicker()
    {
        if (colorPickerPanel != null)
        {
            colorPickerPanel.SetActive(false);
        }
    }
    
    /// <summary>
    /// Инициализирует предустановленные цвета, если не заданы
    /// </summary>
    private void InitializePredefinedColors()
    {
        if (predefinedColors == null || predefinedColors.Count == 0)
        {
            predefinedColors = new List<Color>
            {
                new Color(0.949f, 0.949f, 0.949f), // Белый
                new Color(0.9f, 0.9f, 0.8f),       // Бежевый
                new Color(0.95f, 0.8f, 0.7f),      // Персиковый
                new Color(0.95f, 0.6f, 0.4f),      // Оранжевый
                new Color(0.88f, 0.8f, 0.4f),      // Желтый
                new Color(0.7f, 0.85f, 0.6f),      // Светло-зеленый
                new Color(0.6f, 0.78f, 0.8f),      // Голубой
                new Color(0.5f, 0.5f, 0.9f),       // Синий
                new Color(0.75f, 0.6f, 0.9f),      // Лавандовый
                new Color(0.9f, 0.6f, 0.75f),      // Розовый
                new Color(0.7f, 0.4f, 0.4f),       // Терракотовый
                new Color(0.4f, 0.59f, 0.3f),      // Зеленый
                new Color(0.3f, 0.3f, 0.6f),       // Темно-синий
                new Color(0.5f, 0.3f, 0.3f),       // Коричневый
                new Color(0.3f, 0.3f, 0.3f),       // Темно-серый
                new Color(0.1f, 0.1f, 0.1f)        // Почти черный
            };
        }
        
        // Добавляем в список недавних цветов первые цвета из предустановленных
        if (recentColors.Count == 0 && predefinedColors.Count > 0)
        {
            for (int i = 0; i < Mathf.Min(8, predefinedColors.Count); i++)
            {
                recentColors.Add(predefinedColors[i]);
            }
        }
    }
    
    /// <summary>
    /// Обновляет UI с недавними цветами
    /// </summary>
    private void UpdateRecentColorsUI()
    {
        if (recentColorsContainer == null || colorButtonPrefab == null)
        {
            Debug.LogWarning("[ColorPickerController] Отсутствуют необходимые UI компоненты для недавних цветов");
            return;
        }
        
        // Удаляем существующие кнопки
        foreach (Transform child in recentColorsContainer)
        {
            Destroy(child.gameObject);
        }
        
        // Создаем кнопки для недавних цветов
        for (int i = 0; i < recentColors.Count; i++)
        {
            GameObject buttonObj = Instantiate(colorButtonPrefab, recentColorsContainer);
            Button button = buttonObj.GetComponent<Button>();
            if (button != null)
            {
                // Настраиваем цвет кнопки
                Image image = button.GetComponent<Image>();
                if (image != null)
                {
                    Color buttonColor = recentColors[i];
                    // Для UI сохраняем полную непрозрачность
                    buttonColor.a = 1.0f;
                    image.color = buttonColor;
                }
                
                // Сохраняем индекс для обработчика
                int colorIndex = i;
                button.onClick.AddListener(() => OnColorButtonClicked(colorIndex));
            }
        }
        
        // Добавляем кнопку "Дополнительные цвета", если есть место
        if (recentColors.Count < gridColumns)
        {
            GameObject moreColorsButton = Instantiate(colorButtonPrefab, recentColorsContainer);
            Button button = moreColorsButton.GetComponent<Button>();
            if (button != null)
            {
                // Настраиваем внешний вид кнопки
                Image image = button.GetComponent<Image>();
                if (image != null)
                {
                    image.color = Color.white;
                }
                
                // Добавляем значок "+" или иконку
                Text text = button.GetComponentInChildren<Text>();
                if (text != null)
                {
                    text.text = "+";
                }
                
                button.onClick.AddListener(ShowMoreColors);
            }
        }
    }
    
    /// <summary>
    /// Обработчик нажатия на кнопку цвета
    /// </summary>
    private void OnColorButtonClicked(int colorIndex)
    {
        if (currentTarget != null && colorIndex >= 0 && colorIndex < recentColors.Count)
        {
            Color selectedColor = recentColors[colorIndex];
            
            // Применяем цвет к текущей цели
            currentTarget.SetColor(selectedColor);
            
            // Обновляем UI
            UpdateRecentColorsUI();
            
            Debug.Log($"[ColorPickerController] Выбран цвет: {selectedColor}");
        }
    }
    
    /// <summary>
    /// Показывает расширенную палитру цветов
    /// </summary>
    private void ShowMoreColors()
    {
        Debug.Log("[ColorPickerController] Открыта расширенная палитра цветов");
        // Здесь можно добавить реализацию показа полной палитры цветов
    }

    public void SetSelectedObject(GameObject obj)
    {
        // Implementation of SetSelectedObject method
    }
} 