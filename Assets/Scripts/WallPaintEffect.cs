using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Reflection;
using System.Collections.Generic;

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
    private bool isSegmentationReady = false;
    
    private void Start()
    {
        if (wallPaintMaterial == null)
        {
            // Попробуем найти материал в папке Materials
            wallPaintMaterial = Resources.Load<Material>("Materials/WallPaint");
            
            if (wallPaintMaterial == null)
            {
                Debug.LogError("Wall Paint Material is not assigned and not found in Resources. Disabling component.");
                enabled = false;
                return;
            }
        }
        
        // Создаем копию материала, чтобы не изменять общий шаблон
        wallPaintMaterial = new Material(wallPaintMaterial);

        // Если сегментация не указана, пытаемся найти ее в сцене
        if (wallSegmentation == null)
        {
            wallSegmentation = FindObjectOfType<WallSegmentation>();
            if (wallSegmentation == null)
            {
                Debug.LogWarning("WallSegmentation component not found. Wall masking will not be applied.");
            }
        }
        
        // Находим WallPaintFeature в URP Renderer 
        FindAndSetupWallPaintFeature();
        
        // Применяем начальные настройки
        UpdatePaintParameters();
        isInitialized = true;
    }
    
    // Метод для поиска WallPaintFeature в URP Renderer
    private void FindAndSetupWallPaintFeature()
    {
        // Сначала проверяем текущий активный рендер пайплайн
        var urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urpAsset == null)
        {
            Debug.LogError("URP is not active. WallPaintFeature requires URP to work properly.");
            return;
        }
        
        // Получаем рендереры через рефлексию (поскольку renderers - приватное поле)
        var rendererDataField = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList", 
            BindingFlags.NonPublic | BindingFlags.Instance);
            
        if (rendererDataField == null)
        {
            // Пробуем альтернативное имя поля (может различаться в разных версиях Unity)
            rendererDataField = typeof(UniversalRenderPipelineAsset).GetField("m_RendererData", 
                BindingFlags.NonPublic | BindingFlags.Instance);
                
            if (rendererDataField == null)
            {
                Debug.LogError("Could not access URP Renderer data through reflection.");
                FindWallPaintFeatureFallback();
                return;
            }
        }
        
        var rendererDataList = rendererDataField.GetValue(urpAsset) as IList<ScriptableRendererData>;
        if (rendererDataList == null || rendererDataList.Count == 0)
        {
            // Пробуем получить как единичный объект, а не список
            var singleRendererData = rendererDataField.GetValue(urpAsset) as ScriptableRendererData;
            if (singleRendererData != null)
            {
                CheckRendererDataForFeature(singleRendererData);
            }
            else
            {
                Debug.LogError("No URP Renderer data found");
                FindWallPaintFeatureFallback();
            }
            return;
        }
        
        // Проверяем все доступные рендереры на наличие WallPaintFeature
        foreach (var rendererData in rendererDataList)
        {
            if (rendererData == null) continue;
            CheckRendererDataForFeature(rendererData);
            if (wallPaintFeature != null) break;
        }
        
        // Если не удалось найти через рефлексию, используем запасной метод
        if (wallPaintFeature == null)
        {
            FindWallPaintFeatureFallback();
        }
    }
    
    // Проверяем конкретный ScriptableRendererData на наличие WallPaintFeature
    private void CheckRendererDataForFeature(ScriptableRendererData rendererData)
    {
        if (rendererData == null) return;
        
        // Получаем список features через рефлексию
        var featuresField = typeof(ScriptableRendererData).GetField("m_RendererFeatures", 
            BindingFlags.NonPublic | BindingFlags.Instance);
            
        if (featuresField == null)
        {
            Debug.LogWarning("Could not access renderer features field through reflection.");
            return;
        }
        
        var features = featuresField.GetValue(rendererData) as List<ScriptableRendererFeature>;
        if (features == null) return;
        
        // Ищем WallPaintFeature в списке
        foreach (var feature in features)
        {
            if (feature is WallPaintFeature wallPaintFeatureFound)
            {
                wallPaintFeature = wallPaintFeatureFound;
                
                // Проверяем, есть ли уже материал в фиче
                if (wallPaintFeature.passMaterial == null)
                {
                    wallPaintFeature.SetPassMaterial(wallPaintMaterial);
                }
                
                Debug.Log("WallPaintFeature found in URP Renderer and connected successfully");
                return;
            }
        }
    }
    
    // Запасной метод поиска WallPaintFeature в случае, если рефлексия не сработает
    private void FindWallPaintFeatureFallback()
    {
        WallPaintFeature[] features = FindObjectsOfType<WallPaintFeature>(true);
        if (features.Length > 0)
        {
            wallPaintFeature = features[0];
                
            // Проверяем, есть ли уже материал в фиче
            if (wallPaintFeature.passMaterial == null)
            {
                wallPaintFeature.SetPassMaterial(wallPaintMaterial);
            }
            Debug.Log("WallPaintFeature found using fallback method");
        }
        else
        {
            Debug.LogWarning("WallPaintFeature not found on the URP Renderer. Please add it manually in the editor.");
        }
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
        // Если фича не была найдена при старте, попробуем найти ее сейчас
        if (wallPaintFeature == null)
        {
            FindAndSetupWallPaintFeature();
        }
        
        // Проверяем готовность сегментации
        isSegmentationReady = (wallSegmentation != null && wallSegmentation.IsInitialized && 
                              wallSegmentation.SegmentationMaskTexture != null);
                              
        // Проверяем наличие маски сегментации стен и обновляем материал
        if (useMask && isSegmentationReady)
        {
            if (wallPaintMaterial != null)
            {
                wallPaintMaterial.SetTexture("_SegmentationMask", wallSegmentation.SegmentationMaskTexture);
                
                // Проверяем, является ли маска валидной
                RenderTexture mask = wallSegmentation.SegmentationMaskTexture;
                if (mask != null && mask.IsCreated())
                {
                    // Активируем использование маски
                    wallPaintMaterial.EnableKeyword("USE_MASK");
                }
                else
                {
                    // Деактивируем использование маски, если она не готова
                    wallPaintMaterial.DisableKeyword("USE_MASK");
                }
            }
        }
        else
        {
            // Если маска не используется или недоступна, отключаем ее
            if (wallPaintMaterial != null)
            {
                wallPaintMaterial.DisableKeyword("USE_MASK");
            }
        }
        
        // Если у нас есть WallPaintFeature, убедимся, что она использует наш материал
        if (wallPaintFeature != null && !ReferenceEquals(wallPaintFeature.passMaterial, wallPaintMaterial))
        {
            wallPaintFeature.SetPassMaterial(wallPaintMaterial);
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
            if (isSegmentationReady && useMask)
            {
                wallPaintMaterial.SetTexture("_SegmentationMask", wallSegmentation.SegmentationMaskTexture);
                wallPaintMaterial.EnableKeyword("USE_MASK");
            }
            else
            {
                wallPaintMaterial.DisableKeyword("USE_MASK");
            }
        }

        if (wallPaintFeature != null && !ReferenceEquals(wallPaintFeature.passMaterial, wallPaintMaterial)) 
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
    
    // Проверка готовности эффекта
    public bool IsReady()
    {
        return isInitialized && wallPaintMaterial != null && (!useMask || isSegmentationReady);
    }
} 