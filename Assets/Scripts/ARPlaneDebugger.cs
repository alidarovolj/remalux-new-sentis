using UnityEngine;
using UnityEngine.XR.ARFoundation;
using System.Collections.Generic;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.UI;
using System.Collections;

/// <summary>
/// Компонент для отладки обнаружения и отображения AR плоскостей.
/// Помогает определить, как AR Foundation обнаруживает плоскости в окружении.
/// </summary>
public class ARPlaneDebugger : MonoBehaviour
{
    [SerializeField] private ARPlaneManager planeManager;
    [SerializeField] private ARSession arSession;
    [SerializeField] private Text debugText;
    [SerializeField] private WallPaintEffect wallPaintEffect;
    [SerializeField] private Color verticalPlaneColor = Color.red;
    [SerializeField] private Color horizontalPlaneColor = Color.blue;
    [SerializeField] private bool enhanceVerticalPlanes = true;
    [SerializeField] private float verticalPlaneBoundaryThickness = 0.02f;
    [SerializeField] private bool logPlaneEvents = true;
    [SerializeField] private bool forcePlaneDetection = true;

    private Dictionary<TrackableId, GameObject> planeVisualizers = new Dictionary<TrackableId, GameObject>();
    private int prevPlaneCount = 0;

    private void Awake()
    {
        if (planeManager == null)
            planeManager = FindObjectOfType<ARPlaneManager>();
            
        if (arSession == null)
            arSession = FindObjectOfType<ARSession>();
    }

    private void OnEnable()
    {
        if (planeManager != null)
        {
            planeManager.planesChanged += OnPlanesChanged;
        }
    }

    private void OnDisable()
    {
        if (planeManager != null)
        {
            planeManager.planesChanged -= OnPlanesChanged;
        }
    }

    private void Start()
    {
        LogSessionInfo();
        
        // Проверяем настройки PlaneManager
        if (planeManager != null)
        {
            Debug.Log("ARPlaneDebugger: режим обнаружения плоскостей: " + planeManager.requestedDetectionMode);
            
            // Убеждаемся, что включено обнаружение вертикальных плоскостей
            if ((planeManager.requestedDetectionMode & PlaneDetectionMode.Vertical) == 0)
            {
                Debug.LogWarning("ARPlaneDebugger: обнаружение вертикальных плоскостей НЕ ВКЛЮЧЕНО!");
                planeManager.requestedDetectionMode |= PlaneDetectionMode.Vertical;
                Debug.Log("ARPlaneDebugger: включено обнаружение вертикальных плоскостей");
            }
        }
        else
        {
            Debug.LogError("ARPlaneManager не найден! AR плоскости не будут обнаруживаться.");
        }
        
        // Создаем UI для дебага, если его нет
        if (debugText == null)
        {
            CreateDebugUI();
        }
        
        // Находим WallPaintEffect, если не указан
        if (wallPaintEffect == null)
        {
            wallPaintEffect = FindObjectOfType<WallPaintEffect>();
        }
        
        // Начинаем корутину для периодической проверки и форсирования обнаружения плоскостей
        if (forcePlaneDetection)
        {
            StartCoroutine(ForcePlaneDetectionCoroutine());
        }
    }

    private void Update()
    {
        // Обновляем информацию об обнаруженных плоскостях
        UpdateDebugInfo();
        
        // Логируем изменения в количестве плоскостей
        if (planeManager != null && planeManager.trackables.count != prevPlaneCount)
        {
            prevPlaneCount = planeManager.trackables.count;
            Debug.Log($"ARPlaneDebugger: количество отслеживаемых плоскостей изменилось: {prevPlaneCount}");
            
            // Выводим информацию по типам плоскостей
            int verticalCount = 0;
            int horizontalCount = 0;
            
            foreach (ARPlane plane in planeManager.trackables)
            {
                if (plane.alignment == PlaneAlignment.Vertical)
                    verticalCount++;
                else if (plane.alignment == PlaneAlignment.HorizontalUp || plane.alignment == PlaneAlignment.HorizontalDown)
                    horizontalCount++;
            }
            
            Debug.Log($"ARPlaneDebugger: вертикальных: {verticalCount}, горизонтальных: {horizontalCount}");
        }
        
        // Проверяем, если ARSession перезапустилась
        CheckARSessionState();
    }
    
    private void LogSessionInfo()
    {
        if (arSession != null)
        {
            Debug.Log("ARPlaneDebugger: сессия AR инициализирована. Состояние: " + ARSession.state);
            
            // Проверяем наличие simulartion
            bool isSimulation = IsRunningInSimulation();
            Debug.Log("ARPlaneDebugger: режим симуляции: " + (isSimulation ? "ДА" : "НЕТ"));
        }
        else
        {
            Debug.LogError("ARSession не найден! AR может не работать корректно.");
        }
    }
    
    private bool IsRunningInSimulation()
    {
        // Проверяем по наличию компонентов симуляции
        MonoBehaviour[] allComponents = FindObjectsOfType<MonoBehaviour>();
        foreach (var comp in allComponents)
        {
            if (comp.GetType().Name.Contains("Simulation") && 
                comp.GetType().Namespace != null && 
                comp.GetType().Namespace.Contains("Simulation"))
            {
                return true;
            }
        }
        return false;
    }
    
    private void CheckARSessionState()
    {
        if (arSession != null && Time.frameCount % 60 == 0)
        {
            Debug.Log("ARPlaneDebugger: текущее состояние ARSession: " + ARSession.state);
        }
    }

    private void OnPlanesChanged(ARPlanesChangedEventArgs args)
    {
        if (logPlaneEvents)
        {
            foreach (var plane in args.added)
            {
                Debug.Log($"ARPlaneDebugger: добавлена плоскость {plane.trackableId}, выравнивание: {plane.alignment}, " +
                    $"классификация: {plane.classification}, размер: {plane.size}");
                    
                // Создаем визуализацию для отладки
                if (enhanceVerticalPlanes && plane.alignment == PlaneAlignment.Vertical)
                {
                    CreatePlaneVisualizer(plane);
                }
            }
            
            foreach (var plane in args.updated)
            {
                if (enhanceVerticalPlanes && plane.alignment == PlaneAlignment.Vertical)
                {
                    if (planeVisualizers.TryGetValue(plane.trackableId, out GameObject visualizer))
                    {
                        UpdatePlaneVisualizer(plane, visualizer);
                    }
                    else
                    {
                        CreatePlaneVisualizer(plane);
                    }
                }
            }
            
            foreach (var plane in args.removed)
            {
                Debug.Log($"ARPlaneDebugger: удалена плоскость {plane.trackableId}");
                
                if (planeVisualizers.TryGetValue(plane.trackableId, out GameObject visualizer))
                {
                    Destroy(visualizer);
                    planeVisualizers.Remove(plane.trackableId);
                }
            }
        }
    }
    
    private void CreatePlaneVisualizer(ARPlane plane)
    {
        GameObject visualizer = new GameObject($"PlaneVisualizer_{plane.trackableId}");
        visualizer.transform.SetParent(transform);
        
        LineRenderer lineRenderer = visualizer.AddComponent<LineRenderer>();
        lineRenderer.useWorldSpace = true;
        lineRenderer.startWidth = verticalPlaneBoundaryThickness;
        lineRenderer.endWidth = verticalPlaneBoundaryThickness;
        lineRenderer.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        lineRenderer.startColor = verticalPlaneColor;
        lineRenderer.endColor = verticalPlaneColor;
        
        planeVisualizers.Add(plane.trackableId, visualizer);
        UpdatePlaneVisualizer(plane, visualizer);
    }
    
    private void UpdatePlaneVisualizer(ARPlane plane, GameObject visualizer)
    {
        LineRenderer lineRenderer = visualizer.GetComponent<LineRenderer>();
        if (lineRenderer == null) return;
        
        // Получаем границы плоскости
        if (plane.boundary.Length > 0)
        {
            lineRenderer.positionCount = plane.boundary.Length + 1;
            
            for (int i = 0; i < plane.boundary.Length; i++)
            {
                Vector3 point = plane.transform.TransformPoint(
                    new Vector3(plane.boundary[i].x, 0, plane.boundary[i].y));
                lineRenderer.SetPosition(i, point);
            }
            
            // Замыкаем контур
            lineRenderer.SetPosition(plane.boundary.Length, 
                lineRenderer.GetPosition(0));
        }
    }

    private void UpdateDebugInfo()
    {
        if (debugText == null) return;
        
        string info = "AR Plane Debugger\n";
        
        if (planeManager != null)
        {
            info += $"Обнаружено плоскостей: {planeManager.trackables.count}\n";
            
            int verticalCount = 0;
            int horizontalCount = 0;
            
            foreach (ARPlane plane in planeManager.trackables)
            {
                if (plane.alignment == PlaneAlignment.Vertical)
                    verticalCount++;
                else if (plane.alignment == PlaneAlignment.HorizontalUp || plane.alignment == PlaneAlignment.HorizontalDown)
                    horizontalCount++;
            }
            
            info += $"Вертикальных: {verticalCount}\n";
            info += $"Горизонтальных: {horizontalCount}\n";
        }
        else
        {
            info += "ARPlaneManager не найден!\n";
        }
        
        if (arSession != null)
        {
            info += $"Статус сессии: {ARSession.state}\n";
        }
        
        debugText.text = info;
    }

    // Создаем простой UI элемент для отображения отладочной информации
    private void CreateDebugUI()
    {
        // Проверяем наличие Canvas в сцене
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            // Создаем Canvas
            GameObject canvasObj = new GameObject("DebugCanvas");
            canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            
            // Добавляем CanvasScaler
            CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            
            // Добавляем GraphicRaycaster
            canvasObj.AddComponent<GraphicRaycaster>();
        }
        
        // Создаем объект для текста
        GameObject textObj = new GameObject("ARPlaneDebugText");
        textObj.transform.SetParent(canvas.transform, false);
        
        // Настраиваем RectTransform
        RectTransform rectTransform = textObj.AddComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0, 1);
        rectTransform.anchorMax = new Vector2(1, 1);
        rectTransform.pivot = new Vector2(0.5f, 1);
        rectTransform.anchoredPosition = new Vector2(0, -10);
        rectTransform.sizeDelta = new Vector2(0, 200);
        
        // Добавляем и настраиваем Text
        debugText = textObj.AddComponent<Text>();
        debugText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        debugText.fontSize = 24;
        debugText.color = Color.white;
        debugText.alignment = TextAnchor.UpperLeft;
        debugText.text = "AR Plane Debugger";
        
        // Добавляем фон для лучшей читаемости
        GameObject bgObj = new GameObject("Background");
        bgObj.transform.SetParent(textObj.transform, false);
        
        RectTransform bgRect = bgObj.AddComponent<RectTransform>();
        bgRect.anchorMin = Vector2.zero;
        bgRect.anchorMax = Vector2.one;
        bgRect.sizeDelta = Vector2.zero;
        
        Image bgImage = bgObj.AddComponent<Image>();
        bgImage.color = new Color(0, 0, 0, 0.5f);
        
        // Перемещаем фон под текст в иерархии, чтобы текст был поверх
        bgObj.transform.SetAsFirstSibling();
        
        Debug.Log("ARPlaneDebugger: создан UI для отображения информации");
    }

    // Корутина для периодического форсирования обнаружения плоскостей
    private IEnumerator ForcePlaneDetectionCoroutine()
    {
        // Даем время на инициализацию AR сессии
        yield return new WaitForSeconds(3.0f);
        
        while (true)
        {
            // Проверяем и форсируем обнаружение плоскостей
            ForceUpdatePlaneDetection();
            
            // Добавляем плоскость к WallPaintEffect, если необходимо
            UpdateWallPaintEffectPlanes();
            
            // Проверяем каждые 3 секунды
            yield return new WaitForSeconds(3.0f);
        }
    }
    
    // Принудительно обновляет настройки ARPlaneManager для обнаружения плоскостей
    private void ForceUpdatePlaneDetection()
    {
        if (planeManager == null) return;
        
        if ((planeManager.requestedDetectionMode & PlaneDetectionMode.Vertical) == 0)
        {
            // Принудительно включаем обнаружение вертикальных плоскостей
            planeManager.requestedDetectionMode |= PlaneDetectionMode.Vertical;
            Debug.Log("ARPlaneDebugger: принудительно включено обнаружение вертикальных плоскостей");
        }
        
        // Проверяем наличие плоскостей
        if (planeManager.trackables.count == 0)
        {
            Debug.Log("ARPlaneDebugger: плоскости не обнаружены. Попытка перезапуска обнаружения...");
            
            // Временно отключаем и снова включаем ARPlaneManager для сброса
            planeManager.enabled = false;
            
            // Задержка перед повторным включением
            StartCoroutine(ReenablePlaneManager());
        }
    }
    
    private IEnumerator ReenablePlaneManager()
    {
        yield return new WaitForSeconds(0.5f);
        if (planeManager != null)
        {
            planeManager.enabled = true;
            Debug.Log("ARPlaneDebugger: ARPlaneManager перезапущен");
        }
    }
    
    // Обновляет информацию о плоскостях в WallPaintEffect
    private void UpdateWallPaintEffectPlanes()
    {
        if (wallPaintEffect == null || planeManager == null) return;
        
        // Проверяем, есть ли метод для работы с плоскостями
        var methodInfo = wallPaintEffect.GetType().GetMethod("ForceRefreshARPlanes", 
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | 
            System.Reflection.BindingFlags.NonPublic);
            
        if (methodInfo != null)
        {
            try
            {
                methodInfo.Invoke(wallPaintEffect, null);
                Debug.Log("ARPlaneDebugger: принудительное обновление плоскостей в WallPaintEffect");
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"ARPlaneDebugger: ошибка при обновлении плоскостей в WallPaintEffect: {e.Message}");
            }
        }
        else
        {
            // Если метода нет, попробуем вызвать ApplyMaterialToARPlanes
            methodInfo = wallPaintEffect.GetType().GetMethod("ApplyMaterialToARPlanes", 
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | 
                System.Reflection.BindingFlags.NonPublic);
                
            if (methodInfo != null)
            {
                try
                {
                    methodInfo.Invoke(wallPaintEffect, null);
                    Debug.Log("ARPlaneDebugger: вызван ApplyMaterialToARPlanes в WallPaintEffect");
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"ARPlaneDebugger: ошибка при вызове ApplyMaterialToARPlanes: {e.Message}");
                }
            }
        }
    }
    
    // Публичный метод для принудительного запуска обнаружения плоскостей
    public void TriggerPlaneDetection()
    {
        ForceUpdatePlaneDetection();
        UpdateWallPaintEffectPlanes();
        Debug.Log("ARPlaneDebugger: принудительно запущено обнаружение плоскостей");
    }
} 