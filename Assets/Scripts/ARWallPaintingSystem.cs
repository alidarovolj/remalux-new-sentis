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
            Shader shader = null;

            // Попробуем найти шейдеры в порядке приоритета
            string[] shaderNames = new string[]
            {
                "Custom/WallPaint",
                "Universal Render Pipeline/Lit",
                "Universal Render Pipeline/Unlit",
                "Unlit/Transparent",
                "Unlit/Color",
                "Sprites/Default",
                "Hidden/InternalErrorShader"
            };

            foreach (string shaderName in shaderNames)
            {
                shader = Shader.Find(shaderName);
                if (shader != null)
                {
                    Debug.Log($"[ARWallPaintingSystem] Используется шейдер: {shaderName}");
                    break;
                }
            }

            if (shader == null)
            {
                Debug.LogError("[ARWallPaintingSystem] Не найден ни один подходящий шейдер! Создание материала невозможно.");
                return;
            }

            try
            {
                defaultWallMaterial = new Material(shader);

                // Настраиваем материал в зависимости от найденного шейдера
                if (shader.name.Contains("Custom/WallPaint"))
                {
                    // Настройки для кастомного шейдера
                    defaultWallMaterial.SetColor("_PaintColor", new Color(1.0f, 0.5f, 0.2f, 0.7f));
                    defaultWallMaterial.SetFloat("_BlendFactor", 0.7f);
                    if (defaultWallMaterial.HasProperty("_UseMask"))
                        defaultWallMaterial.SetFloat("_UseMask", 1.0f);
                }
                else if (shader.name.Contains("Universal Render Pipeline"))
                {
                    // Настройки для URP шейдеров
                    if (defaultWallMaterial.HasProperty("_BaseColor"))
                        defaultWallMaterial.SetColor("_BaseColor", new Color(1.0f, 0.5f, 0.2f, 0.7f));
                    else if (defaultWallMaterial.HasProperty("_Color"))
                        defaultWallMaterial.SetColor("_Color", new Color(1.0f, 0.5f, 0.2f, 0.7f));

                    // Настройки прозрачности для URP
                    if (defaultWallMaterial.HasProperty("_Surface"))
                        defaultWallMaterial.SetFloat("_Surface", 1.0f); // Transparent
                    if (defaultWallMaterial.HasProperty("_SrcBlend"))
                        defaultWallMaterial.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
                    if (defaultWallMaterial.HasProperty("_DstBlend"))
                        defaultWallMaterial.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                    if (defaultWallMaterial.HasProperty("_ZWrite"))
                        defaultWallMaterial.SetFloat("_ZWrite", 0.0f);
                }
                else
                {
                    // Настройки для стандартных шейдеров
                    if (defaultWallMaterial.HasProperty("_Color"))
                        defaultWallMaterial.SetColor("_Color", new Color(1.0f, 0.5f, 0.2f, 0.7f));
                    else
                        defaultWallMaterial.color = new Color(1.0f, 0.5f, 0.2f, 0.7f);
                }

                // Общие настройки
                defaultWallMaterial.renderQueue = 3000; // Transparent queue

                Debug.Log($"[ARWallPaintingSystem] ✅ Материал успешно создан с шейдером: {shader.name}");
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[ARWallPaintingSystem] ❌ Ошибка создания материала: {e.Message}");
                defaultWallMaterial = null;
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