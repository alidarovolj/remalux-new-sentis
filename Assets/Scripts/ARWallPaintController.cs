using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using Unity.XR.CoreUtils;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using InputSystemTrackedPoseDriver = UnityEngine.InputSystem.XR.TrackedPoseDriver;
#endif
using UnityEngine.SpatialTracking;
using SpatialTrackedPoseDriver = UnityEngine.SpatialTracking.TrackedPoseDriver;

/// <summary>
/// Оптимизированный контроллер для AR-приложения покраски стен
/// </summary>
public class ARWallPaintController : MonoBehaviour
{
    [Header("AR компоненты")]
    [SerializeField] private XROrigin xrOrigin;
    [SerializeField] private ARPlaneManager arPlaneManager;
    [SerializeField] private ARAnchorManager arAnchorManager;
    [SerializeField] private ARRaycastManager arRaycastManager;
    
    [Header("Настройки обнаружения")]
    [SerializeField] private bool preferVerticalPlanes = true;
    [SerializeField] private bool showARPlanes = true;
    
    [Header("Покраска")]
    [SerializeField] private Material wallPaintMaterial;
    [SerializeField] private bool persistPaintBetweenSessions = false;
    [SerializeField] private Color defaultPaintColor = Color.white;
    
    [Header("Отладка")]
    [SerializeField] public bool debugMode = false;
    [SerializeField] private Color debugPlaneColor = Color.green;
    
    // Словарь для хранения якорей для каждой плоскости
    private Dictionary<TrackableId, ARAnchor> planeAnchors = new Dictionary<TrackableId, ARAnchor>();
    
    // Словарь для хранения материалов покраски для каждой плоскости
    private Dictionary<TrackableId, Material> planeMaterials = new Dictionary<TrackableId, Material>();
    
    private Color currentPaintColor;
    private Camera arCamera;
    
    private void Awake()
    {
        // Инициализация текущего цвета
        currentPaintColor = defaultPaintColor;
    }
    
    private void Start()
    {
        Debug.Log("[ARWallPaintController] Start() - Инициализация контроллера");
        
        // Автоматическое получение ссылок, если не назначены вручную
        if (xrOrigin == null)
            xrOrigin = FindObjectOfType<XROrigin>();
            
        if (arPlaneManager == null && xrOrigin != null)
            arPlaneManager = xrOrigin.GetComponent<ARPlaneManager>();
            
        if (arAnchorManager == null && xrOrigin != null)
            arAnchorManager = xrOrigin.GetComponent<ARAnchorManager>();
            
        if (arRaycastManager == null && xrOrigin != null)
            arRaycastManager = xrOrigin.GetComponent<ARRaycastManager>();
        
        // Получаем AR-камеру
        if (xrOrigin != null)
            arCamera = xrOrigin.Camera;
            
        // Проверяем, что камера имеет компонент для отслеживания движения
        if (arCamera != null)
        {
            GameObject cameraObj = arCamera.gameObject;
            
            // Проверяем наличие TrackedPoseDriver разных типов
            bool hasPoseDriver = false;
            
            #if ENABLE_INPUT_SYSTEM
            // Проверяем новый TrackedPoseDriver из Input System
            if (cameraObj.GetComponent<InputSystemTrackedPoseDriver>() != null)
            {
                hasPoseDriver = true;
                Debug.Log("[ARWallPaintController] Найден TrackedPoseDriver из Input System");
            }
            #endif
            
            // Проверяем старый TrackedPoseDriver
            if (!hasPoseDriver && cameraObj.GetComponent<SpatialTrackedPoseDriver>() != null)
            {
                hasPoseDriver = true;
                Debug.Log("[ARWallPaintController] Найден TrackedPoseDriver из SpatialTracking");
            }
            
            // Если нет ни одного компонента, выводим предупреждение
            if (!hasPoseDriver)
            {
                Debug.LogWarning("[ARWallPaintController] AR-камера не имеет компонента TrackedPoseDriver. Движение камеры может не отслеживаться корректно.");
            }
        }
        
        // Настройка обнаружения плоскостей
        if (arPlaneManager != null)
        {
            // Настройка режима обнаружения плоскостей
            if (preferVerticalPlanes)
                arPlaneManager.requestedDetectionMode = PlaneDetectionMode.Vertical;
            else
                arPlaneManager.requestedDetectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
            
            // Отображение визуализации AR-плоскостей
            arPlaneManager.enabled = true;
            
            // Подписка на события обнаружения и обновления плоскостей
            arPlaneManager.planesChanged += OnPlanesChanged;
            
            Debug.Log($"[ARWallPaintController] ARPlaneManager настроен: detectionMode={arPlaneManager.requestedDetectionMode}, визуализация={showARPlanes}");
        }
        else
        {
            Debug.LogError("[ARWallPaintController] ARPlaneManager не найден! Обнаружение плоскостей невозможно.");
        }
        
        // Инициализация сохранения данных между сессиями, если включено
        if (persistPaintBetweenSessions)
        {
            InitializePersistentStorage();
        }
    }
    
    /// <summary>
    /// Инициализация системы сохранения данных между сессиями
    /// </summary>
    private void InitializePersistentStorage()
    {
        Debug.Log("[ARWallPaintController] Инициализация хранения данных между сессиями");
        // Загрузка сохраненных данных из PlayerPrefs или другого хранилища
        // Код загрузки добавляется здесь
    }
    
    private void OnDestroy()
    {
        if (arPlaneManager != null)
        {
            arPlaneManager.planesChanged -= OnPlanesChanged;
        }
        
        // Сохранение данных, если включено
        if (persistPaintBetweenSessions)
        {
            SavePaintDataToPersistentStorage();
        }
    }
    
    /// <summary>
    /// Сохранение данных о покраске стен в постоянное хранилище
    /// </summary>
    private void SavePaintDataToPersistentStorage()
    {
        Debug.Log("[ARWallPaintController] Сохранение данных о покраске");
        // Код сохранения в PlayerPrefs или другое хранилище
        // Добавляется здесь
    }
    
    private void OnPlanesChanged(ARPlanesChangedEventArgs args)
    {
        // Обработка новых плоскостей
        foreach (ARPlane plane in args.added)
        {
            ProcessNewPlane(plane);
        }
        
        // Обработка обновленных плоскостей
        foreach (ARPlane plane in args.updated)
        {
            UpdatePlane(plane);
        }
        
        // Обработка удаленных плоскостей
        foreach (ARPlane plane in args.removed)
        {
            RemovePlane(plane);
        }
        
        Debug.Log($"[ARWallPaintController] Всего обнаружено плоскостей: {arPlaneManager.trackables.count}");
    }
    
    /// <summary>
    /// Обработка новой обнаруженной плоскости
    /// </summary>
    private void ProcessNewPlane(ARPlane plane)
    {
        // Проверяем, что это вертикальная плоскость, если нужны только вертикальные
        if (preferVerticalPlanes && plane.alignment != PlaneAlignment.Vertical)
        {
            return;
        }
        
        // Создаем якорь для плоскости, если он еще не существует
        CreateOrGetAnchorForPlane(plane);
        
        // Настройка визуализации плоскости
        ConfigurePlaneVisualization(plane, showARPlanes);
        
        // Применение материала к плоскости
        ApplyMaterialToPlane(plane);
        
        if (debugMode)
        {
            Debug.Log($"[ARWallPaintController] Новая плоскость: ID={plane.trackableId}, " +
                      $"Ориентация={plane.alignment}, Размер={plane.size}");
        }
    }
    
    /// <summary>
    /// Обновление параметров плоскости при изменении
    /// </summary>
    private void UpdatePlane(ARPlane plane)
    {
        // Проверка якоря и обновление при необходимости
        CreateOrGetAnchorForPlane(plane);
        
        // Обновление материала плоскости
        UpdatePlaneMaterial(plane);
        
        if (debugMode)
        {
            Debug.Log($"[ARWallPaintController] Обновлена плоскость: ID={plane.trackableId}, " +
                      $"Новый размер={plane.size}");
        }
    }
    
    /// <summary>
    /// Обработка удаления плоскости
    /// </summary>
    private void RemovePlane(ARPlane plane)
    {
        // Удаление якоря плоскости, если он существует
        if (planeAnchors.TryGetValue(plane.trackableId, out ARAnchor anchor))
        {
            Destroy(anchor.gameObject);
            planeAnchors.Remove(plane.trackableId);
        }
        
        // Удаление материала плоскости из словаря
        if (planeMaterials.ContainsKey(plane.trackableId))
        {
            planeMaterials.Remove(plane.trackableId);
        }
        
        if (debugMode)
        {
            Debug.Log($"[ARWallPaintController] Удалена плоскость: ID={plane.trackableId}");
        }
    }
    
    /// <summary>
    /// Создание или получение якоря для плоскости
    /// </summary>
    private ARAnchor CreateOrGetAnchorForPlane(ARPlane plane)
    {
        // Проверяем, существует ли уже якорь для этой плоскости
        if (planeAnchors.TryGetValue(plane.trackableId, out ARAnchor existingAnchor))
        {
            // Якорь существует, проверяем, не был ли он уничтожен
            if (existingAnchor != null)
            {
                return existingAnchor;
            }
            
            // Якорь был уничтожен, удаляем его из словаря
            planeAnchors.Remove(plane.trackableId);
        }
        
        // Создаем новый якорь для плоскости
        // Исправленный вызов метода AttachAnchor с правильным количеством аргументов
        ARAnchor newAnchor = arAnchorManager.AttachAnchor(plane, new Pose(plane.transform.position, plane.transform.rotation));
        
        if (newAnchor != null)
        {
            planeAnchors[plane.trackableId] = newAnchor;
            
            if (debugMode)
            {
                Debug.Log($"[ARWallPaintController] Создан якорь для плоскости: ID={plane.trackableId}");
            }
            
            return newAnchor;
        }
        else
        {
            Debug.LogError($"[ARWallPaintController] Ошибка создания якоря для плоскости: ID={plane.trackableId}");
            return null;
        }
    }
    
    /// <summary>
    /// Настройка визуализации плоскости
    /// </summary>
    private void ConfigurePlaneVisualization(ARPlane plane, bool showPlane)
    {
        // Получаем компонент визуализации плоскости
        ARPlaneMeshVisualizer visualizer = plane.GetComponent<ARPlaneMeshVisualizer>();
        
        if (visualizer != null)
        {
            // Получаем MeshRenderer для плоскости
            MeshRenderer meshRenderer = plane.GetComponent<MeshRenderer>();
            
            if (meshRenderer != null)
            {
                // Включаем или отключаем рендеринг плоскости в зависимости от настроек
                meshRenderer.enabled = showPlane;
                
                if (debugMode && meshRenderer.enabled)
                {
                    // В режиме отладки устанавливаем цвет плоскости
                    MaterialPropertyBlock props = new MaterialPropertyBlock();
                    meshRenderer.GetPropertyBlock(props);
                    props.SetColor("_Color", debugPlaneColor);
                    meshRenderer.SetPropertyBlock(props);
                }
            }
        }
    }
    
    /// <summary>
    /// Применение материала покраски к плоскости
    /// </summary>
    private void ApplyMaterialToPlane(ARPlane plane)
    {
        // Проверяем, подходит ли плоскость для покраски (вертикальная, если нужны только вертикальные)
        if (preferVerticalPlanes && plane.alignment != PlaneAlignment.Vertical)
        {
            return;
        }
        
        // Создаем уникальный экземпляр материала для каждой плоскости
        if (wallPaintMaterial != null)
        {
            Material uniqueMaterial = new Material(wallPaintMaterial);
            uniqueMaterial.color = currentPaintColor;
            
            // Настраиваем материал для использования мировых координат, а не координат экрана
            uniqueMaterial.SetMatrix("_WorldToCameraMatrix", arCamera.worldToCameraMatrix);
            uniqueMaterial.SetMatrix("_CameraInverseProjection", arCamera.projectionMatrix.inverse);
            
            // Сохраняем материал в словаре
            planeMaterials[plane.trackableId] = uniqueMaterial;
            
            // Применяем материал к MeshRenderer плоскости
            MeshRenderer meshRenderer = plane.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                meshRenderer.material = uniqueMaterial;
            }
            
            if (debugMode)
            {
                Debug.Log($"[ARWallPaintController] Применен материал к плоскости: ID={plane.trackableId}");
            }
        }
    }
    
    /// <summary>
    /// Обновление материала плоскости
    /// </summary>
    private void UpdatePlaneMaterial(ARPlane plane)
    {
        if (planeMaterials.TryGetValue(plane.trackableId, out Material material))
        {
            // Обновляем матрицы трансформации для материала
            material.SetMatrix("_WorldToCameraMatrix", arCamera.worldToCameraMatrix);
            material.SetMatrix("_CameraInverseProjection", arCamera.projectionMatrix.inverse);
            
            // Обновляем позицию и ориентацию плоскости в шейдере
            material.SetVector("_PlanePosition", plane.transform.position);
            material.SetVector("_PlaneNormal", plane.transform.up);
        }
    }
    
    /// <summary>
    /// Изменение цвета покраски
    /// </summary>
    public void SetPaintColor(Color newColor)
    {
        currentPaintColor = newColor;
        
        // Обновляем цвет для всех существующих материалов
        foreach (var materialEntry in planeMaterials)
        {
            if (materialEntry.Value != null)
            {
                materialEntry.Value.color = newColor;
            }
        }
        
        Debug.Log($"[ARWallPaintController] Установлен новый цвет покраски: {newColor}");
    }
    
    /// <summary>
    /// Включение/отключение визуализации AR-плоскостей
    /// </summary>
    public void ToggleARPlanesVisibility(bool show)
    {
        showARPlanes = show;
        
        // Обновляем видимость для всех существующих плоскостей
        foreach (ARPlane plane in arPlaneManager.trackables)
        {
            ConfigurePlaneVisualization(plane, showARPlanes);
        }
        
        Debug.Log($"[ARWallPaintController] Видимость AR-плоскостей: {showARPlanes}");
    }
    
    /// <summary>
    /// Включение/отключение отладочного режима
    /// </summary>
    public void ToggleDebugMode(bool enable)
    {
        debugMode = enable;
        Debug.Log($"[ARWallPaintController] Отладочный режим: {debugMode}");
    }
} 