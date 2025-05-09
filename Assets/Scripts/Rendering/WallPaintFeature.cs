using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System;

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
    private bool isFirstFrame = true;
    private bool hasWarnedAboutMissingMask = false;
    private Material fallbackMaterial;
    private bool materialInitialized = false;

    public override void Create()
    {
        // Проверяем наличие материала и пытаемся загрузить его, если не задан
        if (passMaterial == null)
        {
            LoadDefaultMaterial();
        }

        EnsureFallbackMaterial();

        // Передаем материал в конструктор
        wallPaintPass = new WallPaintRenderPass(passMaterial);
        wallPaintPass.renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
        wallPaintPass.minTransparency = minTransparency;
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
                materialInitialized = true;
                Debug.Log("WallPaintFeature: Created default material with Custom/WallPaint shader");
            }
            else
            {
                Debug.LogWarning("WallPaintFeature: Shader 'Custom/WallPaint' not found. Please create or assign material manually.");
            }
        }
        else
        {
            materialInitialized = true;
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

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        // Если это первый кадр, инициализируем материал с безопасными настройками
        if (isFirstFrame && passMaterial != null)
        {
            if (!materialInitialized)
            {
                EnsureSafeInitialState();
                materialInitialized = true;
            }
            isFirstFrame = false;
        }

        Material selectedMaterial = null;
        bool useFallback = false;

        // Проверяем, доступен ли основной материал
        if (passMaterial != null)
        {
            // Проверяем наличие маски сегментации
            if (passMaterial.HasProperty("_SegmentationMask"))
            {
                Texture segMask = passMaterial.GetTexture("_SegmentationMask");
                if (segMask == null)
                {
                    // Маска сегментации отсутствует
                    useFallback = true;

                    // Выводим предупреждение только один раз
                    if (!hasWarnedAboutMissingMask)
                    {
                        Debug.LogWarning("WallPaintFeature: Segmentation mask texture is missing in the material");
                        hasWarnedAboutMissingMask = true;
                    }
                }
                else
                {
                    selectedMaterial = passMaterial;
                    hasWarnedAboutMissingMask = false; // Сбрасываем флаг, так как маска теперь есть
                }
            }
            else
            {
                selectedMaterial = passMaterial;
            }
        }
        else
        {
            useFallback = true;
        }

        // Если надо использовать запасной материал
        if (useFallback && useFallbackMaterial && fallbackMaterial != null)
        {
            selectedMaterial = fallbackMaterial;
        }

        // Если не удалось выбрать материал, выходим
        if (selectedMaterial == null)
        {
            return;
        }

        // Обновляем материал в рендер-пассе
        wallPaintPass.SetMaterial(selectedMaterial);

        // Устанавливаем renderer для использования в Execute
        wallPaintPass.SetRenderer(renderer);

        // Добавляем рендер-пасс
        renderer.EnqueuePass(wallPaintPass);
    }

    // Устанавливает безопасное начальное состояние материала
    private void EnsureSafeInitialState()
    {
        if (passMaterial == null)
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
            this.passMaterial = material;
            materialInitialized = false; // Сбрасываем флаг, чтобы проверить настройки нового материала

            if (wallPaintPass != null)
            {
                wallPaintPass.SetMaterial(material);
            }
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
}

// RenderPass для эффекта перекраски
public class WallPaintRenderPass : ScriptableRenderPass
{
    private Material wallPaintMaterial;
    private RTHandle tempTexture;
    private string profilerTag = "WallPaint Pass";
    private ScriptableRenderer renderer;
    private bool isSetupComplete = false;
    public float minTransparency = 0.3f;

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
            Debug.LogWarning("WallPaintRenderPass: Setup is not complete or required components are missing");
            return;
        }

        // Получаем цветовой буфер только внутри метода Execute
        RTHandle source = renderer.cameraColorTargetHandle;

        if (source == null || tempTexture == null)
        {
            Debug.LogWarning("WallPaintRenderPass: Source or tempTexture is null");
            return;
        }

        CommandBuffer cmd = CommandBufferPool.Get(profilerTag);

        using (new ProfilingScope(cmd, profilingSampler))
        {
            try
            {
                // Проверяем наличие маски сегментации и её валидность
                bool hasValidMask = false;

                if (wallPaintMaterial != null && wallPaintMaterial.HasProperty("_SegmentationMask"))
                {
                    Texture segMask = wallPaintMaterial.GetTexture("_SegmentationMask");
                    hasValidMask = segMask != null;
                }

                // Проверяем, нужно ли применять эффект или просто пропустить его
                bool skipEffect = false;
                float blendFactor = 1.0f;

                if (wallPaintMaterial != null && wallPaintMaterial.HasProperty("_BlendFactor"))
                {
                    blendFactor = wallPaintMaterial.GetFloat("_BlendFactor");
                    skipEffect = blendFactor < 0.01f;
                }

                // Если маски нет, ограничиваем прозрачность для предотвращения эффекта "черного экрана"
                if (!hasValidMask && wallPaintMaterial != null && wallPaintMaterial.HasProperty("_BlendFactor"))
                {
                    // Если эффект был бы непрозрачным, делаем его полупрозрачным
                    if (blendFactor > 1.0f - minTransparency)
                    {
                        // Применяем ограничение прозрачности
                        float safeFactor = Mathf.Min(blendFactor, 1.0f - minTransparency);
                        wallPaintMaterial.SetFloat("_BlendFactor", safeFactor);
                    }
                }

                if (skipEffect || wallPaintMaterial == null)
                {
                    // Если эффект пропускается или нет материала, просто копируем исходное изображение
                    if (source != null && tempTexture != null)
                    {
                        Blit(cmd, source, tempTexture);
                        Blit(cmd, tempTexture, source);
                    }
                }
                else
                {
                    // Применяем эффект с материалом
                    // Blit из source в temp с материалом, затем из temp обратно в source
                    if (source != null && tempTexture != null && wallPaintMaterial != null)
                    {
                        SafeBlitCameraTexture(cmd, source, tempTexture, wallPaintMaterial, 0);
                        SafeBlitCameraTexture(cmd, tempTexture, source);
                    }
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Error during WallPaintRenderPass execution: {e.Message}");
                // Если произошла ошибка, просто копируем исходное изображение без эффекта
                try
                {
                    if (source != null && tempTexture != null)
                    {
                        Blit(cmd, source, tempTexture);
                        Blit(cmd, tempTexture, source);
                        Debug.Log("Выполнен аварийный копирующий Blit после ошибки");
                    }
                }
                catch (System.Exception e2)
                {
                    Debug.LogError($"Failed to perform emergency Blit after error: {e2.Message}");
                }
            }
        }

        context.ExecuteCommandBuffer(cmd);
        CommandBufferPool.Release(cmd);
    }

    // Безопасный метод для Blit с проверками на null и дополнительными мерами безопасности
    private void SafeBlitCameraTexture(CommandBuffer cmd, RTHandle source, RTHandle destination, Material material = null, int pass = 0)
    {
        if (cmd == null)
        {
            Debug.LogWarning("CommandBuffer is null in SafeBlitCameraTexture");
            return;
        }

        if (source == null || destination == null)
        {
            Debug.LogWarning("Source or destination RTHandle is null in SafeBlitCameraTexture");
            return;
        }

        // Используем Blit с RTHandle корректно
        if (material != null)
        {
            try
            {
                // Проверяем, что все текстуры в материале не null
                bool hasMissingTextures = false;

                if (material.HasProperty("_SegmentationMask") && material.GetTexture("_SegmentationMask") == null)
                {
                    hasMissingTextures = true;
                }

                if (hasMissingTextures)
                {
                    // Если нет маски, применяем модифицированный Blit
                    // с полупрозрачностью для предотвращения черного экрана
                    if (material.HasProperty("_BlendFactor"))
                    {
                        float originalBlend = material.GetFloat("_BlendFactor");

                        // Временно устанавливаем более прозрачное значение для безопасного блита
                        float safeBlend = Mathf.Min(originalBlend, 1.0f - minTransparency);
                        material.SetFloat("_BlendFactor", safeBlend);

                        // Выполняем Blit
                        try
                        {
                            Blit(cmd, source, destination, material, pass);
                        }
                        catch (System.Exception ex)
                        {
                            Debug.LogWarning($"Failed to Blit with material: {ex.Message}. Falling back to simple Blit.");
                            Blit(cmd, source, destination);
                        }

                        // Восстанавливаем оригинальное значение
                        material.SetFloat("_BlendFactor", originalBlend);
                    }
                    else
                    {
                        // Если нельзя контролировать прозрачность, то просто копируем без эффекта
                        Blit(cmd, source, destination);
                    }
                    return;
                }

                try
                {
                    // Используем метод Blit из класса ScriptableRenderPass вместо прямого вызова cmd.Blit
                    Blit(cmd, source, destination, material, pass);
                }
                catch (System.Exception ex)
                {
                    Debug.LogError($"Failed to Blit with material: {ex.Message}");

                    // Fallback к простому Blit без материала
                    try
                    {
                        Blit(cmd, source, destination);
                    }
                    catch (System.Exception e2)
                    {
                        Debug.LogError($"Failed to perform fallback Blit: {e2.Message}");
                    }
                }
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Exception in SafeBlitCameraTexture with material: {ex.Message}");
                // Fallback к простому Blit без материала в случае ошибки
                try
                {
                    Blit(cmd, source, destination);
                }
                catch (System.Exception e2)
                {
                    Debug.LogError($"Failed to perform fallback Blit after exception: {e2.Message}");
                }
            }
        }
        else
        {
            // Простой Blit без материала
            try
            {
                Blit(cmd, source, destination);
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"Exception in SafeBlitCameraTexture without material: {ex.Message}");
            }
        }
    }

    public override void OnCameraCleanup(CommandBuffer cmd)
    {
        // RTHandle автоматически освобождается через ReAllocateIfNeeded
    }
}