using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

[RequireComponent(typeof(Camera))]
public class WallPaintEffect : MonoBehaviour
{
    [SerializeField] private Material wallPaintMaterial;
    [SerializeField] private WallSegmentation wallSegmentation;
    [SerializeField] private Color paintColor = Color.white;
    [SerializeField] private float blendFactor = 0.5f;
    [SerializeField] private bool useMask = true;
    
    private WallPaintFeature wallPaintFeature;
    private bool isInitialized = false;
    
    private void Start()
    {
        if (wallPaintMaterial == null)
        {
            Debug.LogError("Wall Paint Material is not assigned. Disabling component.");
            enabled = false;
            return;
        }

        // Если сегментация не указана, пытаемся найти ее в сцене
        if (wallSegmentation == null)
        {
            wallSegmentation = FindObjectOfType<WallSegmentation>();
            if (wallSegmentation == null)
            {
                Debug.LogWarning("WallSegmentation component not found. Wall masking will not be applied.");
            }
        }
        
        // Находим WallPaintFeature среди всех ScriptableRendererFeature в сцене
        // Это не идеальный метод, но он работает для тестирования
        WallPaintFeature[] features = FindObjectsOfType<WallPaintFeature>(true);
        if (features.Length > 0)
        {
            wallPaintFeature = features[0];
        }
        
        if (wallPaintFeature == null)
        {
            // Альтернативный метод - искать в ScriptableRendererData через UniversalRenderPipelineAsset
            var pipeline = GraphicsSettings.currentRenderPipeline 
                as UniversalRenderPipelineAsset;
            
            if (pipeline != null)
            {
                // Здесь мы можем обратиться к настройкам рендерера, но это требует доступа к внутренним полям
                // Метод FindObjectsOfType выше обычно достаточен для прототипирования
            }
        }
        
        if (wallPaintFeature == null)
        {
            Debug.LogWarning("WallPaintFeature not found on the URP Renderer. Please add it manually in the editor.");
        }
        
        // Применяем начальные настройки
        UpdatePaintParameters();
        isInitialized = true;
    }
    
    private void OnEnable()
    {
        if (isInitialized)
        {
            UpdatePaintParameters();
        }
    }
    
    private void Update()
    {
        // Проверяем наличие маски сегментации стен и обновляем материал
        if (useMask && wallSegmentation != null && wallSegmentation.SegmentationMaskTexture != null)
        {
            if (wallPaintMaterial != null)
            {
                wallPaintMaterial.SetTexture("_SegmentationMask", wallSegmentation.SegmentationMaskTexture);
            }
        }
    }
    
    private void UpdatePaintParameters()
    {
        if (wallPaintMaterial != null)
        {
            // Применяем текущие настройки к материалу
            wallPaintMaterial.SetColor("_PaintColor", paintColor);
            wallPaintMaterial.SetFloat("_BlendFactor", blendFactor);
            
            // Применяем или отключаем маску
            if (wallSegmentation != null && wallSegmentation.IsInitialized && useMask)
            {
                wallPaintMaterial.SetTexture("_SegmentationMask", wallSegmentation.SegmentationMaskTexture);
                wallPaintMaterial.EnableKeyword("USE_MASK");
            }
            else
            {
                wallPaintMaterial.DisableKeyword("USE_MASK");
            }
        }

        if (wallPaintFeature != null && wallPaintFeature.passMaterial != wallPaintMaterial) 
        {
           // Обновляем материал в фиче
           wallPaintFeature.SetPassMaterial(wallPaintMaterial);
        }
    }
    
    public void SetPaintColor(Color color)
    {
        paintColor = color;
        UpdatePaintParameters();
    }
    
    public Color GetPaintColor()
    {
        return paintColor;
    }
    
    public void SetBlendFactor(float factor)
    {
        blendFactor = Mathf.Clamp01(factor);
        UpdatePaintParameters();
    }
    
    public float GetBlendFactor()
    {
        return blendFactor;
    }
    
    public void SetUseMask(bool useMaskValue)
    {
        useMask = useMaskValue;
        UpdatePaintParameters();
    }
    
    // Метод для получения материала из WallPaintFeature
    public Material GetMaterial()
    {
        if (wallPaintMaterial == null)
        {
            Debug.LogWarning("Wall Paint Material is null in WallPaintEffect");
        }
        return wallPaintMaterial;
    }
} 