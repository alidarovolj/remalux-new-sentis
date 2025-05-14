using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Класс, который настраивает ARPlaneManager в рантайме для корректной работы с вертикальными плоскостями.
/// Решает проблему неверных настроек ARPlaneManager в сцене и привязки плоскостей к мировому пространству.
/// </summary>
public class ARPlaneConfigurator : MonoBehaviour
{
    [SerializeField] private ARPlaneManager planeManager;
    [SerializeField] private bool enableVerticalPlanes = true;
    [SerializeField] private bool enableHorizontalPlanes = true;
    [SerializeField] private bool showDebugInfo = true;
    [SerializeField] private bool improveVerticalPlaneStability = true; // Улучшение стабильности вертикальных плоскостей
    [SerializeField] private Material verticalPlaneMaterial; // Материал для вертикальных плоскостей

    // Список отслеживаемых вертикальных плоскостей для стабилизации
    private Dictionary<TrackableId, ARPlane> trackedVerticalPlanes = new Dictionary<TrackableId, ARPlane>();
    // Якоря для стабилизации вертикальных плоскостей
    private Dictionary<TrackableId, ARAnchor> planeAnchors = new Dictionary<TrackableId, ARAnchor>();

    private ARAnchorManager anchorManager;

    private void Awake()
    {
        if (planeManager == null)
        {
            planeManager = FindObjectOfType<ARPlaneManager>();
            if (planeManager == null)
            {
                Debug.LogError("ARPlaneConfigurator: ARPlaneManager не найден в сцене!");
                enabled = false;
                return;
            }
        }
        
        // Находим ARAnchorManager для создания якорей
        anchorManager = FindObjectOfType<ARAnchorManager>();
        if (anchorManager == null && improveVerticalPlaneStability)
        {
            Debug.LogWarning("ARPlaneConfigurator: ARAnchorManager не найден. Улучшение стабильности вертикальных плоскостей будет ограничено.");
        }
    }

    private void Start()
    {
        ConfigurePlaneManager();
    }

    /// <summary>
    /// Настраивает ARPlaneManager с правильными параметрами для обнаружения вертикальных плоскостей
    /// </summary>
    public void ConfigurePlaneManager()
    {
        if (planeManager == null) return;

        // Настраиваем режим обнаружения плоскостей
        PlaneDetectionMode detectionMode = PlaneDetectionMode.None;
        
        if (enableHorizontalPlanes)
            detectionMode |= PlaneDetectionMode.Horizontal;
            
        if (enableVerticalPlanes)
            detectionMode |= PlaneDetectionMode.Vertical;
            
        planeManager.requestedDetectionMode = detectionMode;
        
        // Обновляем состояние planeManager
        if (!planeManager.enabled)
        {
            planeManager.enabled = true;
            Debug.Log("ARPlaneConfigurator: ARPlaneManager был включен");
        }
        
        if (showDebugInfo)
        {
            Debug.Log($"ARPlaneConfigurator: Установлен режим обнаружения плоскостей: {detectionMode}");
            Debug.Log($"ARPlaneConfigurator: Вертикальные плоскости: {enableVerticalPlanes}, Горизонтальные плоскости: {enableHorizontalPlanes}");
        }
        
        // Настраиваем материал для плоскостей, если он назначен
        if (verticalPlaneMaterial != null && planeManager.planePrefab != null)
        {
            MeshRenderer prefabRenderer = planeManager.planePrefab.GetComponent<MeshRenderer>();
            if (prefabRenderer != null)
            {
                prefabRenderer.sharedMaterial = verticalPlaneMaterial;
                Debug.Log("ARPlaneConfigurator: Назначен пользовательский материал для плоскостей");
            }
        }
        
        // Убедимся, что планы отображаются правильно
        StartCoroutine(ValidatePlanePrefab());
        
        // Подписываемся на события обновления плоскостей
        planeManager.planesChanged += OnPlanesChanged;
    }
    
    private IEnumerator ValidatePlanePrefab()
    {
        yield return new WaitForSeconds(0.5f);
        
        if (planeManager.planePrefab == null)
        {
            Debug.LogWarning("ARPlaneConfigurator: planePrefab не назначен в ARPlaneManager!");
            yield break;
        }
        
        // Проверяем наличие всех необходимых компонентов в префабе
        ARPlaneMeshVisualizer meshVisualizer = planeManager.planePrefab.GetComponent<ARPlaneMeshVisualizer>();
        MeshRenderer meshRenderer = planeManager.planePrefab.GetComponent<MeshRenderer>();
        
        if (meshVisualizer == null)
        {
            Debug.LogWarning("ARPlaneConfigurator: В префабе плоскости отсутствует ARPlaneMeshVisualizer!");
        }
        
        if (meshRenderer == null)
        {
            Debug.LogWarning("ARPlaneConfigurator: В префабе плоскости отсутствует MeshRenderer!");
        }
        else if (meshRenderer.sharedMaterial == null)
        {
            Debug.LogWarning("ARPlaneConfigurator: В префабе плоскости не назначен материал!");
            
            // Пытаемся назначить материал
            if (verticalPlaneMaterial != null)
            {
                meshRenderer.sharedMaterial = verticalPlaneMaterial;
            }
            else
            {
                // Создаем простой материал как запасной вариант
                Material defaultMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                defaultMaterial.color = new Color(0.5f, 0.7f, 1.0f, 0.5f);
                meshRenderer.sharedMaterial = defaultMaterial;
            }
        }
        
        // Проверяем существующие плоскости
        ApplyMaterialToExistingPlanes();
    }
    
    /// <summary>
    /// Применяет материал ко всем существующим плоскостям
    /// </summary>
    private void ApplyMaterialToExistingPlanes()
    {
        ARPlane[] existingPlanes = FindObjectsOfType<ARPlane>();
        if (existingPlanes.Length == 0) return;
        
        Debug.Log($"ARPlaneConfigurator: Найдено {existingPlanes.Length} существующих плоскостей");
        
        foreach (ARPlane plane in existingPlanes)
        {
            if (IsVerticalPlane(plane) && verticalPlaneMaterial != null)
            {
                MeshRenderer renderer = plane.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    // Создаем экземпляр материала, чтобы каждая плоскость имела свой экземпляр
                    Material instanceMaterial = new Material(verticalPlaneMaterial);
                    renderer.material = instanceMaterial;
                    
                    // Активируем ключевые слова для привязки к мировому пространству AR
                    instanceMaterial.EnableKeyword("USE_AR_WORLD_SPACE");
                    
                    // Добавляем трансформацию плоскости в материал
                    instanceMaterial.SetMatrix("_PlaneToWorldMatrix", plane.transform.localToWorldMatrix);
                    instanceMaterial.SetVector("_PlaneNormal", plane.normal);
                    instanceMaterial.SetVector("_PlaneCenter", plane.center);
                    
                    // Если это вертикальная плоскость, настраиваем специальные параметры
                    renderer.material.SetFloat("_IsVertical", 1.0f);
                    
                    Debug.Log($"ARPlaneConfigurator: Применен материал к вертикальной плоскости {plane.trackableId}");
                    
                    // Добавляем в список отслеживаемых вертикальных плоскостей
                    if (!trackedVerticalPlanes.ContainsKey(plane.trackableId))
                    {
                        trackedVerticalPlanes.Add(plane.trackableId, plane);
                    }
                    
                    // Создаем якорь для этой плоскости, если включено улучшение стабильности
                    if (improveVerticalPlaneStability)
                    {
                        CreateAnchorForPlane(plane);
                    }
                }
            }
        }
    }
    
    /// <summary>
    /// Создает якорь для стабилизации плоскости в AR-пространстве
    /// </summary>
    private void CreateAnchorForPlane(ARPlane plane)
    {
        if (anchorManager == null || plane == null) return;
        
        // Проверяем, есть ли уже якорь для этой плоскости
        if (planeAnchors.ContainsKey(plane.trackableId)) return;
        
        // Создаем якорь в центре плоскости
        Pose anchorPose = new Pose(plane.center, Quaternion.LookRotation(plane.normal));
        ARAnchor anchor = anchorManager.AttachAnchor(plane, anchorPose);
        
        if (anchor != null)
        {
            planeAnchors.Add(plane.trackableId, anchor);
            Debug.Log($"ARPlaneConfigurator: Создан якорь для стабилизации плоскости {plane.trackableId}");
        }
    }
    
    private void OnPlanesChanged(ARPlanesChangedEventArgs args)
    {
        // Обрабатываем добавленные плоскости
        foreach (ARPlane plane in args.added)
        {
            // Проверяем, является ли плоскость вертикальной
            bool isVertical = IsVerticalPlane(plane);
            
            if (isVertical)
            {
                if (showDebugInfo)
                {
                    Debug.Log($"ARPlaneConfigurator: Обнаружена вертикальная плоскость: {plane.trackableId}");
                }
                
                // Добавляем в список отслеживаемых вертикальных плоскостей
                if (!trackedVerticalPlanes.ContainsKey(plane.trackableId))
                {
                    trackedVerticalPlanes.Add(plane.trackableId, plane);
                    
                    // Если у нас есть вертикальный материал, применяем его
                    if (verticalPlaneMaterial != null)
                    {
                        MeshRenderer renderer = plane.GetComponent<MeshRenderer>();
                        if (renderer != null)
                        {
                            // Создаем экземпляр материала для каждой плоскости
                            Material planeMaterial = new Material(verticalPlaneMaterial);
                            renderer.material = planeMaterial;
                            
                            // Настраиваем для привязки к AR-пространству
                            planeMaterial.EnableKeyword("USE_AR_WORLD_SPACE");
                            
                            // Применяем трансформацию плоскости
                            UpdatePlaneMaterialTransform(plane, planeMaterial);
                        }
                    }
                    
                    // Создаем якорь для стабилизации
                    if (improveVerticalPlaneStability)
                    {
                        CreateAnchorForPlane(plane);
                    }
                }
            }
        }
        
        // Обрабатываем обновленные плоскости
        foreach (ARPlane plane in args.updated)
        {
            bool isVertical = IsVerticalPlane(plane);
            
            // Если плоскость вертикальная и уже отслеживается
            if (isVertical && trackedVerticalPlanes.ContainsKey(plane.trackableId))
            {
                // Обновляем трансформацию в материале
                MeshRenderer renderer = plane.GetComponent<MeshRenderer>();
                if (renderer != null && renderer.material != null)
                {
                    UpdatePlaneMaterialTransform(plane, renderer.material);
                }
            }
            // Если плоскость стала вертикальной, добавляем в отслеживаемые
            else if (isVertical && !trackedVerticalPlanes.ContainsKey(plane.trackableId))
            {
                trackedVerticalPlanes.Add(plane.trackableId, plane);
                
                if (showDebugInfo)
                {
                    Debug.Log($"ARPlaneConfigurator: Плоскость {plane.trackableId} стала вертикальной");
                }
                
                // Назначаем материал для вертикальной плоскости
                if (verticalPlaneMaterial != null)
                {
                    MeshRenderer renderer = plane.GetComponent<MeshRenderer>();
                    if (renderer != null)
                    {
                        Material planeMaterial = new Material(verticalPlaneMaterial);
                        renderer.material = planeMaterial;
                        planeMaterial.EnableKeyword("USE_AR_WORLD_SPACE");
                        UpdatePlaneMaterialTransform(plane, planeMaterial);
                    }
                }
                
                // Создаем якорь для стабилизации
                if (improveVerticalPlaneStability)
                {
                    CreateAnchorForPlane(plane);
                }
            }
        }
        
        // Обрабатываем удаленные плоскости
        foreach (ARPlane plane in args.removed)
        {
            // Удаляем из списка отслеживаемых
            trackedVerticalPlanes.Remove(plane.trackableId);
            
            // Удаляем якорь, если он был создан
            if (planeAnchors.TryGetValue(plane.trackableId, out ARAnchor anchor))
            {
                if (anchor != null)
                {
                    Destroy(anchor.gameObject);
                }
                planeAnchors.Remove(plane.trackableId);
            }
        }
    }
    
    /// <summary>
    /// Обновляет трансформацию плоскости в материале
    /// </summary>
    private void UpdatePlaneMaterialTransform(ARPlane plane, Material material)
    {
        if (material == null) return;
        
        material.SetMatrix("_PlaneToWorldMatrix", plane.transform.localToWorldMatrix);
        material.SetVector("_PlaneNormal", plane.normal);
        material.SetVector("_PlaneCenter", plane.center);
        
        // Добавляем уникальный идентификатор плоскости
        material.SetFloat("_PlaneID", plane.trackableId.subId1 % 1000);
    }
    
    /// <summary>
    /// Проверяет, является ли плоскость вертикальной (стеной)
    /// </summary>
    public static bool IsVerticalPlane(ARPlane plane)
    {
        if (plane.alignment == PlaneAlignment.Vertical)
            return true;
        
        // Дополнительная проверка по нормали (плоскость почти вертикальна)
        float dotUp = Vector3.Dot(plane.normal, Vector3.up);
        return Mathf.Abs(dotUp) < 0.25f; // Более строгое значение для определения вертикальности
    }
    
    /// <summary>
    /// Сбрасывает все обнаруженные плоскости для перезапуска обнаружения
    /// </summary>
    public void ResetAllPlanes()
    {
        if (planeManager == null) return;
        
        Debug.Log("ARPlaneConfigurator: Сброс всех AR плоскостей");
        
        // Очищаем списки отслеживаемых плоскостей и якорей
        trackedVerticalPlanes.Clear();
        
        // Удаляем все якоря
        foreach (var anchor in planeAnchors.Values)
        {
            if (anchor != null)
            {
                Destroy(anchor.gameObject);
            }
        }
        planeAnchors.Clear();
        
        // Временно отключаем плоскости и перезапускаем
        planeManager.enabled = false;
        
        // Небольшая задержка перед повторным включением
        StartCoroutine(ReenablePlaneManagerAfterDelay(0.5f));
    }
    
    private IEnumerator ReenablePlaneManagerAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        ConfigurePlaneManager();
    }
    
    private void OnDestroy()
    {
        if (planeManager != null)
        {
            planeManager.planesChanged -= OnPlanesChanged;
        }
        
        // Очищаем созданные якоря
        foreach (var anchor in planeAnchors.Values)
        {
            if (anchor != null)
            {
                Destroy(anchor.gameObject);
            }
        }
    }
} 