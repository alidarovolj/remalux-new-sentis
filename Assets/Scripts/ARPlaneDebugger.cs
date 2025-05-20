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

    [Header("Настройки видимости плоскостей")]
    [SerializeField] private bool filterSmallPlanes = true; // Фильтровать маленькие плоскости
    [SerializeField] private float minimumPlaneArea = 0.15f; // Минимальная площадь для отображения плоскости
    [SerializeField] private bool showOnlyStablePlanes = true; // Показывать только стабильные плоскости
    [SerializeField] private float planeStabilityTime = 0.5f; // Время в секундах для признания плоскости стабильной
    [SerializeField] private bool showMainPlaneOnly = false; // Показывать только самую большую плоскость
    [SerializeField] private bool useDelayedPlaneDisplay = true; // Использовать задержку перед отображением плоскостей

    [Header("Настройки сохранения плоскостей")]
    [SerializeField] private bool highlightSavedPlanes = true; // Подсвечивать сохраненные плоскости
    [SerializeField] private Color savedPlaneColor = new Color(0.4f, 1.0f, 0.4f, 0.7f); // Цвет сохраненных плоскостей
    [SerializeField] private bool showSavedPlanesCounter = true; // Показывать счетчик сохраненных плоскостей
    [SerializeField] private bool createResetButton = true; // Создать кнопку сброса сохраненных плоскостей

    // Дополнительные переменные для отслеживания состояния плоскостей
    private Dictionary<TrackableId, GameObject> planeVisualizers = new Dictionary<TrackableId, GameObject>();
    private Dictionary<TrackableId, float> planeDetectionTimes = new Dictionary<TrackableId, float>();
    private Dictionary<TrackableId, ARPlane> hiddenPlanes = new Dictionary<TrackableId, ARPlane>();
    private HashSet<TrackableId> savedPlaneIds = new HashSet<TrackableId>(); // ID сохраненных плоскостей
    private TrackableId largestPlaneId = TrackableId.invalidId;
    private float largestPlaneArea = 0f;
    private int prevPlaneCount = 0;
    private bool isInitialScanComplete = false;
    private ARPlaneConfigurator planeConfigurator; // Ссылка на конфигуратор плоскостей
    private Button resetButton; // Кнопка для сброса сохраненных плоскостей

    private void Awake()
    {
        if (planeManager == null)
            planeManager = FindObjectOfType<ARPlaneManager>();

        if (arSession == null)
            arSession = FindObjectOfType<ARSession>();

        // Находим ARPlaneConfigurator
        planeConfigurator = FindObjectOfType<ARPlaneConfigurator>();
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

        // Запускаем корутину для задержки начального отображения плоскостей
        if (useDelayedPlaneDisplay)
        {
            StartCoroutine(EnableInitialScanDelay());
        }

        // Создаем кнопку сброса, если нужно
        if (createResetButton)
        {
            CreateResetSavedPlanesButton();
        }
    }

    // Создание кнопки сброса сохраненных плоскостей
    private void CreateResetSavedPlanesButton()
    {
        Canvas canvas = FindObjectOfType<Canvas>();
        if (canvas == null)
        {
            // Создаем Canvas, если его нет
            GameObject canvasObj = new GameObject("DebugCanvas");
            canvas = canvasObj.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasObj.AddComponent<CanvasScaler>();
            canvasObj.AddComponent<GraphicRaycaster>();
        }

        // Создаем кнопку
        GameObject buttonObj = new GameObject("ResetPlanesButton");
        buttonObj.transform.SetParent(canvas.transform, false);

        // Настраиваем RectTransform
        RectTransform rectTransform = buttonObj.AddComponent<RectTransform>();
        rectTransform.anchorMin = new Vector2(0, 0);
        rectTransform.anchorMax = new Vector2(0, 0);
        rectTransform.pivot = new Vector2(0, 0);
        rectTransform.anchoredPosition = new Vector2(20, 20);
        rectTransform.sizeDelta = new Vector2(180, 60);

        // Добавляем изображение для фона кнопки
        Image image = buttonObj.AddComponent<Image>();
        image.color = new Color(0.2f, 0.2f, 0.2f, 0.8f);

        // Добавляем компонент кнопки
        resetButton = buttonObj.AddComponent<Button>();
        resetButton.targetGraphic = image;

        // Добавляем текст кнопки
        GameObject textObj = new GameObject("ButtonText");
        textObj.transform.SetParent(buttonObj.transform, false);

        RectTransform textRectTransform = textObj.AddComponent<RectTransform>();
        textRectTransform.anchorMin = Vector2.zero;
        textRectTransform.anchorMax = Vector2.one;
        textRectTransform.sizeDelta = Vector2.zero;

        Text buttonText = textObj.AddComponent<Text>();
        buttonText.text = "Сбросить плоскости";
        buttonText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        buttonText.fontSize = 18;
        buttonText.alignment = TextAnchor.MiddleCenter;
        buttonText.color = Color.white;

        // Добавляем обработчик нажатия
        resetButton.onClick.AddListener(ResetSavedPlanes);

        Debug.Log("ARPlaneDebugger: создана кнопка сброса сохраненных плоскостей");
    }

    // Метод сброса сохраненных плоскостей
    private void ResetSavedPlanes()
    {
        if (planeConfigurator != null)
        {
            Debug.Log("ARPlaneDebugger: Вызван сброс сохраненных плоскостей");
            planeConfigurator.ResetSavedPlanes();

            // Очищаем локальный список сохраненных ID
            savedPlaneIds.Clear();
        }
        else
        {
            Debug.LogWarning("ARPlaneDebugger: ARPlaneConfigurator не найден, невозможно сбросить сохраненные плоскости");
        }
    }

    // Корутина для задержки начального сканирования
    private IEnumerator EnableInitialScanDelay()
    {
        yield return new WaitForSeconds(3.0f); // Даем некоторое время на начальное сканирование
        isInitialScanComplete = true;
        Debug.Log("ARPlaneDebugger: Завершено начальное сканирование");

        // Обновляем видимость всех плоскостей
        UpdateAllPlanesVisibility();
    }

    // Метод для обновления видимости всех плоскостей
    private void UpdateAllPlanesVisibility()
    {
        if (planeManager == null) return;

        if (showMainPlaneOnly)
        {
            // Находим самую большую плоскость
            FindLargestPlane();
        }

        foreach (ARPlane plane in planeManager.trackables)
        {
            UpdatePlaneVisibility(plane);

            // Если это сохраненная плоскость, обновляем ее визуализацию
            if (savedPlaneIds.Contains(plane.trackableId) && highlightSavedPlanes)
            {
                HighlightSavedPlane(plane);
            }
        }
    }

    // Подсветка сохраненной плоскости
    private void HighlightSavedPlane(ARPlane plane)
    {
        if (!highlightSavedPlanes) return;

        MeshRenderer renderer = plane.GetComponent<MeshRenderer>();
        if (renderer != null && renderer.material != null)
        {
            // Создаем материал для подсветки, если нужно
            Material savedMaterial = renderer.material;

            // Меняем цвет материала
            savedMaterial.color = savedPlaneColor;

            // Если у материала есть специальные свойства для подсветки, устанавливаем их
            if (savedMaterial.HasProperty("_IsSaved"))
            {
                savedMaterial.SetFloat("_IsSaved", 1.0f);
            }

            // Увеличиваем альфа-канал для лучшей видимости
            if (savedMaterial.HasProperty("_Color"))
            {
                Color color = savedMaterial.GetColor("_Color");
                color.a = Mathf.Min(0.8f, color.a);
                savedMaterial.SetColor("_Color", color);
            }
        }
    }

    // Метод для нахождения самой большой плоскости
    private void FindLargestPlane()
    {
        largestPlaneArea = 0f;
        largestPlaneId = TrackableId.invalidId;

        foreach (ARPlane plane in planeManager.trackables)
        {
            float area = plane.size.x * plane.size.y;
            if (area > largestPlaneArea)
            {
                largestPlaneArea = area;
                largestPlaneId = plane.trackableId;
            }
        }
    }

    // Метод для обновления видимости одной плоскости
    private void UpdatePlaneVisibility(ARPlane plane)
    {
        if (plane == null) return;

        MeshRenderer renderer = plane.GetComponent<MeshRenderer>();
        if (renderer == null) return;

        bool shouldShow = true;
        float planeArea = plane.size.x * plane.size.y;

        // Если это сохраненная плоскость, всегда показываем
        if (savedPlaneIds.Contains(plane.trackableId))
        {
            shouldShow = true;
        }
        else
        {
            // Проверяем различные условия видимости
            if (filterSmallPlanes && planeArea < minimumPlaneArea)
            {
                shouldShow = false;
            }

            if (showOnlyStablePlanes && planeDetectionTimes.TryGetValue(plane.trackableId, out float detectionTime))
            {
                if (Time.time - detectionTime < planeStabilityTime)
                {
                    shouldShow = false;
                }
            }

            if (showMainPlaneOnly && plane.trackableId != largestPlaneId)
            {
                shouldShow = false;
            }
        }

        // Если плоскость не должна быть показана, скрываем ее
        renderer.enabled = shouldShow && isInitialScanComplete;

        // Сохраняем скрытые плоскости для отслеживания
        if (!shouldShow && !hiddenPlanes.ContainsKey(plane.trackableId))
        {
            hiddenPlanes[plane.trackableId] = plane;
        }
        else if (shouldShow && hiddenPlanes.ContainsKey(plane.trackableId))
        {
            hiddenPlanes.Remove(plane.trackableId);
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

            // Обновляем основную плоскость, если нужно
            if (showMainPlaneOnly)
            {
                FindLargestPlane();
            }
        }

        // Проверяем, если ARSession перезапустилась
        CheckARSessionState();

        // Периодическое обновление видимости плоскостей
        if (Time.frameCount % 30 == 0) // Примерно каждые полсекунды при 60 FPS
        {
            UpdateAllPlanesVisibility();
        }

        // Синхронизируем список сохраненных плоскостей с ARPlaneConfigurator
        if (Time.frameCount % 60 == 0 && planeConfigurator != null)
        {
            SyncSavedPlanesWithConfigurator();
        }
    }

    // Синхронизация списка сохраненных плоскостей с конфигуратором
    private void SyncSavedPlanesWithConfigurator()
    {
        if (planeConfigurator == null) return;

        // Получаем список сохраненных плоскостей из ARPlaneConfigurator
        HashSet<TrackableId> configuratorPlaneIds = planeConfigurator.GetPersistentPlaneIds();

        // Обновляем локальный список
        savedPlaneIds = configuratorPlaneIds;

        // Обновляем визуализацию всех сохраненных плоскостей
        if (highlightSavedPlanes && planeManager != null)
        {
            foreach (ARPlane plane in planeManager.trackables)
            {
                if (savedPlaneIds.Contains(plane.trackableId))
                {
                    HighlightSavedPlane(plane);
                }
            }
        }

        // Получаем информацию о стабильных плоскостях
        var (hasStablePlanes, count) = planeConfigurator.GetStablePlanesInfo();
        if (hasStablePlanes && count > 0)
        {
            Debug.Log($"ARPlaneDebugger: Синхронизировано {count} стабильных плоскостей");
        }
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
                // Сохраняем время обнаружения плоскости
                if (!planeDetectionTimes.ContainsKey(plane.trackableId))
                {
                    planeDetectionTimes.Add(plane.trackableId, Time.time);
                }

                float planeArea = plane.size.x * plane.size.y;
                Debug.Log($"ARPlaneDebugger: добавлена плоскость {plane.trackableId}, выравнивание: {plane.alignment}, " +
                    $"классификация: {plane.classification}, размер: {plane.size}, площадь: {planeArea}");

                // Создаем визуализацию для отладки
                if (enhanceVerticalPlanes && plane.alignment == PlaneAlignment.Vertical)
                {
                    CreatePlaneVisualizer(plane);
                }

                // Обновляем видимость плоскости в соответствии с настройками
                UpdatePlaneVisibility(plane);
            }

            foreach (var plane in args.updated)
            {
                // Проверяем, является ли плоскость сохраненной
                bool isSavedPlane = savedPlaneIds.Contains(plane.trackableId);

                // Если это сохраненная плоскость, подсвечиваем ее
                if (isSavedPlane && highlightSavedPlanes)
                {
                    HighlightSavedPlane(plane);
                }

                // Обновляем визуализатор вертикальной плоскости, если он уже есть
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

                // Обновляем видимость плоскости
                UpdatePlaneVisibility(plane);

                // Обновляем отслеживание самой большой плоскости, если необходимо
                if (showMainPlaneOnly)
                {
                    float planeArea = plane.size.x * plane.size.y;
                    if (planeArea > largestPlaneArea)
                    {
                        largestPlaneArea = planeArea;
                        largestPlaneId = plane.trackableId;
                        UpdateAllPlanesVisibility(); // Обновляем видимость всех плоскостей
                    }
                }
            }

            foreach (var plane in args.removed)
            {
                // Если это сохраненная плоскость, не обрабатываем ее удаление
                if (savedPlaneIds.Contains(plane.trackableId))
                {
                    continue;
                }

                Debug.Log($"ARPlaneDebugger: удалена плоскость {plane.trackableId}");

                // Удаляем визуализатор
                if (planeVisualizers.TryGetValue(plane.trackableId, out GameObject visualizer))
                {
                    Destroy(visualizer);
                    planeVisualizers.Remove(plane.trackableId);
                }

                // Удаляем из словаря обнаружения времени
                if (planeDetectionTimes.ContainsKey(plane.trackableId))
                {
                    planeDetectionTimes.Remove(plane.trackableId);
                }

                // Удаляем из скрытых плоскостей
                if (hiddenPlanes.ContainsKey(plane.trackableId))
                {
                    hiddenPlanes.Remove(plane.trackableId);
                }

                // Если это была самая большая плоскость, пересчитываем
                if (showMainPlaneOnly && plane.trackableId == largestPlaneId)
                {
                    FindLargestPlane();
                    UpdateAllPlanesVisibility();
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

        // Определяем цвет в зависимости от того, сохранена ли плоскость
        if (savedPlaneIds.Contains(plane.trackableId))
        {
            lineRenderer.startColor = savedPlaneColor;
            lineRenderer.endColor = savedPlaneColor;
        }
        else
        {
            lineRenderer.startColor = verticalPlaneColor;
            lineRenderer.endColor = verticalPlaneColor;
        }

        planeVisualizers.Add(plane.trackableId, visualizer);
        UpdatePlaneVisualizer(plane, visualizer);
    }

    private void UpdatePlaneVisualizer(ARPlane plane, GameObject visualizer)
    {
        LineRenderer lineRenderer = visualizer.GetComponent<LineRenderer>();
        if (lineRenderer == null) return;

        // Обновляем цвет, если плоскость сохранена
        if (savedPlaneIds.Contains(plane.trackableId))
        {
            lineRenderer.startColor = savedPlaneColor;
            lineRenderer.endColor = savedPlaneColor;
        }

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

        int totalPlanes = planeManager != null ? planeManager.trackables.count : 0;
        int visiblePlanes = totalPlanes - hiddenPlanes.Count;
        int verticalPlanes = 0;
        int horizontalPlanes = 0;
        int savedPlanesCount = savedPlaneIds.Count;

        if (planeManager != null)
        {
            foreach (ARPlane plane in planeManager.trackables)
            {
                if (plane.alignment == PlaneAlignment.Vertical)
                    verticalPlanes++;
                else if (plane.alignment == PlaneAlignment.HorizontalUp || plane.alignment == PlaneAlignment.HorizontalDown)
                    horizontalPlanes++;
            }
        }

        string statusInfo = $"Всего плоскостей: {totalPlanes}\n" +
                            $"Видимых: {visiblePlanes}\n" +
                            $"Скрытых: {hiddenPlanes.Count}\n" +
                            $"Вертикальных: {verticalPlanes}\n" +
                            $"Горизонтальных: {horizontalPlanes}\n";

        if (showSavedPlanesCounter)
        {
            statusInfo += $"Сохранено плоскостей: {savedPlanesCount}\n";
        }

        if (showMainPlaneOnly && largestPlaneId != TrackableId.invalidId)
        {
            statusInfo += $"Основная плоскость: {largestPlaneId.ToString().Substring(0, 8)}...\n" +
                         $"Площадь: {largestPlaneArea:F2} м²";
        }

        debugText.text = statusInfo;
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