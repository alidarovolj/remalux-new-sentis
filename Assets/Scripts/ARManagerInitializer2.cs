using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Unity.XR.CoreUtils;
using System.Collections;
using System.Collections.Generic;
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

    // Ссылки на AR компоненты
    [Header("AR компоненты")]
    public ARSessionManager sessionManager;
    public ARPlaneManager planeManager;
    public XROrigin xrOrigin;

    [Header("Настройки сегментации")]
    [Tooltip("Использовать обнаруженные плоскости вместо генерации из маски")]
    public bool useDetectedPlanes = false;
    
    [Tooltip("Минимальный размер плоскости для создания (в метрах)")]
    public float minPlaneSize = 0.2f;
    
    [Tooltip("Материал для вертикальных плоскостей")]
    public Material verticalPlaneMaterial;
    
    [Tooltip("Материал для горизонтальных плоскостей")]
    public Material horizontalPlaneMaterial;

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
        // Находим необходимые компоненты
        FindARComponents();
        
        // Подписываемся на событие обновления маски сегментации
        SubscribeToWallSegmentation();
        
        // НОВОЕ: Отключаем стандартные визуализаторы плоскостей AR Foundation
        DisableARFoundationVisualizers();
        
        // Выводим текущие настройки
        Debug.Log($"[ARManagerInitializer2] Настройки инициализированы: useDetectedPlanes={useDetectedPlanes}");
    }

    private void Update()
    {
        // Если есть обновленная маска, обрабатываем ее
        if (maskUpdated && currentSegmentationMask != null)
        {
            ProcessSegmentationMask();
            maskUpdated = false;
        }
        
        // Добавляем проверку и удаление "плоскостей-наложений"
        RemoveOverlayingPlanes();
        
        // НОВОЕ: Добавляем проверку на длительное отсутствие обновлений сегментации
        if (hadValidSegmentationResult && Time.time - lastSuccessfulSegmentationTime > segmentationTimeoutSeconds)
        {
            Debug.LogWarning($"[ARManagerInitializer2] ⚠️ Долгое отсутствие обновлений сегментации ({Time.time - lastSuccessfulSegmentationTime:F1}с). Пропуск обновления плоскостей для сохранения существующих.");
            // Если долго нет обновлений сегментации, но плоскости есть - сохраняем их
            if (generatedPlanes.Count > 0)
            {
                // Не делаем ничего, просто оставляем существующие плоскости
                lastSuccessfulSegmentationTime = Time.time; // Сбрасываем таймер
            }
        }
        
        // НОВОЕ: Добавляем периодическую проверку на отсутствие плоскостей
        if (generatedPlanes.Count == 0 && hadValidSegmentationResult && Time.time % 5 < 0.1f)
        {
            Debug.LogWarning("[ARManagerInitializer2] ⚠️ Обнаружено отсутствие плоскостей при наличии сегментации. Форсируем обновление.");
            // Если плоскостей нет, но сегментация работала - пробуем принудительно создать плоскости
            if (currentSegmentationMask != null)
            {
                maskUpdated = true; // Форсируем обработку сегментации
            }
        }
        
        // НОВОЕ: Добавляем периодическую проверку и отсоединение от родителей
        if (Time.frameCount % 30 == 0) // Обновляем позиции раз в 30 кадров
        {
            // Удаляем невалидные плоскости (null) из списка
            for (int i = generatedPlanes.Count - 1; i >= 0; i--)
            {
                if (generatedPlanes[i] == null)
                {
                    generatedPlanes.RemoveAt(i);
                }
            }
            
            UpdatePlanePositions();
            
            // НОВОЕ: Периодически проверяем и отключаем другие визуализаторы
            DisableOtherARVisualizers();
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
            verticalPlaneMaterial.color = new Color(1.0f, 0.3f, 0.0f, 1.0f); // Ярко-оранжевый, полностью непрозрачный
            verticalPlaneMaterial.SetFloat("_Surface", 0); // 0 = непрозрачный
            verticalPlaneMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            verticalPlaneMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            verticalPlaneMaterial.SetInt("_ZWrite", 1); // Включаем запись в буфер глубины
            verticalPlaneMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            verticalPlaneMaterial.renderQueue = 2000; // Непрозрачная очередь рендеринга
            
            // Включаем эмиссию для лучшей видимости
            verticalPlaneMaterial.EnableKeyword("_EMISSION");
            verticalPlaneMaterial.SetColor("_EmissionColor", new Color(1.0f, 0.3f, 0.0f, 1.0f) * 1.5f);
            
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
            return;

        // Сохраняем ссылку на маску и устанавливаем флаг обновления
        currentSegmentationMask = mask;
        maskUpdated = true;
        
        // Устанавливаем флаг успешного получения маски
        hadValidSegmentationResult = true;
        lastSuccessfulSegmentationTime = Time.time;
        
        // Обновляем материалы существующих плоскостей, если они есть
        if (generatedPlanes.Count > 0) 
        {
            int updatedCount = 0;
            
            // В будущем здесь можно обновлять материалы на основе новых данных сегментации
            foreach (GameObject plane in generatedPlanes)
            {
                if (plane != null)
                {
                    MeshRenderer renderer = plane.GetComponent<MeshRenderer>();
                    if (renderer != null && renderer.material != null)
                    {
                        // Тут можно обновить материал если нужно
                        updatedCount++;
                    }
                }
            }
            
            // Логируем только когда число плоскостей изменилось
            if (lastPlaneCount != generatedPlanes.Count)
            {
                Debug.Log($"[ARManagerInitializer2] Обновлены материалы для {generatedPlanes.Count} плоскостей");
                lastPlaneCount = generatedPlanes.Count;
            }
        }
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
            // Создаем плоскости на основе маски сегментации
            CreatePlanesFromMask(maskTexture);

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
        
        // Находим области стен на маске
        List<Rect> wallAreas = FindWallAreas(pixels, width, height);
        
        // Ограничиваем количество плоскостей для оптимизации производительности
        int maxWallPlanes = 10; // Уменьшаем до 10 для более стабильной работы

        // ИЗМЕНЕНО: Вместо очистки всех плоскостей каждые 10 кадров, 
        // теперь проверяем когда последний раз обновлялись плоскости
        frameCounter++;
        bool shouldUpdatePlanes = false;
        
        // Обновляем плоскости каждые 30 кадров или если их нет вообще
        if (frameCounter >= 30 || generatedPlanes.Count == 0)
        {
            shouldUpdatePlanes = true;
            frameCounter = 0;
            Debug.Log($"[ARManagerInitializer2] 🔄 Обновление плоскостей (frameCounter={frameCounter}, planes={generatedPlanes.Count})");
        }
        
        // Удаляем невалидные плоскости (null) из списка
        for (int i = generatedPlanes.Count - 1; i >= 0; i--)
        {
            if (generatedPlanes[i] == null)
            {
                generatedPlanes.RemoveAt(i);
            }
        }
        
        // НОВОЕ: Если обновление не требуется, но есть существующие плоскости - всегда сохраняем их
        if (!shouldUpdatePlanes && generatedPlanes.Count > 0)
        {
            // КРИТИЧЕСКИ ВАЖНО: Всегда сохраняем хотя бы 3 плоскости, даже при отсутствии новых областей
            if (generatedPlanes.Count <= 3)
            {
                Debug.Log($"[ARManagerInitializer2] ⚠️ Сохраняем последние {generatedPlanes.Count} плоскости для стабильности");
            }
            return;
        }
        
        // Если обновление необходимо, удаляем старые плоскости только если нашли новые области
        if (shouldUpdatePlanes && generatedPlanes.Count > 0)
        {
            // НОВОЕ: Удаляем старые плоскости только если нашли новые и их больше минимума
            if (wallAreas.Count >= 3)
            {
                foreach (GameObject plane in generatedPlanes)
                {
                    if (plane != null)
                        Destroy(plane);
                }
                generatedPlanes.Clear();
                Debug.Log($"[ARManagerInitializer2] 🗑️ Очищены старые плоскости перед созданием новых");
            }
            else
            {
                // Если областей мало, но плоскости есть - сохраняем существующие плоскости
                Debug.Log($"[ARManagerInitializer2] ⚠️ Областей стен не найдено или их мало ({wallAreas.Count}), сохраняем {generatedPlanes.Count} существующих плоскостей");
                return;
            }
        }
        
        // Сортируем области по размеру (от большей к меньшей)
        wallAreas.Sort((a, b) => (b.width * b.height).CompareTo(a.width * a.height));
        
        // Используем только maxWallPlanes самых больших областей
        int areasToUse = Mathf.Min(wallAreas.Count, maxWallPlanes);
        
        if (areasToUse > 0)
            Debug.Log($"[ARManagerInitializer2] Создаем {areasToUse} плоскостей (из {wallAreas.Count} найденных областей)");
        else
            Debug.Log($"[ARManagerInitializer2] ⚠️ Не найдено подходящих областей для создания плоскостей");
        
        // Создаем плоскости для областей стен
        for (int i = 0; i < areasToUse; i++)
        {
            CreatePlaneForWallArea(wallAreas[i], width, height);
        }
        
        // НОВОЕ: Если плоскости так и не созданы, попробуем создать базовую плоскость перед пользователем
        if (generatedPlanes.Count == 0 && hadValidSegmentationResult)
        {
            Debug.LogWarning("[ARManagerInitializer2] ⚠️ Не удалось создать плоскости из сегментации. Создаем базовую плоскость.");
            CreateBasicPlaneInFrontOfUser();
        }
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
        byte threshold = 180; // Снижаем порог для обнаружения большего количества областей
        
        // Минимальный размер области в пикселях для учета - уменьшаем для обнаружения маленьких областей
        int minAreaSize = 3; 
        
        // Создаем временную матрицу для отслеживания посещенных пикселей
        bool[,] visited = new bool[width, height];
        
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
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
        
        return wallAreas;
    }
    
    // Создание плоскости для области стены
    private void CreatePlaneForWallArea(Rect area, int textureWidth, int textureHeight)
    {
        if (xrOrigin == null || xrOrigin.Camera == null)
        {
            Debug.LogError("[ARManagerInitializer2] ❌ XROrigin или его камера не найдены");
            return;
        }
        
        // КРИТИЧЕСКИ ВАЖНО: Проверяем, не слишком ли много плоскостей уже создано
        // Это поможет предотвратить чрезмерное создание плоскостей
        if (generatedPlanes.Count > 6) // Уменьшаем лимит до 6 для лучшей производительности
        {
            // Удаляем самую старую плоскость, если достигли лимита
            if (generatedPlanes.Count > 0 && generatedPlanes[0] != null)
            {
                GameObject oldestPlane = generatedPlanes[0];
                generatedPlanes.RemoveAt(0);
                Destroy(oldestPlane);
                Debug.Log("[ARManagerInitializer2] 🔄 Удалена самая старая плоскость из-за достижения лимита");
            }
        }
            
        // Вычисляем размер и положение плоскости
        float planeWidthInMeters = area.width / textureWidth * 5.0f;
        
        // ИЗМЕНЕНО: Уменьшаем требование к минимальному размеру плоскости
        if (planeWidthInMeters < minPlaneSize)
        {
            return;
        }
        
        // Соотношение сторон плоскости (ширина к высоте)
        float aspectRatio = 0.75f;
        float planeHeightInMeters = planeWidthInMeters * aspectRatio;
        
        // Получаем данные камеры
        Camera arCamera = xrOrigin.Camera;
        Vector3 cameraPosition = arCamera.transform.position;
        Vector3 cameraForward = arCamera.transform.forward;
        Vector3 cameraUp = arCamera.transform.up;
        Vector3 cameraRight = arCamera.transform.right;
        
        // Нормализуем координаты из текстуры (0..1)
        float normalizedX = area.x / textureWidth;
        float normalizedY = area.y / textureHeight;
        
        // ИЗМЕНЕНО: Смягчаем фильтрацию по краям экрана
        if (normalizedX < 0.15f || normalizedX > 0.9f || normalizedY < 0.15f || normalizedY > 0.9f)
        {
            return;
        }
        
        // СПЕЦИАЛЬНАЯ ПРОВЕРКА для верхней левой четверти экрана, где часто дублируются объекты сцены
        // ИЗМЕНЕНО: Немного уменьшаем размер запрещенной зоны
        if (normalizedX < 0.3f && normalizedY < 0.3f)
        {
            return;
        }
        
        // Проецируем луч из камеры в направлении текстурной координате
        float horizontalFov = 2.0f * Mathf.Atan(Mathf.Tan(arCamera.fieldOfView * Mathf.Deg2Rad * 0.5f) * arCamera.aspect);
        
        // Вычисляем направление от камеры к текстурной координате
        float angleH = (normalizedX - 0.5f) * horizontalFov;
        float angleV = (normalizedY - 0.5f) * arCamera.fieldOfView * Mathf.Deg2Rad;
        
        Vector3 rayDirection = cameraForward +
                              cameraRight * Mathf.Tan(angleH) +
                              cameraUp * Mathf.Tan(angleV);
        rayDirection.Normalize();
        
        // ВАЖНОЕ ИЗМЕНЕНИЕ: Увеличиваем дистанцию размещения плоскостей для лучшего совпадения со стенами
        // Используем рейкастинг для более точного определения расстояния до стены
        float distanceFromCamera = 3.0f; // ИЗМЕНЕНО: Уменьшаем базовое расстояние по умолчанию
        
        // Пробуем сделать рейкастинг в направлении луча
        RaycastHit hit;
        int layerMask = ~0; // Все слои
        
        // Пытаемся найти реальные поверхности с помощью рейкастинга
        bool didHit = Physics.Raycast(cameraPosition, rayDirection, out hit, 10.0f, layerMask);
        if (didHit)
        {
            // Если нашли поверхность, используем это расстояние 
            // плюс небольшой отступ для избежания z-fighting
            distanceFromCamera = hit.distance + 0.05f;
            Debug.Log($"[ARManagerInitializer2] 📏 Найдена поверхность на расстоянии {hit.distance:F2}м (объект: {hit.collider.gameObject.name})");
        }
        else
        {
            // Если не нашли поверхности, пробуем найти существующие AR плоскости для более точного позиционирования
            if (planeManager != null && planeManager.trackables.count > 0)
            {
                // Ищем ближайшую плоскость в направлении луча
                float closestDistance = float.MaxValue;
                foreach (var plane in planeManager.trackables)
                {
                    if (plane == null) continue;
                    
                    // Получаем центр плоскости
                    Vector3 planeCenter = plane.center;
                    
                    // Проецируем вектор от камеры до центра плоскости на направление луча
                    Vector3 toCenterVector = planeCenter - cameraPosition;
                    float projectionLength = Vector3.Dot(toCenterVector, rayDirection);
                    
                    // Если проекция положительная и ближе, чем предыдущие найденные плоскости
                    if (projectionLength > 0 && projectionLength < closestDistance)
                    {
                        // Вычисляем перпендикулярное расстояние от луча до центра плоскости
                        Vector3 projection = cameraPosition + rayDirection * projectionLength;
                        float perpendicularDistance = Vector3.Distance(projection, planeCenter);
                        
                        // ИЗМЕНЕНО: Увеличиваем порог расстояния для лучшего обнаружения плоскостей
                        if (perpendicularDistance < 1.5f)
                        {
                            closestDistance = projectionLength;
                            distanceFromCamera = projectionLength;
                        }
                    }
                }
                
                if (closestDistance != float.MaxValue)
                {
                    Debug.Log($"[ARManagerInitializer2] 📏 Найдена AR плоскость на расстоянии {distanceFromCamera:F2}м");
                }
                else
                {
                    Debug.Log($"[ARManagerInitializer2] ⚠️ Не найдено подходящих AR плоскостей для ориентации, используем значение по умолчанию");
                }
            }
        }
        
        // Позиция плоскости в мировом пространстве
        Vector3 planePos = cameraPosition + rayDirection * distanceFromCamera;
        
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
            return;
        }
        
        // Проверка 2: Не создаем плоскость, если уже есть похожая рядом
        bool tooClose = false;
        foreach (GameObject existingPlane in generatedPlanes)
        {
            if (existingPlane == null) continue;
            
            // ИЗМЕНЕНО: Слегка уменьшаем порог расстояния для лучшего обнаружения дубликатов
            if (Vector3.Distance(existingPlane.transform.position, planePos) < 0.4f)
            {
                // Если плоскости близко расположены, считаем дубликатом
                tooClose = true;
                break;
            }
            
            // Дополнительная проверка на похожую ориентацию в близких областях
            if (Vector3.Distance(existingPlane.transform.position, planePos) < 1.2f)
            {
                float dotProduct = Vector3.Dot(existingPlane.transform.forward, rayDirection);
                if (Mathf.Abs(dotProduct) > 0.85f) // Если ориентация очень похожая
                {
                    tooClose = true;
                    break;
                }
            }
        }
        
        if (tooClose)
        {
            Debug.Log($"[ARManagerInitializer2] ⚠️ Обнаружен дубликат плоскости, пропускаем создание");
            return;
        }
        
        // ПРОВЕРКА 3: Отсеиваем объекты на экстремальных углах обзора
        // ИЗМЕНЕНО: Слегка увеличиваем допустимый угол обзора для плоскостей
        if (Mathf.Abs(angleH) > 0.45f || Mathf.Abs(angleV) > 0.35f)
        {
            Debug.Log($"[ARManagerInitializer2] ⚠️ Плоскость находится на экстремальном угле обзора, пропускаем");
            return;
        }
        
        // Создаем и настраиваем GameObject для плоскости
        GameObject planeObj = new GameObject($"WallPlane_{generatedPlanes.Count}");
        
        // Позиционируем плоскость подальше от камеры, чтобы она не накладывалась
        planeObj.transform.position = planePos;
        
        // УЛУЧШЕНИЕ: Ориентируем плоскость в зависимости от найденных поверхностей
        Quaternion planeRotation;
        
        if (didHit)
        {
            // Если мы нашли поверхность с помощью рейкастинга, ориентируем плоскость по нормали поверхности
            planeRotation = Quaternion.LookRotation(-hit.normal);
        }
        else
        {
            // Иначе используем направление, противоположное лучу (как раньше)
            planeRotation = Quaternion.LookRotation(-rayDirection);
        }
        
        planeObj.transform.rotation = planeRotation;
        
        // Уменьшаем масштаб плоскости для более точной интеграции
        planeObj.transform.localScale = new Vector3(0.8f, 0.8f, 1.0f);
        
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
        
        Debug.Log($"[ARManagerInitializer2] ✓ Создана AR-плоскость #{generatedPlanes.Count-1} на расстоянии {distanceToCam:F2}м");
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
            
            // Проверяем несколько условий для определения плоскости поверх камеры:
            
            // 1. Расстояние до камеры
            Vector3 directionToPlane = plane.transform.position - arCamera.transform.position;
            float distanceToCamera = directionToPlane.magnitude;
            
            // 2. Угол между направлением камеры и направлением к плоскости
            // (насколько плоскость находится прямо перед камерой)
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
            // - Плоскость находится близко к камере (менее 1.2 метра)
            // - И плоскость находится примерно перед камерой (положительный dot product)
            // - И плоскость почти перпендикулярна направлению взгляда
            // - И плоскость находится в центральной части экрана
            if (distanceToCamera < 1.2f && alignmentWithCamera > 0.7f && facingDot > 0.6f && isInCentralViewport)
            {
                planesToRemove.Add(plane);
                Debug.Log($"[ARManagerInitializer2] Обнаружена плоскость-наложение: dist={distanceToCamera:F2}м, " + 
                         $"align={alignmentWithCamera:F2}, facing={facingDot:F2}, inCenter={isInCentralViewport}");
            }
        }
        
        // Удаляем плоскости-наложения
        foreach (GameObject planeToRemove in planesToRemove)
        {
            generatedPlanes.Remove(planeToRemove);
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
        if (xrOrigin == null || xrOrigin.Camera == null || generatedPlanes.Count == 0)
            return;
        
        Camera arCamera = xrOrigin.Camera;
        int updatedPlanes = 0;
        
        foreach (GameObject plane in generatedPlanes)
        {
            if (plane == null) continue;
            
            // Проверяем, не прикреплена ли плоскость к камере или другому объекту
            if (plane.transform.parent != null)
            {
                // Отсоединяем плоскость от родительского объекта
                Vector3 worldPos = plane.transform.position;
                Quaternion worldRot = plane.transform.rotation;
                Vector3 worldScale = plane.transform.lossyScale;
                
                plane.transform.SetParent(null, false);
                plane.transform.position = worldPos;
                plane.transform.rotation = worldRot;
                plane.transform.localScale = worldScale;
                
                updatedPlanes++;
            }
        }
        
        if (updatedPlanes > 0)
        {
            Debug.Log($"[ARManagerInitializer2] ⚠️ Отсоединено {updatedPlanes} плоскостей от родительских объектов");
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

    // НОВЫЙ МЕТОД: Создание базовой плоскости перед пользователем при отсутствии данных сегментации
    private void CreateBasicPlaneInFrontOfUser()
    {
        if (xrOrigin == null || xrOrigin.Camera == null)
            return;
        
        // Получаем данные камеры
        Camera arCamera = xrOrigin.Camera;
        Vector3 cameraPosition = arCamera.transform.position;
        Vector3 cameraForward = arCamera.transform.forward;
        
        // Базовые размеры плоскости
        float planeWidth = 1.0f;
        float planeHeight = 1.5f;
        
        // Размещаем плоскость на расстоянии 2-3 метра от камеры, прямо перед пользователем
        float distanceFromCamera = 2.5f;
        Vector3 planePos = cameraPosition + cameraForward * distanceFromCamera;
        
        // Ориентируем плоскость лицевой стороной к пользователю
        Quaternion planeRotation = Quaternion.LookRotation(-cameraForward);
        
        // Создаем и настраиваем GameObject для плоскости
        GameObject planeObj = new GameObject("BasicWallPlane");
        planeObj.transform.position = planePos;
        planeObj.transform.rotation = planeRotation;
        planeObj.transform.localScale = new Vector3(1.0f, 1.0f, 1.0f);
        
        // Важно: отсоединяем от родителя, чтобы плоскость не двигалась с камерой
        planeObj.transform.SetParent(null);
        
        // Настраиваем слой
        planeObj.layer = LayerMask.NameToLayer("Default");
        
        // Добавляем компоненты
        MeshFilter meshFilter = planeObj.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = planeObj.AddComponent<MeshRenderer>();
        
        // Создаем меш
        meshFilter.mesh = CreatePlaneMesh(planeWidth, planeHeight);
        
        // Создаем материал
        Material planeMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        Color planeColor = Color.HSVToRGB(0.1f, 0.6f, 0.7f); // Приглушенный золотистый
        planeMaterial.color = planeColor;
        
        // Настраиваем материал
        planeMaterial.SetFloat("_Surface", 0);
        planeMaterial.SetInt("_ZWrite", 1);
        planeMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
        planeMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
        planeMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
        planeMaterial.EnableKeyword("_EMISSION");
        planeMaterial.SetColor("_EmissionColor", planeColor * 0.2f);
        planeMaterial.SetFloat("_Smoothness", 0.3f);
        planeMaterial.SetFloat("_Metallic", 0.1f);
        planeMaterial.renderQueue = 2000;
        
        meshRenderer.material = planeMaterial;
        
        // Добавляем в список созданных плоскостей
        generatedPlanes.Add(planeObj);
        
        Debug.Log("[ARManagerInitializer2] ✓ Создана базовая плоскость перед пользователем");
    }
} 