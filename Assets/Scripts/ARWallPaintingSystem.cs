using UnityEngine;
using UnityEngine.XR.ARFoundation;
using System.Collections.Generic;

/// <summary>
/// Основной компонент системы покраски стен в AR, интегрирующий все подсистемы
/// </summary>
public class ARWallPaintingSystem : MonoBehaviour
{
    [Header("AR компоненты")]
    [SerializeField] private ARPlaneManager planeManager;
    [SerializeField] private ARCameraManager cameraManager;
    [SerializeField] private WallSegmentation wallSegmentation;
    [SerializeField] private ARManagerInitializer2 arManagerInitializer;
    
    [Header("Настройки системы")]
    [SerializeField] private bool autoCreateColorPickerUI = true;
    [SerializeField] private Material defaultWallMaterial;
    
    private List<GameObject> wallObjects = new List<GameObject>();
    private CreateColorPickerUI colorPickerUICreator;
    
    private void Awake()
    {
        // Находим необходимые компоненты, если не заданы
        FindRequiredComponents();
        
        // Проверяем и создаем материал по умолчанию
        CreateDefaultMaterial();
    }
    
    private void Start()
    {
        // Создаем UI для выбора цветов
        if (autoCreateColorPickerUI)
        {
            CreateColorPickerUI();
        }
        
        // Подписываемся на события обнаружения стен
        SubscribeToEvents();
        
        Debug.Log("[ARWallPaintingSystem] ✅ Система покраски стен AR инициализирована");
    }
    
    /// <summary>
    /// Находит необходимые компоненты в сцене
    /// </summary>
    private void FindRequiredComponents()
    {
        if (planeManager == null)
        {
            planeManager = FindObjectOfType<ARPlaneManager>();
        }
        
        if (cameraManager == null)
        {
            cameraManager = FindObjectOfType<ARCameraManager>();
        }
        
        if (wallSegmentation == null)
        {
            wallSegmentation = FindObjectOfType<WallSegmentation>();
        }
        
        if (arManagerInitializer == null)
        {
            arManagerInitializer = FindObjectOfType<ARManagerInitializer2>();
        }
    }
    
    /// <summary>
    /// Создает материал стены по умолчанию
    /// </summary>
    private void CreateDefaultMaterial()
    {
        if (defaultWallMaterial == null)
        {
            defaultWallMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            if (defaultWallMaterial != null)
            {
                defaultWallMaterial.color = new Color(1.0f, 0.5f, 0.2f, 0.5f);
                defaultWallMaterial.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                defaultWallMaterial.renderQueue = 3000; // Прозрачный
                
                // Настраиваем свойства материала для блендинга
                defaultWallMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                defaultWallMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                defaultWallMaterial.SetFloat("_ZWrite", 0.0f);
                defaultWallMaterial.SetFloat("_Surface", 1.0f); // Прозрачность
            }
        }
    }
    
    /// <summary>
    /// Создает UI для выбора цветов
    /// </summary>
    private void CreateColorPickerUI()
    {
        GameObject uiCreator = new GameObject("ColorPickerUICreator");
        colorPickerUICreator = uiCreator.AddComponent<CreateColorPickerUI>();
    }
    
    /// <summary>
    /// Подписывается на события обнаружения и сегментации стен
    /// </summary>
    private void SubscribeToEvents()
    {
        // Подписываемся на события сегментации стен
        if (wallSegmentation != null)
        {
            wallSegmentation.OnSegmentationMaskUpdated += OnSegmentationMaskUpdated;
        }
        
        // Подписываемся на события создания плоскостей
        if (arManagerInitializer != null)
        {
            // Метод замещаем через reflection, так как метод может быть приватным
            RegisterARManagerCallback();
        }
    }
    
    /// <summary>
    /// Регистрирует обратный вызов для ARManagerInitializer2 через reflection
    /// </summary>
    private void RegisterARManagerCallback()
    {
        // Пытаемся найти событие или добавить делегат через reflection
        System.Type type = arManagerInitializer.GetType();
        
        // Ищем метод по имени
        System.Reflection.MethodInfo method = type.GetMethod("CreatePlaneForWallArea", 
            System.Reflection.BindingFlags.NonPublic | 
            System.Reflection.BindingFlags.Instance);
        
        if (method != null)
        {
            Debug.Log("[ARWallPaintingSystem] Найден метод CreatePlaneForWallArea в ARManagerInitializer2");
            
            // Здесь мы не можем напрямую подписаться на приватный метод, 
            // но можем модифицировать настройки ARManagerInitializer2
            
            // Устанавливаем материал в ARManagerInitializer если у него есть соответствующее поле
            System.Reflection.FieldInfo materialField = type.GetField("verticalPlaneMaterial", 
                System.Reflection.BindingFlags.NonPublic | 
                System.Reflection.BindingFlags.Instance);
            
            if (materialField != null && defaultWallMaterial != null)
            {
                materialField.SetValue(arManagerInitializer, defaultWallMaterial);
                Debug.Log("[ARWallPaintingSystem] Установлен материал стен для ARManagerInitializer2");
            }
        }
    }
    
    /// <summary>
    /// Обработчик события обновления маски сегментации
    /// </summary>
    private void OnSegmentationMaskUpdated(RenderTexture segmentationMask)
    {
        // Этот метод будет вызываться при обновлении маски сегментации
        Debug.Log("[ARWallPaintingSystem] Получена обновленная маска сегментации стен");
        
        // ARManagerInitializer2 должен уже обрабатывать эту маску и создавать плоскости
        // Мы можем добавить дополнительную логику если нужно
    }
    
    /// <summary>
    /// Добавляет созданную стену в список
    /// </summary>
    public void RegisterWallObject(GameObject wallObject)
    {
        if (wallObject != null && !wallObjects.Contains(wallObject))
        {
            wallObjects.Add(wallObject);
            
            // Проверяем наличие компонента ColorPickTarget, добавляем если нет
            ColorPickTarget target = wallObject.GetComponent<ColorPickTarget>();
            if (target == null)
            {
                target = wallObject.AddComponent<ColorPickTarget>();
            }
        }
    }
} 