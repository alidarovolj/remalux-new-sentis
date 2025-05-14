using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

[RequireComponent(typeof(WallPaintEffect))]
public class WallPaintDebugPanel : MonoBehaviour
{
    [Header("Ссылки на компоненты")]
    [SerializeField] private WallPaintDebugger debugger;
    [SerializeField] private WallPaintEffect wallPaintEffect;
    [SerializeField] private ARWallPainter wallPainter;

    [Header("UI Элементы")]
    [SerializeField] private Toggle useMaskToggle;
    [SerializeField] private Slider blendSlider;
    [SerializeField] private TMP_Text blendValueText;
    [SerializeField] private Image colorPreview;
    [SerializeField] private Toggle debugOverlayToggle;
    [SerializeField] private Toggle showPlanesToggle;
    [SerializeField] private Toggle highlightVerticalToggle;
    [SerializeField] private Toggle showNormalsToggle;
    [SerializeField] private Button redButton;
    [SerializeField] private Button greenButton;
    [SerializeField] private Button blueButton;
    [SerializeField] private Button yellowButton;
    [SerializeField] private Button purpleButton;
    [SerializeField] private Button whiteButton;
    [SerializeField] private Button resetButton;
    [SerializeField] private RectTransform mainPanel;

    private bool isPanelVisible = true;
    private bool isInitialized = false;

    void Start()
    {
        // Проверяем, есть ли необходимые компоненты
        if (debugger == null) debugger = FindObjectOfType<WallPaintDebugger>();
        if (wallPaintEffect == null) wallPaintEffect = FindObjectOfType<WallPaintEffect>();
        if (wallPainter == null) wallPainter = FindObjectOfType<ARWallPainter>();

        if (debugger == null || wallPaintEffect == null || wallPainter == null)
        {
            Debug.LogError("WallPaintDebugPanel: Не удалось найти необходимые компоненты");
            return;
        }

        // Настраиваем начальное состояние UI
        if (useMaskToggle != null) useMaskToggle.isOn = true;
        if (debugOverlayToggle != null) debugOverlayToggle.isOn = false;
        if (showPlanesToggle != null) showPlanesToggle.isOn = true;
        if (highlightVerticalToggle != null) highlightVerticalToggle.isOn = true;
        if (showNormalsToggle != null) showNormalsToggle.isOn = true;
        
        if (blendSlider != null)
        {
            blendSlider.value = 0.5f;
            UpdateBlendValueText(0.5f);
        }

        // Назначаем обработчики событий
        if (useMaskToggle != null) useMaskToggle.onValueChanged.AddListener(OnUseMaskToggleChanged);
        if (blendSlider != null) blendSlider.onValueChanged.AddListener(OnBlendSliderChanged);
        if (debugOverlayToggle != null) debugOverlayToggle.onValueChanged.AddListener(OnDebugOverlayToggleChanged);
        if (showPlanesToggle != null) showPlanesToggle.onValueChanged.AddListener(OnShowPlanesToggleChanged);
        if (highlightVerticalToggle != null) highlightVerticalToggle.onValueChanged.AddListener(OnHighlightVerticalToggleChanged);
        if (showNormalsToggle != null) showNormalsToggle.onValueChanged.AddListener(OnShowNormalsToggleChanged);

        // Настраиваем кнопки цветов
        if (redButton != null) redButton.onClick.AddListener(() => SetColor(Color.red));
        if (greenButton != null) greenButton.onClick.AddListener(() => SetColor(new Color(0, 0.8f, 0)));
        if (blueButton != null) blueButton.onClick.AddListener(() => SetColor(new Color(0, 0.5f, 1f)));
        if (yellowButton != null) yellowButton.onClick.AddListener(() => SetColor(new Color(1f, 0.9f, 0)));
        if (purpleButton != null) purpleButton.onClick.AddListener(() => SetColor(new Color(0.8f, 0, 0.8f)));
        if (whiteButton != null) whiteButton.onClick.AddListener(() => SetColor(Color.white));
        if (resetButton != null) resetButton.onClick.AddListener(OnResetButtonClicked);

        // Устанавливаем начальный цвет
        SetColor(Color.red);

        // Настраиваем панель
        isPanelVisible = true;
        isInitialized = true;
    }

    private void Update()
    {
        // Проверяем клавишу для скрытия/показа отладочной панели
        if (Input.GetKeyDown(KeyCode.D))
        {
            TogglePanel();
        }
    }

    // Переключение видимости панели
    public void TogglePanel()
    {
        isPanelVisible = !isPanelVisible;
        if (mainPanel != null)
        {
            mainPanel.gameObject.SetActive(isPanelVisible);
        }
    }

    // Обработчик изменения переключателя использования маски сегментации
    private void OnUseMaskToggleChanged(bool value)
    {
        if (!isInitialized || wallPaintEffect == null) return;
        wallPaintEffect.SetUseMask(value);
    }

    // Обработчик изменения ползунка интенсивности покраски
    private void OnBlendSliderChanged(float value)
    {
        if (!isInitialized || wallPainter == null) return;
        wallPainter.SetBlendFactor(value);
        UpdateBlendValueText(value);
    }

    // Обработчик изменения переключателя отладочного режима
    private void OnDebugOverlayToggleChanged(bool value)
    {
        if (!isInitialized || wallPaintEffect == null) return;
        
        if (value)
            wallPaintEffect.EnableDebugMode();
        else
            wallPaintEffect.DisableDebugMode();
    }

    // Обработчик изменения переключателя отображения плоскостей
    private void OnShowPlanesToggleChanged(bool value)
    {
        if (!isInitialized || debugger == null) return;
        debugger.ToggleDebugVisuals();
    }

    // Обработчик изменения переключателя подсветки вертикальных плоскостей
    private void OnHighlightVerticalToggleChanged(bool value)
    {
        if (!isInitialized || debugger == null) return;
        debugger.SetHighlightVerticalPlanes(value);
    }

    // Обработчик изменения переключателя отображения нормалей
    private void OnShowNormalsToggleChanged(bool value)
    {
        if (!isInitialized || debugger == null) return;
        debugger.SetShowNormals(value);
    }

    // Обновление текста, отображающего значение blend factor
    private void UpdateBlendValueText(float value)
    {
        if (blendValueText != null)
        {
            blendValueText.text = value.ToString("F2");
        }
    }

    // Установка цвета для покраски
    private void SetColor(Color color)
    {
        if (!isInitialized || wallPainter == null) return;
        
        wallPainter.SetPaintColor(color);
        
        // Обновляем предпросмотр цвета
        if (colorPreview != null)
        {
            colorPreview.color = color;
        }
    }

    // Обработчик нажатия кнопки сброса
    private void OnResetButtonClicked()
    {
        if (!isInitialized || wallPainter == null) return;
        wallPainter.ResetAllWalls();
    }
}