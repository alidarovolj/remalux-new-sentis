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
    [SerializeField] private float planeStabilizationDelay = 2.0f; // Задержка перед стабилизацией плоскостей
    [SerializeField] private bool reducePlaneFlickering = true; // Уменьшить мерцание плоскостей
    [SerializeField] private float minPlaneAreaToDisplay = 0.1f; // Минимальная площадь для отображения плоскости

    [Header("Настройки сохранения плоскостей")]
    [SerializeField] private bool persistDetectedPlanes = true; // Сохранять обнаруженные плоскости
    [SerializeField] private float planeStabilityThreshold = 1.0f; // Время в секундах для признания плоскости стабильной
    [SerializeField] private float minAreaForPersistence = 0.3f; // Минимальная площадь для сохранения плоскости
    [SerializeField] private float mergeOverlapThreshold = 0.5f; // Порог перекрытия для объединения плоскостей (0-1)
    [SerializeField] private bool disablePlaneUpdatesAfterStabilization = true; // Отключить обновление плоскостей после стабилизации

    // Список отслеживаемых вертикальных плоскостей для стабилизации
    private Dictionary<TrackableId, ARPlane> trackedVerticalPlanes = new Dictionary<TrackableId, ARPlane>();
    // Якоря для стабилизации вертикальных плоскостей
    private Dictionary<TrackableId, ARAnchor> planeAnchors = new Dictionary<TrackableId, ARAnchor>();
    // Время обнаружения плоскостей для снижения мерцания
    private Dictionary<TrackableId, float> planeDetectionTimes = new Dictionary<TrackableId, float>();
    // Список стабильных плоскостей, которые мы хотим сохранить
    private Dictionary<TrackableId, ARPlane> stablePlanes = new Dictionary<TrackableId, ARPlane>();
    // Плоскости, которые мы пометили как "постоянные" (не будут удаляться)
    private HashSet<TrackableId> persistentPlaneIds = new HashSet<TrackableId>();
    // Регионы, которые уже покрыты стабильными плоскостями
    private List<Bounds> coveredRegions = new List<Bounds>();

    private ARAnchorManager anchorManager;
    private bool isInitialScanComplete = false;
    private bool hasStabilizedPlanes = false;

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
        // Запускаем корутину для стабилизации плоскостей после начального сканирования
        StartCoroutine(StabilizePlanesAfterDelay());
    }

    // Корутина для стабилизации плоскостей после задержки
    private IEnumerator StabilizePlanesAfterDelay()
    {
        yield return new WaitForSeconds(planeStabilizationDelay);
        isInitialScanComplete = true;

        if (showDebugInfo)
        {
            Debug.Log("ARPlaneConfigurator: Завершено начальное сканирование, применяется стабилизация плоскостей");
        }

        // Стабилизируем существующие плоскости
        StabilizeExistingPlanes();

        // Ожидаем еще некоторое время для накопления стабильных плоскостей
        yield return new WaitForSeconds(2.0f);

        // Закрепляем стабильные плоскости, если включена опция сохранения
        if (persistDetectedPlanes)
        {
            PersistStablePlanes();
        }
    }

    // Метод для закрепления стабильных плоскостей
    private void PersistStablePlanes()
    {
        if (!isInitialScanComplete || hasStabilizedPlanes) return;

        hasStabilizedPlanes = true;
        int persistedCount = 0;

        // Проходим через все отслеживаемые плоскости
        foreach (var plane in trackedVerticalPlanes.Values)
        {
            if (plane == null) continue;

            float planeArea = plane.size.x * plane.size.y;
            bool isStable = planeDetectionTimes.TryGetValue(plane.trackableId, out float detectionTime) &&
                           (Time.time - detectionTime) >= planeStabilityThreshold;

            // Если плоскость достаточно большая и стабильная
            if (isStable && planeArea >= minAreaForPersistence)
            {
                // Добавляем в список стабильных плоскостей
                if (!stablePlanes.ContainsKey(plane.trackableId))
                {
                    stablePlanes.Add(plane.trackableId, plane);
                    persistentPlaneIds.Add(plane.trackableId);

                    // Добавляем регион, покрытый этой плоскостью
                    Bounds planeBounds = GetPlaneBounds(plane);
                    coveredRegions.Add(planeBounds);

                    // Создаем якорь для этой плоскости, если еще не создан
                    if (!planeAnchors.ContainsKey(plane.trackableId))
                    {
                        CreateAnchorForPlane(plane);
                    }

                    persistedCount++;
                }
            }
        }

        if (showDebugInfo)
        {
            Debug.Log($"ARPlaneConfigurator: Закреплено {persistedCount} стабильных плоскостей");
        }

        // Если нужно отключить обновления плоскостей после стабилизации
        if (disablePlaneUpdatesAfterStabilization && persistedCount > 0)
        {
            // Отключаем обновление плоскостей, но не сам PlaneManager
            // Сначала сохраняем текущий режим обнаружения, затем временно отключаем
            PlaneDetectionMode currentMode = planeManager.requestedDetectionMode;
            planeManager.requestedDetectionMode = PlaneDetectionMode.None;

            // Через 1 секунду включаем обратно, но только для новых плоскостей
            StartCoroutine(ReenableLimitedPlaneDetection(currentMode));
        }
    }

    // Получение границ плоскости в мировых координатах
    private Bounds GetPlaneBounds(ARPlane plane)
    {
        Vector3 center = plane.center;
        Vector3 size = new Vector3(plane.size.x, 0.1f, plane.size.y);
        Bounds bounds = new Bounds(center, size);

        // Учитываем реальное вращение плоскости
        Matrix4x4 rotationMatrix = Matrix4x4.Rotate(plane.transform.rotation);
        bounds.extents = rotationMatrix.MultiplyVector(bounds.extents);

        return bounds;
    }

    // Проверка перекрытия с существующими стабильными плоскостями
    private bool OverlapsWithStablePlanes(ARPlane plane)
    {
        Bounds newPlaneBounds = GetPlaneBounds(plane);

        foreach (var bounds in coveredRegions)
        {
            // Проверяем пересечение границ
            if (bounds.Intersects(newPlaneBounds))
            {
                // Вычисляем примерный объем пересечения
                Bounds intersection = new Bounds();
                bool hasIntersection = CalculateBoundsIntersection(bounds, newPlaneBounds, out intersection);

                if (hasIntersection)
                {
                    // Вычисляем соотношение объема пересечения к объему новой плоскости
                    float intersectionVolume = intersection.size.x * intersection.size.y * intersection.size.z;
                    float planeVolume = newPlaneBounds.size.x * newPlaneBounds.size.y * newPlaneBounds.size.z;

                    if (planeVolume > 0 && (intersectionVolume / planeVolume) > mergeOverlapThreshold)
                    {
                        return true; // Есть значительное перекрытие
                    }
                }
            }
        }

        return false;
    }

    // Вычисление пересечения двух Bounds
    private bool CalculateBoundsIntersection(Bounds a, Bounds b, out Bounds intersection)
    {
        intersection = new Bounds();

        // Проверяем пересечение
        if (!a.Intersects(b))
            return false;

        // Находим минимальные и максимальные точки обоих bounds
        Vector3 min = Vector3.Max(a.min, b.min);
        Vector3 max = Vector3.Min(a.max, b.max);

        // Создаем новый bounds для пересечения
        intersection = new Bounds();
        intersection.SetMinMax(min, max);

        return true;
    }

    // Корутина для повторного включения обнаружения плоскостей, но с ограничениями
    private IEnumerator ReenableLimitedPlaneDetection(PlaneDetectionMode originalMode)
    {
        yield return new WaitForSeconds(1.0f);

        // Включаем обратно обнаружение плоскостей
        planeManager.requestedDetectionMode = originalMode;

        if (showDebugInfo)
        {
            Debug.Log("ARPlaneConfigurator: Обнаружение плоскостей включено снова, но с фильтрацией по перекрытию");
        }
    }

    // Метод для стабилизации существующих плоскостей
    private void StabilizeExistingPlanes()
    {
        if (planeManager == null) return;

        // Проходимся по всем активным плоскостям
        foreach (ARPlane plane in planeManager.trackables)
        {
            // Создаем якорь для плоскости, если еще не создан
            if (improveVerticalPlaneStability && IsVerticalPlane(plane) && !planeAnchors.ContainsKey(plane.trackableId))
            {
                CreateAnchorForPlane(plane);
            }
        }
    }

    /// <summary>
    /// Настраивает ARPlaneManager с правильными параметрами для обнаружения вертикальных плоскостей
    /// УЛУЧШЕНО согласно техническому отчету: более строгая фильтрация и стабилизация
    /// </summary>
    public void ConfigurePlaneManager()
    {
        if (planeManager == null) return;

        // Настраиваем режим обнаружения плоскостей согласно отчету
        PlaneDetectionMode detectionMode = PlaneDetectionMode.None;

        if (enableHorizontalPlanes)
            detectionMode |= PlaneDetectionMode.Horizontal;

        if (enableVerticalPlanes)
            detectionMode |= PlaneDetectionMode.Vertical;

        planeManager.requestedDetectionMode = detectionMode;

        // КРИТИЧЕСКИЕ НАСТРОЙКИ согласно отчету для борьбы с "мелкими квадратами"
        if (reducePlaneFlickering)
        {
            // Увеличиваем минимальную площадь плоскости более агрессивно
            float enhancedMinArea = Mathf.Max(minPlaneAreaToDisplay, 0.25f); // Минимум 0.25 м²
            SetFieldIfExists(planeManager, "m_MinimumPlaneArea", enhancedMinArea);

            // Устанавливаем более строгие пороги для стабильности
            SetFieldIfExists(planeManager, "m_PlaneStabilityThreshold", 0.8f);

            if (showDebugInfo)
            {
                Debug.Log($"ARPlaneConfigurator: Применены улучшенные фильтры - мин.площадь: {enhancedMinArea}м²");
            }
        }

        // Применяем дополнительные оптимизации для фильтрации согласно отчету
        SetFieldIfExists(planeManager, "m_TrackingQualityThreshold", 0.7f);

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
            Debug.Log($"ARPlaneConfigurator: Применены улучшенные фильтры согласно техническому отчету");
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

    // Хелперы для доступа к частным полям ARPlaneManager через рефлексию
    private bool SetFieldIfExists(object obj, string fieldName, object value)
    {
        var field = obj.GetType().GetField(fieldName, System.Reflection.BindingFlags.Instance |
                                    System.Reflection.BindingFlags.NonPublic |
                                    System.Reflection.BindingFlags.Public);
        if (field != null)
        {
            field.SetValue(obj, value);
            return true;
        }
        return false;
    }

    private bool TryGetFieldValue<T>(object obj, string fieldName, out T value)
    {
        value = default;
        var field = obj.GetType().GetField(fieldName, System.Reflection.BindingFlags.Instance |
                                    System.Reflection.BindingFlags.NonPublic |
                                    System.Reflection.BindingFlags.Public);
        if (field != null)
        {
            value = (T)field.GetValue(obj);
            return true;
        }
        return false;
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

            // Сохраняем время обнаружения для стабилизации
            if (!planeDetectionTimes.ContainsKey(plane.trackableId))
            {
                planeDetectionTimes.Add(plane.trackableId, Time.time);
            }

            // Если включено сохранение плоскостей и есть стабильные плоскости,
            // проверяем, не перекрывается ли новая плоскость с существующими стабильными
            if (persistDetectedPlanes && hasStabilizedPlanes && coveredRegions.Count > 0)
            {
                // Если плоскость перекрывается с существующими стабильными плоскостями,
                // отключаем ее отображение и пропускаем дальнейшую обработку
                if (OverlapsWithStablePlanes(plane))
                {
                    MeshRenderer renderer = plane.GetComponent<MeshRenderer>();
                    if (renderer != null)
                    {
                        renderer.enabled = false;
                    }
                    continue;
                }
            }

            // Фильтруем маленькие плоскости, если включено снижение мерцания
            if (reducePlaneFlickering && plane.size.x * plane.size.y < minPlaneAreaToDisplay)
            {
                // Маленькие плоскости не отображаем, но можем отслеживать
                MeshRenderer renderer = plane.GetComponent<MeshRenderer>();
                if (renderer != null)
                {
                    renderer.enabled = false;
                }
                continue;
            }

            if (isVertical)
            {
                if (showDebugInfo)
                {
                    Debug.Log($"ARPlaneConfigurator: Обнаружена вертикальная плоскость: {plane.trackableId}, площадь: {plane.size.x * plane.size.y}");
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
            // Если плоскость уже помечена как постоянная, игнорируем обновления ее геометрии
            // но обновляем ее материал для правильного отображения
            if (persistDetectedPlanes && persistentPlaneIds.Contains(plane.trackableId))
            {
                MeshRenderer renderer = plane.GetComponent<MeshRenderer>();
                if (renderer != null && renderer.material != null)
                {
                    UpdatePlaneMaterialTransform(plane, renderer.material);
                }
                continue;
            }

            bool isVertical = IsVerticalPlane(plane);

            // Если включено сохранение плоскостей и есть стабильные плоскости,
            // проверяем, не перекрывается ли обновленная плоскость с существующими стабильными
            if (persistDetectedPlanes && hasStabilizedPlanes && coveredRegions.Count > 0)
            {
                // Если плоскость перекрывается с существующими стабильными плоскостями,
                // отключаем ее отображение
                if (OverlapsWithStablePlanes(plane))
                {
                    MeshRenderer renderer = plane.GetComponent<MeshRenderer>();
                    if (renderer != null)
                    {
                        renderer.enabled = false;
                    }
                    continue;
                }
            }

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
            // Если плоскость помечена как постоянная, игнорируем ее удаление
            if (persistDetectedPlanes && persistentPlaneIds.Contains(plane.trackableId))
            {
                // Оставляем плоскость в списке отслеживаемых и стабильных
                if (showDebugInfo)
                {
                    Debug.Log($"ARPlaneConfigurator: Игнорируем удаление стабильной плоскости {plane.trackableId}");
                }
                continue;
            }

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
    /// Сбрасывает все сохраненные плоскости и запускает новое обнаружение
    /// </summary>
    public void ResetSavedPlanes()
    {
        if (showDebugInfo)
        {
            Debug.Log("ARPlaneConfigurator: Сброс всех сохраненных плоскостей");
        }

        // Очищаем списки сохраненных плоскостей
        stablePlanes.Clear();
        persistentPlaneIds.Clear();
        coveredRegions.Clear();

        // Сбрасываем флаг стабилизации
        hasStabilizedPlanes = false;

        // Запускаем обнаружение плоскостей заново
        ResetAllPlanes();

        // Запускаем корутину для новой стабилизации
        StartCoroutine(StabilizePlanesAfterDelay());
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
        planeDetectionTimes.Clear();

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
    /// Возвращает список ID сохраненных плоскостей
    /// </summary>
    public HashSet<TrackableId> GetPersistentPlaneIds()
    {
        return new HashSet<TrackableId>(persistentPlaneIds);
    }

    /// <summary>
    /// Проверяет, является ли данная плоскость сохраненной
    /// </summary>
    public bool IsPersistentPlane(TrackableId planeId)
    {
        return persistentPlaneIds.Contains(planeId);
    }

    /// <summary>
    /// Возвращает информацию о состоянии сохранения плоскостей
    /// </summary>
    public (bool hasStablePlanes, int stablePlanesCount) GetStablePlanesInfo()
    {
        return (hasStabilizedPlanes, stablePlanes.Count);
    }

    /// <summary>
    /// Добавляет плоскость в список сохраненных вручную
    /// </summary>
    public bool AddPlaneToPersistent(ARPlane plane)
    {
        if (plane == null || persistentPlaneIds.Contains(plane.trackableId))
            return false;

        // Добавляем в список стабильных плоскостей
        stablePlanes[plane.trackableId] = plane;
        persistentPlaneIds.Add(plane.trackableId);

        // Добавляем регион, покрытый этой плоскостью
        Bounds planeBounds = GetPlaneBounds(plane);
        coveredRegions.Add(planeBounds);

        // Создаем якорь для этой плоскости, если еще не создан
        if (!planeAnchors.ContainsKey(plane.trackableId))
        {
            CreateAnchorForPlane(plane);
        }

        if (showDebugInfo)
        {
            Debug.Log($"ARPlaneConfigurator: Вручную добавлена стабильная плоскость {plane.trackableId}");
        }

        return true;
    }

    /// <summary>
    /// Удаляет плоскость из списка сохраненных
    /// </summary>
    public bool RemovePlaneFromPersistent(TrackableId planeId)
    {
        if (!persistentPlaneIds.Contains(planeId))
            return false;

        persistentPlaneIds.Remove(planeId);

        // Удаляем из списка стабильных плоскостей
        if (stablePlanes.ContainsKey(planeId))
        {
            stablePlanes.Remove(planeId);
        }

        // Удаляем якорь, если он был создан
        if (planeAnchors.TryGetValue(planeId, out ARAnchor anchor))
        {
            if (anchor != null)
            {
                Destroy(anchor.gameObject);
            }
            planeAnchors.Remove(planeId);
        }

        // Обновляем список покрытых регионов
        // Это более сложная операция, поэтому просто пересоздаем его
        coveredRegions.Clear();
        foreach (var plane in stablePlanes.Values)
        {
            if (plane != null)
            {
                Bounds planeBounds = GetPlaneBounds(plane);
                coveredRegions.Add(planeBounds);
            }
        }

        if (showDebugInfo)
        {
            Debug.Log($"ARPlaneConfigurator: Вручную удалена стабильная плоскость {planeId}");
        }

        return true;
    }
}