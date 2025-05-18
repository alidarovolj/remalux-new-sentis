using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.AR;
using System.IO;
using Unity.XR.CoreUtils; // Для XROrigin

/// <summary>
/// Вспомогательный класс для создания UI панели выбора цветов в runtime
/// </summary>
public class CreateColorPickerUI : MonoBehaviour
{
    [Header("Префабы UI компонентов")]
    [SerializeField] private GameObject canvasPrefab;
    [SerializeField] private GameObject panelPrefab;
    [SerializeField] private GameObject buttonPrefab;
    
    [Header("Настройки UI")]
    [SerializeField] private string colorPickerName = "ColorPickerCanvas";
    [SerializeField] private Vector2 panelSize = new Vector2(700, 200);
    [SerializeField] private float buttonSize = 60f;
    [SerializeField] private float buttonSpacing = 10f;
    [SerializeField] private int columns = 6;
    
    private Canvas colorPickerCanvas;
    private GameObject colorPickerPanel;
    private RectTransform recentColorsContainer;
    
    void Start()
    {
        // Если не указаны префабы, создаем их программно
        CreateUIAssets();
        
        // Создаем UI для выбора цветов
        // BuildColorPickerUI(); // ВРЕМЕННО ОТКЛЮЧАЕМ СОЗДАНИЕ UI
        
        // Добавляем контроллер
        // SetupColorPickerController(); // ВРЕМЕННО ОТКЛЮЧАЕМ НАСТРОЙКУ КОНТРОЛЛЕРА
    }
    
    /// <summary>
    /// Создает UI активы если не заданы префабы
    /// </summary>
    private void CreateUIAssets()
    {
        if (canvasPrefab == null)
        {
            canvasPrefab = new GameObject("CanvasPrefab");
            Canvas canvas = canvasPrefab.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasPrefab.AddComponent<CanvasScaler>();
            canvasPrefab.AddComponent<GraphicRaycaster>();
            
            // Не показываем префаб в сцене
            canvasPrefab.SetActive(false);
        }
        
        if (panelPrefab == null)
        {
            panelPrefab = new GameObject("PanelPrefab");
            Image image = panelPrefab.AddComponent<Image>();
            image.color = new Color(0.1f, 0.1f, 0.1f, 0.8f);
            panelPrefab.SetActive(false);
        }
        
        if (buttonPrefab == null)
        {
            buttonPrefab = new GameObject("ColorButtonPrefab");
            Image image = buttonPrefab.AddComponent<Image>();
            Button button = buttonPrefab.AddComponent<Button>();
            button.targetGraphic = image;
            
            // Создаем объект текста для кнопки
            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(buttonPrefab.transform);
            Text text = textObj.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.alignment = TextAnchor.MiddleCenter;
            text.color = Color.white;
            text.text = "";
            
            // Настраиваем RectTransform
            RectTransform textRect = text.GetComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.sizeDelta = Vector2.zero;
            
            buttonPrefab.SetActive(false);
        }
    }
    
    /// <summary>
    /// Создает UI выбора цветов
    /// </summary>
    private void BuildColorPickerUI()
    {
        // Создаем канвас
        GameObject canvasObj = Instantiate(canvasPrefab);
        canvasObj.name = colorPickerName;
        canvasObj.SetActive(true);
        
        colorPickerCanvas = canvasObj.GetComponent<Canvas>();
        
        // Создаем панель
        colorPickerPanel = Instantiate(panelPrefab, canvasObj.transform);
        colorPickerPanel.name = "ColorPickerPanel";
        colorPickerPanel.SetActive(true);
        
        // Настраиваем RectTransform панели
        RectTransform panelRect = colorPickerPanel.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0);
        panelRect.anchorMax = new Vector2(0.5f, 0);
        panelRect.pivot = new Vector2(0.5f, 0);
        panelRect.sizeDelta = panelSize;
        panelRect.anchoredPosition = new Vector2(0, 20);
        
        // Создаем контейнер для цветов
        GameObject colorsContainer = new GameObject("RecentColorsContainer");
        colorsContainer.transform.SetParent(colorPickerPanel.transform);
        recentColorsContainer = colorsContainer.AddComponent<RectTransform>();
        
        // Настраиваем сетку для цветов
        GridLayoutGroup gridLayout = colorsContainer.AddComponent<GridLayoutGroup>();
        gridLayout.cellSize = new Vector2(buttonSize, buttonSize);
        gridLayout.spacing = new Vector2(buttonSpacing, buttonSpacing);
        gridLayout.padding = new RectOffset(20, 20, 20, 20);
        gridLayout.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
        gridLayout.constraintCount = columns;
        
        // Настраиваем RectTransform контейнера
        recentColorsContainer.anchorMin = new Vector2(0, 0);
        recentColorsContainer.anchorMax = new Vector2(1, 1);
        recentColorsContainer.pivot = new Vector2(0.5f, 0.5f);
        recentColorsContainer.sizeDelta = new Vector2(0, 0); // Учитываем высоту заголовка - УБРАЛИ ЗАГОЛОВОК
        recentColorsContainer.anchoredPosition = Vector2.zero; // СКОРРЕКТИРОВАЛИ ПОЗИЦИЮ
        
        // Создаем кнопку закрытия
        GameObject closeButtonObj = Instantiate(buttonPrefab, colorPickerPanel.transform);
        closeButtonObj.name = "CloseButton";
        Button closeButton = closeButtonObj.GetComponent<Button>();
        closeButton.GetComponent<Image>().color = new Color(0.8f, 0.2f, 0.2f, 1);
        
        Text closeText = closeButtonObj.GetComponentInChildren<Text>();
        if (closeText != null)
        {
            closeText.text = "X";
            closeText.fontSize = 20;
        }
        
        // Настраиваем RectTransform кнопки закрытия
        RectTransform closeRect = closeButtonObj.GetComponent<RectTransform>();
        closeRect.anchorMin = new Vector2(1, 1);
        closeRect.anchorMax = new Vector2(1, 1);
        closeRect.pivot = new Vector2(1, 1);
        closeRect.sizeDelta = new Vector2(40, 40);
        closeRect.anchoredPosition = new Vector2(-10, -10);
    }
    
    /// <summary>
    /// Настраивает контроллер для работы с UI выбора цветов
    /// </summary>
    private void SetupColorPickerController()
    {
        ColorPickerController controller = colorPickerCanvas.gameObject.AddComponent<ColorPickerController>();
        controller.enabled = true;
        
        // Через рефлексию устанавливаем приватные поля
        System.Type type = controller.GetType();
        
        System.Reflection.FieldInfo panelField = type.GetField("colorPickerPanel", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (panelField != null)
        {
            panelField.SetValue(controller, colorPickerPanel);
        }
        
        System.Reflection.FieldInfo containerField = type.GetField("recentColorsContainer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (containerField != null)
        {
            containerField.SetValue(controller, recentColorsContainer);
        }
        
        System.Reflection.FieldInfo buttonField = type.GetField("colorButtonPrefab", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (buttonField != null)
        {
            buttonField.SetValue(controller, buttonPrefab);
        }
        
        // Находим AR камеру
        Camera arCamera = FindARCamera();
        
        System.Reflection.FieldInfo cameraField = type.GetField("arCamera", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        if (cameraField != null && arCamera != null)
        {
            cameraField.SetValue(controller, arCamera);
        }
        
        // Добавляем обработчик для кнопки закрытия
        Transform closeButton = colorPickerPanel.transform.Find("CloseButton");
        if (closeButton != null)
        {
            Button button = closeButton.GetComponent<Button>();
            if (button != null)
            {
                button.onClick.AddListener(controller.HideColorPicker);
            }
        }
    }

    private Camera FindARCamera()
    {
        XROrigin xrOrigin = FindObjectOfType<XROrigin>();
        if (xrOrigin != null)
        {
            return xrOrigin.Camera;
        }
        Debug.LogError("XROrigin не найден в сцене!");
        return null;
    }

    private void OnDestroy()
    {
        // Implement any necessary cleanup code here
    }
} 