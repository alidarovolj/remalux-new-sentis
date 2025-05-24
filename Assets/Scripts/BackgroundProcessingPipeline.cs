using System.Collections;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Unity.Sentis;

namespace RemaluxAR.Optimization
{
      /// <summary>
      /// Фоновый конвейер обработки для ML инференса
      /// Часть Спринт 4: Продвинутые оптимизации
      /// </summary>
      public class BackgroundProcessingPipeline : MonoBehaviour
      {
            [Header("Pipeline Settings")]
            [Tooltip("Включить фоновую обработку")]
            public bool enableBackgroundProcessing = true;

            [Tooltip("Максимальное количество параллельных задач")]
            [Range(1, 4)]
            public int maxConcurrentTasks = 2;

            [Tooltip("Размер очереди для входящих кадров")]
            [Range(5, 20)]
            public int inputQueueSize = 10;

            [Tooltip("Таймаут обработки одного кадра (секунды)")]
            [Range(1, 10)]
            public float processingTimeoutSeconds = 5f;

            [Header("Quality Management")]
            [Tooltip("Автоматически снижать качество при высокой нагрузке")]
            public bool enableDynamicQuality = true;

            [Tooltip("Целевое время обработки для адаптации качества (мс)")]
            [Range(16, 100)]
            public float targetProcessingTimeMs = 33f; // ~30 FPS

            // Структуры для данных
            [System.Serializable]
            public class ProcessingTask
            {
                  public Texture2D inputFrame;
                  public Vector2Int targetResolution;
                  public float priority;
                  public float timestamp;
                  public string taskId;
                  public bool isHighPriority;

                  public ProcessingTask(Texture2D frame, Vector2Int resolution, float prio = 1f)
                  {
                        inputFrame = frame;
                        targetResolution = resolution;
                        priority = prio;
                        timestamp = Time.realtimeSinceStartup;
                        taskId = System.Guid.NewGuid().ToString("N")[..8];
                        isHighPriority = prio > 1.5f;
                  }
            }

            [System.Serializable]
            public class ProcessingResult
            {
                  public RenderTexture segmentationMask;
                  public float confidence;
                  public float processingTime;
                  public string taskId;
                  public bool wasFromCache;
                  public Vector2Int resolution;
            }

            // Очереди и состояние
            private ConcurrentQueue<ProcessingTask> inputQueue = new ConcurrentQueue<ProcessingTask>();
            private ConcurrentQueue<ProcessingResult> outputQueue = new ConcurrentQueue<ProcessingResult>();
            private volatile int activeTasks = 0;
            // private volatile bool isProcessing = false; // Закомментировано из-за CS0414 (не используется в текущей логике)

            // Компоненты
            private AdvancedCacheManager cacheManager;
            private WallSegmentation wallSegmentation;

            // Статистика
            private float averageProcessingTime = 33f;
            private int totalProcessedFrames = 0;
            private int droppedFrames = 0;
            private float lastQualityAdjustment = 0f;
            private Vector2Int currentDynamicResolution;

            // События
            public delegate void ProcessingCompletedHandler(ProcessingResult result);
            public event ProcessingCompletedHandler OnProcessingCompleted;

            public delegate void QueueOverflowHandler(int droppedCount);
            public event QueueOverflowHandler OnQueueOverflow;

            private void Start()
            {
                  cacheManager = FindObjectOfType<AdvancedCacheManager>();
                  wallSegmentation = FindObjectOfType<WallSegmentation>();

                  if (wallSegmentation == null)
                  {
                        Debug.LogError("[BackgroundProcessingPipeline] WallSegmentation не найден!");
                        enabled = false;
                        return;
                  }

                  currentDynamicResolution = new Vector2Int(512, 384); // Начальное разрешение

                  Debug.Log($"[BackgroundProcessingPipeline] Инициализация с {maxConcurrentTasks} параллельными задачами");

                  if (enableBackgroundProcessing)
                  {
                        StartCoroutine(ProcessingLoop());
                        StartCoroutine(ResultDeliveryLoop());
                  }
            }

            /// <summary>
            /// Добавляет кадр в очередь обработки
            /// </summary>
            public bool EnqueueFrame(Texture2D frame, Vector2Int? targetResolution = null, float priority = 1f)
            {
                  if (!enableBackgroundProcessing || frame == null)
                        return false;

                  // Проверяем, не переполнена ли очередь
                  if (GetQueueSize() >= inputQueueSize)
                  {
                        // Удаляем старые низкоприоритетные задачи
                        CleanupOldTasks();

                        if (GetQueueSize() >= inputQueueSize)
                        {
                              droppedFrames++;
                              OnQueueOverflow?.Invoke(droppedFrames);
                              Debug.LogWarning($"[BackgroundProcessingPipeline] Очередь переполнена, кадр пропущен. Всего пропущено: {droppedFrames}");
                              return false;
                        }
                  }

                  Vector2Int resolution = targetResolution ?? currentDynamicResolution;
                  var task = new ProcessingTask(frame, resolution, priority);
                  inputQueue.Enqueue(task);

                  return true;
            }

            /// <summary>
            /// Основной цикл обработки
            /// </summary>
            private IEnumerator ProcessingLoop()
            {
                  while (enableBackgroundProcessing)
                  {
                        if (activeTasks < maxConcurrentTasks && inputQueue.TryDequeue(out ProcessingTask task))
                        {
                              // Запускаем обработку в фоне
                              StartBackgroundProcessing(task);
                        }

                        // Адаптируем качество если нужно
                        if (enableDynamicQuality && Time.realtimeSinceStartup - lastQualityAdjustment > 2f)
                        {
                              AdaptQuality();
                              lastQualityAdjustment = Time.realtimeSinceStartup;
                        }

                        yield return new WaitForSeconds(0.016f); // ~60 FPS check
                  }
            }

            /// <summary>
            /// Цикл доставки результатов
            /// </summary>
            private IEnumerator ResultDeliveryLoop()
            {
                  while (enableBackgroundProcessing)
                  {
                        if (outputQueue.TryDequeue(out ProcessingResult result))
                        {
                              OnProcessingCompleted?.Invoke(result);
                              totalProcessedFrames++;
                        }

                        yield return new WaitForSeconds(0.008f); // ~120 FPS check for results
                  }
            }

            /// <summary>
            /// Запускает фоновую обработку задачи
            /// </summary>
            private async void StartBackgroundProcessing(ProcessingTask task)
            {
                  Interlocked.Increment(ref activeTasks);

                  try
                  {
                        var result = await ProcessFrameAsync(task);
                        if (result != null)
                        {
                              outputQueue.Enqueue(result);
                        }
                  }
                  catch (System.Exception e)
                  {
                        Debug.LogError($"[BackgroundProcessingPipeline] Ошибка обработки кадра {task.taskId}: {e.Message}");
                  }
                  finally
                  {
                        Interlocked.Decrement(ref activeTasks);
                  }
            }

            /// <summary>
            /// Асинхронно обрабатывает кадр
            /// </summary>
            private async Task<ProcessingResult> ProcessFrameAsync(ProcessingTask task)
            {
                  float startTime = Time.realtimeSinceStartup;

                  // Проверяем кеш сначала
                  if (cacheManager != null)
                  {
                        var cachedResult = cacheManager.FindSimilarFrame(task.inputFrame, out float similarity);
                        if (cachedResult != null && similarity > 0.8f)
                        {
                              Debug.Log($"[BackgroundProcessingPipeline] Cache hit для задачи {task.taskId}, similarity: {similarity:F2}");

                              return new ProcessingResult
                              {
                                    segmentationMask = cachedResult.segmentationMask,
                                    confidence = cachedResult.confidence,
                                    processingTime = Time.realtimeSinceStartup - startTime,
                                    taskId = task.taskId,
                                    wasFromCache = true,
                                    resolution = task.targetResolution
                              };
                        }
                  }

                  // Выполняем инференс на основном потоке (Unity требование)
                  ProcessingResult result = null;
                  bool completed = false;

                  MainThreadDispatcher.Enqueue(() =>
                  {
                        try
                        {
                              result = ProcessFrameMainThread(task, startTime);
                              completed = true;
                        }
                        catch (System.Exception e)
                        {
                              Debug.LogError($"[BackgroundProcessingPipeline] Ошибка инференса: {e.Message}");
                              completed = true;
                        }
                  });

                  // Ждем завершения с таймаутом
                  float timeoutTime = startTime + processingTimeoutSeconds;
                  while (!completed && Time.realtimeSinceStartup < timeoutTime)
                  {
                        await Task.Delay(10);
                  }

                  if (!completed)
                  {
                        Debug.LogWarning($"[BackgroundProcessingPipeline] Таймаут обработки задачи {task.taskId}");
                        return null;
                  }

                  // Добавляем в кеш если результат хороший
                  if (result != null && result.confidence > 0.5f && cacheManager != null)
                  {
                        cacheManager.AddToCache(task.inputFrame, result.segmentationMask, result.confidence);
                  }

                  return result;
            }

            /// <summary>
            /// Обрабатывает кадр на главном потоке
            /// </summary>
            private ProcessingResult ProcessFrameMainThread(ProcessingTask task, float startTime)
            {
                  // Изменяем размер если нужно
                  Texture2D processedFrame = task.inputFrame;
                  if (task.targetResolution != new Vector2Int(task.inputFrame.width, task.inputFrame.height))
                  {
                        processedFrame = ResizeTexture(task.inputFrame, task.targetResolution);
                  }

                  // Симулируем инференс (в реальности вызвали бы wallSegmentation.ProcessFrame)
                  RenderTexture segmentationMask = new RenderTexture(
                      processedFrame.width,
                      processedFrame.height,
                      0,
                      RenderTextureFormat.ARGB32
                  );
                  segmentationMask.Create();

                  // Здесь был бы реальный ML инференс
                  // Пока что создаем простую маску для демонстрации
                  var tempRT = RenderTexture.GetTemporary(processedFrame.width, processedFrame.height);
                  Graphics.Blit(processedFrame, tempRT);
                  Graphics.Blit(tempRT, segmentationMask);
                  RenderTexture.ReleaseTemporary(tempRT);

                  float processingTime = (Time.realtimeSinceStartup - startTime) * 1000f; // в мс
                  UpdateAverageProcessingTime(processingTime);

                  // Освобождаем временную текстуру если создавали
                  if (processedFrame != task.inputFrame)
                  {
                        DestroyImmediate(processedFrame);
                  }

                  return new ProcessingResult
                  {
                        segmentationMask = segmentationMask,
                        confidence = 0.8f, // Симулированная уверенность
                        processingTime = processingTime,
                        taskId = task.taskId,
                        wasFromCache = false,
                        resolution = task.targetResolution
                  };
            }

            /// <summary>
            /// Изменяет размер текстуры
            /// </summary>
            private Texture2D ResizeTexture(Texture2D source, Vector2Int targetSize)
            {
                  RenderTexture rt = RenderTexture.GetTemporary(targetSize.x, targetSize.y);
                  Graphics.Blit(source, rt);

                  RenderTexture.active = rt;
                  Texture2D result = new Texture2D(targetSize.x, targetSize.y, source.format, false);
                  result.ReadPixels(new Rect(0, 0, targetSize.x, targetSize.y), 0, 0);
                  result.Apply();

                  RenderTexture.active = null;
                  RenderTexture.ReleaseTemporary(rt);

                  return result;
            }

            /// <summary>
            /// Адаптирует качество на основе производительности
            /// </summary>
            private void AdaptQuality()
            {
                  if (averageProcessingTime > targetProcessingTimeMs * 1.5f)
                  {
                        // Снижаем разрешение
                        currentDynamicResolution = new Vector2Int(
                            Mathf.Max(256, currentDynamicResolution.x - 64),
                            Mathf.Max(192, currentDynamicResolution.y - 48)
                        );
                        Debug.Log($"[BackgroundProcessingPipeline] Снижено разрешение до {currentDynamicResolution} (время: {averageProcessingTime:F1}ms)");
                  }
                  else if (averageProcessingTime < targetProcessingTimeMs * 0.7f && currentDynamicResolution.x < 640)
                  {
                        // Повышаем разрешение
                        currentDynamicResolution = new Vector2Int(
                            Mathf.Min(640, currentDynamicResolution.x + 32),
                            Mathf.Min(480, currentDynamicResolution.y + 24)
                        );
                        Debug.Log($"[BackgroundProcessingPipeline] Повышено разрешение до {currentDynamicResolution} (время: {averageProcessingTime:F1}ms)");
                  }
            }

            /// <summary>
            /// Обновляет среднее время обработки
            /// </summary>
            private void UpdateAverageProcessingTime(float processingTime)
            {
                  averageProcessingTime = Mathf.Lerp(averageProcessingTime, processingTime, 0.1f);
            }

            /// <summary>
            /// Очищает старые задачи из очереди
            /// </summary>
            private void CleanupOldTasks()
            {
                  var tempList = new System.Collections.Generic.List<ProcessingTask>();

                  while (inputQueue.TryDequeue(out ProcessingTask task))
                  {
                        if (Time.realtimeSinceStartup - task.timestamp < 2f || task.isHighPriority)
                        {
                              tempList.Add(task);
                        }
                        else
                        {
                              droppedFrames++;
                        }
                  }

                  foreach (var task in tempList)
                  {
                        inputQueue.Enqueue(task);
                  }
            }

            /// <summary>
            /// Получает размер очереди
            /// </summary>
            public int GetQueueSize()
            {
                  return inputQueue.Count;
            }

            /// <summary>
            /// Получает статистику конвейера
            /// </summary>
            public PipelineStats GetStats()
            {
                  return new PipelineStats
                  {
                        queueSize = GetQueueSize(),
                        activeTasks = activeTasks,
                        averageProcessingTime = averageProcessingTime,
                        totalProcessedFrames = totalProcessedFrames,
                        droppedFrames = droppedFrames,
                        currentResolution = currentDynamicResolution
                  };
            }

            [System.Serializable]
            public struct PipelineStats
            {
                  public int queueSize;
                  public int activeTasks;
                  public float averageProcessingTime;
                  public int totalProcessedFrames;
                  public int droppedFrames;
                  public Vector2Int currentResolution;
            }

            private void OnDestroy()
            {
                  enableBackgroundProcessing = false;

                  // Очищаем очереди
                  while (inputQueue.TryDequeue(out _)) { }
                  while (outputQueue.TryDequeue(out ProcessingResult result))
                  {
                        if (result.segmentationMask != null)
                              result.segmentationMask.Release();
                  }
            }
      }

      /// <summary>
      /// Диспетчер для выполнения кода на главном потоке
      /// </summary>
      public class MainThreadDispatcher : MonoBehaviour
      {
            private static readonly ConcurrentQueue<System.Action> actions = new ConcurrentQueue<System.Action>();
            private static MainThreadDispatcher instance;

            private void Awake()
            {
                  if (instance == null)
                  {
                        instance = this;
                        DontDestroyOnLoad(gameObject);
                  }
                  else
                  {
                        Destroy(gameObject);
                  }
            }

            public static void Enqueue(System.Action action)
            {
                  actions.Enqueue(action);
            }

            private void Update()
            {
                  while (actions.TryDequeue(out System.Action action))
                  {
                        action?.Invoke();
                  }
            }
      }
}