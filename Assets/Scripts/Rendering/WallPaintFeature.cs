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
        // Выделяем временный RTHandle
        var desc = renderingData.cameraData.cameraTargetDescriptor;
        desc.depthBufferBits = 0; // Глубина не нужна для blit эффекта
        RenderingUtils.ReAllocateIfNeeded(ref tempTexture, desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_TempWallPaintTexture");
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        if (wallPaintMaterial == null || renderer == null)
        {
            Debug.LogError("WallPaintRenderPass is not properly configured.");
            return;
        }
        
        // Получаем цветовой буфер только внутри метода Execute
        RTHandle source = renderer.cameraColorTargetHandle;
        
        if (source == null || tempTexture == null)
        {
            Debug.LogError("Source or target texture is null in WallPaintRenderPass.");
            return;
        }
        
        CommandBuffer cmd = CommandBufferPool.Get(profilerTag);
        
        using (new ProfilingScope(cmd, profilingSampler))
        {
            // Blit из source в temp с материалом, затем из temp обратно в source
            Blitter.BlitCameraTexture(cmd, source, tempTexture, wallPaintMaterial, 0);
            Blitter.BlitCameraTexture(cmd, tempTexture, source);
        }
        
        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }

    public override void OnCameraCleanup(CommandBuffer cmd)
    {
        // Очищаем временные ресурсы
        if (tempTexture != null)
        {
            tempTexture.Release();
            tempTexture = null;
        }
    }
} 