using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Collections.Generic;

public class WallPaintDebugger : MonoBehaviour
{
    public ARPlaneManager planeManager;
    public MonoBehaviour wallPaintEffect;
    
    [Tooltip("Выделять вертикальные плоскости зеленым цветом")]
    public bool highlightVerticalPlanes = false;
    
    [Tooltip("Показывать контуры всех плоскостей")]
    public bool showAllPlaneOutlines = true;
    
    // Список вертикальных плоскостей
    private List<ARPlane> verticalPlanes = new List<ARPlane>();
    
    // Словарь для хранения LineRenderer'ов для каждой плоскости
    private Dictionary<TrackableId, LineRenderer> planeLineRenderers = new Dictionary<TrackableId, LineRenderer>();
    
    // Переменная для хранения цвета вертикальных плоскостей
    private Color verticalColor = Color.green;

      private void Awake()
      {
        // Находим необходимые компоненты, если они не назначены
        if (planeManager == null)
        {
            var xrOrigin = FindObjectOfType<Unity.XR.CoreUtils.XROrigin>();
            if (xrOrigin != null)
            {
                var trackers = xrOrigin.transform.Find("AR Trackers");
                if (trackers != null)
                {
                    planeManager = trackers.GetComponent<ARPlaneManager>();
                }
            }
        }
    }
    
    void Start()
    {
        // Подписываемся на события обнаружения плоскостей
            if (planeManager != null)
            {
                  planeManager.planesChanged += OnPlanesChanged;
            }
      }

    void OnDestroy()
      {
        // Отписываемся от событий
            if (planeManager != null)
            {
                  planeManager.planesChanged -= OnPlanesChanged;
            }
    }
    
    private void OnPlanesChanged(ARPlanesChangedEventArgs args)
    {
        // Обрабатываем новые плоскости
        foreach (ARPlane plane in args.added)
        {
            ProcessPlane(plane);
        }
        
        // Обрабатываем обновленные плоскости
        foreach (ARPlane plane in args.updated)
        {
            ProcessPlane(plane);
        }
        
        // Обрабатываем удаленные плоскости
        foreach (ARPlane plane in args.removed)
        {
            // Удаляем из списка вертикальных плоскостей
            verticalPlanes.Remove(plane);
            
            // Удаляем LineRenderer из словаря, если он был добавлен
            if (planeLineRenderers.TryGetValue(plane.trackableId, out LineRenderer lineRenderer))
            {
                planeLineRenderers.Remove(plane.trackableId);
            }
        }
    }
    
    private void ProcessPlane(ARPlane plane)
    {
        // Проверяем, является ли плоскость вертикальной, используя VerticalPlaneMarker если возможно
        bool isVertical = false;
        
        // Сначала проверяем наличие VerticalPlaneMarker для более точного определения
        VerticalPlaneMarker marker = plane.GetComponent<VerticalPlaneMarker>();
        if (marker != null)
        {
            isVertical = marker.IsVertical();
        }
        else
        {
            // Если маркера нет, используем наш метод
            isVertical = IsVerticalPlane(plane);
            
            // Добавляем маркер для будущего использования
            if (isVertical && plane.gameObject.GetComponent<VerticalPlaneMarker>() == null)
            {
                var newMarker = plane.gameObject.AddComponent<VerticalPlaneMarker>();
                newMarker.debugVisualizeVertical = highlightVerticalPlanes;
                newMarker.autoAddAnchor = true;
            }
        }
        
        if (isVertical)
        {
            // Если это вертикальная плоскость и её еще нет в списке
            if (!verticalPlanes.Contains(plane))
            {
                verticalPlanes.Add(plane);
                
                // Стабилизируем плоскость с помощью якоря
                if (plane.gameObject.GetComponent<ARAnchor>() == null)
                {
                    plane.gameObject.AddComponent<ARAnchor>();
                }
                
                // Отключаем тени для производительности
                Renderer renderer = plane.GetComponent<Renderer>();
                if (renderer != null)
                {
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    renderer.receiveShadows = false;
                }
                
                // Визуальное выделение для отладки
                if (highlightVerticalPlanes)
                {
                    // Изменяем цвет для визуализации вертикальных плоскостей
                    LineRenderer lineRenderer = plane.GetComponent<LineRenderer>();
                    if (lineRenderer != null)
                    {
                        lineRenderer.startColor = verticalColor;
                        lineRenderer.endColor = verticalColor;
                    }
                }
            }
                  }
                  else
                  {
            // Удаляем плоскость из списка вертикальных, если она была там
            verticalPlanes.Remove(plane);
        }
        
        // Обновляем визуализацию контура плоскости
        if (showAllPlaneOutlines)
        {
            UpdatePlaneOutline(plane);
        }
    }
    
    private void UpdatePlaneOutline(ARPlane plane)
    {
        // Получаем LineRenderer для этой плоскости или создаем новый
        LineRenderer lineRenderer;
        if (!planeLineRenderers.TryGetValue(plane.trackableId, out lineRenderer))
        {
            lineRenderer = plane.GetComponent<LineRenderer>();
            if (lineRenderer == null)
            {
                lineRenderer = plane.gameObject.AddComponent<LineRenderer>();
                // Настраиваем LineRenderer
                lineRenderer.loop = true;
                  lineRenderer.positionCount = 0;
                lineRenderer.startWidth = 0.01f;
                lineRenderer.endWidth = 0.01f;
            }
            
            // Сохраняем LineRenderer в словаре
            planeLineRenderers[plane.trackableId] = lineRenderer;
        }
        
        // Получаем контур плоскости и обновляем LineRenderer
        if (plane.boundary.Length > 0)
        {
            lineRenderer.positionCount = plane.boundary.Length;
            for (int i = 0; i < plane.boundary.Length; i++)
            {
                Vector3 localPoint = new Vector3(plane.boundary[i].x, 0, plane.boundary[i].y);
                Vector3 worldPoint = plane.transform.TransformPoint(localPoint);
                lineRenderer.SetPosition(i, worldPoint);
            }
            
            // Устанавливаем цвет в зависимости от типа плоскости
            Color outlineColor = IsVerticalPlane(plane) ? verticalColor : Color.yellow;
            lineRenderer.startColor = outlineColor;
            lineRenderer.endColor = outlineColor;
        }
    }
    
    // Метод для определения вертикальной плоскости
    private bool IsVerticalPlane(ARPlane plane)
    {
        // Проверяем нормаль плоскости
        Vector3 planeNormal = plane.normal;
        float dotProduct = Vector3.Dot(planeNormal, Vector3.up);
        
        // Если скалярное произведение с вектором вверх близко к 0, то плоскость вертикальная
        return Mathf.Abs(dotProduct) < 0.25f; // допустимое отклонение ~15 градусов
    }
    
    // Публичный метод для получения всех вертикальных плоскостей
    public List<ARPlane> GetVerticalPlanes()
    {
        return new List<ARPlane>(verticalPlanes);
    }
    
    // Отладочная визуализация в редакторе
    void OnDrawGizmos()
    {
        if (!highlightVerticalPlanes) return;
        
        Gizmos.color = verticalColor;
        foreach (var plane in verticalPlanes)
        {
            if (plane != null)
            {
                Gizmos.DrawWireCube(plane.center, new Vector3(plane.size.x, plane.size.y, 0.01f));
            }
        }
    }
    
    // Метод для изменения цвета плоскостей для отладки
    public void SetColor(Color color)
    {
        // Устанавливаем цвет для вертикальных плоскостей
        verticalColor = color;
        
        // Обновляем цвет для всех линий вертикальных плоскостей
        foreach (var plane in verticalPlanes)
        {
            if (plane != null)
            {
                LineRenderer lineRenderer = plane.GetComponent<LineRenderer>();
                if (lineRenderer != null)
                {
                    lineRenderer.startColor = color;
                    lineRenderer.endColor = color;
                }
            }
        }
        
        // Обновляем цвет в компоненте WallPaintEffect, если он доступен
        if (wallPaintEffect != null && wallPaintEffect is WallPaintEffect)
        {
            WallPaintEffect effect = (WallPaintEffect)wallPaintEffect;
            effect.SetPaintColor(color);
            effect.ForceUpdateMaterial();
            Debug.Log($"WallPaintDebugger: Обновлен цвет в WallPaintEffect на {color}");
        }
        else
        {
            // Пытаемся найти компонент WallPaintEffect, если он не был назначен
            WallPaintEffect effect = FindObjectOfType<WallPaintEffect>();
            if (effect != null)
            {
                effect.SetPaintColor(color);
                effect.ForceUpdateMaterial();
                wallPaintEffect = effect;
                Debug.Log($"WallPaintDebugger: Найден и обновлен WallPaintEffect с цветом {color}");
            }
            else
            {
                Debug.LogWarning("WallPaintDebugger: WallPaintEffect не найден, невозможно обновить цвет материала");
            }
        }
      }
}