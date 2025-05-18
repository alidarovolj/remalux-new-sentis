using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Unity.XR.CoreUtils;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.Rendering.Universal;
using System;

/// <summary>
/// Класс для автоматического добавления ARManagerInitializer в сцену при старте игры
/// и управления плоскостями AR на основе маски сегментации
/// </summary>
[DefaultExecutionOrder(-10)] 
public class ARManagerInitializer2 : MonoBehaviour
{
    // Синглтон для глобального доступа
    public static ARManagerInitializer2 Instance { get; private set; }

    // Статический счетчик для уникальных имен плоскостей
    private static int planeInstanceCounter = 0;

    // Ссылки на AR компоненты
    [Header("AR компоненты")]
    public ARSessionManager sessionManager;
    public ARPlaneManager planeManager;
    public XROrigin xrOrigin;

    [Header("Настройки сегментации")]
    [Tooltip("Использовать обнаруженные плоскости вместо генерации из маски")]
    public bool useDetectedPlanes = false;
    
    [Tooltip("Минимальный размер плоскости для создания (в метрах)")]
    [SerializeField] private float minPlaneSizeInMeters = 0.1f;
    
    [Tooltip("Минимальный размер области в пикселях (ширина И высота) для её учета")]
    [SerializeField] private int minPixelsDimensionForArea = 2; // было minAreaSize
    
    [Tooltip("Минимальная площадь области в пикселях для её учета")]
    [SerializeField] private int minAreaSizeInPixels = 10; // Новое поле, раньше было minAreaSize = 2*2=4 или 50
    
    [Tooltip("Порог значения красного канала (0-255) для пикселя, чтобы считать его частью области стены при поиске связных областей.")]
    [SerializeField] private byte wallAreaDetectionThreshold = 30;
    
    // Добавляем защиту от удаления недавно созданных плоскостей
    private Dictionary<GameObject, float> planeCreationTimes = new Dictionary<GameObject, float>();
    
    [Tooltip("Материал для вертикальных плоскостей")]
    public Material verticalPlaneMaterial;
    
    [Tooltip("Материал для горизонтальных плоскостей")]
    public Material horizontalPlaneMaterial;

    // Поле для RawImage будет установлено извне
    private UnityEngine.UI.RawImage отображениеМаскиUI;

    // Текущая маска сегментации
    private RenderTexture currentSegmentationMask;
    private bool maskUpdated = false;
    private List<GameObject> generatedPlanes = new List<GameObject>();

    // Для отслеживания изменений количества плоскостей
    private int lastPlaneCount = 0;

    // Счетчик кадров для обновления плоскостей
    private int frameCounter = 0;

    // Переменная для отслеживания последнего хорошего результата сегментации
    private bool hadValidSegmentationResult = false;
    private float lastSuccessfulSegmentationTime = 0f;
    private float segmentationTimeoutSeconds = 10f; // Тайм-аут для определения "потери" сегментации

    // Переменная для хранения InstanceID TrackablesParent из Start()
    private int trackablesParentInstanceID_FromStart = 0;

    // [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)] // ЗАКОММЕНТИРОВАТЬ или УДАЛИТЬ
    // private static void Initialize()
    // {
    //     GameObject initializer = new GameObject("AR Manager Initializer 2");
    //     ARManagerInitializer2 component = initializer.AddComponent<ARManagerInitializer2>();
    //     component.useDetectedPlanes = false;
    //     DontDestroyOnLoad(initializer);
    //     Debug.Log("[ARManagerInitializer2] Инициализирован");
    // }

    private void Awake()
    {
        // Debug.Log("[ARManagerInitializer2] Awake() called.");
        if (Instance == null)
        {
            Instance = this;
            // Debug.Log("[ARManagerInitializer2] Instance set.");
            if (transform.parent == null) // Убедимся, что объект корневой перед DontDestroyOnLoad
            {
                // Debug.Log("[ARManagerInitializer2] Making it DontDestroyOnLoad as it's a root object.");
                DontDestroyOnLoad(gameObject);
            }
            else
            {
                // Debug.LogWarning("[ARManagerInitializer2] Instance is not a root object, not setting DontDestroyOnLoad. This might be an issue if it's destroyed on scene changes.");
            }
        }
        else if (Instance != this)
        {
            // Debug.LogWarning("[ARManagerInitializer2] Another instance already exists. Destroying this one.");
            Destroy(gameObject);
            return;
        }
        
        // Debug.Log($"[ARManagerInitializer2] Awake complete. Instance ID: {this.GetInstanceID()}, Name: {this.gameObject.name}");
    }

    private void Start()
    {
        // Debug.Log("[ARManagerInitializer2] Start() called.");

        FindARComponents();
        SubscribeToWallSegmentation();

        // Попытка отключить стандартный ARPlaneManager, если он есть и мы используем кастомную генерацию
        if (planeManager != null && useDetectedPlanes)
        {
            // Debug.LogWarning("[ARManagerInitializer2] Попытка отключить ARPlaneManager.");
            // planeManager.enabled = false;
            // if (!planeManager.enabled)
            // {
            //     Debug.Log("[ARManagerInitializer2] ARPlaneManager успешно отключен.");
            // }
            // else
            // {
            //     Debug.LogWarning("[ARManagerInitializer2] Не удалось отключить ARPlaneManager.");
            // }
        } else if (planeManager == null && useDetectedPlanes) {
            // Debug.LogWarning("[ARManagerInitializer2] planeManager не назначен, но useDetectedPlanes=true. Нечего отключать.");
        }


        // Debug.Log($"[ARManagerInitializer2] Настройки инициализированы: useDetectedPlanes={useDetectedPlanes}");

        if (xrOrigin != null && xrOrigin.TrackablesParent != null)
        {
            trackablesParentInstanceID_FromStart = xrOrigin.TrackablesParent.GetInstanceID();
            // Debug.Log($"[ARManagerInitializer2-Start] TrackablesParent is: {xrOrigin.TrackablesParent.name}, ID: {trackablesParentInstanceID_FromStart}, Path: {GetGameObjectPath(xrOrigin.TrackablesParent)}");
        }
        else
        {
            // Debug.LogError("[ARManagerInitializer2-Start] XROrigin or XROrigin.TrackablesParent is not assigned!");
        }
        // CreateBasicPlaneInFrontOfUser();
    }

    public void УстановитьОтображениеМаскиUI(UnityEngine.UI.RawImage rawImageДляУстановки)
    {
        if (rawImageДляУстановки != null)
        {
            отображениеМаскиUI = rawImageДляУстановки;
            // Debug.Log("[ARManagerInitializer2] Успешно установлен RawImage для отображения маски через УстановитьОтображениеМаскиUI.");
            if (currentSegmentationMask != null && отображениеМаскиUI.texture == null)
            {
                // Debug.Log("[ARManagerInitializer2] Немедленное применение текущей маски к новому RawImage.");
                отображениеМаскиUI.texture = currentSegmentationMask;
                отображениеМаскиUI.gameObject.SetActive(true);
            }
        }
        else
        {
            // Debug.LogWarning("[ARManagerInitializer2] Попытка установить null RawImage для отображения маски.");
        }
    }

    private void Update()
    {
        frameCounter++;

        if (maskUpdated)
        {
            ProcessSegmentationMask();
            maskUpdated = false;
        }

        // Обновление позиций существующих плоскостей (если они не привязаны к трекаблам XROrigin)
        // UpdatePlanePositions(); // Пока отключено, т.к. привязываем к TrackablesParent

        // Периодическая проверка, если сегментация "зависла"
        if (hadValidSegmentationResult && Time.time - lastSuccessfulSegmentationTime > segmentationTimeoutSeconds)
        {
            // Debug.LogWarning($"[ARManagerInitializer2] Нет успешной сегментации более {segmentationTimeoutSeconds} секунд. Сбрасываем состояние.");
            // ResetAllPlanes(); // Очищаем плоскости, если сегментация потеряна
            hadValidSegmentationResult = false; // Сбрасываем флаг, чтобы избежать повторных сбросов подряд
            // Можно также попробовать перезапустить подписку или сам WallSegmentation
        }


        // Отладка: Вывод количества плоскостей и их позиций
        // if (frameCounter % 120 == 0) // Каждые 120 кадров (примерно раз в 2 секунды)
        // {
        //     if (generatedPlanes != null)
        //     {
        //          Debug.Log($"[ARManagerInitializer2] 📊 Текущее количество плоскостей: {generatedPlanes.Count}");
        //          for (int i = 0; i < generatedPlanes.Count; i++)
        //          {
        //              if (generatedPlanes[i] != null)
        //              {
        //                  Debug.Log($"[ARManagerInitializer2-DebugPlanePos] Plane {i} (ID: {generatedPlanes[i].GetInstanceID()}) world position: {generatedPlanes[i].transform.position:F2}, rotation: {generatedPlanes[i].transform.eulerAngles:F2}, parent: {(generatedPlanes[i].transform.parent != null ? generatedPlanes[i].transform.parent.name : "null")}");
        //              }
        //          }
        //     }
            
        //     if (xrOrigin != null && xrOrigin.TrackablesParent != null)
        //     {
        //         // Debug.Log($"[ARManagerInitializer2-TrackableCheck-Update] Проверка TrackablesParent: {GetGameObjectPath(xrOrigin.TrackablesParent)} (ID: {xrOrigin.TrackablesParent.GetInstanceID()}). Количество дочерних объектов: {xrOrigin.TrackablesParent.childCount}");
        //         for (int i = 0; i < xrOrigin.TrackablesParent.childCount; i++)
        //         {
        //             Transform child = xrOrigin.TrackablesParent.GetChild(i);
        //             bool isOurPlane = false;
        //             foreach (var plane in generatedPlanes)
        //             {
        //                 if (plane != null && plane.transform == child)
        //                 {
        //                     isOurPlane = true;
        //                     break;
        //                 }
        //             }
        //             // if (child.name.StartsWith("MyARPlane_Debug_")) {
        //             //     Debug.Log($"[ARManagerInitializer2-TrackableCheck-Update] Child of Trackables: {child.name}, ID: {child.GetInstanceID()}, Path: {GetGameObjectPath(child)}");
        //             //     if (!isOurPlane) Debug.LogWarning($"[ARManagerInitializer2-TrackableCheck-Update] ВНИМАНИЕ! Найдена плоскость '{child.name}' (ID: {child.GetInstanceID()}) под TrackablesParent ({GetGameObjectPath(xrOrigin.TrackablesParent)}) но ее нет в списке generatedPlanes!");
        //             // }
        //         }
        //     }
        //     else
        //     {
        //         // Debug.LogError("[ARManagerInitializer2-Update] XROrigin or TrackablesParent is null in Update.");
        //     }
        // }
    }


    // Поиск необходимых компонентов в сцене
    private void FindARComponents()
    {
        if (sessionManager == null)
        {
            // Debug.Log("[ARManagerInitializer2] Поле sessionManager было null. Попытка найти ARSessionManager в сцене (включая неактивные объекты)...");
            sessionManager = FindObjectOfType<ARSessionManager>(true); // Ищем включая неактивные
            if (sessionManager != null)
            {
                // Debug.Log($"[ARManagerInitializer2] ✅ ARSessionManager успешно найден и назначен: {sessionManager.gameObject.name} (ID: {sessionManager.gameObject.GetInstanceID()}), активен: {sessionManager.gameObject.activeInHierarchy}");
            }
            else
            {
                // Debug.LogError("[ARManagerInitializer2] ❌ ARSessionManager не найден в сцене!");
            }
        }
        // else
        // {
            // Debug.Log($"[ARManagerInitializer2] Поле sessionManager уже было назначено: {sessionManager.gameObject.name} (ID: {sessionManager.gameObject.GetInstanceID()}), активен: {sessionManager.gameObject.activeInHierarchy}");
        // }

        if (xrOrigin == null)
        {
            // Debug.Log("[ARManagerInitializer2] Поле xrOrigin было null. Попытка найти XROrigin в сцене (включая неактивные объекты)...");
            xrOrigin = FindObjectOfType<XROrigin>(true);
             if (xrOrigin != null)
            {
                // Debug.Log($"[ARManagerInitializer2] ✅ XROrigin успешно найден и назначен: {xrOrigin.gameObject.name} (ID: {xrOrigin.gameObject.GetInstanceID()}), активен: {xrOrigin.gameObject.activeInHierarchy}");
            }
            else
            {
                // Debug.LogError("[ARManagerInitializer2] ❌ XROrigin не найден в сцене!");
            }
        }

        if (planeManager == null && xrOrigin != null)
        {
            // Debug.Log("[ARManagerInitializer2] Поле planeManager было null. Попытка найти ARPlaneManager на XROrigin...");
            planeManager = xrOrigin.GetComponent<ARPlaneManager>();
            if (planeManager != null) {
                // Debug.Log($"[ARManagerInitializer2] ✅ ARPlaneManager успешно найден на XROrigin: {planeManager.gameObject.name} (ID: {planeManager.gameObject.GetInstanceID()}), активен: {planeManager.gameObject.activeInHierarchy}, enabled: {planeManager.enabled}");
                // planeManager.planesChanged += OnPlanesChanged; // Подписываемся на события
                // Debug.Log("[ARManagerInitializer2] Подписано на события planesChanged");
            } else {
                // Debug.LogWarning("[ARManagerInitializer2] ARPlaneManager не найден на XROrigin. Возможно, он не используется или не настроен.");
            }
        }
        InitializeMaterials();
    }

    private void InitializeMaterials()
    {
        if (verticalPlaneMaterial == null)
        {
            // Debug.LogWarning("[ARManagerInitializer2] Материал для вертикальных плоскостей (verticalPlaneMaterial) не назначен. Создание стандартного материала.");
            verticalPlaneMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            verticalPlaneMaterial.color = new Color(1.0f, 0.5f, 0.0f, 0.5f); // Оранжевый полупрозрачный
            // Debug.Log("[ARManagerInitializer2] Создан материал для вертикальных плоскостей");
        }
        else
        {
            // Debug.Log("[ARManagerInitializer2] Используется назначенный материал для вертикальных плоскостей.");
        }

        if (horizontalPlaneMaterial == null)
        {
            // Debug.LogWarning("[ARManagerInitializer2] Материал для горизонтальных плоскостей (horizontalPlaneMaterial) не назначен. Создание стандартного материала.");
            horizontalPlaneMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            horizontalPlaneMaterial.color = new Color(0.0f, 1.0f, 0.5f, 0.5f); // Зеленый полупрозрачный
            // Debug.Log("[ARManagerInitializer2] Создан материал для горизонтальных плоскостей");
        }
        else
        {
            // Debug.Log("[ARManagerInitializer2] Используется назначенный материал для горизонтальных плоскостей.");
        }
    }


    private void SubscribeToWallSegmentation()
    {
        // Debug.Log("[ARManagerInitializer2] Попытка подписки на события WallSegmentation...");
        WallSegmentation wallSegmentationInstance = FindObjectOfType<WallSegmentation>();
        if (wallSegmentationInstance != null)
        {
            // Debug.Log($"[ARManagerInitializer2] Найден экземпляр WallSegmentation: {wallSegmentationInstance.gameObject.name}. Подписка на OnSegmentationMaskUpdated.");
            wallSegmentationInstance.OnSegmentationMaskUpdated -= OnSegmentationMaskUpdated; // Отписываемся на всякий случай
            wallSegmentationInstance.OnSegmentationMaskUpdated += OnSegmentationMaskUpdated; // Подписываемся
            // Debug.Log("[ARManagerInitializer2] ✅ Подписка на события OnSegmentationMaskUpdated настроена");
        }
        else
        {
            // Debug.LogError("[ARManagerInitializer2] ❌ Экземпляр WallSegmentation не найден в сцене. Невозможно подписаться на обновления маски.");
            // Попробуем переподписаться через некоторое время, если сцена еще загружается
            StartCoroutine(RetrySubscriptionAfterDelay(1.0f));
        }
    }

    // Повторная попытка подписки после задержки
    private IEnumerator RetrySubscriptionAfterDelay(float delay)
    {
        yield return new WaitForSeconds(delay);
        SubscribeToWallSegmentation();
    }
    
    // Обработчик события изменения плоскостей
    private void OnPlanesChanged(ARPlanesChangedEventArgs args)
    {
        foreach (ARPlane plane in args.added)
        {
            ConfigurePlane(plane);
        }
        
        foreach (ARPlane plane in args.updated)
        {
            UpdatePlane(plane);
        }
    }
    
    // Настройка новой плоскости
    private void ConfigurePlane(ARPlane plane)
    {
        if (plane == null) return;
        
        // Определяем, вертикальная ли это плоскость
        bool isVertical = plane.alignment == PlaneAlignment.Vertical;
        
        // Назначаем материал в зависимости от типа плоскости
        MeshRenderer renderer = plane.GetComponent<MeshRenderer>();
        if (renderer != null)
        {
            // Выбираем материал в зависимости от ориентации плоскости
            Material material = isVertical ? verticalPlaneMaterial : horizontalPlaneMaterial;
            
            // Создаем уникальный экземпляр материала для каждой плоскости
            renderer.material = new Material(material);
            
            // Если у нас есть маска сегментации и это вертикальная плоскость, применяем ее
            if (isVertical && currentSegmentationMask != null)
            {
                renderer.material.SetTexture("_SegmentationMask", currentSegmentationMask);
                renderer.material.EnableKeyword("USE_MASK");
            }
            
            Debug.Log($"[ARManagerInitializer2] Настроена новая плоскость: {plane.trackableId}, тип: {(isVertical ? "вертикальная" : "горизонтальная")}");
        }
    }
    
    // Обновление существующей плоскости
    private void UpdatePlane(ARPlane plane)
    {
        if (plane == null) return;
        
        // Обновляем материал и данные для существующей плоскости, если это вертикальная плоскость
        if (plane.alignment == PlaneAlignment.Vertical)
        {
            MeshRenderer renderer = plane.GetComponent<MeshRenderer>();
            if (renderer != null && renderer.material != null && currentSegmentationMask != null)
            {
                renderer.material.SetTexture("_SegmentationMask", currentSegmentationMask);
                renderer.material.EnableKeyword("USE_MASK");
            }
        }
    }
    
    // Обработка обновления маски сегментации
    private void OnSegmentationMaskUpdated(RenderTexture mask)
    {
        if (mask == null)
        {
            // Debug.LogWarning("[ARManagerInitializer2] Получена null маска сегментации.");
            return;
        }

        // Debug.Log($"[ARManagerInitializer2] Маска сегментации обновлена: {mask.width}x{mask.height}");
        currentSegmentationMask = mask;
        maskUpdated = true;
        hadValidSegmentationResult = true;
        lastSuccessfulSegmentationTime = Time.time;

        if (отображениеМаскиUI != null)
        {
            отображениеМаскиUI.texture = currentSegmentationMask;
            отображениеМаскиUI.gameObject.SetActive(true); // Убедимся, что RawImage активен
            // Debug.Log("[ARManagerInitializer2] Текстура RawImage обновлена маской сегментации.");
        }
        else
        {
            // Debug.LogWarning("[ARManagerInitializer2] отображениеМаскиUI не установлено, некуда выводить маску.");
        }
    }
    
    // Обработка маски сегментации для генерации плоскостей
    private void ProcessSegmentationMask()
    {
        if (currentSegmentationMask == null)
        {
            // Debug.LogWarning("[ARManagerInitializer2] Попытка обработки null маски сегментации.");
            return;
        }

        // Debug.Log($"[ARManagerInitializer2] Обработка маски сегментации {currentSegmentationMask.width}x{currentSegmentationMask.height}");

        // Конвертируем RenderTexture в Texture2D для анализа пикселей
        // Это может быть ресурсоемко, особенно если делать каждый кадр.
        // Рассмотреть оптимизацию, если производительность станет проблемой.
        Texture2D maskTexture = RenderTextureToTexture2D(currentSegmentationMask);

        if (maskTexture != null)
        {
            CreatePlanesFromMask(maskTexture);
            Destroy(maskTexture); // Освобождаем память Texture2D
        }
        else
        {
            // Debug.LogError("[ARManagerInitializer2] Не удалось конвертировать RenderTexture в Texture2D.");
        }
    }

    // Преобразование RenderTexture в Texture2D для обработки
    private Texture2D RenderTextureToTexture2D(RenderTexture renderTexture)
    {
        // Создаем временную текстуру
        Texture2D texture = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGBA32, false);
        
        // Сохраняем текущий RenderTexture
        RenderTexture currentRT = RenderTexture.active;
        
        try
        {
            // Устанавливаем renderTexture как активный
            RenderTexture.active = renderTexture;
            
            // Считываем пиксели из RenderTexture в Texture2D
            texture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
            texture.Apply();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[ARManagerInitializer2] ❌ Ошибка при конвертации RenderTexture в Texture2D: {e.Message}");
            Destroy(texture);
            return null;
        }
        finally
        {
            // Восстанавливаем предыдущий активный RenderTexture
            RenderTexture.active = currentRT;
        }
        
        return texture;
    }

    // Создание плоскостей на основе маски сегментации
    private void CreatePlanesFromMask(Texture2D maskTexture)
    {
        // Debug.Log($"[ARManagerInitializer2-CreatePlanesFromMask] Начало создания плоскостей из маски. Размеры маски: {maskTexture.width}x{maskTexture.height}");
        Color32[] textureData = maskTexture.GetPixels32();
        // Debug.Log($"[ARManagerInitializer2-CreatePlanesFromMask] Получено {textureData.Length} пикселей из маски.");

        // if (textureData.Length == 0)
        // {
        //     Debug.LogWarning("[ARManagerInitializer2-CreatePlanesFromMask] Данные текстуры пусты. Невозможно создать плоскости.");
        //     return;
        // }

        // int activePixelCount = 0;
        // for (int i = 0; i < textureData.Length; i++)
        // {
        //     if (textureData[i].r > 10) // Используем низкий порог для подсчета "активных" пикселей
        //     {
        //         activePixelCount++;
        //     }
        // }
        // Debug.Log($"[ARManagerInitializer2-CreatePlanesFromMask] Количество потенциально активных пикселей (r > 10) в маске: {activePixelCount} из {textureData.Length}");


        List<Rect> wallAreas = FindWallAreas(textureData, maskTexture.width, maskTexture.height, wallAreaDetectionThreshold);
        // Debug.Log($"[ARManagerInitializer2-CreatePlanesFromMask] Найдено областей (wallAreas.Count): {wallAreas.Count}");

        // Словарь для отслеживания "посещенных" (обновленных или подтвержденных) плоскостей в этом кадре
        Dictionary<GameObject, bool> visitedPlanes = new Dictionary<GameObject, bool>();

        // Очищаем старые плоскости перед созданием новых (или обновлением существующих)
        // Debug.Log($"[ARManagerInitializer2-CreatePlanesFromMask] Очистка старых плоскостей ({generatedPlanes.Count}) перед созданием новых.");
        // CleanupOldPlanes(visitedPlanes); // Теперь очистка происходит в UpdateOrCreatePlaneForWallArea

        // Сортируем области по размеру (площади), чтобы сначала обрабатывать большие
        // Это необязательно, если мы обновляем/создаем для всех, но может быть полезно для отладки или лимитов
        var significantAreas = wallAreas
            .OrderByDescending(area => area.width * area.height)
            .ToList();

        // Debug.Log($"[ARManagerInitializer2-CreatePlanesFromMask] После сортировки осталось {significantAreas.Count} областей для потенциального создания.");

        int planesCreatedThisFrame = 0;
        // float normalizedMinPlaneSize = minPlaneSizeInMeters * minPlaneSizeInMeters; // Площадь в метрах
        float normalizedMinPlaneSize = (minPlaneSizeInMeters / (Camera.main.orthographic ? Camera.main.orthographicSize * 2f : Mathf.Tan(Camera.main.fieldOfView * Mathf.Deg2Rad / 2f) * 2f * 2f)); // Примерный перевод метров в "нормализованный размер" относительно FOV
        normalizedMinPlaneSize *= normalizedMinPlaneSize; // Сравниваем площади

        foreach (Rect area in significantAreas)
        {
            float normalizedAreaSize = (area.width / (float)maskTexture.width) * (area.height / (float)maskTexture.height);
            // Debug.Log($"[ARManagerInitializer2-CreatePlanesFromMask] Обработка области: x={area.xMin}, y={area.yMin}, w={area.width}, h={area.height}, normSize={normalizedAreaSize:F4}");

            // if (normalizedAreaSize >= normalizedMinPlaneSize) // Фильтр по минимальному размеру
            if (area.width * area.height >= minAreaSizeInPixels) // Используем пиксельный размер для фильтрации областей
            {
                 // Вместо CreatePlaneForWallArea вызываем UpdateOrCreatePlaneForWallArea
                if (UpdateOrCreatePlaneForWallArea(area, maskTexture.width, maskTexture.height, visitedPlanes))
                {
                    planesCreatedThisFrame++;
                }
                // Debug.Log($"[ARManagerInitializer2-CreatePlanesFromMask] ✅ Попытка создания/обновления плоскости для области normSize={normalizedAreaSize:F4}. Создано/обновлено в этом кадре: {planesCreatedThisFrame}. Общее количество плоскостей после вызова: {generatedPlanes.Count}");
            }
            else
            {
                // Debug.Log($"[ARManagerInitializer2-CreatePlanesFromMask] Область (normSize={normalizedAreaSize:F4}) слишком мала (требуется >= {minAreaSizeInPixels} пикс. площадь). Плоскость не создана.");
            }
        }
        
        // Окончательная очистка плоскостей, которые не были "посещены" (т.е. для них не нашлось подходящей области в новой маске)
        CleanupOldPlanes(visitedPlanes);

        // Debug.Log($"[ARManagerInitializer2-CreatePlanesFromMask] ✅ Завершено. Обновлено/Создано {planesCreatedThisFrame} плоскостей из {significantAreas.Count} рассмотренных областей. Всего активных плоскостей: {generatedPlanes.Count}");

        lastPlaneCount = generatedPlanes.Count;
    }

    // Метод для поиска связной области начиная с заданного пикселя
    private Rect FindConnectedArea(Color32[] pixels, int width, int height, int startX, int startY, bool[,] visited, byte threshold)
    {
        // Debug.Log($"[ARManagerInitializer2-FindConnectedArea] Запуск для ({startX},{startY}) с порогом {threshold}");
        if (startX < 0 || startX >= width || startY < 0 || startY >= height || visited[startX, startY] || pixels[startY * width + startX].r < threshold)
        {
            // Debug.LogWarning($"[ARManagerInitializer2-FindConnectedArea] Неверные стартовые параметры или пиксель ниже порога/уже посещен. startX={startX}, startY={startY}, visited={visited[startX, startY]}, pixel.r={pixels[startY * width + startX].r}, threshold={threshold}");
            return Rect.zero;
        }

        // Границы области
        int minX = startX;
        int maxX = startX;
        int minY = startY;
        int maxY = startY;

        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        queue.Enqueue(new Vector2Int(startX, startY));
        visited[startX, startY] = true;
        // ОТЛАДКА: Выводим начальную точку
        // Debug.Log($"[FindConnectedArea] Start: ({startX},{startY}), Pixel R: {pixels[startY * width + startX].r}");
        
        // Возможные направления для обхода (4 соседа)
        Vector2Int[] directions = new Vector2Int[]
        {
            new Vector2Int(1, 0),  // вправо
            new Vector2Int(-1, 0), // влево
            new Vector2Int(0, 1),  // вниз
            new Vector2Int(0, -1)  // вверх
        };
        
        // Алгоритм обхода в ширину для поиска связной области
        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            // ОТЛАДКА: Выводим текущую обрабатываемую точку и значение ее красного канала
            // Debug.Log($"[FindConnectedArea] Processing: ({current.x},{current.y}), Pixel R: {pixels[current.y * width + current.x].r}, Current Bounds: minX={minX}, maxX={maxX}, minY={minY}, maxY={maxY}");
            
            // Обновляем границы области
            minX = Mathf.Min(minX, current.x);
            maxX = Mathf.Max(maxX, current.x);
            minY = Mathf.Min(minY, current.y);
            maxY = Mathf.Max(maxY, current.y);
            
            // Проверяем соседей
            foreach (Vector2Int dir in directions)
            {
                int newX = current.x + dir.x;
                int newY = current.y + dir.y;
                
                // Проверяем, что новые координаты в пределах текстуры
                if (newX >= 0 && newX < width && newY >= 0 && newY < height)
                {
                    // Если пиксель не посещен и это часть стены
                    if (!visited[newX, newY] && pixels[newY * width + newX].r > threshold)
                    {
                        visited[newX, newY] = true;
                        queue.Enqueue(new Vector2Int(newX, newY));
                    }
                }
            }
        }
        
        // ОТЛАДКА: Выводим итоговые границы найденной области
        // Debug.Log($"[FindConnectedArea] Finished Area. Initial: ({startX},{startY}). Final Bounds: minX={minX}, maxX={maxX}, minY={minY}, maxY={maxY}. Resulting Rect: x={minX}, y={minY}, w={maxX - minX + 1}, h={maxY - minY + 1}");
        return new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private List<Rect> FindWallAreas(Color32[] pixels, int width, int height, byte threshold)
    {
        // Debug.Log($"[ARManagerInitializer2-FindWallAreas] Поиск областей в текстуре {width}x{height} с порогом {threshold}");
        List<Rect> wallAreas = new List<Rect>();
        bool[,] visited = new bool[width, height];
        int areasFoundBeforeFiltering = 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                // Debug.Log($"[ARManagerInitializer2-FindWallAreas] Проверка пикселя ({x},{y}). visited={visited[x,y]}, pixel.r = {pixels[y * width + x].r}");
                if (!visited[x, y] && pixels[y * width + x].r >= threshold)
                {
                    // Debug.Log($"[ARManagerInitializer2-FindWallAreas] Найден активный непосещенный пиксель ({x},{y}). Запуск FindConnectedArea.");
                    Rect area = FindConnectedArea(pixels, width, height, x, y, visited, threshold);
                    areasFoundBeforeFiltering++;
                    if (area.width >= minPixelsDimensionForArea && area.height >= minPixelsDimensionForArea && area.width * area.height >= minAreaSizeInPixels)
                    {
                        // Debug.Log($"[ARManagerInitializer2-FindWallAreas] Добавлена область: {area} (Площадь: {area.width * area.height})");
                        wallAreas.Add(area);
                    }
                    // else
                    // {
                        // if (area.width > 0 && area.height > 0) // Если это не Rect.zero
                        // {
                        //      Debug.Log($"[ARManagerInitializer2-FindWallAreas] Область {area} (пикс.размеры: {area.width}x{area.height}, площадь: {area.width*area.height}) отфильтрована по размеру. minPixelsDimensionForArea={minPixelsDimensionForArea}, minAreaSizeInPixels={minAreaSizeInPixels}");
                        // } else {
                        //      Debug.Log($"[ARManagerInitializer2-FindWallAreas] FindConnectedArea вернул пустую область для ({x},{y}).");
                        // }
                    // }
                }
            }
        }
        // Debug.Log($"[ARManagerInitializer2-FindWallAreas] Завершено. Найдено валидных областей: {wallAreas.Count} (из {areasFoundBeforeFiltering} до фильтрации).");
        return wallAreas;
    }
    
    // Создание плоскости для области стены
    private void CreatePlaneForWallArea(Rect area, int textureWidth, int textureHeight)
    {
        if (xrOrigin == null || xrOrigin.Camera == null)
        {
            Debug.LogError("[ARManagerInitializer2-CreatePlaneForWallArea] XROrigin or Camera is null. Cannot create plane.");
            return;
        }

        Camera mainCamera = xrOrigin.Camera;
        float planeWorldWidth, planeWorldHeight;
        float distanceFromCamera;

        // Расчет ширины и высоты видимой области на определенном расстоянии от камеры
        // Для перспективной камеры:
        float halfFovVertical = mainCamera.fieldOfView * 0.5f * Mathf.Deg2Rad;
        float halfFovHorizontal = Mathf.Atan(Mathf.Tan(halfFovVertical) * mainCamera.aspect);

        // Используем центральную точку области для рейкаста
        Vector2 areaCenterUV = new Vector2(
            (area.xMin + area.width * 0.5f) / textureWidth,
            (area.yMin + area.height * 0.5f) / textureHeight
        );

        Ray ray = mainCamera.ViewportPointToRay(new Vector3(areaCenterUV.x, areaCenterUV.y, 0));
        RaycastHit hit;
        Vector3 planePosition;
        Quaternion planeRotation;
        float fallbackDistance = 2.0f; // Расстояние по умолчанию, если рейкаст не удался

        // Устанавливаем маску слоев для рейкаста (например, только "Default" или специальный слой для геометрии сцены)
        // int layerMask = LayerMask.GetMask("Default", "SimulatedEnvironment"); // Пример
        // int layerMask = ~LayerMask.GetMask("ARCustomPlane", "Ignore Raycast"); // Игнорируем собственные плоскости и слой Ignore Raycast
        LayerMask دیوارИлиПолДляРейкаста = LayerMask.GetMask("SimulatedEnvironment", "Default", "Wall"); // Добавляем слой "Wall"


        if (Physics.Raycast(ray, out hit, maxDistance: 20.0f, layerMask: دیوارИлиПолДляРейкаста)) // Ограничиваем дистанцию рейкаста
        {
            distanceFromCamera = hit.distance;
            Debug.Log($"[ARManagerInitializer2-CreatePlaneForWallArea] Raycast hit! Distance: {distanceFromCamera:F2}m, Normal: {hit.normal:F2}, Point: {hit.point:F2}, Hit Object: {hit.collider.name}");
            planeRotation = Quaternion.LookRotation(-hit.normal, mainCamera.transform.up); // Ориентируем Z плоскости по нормали
            planePosition = hit.point + hit.normal * 0.01f; // Смещаем немного от поверхности, чтобы избежать Z-fighting
        }
        else
        {
            // Если луч не попал, используем фиксированное расстояние и ориентацию параллельно камере
            distanceFromCamera = fallbackDistance; // Используем заданное расстояние по умолчанию
            planePosition = mainCamera.transform.position + mainCamera.transform.forward * distanceFromCamera;
            planeRotation = mainCamera.transform.rotation; // Ориентация как у камеры
            Debug.LogWarning($"[ARManagerInitializer2-CreatePlaneForWallArea] Raycast miss. Using fixed distance ({distanceFromCamera}m) and camera-parallel orientation.");
        }


        // Расчет мировых размеров плоскости
        // Ширина видимой области на расстоянии distanceFromCamera
        float worldHeightAtDistance = 2.0f * distanceFromCamera * Mathf.Tan(halfFovVertical);
        float worldWidthAtDistance = worldHeightAtDistance * mainCamera.aspect;

        // Мировые размеры плоскости, основанные на ее доле в маске
        planeWorldWidth = (area.width / textureWidth) * worldWidthAtDistance;
        planeWorldHeight = (area.height / textureHeight) * worldHeightAtDistance;
        
        // Проверка на минимальный размер перед созданием
        if (planeWorldWidth < this.minPlaneSizeInMeters || planeWorldHeight < this.minPlaneSizeInMeters)
        {
            Debug.Log($"[ARManagerInitializer2-CreatePlaneForWallArea] Плоскость для области ({area.width}x{area.height}px) слишком мала ({planeWorldWidth:F2}x{planeWorldHeight:F2}m) для создания. Min size: {this.minPlaneSizeInMeters}m.");
            return;
        }

        Debug.Log($"[ARManagerInitializer2-CreatePlaneForWallArea] Plane World Size: Width={planeWorldWidth:F2}, Height={planeWorldHeight:F2} at distance {distanceFromCamera:F2}m");

        // Проверка на дубликаты (упрощенная)
        foreach (GameObject existingPlane in this.generatedPlanes)
        {
            if (existingPlane == null) continue;
            if (Vector3.Distance(existingPlane.transform.position, planePosition) < 0.2f) // Если очень близко
            {
                Debug.LogWarning($"[ARManagerInitializer2-CreatePlaneForWallArea] ⚠️ Обнаружена потенциально дублирующаяся плоскость (слишком близко), пропускаем создание. Pos: {planePosition}, ExistingPos: {existingPlane.transform.position}");
                return; // Пропускаем создание этой плоскости
            }
        }

        string planeName = $"MyARPlane_Debug_{planeInstanceCounter++}";
        GameObject planeObject = new GameObject(planeName);
        planeObject.transform.SetParent(null); 
        planeObject.transform.position = planePosition;
        // Ориентируем плоскость так, чтобы ее нормаль была planeNormal (полученная из рейкаста или направленная от камеры)
        planeObject.transform.rotation = planeRotation; 
        // Меш создается в XY, поэтому его нужно повернуть, если LookRotation использовал Z как "вперед"
        // Стандартный Quad Unity ориентирован вдоль локальной оси Z. LookRotation выравнивает Z объекта с направлением.
        // Если planeNormal - это нормаль поверхности, то LookRotation(planeNormal) выровняет +Z объекта с этой нормалью.
        // Это обычно то, что нужно для плоскости, представляющей поверхность.

        planeObject.transform.localScale = Vector3.one; // Масштаб будет применен к мешу напрямую
        
        Debug.Log($"[ARManagerInitializer2-CreatePlaneForWallArea] Created {planeName}. World Position: {planeObject.transform.position}, Rotation: {planeObject.transform.rotation.eulerAngles}, Initial Scale: {planeObject.transform.localScale}");

        MeshFilter meshFilter = planeObject.AddComponent<MeshFilter>();
        meshFilter.mesh = CreatePlaneMesh(planeWorldWidth, planeWorldHeight); // Используем мировые размеры для меша
        
        MeshRenderer meshRenderer = planeObject.AddComponent<MeshRenderer>();
        if (this.verticalPlaneMaterial != null) 
        {
            meshRenderer.material = new Material(this.verticalPlaneMaterial); 
            // Можно сделать полупрозрачным для отладки
            // Color color = meshRenderer.material.color;
            // color.a = 0.7f; 
            // meshRenderer.material.color = color;
        }
        else
        {
             Debug.LogError("[ARManagerInitializer2-CreatePlaneForWallArea] wallMaterialVertical is not set! Assigning default magenta.");
             Material simpleMaterial = new Material(Shader.Find("Unlit/Color"));
             simpleMaterial.color = Color.magenta;
             meshRenderer.material = simpleMaterial;
        }
        Debug.Log($"[ARManagerInitializer2-CreatePlaneForWallArea] Applied material to {planeName}. Mesh bounds: {meshFilter.mesh.bounds.size}");
        
        MeshCollider meshCollider = planeObject.AddComponent<MeshCollider>();
        meshCollider.sharedMesh = meshFilter.mesh;
        
        this.generatedPlanes.Add(planeObject);
        if (this.planeCreationTimes != null) this.planeCreationTimes[planeObject] = Time.time;

        // Попытка привязать к TrackablesParent, если он есть и не был равен null при старте
        if (this.xrOrigin != null && this.xrOrigin.TrackablesParent != null)
        {
            // Проверяем, не является ли TrackablesParent частью самого XR Origin, который может быть отключен при симуляции
            // и имеет ли он тот же InstanceID, что и при старте (на случай если он был пересоздан)
            if (this.trackablesParentInstanceID_FromStart == 0 || 
                (this.xrOrigin.TrackablesParent.gameObject.activeInHierarchy && this.xrOrigin.TrackablesParent.GetInstanceID() == this.trackablesParentInstanceID_FromStart))
            {
                planeObject.transform.SetParent(this.xrOrigin.TrackablesParent, true); 
                Debug.Log($"[ARManagerInitializer2-CreatePlaneForWallArea] {planeName} привязан к {this.xrOrigin.TrackablesParent.name} (ID: {this.xrOrigin.TrackablesParent.GetInstanceID()}).");
            }
            else
            {
                Debug.LogWarning($"[ARManagerInitializer2-CreatePlaneForWallArea] {planeName} не привязан к TrackablesParent, так как он неактивен или был изменен (ожидался ID: {this.trackablesParentInstanceID_FromStart}, текущий: {this.xrOrigin.TrackablesParent.GetInstanceID()}, активен: {this.xrOrigin.TrackablesParent.gameObject.activeInHierarchy}). Оставлен в корне.");
            }
        }
        else
        {
            Debug.LogWarning($"[ARManagerInitializer2-CreatePlaneForWallArea] {planeName} не привязан, так как XROrigin или TrackablesParent не найдены. Оставлен в корне.");
        }
    }

    private Mesh CreatePlaneMesh(float width, float height)
    {
        Mesh mesh = new Mesh();
        
        // Создаем вершины для более детализированного меша
        // Используем сетку 4x4 для более гибкой геометрии
        int segmentsX = 4;
        int segmentsY = 4; 
        float thickness = 0.02f; // Уменьшенная толщина
        
        int vertCount = (segmentsX + 1) * (segmentsY + 1) * 2; // передняя и задняя грани
        Vector3[] vertices = new Vector3[vertCount];
        Vector2[] uv = new Vector2[vertCount];
        
        // Создаем передние и задние вершины
        int index = 0;
        for (int z = 0; z < 2; z++)
        {
            float zPos = z == 0 ? 0 : -thickness;
            
            for (int y = 0; y <= segmentsY; y++)
            {
                float yPos = -height/2 + height * ((float)y / segmentsY);
                
                for (int x = 0; x <= segmentsX; x++)
                {
                    float xPos = -width/2 + width * ((float)x / segmentsX);
                    
                    vertices[index] = new Vector3(xPos, yPos, zPos);
                    uv[index] = new Vector2((float)x / segmentsX, (float)y / segmentsY);
                    index++;
                }
            }
        }
        
        // Создаем треугольники
        int quadCount = segmentsX * segmentsY * 2 + // передняя и задняя грани
                        segmentsX * 2 + // верхняя и нижняя грани
                        segmentsY * 2;  // левая и правая грани
                        
        int[] triangles = new int[quadCount * 6]; // 6 индексов на квадрат (2 треугольника)
        
        index = 0;
        
        // Передняя грань
        int frontOffset = 0;
        int verticesPerRow = segmentsX + 1;
        
        for (int y = 0; y < segmentsY; y++)
        {
            for (int x = 0; x < segmentsX; x++)
            {
                int currentIndex = frontOffset + y * verticesPerRow + x;
                
                triangles[index++] = currentIndex;
                triangles[index++] = currentIndex + verticesPerRow + 1;
                triangles[index++] = currentIndex + 1;
                
                triangles[index++] = currentIndex;
                triangles[index++] = currentIndex + verticesPerRow;
                triangles[index++] = currentIndex + verticesPerRow + 1;
            }
        }
        
        // Задняя грань (инвертированные треугольники)
        int backOffset = (segmentsX + 1) * (segmentsY + 1);
        
        for (int y = 0; y < segmentsY; y++)
        {
            for (int x = 0; x < segmentsX; x++)
            {
                int currentIndex = backOffset + y * verticesPerRow + x;
                
                triangles[index++] = currentIndex + 1;
                triangles[index++] = currentIndex + verticesPerRow + 1;
                triangles[index++] = currentIndex;
                
                triangles[index++] = currentIndex + verticesPerRow + 1;
                triangles[index++] = currentIndex + verticesPerRow;
                triangles[index++] = currentIndex;
            }
        }
        
        // Верхняя, нижняя, левая и правая грани
        // Для простоты опускаю эту часть кода, она по сути аналогична
        
        // Назначаем данные сетке
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uv;
        
        // Вычисляем нормали и границы
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        
        return mesh;
    }

    // Новый метод для удаления плоскостей, накладывающихся поверх камеры
    private void RemoveOverlayingPlanes()
    {
        if (xrOrigin == null || xrOrigin.Camera == null || generatedPlanes.Count == 0)
            return;
        
        Camera arCamera = xrOrigin.Camera;
        List<GameObject> planesToRemove = new List<GameObject>();
        
        // Определяем вектор "вперед" для камеры в мировом пространстве
        Vector3 cameraForward = arCamera.transform.forward;
        
        foreach (GameObject plane in generatedPlanes)
        {
            if (plane == null) continue;
            
            Vector3 directionToPlane = plane.transform.position - arCamera.transform.position;
            float distanceToCamera = directionToPlane.magnitude;
            
            float protectionTime = 1.0f; 
            bool isRecentlyCreated = planeCreationTimes.ContainsKey(plane) && 
                                      Time.time - planeCreationTimes[plane] < protectionTime;

            // НОВАЯ ПРОВЕРКА: Экстремально близкие плоскости
            if (distanceToCamera < 0.2f) // Порог для "экстремально близко", например, 20 см
            {
                if (!isRecentlyCreated)
                {
                    planesToRemove.Add(plane);
                    Debug.LogWarning($"[ARManagerInitializer2] 🚨 Удаление экстремально близкой плоскости: dist={distanceToCamera:F2}м, name={plane.name}");
                }
                else
                {
                    Debug.Log($"[ARManagerInitializer2] Экстремально близкая плоскость '{plane.name}' защищена (недавно создана): dist={distanceToCamera:F2}м. Пропускаем дальнейшие проверки наложения.");
                }
                continue; // Пропускаем остальные проверки для этой плоскости, если она экстремально близка
            }
            
            // Проверяем несколько условий для определения плоскости поверх камеры:
            
            // 1. Расстояние до камеры (остается актуальным для плоскостей > 0.2м)
            // float distanceToCamera = directionToPlane.magnitude; // Уже вычислено
            
            // 2. Угол между направлением камеры и направлением к плоскости
            // (насколько плоскость находится прямо перед камерой)
            // Нормализация безопасна, так как distanceToCamera >= 0.2f
            float alignmentWithCamera = Vector3.Dot(cameraForward.normalized, directionToPlane.normalized);
            
            // 3. Угол между нормалью плоскости и направлением камеры
            // (насколько плоскость обращена к камере)
            float facingDot = Vector3.Dot(cameraForward, -plane.transform.forward);
            
            // 4. Находится ли плоскость в центральной части поля зрения
            Vector3 viewportPos = arCamera.WorldToViewportPoint(plane.transform.position);
            bool isInCentralViewport = (viewportPos.x > 0.3f && viewportPos.x < 0.7f && 
                                       viewportPos.y > 0.3f && viewportPos.y < 0.7f && 
                                       viewportPos.z > 0);
            
            // Условие для определения плоскости-наложения:
            // - Плоскость находится близко к камере (менее 2.0 метра)
            // - И плоскость находится примерно перед камерой (положительный dot product)
            // - И плоскость почти перпендикулярна направлению взгляда
            // - И плоскость находится в центральной части экрана
            // Защита от удаления недавно созданных плоскостей уже проверена выше для экстремально близких.
            // Здесь она применяется для "обычных" наложений.
                                  
            if (!isRecentlyCreated && distanceToCamera < 2.0f && alignmentWithCamera > 0.7f && facingDot > 0.6f && isInCentralViewport)
            {
                planesToRemove.Add(plane);
                Debug.Log($"[ARManagerInitializer2] Обнаружена плоскость-наложение '{plane.name}': dist={distanceToCamera:F2}м, " + 
                         $"align={alignmentWithCamera:F2}, facing={facingDot:F2}, inCenter={isInCentralViewport}");
            }
            else if (isRecentlyCreated && distanceToCamera < 2.0f && alignmentWithCamera > 0.7f && facingDot > 0.6f && isInCentralViewport)
            {
                 Debug.Log($"[ARManagerInitializer2] Плоскость-наложение '{plane.name}' защищена (недавно создана): dist={distanceToCamera:F2}м");
            }
        }
        
        // Удаляем плоскости-наложения
        foreach (GameObject planeToRemove in planesToRemove)
        {
            generatedPlanes.Remove(planeToRemove);
            if (planeCreationTimes.ContainsKey(planeToRemove))
            {
                planeCreationTimes.Remove(planeToRemove);
            }
            Destroy(planeToRemove);
        }
        
        if (planesToRemove.Count > 0)
        {
            Debug.Log($"[ARManagerInitializer2] ⚠️ Удалено {planesToRemove.Count} плоскостей-наложений");
        }
    }

    // НОВЫЙ МЕТОД: Обновление позиций плоскостей для обеспечения стабильности
    private void UpdatePlanePositions()
    {
        if (xrOrigin == null || xrOrigin.Camera == null || xrOrigin.TrackablesParent == null)
        {
            // Debug.LogError("[ARManagerInitializer2-UpdatePlanePositions] XR Origin, Camera, or TrackablesParent is not set. Cannot update plane positions.");
            return;
        }

        // Debug.Log($"[ARManagerInitializer2-UpdatePlanePositions] Running. Planes to check: {generatedPlanes.Count}");
        int detachedPlanes = 0; // Объявляем переменную здесь

        for (int i = generatedPlanes.Count - 1; i >= 0; i--) // Идем в обратном порядке для безопасного удаления
        {
            GameObject plane = generatedPlanes[i];
            if (plane == null)
            {
                generatedPlanes.RemoveAt(i); // Удаляем null ссылки из списка
                continue;
            }
            
            // Проверяем, не прикреплена ли плоскость к камере или ДРУГОМУ НЕОЖИДАННОМУ объекту
            if (plane.transform.parent != null && (this.xrOrigin == null || this.xrOrigin.TrackablesParent == null || plane.transform.parent != this.xrOrigin.TrackablesParent))
            {
                // Debug.LogWarning($"[ARManagerInitializer2-UpdatePlanePositions] Плоскость '{plane.name}' (ID: {plane.GetInstanceID()}) была присоединена к НЕОЖИДАННОМУ родителю '{GetGameObjectPath(plane.transform.parent)}' (ожидался TrackablesParent или null). Отсоединяем.");
                
                // Отсоединяем плоскость. Аргумент 'true' сохраняет мировые координаты,
                // localScale будет скорректирован для сохранения текущего lossyScale.
                plane.transform.SetParent(null, true); 
                
                detachedPlanes++;
            }
            else if (plane.transform.parent == null && this.xrOrigin != null && this.xrOrigin.TrackablesParent != null)
            {
                // Если плоскость почему-то отсоединилась от TrackablesParent, но TrackablesParent существует,
                // присоединяем ее обратно. Это может произойти, если что-то другое в коде изменяет родителя.
                // Debug.LogWarning($"[ARManagerInitializer2-UpdatePlanePositions] Плоскость '{plane.name}' (ID: {plane.GetInstanceID()}) была отсоединена от TrackablesParent. Присоединяем обратно к '{GetGameObjectPath(this.xrOrigin.TrackablesParent)}'.");
                plane.transform.SetParent(this.xrOrigin.TrackablesParent, true);
            }
            else if (plane.transform.parent != null && this.xrOrigin != null && this.xrOrigin.TrackablesParent != null && plane.transform.parent == this.xrOrigin.TrackablesParent)
            {
                 // Плоскость уже корректно привязана к TrackablesParent. Ничего делать не нужно.
                 // Debug.Log($"[ARManagerInitializer2-UpdatePlanePositions] Плоскость '{plane.name}' (ID: {plane.GetInstanceID()}) уже корректно привязана к TrackablesParent.");
            }


            // Логика для обновления позиции, если плоскость НЕ привязана к TrackablesParent
            // Эта часть теперь менее актуальна, так как мы привязываем к TrackablesParent при создании
            // и проверяем/восстанавливаем привязку выше.
            // if (plane.transform.parent == null)
            // {
            //     Vector3 targetPosition = xrOrigin.Camera.transform.position + xrOrigin.Camera.transform.forward * 2.0f; // Пример: 2м перед камерой
            //     plane.transform.position = targetPosition;
            //     plane.transform.rotation = Quaternion.LookRotation(xrOrigin.Camera.transform.forward); // Ориентируем как камеру
            //     Debug.Log($"[ARManagerInitializer2-UpdatePlanePositions] Плоскость '{plane.name}' обновлена (не была привязана): pos={targetPosition}, rot={plane.transform.rotation.eulerAngles}");
            // }
        }
        
        if (detachedPlanes > 0)
        {
            Debug.LogWarning($"[ARManagerInitializer2-UpdatePlanePositions] Отсоединено {detachedPlanes} плоскостей, которые были некорректно присоединены к родительским объектам.");
        }
    }

    // Перезагружаем все плоскости, если что-то пошло не так
    public void ResetAllPlanes()
    {
        foreach (GameObject plane in generatedPlanes)
        {
            if (plane != null)
                Destroy(plane);
        }
        
        generatedPlanes.Clear();
        Debug.Log("[ARManagerInitializer2] 🔄 Все плоскости удалены и будут пересозданы");
        
        // Сбрасываем счетчик кадров для немедленного создания новых плоскостей
        frameCounter = 10;
    }

    // НОВЫЙ МЕТОД: Отключение стандартных визуализаторов AR Foundation
    private void DisableARFoundationVisualizers()
    {
        // Проверяем наличие всех компонентов AR Foundation, которые могут создавать визуальные элементы
        
        // 1. Отключаем визуализаторы плоскостей
        if (planeManager != null)
        {
            // Отключаем отображение префаба плоскостей
            planeManager.planePrefab = null;
            
            // Проходимся по всем трекабл-объектам и отключаем их визуализацию
            foreach (var plane in planeManager.trackables)
            {
                if (plane != null)
                {
                    MeshRenderer meshRenderer = plane.GetComponent<MeshRenderer>();
                    if (meshRenderer != null)
                    {
                        meshRenderer.enabled = false;
                    }
                    
                    LineRenderer lineRenderer = plane.GetComponent<LineRenderer>();
                    if (lineRenderer != null)
                    {
                        lineRenderer.enabled = false;
                    }
                }
            }
            
            Debug.Log("[ARManagerInitializer2] ✅ Отключены стандартные визуализаторы плоскостей AR Foundation");
        }
        
        // 2. Отключаем визуализаторы точек
        var pointCloudManager = FindObjectOfType<UnityEngine.XR.ARFoundation.ARPointCloudManager>();
        if (pointCloudManager != null)
        {
            pointCloudManager.enabled = false;
            Debug.Log("[ARManagerInitializer2] ✅ Отключен ARPointCloudManager");
        }
        
        // 3. Поиск и отключение всех визуальных объектов TrackablesParent
        if (xrOrigin != null)
        {
            var trackablesParent = xrOrigin.transform.Find("Trackables");
            if (trackablesParent != null)
            {
                // Проходимся по всем дочерним объектам и отключаем их рендереры
                foreach (Transform child in trackablesParent)
                {
                    // Отключаем все рендереры у дочерних объектов
                    foreach (Renderer renderer in child.GetComponentsInChildren<Renderer>())
                    {
                        renderer.enabled = false;
                    }
                }
                Debug.Log("[ARManagerInitializer2] ✅ Отключены рендереры в Trackables");
            }
        }
    }

    // НОВЫЙ МЕТОД: Отключение других AR визуализаторов, которые могут появляться в рантайме
    private void DisableOtherARVisualizers()
    {
        // Отключаем все объекты с оранжевым/желтым цветом и именами, содержащими "Trackable", "Feature", "Point"
        var allRenderers = FindObjectsOfType<Renderer>();
        int disabledCount = 0;
        
        foreach (var renderer in allRenderers)
        {
            // Пропускаем наши собственные плоскости
            bool isOurPlane = false;
            foreach (var plane in generatedPlanes)
            {
                if (plane != null && renderer.gameObject == plane)
                {
                    isOurPlane = true;
                    break;
                }
            }
            
            if (isOurPlane)
                continue;
            
            // Проверяем имя объекта на ключевые слова, связанные с AR Foundation
            string objName = renderer.gameObject.name.ToLower();
            if (objName.Contains("track") || objName.Contains("feature") || 
                objName.Contains("point") || objName.Contains("plane") ||
                objName.Contains("mesh") || objName.Contains("visualizer"))
            {
                renderer.enabled = false;
                disabledCount++;
            }
            
            // Проверяем материал на желтый/оранжевый цвет
            if (renderer.sharedMaterial != null)
            {
                // Получаем основной цвет материала
                Color color = renderer.sharedMaterial.color;
                
                // Проверяем, является ли цвет желтым или оранжевым
                // (красный компонент высокий, зеленый средний, синий низкий)
                if (color.r > 0.6f && color.g > 0.4f && color.b < 0.3f)
                {
                    renderer.enabled = false;
                    disabledCount++;
                }
            }
        }
        
        if (disabledCount > 0)
        {
            Debug.Log($"[ARManagerInitializer2] 🔴 Отключено {disabledCount} сторонних AR-визуализаторов");
        }
    }

    // УЛУЧШЕННЫЙ МЕТОД: Создание стабильной базовой плоскости перед пользователем при отсутствии данных сегментации
    private void CreateBasicPlaneInFrontOfUser()
    {
        if (xrOrigin == null || xrOrigin.Camera == null)
        {
            // Debug.LogError("[ARManagerInitializer2] XROrigin or Camera is null, cannot create basic plane.");
            return;
        }

        // Debug.Log("[ARManagerInitializer2] Создание базовой плоскости перед пользователем.");

        // Проверяем, есть ли уже существующая базовая плоскость
        foreach (GameObject existingPlane in generatedPlanes)
        {
            if (existingPlane != null && existingPlane.name.StartsWith("MyARPlane_Debug_Basic_"))
            {
                // Debug.Log("[ARManagerInitializer2] Базовая плоскость уже существует, новая не создается.");
                return; // Если уже есть, ничего не делаем
            }
        }

        Camera mainCamera = xrOrigin.Camera;
        float distanceFromCamera = 2.0f; // Фиксированное расстояние

        // Расчет ширины и высоты видимой области на расстоянии distanceFromCamera
        float halfFovVertical = mainCamera.fieldOfView * 0.5f * Mathf.Deg2Rad;
        float worldHeightAtDistance = 2.0f * distanceFromCamera * Mathf.Tan(halfFovVertical);
        float worldWidthAtDistance = worldHeightAtDistance * mainCamera.aspect;

        // Создаем плоскость, которая занимает примерно 30% от ширины и высоты обзора
        float planeWorldWidth = worldWidthAtDistance * 0.3f;
        float planeWorldHeight = worldHeightAtDistance * 0.3f;
        
        // Убеждаемся, что размер не меньше минимального
        planeWorldWidth = Mathf.Max(planeWorldWidth, minPlaneSizeInMeters); 
        planeWorldHeight = Mathf.Max(planeWorldHeight, minPlaneSizeInMeters);

        Mesh planeMesh = CreatePlaneMesh(planeWorldWidth, planeWorldHeight);

        string planeName = $"MyARPlane_Debug_Basic_{planeInstanceCounter++}";
        GameObject planeObject = new GameObject(planeName);
        planeObject.transform.SetParent(null); 
        planeObject.transform.position = mainCamera.transform.position + mainCamera.transform.forward * distanceFromCamera;
        planeObject.transform.rotation = mainCamera.transform.rotation;
        planeObject.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
        planeObject.transform.SetParent(null);
        
        // Добавляем компоненты
        MeshFilter meshFilter = planeObject.AddComponent<MeshFilter>();
        meshFilter.mesh = planeMesh;
        
        MeshRenderer meshRenderer = planeObject.AddComponent<MeshRenderer>();
        if (this.verticalPlaneMaterial != null)
        {
            meshRenderer.material = new Material(this.verticalPlaneMaterial);
        }
        else
        {
            // Создаем материал для резервного случая
            Material planeMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            Color planeColor = Color.HSVToRGB(0.1f, 0.6f, 0.7f); // Приглушенный золотистый
            planeMaterial.color = planeColor;
            
            // Настройки материала для полупрозрачности
            planeMaterial.SetFloat("_Surface", 1); // 1 = прозрачный
            planeMaterial.SetInt("_ZWrite", 0); // Отключаем запись в буфер глубины для прозрачности
            planeMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            planeMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            planeMaterial.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            planeMaterial.EnableKeyword("_EMISSION");
            planeMaterial.SetColor("_EmissionColor", planeColor * 0.15f); // Уменьшенная эмиссия
            planeMaterial.SetFloat("_Smoothness", 0.2f);
            planeMaterial.SetFloat("_Metallic", 0.05f);
            planeMaterial.renderQueue = 3000; // Очередь прозрачных объектов
            planeColor.a = 0.5f; // Добавляем полупрозрачность
            planeMaterial.color = planeColor;
            
            meshRenderer.material = planeMaterial;
        }
        
        // Добавляем коллайдер для взаимодействия
        MeshCollider meshCollider = planeObject.AddComponent<MeshCollider>();
        meshCollider.sharedMesh = meshFilter.sharedMesh;
        
        // Добавляем в список созданных плоскостей
        generatedPlanes.Add(planeObject);
        
        // Также сохраняем время создания плоскости для защиты от раннего удаления
        planeCreationTimes[planeObject] = Time.time;
        
        Debug.Log("[ARManagerInitializer2] ✅ Создана стабильная базовая плоскость перед пользователем");

        planeObject.name = $"MyARPlane_Debug_Basic_{planeInstanceCounter++}";
        // Debug.Log($"[ARManagerInitializer2] Создана базовая плоскость: {planeObj.name} на расстоянии {distanceFromCamera}m");

        generatedPlanes.Add(planeObject);
        planeCreationTimes[planeObject] = Time.time;

        if (xrOrigin.TrackablesParent != null)
        {
            planeObject.transform.SetParent(xrOrigin.TrackablesParent, true);
            // Debug.Log($"[ARManagerInitializer2] Базовая плоскость {planeObj.name} привязана к {xrOrigin.TrackablesParent.name}.");
        } else {
            // Debug.LogWarning($"[ARManagerInitializer2] TrackablesParent не найден на XROrigin, базовая плоскость {planeObj.name} не будет привязана.");
        }
    }

    // НОВЫЙ МЕТОД: Обновление существующей плоскости или создание новой для области стены
    private bool UpdateOrCreatePlaneForWallArea(Rect area, int textureWidth, int textureHeight, Dictionary<GameObject, bool> visitedPlanes)
    {
        if (xrOrigin == null || xrOrigin.Camera == null)
        {
            Debug.LogError("[ARManagerInitializer2] ❌ XROrigin или его камера не найдены");
            return false;
        }
        
        // Получаем размеры и положение плоскости
        float planeWidthInMeters = area.width / textureWidth * 5.0f;
        
        // Проверяем минимальный размер
        if (planeWidthInMeters < minPlaneSizeInMeters)
        {
            return false;
        }
        
        float aspectRatio = 0.75f;
        float planeHeightInMeters = planeWidthInMeters * aspectRatio;
        
        // Получаем данные камеры
        Camera arCamera = xrOrigin.Camera;
        Vector3 cameraPosition = arCamera.transform.position;
        Vector3 cameraForward = arCamera.transform.forward;
        Vector3 cameraUp = arCamera.transform.up;
        Vector3 cameraRight = arCamera.transform.right;
        
        // Нормализуем координаты из текстуры
        float normalizedX = area.x / textureWidth;
        float normalizedY = area.y / textureHeight;
        
        // Смягченная фильтрация по краям и углам экрана - позволяем обрабатывать больше областей
        if (normalizedX < 0.05f || normalizedX > 0.95f || normalizedY < 0.05f || normalizedY > 0.95f)
        {
            return false; // Отсекаем только самые крайние области (5% от краев)
        }
        
        // Более мягкая фильтрация углов - позволяем обнаруживать больше стен
        if ((normalizedX < 0.1f && normalizedY < 0.1f) || 
            (normalizedX > 0.9f && normalizedY < 0.1f) ||  
            (normalizedX < 0.1f && normalizedY > 0.9f) ||  
            (normalizedX > 0.9f && normalizedY > 0.9f))
        {
            return false; // Фильтруем только самые угловые области (10%)
        }
        
        // Немного менее агрессивная фильтрация верхней части экрана
        if (normalizedY < 0.08f) // Уменьшено с 0.15
        {
            return false; // Отсекаем только самую верхнюю часть
        }
        
        // УЛУЧШЕННОЕ ПРОЕЦИРОВАНИЕ: Более точные расчеты на основе перспективы камеры
        float horizontalFov = 2.0f * Mathf.Atan(Mathf.Tan(arCamera.fieldOfView * Mathf.Deg2Rad * 0.5f) * arCamera.aspect);
        float angleH = (normalizedX - 0.5f) * horizontalFov;
        float angleV = (normalizedY - 0.5f) * arCamera.fieldOfView * Mathf.Deg2Rad;
        
        // УЛУЧШЕННАЯ НЕЛИНЕЙНАЯ КОРРЕКЦИЯ: адаптивная коррекция на основе глубины сцены
        float focalDepth = 2.5f; // Предполагаемая глубина фокуса
        float perspectiveStrength = 0.15f; // Сила перспективной коррекции
        
        // Более сильная коррекция для краевых областей с учетом дисторсии объектива
        float distortionFactor = 1.5f; // Фактор дисторсии объектива (больше для широкоугольных камер)
        float correctionH = Mathf.Sign(angleH) * Mathf.Pow(Mathf.Abs(angleH) / (0.5f * horizontalFov), distortionFactor) * perspectiveStrength * focalDepth;
        float correctionV = Mathf.Sign(angleV) * Mathf.Pow(Mathf.Abs(angleV) / (0.5f * arCamera.fieldOfView * Mathf.Deg2Rad), distortionFactor) * perspectiveStrength * focalDepth;
        
        // Дополнительная коррекция для более точного выравнивания с реальным миром
        if (Mathf.Abs(angleH) > horizontalFov * 0.3f)
        {
            // Усиленная коррекция для боковых областей экрана
            correctionH *= 1.2f;
        }
        
        if (Mathf.Abs(angleV) > arCamera.fieldOfView * Mathf.Deg2Rad * 0.3f)
        {
            // Усиленная коррекция для верхних/нижних областей экрана
            correctionV *= 1.2f;
        }
        
        angleH += correctionH;
        angleV += correctionV;
        
        Vector3 rayDirection = cameraForward +
                               cameraRight * Mathf.Tan(angleH) +
                               cameraUp * Mathf.Tan(angleV);
        rayDirection.Normalize();
        
        // УЛУЧШЕННОЕ ОПРЕДЕЛЕНИЕ РАССТОЯНИЯ: Многослойный подход для точного определения расстояния до стены
        float distanceFromCamera = 2.2f; // Умеренное начальное значение для предотвращения перекрытия экрана
        
        // УЛУЧШЕННЫЙ МНОГОТОЧЕЧНЫЙ РЕЙКАСТИНГ: Расширенный набор лучей и анализ их результатов
        RaycastHit hit = new RaycastHit();
        int layerMask = ~0; // Все слои
        bool didHit = false;
        
        // Массив смещений для лучей с более широким охватом и разной плотностью
        // Центральная область имеет более высокую плотность лучей
        List<Vector3> rayOffsets = new List<Vector3>();
        
        // Центральный луч (с наивысшим приоритетом)
        rayOffsets.Add(Vector3.zero);
        
        // Ближние лучи вокруг центра (высокий приоритет)
        float innerRadius = 0.08f; // Внутренний радиус в метрах
        rayOffsets.Add(cameraRight * innerRadius);                 // Правый
        rayOffsets.Add(-cameraRight * innerRadius);                // Левый
        rayOffsets.Add(cameraUp * innerRadius);                    // Верхний
        rayOffsets.Add(-cameraUp * innerRadius);                   // Нижний
        rayOffsets.Add(cameraRight * innerRadius * 0.7f + cameraUp * innerRadius * 0.7f);  // Правый верхний
        rayOffsets.Add(cameraRight * innerRadius * 0.7f - cameraUp * innerRadius * 0.7f);  // Правый нижний
        rayOffsets.Add(-cameraRight * innerRadius * 0.7f + cameraUp * innerRadius * 0.7f); // Левый верхний
        rayOffsets.Add(-cameraRight * innerRadius * 0.7f - cameraUp * innerRadius * 0.7f); // Левый нижний
        
        // Дальние лучи (средний приоритет)
        float outerRadius = 0.15f; // Внешний радиус в метрах
        rayOffsets.Add(cameraRight * outerRadius);                 // Дальний правый
        rayOffsets.Add(-cameraRight * outerRadius);                // Дальний левый 
        rayOffsets.Add(cameraUp * outerRadius);                    // Дальний верхний
        rayOffsets.Add(-cameraUp * outerRadius);                   // Дальний нижний
        
        float bestDistance = float.MaxValue;
        Vector3 bestNormal = Vector3.zero;
        float bestConfidence = 0f;
        
        // Хранение всех успешных попаданий лучей для анализа
        List<RaycastHit> successfulHits = new List<RaycastHit>();
        List<float> hitWeights = new List<float>();
        
        // --- НАЧАЛО БЛОКА ЛОГИРОВАНИЯ РЕЙКАСТОВ ---
        int totalRaysShot = 0;
        int raysHitSomething = 0;
        int raysHitValidSurface = 0;
        // --- КОНЕЦ БЛОКА ЛОГИРОВАНИЯ РЕЙКАСТОВ ---

        // ВЫПОЛНЯЕМ СЕРИЮ РЕЙКАСТОВ с разными смещениями и весовыми коэффициентами
        for (int i = 0; i < rayOffsets.Count; i++)
        {
            Vector3 offsetPos = cameraPosition + rayOffsets[i];
            float rayWeight = 1.0f; // Базовый вес
            totalRaysShot++; // --- ЛОГИРОВАНИЕ ---
            
            // Центральные лучи имеют больший вес
            if (i == 0) rayWeight = 2.0f; // Центральный луч
            else if (i < 5) rayWeight = 1.5f; // Ближние к центру
            
            if (Physics.Raycast(offsetPos, rayDirection, out hit, 10.0f, layerMask))
            {
                // УЛУЧШЕННАЯ ФИЛЬТРАЦИЯ: Исключаем AR объекты, UI элементы, другие плоскости
                bool isValidHit = true;
                raysHitSomething++; // --- ЛОГИРОВАНИЕ ---
                
                // Имя объекта содержит ключевые слова, которые указывают на AR/UI элементы
                if (hit.collider.gameObject.name.Contains("AR") || 
                    hit.collider.gameObject.name.Contains("UI") ||
                    hit.collider.gameObject.name.Contains("XR") ||
                    hit.collider.gameObject.name.Contains("Plane"))
                {
                    isValidHit = false;
                    // --- ЛОГИРОВАНИЕ ---
                    // Debug.Log($"[ARManagerInitializer2] Рейкаст отфильтрован по имени: {hit.collider.gameObject.name}");
                    // --- КОНЕЦ ЛОГИРОВАНИЯ ---
                }
                
                // Проверяем слой объекта (можно добавить проверки конкретных слоев)
                // if (hit.collider.gameObject.layer == 8) isValidHit = false; // Пример проверки слоя
                
                if (isValidHit)
                {
                    raysHitValidSurface++; // --- ЛОГИРОВАНИЕ ---
                    // Сохраняем информацию о попадании
                    successfulHits.Add(hit);
                    hitWeights.Add(rayWeight);
                    
                    // Определяем лучшее попадание на основе веса луча и расстояния
                    float combinedMetric = hit.distance / rayWeight;
                    
                    if (combinedMetric < bestDistance)
                    {
                        bestDistance = combinedMetric;
                        bestNormal = hit.normal;
                        bestConfidence = rayWeight;
                        didHit = true;
                    }
                }
            }
        }
        
        // --- ЛОГИРОВАНИЕ РЕЗУЛЬТАТОВ РЕЙКАСТОВ ---
        // Debug.Log($"[ARManagerInitializer2] Рейкасты: Всего выпущено={totalRaysShot}, Попало во что-то={raysHitSomething}, Прошло фильтр={raysHitValidSurface}, SuccessfulHits.Count={successfulHits.Count}");
        if (raysHitSomething > 0 && raysHitValidSurface == 0)
        {
            // Debug.Log("[ARManagerInitializer2] Все попадания рейкастов были отфильтрованы по имени или слою.");
        }
        // --- КОНЕЦ ЛОГИРОВАНИЯ РЕЗУЛЬТАТОВ РЕЙКАСТОВ ---
        
        // УЛУЧШЕННЫЙ АНАЛИЗ РЕЗУЛЬТАТОВ: Кластеризация попаданий для более точного определения стены
        if (successfulHits.Count > 3) // Если у нас достаточно данных для кластеризации
        {
            // Группируем попадания по расстоянию и нормали
            var distanceClusters = new Dictionary<float, List<int>>();
            
            for (int i = 0; i < successfulHits.Count; i++)
            {
                float distance = successfulHits[i].distance;
                bool foundCluster = false;
                
                foreach (var clusterCenter in distanceClusters.Keys.ToList())
                {
                    if (Mathf.Abs(distance - clusterCenter) < 0.3f) // Порог кластеризации по расстоянию
                    {
                        distanceClusters[clusterCenter].Add(i);
                        foundCluster = true;
                        break;
                    }
                }
                
                if (!foundCluster)
                {
                    distanceClusters[distance] = new List<int> { i };
                }
            }
            
            // Находим самый значимый кластер (с наибольшим суммарным весом)
            float bestClusterWeight = 0f;
            float bestClusterDistance = 0f;
            Vector3 bestClusterNormal = Vector3.zero;
            
            foreach (var cluster in distanceClusters)
            {
                float clusterWeight = 0f;
                Vector3 clusterNormal = Vector3.zero;
                
                foreach (int index in cluster.Value)
                {
                    clusterWeight += hitWeights[index];
                    clusterNormal += successfulHits[index].normal * hitWeights[index];
                }
                
                if (clusterWeight > bestClusterWeight)
                {
                    bestClusterWeight = clusterWeight;
                    bestClusterDistance = cluster.Key;
                    bestClusterNormal.Normalize();
                    bestClusterNormal = clusterNormal / clusterWeight;
                }
            }
            
            // Используем данные лучшего кластера, если он достаточно значимый
            if (bestClusterWeight > bestConfidence)
            {
                bestDistance = bestClusterDistance;
                bestNormal = bestClusterNormal.normalized;
                bestConfidence = bestClusterWeight;
            }
        }
        
        // УЛУЧШЕННЫЙ АЛГОРИТМ ОПРЕДЕЛЕНИЯ РАССТОЯНИЯ: Многоуровневый подход
        if (didHit)
        {
            // Результат рейкастинга найден - используем его с небольшим отступом
            // bestDistance содержит значение метрики (distance/weight), нужно восстановить настоящее расстояние
            float actualDistance = successfulHits.Count > 0 ? successfulHits[0].distance : bestDistance;
            for (int i = 0; i < successfulHits.Count; i++)
            {
                if (hitWeights[i] == bestConfidence)
                {
                    actualDistance = successfulHits[i].distance;
                    break;
                }
            }
            
            // Применяем небольшой отступ для избежания наложений
            distanceFromCamera = actualDistance + 0.02f;
            
            // Ограничиваем минимальное и максимальное значения для предотвращения экстремальных случаев
            distanceFromCamera = Mathf.Clamp(distanceFromCamera, 1.0f, 6.0f);
            Debug.Log($"[ARManagerInitializer2] 📏 Найдена реальная поверхность на расстоянии {distanceFromCamera:F2}м (доверие: {bestConfidence:F1})");
        }
        else
        {
            // РАСШИРЕННЫЙ АЛГОРИТМ для случаев, когда рейкастинг не нашел поверхностей
            
            // Шаг 1: Пробуем использовать информацию от существующих AR плоскостей
            bool foundARPlane = false;
            if (planeManager != null && planeManager.trackables.count > 0)
            {
                // --- ЛОГИРОВАНИЕ ПОИСКА AR ПЛОСКОСТЕЙ ---
                // Debug.Log($"[ARManagerInitializer2] Поиск AR-плоскостей: Всего {planeManager.trackables.count} AR-плоскостей в сцене.");
                // --- КОНЕЦ ЛОГИРОВАНИЯ ---
                ARPlane bestMatchPlane = null;
                float bestMatchScore = 0f;
                float bestMatchDistance = 0f;
                
                foreach (var plane in planeManager.trackables)
                {
                    if (plane == null) continue;
                    
                    Vector3 planeCenter = plane.center;
                    Vector3 planeSurfaceNormal = plane.normal;
                    
                    // УЛУЧШЕННАЯ ОЦЕНКА СООТВЕТСТВИЯ ПЛОСКОСТИ ЛУЧУ
                    // Учитываем ориентацию, расстояние и размер плоскости
                    
                    // Фактор ориентации - насколько плоскость параллельна лучу
                    float angleWithRay = Vector3.Angle(planeSurfaceNormal, -rayDirection);
                    float orientationFactor = Mathf.Cos(angleWithRay * Mathf.Deg2Rad);
                    if (orientationFactor < 0.3f) continue; // Игнорируем плоскости с плохой ориентацией
                    
                    // Фактор расстояния - насколько плоскость близко к проекции луча
                    Vector3 toCenterVector = planeCenter - cameraPosition;
                    float projectionLength = Vector3.Dot(toCenterVector, rayDirection);
                    
                    // Игнорируем плоскости позади камеры или слишком далеко
                    if (projectionLength <= 0.5f || projectionLength > 8.0f) continue;
                    
                    // Находим ближайшую точку луча к центру плоскости
                    Vector3 projectedPoint = cameraPosition + rayDirection * projectionLength;
                    float perpendicularDistance = Vector3.Distance(projectedPoint, planeCenter);
                    
                    // Фактор перпендикулярного расстояния - насколько точка проекции близка к центру плоскости
                    // Учитываем размер плоскости - для больших плоскостей допускаем большее расстояние
                    float sizeCompensation = Mathf.Sqrt(plane.size.x * plane.size.y);
                    float maxPerpDistance = 0.5f + sizeCompensation * 0.5f;
                    
                    if (perpendicularDistance > maxPerpDistance) continue;
                    
                    // Вычисляем общий скор для этой плоскости
                    float perpDistanceFactor = 1.0f - (perpendicularDistance / maxPerpDistance);
                    float distanceFactor = 1.0f - Mathf.Clamp01((projectionLength - 1.0f) / 7.0f); // Ближе лучше
                    float sizeFactor = Mathf.Clamp01(sizeCompensation / 2.0f); // Чем больше плоскость, тем лучше
                    
                    // Комбинируем факторы с разными весами
                    float planeScore = orientationFactor * 0.4f + perpDistanceFactor * 0.4f + distanceFactor * 0.1f + sizeFactor * 0.1f;
                    
                    if (planeScore > bestMatchScore)
                    {
                        bestMatchScore = planeScore;
                        bestMatchPlane = plane;
                        bestMatchDistance = projectionLength;
                    }
                }
                
                // --- ЛОГИРОВАНИЕ ПОИСКА AR ПЛОСКОСТЕЙ ---
                if (bestMatchPlane == null && planeManager.trackables.count > 0)
                {
                    // Debug.Log($"[ARManagerInitializer2] Подходящая AR-плоскость не найдена (макс. скор был {bestMatchScore:F2}, порог 0.6).");
                }
                // --- КОНЕЦ ЛОГИРОВАНИЯ ---
                
                // Если нашли подходящую AR плоскость, используем её
                if (bestMatchPlane != null && bestMatchScore > 0.6f) // Требуем достаточно высокий скор
                {
                    // Небольшой отступ от плоскости
                    distanceFromCamera = bestMatchDistance - 0.05f;
                    
                    // Ограничиваем диапазон для безопасности
                    distanceFromCamera = Mathf.Clamp(distanceFromCamera, 1.0f, 5.0f);
                    
                    // Сохраняем тип плоскости для ориентации
                    bool isVertical = Mathf.Abs(Vector3.Dot(bestMatchPlane.normal, Vector3.up)) < 0.3f;
                    
                    Debug.Log($"[ARManagerInitializer2] 📏 Используется AR плоскость: {distanceFromCamera:F2}м (скор: {bestMatchScore:F2}, {(isVertical ? "вертикальная" : "горизонтальная")})");
                    foundARPlane = true;
                    
                    // Используем нормаль плоскости для bestNormal, если нет результатов рейкастинга
                    bestNormal = bestMatchPlane.normal;
                }
            }
            
            // Шаг 2: Если не нашли AR плоскость, используем адаптивный эвристический подход
            if (!foundARPlane)
            {
                // --- ЛОГИРОВАНИЕ ПРИЧИНЫ ЭВРИСТИКИ ---
                if (planeManager == null || planeManager.trackables.count == 0)
                {
                    // Debug.Log("[ARManagerInitializer2] Эвристика: ARPlaneManager не доступен или нет AR-плоскостей.");
                }
                else if (didHit) // Эта ветка сейчас не достижима, т.к. эвристика вызывается если didHit == false
                {
                     // Debug.Log("[ARManagerInitializer2] Эвристика: Рейкасты что-то нашли, но AR-плоскость не подошла.");
                }
                else
                {
                    // Debug.Log("[ARManagerInitializer2] Эвристика: Ни рейкасты, ни поиск по AR-плоскостям не дали результата.");
                }
                // --- КОНЕЦ ЛОГИРОВАНИЯ ПРИЧИНЫ ЭВРИСТИКИ ---

                // Используем комбинацию статистических данных и контекстной информации
                
                // Анализ текущей позиции в пространстве
                float viewportY = normalizedY;
                float adaptiveBaseDistance;
                
                // Для разных частей экрана используем разные базовые расстояния
                if (viewportY < 0.3f)
                {
                    // Нижняя часть экрана - обычно близкие объекты
                    adaptiveBaseDistance = 1.8f;
                }
                else if (viewportY > 0.7f)
                {
                    // Верхняя часть экрана - обычно дальние объекты
                    adaptiveBaseDistance = 2.5f;
                }
                else
                {
                    // Середина экрана - средние расстояния
                    adaptiveBaseDistance = 2.2f;
                }
                
                // Адаптируем расстояние на основе размера плоскости и позиции на экране
                float sizeAdjustment = planeWidthInMeters * 0.3f;
                float positionAdjustment = Mathf.Abs(normalizedX - 0.5f) * 0.5f; // Боковые части немного дальше
                
                distanceFromCamera = adaptiveBaseDistance + sizeAdjustment + positionAdjustment;
                
                // Ограничиваем диапазон для безопасности
                distanceFromCamera = Mathf.Clamp(distanceFromCamera, 1.4f, 4.5f);
                
                Debug.Log($"[ARManagerInitializer2] ⚠️ Используется адаптивное эвристическое расстояние: {distanceFromCamera:F2}м");
                
                // По умолчанию ориентируем плоскость перпендикулярно лучу
                bestNormal = -rayDirection;
            }
        }
        
        // Позиция плоскости в мировом пространстве
        Vector3 planePos = cameraPosition + rayDirection * distanceFromCamera;
        
        // Вычисляем ориентацию плоскости (нормаль)
        Vector3 planeNormal;
        Quaternion planeRotation;
        
        if (didHit && bestNormal != Vector3.zero) 
        {
            // Если нашли реальную поверхность, используем ее нормаль
            planeNormal = bestNormal;
            // Создаем поворот от базовой оси вперед к нормали плоскости
            planeRotation = Quaternion.FromToRotation(Vector3.forward, -planeNormal);
            Debug.Log($"[ARManagerInitializer2] 🧭 Используем нормаль реальной поверхности: {planeNormal}");
        }
        else 
        {
            // Иначе, ориентируем плоскость перпендикулярно лучу от камеры
            planeNormal = -rayDirection;
            
            // Но проверяем, не слишком ли плоскость наклонена (не должна быть "полом")
            float upDot = Vector3.Dot(planeNormal, Vector3.up);
            if (Mathf.Abs(upDot) > 0.7f) 
            {
                // Если плоскость слишком горизонтальна, корректируем её ориентацию
                // Делаем её более вертикальной
                Vector3 horizontalDirection = rayDirection;
                horizontalDirection.y = 0;
                horizontalDirection.Normalize();
                
                // Плавно переходим от направления луча к горизонтальному направлению
                planeNormal = Vector3.Lerp(-rayDirection, -horizontalDirection, Mathf.Abs(upDot) - 0.3f).normalized;
                Debug.Log($"[ARManagerInitializer2] 🧭 Корректируем ориентацию слишком горизонтальной плоскости");
            }
            
            // Ориентация на основе нормали
            planeRotation = Quaternion.FromToRotation(Vector3.forward, -planeNormal);
            Debug.Log($"[ARManagerInitializer2] 🧭 Ориентация на основе луча: {planeNormal}");
        }
        
        // Проверки и фильтрация
        // После вычисления planePos, добавим более жесткую проверку
        // на дубликаты и плоскости перед камерой
        
        // Проверка 1: Не создаем плоскость, если находится слишком близко к камере и прямо перед ней
        Vector3 directionToPosition = planePos - arCamera.transform.position;
        float distanceToCam = directionToPosition.magnitude;
        float alignmentWithCamera = Vector3.Dot(arCamera.transform.forward.normalized, directionToPosition.normalized);
        
        // ИЗМЕНЕНО: Смягчаем проверку на близость к камере
        if (distanceToCam < 1.0f && alignmentWithCamera > 0.85f)
        {
            // Debug.Log($"[ARManagerInitializer2] ⚠️ Плоскость слишком близко к камере ({distanceToCam:F2}м), пропускаем");
            return false;
        }
        
        // УЛУЧШЕННЫЙ АЛГОРИТМ: Интеллектуальное выявление дубликатов плоскостей
        bool tooClose = false;
        int similarOrientationCount = 0;
        GameObject closestExistingPlane = null;
        float closestDuplDistance = float.MaxValue;
        
        // === НАЧАЛО БЛОКА ПОИСКА И ОБНОВЛЕНИЯ СУЩЕСТВУЮЩЕЙ ПЛОСКОСТИ ===
        // ИЩЕМ СУЩЕСТВУЮЩУЮ ПЛОСКОСТЬ ДЛЯ ОБНОВЛЕНИЯ
        // Используем более мягкие критерии, чтобы чаще обновлять существующие плоскоСТИ, чем создавать новые.
        // Это должно помочь с "миганием".
        // Параметры для FindClosestExistingPlane: (позицияНовой, нормальНовой, максРасстояниеДляОбновления, максУголДляОбновления)
        var (planeToUpdate, updateDistance, updateAngleDiff) = FindClosestExistingPlane(planePos, planeNormal, 1.5f, 60f); // Было 1.0f, 45f

        if (planeToUpdate != null)
        {
            // Debug.Log($"[ARManagerInitializer2] 🔄 Обновляем существующую плоскость '{planeToUpdate.name}' вместо создания новой. Расстояние: {updateDistance:F2}м, Угол: {updateAngleDiff:F1}°");
            
            // Обновляем позицию и ориентацию существующей плоскости
            planeToUpdate.transform.position = planePos;
            planeToUpdate.transform.rotation = planeRotation;
            
            // Опционально: Обновить меш, если размеры области значительно изменились
            MeshFilter mf = planeToUpdate.GetComponent<MeshFilter>();
            if (mf != null) 
            {
               // Убедимся, что planeWidthInMeters и planeHeightInMeters доступны и корректны
               mf.mesh = CreatePlaneMesh(planeWidthInMeters, planeHeightInMeters); 
            }
            // Масштаб (distanceScale) вычисляется позже, поэтому здесь его не обновляем, если выходим раньше.
            // planeToUpdate.transform.localScale = new Vector3(distanceScale, distanceScale, 1.0f); 

            if (visitedPlanes != null) visitedPlanes[planeToUpdate] = true; // Помечаем обновленную плоскость как посещенную
            // planesCreatedThisFrame++; // Если считаем обновление за создание для статистики
            Debug.Log($"[ARManagerInitializer2] Обновлена плоскость '{planeToUpdate.name}' для области X:{area.x:F0} Y:{area.y:F0} W:{area.width:F0} H:{area.height:F0}");
            return true; // Успешно обновили существующую плоскость, дальше не идем
        }
        // === КОНЕЦ БЛОКА ПОИСКА И ОБНОВЛЕНИЯ СУЩЕСТВУЮЩЕЙ ПЛОСКОСТИ ===
        
        foreach (GameObject existingPlane in generatedPlanes)
        {
            if (existingPlane == null) continue;
            
            // Рассчитываем расстояние между плоскостями
            float distanceBetweenPlanes = Vector3.Distance(existingPlane.transform.position, planePos);
            
                         // Сохраняем ближайшую плоскость для возможной замены
             if (distanceBetweenPlanes < closestDuplDistance)
             {
                 closestDuplDistance = distanceBetweenPlanes;
                 closestExistingPlane = existingPlane;
             }
            
            // Проверка на прямой дубликат (очень близкое расположение)
            if (distanceBetweenPlanes < 0.35f)
            {
                tooClose = true;
                // Debug.Log($"[ARManagerInitializer2] ⚠️ Обнаружен близкий дубликат плоскости на расстоянии {distanceBetweenPlanes:F2}м");
                break;
            }
            
            // Проверка на плоскости с похожей ориентацией в средней близости
            if (distanceBetweenPlanes < 1.0f)
            {
                                 // Сравниваем направления нормалей плоскостей
                 float dotProduct = Vector3.Dot(existingPlane.transform.forward, -rayDirection);
                
                if (Mathf.Abs(dotProduct) > 0.9f) // Очень похожая ориентация
                {
                    tooClose = true;
                    // Debug.Log($"[ARManagerInitializer2] ⚠️ Обнаружена плоскость с похожей ориентацией на расстоянии {distanceBetweenPlanes:F2}м");
                    break;
                }
                else if (Mathf.Abs(dotProduct) > 0.7f) // Умеренно похожая ориентация
                {
                    similarOrientationCount++;
                }
            }
            
            // Проверка на общее количество плоскостей в одном направлении
            if (distanceBetweenPlanes < 2.0f)
            {
                                 float dotProduct = Vector3.Dot(existingPlane.transform.forward, -rayDirection);
                if (Mathf.Abs(dotProduct) > 0.6f)
                {
                    similarOrientationCount++;
                }
            }
        }
        
        // Если уже несколько плоскостей смотрят в похожем направлении, пропускаем создание новой
        if (similarOrientationCount >= 3)
        {
            // Debug.Log($"[ARManagerInitializer2] ⚠️ Уже существует {similarOrientationCount} плоскостей в похожем направлении");
            return false;
        }
        
                 // Улучшенный алгоритм замены плоскостей вместо добавления новых
         if (tooClose && generatedPlanes.Count > 4 && closestExistingPlane != null && closestDuplDistance < 1.0f)
        {
            // Если мы обнаружили дубликат, и у нас уже много плоскостей, 
            // то заменяем существующую плоскость вместо добавления новой
            // Debug.Log($"[ARManagerInitializer2] 🔄 Заменяем существующую плоскость вместо создания дубликата");
            generatedPlanes.Remove(closestExistingPlane);
            Destroy(closestExistingPlane);
            tooClose = false; // Разрешаем создание новой плоскости
        }
        else if (tooClose)
        {
            // Debug.Log($"[ARManagerInitializer2] ⚠️ Обнаружен дубликат плоскости, пропускаем создание");
            return false;
        }
        
        // ПРОВЕРКА 3: Отсеиваем объекты на экстремальных углах обзора
        // ИЗМЕНЕНО: Слегка увеличиваем допустимый угол обзора для плоскостей
        if (Mathf.Abs(angleH) > 0.95f || Mathf.Abs(angleV) > 0.95f) // Было 0.45f и 0.35f
        {
            // Debug.Log($"[ARManagerInitializer2] ⚠️ Плоскость находится на экстремальном угле обзора (НОВЫЕ МЯГКИЕ УСЛОВИЯ), пропускаем");
            return false;
        }
        
        // Создаем и настраиваем GameObject для плоскости
        GameObject planeObj = new GameObject($"MyARPlane_Debug_{planeInstanceCounter++}");
        // Debug.Log($"[ARManagerInitializer2] Создана новая плоскость '{planeObj.name}' для области X:{area.x:F0} Y:{area.y:F0} W:{area.width:F0} H:{area.height:F0}");

        // Добавляем компоненты
        MeshFilter meshFilter = planeObj.AddComponent<MeshFilter>();
        meshFilter.mesh = CreatePlaneMesh(planeWidthInMeters, planeHeightInMeters);
        
        MeshRenderer meshRenderer = planeObj.AddComponent<MeshRenderer>();
        if (this.verticalPlaneMaterial != null) 
        {
            meshRenderer.material = new Material(this.verticalPlaneMaterial); 
            // Можно сделать полупрозрачным для отладки
            // Color color = meshRenderer.material.color;
            // color.a = 0.7f; 
            // meshRenderer.material.color = color;
        }
        else
        {
             Debug.LogError("[ARManagerInitializer2-CreatePlaneForWallArea] wallMaterialVertical is not set! Assigning default magenta.");
             Material simpleMaterial = new Material(Shader.Find("Unlit/Color"));
             simpleMaterial.color = Color.magenta;
             meshRenderer.material = simpleMaterial;
        }
        Debug.Log($"[ARManagerInitializer2-CreatePlaneForWallArea] Applied material to {planeObj.name}. Mesh bounds: {meshFilter.mesh.bounds.size}");
        
        MeshCollider meshCollider = planeObj.AddComponent<MeshCollider>();
        meshCollider.sharedMesh = meshFilter.mesh;
        
        this.generatedPlanes.Add(planeObj);
        if (this.planeCreationTimes != null) this.planeCreationTimes[planeObj] = Time.time;

        // Попытка привязать к TrackablesParent, если он есть и не был равен null при старте
        if (this.xrOrigin != null && this.xrOrigin.TrackablesParent != null)
        {
            // Проверяем, не является ли TrackablesParent частью самого XR Origin, который может быть отключен при симуляции
            // и имеет ли он тот же InstanceID, что и при старте (на случай если он был пересоздан)
            if (this.trackablesParentInstanceID_FromStart == 0 || 
                (this.xrOrigin.TrackablesParent.gameObject.activeInHierarchy && this.xrOrigin.TrackablesParent.GetInstanceID() == this.trackablesParentInstanceID_FromStart))
            {
                planeObj.transform.SetParent(this.xrOrigin.TrackablesParent, true); 
                Debug.Log($"[ARManagerInitializer2-CreatePlaneForWallArea] {planeObj.name} привязан к {this.xrOrigin.TrackablesParent.name} (ID: {this.xrOrigin.TrackablesParent.GetInstanceID()}).");
            }
            else
            {
                Debug.LogWarning($"[ARManagerInitializer2-CreatePlaneForWallArea] {planeObj.name} не привязан к TrackablesParent, так как он неактивен или был изменен (ожидался ID: {this.trackablesParentInstanceID_FromStart}, текущий: {this.xrOrigin.TrackablesParent.GetInstanceID()}, активен: {this.xrOrigin.TrackablesParent.gameObject.activeInHierarchy}). Оставлен в корне.");
            }
        }
        else
        {
            Debug.LogWarning($"[ARManagerInitializer2-CreatePlaneForWallArea] {planeObj.name} не привязан, так как XROrigin или TrackablesParent не найдены. Оставлен в корне.");
        }

        if (visitedPlanes != null) visitedPlanes[planeObj] = true; // Помечаем новую плоскость как посещенную
        return true; // Успешно создали новую плоскость
    }

    // Удаляем устаревшие плоскости, если их слишком много
    private void CleanupOldPlanes(Dictionary<GameObject, bool> visitedPlanes)
    {
        // Debug.Log($"[CleanupOldPlanes] Начало очистки. Всего плоскостей в generatedPlanes: {generatedPlanes.Count}. Посещено в этом кадре: {visitedPlanes.Count}");
        List<GameObject> planesToRemove = new List<GameObject>();
        float currentTime = Time.time;
        float planeLifetime = 10.0f; // Время жизни плоскости в секундах (если не обновляется)
        foreach (GameObject plane in generatedPlanes)
        {
            if (plane == null) continue;

            if (!visitedPlanes.ContainsKey(plane)) // Если плоскости НЕТ в словаре visitedPlanes, значит она не была подтверждена новой маской
            {
                // Debug.Log($"[CleanupOldPlanes] Плоскость {plane.name} (ID: {plane.GetInstanceID()}) не была посещена и будет удалена.");
                planesToRemove.Add(plane);
            }
            // else if (planeCreationTimes.ContainsKey(plane) && currentTime - planeCreationTimes[plane] > planeLifetime)
            // {
                // Debug.Log($"[CleanupOldPlanes] Плоскость {plane.name} (ID: {plane.GetInstanceID()}) существует слишком долго ({currentTime - planeCreationTimes[plane]:F1}с > {planeLifetime}с) и будет удалена.");
                // planesToRemove.Add(plane);
            // }
        }

        foreach (GameObject plane in planesToRemove)
        {
            // Debug.Log($"[CleanupOldPlanes] Уничтожение плоскости: {plane.name} (ID: {plane.GetInstanceID()})");
            generatedPlanes.Remove(plane);
            if (planeCreationTimes.ContainsKey(plane))
            {
                planeCreationTimes.Remove(plane);
            }
            Destroy(plane);
        }
        // Debug.Log($"[CleanupOldPlanes] Завершено. Удалено {planesToRemove.Count} плоскостей. Осталось в generatedPlanes: {generatedPlanes.Count}");
    }

    private (GameObject, float, float) FindClosestExistingPlane(Vector3 position, Vector3 normal, float maxDistance, float maxAngleDegrees)
    {
        GameObject closestPlane = null;
        float minDistance = float.MaxValue;
        float angleDifference = float.MaxValue;

        if (generatedPlanes == null) return (null, 0, 0);

        foreach (GameObject existingPlane in generatedPlanes)
        {
            if (existingPlane == null) continue;

            float dist = Vector3.Distance(existingPlane.transform.position, position);
            if (dist < minDistance && dist <= maxDistance)
            {
                // Предполагаем, что -transform.forward это нормаль плоскости, как и у новой 'normal'
                // Это согласуется с использованием Quaternion.LookRotation(-normal) или FromToRotation(Vector3.forward, -normal)
                float angle = Vector3.Angle(-existingPlane.transform.forward, normal); 

                if (angle <= maxAngleDegrees)
                {
                    minDistance = dist;
                    closestPlane = existingPlane;
                    angleDifference = angle;
                }
            }
        }
        return (closestPlane, minDistance, angleDifference);
    }

    // Вспомогательный метод для получения полного пути к GameObject
    public static string GetGameObjectPath(Transform transform)
    {
        if (transform == null) return "null_transform";
        string path = transform.name;
        Transform current = transform.parent;
        while (current != null)
        {
            path = current.name + "/" + path;
            current = current.parent;
        }
        return path;
    }
}