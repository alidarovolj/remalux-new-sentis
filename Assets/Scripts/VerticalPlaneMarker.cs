using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Компонент для маркировки вертикальных плоскостей
/// Автоматически определяет вертикальные поверхности на основе нормали
/// </summary>
[RequireComponent(typeof(ARPlane))]
public class VerticalPlaneMarker : MonoBehaviour
{
    [Tooltip("Максимальное отклонение от вертикали в градусах (0-90)")]
    [Range(0, 90)]
    public float maxVerticalDeviation = 15f;
    
    [Tooltip("Автоматически добавлять якорь к вертикальной плоскости")]
    public bool autoAddAnchor = true;
    
    [Tooltip("Изменять цвет контура для отладки")]
    public bool debugVisualizeVertical = false;
    
    [Tooltip("Цвет контура для вертикальных плоскостей")]
    public Color verticalColor = Color.green;
    
    [Tooltip("Цвет контура для невертикальных плоскостей")]
    public Color nonVerticalColor = Color.yellow;
    
    private ARPlane arPlane;
    private LineRenderer lineRenderer;
    private ARAnchor anchor;
    private bool isVertical = false;
    
    private void Awake()
    {
        arPlane = GetComponent<ARPlane>();
        lineRenderer = GetComponent<LineRenderer>();
    }
    
    private void Start()
    {
        // Проверяем плоскость при запуске
        CheckIfVertical();
    }
    
    private void Update()
    {
        // Повторно проверяем каждый кадр, так как плоскость может меняться
        CheckIfVertical();
    }
    
    private void CheckIfVertical()
    {
        if (arPlane == null) return;
        
        // Получаем нормаль плоскости
        Vector3 planeNormal = arPlane.normal;
        
        // Вычисляем угол между нормалью и вертикалью (вектор вверх)
        float dotProduct = Vector3.Dot(planeNormal, Vector3.up);
        float angleRad = Mathf.Acos(Mathf.Abs(dotProduct));
        float angleDeg = angleRad * Mathf.Rad2Deg;
        
        // Если угол близок к 90 градусам (с учетом допуска), то плоскость вертикальная
        // Угол 90 градусов означает, что нормаль перпендикулярна вектору вверх
        bool wasVertical = isVertical;
        isVertical = angleDeg >= (90f - maxVerticalDeviation);
        
        // Если статус изменился или это первая проверка
        if (isVertical != wasVertical || anchor == null)
        {
            // Визуализация для отладки
            if (debugVisualizeVertical && lineRenderer != null)
            {
                lineRenderer.startColor = isVertical ? verticalColor : nonVerticalColor;
                lineRenderer.endColor = isVertical ? verticalColor : nonVerticalColor;
            }
            
            // Добавляем якорь к вертикальной плоскости, если нужно
            if (isVertical && autoAddAnchor)
            {
                if (anchor == null)
                {
                    anchor = GetComponent<ARAnchor>();
                    if (anchor == null)
                    {
                        anchor = gameObject.AddComponent<ARAnchor>();
                    }
                }
            }
            
            // Можно добавить дополнительную логику для обработки вертикальных плоскостей
            // например, отправка события другим компонентам
        }
    }
    
    // Публичный метод для проверки, является ли плоскость вертикальной
    public bool IsVertical()
    {
        return isVertical;
    }
} 