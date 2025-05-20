using UnityEngine;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// Компонент для улучшенной визуализации маски сегментации с поддержкой пространственно-временной стабилизации
/// </summary>
[RequireComponent(typeof(RawImage))]
public class SegmentationVisualizer : MonoBehaviour
{
      [Header("Источник данных")]
      [Tooltip("Источник сегментационной маски")]
      public WallSegmentation wallSegmentation;

      [Tooltip("Источник информации о глубине")]
      public ARCameraManager depthSource;

      [Header("Настройки визуализации")]
      [Tooltip("Цвет для стен")]
      public Color wallColor = new Color(1, 0, 0, 0.7f);

      [Tooltip("Цвет для пола")]
      public Color floorColor = new Color(0, 1, 0, 0.7f);

      [Tooltip("Порог обнаружения для стен")]
      [Range(0.01f, 0.99f)]
      public float wallThreshold = 0.5f;

      [Tooltip("Порог обнаружения для пола")]
      [Range(0.01f, 0.99f)]
      public float floorThreshold = 0.5f;

      [Header("Эффекты")]
      [Tooltip("Мягкость краев")]
      [Range(0.001f, 0.1f)]
      public float edgeSoftness = 0.02f;

      [Tooltip("Интенсивность свечения краев")]
      [Range(0f, 1f)]
      public float edgeGlow = 0.3f;

      [Tooltip("Вес временной стабилизации")]
      [Range(0f, 0.98f)]
      public float temporalWeight = 0.8f;

      [Tooltip("Резкость")]
      [Range(0f, 2f)]
      public float sharpness = 1f;

      [Tooltip("Влияние глубины")]
      [Range(0f, 1f)]
      public float depthInfluence = 0.5f;

      [Tooltip("Максимальная глубина")]
      public float maxDepth = 5f;

      [Header("Динамическое качество")]
      [Tooltip("Включить автоматическую настройку качества")]
      public bool enableDynamicQuality = true;

      [Tooltip("Целевой FPS")]
      public float targetFPS = 30f;

      [Header("Отладка")]
      [Tooltip("Режим отображения для отладки")]
      public DebugRenderMode debugMode = DebugRenderMode.Final;

      // Приватные поля
      private RawImage rawImage;
      private Material visualizerMaterial;
      private RenderTexture[] buffers = new RenderTexture[2];
      private int currentBuffer = 0;
      private RenderTexture currentFrame;
      private RenderTexture previousFrame;
      private RenderTexture depthTexture;
      private RenderTexture outputTexture;
      private RenderTexture motionVectorsTexture;
      private int frameCount = 0;
      private Texture2D _depthTexture;
      private bool subscribedToEvents = false;

      private float lastResolutionChangeTime = 0f;
      private int consecutiveLowFrameCount = 0;
      private int consecutiveHighFrameCount = 0;
      private float[] fpsSamples = new float[10];
      private int fpsSampleIndex = 0;

      // Режимы отладки
      public enum DebugRenderMode
      {
            Final,
            RawProbability,
            Edges,
            TemporalBlend,
            Depth,
            Performance
      }

      void Awake()
      {
            rawImage = GetComponent<RawImage>();

            // Создаем материал из шейдера
            Shader shader = Shader.Find("Hidden/SegmentationVisualizer");
            if (shader == null)
            {
                  Debug.LogError("[SegmentationVisualizer] Shader not found!");
                  return;
            }

            visualizerMaterial = new Material(shader);
            visualizerMaterial.hideFlags = HideFlags.DontSave;

            // Инициализируем буферы
            for (int i = 0; i < buffers.Length; i++)
            {
                  buffers[i] = new RenderTexture(256, 256, 0, GetOptimalRenderTextureFormat());
                  buffers[i].filterMode = FilterMode.Bilinear;
                  buffers[i].wrapMode = TextureWrapMode.Clamp;
                  buffers[i].antiAliasing = 1;
                  buffers[i].useMipMap = false;
                  buffers[i].Create();
            }

            // Устанавливаем начальную текстуру для отображения
            rawImage.texture = buffers[0];

            // Создаем текстуру для моушн-векторов
            motionVectorsTexture = new RenderTexture(256, 256, 0, RenderTextureFormat.RGHalf);
            motionVectorsTexture.filterMode = FilterMode.Bilinear;
            motionVectorsTexture.Create();
      }

      void Start()
      {
            if (wallSegmentation == null)
            {
                  wallSegmentation = FindObjectOfType<WallSegmentation>();
            }

            if (depthSource == null)
            {
                  depthSource = FindObjectOfType<ARCameraManager>();
            }

            if (wallSegmentation != null && !subscribedToEvents)
            {
                  SubscribeToEvents();
            }
      }

      void OnEnable()
      {
            if (wallSegmentation != null && !subscribedToEvents)
            {
                  SubscribeToEvents();
            }
      }

      void OnDisable()
      {
            if (subscribedToEvents)
            {
                  UnsubscribeFromEvents();
            }
      }

      void Update()
      {
            UpdateShaderParameters();

            if (enableDynamicQuality)
            {
                  // Обновляем FPS-метрики и адаптируем качество
                  UpdateFPSMetrics();
                  AdaptQualityToPerformance(GetAverageFPS());
            }

            // Обновляем информацию о глубине, если доступна
            UpdateDepthTexture();

            // Обновляем моушн векторы для временной стабилизации
            UpdateMotionVectors();

            frameCount++;
      }

      void OnDestroy()
      {
            if (visualizerMaterial != null)
            {
                  Destroy(visualizerMaterial);
                  visualizerMaterial = null;
            }

            CleanupTextures();
      }

      // Подписываемся на события обновления маски
      private void SubscribeToEvents()
      {
            if (wallSegmentation != null)
            {
                  wallSegmentation.OnSegmentationMaskUpdated += OnSegmentationMaskUpdated;
                  subscribedToEvents = true;
                  Debug.Log("[SegmentationVisualizer] Subscribed to segmentation events");
            }
      }

      // Отписываемся от событий
      private void UnsubscribeFromEvents()
      {
            if (wallSegmentation != null)
            {
                  wallSegmentation.OnSegmentationMaskUpdated -= OnSegmentationMaskUpdated;
                  subscribedToEvents = false;
                  Debug.Log("[SegmentationVisualizer] Unsubscribed from segmentation events");
            }
      }

      // Обработка новой маски сегментации
      private void OnSegmentationMaskUpdated(RenderTexture maskTexture)
      {
            if (maskTexture == null || !maskTexture.IsCreated())
            {
                  Debug.LogWarning("[SegmentationVisualizer] Received null or invalid mask texture");
                  return;
            }

            // Убеждаемся, что все текстуры созданы с нужным разрешением
            EnsureTexturesCreated(maskTexture.width, maskTexture.height);

            // Рендерим результат с эффектами
            RenderOutput(maskTexture);
      }

      // Создание необходимых текстур
      private void EnsureTexturesCreated(int width, int height)
      {
            // Создаем буферы для текущего и предыдущего кадра
            if (currentFrame == null || currentFrame.width != width || currentFrame.height != height)
            {
                  CleanupTextures();

                  currentFrame = new RenderTexture(width, height, 0, GetOptimalRenderTextureFormat());
                  currentFrame.filterMode = FilterMode.Bilinear;
                  currentFrame.enableRandomWrite = true;
                  currentFrame.Create();

                  previousFrame = new RenderTexture(width, height, 0, GetOptimalRenderTextureFormat());
                  previousFrame.filterMode = FilterMode.Bilinear;
                  previousFrame.Create();

                  depthTexture = new RenderTexture(width, height, 0, RenderTextureFormat.RFloat);
                  depthTexture.filterMode = FilterMode.Bilinear;
                  depthTexture.Create();

                  outputTexture = new RenderTexture(width, height, 0, GetOptimalRenderTextureFormat());
                  outputTexture.filterMode = FilterMode.Bilinear;
                  outputTexture.Create();

                  motionVectorsTexture = new RenderTexture(width, height, 0, RenderTextureFormat.RGHalf);
                  motionVectorsTexture.filterMode = FilterMode.Bilinear;
                  motionVectorsTexture.Create();

                  _depthTexture = new Texture2D(width, height, TextureFormat.RFloat, false);

                  // Обновляем буферы
                  for (int i = 0; i < buffers.Length; i++)
                  {
                        if (buffers[i] != null)
                        {
                              buffers[i].Release();
                              Destroy(buffers[i]);
                        }

                        buffers[i] = new RenderTexture(width, height, 0, GetOptimalRenderTextureFormat());
                        buffers[i].filterMode = FilterMode.Bilinear;
                        buffers[i].Create();
                  }

                  // Чтобы не было ошибок при первом кадре
                  Graphics.Blit(Texture2D.blackTexture, previousFrame);

                  Debug.Log($"[SegmentationVisualizer] Created textures {width}x{height}");
            }
      }

      // Обновление параметров шейдера
      private void UpdateShaderParameters()
      {
            if (visualizerMaterial == null) return;

            visualizerMaterial.SetColor("_WallColor", wallColor);
            visualizerMaterial.SetColor("_FloorColor", floorColor);
            visualizerMaterial.SetFloat("_WallThreshold", wallThreshold);
            visualizerMaterial.SetFloat("_FloorThreshold", floorThreshold);
            visualizerMaterial.SetFloat("_EdgeSoftness", edgeSoftness);
            visualizerMaterial.SetFloat("_EdgeGlow", edgeGlow);
            visualizerMaterial.SetFloat("_TemporalWeight", temporalWeight);
            visualizerMaterial.SetFloat("_Sharpness", sharpness);
            visualizerMaterial.SetFloat("_DepthInfluence", depthInfluence);
            visualizerMaterial.SetFloat("_MaxDepth", maxDepth);

            // Устанавливаем правильные ключевые слова для режима отладки
            visualizerMaterial.DisableKeyword("_DEBUG_RAWPROB");
            visualizerMaterial.DisableKeyword("_DEBUG_EDGES");
            visualizerMaterial.DisableKeyword("_DEBUG_TEMPORAL");

            switch (debugMode)
            {
                  case DebugRenderMode.RawProbability:
                        visualizerMaterial.EnableKeyword("_DEBUG_RAWPROB");
                        break;
                  case DebugRenderMode.Edges:
                        visualizerMaterial.EnableKeyword("_DEBUG_EDGES");
                        break;
                  case DebugRenderMode.TemporalBlend:
                        visualizerMaterial.EnableKeyword("_DEBUG_TEMPORAL");
                        break;
            }
      }

      // Обновление текстуры глубины
      private void UpdateDepthTexture()
      {
            if (depthSource == null || depthTexture == null) return;

            try
            {
                  // Здесь логика получения карты глубины из ARCameraManager
                  // Это зависит от реализации вашего проекта

                  // Пример кода:
                  // if (depthSource.TryAcquireLatestCpuImage(out XRCpuImage image))
                  // {
                  //     using (image)
                  //     {
                  //         // Преобразуем карту глубины в текстуру
                  //         ConvertDepthImageToTexture(image, _depthTexture);
                  //         
                  //         // Копируем в RenderTexture
                  //         Graphics.Blit(_depthTexture, depthTexture);
                  //     }
                  // }
            }
            catch (System.Exception e)
            {
                  Debug.LogWarning($"[SegmentationVisualizer] Depth update error: {e.Message}");
            }
      }

      // Обновление векторов движения для временной стабилизации
      private void UpdateMotionVectors()
      {
            if (motionVectorsTexture == null) return;

            // Для простой реализации можно использовать разницу положения камеры
            // между кадрами. Для более точной реализации можно использовать
            // оптический поток или моушн векторы из рендеринга

            // В этой упрощенной версии мы просто заполняем текстуру нулями,
            // что означает отсутствие движения
            if (frameCount % 5 == 0) // Обновляем каждые 5 кадров для оптимизации
            {
                  Graphics.Blit(Texture2D.blackTexture, motionVectorsTexture);
            }
      }

      // Рендеринг финального результата с применением всех эффектов
      private void RenderOutput(RenderTexture maskTexture)
      {
            if (visualizerMaterial == null || maskTexture == null) return;

            // Сохраняем текущий кадр как предыдущий для временной стабилизации
            Graphics.Blit(currentFrame, previousFrame);

            // Копируем новую маску в текущий кадр
            Graphics.Blit(maskTexture, currentFrame);

            // Устанавливаем текстуры в материал
            visualizerMaterial.SetTexture("_MainTex", currentFrame);
            visualizerMaterial.SetTexture("_PrevFrame", previousFrame);
            visualizerMaterial.SetTexture("_DepthTex", depthTexture);
            visualizerMaterial.SetTexture("_MotionVectors", motionVectorsTexture);

            // Используем double buffering для более плавного обновления UI
            int targetBuffer = 1 - currentBuffer;

            // Рендерим с эффектами
            Graphics.Blit(currentFrame, buffers[targetBuffer], visualizerMaterial);

            // Меняем буферы и обновляем UI
            currentBuffer = targetBuffer;
            rawImage.texture = buffers[currentBuffer];
      }

      // Очистка ресурсов
      private void CleanupTextures()
      {
            if (currentFrame != null)
            {
                  currentFrame.Release();
                  Destroy(currentFrame);
                  currentFrame = null;
            }

            if (previousFrame != null)
            {
                  previousFrame.Release();
                  Destroy(previousFrame);
                  previousFrame = null;
            }

            if (depthTexture != null)
            {
                  depthTexture.Release();
                  Destroy(depthTexture);
                  depthTexture = null;
            }

            if (outputTexture != null)
            {
                  outputTexture.Release();
                  Destroy(outputTexture);
                  outputTexture = null;
            }

            if (motionVectorsTexture != null)
            {
                  motionVectorsTexture.Release();
                  Destroy(motionVectorsTexture);
                  motionVectorsTexture = null;
            }

            if (_depthTexture != null)
            {
                  Destroy(_depthTexture);
                  _depthTexture = null;
            }

            // Очищаем буферы
            for (int i = 0; i < buffers.Length; i++)
            {
                  if (buffers[i] != null)
                  {
                        buffers[i].Release();
                        Destroy(buffers[i]);
                        buffers[i] = null;
                  }
            }
      }

      // Адаптивное качество в зависимости от производительности
      private void AdaptQualityToPerformance(float currentFPS)
      {
            if (!enableDynamicQuality) return;

            // Не меняем разрешение слишком часто
            if (Time.time - lastResolutionChangeTime < 1.0f) return;

            if (currentFPS < targetFPS - 5)
            {
                  consecutiveLowFrameCount++;
                  consecutiveHighFrameCount = 0;

                  if (consecutiveLowFrameCount > 5)
                  {
                        // Уменьшаем качество
                        edgeSoftness = Mathf.Max(0.001f, edgeSoftness * 0.8f);
                        edgeGlow = Mathf.Max(0f, edgeGlow * 0.8f);

                        // Сообщаем WallSegmentation о необходимости уменьшить разрешение
                        if (wallSegmentation != null && wallSegmentation.inputResolution.x > 128)
                        {
                              Vector2Int newResolution = new Vector2Int(
                                    Mathf.Max(128, wallSegmentation.inputResolution.x / 2),
                                    Mathf.Max(128, wallSegmentation.inputResolution.y / 2)
                              );

                              wallSegmentation.inputResolution = newResolution;
                              Debug.Log($"[SegmentationVisualizer] Reduced quality to {newResolution.x}x{newResolution.y} (FPS: {currentFPS:F1})");
                        }

                        lastResolutionChangeTime = Time.time;
                        consecutiveLowFrameCount = 0;
                  }
            }
            else if (currentFPS > targetFPS + 10)
            {
                  consecutiveHighFrameCount++;
                  consecutiveLowFrameCount = 0;

                  if (consecutiveHighFrameCount > 10)
                  {
                        // Улучшаем качество
                        edgeSoftness = Mathf.Min(0.1f, edgeSoftness * 1.2f);
                        edgeGlow = Mathf.Min(1f, edgeGlow * 1.2f);

                        // Сообщаем WallSegmentation о возможности увеличить разрешение
                        if (wallSegmentation != null && wallSegmentation.inputResolution.x < 512)
                        {
                              Vector2Int newResolution = new Vector2Int(
                                    Mathf.Min(512, wallSegmentation.inputResolution.x * 5 / 4),
                                    Mathf.Min(512, wallSegmentation.inputResolution.y * 5 / 4)
                              );

                              wallSegmentation.inputResolution = newResolution;
                              Debug.Log($"[SegmentationVisualizer] Increased quality to {newResolution.x}x{newResolution.y} (FPS: {currentFPS:F1})");
                        }

                        lastResolutionChangeTime = Time.time;
                        consecutiveHighFrameCount = 0;
                  }
            }
      }

      // Обновление метрик FPS
      private void UpdateFPSMetrics()
      {
            float fps = 1.0f / Time.unscaledDeltaTime;

            // Добавляем текущий FPS в массив сэмплов
            fpsSamples[fpsSampleIndex] = fps;
            fpsSampleIndex = (fpsSampleIndex + 1) % fpsSamples.Length;
      }

      // Получение среднего FPS
      private float GetAverageFPS()
      {
            float sum = 0;
            foreach (float sample in fpsSamples)
            {
                  sum += sample;
            }
            return sum / fpsSamples.Length;
      }

      // Получение оптимального формата текстуры для текущей платформы
      private RenderTextureFormat GetOptimalRenderTextureFormat()
      {
            // На iOS/Metal лучше использовать RGHalf, если поддерживается
#if UNITY_IOS
            if (SystemInfo.SupportsRenderTextureFormat(RenderTextureFormat.RGHalf))
            {
                  return RenderTextureFormat.RGHalf;
            }
#endif

            // Для других платформ используем ARGB32
            return RenderTextureFormat.ARGB32;
      }
}