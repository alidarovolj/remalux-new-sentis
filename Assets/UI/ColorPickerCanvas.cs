using UnityEngine;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// Управляет Canvas-ом пользовательского интерфейса выбора цвета
/// </summary>
public class ColorPickerCanvas : MonoBehaviour
{
    [SerializeField] private GameObject colorPickerPanel;
    [SerializeField] private Button toggleButton;
    [SerializeField] private RectTransform colorPickerTransform;
    
    [Header("Анимация")]
    [SerializeField] private bool useAnimation = true;
    [SerializeField] private float animationDuration = 0.3f;
    
    private bool isPickerVisible = false;
    private Canvas canvas;
    private Vector2 hiddenPosition;
    private Vector2 visiblePosition;
    private Coroutine animationCoroutine;
    
    private void Awake()
    {
        canvas = GetComponent<Canvas>();
        
        if (toggleButton != null)
        {
            toggleButton.onClick.AddListener(ToggleColorPicker);
        }
        
        if (colorPickerTransform == null && colorPickerPanel != null)
        {
            colorPickerTransform = colorPickerPanel.GetComponent<RectTransform>();
        }
    }
    
    private void Start()
    {
        // Запоминаем исходную видимую позицию панели
        if (colorPickerTransform != null)
        {
            visiblePosition = colorPickerTransform.anchoredPosition;
            
            // Рассчитываем скрытую позицию (за пределами экрана)
            hiddenPosition = new Vector2(visiblePosition.x, -colorPickerTransform.rect.height);
            
            // Инициализируем в скрытом состоянии
            colorPickerTransform.anchoredPosition = hiddenPosition;
            isPickerVisible = false;
            
            if (colorPickerPanel != null)
            {
                colorPickerPanel.SetActive(false);
            }
        }
    }
    
    /// <summary>
    /// Переключает видимость панели выбора цвета
    /// </summary>
    public void ToggleColorPicker()
    {
        isPickerVisible = !isPickerVisible;
        
        if (colorPickerPanel != null)
        {
            colorPickerPanel.SetActive(isPickerVisible);
            
            if (useAnimation && colorPickerTransform != null)
            {
                // Остановка текущей анимации, если она выполняется
                if (animationCoroutine != null)
                {
                    StopCoroutine(animationCoroutine);
                }
                
                // Запуск новой анимации
                if (isPickerVisible)
                {
                    colorPickerTransform.anchoredPosition = hiddenPosition;
                    animationCoroutine = StartCoroutine(AnimatePanel(hiddenPosition, visiblePosition));
                }
                else
                {
                    colorPickerTransform.anchoredPosition = visiblePosition;
                    animationCoroutine = StartCoroutine(AnimatePanel(visiblePosition, hiddenPosition));
                }
            }
            else if (colorPickerTransform != null)
            {
                // Без анимации
                colorPickerTransform.anchoredPosition = isPickerVisible ? visiblePosition : hiddenPosition;
            }
        }
    }
    
    /// <summary>
    /// Корутина для анимации перемещения панели
    /// </summary>
    private IEnumerator AnimatePanel(Vector2 startPos, Vector2 endPos)
    {
        float startTime = Time.time;
        float elapsedTime = 0f;
        
        while (elapsedTime < animationDuration)
        {
            elapsedTime = Time.time - startTime;
            float t = Mathf.Clamp01(elapsedTime / animationDuration);
            
            // Применяем кривую сглаживания для более естественной анимации
            t = EaseInOutCubic(t);
            
            colorPickerTransform.anchoredPosition = Vector2.Lerp(startPos, endPos, t);
            yield return null;
        }
        
        // Устанавливаем конечную позицию точно
        colorPickerTransform.anchoredPosition = endPos;
        animationCoroutine = null;
    }
    
    /// <summary>
    /// Функция сглаживания для эффекта ускорения/замедления
    /// </summary>
    private float EaseInOutCubic(float t)
    {
        return t < 0.5f ? 4f * t * t * t : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;
    }
    
    /// <summary>
    /// Показывает панель выбора цвета
    /// </summary>
    public void ShowColorPicker()
    {
        if (!isPickerVisible)
        {
            ToggleColorPicker();
        }
    }
    
    /// <summary>
    /// Скрывает панель выбора цвета
    /// </summary>
    public void HideColorPicker()
    {
        if (isPickerVisible)
        {
            ToggleColorPicker();
        }
    }
    
    private void OnDestroy()
    {
        if (toggleButton != null)
        {
            toggleButton.onClick.RemoveListener(ToggleColorPicker);
        }
        
        // Остановка всех активных анимаций
        if (animationCoroutine != null)
        {
            StopCoroutine(animationCoroutine);
            animationCoroutine = null;
        }
    }
} 