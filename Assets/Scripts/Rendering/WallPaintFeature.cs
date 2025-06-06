using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System;
using System.Linq;

// ScriptableRendererFeature для эффекта перекраски
[System.Serializable]
public class WallPaintFeature : ScriptableRendererFeature
{
    [Tooltip("Материал, доступный для настройки из редактора")]
    public Material passMaterial;

    [Tooltip("Минимальная прозрачность эффекта (0-1)")]
    [Range(0, 1)]
    public float minTransparency = 0.3f;

    [Tooltip("Использовать запасной материал если основной недоступен")]
    public bool useFallbackMaterial = true;

    private WallPaintRenderPass wallPaintPass;
    private bool hasWarnedAboutMissingMask = false;
    private Material fallbackMaterial;

    public override void Create()
    {
        // Проверяем наличие материала и пытаемся загрузить его, если не задан
        // if (passMaterial == null)
        // {
        // LoadDefaultMaterial(); // Не загружаем здесь, пусть WallPaintEffect установит
        // }

        EnsureFallbackMaterial(); // Запасной материал создаем в любом случае

        // Ensure the material has textures
        // EnsureMaterialTextures(); // Не проверяем текстуры здесь, passMaterial еще может быть null

        // Передаем материал в конструктор, он может быть null на этом этапе
        // wallPaintPass будет сам проверять материал перед использованием
        wallPaintPass = new WallPaintRenderPass(passMaterial); // passMaterial может быть null здесь
        wallPaintPass.renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
        wallPaintPass.minTransparency = minTransparency;
        hasWarnedAboutMissingMask = false; // Сбросим флаг предупреждения
    }

    // Пытаемся загрузить материал по умолчанию
    private void LoadDefaultMaterial()
    {
        // Пробуем найти в Resources
        passMaterial = Resources.Load<Material>("Materials/WallPaint");

        if (passMaterial == null)
        {
            // Пробуем найти по шейдеру
            Shader wallPaintShader = Shader.Find("Custom/WallPaint");
            if (wallPaintShader != null)
            {
                passMaterial = new Material(wallPaintShader);
                passMaterial.SetColor("_PaintColor", Color.red);
                passMaterial.SetFloat("_BlendFactor", 0.7f);
                Debug.Log("WallPaintFeature: Created default material with Custom/WallPaint shader");
            }
            else
            {
                Debug.LogWarning("WallPaintFeature: Shader 'Custom/WallPaint' not found. Please create or assign material manually.");
            }
        }
        else
        {
            Debug.Log("WallPaintFeature: Loaded default material from Resources/Materials/WallPaint");
        }
    }

    // Создаем простой запасной материал для отрисовки в случае проблем
    private void EnsureFallbackMaterial()
    {
        if (fallbackMaterial != null)
            return;

        try
        {
            // Создаем простой материал с прозрачностью
            Shader transparentShader = Shader.Find("Universal Render Pipeline/Unlit");
            if (transparentShader != null)
            {
                fallbackMaterial = new Material(transparentShader);
                fallbackMaterial.SetColor("_BaseColor", new Color(1f, 0.5f, 0.5f, 0.2f));
                fallbackMaterial.SetFloat("_Surface", 1f); // Прозрачный режим
                fallbackMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                fallbackMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                fallbackMaterial.SetInt("_ZWrite", 0);
                fallbackMaterial.DisableKeyword("_ALPHATEST_ON");
                fallbackMaterial.EnableKeyword("_ALPHABLEND_ON");
                fallbackMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                fallbackMaterial.renderQueue = 3000;
                Debug.Log("WallPaintFeature: Created fallback material");
            }
            else
            {
                // Попробуем другие стандартные шейдеры
                Shader[] fallbackShaders = new Shader[]
                {
                    Shader.Find("Unlit/Transparent"),
                    Shader.Find("UI/Default"),
                    Shader.Find("Sprites/Default"),
                    Shader.Find("Unlit/Color")
                };

                foreach (var shader in fallbackShaders)
                {
                    if (shader != null)
                    {
                        fallbackMaterial = new Material(shader);

                        // Настраиваем в зависимости от типа шейдера
                        if (shader.name.Contains("Transparent"))
                        {
                            fallbackMaterial.color = new Color(1f, 0.5f, 0.5f, 0.2f);
                        }
                        else if (shader.name.Contains("UI"))
                        {
                            fallbackMaterial.SetColor("_Color", new Color(1f, 0.5f, 0.5f, 0.2f));
                        }
                        else if (shader.name.Contains("Sprites"))
                        {
                            fallbackMaterial.SetColor("_Color", new Color(1f, 0.5f, 0.5f, 0.2f));
                        }
                        else
                        {
                            fallbackMaterial.SetColor("_Color", new Color(1f, 0.5f, 0.5f, 0.5f));
                        }

                        fallbackMaterial.renderQueue = 3000;
                        Debug.Log($"WallPaintFeature: Created fallback material with shader {shader.name}");
                        break;
                    }
                }

                // Если ни один шейдер не найден, создаем самый простой материал
                if (fallbackMaterial == null)
                {
                    fallbackMaterial = new Material(Shader.Find("Hidden/InternalErrorShader"));
                    fallbackMaterial.color = new Color(1f, 0.5f, 0.5f, 0.2f);
                    Debug.LogWarning("WallPaintFeature: Created fallback material with error shader");
                }
            }
        }
        catch (Exception e)
        {
            Debug.LogError($"WallPaintFeature: Не удалось создать запасной материал: {e.Message}");

            // Критический резервный вариант - создаём материал самым простым способом
            try
            {
                fallbackMaterial = new Material(Shader.Find("Hidden/InternalErrorShader"));
                Debug.LogWarning("WallPaintFeature: Created minimal fallback material");
            }
            catch
            {
                Debug.LogError("WallPaintFeature: Could not create any fallback material");
            }
        }
    }

    // Add a new method to ensure material textures
    private void EnsureMaterialTextures()
    {
        if (passMaterial == null) return;

        // Check _MainTex
        if (passMaterial.GetTexture("_MainTex") == null)
        {
            Texture2D defaultTexture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[16];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(255, 255, 255, 255);
            }
            defaultTexture.SetPixels32(pixels);
            defaultTexture.Apply();

            passMaterial.SetTexture("_MainTex", defaultTexture);
            Debug.Log("WallPaintFeature: Set default _MainTex to prevent rendering errors");
        }

        // Check _SegmentationMask
        if (passMaterial.GetTexture("_SegmentationMask") == null)
        {
            Texture2D defaultMask = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[16];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(0, 0, 0, 0);
            }
            defaultMask.SetPixels32(pixels);
            defaultMask.Apply();

            passMaterial.SetTexture("_SegmentationMask", defaultMask);
            Debug.Log("WallPaintFeature: Set default _SegmentationMask to prevent rendering errors");

            // Since we're using a blank mask, we should disable the USE_MASK keyword
            passMaterial.DisableKeyword("USE_MASK");
        }
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        // If the pass or renderer is null, setup failed, so exit
        if (wallPaintPass == null || renderer == null)
        {
            return;
        }

        // Create fallback material if needed
        if (useFallbackMaterial && fallbackMaterial == null)
        {
            EnsureFallbackMaterial();
        }

        // If passMaterial is null, automatically create a default one instead of just showing a warning
        if (passMaterial == null)
        {
            Shader defaultShader = Shader.Find("Universal Render Pipeline/Lit");
            if (defaultShader != null)
            {
                passMaterial = new Material(defaultShader);
                passMaterial.SetColor("_BaseColor", new Color(1f, 1f, 1f, 0.7f));
                // Initialize with basic textures
                EnsureMaterialTexturesInternal(passMaterial);
            }
            else
            {
            }
        }

        // Проверяем наличие и готовность материала
        Material materialToUse = null;

        // Проверяем состояние основного материала
        if (passMaterial != null)
        {
            // Проверка наличия требуемых текстур
            bool mainTexOk = passMaterial.HasProperty("_MainTex") && passMaterial.GetTexture("_MainTex") != null;
            bool maskTexOk = true; // По умолчанию маска не обязательна

            // Проверяем маску только если USE_MASK включен
            if (passMaterial.IsKeywordEnabled("USE_MASK"))
            {
                if (passMaterial.HasProperty("_SegmentationMask"))
                {
                    Texture segMask = passMaterial.GetTexture("_SegmentationMask");
                    maskTexOk = segMask != null;

                    // Если маски нет, но требуется, логируем предупреждение
                    if (!maskTexOk && !hasWarnedAboutMissingMask)
                    {
                        hasWarnedAboutMissingMask = true;
                    }
                }
                else
                {
                    // Если свойство _SegmentationMask отсутствует, но USE_MASK включен
                    maskTexOk = false;
                    if (!hasWarnedAboutMissingMask)
                    {
                        hasWarnedAboutMissingMask = true;
                    }
                }
            }

            if (mainTexOk && maskTexOk)
            {
                materialToUse = passMaterial;
            }
        }

        // Если основной материал не готов или не установлен, и разрешен fallback
        if (materialToUse == null && useFallbackMaterial && fallbackMaterial != null)
        {
            materialToUse = fallbackMaterial;
        }
        else if (materialToUse == null)
        {
            // Если основной материал не готов, и fallback не разрешен или отсутствует
            return;
        }

        // Если не удалось выбрать материал, выходим
        if (materialToUse == null)
        {
            return;
        }


        // Обновляем материал в рендер-пассе
        wallPaintPass.SetMaterial(materialToUse);

        // Устанавливаем renderer для использования в Execute
        wallPaintPass.SetRenderer(renderer);

        // Добавляем рендер-пасс
        renderer.EnqueuePass(wallPaintPass);
    }

    // Устанавливает безопасное начальное состояние материала
    private void EnsureSafeInitialState()
    {
        if (passMaterial == null) // Этот метод теперь должен вызываться только если passMaterial уже установлен
            return;

        // Устанавливаем полупрозрачность эффекта для предотвращения черного экрана
        if (passMaterial.HasProperty("_BlendFactor"))
        {
            float currentBlend = passMaterial.GetFloat("_BlendFactor");
            if (currentBlend > 0.95f)
            {
                passMaterial.SetFloat("_BlendFactor", 0.7f);
                Debug.Log("WallPaintFeature: Adjusted BlendFactor to safer value (0.7)");
            }
        }

        // Убеждаемся, что материал работает в режиме прозрачности
        if (passMaterial.HasProperty("_SrcBlend") && passMaterial.HasProperty("_DstBlend"))
        {
            passMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            passMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        }

        // Убеждаемся в корректном режиме наложения
        if (passMaterial.HasProperty("_BlendMode"))
        {
            passMaterial.SetFloat("_BlendMode", 1.0f); // Обычно это Alpha Blend
        }
    }

    // Метод для обновления материала
    public void SetPassMaterial(Material material)
    {
        if (material != null)
        {
            this.passMaterial = material; // Это основной материал, который WallPaintEffect устанавливает
            // Сбросим флаги, чтобы AddRenderPasses выполнил проверки заново
            hasWarnedAboutMissingMask = false;

            // Log details about the material
            string materialName = material.name;
            string shaderName = material.shader != null ? material.shader.name : "null shader";
            Debug.Log($"WallPaintFeature: SetPassMaterial called with material '{materialName}', shader '{shaderName}'. Instance ID: {material.GetInstanceID()}");

            // Убедимся, что у нового материала есть нужные текстуры или установим заглушки
            // Это гарантирует, что материал будет рендер-способным, даже если текстуры еще не пришли от WallSegmentation
            EnsureMaterialTexturesInternal(this.passMaterial);


            if (wallPaintPass != null)
            {
                // wallPaintPass.SetMaterial(this.passMaterial); // Не устанавливаем здесь напрямую, пусть AddRenderPasses решает
            }
            // Debug.Log("WallPaintFeature: passMaterial has been set by WallPaintEffect."); // Эта строка теперь избыточна
        }
        else
        {
            this.passMaterial = null; // Явно обнуляем, если пришел null
            Debug.Log("WallPaintFeature: SetPassMaterial called with null material.");
        }
    }

    // Методы обновления настроек во время выполнения
    public void SetMinTransparency(float value)
    {
        minTransparency = Mathf.Clamp01(value);
        if (wallPaintPass != null)
        {
            wallPaintPass.minTransparency = minTransparency;
        }
    }

    public void SetUseFallbackMaterial(bool value)
    {
        useFallbackMaterial = value;
    }

    // This is called when the feature is enabled
    public override void SetupRenderPasses(ScriptableRenderer renderer, in RenderingData renderingData)
    {
        // Проверка инициализации материала
        // Эту логику лучше убрать отсюда, пусть WallPaintEffect отвечает за готовность passMaterial
        // if (!materialInitialized && passMaterial != null)
        // {
        // EnsureSafeInitialState();
        // EnsureMaterialTextures(); // Ensure textures are initialized
        // materialInitialized = true;
        // }

        // Передаем рендерер в рендер-пасс
        if (wallPaintPass != null) // Добавим проверку
        {
            wallPaintPass.SetRenderer(renderer);
        }
    }

    // Override proper method in the feature to clean up
    protected override void Dispose(bool disposing)
    {
        // Clean up the render pass if it exists
        if (disposing && wallPaintPass != null)
        {
            wallPaintPass.Dispose();
            wallPaintPass = null;
        }

        base.Dispose(disposing);
    }

    // Внутренний метод для проверки и установки текстур-заглушек для переданного материала
    private void EnsureMaterialTexturesInternal(Material mat)
    {
        if (mat == null) return;

        // Check _MainTex
        if (mat.GetTexture("_MainTex") == null)
        {
            Texture2D defaultTexture = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[16];
            for (int i = 0; i < pixels.Length; i++)
            {
                pixels[i] = new Color32(255, 255, 255, 255);
            }
            defaultTexture.SetPixels32(pixels);
            defaultTexture.Apply();

            mat.SetTexture("_MainTex", defaultTexture);
            Debug.Log("WallPaintFeature (Internal): Set default _MainTex on a material.");
        }

        // Check _SegmentationMask
        // Не создаем здесь заглушку для _SegmentationMask агрессивно,
        // так как ее наличие проверяется в AddRenderPasses для выбора fallback.
        // Если USE_MASK включено, но маски нет, AddRenderPasses использует fallback.
        // if (mat.GetTexture("_SegmentationMask") == null && mat.IsKeywordEnabled("USE_MASK"))
        // {
        // Texture2D defaultMask = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        // Color32[] pixels = new Color32[16];
        // for (int i = 0; i < pixels.Length; i++)
        //    {
        // pixels[i] = new Color32(0, 0, 0, 0); // Полностью прозрачная черная маска
        //    }
        // defaultMask.SetPixels32(pixels);
        // defaultMask.Apply();
        //
        // mat.SetTexture("_SegmentationMask", defaultMask);
        // Debug.Log("WallPaintFeature (Internal): Set default _SegmentationMask on a material because USE_MASK was enabled but no mask was present.");
        // }
    }
}

// RenderPass для эффекта перекраски
public class WallPaintRenderPass : ScriptableRenderPass, System.IDisposable
{
    private Material wallPaintMaterial;
    private RTHandle tempTexture;
    private string profilerTag = "WallPaint Pass";
    private ScriptableRenderer renderer;
    private bool isSetupComplete = false;
    public float minTransparency = 0.3f;
    private bool disposed = false;

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

        try
        {
            // Get descriptor from camera data
            var desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0; // No depth needed for blit effect
            desc.msaaSamples = 1; // Force no MSAA for compatibility

            try
            {
                // Try to allocate or reallocate the temporary texture
                RenderingUtils.ReAllocateIfNeeded(ref tempTexture, desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_TempWallPaintTexture");
            }
            catch (System.Exception)
            {
                // Try alternative allocation method
                if (tempTexture == null)
                {
                    try
                    {
                        // Try direct RenderTexture creation as fallback
                        RenderTexture rt = new RenderTexture(desc.width, desc.height, 0, desc.colorFormat);
                        rt.filterMode = FilterMode.Bilinear;
                        rt.wrapMode = TextureWrapMode.Clamp;
                        rt.Create();

                        tempTexture = RTHandles.Alloc(rt);
                    }
                    catch (System.Exception)
                    {
                        return;
                    }
                }
            }

            isSetupComplete = tempTexture != null;
        }
        catch (System.Exception)
        {
            isSetupComplete = false;
        }
    }

    public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
    {
        // Early exit conditions - fail safe
        if (wallPaintMaterial == null || renderer == null)
        {
            return;
        }

        if (!isSetupComplete)
        {
            return;
        }

        // Get camera color target
        RTHandle source = null;
        try
        {
            source = renderer.cameraColorTargetHandle;
        }
        catch (System.Exception)
        {
            return;
        }

        if (source == null || tempTexture == null)
        {
            return;
        }

        // Convert RTHandles to RenderTargetIdentifiers safely
        RenderTargetIdentifier sourceID, tempID;

        // Create RenderTargetIdentifiers safely without using nameID which can cause assertions
        if (source.rt != null)
            sourceID = new RenderTargetIdentifier(source.rt);
        else
            sourceID = source; // Let the implicit operator handle it if possible

        if (tempTexture.rt != null)
            tempID = new RenderTargetIdentifier(tempTexture.rt);
        else
            tempID = tempTexture; // Let the implicit operator handle it if possible

        CommandBuffer cmd = CommandBufferPool.Get(profilerTag);

        using (new ProfilingScope(cmd, profilingSampler))
        {
            try
            {
                // Check material textures before using them
                bool hasValidMask = false;
                bool hasValidMainTex = false;

                if (wallPaintMaterial != null)
                {
                    if (wallPaintMaterial.HasProperty("_SegmentationMask"))
                    {
                        Texture segMask = wallPaintMaterial.GetTexture("_SegmentationMask");
                        hasValidMask = segMask != null;
                    }

                    if (wallPaintMaterial.HasProperty("_MainTex"))
                    {
                        Texture mainTex = wallPaintMaterial.GetTexture("_MainTex");
                        hasValidMainTex = mainTex != null;
                    }
                }

                // If main texture is missing, we should add it (using source as reference)
                if (!hasValidMainTex && wallPaintMaterial != null)
                {
                    // Create a simple white texture
                    Texture2D defaultTex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
                    Color[] pixels = Enumerable.Repeat(Color.white, 16).ToArray();
                    defaultTex.SetPixels(pixels);
                    defaultTex.Apply();

                    wallPaintMaterial.SetTexture("_MainTex", defaultTex);
                    hasValidMainTex = true;
                }

                // Check for blend factor (to handle transparency)
                bool skipEffect = false;
                float blendFactor = 1.0f;

                if (wallPaintMaterial != null && wallPaintMaterial.HasProperty("_BlendFactor"))
                {
                    blendFactor = wallPaintMaterial.GetFloat("_BlendFactor");
                    skipEffect = blendFactor < 0.01f;
                }

                // For safety, if mask is missing, limit opacity
                if (!hasValidMask && wallPaintMaterial != null && wallPaintMaterial.HasProperty("_BlendFactor"))
                {
                    // If effect would be opaque, make it semi-transparent
                    if (blendFactor > 1.0f - minTransparency)
                    {
                        // Apply transparency limit
                        float safeFactor = Mathf.Min(blendFactor, 1.0f - minTransparency);
                        wallPaintMaterial.SetFloat("_BlendFactor", safeFactor);
                    }
                }

                // Decide whether to apply effect or just copy
                if (skipEffect || wallPaintMaterial == null || !hasValidMainTex)
                {
                    // If effect is skipped or no material, just copy source image directly
                    try
                    {
                        cmd.Blit(sourceID, tempID);
                        cmd.Blit(tempID, sourceID);
                    }
                    catch (System.Exception)
                    {
                    }
                }
                else
                {
                    // Apply effect with material
                    SafeBlitCameraTexture(cmd, source, tempTexture, wallPaintMaterial, 0);
                    SafeBlitCameraTexture(cmd, tempTexture, source);
                }
            }
            catch (System.Exception)
            {
                // In case of error, just copy source image without effect
                try
                {
                    if (source != null && tempTexture != null)
                    {
                        // Use the already created safer RenderTargetIdentifiers
                        cmd.Blit(sourceID, tempID);
                        cmd.Blit(tempID, sourceID);
                    }
                }
                catch (System.Exception)
                {
                    Debug.LogError("Failed to perform emergency copy after error");

                    // Ultra-fallback: try direct RenderTexture blits
                    try
                    {
                        if (source.rt != null && tempTexture.rt != null)
                        {
                            cmd.Blit(source.rt, tempTexture.rt);
                            cmd.Blit(tempTexture.rt, source.rt);
                        }
                    }
                    catch (System.Exception)
                    {
                        // Nothing left to try
                    }
                }
            }
        }

        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }

    // Безопасный метод для Blit с проверками на null и дополнительными мерами безопасности
    private void SafeBlitCameraTexture(CommandBuffer cmd, RTHandle source, RTHandle destination, Material material = null, int pass = 0)
    {
        try
        {
            // Skip blit if source or destination textures are invalid
            if (source == null || destination == null)
            {
                return;
            }

            // Convert to RenderTargetIdentifier more safely without using nameID which can cause assertions
            RenderTargetIdentifier srcId, destId;

            // Use a safer approach to get RenderTargetIdentifier from RTHandle
            if (source.rt != null)
                srcId = new RenderTargetIdentifier(source.rt);
            else
                srcId = source; // Let the implicit operator handle it if possible

            if (destination.rt != null)
                destId = new RenderTargetIdentifier(destination.rt);
            else
                destId = destination; // Let the implicit operator handle it if possible

            if (material != null)
            {
                // Use command buffer blit with material
                cmd.Blit(srcId, destId, material, pass);
            }
            else
            {
                // Use command buffer blit without material
                cmd.Blit(srcId, destId);
            }
        }
        catch (System.Exception)
        {
            // Error handling without unused variable
        }
    }

    public override void OnCameraCleanup(CommandBuffer cmd)
    {
        // Explicitly handle cleanup for safety
        isSetupComplete = false;
    }

    // Implement IDisposable pattern correctly
    public void Dispose()
    {
        Dispose(true);
        System.GC.SuppressFinalize(this);
    }

    // Implementation of the disposal pattern
    protected virtual void Dispose(bool disposing)
    {
        if (!disposed)
        {
            if (disposing)
            {
                // Release managed resources
                if (tempTexture != null)
                {
                    try
                    {
                        RTHandles.Release(tempTexture);
                        tempTexture = null;
                    }
                    catch (System.Exception)
                    {
                    }
                }
            }

            // Mark as disposed
            disposed = true;
        }
    }

    // Finalizer as a backup
    ~WallPaintRenderPass()
    {
        Dispose(false);
    }
}