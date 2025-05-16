using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Панель отладки для управления WallPaintDebugger
/// </summary>
public class WallPaintDebugPanel : MonoBehaviour
{
    [Tooltip("Ссылка на WallPaintDebugger")]
    public WallPaintDebugger debugger;

    [Tooltip("Переключатель для отображения отладочной визуализации")]
    public Toggle debugVisualizationToggle;

    [Tooltip("Кнопка для сброса обнаружения плоскостей")]
    public Button resetPlanesButton;

    private void Start()
    {
        // Находим WallPaintDebugger если он не назначен
        if (debugger == null)
        {
            debugger = FindObjectOfType<WallPaintDebugger>();
        }

        // Настраиваем переключатель
        if (debugVisualizationToggle != null)
        {
            debugVisualizationToggle.isOn = debugger != null && debugger.highlightVerticalPlanes;
            debugVisualizationToggle.onValueChanged.AddListener(OnToggleDebug);
        }

        // Настраиваем кнопку
        if (resetPlanesButton != null)
        {
            resetPlanesButton.onClick.AddListener(ResetARPlanes);
        }
    }

    // Обработчик переключателя отладочной визуализации
    private void OnToggleDebug(bool value)
    {
        if (debugger != null)
        {
            debugger.highlightVerticalPlanes = value;
        }
    }

    // Метод для перезапуска обнаружения плоскостей
    public void ResetARPlanes()
    {
        var planeManager = FindObjectOfType<UnityEngine.XR.ARFoundation.ARPlaneManager>();
        if (planeManager != null)
        {
            // Перезапускаем обнаружение плоскостей
            bool wasEnabled = planeManager.enabled;
            planeManager.enabled = false;
            
            // Небольшая задержка перед повторной активацией
            Invoke(nameof(ReenablePlaneDetection), 0.5f);
        }
    }

    // Метод для повторной активации обнаружения плоскостей
    private void ReenablePlaneDetection()
    {
        var planeManager = FindObjectOfType<UnityEngine.XR.ARFoundation.ARPlaneManager>();
        if (planeManager != null)
        {
            planeManager.enabled = true;
        }
    }
} 