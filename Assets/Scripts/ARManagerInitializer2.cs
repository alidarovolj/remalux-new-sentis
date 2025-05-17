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
    public float minPlaneSize = 0.1f; // Уменьшен с 0.2 до 0.1 для создания большего числа плоскостей
    
    // Добавляем защиту от удаления недавно созданных плоскостей
    private Dictionary<GameObject, float> planeCreationTimes = new Dictionary<GameObject, float>();
    
    [Tooltip("Материал для вертикальных плоскостей")]
    public Material verticalPlaneMaterial;
    
    [Tooltip("Материал для горизонтальных плоскостей")]
    public Material horizontalPlaneMaterial;

    [Header("Отладка Визуализации Маски")] // Новый заголовок для инспектора
    [Tooltip("RawImage для отображения маски сегментации в UI.")]
    public UnityEngine.UI.RawImage debugMaskDisplay; // <--- ДОБАВЛЕНО ПОЛЕ

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

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        // Создаем GameObject с этим компонентом, который переживет перезагрузку сцены
        GameObject initializer = new GameObject("AR Manager Initializer 2");
        ARManagerInitializer2 component = initializer.AddComponent<ARManagerInitializer2>();
        
        // Принудительно устанавливаем значения по умолчанию
        component.useDetectedPlanes = false;
        
        // Объект не уничтожится при перезагрузке сцены
        DontDestroyOnLoad(initializer);
        Debug.Log("[ARManagerInitializer2] Инициализирован");
    }

    private void Awake()
    {
        // Реализация синглтона
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        Debug.Log("[ARManagerInitializer2] Start() called.");
        // Находим необходимые компоненты
        FindARComponents();
        
        // Подписываемся на событие обновления маски сегментации
        SubscribeToWallSegmentation();
        
        // ПОПЫТКА ОТКЛЮЧИТЬ ARPlaneManager
        if (this.planeManager != null)
        {
            Debug.LogWarning("[ARManagerInitializer2] Попытка отключить ARPlaneManager.");
            this.planeManager.enabled = false;
            if (!this.planeManager.enabled)
            {
                Debug.Log("[ARManagerInitializer2] ARPlaneManager успешно отключен.");
            }
            else
            {
                Debug.LogError("[ARManagerInitializer2] НЕ УДАЛОСЬ отключить ARPlaneManager!");
            }
        }
        
        // НОВОЕ: Отключаем стандартные визуализаторы плоскостей AR Foundation
        // DisableARFoundationVisualizers(); // ВРЕМЕННО ОТКЛЮЧАЕМ
        
        // Выводим текущие настройки
        Debug.Log($"[ARManagerInitializer2] Настройки инициализированы: useDetectedPlanes={useDetectedPlanes}");
        
        // Логирование TrackablesParent при старте
        if (this.xrOrigin != null && this.xrOrigin.TrackablesParent != null)
        {
            trackablesParentInstanceID_FromStart = this.xrOrigin.TrackablesParent.GetInstanceID(); // Сохраняем ID
            Debug.Log($"[ARManagerInitializer2-Start] TrackablesParent is: {this.xrOrigin.TrackablesParent.name}, ID: {trackablesParentInstanceID_FromStart}, Path: {GetGameObjectPath(this.xrOrigin.TrackablesParent)}");
        }
        else
        {
            Debug.LogWarning("[ARManagerInitializer2-Start] xrOrigin or xrOrigin.TrackablesParent is NULL.");
        }
    }

    private void Update()
    {
        frameCounter++;

        // Проверка, не изменился ли TrackablesParent
        if (this.xrOrigin != null && this.xrOrigin.TrackablesParent != null)
        {
            if (this.xrOrigin.TrackablesParent.GetInstanceID() != trackablesParentInstanceID_FromStart)
            {
                Debug.LogWarning($"[ARManagerInitializer2-Update] TrackablesParent ИЗМЕНИЛСЯ! Был ID: {trackablesParentInstanceID_FromStart}, стал ID: {this.xrOrigin.TrackablesParent.GetInstanceID()}, Path: {GetGameObjectPath(this.xrOrigin.TrackablesParent)}");
                trackablesParentInstanceID_FromStart = this.xrOrigin.TrackablesParent.GetInstanceID(); // Обновляем, чтобы не спамить
            }
        }

        // Обработка маски сегментации
        if (this.maskUpdated)
        {
            ProcessSegmentationMask();
        }

        // Проверка на тайм-аут сегментации
        if (this.hadValidSegmentationResult && Time.time - this.lastSuccessfulSegmentationTime > this.segmentationTimeoutSeconds)
        {
            Debug.LogWarning($"[ARManagerInitializer2] ⚠️ Тайм-аут сегментации ({this.segmentationTimeoutSeconds}с). Создание основной плоскости.");
            CreateBasicPlaneInFrontOfUser();
            this.hadValidSegmentationResult = false;
        }

        // Периодически очищаем устаревшие плоскости (каждые 60 кадров)
        if (this.frameCounter % 60 == 0)
        {
            CleanupOldPlanes();
        }

        // Обновление позиций плоскостей каждые 10 кадров
        // Это помогает стабилизировать плоскости
        if (this.frameCounter % 10 == 0)
        {
            UpdatePlanePositions();
        }

        // Каждые 60 кадров проводим проверку плоскостей на перекрытие
        // ИЗМЕНЕНО: Уменьшаем интервал для более частого удаления накладывающихся плоскостей
        if (this.frameCounter % 60 == 0) // Было 200
        {
            RemoveOverlayingPlanes();
        }

        // Периодически отключаем другие AR визуализаторы (каждые 60 кадров)
        if (this.frameCounter % 60 == 0)
        {
            // DisableOtherARVisualizers(); // ВРЕМЕННО ОТКЛЮЧАЕМ
        }

        // Вывод отладочной информации каждые 300 кадров
        if (this.frameCounter % 300 == 0)
        {
            int planeCount = this.generatedPlanes.Count;
            if (planeCount != this.lastPlaneCount)
            {
                Debug.Log($"[ARManagerInitializer2] 📊 Текущее количество плоскостей: {planeCount}");
                this.lastPlaneCount = planeCount;
            }
        }

        // ОТЛАДКА: Логирование позиции первой плоскости
        if (this.frameCounter % 30 == 0 && this.generatedPlanes.Count > 0 && this.generatedPlanes[0] != null)
        {
            Debug.Log($"[ARManagerInitializer2-DebugPlanePos] Plane 0 (ID: {this.generatedPlanes[0].GetInstanceID()}) world position: {this.generatedPlanes[0].transform.position}, rotation: {this.generatedPlanes[0].transform.rotation.eulerAngles}, parent: {this.generatedPlanes[0].transform.parent?.name ?? "null (Root)"}");
        }
        
        // Учащенная проверка Trackables (каждые 30 кадров)
        if (this.frameCounter % 30 == 0) 
        {
            if (this.xrOrigin != null && this.xrOrigin.TrackablesParent != null)
            {
                Debug.Log($"[ARManagerInitializer2-TrackableCheck-Update] Проверка TrackablesParent: {GetGameObjectPath(this.xrOrigin.TrackablesParent)} (ID: {this.xrOrigin.TrackablesParent.GetInstanceID()}). Количество дочерних объектов: {this.xrOrigin.TrackablesParent.childCount}");
                if (this.xrOrigin.TrackablesParent.childCount > 0)
                {
                    for (int i = 0; i < this.xrOrigin.TrackablesParent.childCount; i++)
                    {
                        Transform child = this.xrOrigin.TrackablesParent.GetChild(i);
                        Debug.Log($"[ARManagerInitializer2-TrackableCheck-Update] Child of Trackables: {child.name}, ID: {child.gameObject.GetInstanceID()}, Path: {GetGameObjectPath(child)}");
                        if (child.name.StartsWith("MyARPlane_Debug"))
                        {
                            Debug.LogWarning($"[ARManagerInitializer2-TrackableCheck-Update] ВНИМАНИЕ! Найдена плоскость '{child.name}' (ID: {child.gameObject.GetInstanceID()}) под TrackablesParent ({GetGameObjectPath(this.xrOrigin.TrackablesParent)})!");
                        }
                    }
                }
                else
                {
                    Debug.Log($"[ARManagerInitializer2-TrackableCheck-Update] TrackablesParent ({GetGameObjectPath(this.xrOrigin.TrackablesParent)}) has no children.");
                }
            }
            else
            {
                Debug.LogWarning("[ARManagerInitializer2-TrackableCheck-Update] xrOrigin or xrOrigin.TrackablesParent is NULL for periodic check.");
            }
        }
    }

    // Поиск необходимых компонентов в сцене
    private void FindARComponents()
    {
        if (sessionManager == null)
            sessionManager = FindObjectOfType<ARSessionManager>();
            
        if (planeManager == null)
            planeManager = FindObjectOfType<ARPlaneManager>();
            
        if (xrOrigin == null)
            xrOrigin = FindObjectOfType<XROrigin>();
            
        if (planeManager != null)
        {
            // Настраиваем обработку плоскостей
            planeManager.planesChanged += OnPlanesChanged;
            Debug.Log("[ARManagerInitializer2] Подписано на события planesChanged");
        }
        
        // Настраиваем материалы по умолчанию, если не заданы
        InitializeMaterials();
    }
    
    // Инициализация материалов для плоскостей
    private void InitializeMaterials()
    {
        // Настраиваем материалы по умолчанию, если не заданы
        if (verticalPlaneMaterial == null)
        {
            verticalPlaneMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            verticalPlaneMaterial.color = new Color(1.0f, 0.3f, 0.0f, 0.7f); // Ярко-оранжевый, полупрозрачный (0.7)
            verticalPlaneMaterial.SetFloat("_Surface", 1); // 1 = полупрозрачный
            verticalPlaneMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            verticalPlaneMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            verticalPlaneMaterial.SetInt("_ZWrite", 0); // Отключаем запись в буфер глубины для прозрачности
            verticalPlaneMaterial.EnableKeyword("_ALPHAPREMULTIPLY_ON");
            verticalPlaneMaterial.renderQueue = 3000; // Прозрачная очередь рендеринга
            
            // Умеренная эмиссия для лучшей видимости без перенасыщения
            verticalPlaneMaterial.EnableKeyword("_EMISSION");
            verticalPlaneMaterial.SetColor("_EmissionColor", new Color(1.0f, 0.3f, 0.0f, 0.7f) * 0.5f);
            
            // Делаем гладкую поверхность для отражений
            verticalPlaneMaterial.SetFloat("_Smoothness", 0.8f);
            verticalPlaneMaterial.SetFloat("_Metallic", 0.3f);
            
            Debug.Log("[ARManagerInitializer2] Создан материал для вертикальных плоскостей");
        }
        
        if (horizontalPlaneMaterial == null)
        {
            horizontalPlaneMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            horizontalPlaneMaterial.color = new Color(0.0f, 0.7f, 1.0f, 1.0f); 
            horizontalPlaneMaterial.SetFloat("_Surface", 0); // Непрозрачный
            horizontalPlaneMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            horizontalPlaneMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            horizontalPlaneMaterial.SetInt("_ZWrite", 1);
            horizontalPlaneMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            horizontalPlaneMaterial.renderQueue = 2000;
            
            // Включаем эмиссию
            horizontalPlaneMaterial.EnableKeyword("_EMISSION");
            horizontalPlaneMaterial.SetColor("_EmissionColor", new Color(0.0f, 0.7f, 1.0f, 1.0f) * 1.0f);
        }
    }

    // Подписка на события обновления маски сегментации
    private void SubscribeToWallSegmentation()
    {
        WallSegmentation wallSegmentation = FindObjectOfType<WallSegmentation>();
        if (wallSegmentation != null)
        {
            // Отписываемся от предыдущих событий
            wallSegmentation.OnSegmentationMaskUpdated -= OnSegmentationMaskUpdated;
            
            // Подписываемся на событие обновления маски
            wallSegmentation.OnSegmentationMaskUpdated += OnSegmentationMaskUpdated;
            Debug.Log("[ARManagerInitializer2] ✅ Подписка на события OnSegmentationMaskUpdated настроена");
        }
        else
        {
            Debug.LogWarning("[ARManagerInitializer2] ⚠️ WallSegmentation не найден в сцене! Инициализация компонента...");
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
            Debug.LogWarning("[ARManagerInitializer2-OnSegmentationMaskUpdated] Получена NULL маска.");
            if (debugMaskDisplay != null) // Если маска null, скрываем отображение
            {
                debugMaskDisplay.texture = null;
                debugMaskDisplay.gameObject.SetActive(false);
            }
            return;
        }

        currentSegmentationMask = mask;
        maskUpdated = true;
        hadValidSegmentationResult = true; // Фиксируем, что получили результат
        lastSuccessfulSegmentationTime = Time.time; // Обновляем время последнего успеха

        if (debugMaskDisplay != null) // <--- ДОБАВЛЕН БЛОК ОБНОВЛЕНИЯ RAWIMAGE
        {
            debugMaskDisplay.texture = currentSegmentationMask;
            if (!debugMaskDisplay.gameObject.activeSelf)
            {
                debugMaskDisplay.gameObject.SetActive(true);
            }
            // Опционально: можно настроить размер RawImage под размер маски, если нужно
            // debugMaskDisplay.rectTransform.sizeDelta = new Vector2(currentSegmentationMask.width, currentSegmentationMask.height);
        }

        Debug.Log($"[ARManagerInitializer2] Маска сегментации обновлена: {mask.width}x{mask.height}");
    }
    
    // Обработка маски сегментации для генерации плоскостей
    private void ProcessSegmentationMask()
    {
        if (currentSegmentationMask == null || !maskUpdated)
        {
            return;
        }

        // Сбрасываем флаг обновления маски
        maskUpdated = false;
        
        Debug.Log($"[ARManagerInitializer2] Обработка маски сегментации {currentSegmentationMask.width}x{currentSegmentationMask.height}");
        
        // Конвертируем RenderTexture в Texture2D для обработки
        Texture2D maskTexture = RenderTextureToTexture2D(currentSegmentationMask);
        
        if (maskTexture != null)
        {
            // Увеличиваем частоту обновления плоскостей, если их еще нет
            bool shouldUpdate = generatedPlanes.Count < 3 || (frameCounter % 60 == 0);
            
            // Всегда создаем хотя бы одну плоскость, если их нет
            if (generatedPlanes.Count == 0)
            {
                shouldUpdate = true;
            }
            
            if (shouldUpdate)
            {
                // Создаем плоскости на основе маски сегментации
                CreatePlanesFromMask(maskTexture);
                frameCounter = 0;
            }
            else
            {
                frameCounter++;
            }

            // Уничтожаем временную текстуру
            Destroy(maskTexture);
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
        // Получаем данные пикселей из текстуры
        Color32[] pixels = maskTexture.GetPixels32();
        int width = maskTexture.width;
        int height = maskTexture.height;

        Debug.Log($"[ARManagerInitializer2-CreatePlanesFromMask] maskTexture получена: Width={width}, Height={height}. Формат={maskTexture.format}");
        // ОТЛАДКА: Вывод нескольких пикселей из центра маски
        if (width > 20 && height > 20) // Убедимся, что текстура достаточно большая
        {
            System.Text.StringBuilder pixelDebug = new System.Text.StringBuilder();
            pixelDebug.Append("[ARManagerInitializer2-CreatePlanesFromMask] Центральные пиксели (R канал): ");
            for (int y = height / 2 - 2; y < height / 2 + 3; y++)
            {
                for (int x = width / 2 - 2; x < width / 2 + 3; x++)
                {
                    if (x >= 0 && x < width && y >= 0 && y < height)
                    {
                        pixelDebug.Append($"{pixels[y * width + x].r}, ");
                    }
                }
            }
            Debug.Log(pixelDebug.ToString());
            
            // Дополнительная проверка на наличие ненулевых пикселей
            int nonZeroRedPixels = 0;
            for(int i = 0; i < pixels.Length; i++)
            {
                if(pixels[i].r > 10) // Используем низкий порог для обнаружения хоть какого-то сигнала
                {
                    nonZeroRedPixels++;
                }
            }
            Debug.Log($"[ARManagerInitializer2-CreatePlanesFromMask] Найдено {nonZeroRedPixels} пикселей с R > 10 в полученной maskTexture (всего {pixels.Length}).");
        }
        
        // Находим области стен на маске
        List<Rect> wallAreas = FindWallAreas(pixels, width, height);
        Debug.Log($"[ARManagerInitializer2-CreatePlanesFromMask] Получено {wallAreas.Count} областей из FindWallAreas.");
        
        // Ограничиваем максимальное количество плоскостей
        int maxWallPlanes = 1; // Жестко ограничиваем одной плоскостью для теста
        
        // Если уже есть сгенерированные плоскости, ничего не делаем
        if (generatedPlanes.Count >= maxWallPlanes)
        {
            Debug.Log($"[ARManagerInitializer2-CreatePlanesFromMask] Уже существует {generatedPlanes.Count} плоскостей (лимит {maxWallPlanes}). Новые плоскости не создаются.");
            return;
        }
        
        // Сначала очищаем все существующие плоскости, если их стало слишком много
        if (generatedPlanes.Count > 10)
        {
            Debug.Log($"[ARManagerInitializer2] 🔄 Сброс всех плоскостей (было {generatedPlanes.Count}) для предотвращения перегрузки");
            foreach (GameObject plane in generatedPlanes)
            {
                if (plane != null)
                    Destroy(plane);
            }
            generatedPlanes.Clear();
            planeCreationTimes.Clear();
        }
        
        // Удаляем невалидные плоскости (null) из списка
        for (int i = generatedPlanes.Count - 1; i >= 0; i--)
        {
            if (generatedPlanes[i] == null)
            {
                generatedPlanes.RemoveAt(i);
                if (i < generatedPlanes.Count)
                {
                    i--; // Корректируем индекс после удаления
                }
            }
        }
        
        // Выбираем только крупные области для создания плоскостей
        List<Rect> significantAreas = wallAreas
            .OrderByDescending(area => area.width * area.height)
            .Take(maxWallPlanes)
            .ToList();
        
        Debug.Log($"[ARManagerInitializer2-CreatePlanesFromMask] После сортировки и Take({maxWallPlanes}) осталось {significantAreas.Count} областей.");
        if (significantAreas.Count > 0)
        {
            for(int i = 0; i < significantAreas.Count; i++)
            {
                Rect area = significantAreas[i];
                float normalizedAreaSize = (area.width * area.height) / (width * height);
                Debug.Log($"[ARManagerInitializer2-CreatePlanesFromMask] Кандидат на область {i}: x={area.x}, y={area.y}, w={area.width}, h={area.height}, normSize={normalizedAreaSize:F4}");
            }
        }
        
        // Ограничиваем максимальное количество создаваемых плоскостей
        // Создаем только ОДНУ плоскость за вызов для предотвращения накопления
        int maxPlanesToCreate = 1;
        
        // Не создаем новые плоскости, если их уже достаточно
        if (generatedPlanes.Count >= maxWallPlanes)
        {
            Debug.Log($"[ARManagerInitializer2] ⚠️ Достигнут лимит плоскостей ({generatedPlanes.Count}/{maxWallPlanes}). Пропускаем создание.");
            return;
        }
        
        // Счетчик создания плоскостей
        int planesCreated = 0;
        
        // Создаем только одну плоскость из наибольшей области
        if (significantAreas.Count > 0)
        {
            Rect bestArea = significantAreas[0];
            
            // Проверяем, достаточно ли большая площадь для создания плоскости
            float normalizedArea = (bestArea.width * bestArea.height) / (width * height);
            if (normalizedArea >= 0.02f) // Увеличиваем минимальный размер для создания плоскости
            {
                // Создаем плоскость для этой области с фиксированным размером
                CreatePlaneForWallArea(bestArea, width, height);
                planesCreated++;
                
                Debug.Log($"[ARManagerInitializer2] ✅ Создана 1 новая плоскость из области размером {normalizedArea:F3} (пиксели: {bestArea.width * bestArea.height})");
            }
            else
            {
                Debug.Log($"[ARManagerInitializer2-CreatePlanesFromMask] Лучшая область (normSize={normalizedArea:F4}, пиксели: {bestArea.width * bestArea.height}) слишком мала (требуется >= 0.02). Плоскость не создана.");
            }
        }
        
        // Обновляем время последней успешной сегментации
        lastSuccessfulSegmentationTime = Time.time;
        
        // Сохраняем информацию о созданных плоскостях
        hadValidSegmentationResult = true;
        
        Debug.Log($"[ARManagerInitializer2] ✅ Создано {planesCreated} новых плоскостей из {significantAreas.Count} областей. Всего плоскостей: {generatedPlanes.Count}");
    }

    // Метод для поиска связной области начиная с заданного пикселя
    private Rect FindConnectedArea(Color32[] pixels, int width, int height, int startX, int startY, bool[,] visited, byte threshold)
    {
        // Границы области
        int minX = startX;
        int maxX = startX;
        int minY = startY;
        int maxY = startY;
        
        // Очередь для алгоритма обхода в ширину
        Queue<Vector2Int> queue = new Queue<Vector2Int>();
        queue.Enqueue(new Vector2Int(startX, startY));
        visited[startX, startY] = true;
        
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
        
        // Возвращаем прямоугольник, охватывающий всю связную область
        return new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
    }

    private List<Rect> FindWallAreas(Color32[] pixels, int width, int height)
    {
        List<Rect> wallAreas = new List<Rect>();
        
        // Порог интенсивности для определения стены (маска содержит белые области как стены)
        // ИЗМЕНЕНО: Порог должен быть очень низким, т.к. WallSegmentation уже применил свой порог.
        // Здесь мы просто ищем связные области "активных" пикселей.
        byte threshold = 10; 
        
        // Минимальный размер области в пикселях для учета - уменьшаем для обнаружения маленьких областей 
        int minAreaSize = 2; // Уменьшено для обнаружения даже очень маленьких областей 
        
        // Создаем временную матрицу для отслеживания посещенных пикселей
        bool[,] visited = new bool[width, height];
        
        int pixelsAboveThreshold = 0; // Отладочный счетчик
        float sumRedChannel = 0;
        byte maxRedChannel = 0;
        int totalPixelsProcessed = 0;

        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                byte rValue = pixels[y * width + x].r;
                sumRedChannel += rValue;
                if (rValue > maxRedChannel) maxRedChannel = rValue;
                totalPixelsProcessed++;

                if (rValue > threshold)
                {
                    pixelsAboveThreshold++;
                }

                // Если пиксель не посещен и интенсивность > порога (это часть стены)
                if (!visited[x, y] && pixels[y * width + x].r > threshold)
                {
                    // Находим связную область, начиная с этого пикселя
                    Rect area = FindConnectedArea(pixels, width, height, x, y, visited, threshold);
                    
                    // Если область достаточно большая, добавляем ее в список
                    if (area.width * area.height >= minAreaSize)
                    {
                        wallAreas.Add(area);
                    }
                }
            }
        }
        
        Debug.Log($"[ARManagerInitializer2-FindWallAreas] Найдено пикселей выше порога ({threshold}): {pixelsAboveThreshold} из {width * height}. Найдено областей (до фильтрации по размеру >= {minAreaSize}): {wallAreas.Count}");
        return wallAreas;
    }
    
    // Создание плоскости для области стены
    private void CreatePlaneForWallArea(Rect area, int textureWidth, int textureHeight) // Убедимся, что этот метод вызывается
    {
        // Возвращаем оригинальную логику позиционирования и создания плоскости
        if (this.xrOrigin == null || this.xrOrigin.Camera == null)
        {
            Debug.LogError("[ARManagerInitializer2-CreatePlaneForWallArea] ❌ XROrigin или его камера не найдены");
            return;
        }

        Camera arCamera = this.xrOrigin.Camera;

        // Нормализуем координаты центра области из текстуры (0..1)
        float normCenterX = (area.x + area.width / 2.0f) / textureWidth;
        float normCenterY = (area.y + area.height / 2.0f) / textureHeight;

        // Преобразуем нормализованные экранные координаты в точку в мировом пространстве
        // Используем Raycast для определения реального расстояния до стены
        Ray screenRay = arCamera.ViewportPointToRay(new Vector3(normCenterX, normCenterY, 0));
        RaycastHit hit;
        float distanceFromCamera = 2.0f; // Расстояние по умолчанию, если Raycast не удался
        Vector3 planeNormal = -arCamera.transform.forward; // Нормаль по умолчанию

        if (Physics.Raycast(screenRay, out hit, 10.0f))
        {
            distanceFromCamera = hit.distance;
            planeNormal = hit.normal;
            Debug.Log($"[ARManagerInitializer2-CreatePlaneForWallArea] Raycast hit! Distance: {distanceFromCamera:F2}m, Normal: {planeNormal}");
        }
        else
        {
            Debug.LogWarning("[ARManagerInitializer2-CreatePlaneForWallArea] Raycast miss. Using default distance and normal.");
        }

        // Позиция плоскости в мировом пространстве
        // Смещаем немного от точки попадания вдоль нормали, чтобы плоскость была на поверхности
        Vector3 planeWorldPosition = screenRay.GetPoint(distanceFromCamera - 0.01f); 

        // Размеры плоскости в метрах (примерная оценка)
        // Ширина плоскости пропорциональна ширине области на экране
        // Этот расчет можно улучшить, учитывая FOV камеры и расстояние
        float planeViewWidth = area.width / (float)textureWidth; // Ширина области в долях экрана
        float planeWorldWidth = planeViewWidth * distanceFromCamera * Mathf.Tan(arCamera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 2.0f * arCamera.aspect;
        planeWorldWidth = Mathf.Max(planeWorldWidth, this.minPlaneSize); // Учитываем минимальный размер
        float planeWorldHeight = planeWorldWidth; // Делаем плоскость квадратной для простоты

        // Проверка на дубликаты (используем более простой критерий)
        foreach (GameObject existingPlane in this.generatedPlanes)
        {
            if (existingPlane == null) continue;
            if (Vector3.Distance(existingPlane.transform.position, planeWorldPosition) < 0.5f && // Близко
                Vector3.Dot(existingPlane.transform.forward, planeNormal) > 0.9f) // Похожая ориентация
            {
                Debug.LogWarning($"[ARManagerInitializer2-CreatePlaneForWallArea] ⚠️ Обнаружен дубликат плоскости, пропускаем создание. Pos: {planeWorldPosition}, ExistingPos: {existingPlane.transform.position}");
                return;
            }
        }

        // string planeName = "MyARPlane_Debug"; // Старое имя
        string planeName = $"MyARPlane_Debug_{planeInstanceCounter++}"; // Новое уникальное имя
        GameObject planeObject = new GameObject(planeName);
        planeObject.transform.SetParent(null); 
        planeObject.transform.position = planeWorldPosition;
        planeObject.transform.rotation = Quaternion.LookRotation(-planeNormal); // Плоскость смотрит на камеру
        planeObject.transform.localScale = new Vector3(planeWorldWidth, planeWorldHeight, 0.01f); // Тонкая плоскость
        
        Debug.Log($"[ARManagerInitializer2-CreatePlaneForWallArea] Created {planeName}. World Position: {planeObject.transform.position}, Rotation: {planeObject.transform.rotation.eulerAngles}, Scale: {planeObject.transform.localScale}, Parent: {(planeObject.transform.parent == null ? "null (Root)" : planeObject.transform.parent.name)}");

        MeshFilter meshFilter = planeObject.AddComponent<MeshFilter>();
        meshFilter.mesh = CreatePlaneMesh(1, 1); // Используем меш 1x1, масштабируем через transform
        
        MeshRenderer meshRenderer = planeObject.AddComponent<MeshRenderer>();
        // if (this.verticalPlaneMaterial != null) // Временно отключаем использование verticalPlaneMaterial
        // {
        //     meshRenderer.material = new Material(this.verticalPlaneMaterial); // Создаем экземпляр материала
        //     Color color = meshRenderer.material.color;
        //     color.a = 0.7f; // Делаем полупрозрачным для отладки
        //     meshRenderer.material.color = color;
        // }
        // else
        // {
        //     Debug.LogError("[ARManagerInitializer2-CreatePlaneForWallArea] Vertical plane material is not set!");
        // }

        // ВРЕМЕННО: Используем простой Unlit/Color материал для теста
        Material simpleMaterial = new Material(Shader.Find("Unlit/Color"));
        simpleMaterial.color = Color.magenta; // Новый цвет для отладки
        meshRenderer.material = simpleMaterial;
        Debug.Log($"[ARManagerInitializer2-CreatePlaneForWallArea] Applied simple Unlit/Color (magenta) material to {planeName}.");
        Debug.Log($"[ARManagerInitializer2-CreatePlaneForWallArea] Instance ID of created {planeName}: {planeObject.GetInstanceID()}"); // Логируем InstanceID
        
        MeshCollider meshCollider = planeObject.AddComponent<MeshCollider>();
        meshCollider.sharedMesh = meshFilter.mesh;
        
        this.generatedPlanes.Add(planeObject);
        this.planeCreationTimes[planeObject] = Time.time;
    }
    
    // Создание сетки для плоскости с улучшенной геометрией
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
        if (generatedPlanes.Count == 0) // Убрана проверка xrOrigin/Camera, т.к. здесь не используется
            return;
        
        int detachedPlanes = 0;

        // Итерация в обратном порядке для безопасного удаления из списка при необходимости (хотя здесь только чистим null)
        for (int i = generatedPlanes.Count - 1; i >= 0; i--) 
        {
            GameObject plane = generatedPlanes[i];
            if (plane == null)
            {
                generatedPlanes.RemoveAt(i); // Удаляем null ссылки из списка
                continue;
            }
            
            // Проверяем, не прикреплена ли плоскость к камере или другому объекту
            if (plane.transform.parent != null)
            {
                Debug.LogWarning($"[ARManagerInitializer2-UpdatePlanePositions] Плоскость '{plane.name}' (ID: {plane.GetInstanceID()}) была присоединена к '{GetGameObjectPath(plane.transform.parent)}'. Отсоединяем.");
                
                // Отсоединяем плоскость. Аргумент 'true' сохраняет мировые координаты,
                // localScale будет скорректирован для сохранения текущего lossyScale.
                plane.transform.SetParent(null, true); 
                
                detachedPlanes++;
            }
        }
        
        if (detachedPlanes > 0)
        {
            // Сообщение изменено для ясности
            Debug.Log($"[ARManagerInitializer2] Отсоединено {detachedPlanes} плоскостей, которые были некорректно присоединены к родительским объектам.");
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
            return;
        
        // Проверяем, есть ли уже существующая базовая плоскость
        GameObject existingBasicPlane = generatedPlanes.FirstOrDefault(p => p != null && p.name == "BasicWallPlane");
        if (existingBasicPlane != null)
        {
            Debug.Log("[ARManagerInitializer2] ⚠️ Базовая плоскость уже существует. Не создаем новую.");
            return;
        }
        
        // Получаем данные камеры
        Camera arCamera = xrOrigin.Camera;
        Vector3 cameraPosition = arCamera.transform.position;
        Vector3 cameraForward = arCamera.transform.forward;
        Vector3 cameraRight = arCamera.transform.right;
        Vector3 cameraUp = arCamera.transform.up;
        
        // Радикально уменьшаем размеры плоскости
        float planeWidth = 0.5f;  // Сильно уменьшено
        float planeHeight = 0.4f; // Сильно уменьшено
        
        // ОПТИМАЛЬНАЯ СТРАТЕГИЯ: Значительно увеличиваем расстояние размещения плоскости
        // После тестирования видно, что плоскость всё ещё слишком близко
        float distanceFromCamera = 2.5f; // Устанавливаем большое расстояние для гарантированного разделения
        
        // РАСШИРЕННЫЙ МНОГОТОЧЕЧНЫЙ РЕЙКАСТ: Запускаем лучи в 9 разных направлениях для максимального охвата
        List<Vector3> raycastDirections = new List<Vector3> {
            cameraForward,
            cameraForward + cameraRight * 0.25f,
            cameraForward - cameraRight * 0.25f,
            cameraForward + cameraUp * 0.25f,
            cameraForward - cameraUp * 0.25f,
            // Добавляем диагональные направления для лучшего охвата
            cameraForward + cameraRight * 0.2f + cameraUp * 0.2f,
            cameraForward - cameraRight * 0.2f + cameraUp * 0.2f,
            cameraForward + cameraRight * 0.2f - cameraUp * 0.2f,
            cameraForward - cameraRight * 0.2f - cameraUp * 0.2f
        };

        RaycastHit hit = new RaycastHit();
        bool didHit = false;
        float minDistance = float.MaxValue;
        Vector3 bestPosition = cameraPosition + cameraForward * distanceFromCamera;
        Vector3 bestNormal = -cameraForward;
        
        // Используем уже созданные направления для многоточечного рейкаста
        foreach (var direction in raycastDirections)
        {
            if (Physics.Raycast(cameraPosition, direction, out RaycastHit currentHit, 3.5f))
            {
                // Проверяем, находится ли точка попадания в допустимом диапазоне (не слишком близко к камере)
                if (currentHit.distance > 1.8f && currentHit.distance < minDistance)
                {
                    hit = currentHit;
                    minDistance = currentHit.distance;
                    bestPosition = currentHit.point + currentHit.normal * 0.005f; // Минимальное смещение от стены
                    bestNormal = currentHit.normal;
                    didHit = true;
                }
            }
        }
        
        Vector3 planePos;
        
        if (didHit)
        {
            // Если нашли точку попадания, используем оптимизированное положение
            planePos = bestPosition;
            
            // Используем нормаль для лучшей ориентации плоскости
            Debug.Log($"[ARManagerInitializer2] 🎯 Найдено точное расстояние до поверхности: {minDistance:F2}м, нормаль: {bestNormal}");
        }
        else
        {
            // Если не нашли ничего, используем увеличенное расстояние
            planePos = cameraPosition + cameraForward * distanceFromCamera;
            
            // Корректируем положение для лучшей видимости и больших расстояний
            planePos.y -= 0.4f; // Сильнее смещаем вниз
            // Не приближаем к камере, чтобы избежать заполнения экрана
        }
        
        // Ориентируем плоскость с учетом нормали поверхности или к пользователю
        // Выравниваем нормаль по вертикали для более стабильного размещения
        Vector3 orientationNormal = didHit ? bestNormal : cameraForward;
        Vector3 horizontalNormal = new Vector3(orientationNormal.x, 0, orientationNormal.z).normalized;
        Quaternion planeRotation = Quaternion.LookRotation(-horizontalNormal);
        
        // Создаем и настраиваем GameObject для плоскости
        GameObject planeObj = new GameObject("BasicWallPlane");
        planeObj.transform.position = planePos;
        planeObj.transform.rotation = planeRotation;
        // Сильно уменьшаем масштаб плоскости для минимального визуального присутствия
        planeObj.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
        
        // Важно: отсоединяем от родителя, чтобы плоскость не двигалась с камерой
        planeObj.transform.SetParent(null);
        
        // Добавляем компоненты
        MeshFilter meshFilter = planeObj.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = planeObj.AddComponent<MeshRenderer>();
        
        // Создаем меш с улучшенной геометрией
        meshFilter.mesh = CreatePlaneMesh(planeWidth, planeHeight);
        
        // Используем тот же материал, что и для обычных плоскостей, для единообразия
        if (verticalPlaneMaterial != null)
        {
            meshRenderer.material = verticalPlaneMaterial;
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
        MeshCollider meshCollider = planeObj.AddComponent<MeshCollider>();
        meshCollider.sharedMesh = meshFilter.sharedMesh;
        
        // Добавляем в список созданных плоскостей
        generatedPlanes.Add(planeObj);
        
        // Также сохраняем время создания плоскости для защиты от раннего удаления
        planeCreationTimes[planeObj] = Time.time;
        
        Debug.Log("[ARManagerInitializer2] ✅ Создана стабильная базовая плоскость перед пользователем");
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
        if (planeWidthInMeters < minPlaneSize)
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
        
        // ВЫПОЛНЯЕМ СЕРИЮ РЕЙКАСТОВ с разными смещениями и весовыми коэффициентами
        for (int i = 0; i < rayOffsets.Count; i++)
        {
            Vector3 offsetPos = cameraPosition + rayOffsets[i];
            float rayWeight = 1.0f; // Базовый вес
            
            // Центральные лучи имеют больший вес
            if (i == 0) rayWeight = 2.0f; // Центральный луч
            else if (i < 5) rayWeight = 1.5f; // Ближние к центру
            
            if (Physics.Raycast(offsetPos, rayDirection, out hit, 10.0f, layerMask))
            {
                // УЛУЧШЕННАЯ ФИЛЬТРАЦИЯ: Исключаем AR объекты, UI элементы, другие плоскости
                bool isValidHit = true;
                
                // Имя объекта содержит ключевые слова, которые указывают на AR/UI элементы
                if (hit.collider.gameObject.name.Contains("AR") || 
                    hit.collider.gameObject.name.Contains("UI") ||
                    hit.collider.gameObject.name.Contains("XR") ||
                    hit.collider.gameObject.name.Contains("Plane"))
                {
                    isValidHit = false;
                }
                
                // Проверяем слой объекта (можно добавить проверки конкретных слоев)
                // if (hit.collider.gameObject.layer == 8) isValidHit = false; // Пример проверки слоя
                
                if (isValidHit)
                {
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
            Debug.Log($"[ARManagerInitializer2] ⚠️ Плоскость слишком близко к камере ({distanceToCam:F2}м), пропускаем");
            return false;
        }
        
        // УЛУЧШЕННЫЙ АЛГОРИТМ: Интеллектуальное выявление дубликатов плоскостей
        bool tooClose = false;
        int similarOrientationCount = 0;
        GameObject closestExistingPlane = null;
        float closestDuplDistance = float.MaxValue;
        
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
                Debug.Log($"[ARManagerInitializer2] ⚠️ Обнаружен близкий дубликат плоскости на расстоянии {distanceBetweenPlanes:F2}м");
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
                    Debug.Log($"[ARManagerInitializer2] ⚠️ Обнаружена плоскость с похожей ориентацией на расстоянии {distanceBetweenPlanes:F2}м");
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
            Debug.Log($"[ARManagerInitializer2] ⚠️ Уже существует {similarOrientationCount} плоскостей в похожем направлении");
            return false;
        }
        
                 // Улучшенный алгоритм замены плоскостей вместо добавления новых
         if (tooClose && generatedPlanes.Count > 4 && closestExistingPlane != null && closestDuplDistance < 1.0f)
        {
            // Если мы обнаружили дубликат, и у нас уже много плоскостей, 
            // то заменяем существующую плоскость вместо добавления новой
            Debug.Log($"[ARManagerInitializer2] 🔄 Заменяем существующую плоскость вместо создания дубликата");
            generatedPlanes.Remove(closestExistingPlane);
            Destroy(closestExistingPlane);
            tooClose = false; // Разрешаем создание новой плоскости
        }
        else if (tooClose)
        {
            Debug.Log($"[ARManagerInitializer2] ⚠️ Обнаружен дубликат плоскости, пропускаем создание");
            return false;
        }
        
        // ПРОВЕРКА 3: Отсеиваем объекты на экстремальных углах обзора
        // ИЗМЕНЕНО: Слегка увеличиваем допустимый угол обзора для плоскостей
        if (Mathf.Abs(angleH) > 0.45f || Mathf.Abs(angleV) > 0.35f)
        {
            Debug.Log($"[ARManagerInitializer2] ⚠️ Плоскость находится на экстремальном угле обзора, пропускаем");
            return false;
        }
        
        // Создаем и настраиваем GameObject для плоскости
        GameObject planeObj = new GameObject($"WallPlane_{generatedPlanes.Count}");
        
        // Позиционируем плоскость подальше от камеры, чтобы она не накладывалась
        planeObj.transform.position = planePos;
        
        // АДАПТИВНЫЙ РАЗМЕР: Регулируем размер плоскости в зависимости от расстояния до камеры
        // Чем дальше плоскость, тем она должна быть больше
        float distanceScale = Mathf.Clamp(distanceFromCamera / 2.0f, 0.8f, 1.5f);
        
        // УЛУЧШЕННАЯ ОРИЕНТАЦИЯ: Более точное выравнивание плоскостей с учетом найденных поверхностей
        // Переиспользуем ранее созданную переменную planeRotation
        
        if (didHit)
        {
            // Если нашли реальную поверхность через рейкастинг
            // Используем найденную нормаль для ориентации, но с коррекцией для вертикальности
            Vector3 orientNormal = bestNormal;
            
            // Проверяем, является ли поверхность примерно вертикальной
            float verticalDot = Vector3.Dot(orientNormal, Vector3.up);
            bool isApproximatelyVertical = Mathf.Abs(verticalDot) < 0.3f; // Если близко к 0, то вертикальная
            
            if (isApproximatelyVertical)
            {
                // Для вертикальных поверхностей обеспечиваем точную вертикальность
                // Проецируем нормаль на горизонтальную плоскость
                Vector3 horizontalComponent = orientNormal - Vector3.up * verticalDot;
                if (horizontalComponent.magnitude > 0.01f)
                {
                    horizontalComponent.Normalize();
                    orientNormal = horizontalComponent;
                }
            }
            
            planeRotation = Quaternion.LookRotation(-orientNormal);
        }
        else if (planeManager != null && planeManager.trackables.count > 0)
        {
            // Если не нашли через рейкаст, но есть AR плоскости
            ARPlane closestVerticalPlane = null;
            float minDistance = float.MaxValue;
            
            // Ищем ближайшую вертикальную плоскость
            foreach (var plane in planeManager.trackables)
            {
                if (plane == null) continue;
                
                if (plane.alignment == PlaneAlignment.Vertical)
                {
                    float dist = Vector3.Distance(plane.center, planePos);
                    if (dist < minDistance && dist < 2.0f)
                    {
                        minDistance = dist;
                        closestVerticalPlane = plane;
                    }
                }
            }
            
            if (closestVerticalPlane != null)
            {
                // Используем ориентацию ближайшей вертикальной AR плоскости
                planeRotation = Quaternion.LookRotation(-closestVerticalPlane.normal);
            }
            else
            {
                // Используем направление, противоположное лучу, но выравниваем вертикально
                Vector3 adjustedDirection = -rayDirection;
                float upDot = Vector3.Dot(adjustedDirection, Vector3.up);
                
                // Удаляем вертикальный компонент для обеспечения вертикальности плоскости
                if (Mathf.Abs(upDot) > 0.1f)
                {
                    adjustedDirection -= Vector3.up * upDot;
                    adjustedDirection.Normalize();
                }
                
                planeRotation = Quaternion.LookRotation(adjustedDirection);
            }
        }
        else
        {
            // Если никаких ориентиров нет, используем базовое направление
            planeRotation = Quaternion.LookRotation(-rayDirection);
        }
        
        planeObj.transform.rotation = planeRotation;
        
        // Применяем адаптивный масштаб плоскости для более точной интеграции с учетом расстояния
        planeObj.transform.localScale = new Vector3(distanceScale, distanceScale, 1.0f);
        
        // ВАЖНОЕ ИЗМЕНЕНИЕ: Вместо привязки к XR Origin, создаем плоскость в корне сцены
        // Это предотвратит движение плоскостей вместе с камерой
        planeObj.transform.SetParent(null, true);
        
        // Устанавливаем объект в слой "Default" для правильной интеграции в AR сцену
        planeObj.layer = LayerMask.NameToLayer("Default");
        
        // Добавляем компоненты для отображения
        MeshFilter meshFilter = planeObj.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = planeObj.AddComponent<MeshRenderer>();
        
        // Создаем меш для плоскости
        meshFilter.mesh = CreatePlaneMesh(planeWidthInMeters, planeHeightInMeters);
        
        // Применяем материал для стены с настройками для AR
        Material planeMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        
        // Цветовая схема для визуализации в AR - более приглушенная для лучшей интеграции
        // Используем ОДИН СТАБИЛЬНЫЙ ЦВЕТ вместо чередования для более аккуратного вида
        Color planeColor = Color.HSVToRGB(0.1f, 0.6f, 0.7f); // Приглушенный золотистый
        planeMaterial.color = planeColor;
        
        // Правильные настройки рендеринга для AR-плоскостей
        planeMaterial.SetFloat("_Surface", 0); // 0 = непрозрачный
        planeMaterial.SetInt("_ZWrite", 1); // Включаем запись в буфер глубины
        planeMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        planeMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
        planeMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        
        // Включаем эмиссию для лучшей видимости, но с ОЧЕНЬ умеренной интенсивностью
        planeMaterial.EnableKeyword("_EMISSION");
        planeMaterial.SetColor("_EmissionColor", planeColor * 0.2f); // Снижаем интенсивность свечения еще больше
        
        // Умеренные значения для металлик/гладкость для более естественного вида
        planeMaterial.SetFloat("_Smoothness", 0.3f);
        planeMaterial.SetFloat("_Metallic", 0.1f);
        
        // КРИТИЧЕСКИ ВАЖНО: корректная очередь рендеринга для AR-объектов
        // Гарантируем, что материал будет видимым и корректно взаимодействовать с глубиной
        planeMaterial.renderQueue = 2000; // Стандартная очередь для непрозрачных объектов
        
        meshRenderer.material = planeMaterial;
        
        // Добавляем плоскость в список созданных
        generatedPlanes.Add(planeObj);
        
        // Регистрируем время создания плоскости для защиты от быстрого удаления
        planeCreationTimes[planeObj] = Time.time;
        
        Debug.Log($"[ARManagerInitializer2] ✓ Создана AR-плоскость #{generatedPlanes.Count-1} на расстоянии {distanceToCam:F2}м");
        
        return true;
    }

    // Удаляем устаревшие плоскости, если их слишком много
    private void CleanupOldPlanes()
    {
        // Максимальное количество плоскостей
        int maxPlanes = 8;
        
        // Если количество плоскостей не превышает лимит, ничего не делаем
        if (generatedPlanes.Count <= maxPlanes)
        {
            return;
        }
        
        // Очищаем список от null-ссылок
        generatedPlanes.RemoveAll(p => p == null);
        
        // Если после очистки количество не превышает лимит, выходим
        if (generatedPlanes.Count <= maxPlanes)
        {
            return;
        }
        
        // Сортируем плоскости по времени создания (от старых к новым)
        List<GameObject> sortedPlanes = new List<GameObject>(generatedPlanes);
        
        // Сортируем по времени создания
        sortedPlanes.Sort((a, b) => {
            float timeA = planeCreationTimes.ContainsKey(a) ? planeCreationTimes[a] : 0;
            float timeB = planeCreationTimes.ContainsKey(b) ? planeCreationTimes[b] : 0;
            return timeA.CompareTo(timeB); // Сортируем от старых к новым
        });
        
        // Количество плоскостей для удаления (удаляем половину лишних)
        int planesToRemove = Mathf.CeilToInt((generatedPlanes.Count - maxPlanes) / 2f);
        planesToRemove = Mathf.Min(planesToRemove, generatedPlanes.Count - 2); // Оставляем минимум 2 плоскости
        
        Debug.Log($"[ARManagerInitializer2] 🧹 Начинаем очистку плоскостей: {generatedPlanes.Count} → {generatedPlanes.Count - planesToRemove}");
        
        // Проходим по отсортированным плоскостям и удаляем самые старые
        for (int i = 0; i < planesToRemove; i++)
        {
            if (i >= sortedPlanes.Count) break;
            
            GameObject plane = sortedPlanes[i];
            
            // Пропускаем нулевые ссылки
            if (plane == null) continue;
            
            // Удаляем из словаря времени создания
            if (planeCreationTimes.ContainsKey(plane))
            {
                planeCreationTimes.Remove(plane);
            }
            
            // Удаляем из списка плоскостей
            generatedPlanes.Remove(plane);
            
            // Уничтожаем объект
            Destroy(plane);
        }
        
        Debug.Log($"[ARManagerInitializer2] 🧹 Удалено {planesToRemove} старых плоскостей. Осталось {generatedPlanes.Count}");
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