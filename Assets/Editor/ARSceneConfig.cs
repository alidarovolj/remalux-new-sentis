using UnityEngine;

/// <summary>
/// ScriptableObject для хранения конфигурации AR сцены
/// </summary>
[CreateAssetMenu(fileName = "ARSceneConfig", menuName = "Remalux/AR Scene Configuration")]
public class ARSceneConfig : ScriptableObject
{
    [Tooltip("Префаб AR Session")]
    public GameObject arSessionPrefab;

    [Tooltip("Префаб XR Origin")]
    public GameObject xrOriginPrefab;

    [Tooltip("Префаб плоскости AR")]
    public GameObject arPlanePrefab;

    [Tooltip("Префаб пользовательского интерфейса")]
    public GameObject uiPrefab;

    [Tooltip("Модель для сегментации стен")]
    public UnityEngine.Object wallSegmentationModel;

    [Tooltip("Материал для перекраски стен")]
    public Material wallPaintMaterial;

    [Tooltip("Разрешение маски сегментации")]
    public Vector2Int segmentationMaskResolution = new Vector2Int(256, 256);

    [Tooltip("Цвет перекраски по умолчанию")]
    public Color defaultPaintColor = Color.red;

    [Tooltip("Коэффициент смешивания по умолчанию")]
    [Range(0f, 1f)]
    public float defaultBlendFactor = 0.7f;

    [Header("Настройки симуляции AR")]
    [Tooltip("Включить симуляцию AR в редакторе")]
    public bool enableARSimulation = true;

    [Tooltip("Префаб среды симуляции AR")]
    public GameObject simulationEnvironmentPrefab;

    [Tooltip("Показывать тестовый объект для проверки рендеринга")]
    public bool showTestObject = true;

    [Header("Настройки камеры AR")]
    [Tooltip("Материал для фона AR камеры (опционально)")]
    public Material arCameraBackgroundMaterial;

    [Tooltip("Использовать пользовательский материал для фона AR камеры")]
    public bool useCustomARCameraBackground = false;
}