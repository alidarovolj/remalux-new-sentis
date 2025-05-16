using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Unity.XR.CoreUtils;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.XR.ARSubsystems;

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
        else
        {
            Debug.LogWarning("[ARManagerInitializer2] ARPlaneManager не найден в сцене!");
        }
        
        // Настраиваем материалы по умолчанию, если не заданы
        if (verticalPlaneMaterial == null)
        {
            verticalPlaneMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            verticalPlaneMaterial.color = new Color(0.3f, 0.5f, 1.0f, 0.8f); // Увеличиваем непрозрачность для стен
            verticalPlaneMaterial.SetFloat("_Surface", 0); // 0 = непрозрачный
            verticalPlaneMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            verticalPlaneMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            verticalPlaneMaterial.SetInt("_ZWrite", 1); // Включаем запись в буфер глубины
            verticalPlaneMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            verticalPlaneMaterial.renderQueue = 2000; // Непрозрачная очередь рендеринга
        }
        
        if (horizontalPlaneMaterial == null)
        {
            horizontalPlaneMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            horizontalPlaneMaterial.color = new Color(0.3f, 1.0f, 0.5f, 0.8f); // Увеличиваем непрозрачность для пола
            horizontalPlaneMaterial.SetFloat("_Surface", 0); // 0 = непрозрачный
            horizontalPlaneMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.One);
            horizontalPlaneMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.Zero);
            horizontalPlaneMaterial.SetInt("_ZWrite", 1); // Включаем запись в буфер глубины
            horizontalPlaneMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
            horizontalPlaneMaterial.renderQueue = 2000; // Непрозрачная очередь рендеринга
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
    
    // Обработчик события обновления маски сегментации
    private void OnSegmentationMaskUpdated(RenderTexture mask)
    {
        if (mask == null)
        {
            Debug.LogWarning("[ARManagerInitializer2] ⚠️ Получена пустая маска сегментации");
            return;
        }
        
        currentSegmentationMask = mask;
        maskUpdated = true;
        
        Debug.Log($"[ARManagerInitializer2] ✅ Получена новая маска сегментации {mask.width}x{mask.height}. UseDetectedPlanes={useDetectedPlanes}");
        
        // Обновляем материалы для существующих вертикальных плоскостей
        if (planeManager != null)
        {
            int updatedCount = 0;
            
            foreach (ARPlane plane in planeManager.trackables)
            {
                if (plane.alignment == PlaneAlignment.Vertical)
                {
                    MeshRenderer renderer = plane.GetComponent<MeshRenderer>();
                    if (renderer != null && renderer.material != null)
                    {
                        renderer.material.SetTexture("_SegmentationMask", mask);
                        renderer.material.EnableKeyword("USE_MASK");
                        updatedCount++;
                    }
                }
            }
            
            Debug.Log($"[ARManagerInitializer2] Обновлены материалы для {updatedCount} вертикальных плоскостей");
        }
    }
    
    // Обработка маски сегментации для генерации плоскостей
    private void ProcessSegmentationMask()
    {
        if (useDetectedPlanes)
        {
            Debug.Log("[ARManagerInitializer2] ⚠️ Создание плоскостей из маски отключено (useDetectedPlanes=true)");
            return;
        }
        
        if (currentSegmentationMask == null)
        {
            Debug.LogWarning("[ARManagerInitializer2] ⚠️ Маска сегментации не определена");
            return;
        }
            
        Debug.Log($"[ARManagerInitializer2] Анализируем маску сегментации для создания плоскостей. Размер маски: {currentSegmentationMask.width}x{currentSegmentationMask.height}");
        
        // Проверяем наличие необходимых компонентов
        if (xrOrigin == null) 
        {
            Debug.LogError("[ARManagerInitializer2] ❌ XROrigin не назначен! Невозможно создать плоскости.");
            return;
        }
        
        if (xrOrigin.Camera == null) 
        {
            Debug.LogError("[ARManagerInitializer2] ❌ Камера XROrigin не найдена! Невозможно создать плоскости.");
            return;
        }
            
        // Анализируем маску сегментации и создаем плоскости
        // Это упрощенная реализация - для реального проекта нужно использовать
        // более сложные алгоритмы выделения связных областей на маске
        
        // Получаем данные из текстуры
        Texture2D segmentationTexture = RenderTextureToTexture2D(currentSegmentationMask);
        
        if (segmentationTexture != null)
        {
            Debug.Log($"[ARManagerInitializer2] ✅ Успешно создана Texture2D из RenderTexture: {segmentationTexture.width}x{segmentationTexture.height}");
            
            // Находим области стен на маске и создаем для них плоскости
            CreatePlanesFromMask(segmentationTexture);
            
            // Уничтожаем временную текстуру
            Destroy(segmentationTexture);
        }
        else
        {
            Debug.LogError("[ARManagerInitializer2] ❌ Не удалось создать Texture2D из маски сегментации");
        }
    }
    
    // Преобразование RenderTexture в Texture2D
    private Texture2D RenderTextureToTexture2D(RenderTexture renderTexture)
    {
        if (renderTexture == null)
            return null;
            
        Texture2D texture = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGBA32, false);
        RenderTexture prevRT = RenderTexture.active;
        RenderTexture.active = renderTexture;
        
        texture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
        texture.Apply();
        
        RenderTexture.active = prevRT;
        return texture;
    }
    
    // Создание плоскостей на основе маски сегментации
    private void CreatePlanesFromMask(Texture2D maskTexture)
    {
        if (maskTexture == null || planeManager == null || xrOrigin == null)
            return;
            
        // Считаем пиксели стен (красные в маске)
        Color32[] pixels = maskTexture.GetPixels32();
        int width = maskTexture.width;
        int height = maskTexture.height;
        
        // Найдем области стен, используя простой алгоритм
        List<Rect> wallAreas = FindWallAreas(pixels, width, height);
        
        // Создаем плоскости для каждой области стены
        foreach (Rect area in wallAreas)
        {
            // Преобразуем координаты из пространства текстуры в мировые координаты
            CreatePlaneForWallArea(area, width, height);
        }
    }
    
    // Поиск областей стен на маске
    private List<Rect> FindWallAreas(Color32[] pixels, int width, int height)
    {
        List<Rect> wallAreas = new List<Rect>();
        
        // Добавляем отладочную информацию
        Debug.Log($"[ARManagerInitializer2] Анализируемый массив пикселей: {pixels.Length}, ширина={width}, высота={height}");
        
        // Считаем количество пикселей стен для отладки
        int wallPixelCount = 0;
        for (int i = 0; i < pixels.Length; i++) {
            if (pixels[i].r > 200) wallPixelCount++;
        }
        Debug.Log($"[ARManagerInitializer2] Найдено {wallPixelCount} пикселей стен в маске (красный канал > 200)");
        
        // Очень простой алгоритм для демонстрации - 
        // для реального проекта нужен более сложный алгоритм поиска связных областей
        
        int minWallWidth = Mathf.Max(3, width / 20);  // Минимальная ширина стены в пикселях
        int wallCount = 0;
        
        // Проходим по каждой строке изображения
        for (int y = 0; y < height; y++)
        {
            int startX = -1;
            int currentWidth = 0;
            
            for (int x = 0; x < width; x++)
            {
                int index = y * width + x;
                if (index < pixels.Length)
                {
                    // Проверяем, является ли пиксель частью стены (красный канал > 200)
                    bool isWallPixel = pixels[index].r > 200;
                    
                    if (isWallPixel)
                    {
                        if (startX < 0)
                            startX = x;
                            
                        currentWidth++;
                    }
                    else if (startX >= 0)
                    {
                        // Если ширина области достаточно большая, добавляем ее
                        if (currentWidth >= minWallWidth)
                        {
                            wallAreas.Add(new Rect(startX, y, currentWidth, 1));
                            wallCount++;
                        }
                        
                        startX = -1;
                        currentWidth = 0;
                    }
                }
            }
            
            // Проверяем последнюю область в строке
            if (startX >= 0 && currentWidth >= minWallWidth)
            {
                wallAreas.Add(new Rect(startX, y, currentWidth, 1));
                wallCount++;
            }
        }
        
        Debug.Log($"[ARManagerInitializer2] Найдено {wallCount} областей стен на маске, minWallWidth={minWallWidth} пикселей");
        return wallAreas;
    }
    
    // Создание плоскости для области стены
    private void CreatePlaneForWallArea(Rect area, int textureWidth, int textureHeight)
    {
        Debug.Log($"[ARManagerInitializer2] Создаем плоскость для области: x={area.x}, y={area.y}, width={area.width}, height={area.height}");
        
        if (xrOrigin == null || xrOrigin.Camera == null)
        {
            Debug.LogError("[ARManagerInitializer2] ❌ XROrigin или его камера не найдены");
            return;
        }
            
        // Вычисляем размер и положение плоскости
        float planeWidthInMeters = area.width / textureWidth * 4.0f;  // Увеличиваем до 4 метров (было 3.0f)
        
        // Если размер плоскости слишком мал, пропускаем
        if (planeWidthInMeters < minPlaneSize)
        {
            Debug.Log($"[ARManagerInitializer2] ⚠️ Плоскость слишком маленькая: {planeWidthInMeters:F2}м < {minPlaneSize}м, пропускаем");
            return;
        }
            
        // Вычисляем положение в координатах текстуры (0-1)
        float normalizedX = (area.x + area.width / 2) / textureWidth;
        float normalizedY = (area.y + area.height / 2) / textureHeight;
        
        // Преобразуем в мировые координаты
        // Нормализованные координаты от -1 до 1, где (0,0) - центр экрана
        float viewX = (normalizedX * 2 - 1);
        float viewY = (normalizedY * 2 - 1);
        
        Debug.Log($"[ARManagerInitializer2] Координаты плоскости: normalizedX={normalizedX:F2}, normalizedY={normalizedY:F2}, viewX={viewX:F2}, viewY={viewY:F2}");
        
        // Создаем плоскость на определенном расстоянии от камеры
        float distanceFromCamera = 1.5f;  // Уменьшено с 2.0м до 1.5м для более близкого расположения
        
        // Преобразуем координаты экрана в мировые
        Vector3 cameraPos = xrOrigin.Camera.transform.position;
        Vector3 cameraForward = xrOrigin.Camera.transform.forward;
        Vector3 cameraRight = xrOrigin.Camera.transform.right;
        Vector3 cameraUp = xrOrigin.Camera.transform.up;
        
        // Вычисляем позицию плоскости
        Vector3 planePos = cameraPos + cameraForward * distanceFromCamera;
        planePos += cameraRight * viewX * distanceFromCamera * 0.7f; // Увеличиваем множитель для лучшего разделения
        planePos += cameraUp * viewY * distanceFromCamera * 0.7f;    // Увеличиваем множитель для лучшего разделения
        
        Debug.Log($"[ARManagerInitializer2] Позиция плоскости: {planePos}, размер: {planeWidthInMeters:F2}x{planeWidthInMeters * 0.75f:F2}м");
        
        // Создаем и настраиваем GameObject для плоскости
        GameObject planeObj = new GameObject($"WallPlane_{generatedPlanes.Count}");
        planeObj.transform.position = planePos;
        planeObj.transform.rotation = Quaternion.LookRotation(-cameraForward);  // Плоскость смотрит на камеру
        planeObj.transform.localScale = new Vector3(1.2f, 1.2f, 1.2f); // Увеличиваем масштаб на 20%
        
        // Делаем плоскость дочерним объектом этого компонента
        planeObj.transform.SetParent(transform);
        
        // Добавляем в список для отслеживания
        generatedPlanes.Add(planeObj);
        
        // Добавляем компоненты для визуализации
        MeshFilter meshFilter = planeObj.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = planeObj.AddComponent<MeshRenderer>();
        
        // Создаем меш для плоскости
        meshFilter.mesh = CreatePlaneMesh(planeWidthInMeters, planeWidthInMeters * 0.75f);  // Соотношение сторон 4:3
        
        // Применяем материал для стены
        if (verticalPlaneMaterial != null)
        {
            // Создаем уникальный экземпляр материала
            Material planeMaterial = new Material(verticalPlaneMaterial);
            // Явно задаем для каждой плоскости непрозрачный режим рендеринга
            planeMaterial.SetFloat("_Surface", 0); // 0 = непрозрачный
            planeMaterial.color = new Color(0.2f, 0.4f, 1.0f, 1.0f); // Ярко-синий, полностью непрозрачный
            
            // Включаем эмиссию для лучшей видимости (свечение)
            planeMaterial.EnableKeyword("_EMISSION");
            planeMaterial.SetColor("_EmissionColor", new Color(0.2f, 0.4f, 1.0f, 1.0f) * 0.5f);
            
            meshRenderer.material = planeMaterial;
            Debug.Log($"[ARManagerInitializer2] ✅ Применен материал {verticalPlaneMaterial.name} к плоскости {planeObj.name}");
        }
        else
        {
            Debug.LogWarning($"[ARManagerInitializer2] ⚠️ verticalPlaneMaterial не задан, используется стандартный материал");
        }
        
        // Выводим отладочную информацию
        Debug.Log($"[ARManagerInitializer2] ✅ Создана плоскость для стены: {planeObj.name} размером {planeWidthInMeters:F2}x{planeWidthInMeters * 0.75f:F2}м");
    }
    
    // Создание сетки для плоскости
    private Mesh CreatePlaneMesh(float width, float height)
    {
        Mesh mesh = new Mesh();
        
        // Четыре вершины для прямоугольника
        Vector3[] vertices = new Vector3[4]
        {
            new Vector3(-width/2, -height/2, 0),
            new Vector3(width/2, -height/2, 0),
            new Vector3(-width/2, height/2, 0),
            new Vector3(width/2, height/2, 0)
        };
        
        // Индексы для двух треугольников
        int[] triangles = new int[6]
        {
            0, 2, 1,
            2, 3, 1
        };
        
        // UV-координаты для текстурирования
        Vector2[] uv = new Vector2[4]
        {
            new Vector2(0, 0),
            new Vector2(1, 0),
            new Vector2(0, 1),
            new Vector2(1, 1)
        };
        
        // Назначаем данные сетке
        mesh.vertices = vertices;
        mesh.triangles = triangles;
        mesh.uv = uv;
        
        // Вычисляем нормали и границы
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        
        return mesh;
    }
} 