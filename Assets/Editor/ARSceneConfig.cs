using UnityEngine;

/// <summary>
/// ScriptableObject для хранения конфигурации AR сцены
/// </summary>
[CreateAssetMenu(fileName = "ARSceneConfig", menuName = "Remalux/AR Scene Configuration")]
public class ARSceneConfig : ScriptableObject
{
    [Header("AR Компоненты")]
    [Tooltip("Префаб AR Session")]
    public GameObject arSessionPrefab;
    
    [Tooltip("Префаб XR Origin")]
    public GameObject xrOriginPrefab;
    
    [Tooltip("Префаб для визуализации AR плоскостей")]
    public GameObject arPlanePrefab;
    
    [Header("Настройки сегментации стен")]
    [Tooltip("Модель ONNX для сегментации стен")]
    public Object wallSegmentationModel;
    
    [Tooltip("Разрешение текстуры маски сегментации")]
    public Vector2Int segmentationMaskResolution = new Vector2Int(256, 256);
    
    [Header("Настройки перекраски")]
    [Tooltip("Материал для шейдера перекраски стен")]
    public Material wallPaintMaterial;
    
    [Tooltip("Цвет перекраски по умолчанию")]
    public Color defaultPaintColor = Color.red;
    
    [Tooltip("Коэффициент смешивания по умолчанию")]
    [Range(0f, 1f)]
    public float defaultBlendFactor = 0.7f;
    
    [Header("UI Настройки")]
    [Tooltip("Префаб UI элементов")]
    public GameObject uiPrefab;
} 