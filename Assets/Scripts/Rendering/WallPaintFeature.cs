using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

// ScriptableRendererFeature для эффекта перекраски
[System.Serializable]
public class WallPaintFeature : ScriptableRendererFeature
{
    public Material passMaterial; // Материал, доступный для настройки из редактора
    private WallPaintRenderPass wallPaintPass;
    
    public override void Create()
    {
        // Передаем материал в конструктор
        wallPaintPass = new WallPaintRenderPass(passMaterial);
        wallPaintPass.renderPassEvent = RenderPassEvent.AfterRenderingTransparents; 
    }
    
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (passMaterial == null) 
        {
            Debug.LogWarningFormat("Missing Pass Material for WallPaintFeature. Disabling pass.");
            return;
        }
        
        // Проверяем, что материал содержит необходимые текстуры
        if (passMaterial.HasProperty("_SegmentationMask"))
        {
            Texture segMask = passMaterial.GetTexture("_SegmentationMask");
            if (segMask == null)
            {
                // Пропускаем добавление паса, если маска сегментации еще не готова
                return;
            }
        }
        
        // Устанавливаем renderer для использования в Execute, не вызываем cameraColorTargetHandle здесь
        wallPaintPass.SetRenderer(renderer);
        
        // Добавляем рендер-пасс
        renderer.EnqueuePass(wallPaintPass);
    }

    // Метод для обновления материала
    public void SetPassMaterial(Material material) 
    {
        this.passMaterial = material;
        if (wallPaintPass != null) 
        {
            wallPaintPass.SetMaterial(material);
        }
    }
}

// RenderPass для эффекта перекраски
public class WallPaintRenderPass : ScriptableRenderPass
{
    private Material wallPaintMaterial;
    private RTHandle tempTexture;
    private string profilerTag = "WallPaint Pass";
    private ScriptableRenderer renderer;
    private bool isSetupComplete = false;
    
    public WallPaintRenderPass(Material material)
    {
        this.wallPaintMaterial = material;
        profilingSampler = new ProfilingSampler(profilerTag);
    }

    public void SetMaterial(Material material) 
    {
        this.wallPaintMaterial = material;
    }
    
    public void SetRenderer(ScriptableRenderer renderer)
    {
        this.renderer = renderer;
    }
    
    public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
    {
        isSetupComplete = false;
        
        if (renderer == null || wallPaintMaterial == null)
        {
            return;
        }
        
        // Выделяем временный RTHandle
        var desc = renderingData.cameraData.cameraTargetDescriptor;
        desc.depthBufferBits = 0; // Глубина не нужна для blit эффекта
        RenderingUtils.ReAllocateIfNeeded(ref tempTexture, desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_TempWallPaintTexture");
        
        isSetupComplete = tempTexture != null;
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (!isSetupComplete || wallPaintMaterial == null || renderer == null)
        {
            return;
        }
        
        // Проверяем, что материал содержит все необходимые текстуры и они не null
        if (wallPaintMaterial.HasProperty("_SegmentationMask"))
        {
            Texture segMask = wallPaintMaterial.GetTexture("_SegmentationMask");
            if (segMask == null)
            {
                return;
            }
        }
        
        // Получаем цветовой буфер только внутри метода Execute
        RTHandle source = renderer.cameraColorTargetHandle;
        
        if (source == null || tempTexture == null)
        {
            return;
        }
        
        CommandBuffer cmd = CommandBufferPool.Get(profilerTag);
        
        using (new ProfilingScope(cmd, profilingSampler))
        {
            try
            {
                // Blit из source в temp с материалом, затем из temp обратно в source
                // Используем безопасный вариант с обработкой ошибок
                SafeBlitCameraTexture(cmd, source, tempTexture, wallPaintMaterial, 0);
                SafeBlitCameraTexture(cmd, tempTexture, source);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error during WallPaintRenderPass execution: {e.Message}");
                // Если произошла ошибка, просто прерываем выполнение
                return;
            }
        }
        
        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }
    
    // Безопасный метод для Blit с проверками на null
    private void SafeBlitCameraTexture(CommandBuffer cmd, RTHandle source, RTHandle destination, Material material = null, int pass = 0)
    {
        if (source == null || destination == null)
        {
            Debug.LogWarning("Source or destination RTHandle is null in SafeBlitCameraTexture");
            return;
        }
        
        // Используем Blit с RTHandle корректно
        if (material != null)
        {
            // Проверяем, что все текстуры в материале не null
            if (material.HasProperty("_SegmentationMask") && material.GetTexture("_SegmentationMask") == null)
            {
                // Если маска отсутствует, используем простой Blit без материала
                Blit(cmd, source, destination);
                return;
            }
            
            // Используем метод Blit из класса ScriptableRenderPass вместо прямого вызова cmd.Blit
            Blit(cmd, source, destination, material, pass);
        }
        else
        {
            // Простой Blit без материала
            Blit(cmd, source, destination);
        }
    }

    public override void OnCameraCleanup(CommandBuffer cmd)
    {
        // Очищаем временные ресурсы
        if (tempTexture != null)
        {
            tempTexture.Release();
            tempTexture = null;
        }
        
        isSetupComplete = false;
    }
} 