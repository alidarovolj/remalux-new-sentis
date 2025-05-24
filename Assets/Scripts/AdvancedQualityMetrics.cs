using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace RemaluxAR.Optimization
{
      /// <summary>
      /// Продвинутая система метрик качества для масок сегментации
      /// Часть Спринт 4: Продвинутые оптимизации
      /// </summary>
      public class AdvancedQualityMetrics : MonoBehaviour
      {
            [Header("Quality Metrics Settings")]
            [Tooltip("Включить расширенные метрики качества")]
            public bool enableAdvancedMetrics = true;

            [Tooltip("Размер окна для анализа качества во времени")]
            [Range(5, 50)]
            public int qualityWindowSize = 20;

            [Tooltip("Минимальный порог качества для принятия маски")]
            [Range(0.1f, 1.0f)]
            public float minQualityThreshold = 0.3f;

            [Tooltip("Вес стабильности в общей оценке качества")]
            [Range(0.0f, 1.0f)]
            public float stabilityWeight = 0.3f;

            [Header("Edge Detection")]
            [Tooltip("Включить анализ качества краев")]
            public bool enableEdgeAnalysis = true;

            [Tooltip("Порог для детекции краев")]
            [Range(0.01f, 0.5f)]
            public float edgeThreshold = 0.1f;

            [Header("Consistency Tracking")]
            [Tooltip("Включить отслеживание консистентности")]
            public bool enableConsistencyTracking = true;

            [Tooltip("Максимальное отклонение для консистентности")]
            [Range(0.05f, 0.5f)]
            public float maxConsistencyDeviation = 0.15f;

            // Структура для хранения метрик качества
            [System.Serializable]
            public struct QualityMetrics
            {
                  public float overallQuality;        // Общая оценка качества [0-1]
                  public float edgeSharpness;         // Резкость краев [0-1]
                  public float consistency;           // Консистентность [0-1]
                  public float stability;             // Стабильность во времени [0-1]
                  public float coverage;              // Покрытие значимых областей [0-1]
                  public float noise;                 // Уровень шума [0-1, где 0 = нет шума]
                  public float confidenceVariance;   // Вариативность уверенности [0-1]
                  public int significantPixels;      // Количество значимых пикселей
                  public float timestamp;
                  public Vector2Int resolution;

                  public bool IsAcceptable(float threshold)
                  {
                        return overallQuality >= threshold;
                  }
            }

            // История качества для анализа стабильности
            private Queue<QualityMetrics> qualityHistory = new Queue<QualityMetrics>();
            private QualityMetrics lastMetrics;
            private float lastAnalysisTime;

            // Кеш для анализа
            private RenderTexture analysisTexture;
            private Texture2D cpuTexture;
            private Color32[] pixelBuffer;

            // Статистика
            private int totalAnalyzedFrames = 0;
            private float averageQuality = 0f;
            private int rejectedFrames = 0;

            // События
            public delegate void QualityAnalyzedHandler(QualityMetrics metrics);
            public event QualityAnalyzedHandler OnQualityAnalyzed;

            public delegate void LowQualityDetectedHandler(QualityMetrics metrics, string reason);
            public event LowQualityDetectedHandler OnLowQualityDetected;

            private void Start()
            {
                  Debug.Log("[AdvancedQualityMetrics] Инициализация системы метрик качества");
            }

            /// <summary>
            /// Анализирует качество маски сегментации
            /// </summary>
            public QualityMetrics AnalyzeMask(RenderTexture segmentationMask, bool forceAnalysis = false)
            {
                  if (!enableAdvancedMetrics || segmentationMask == null)
                  {
                        return CreateDefaultMetrics();
                  }

                  // Ограничиваем частоту анализа для производительности
                  if (!forceAnalysis && Time.realtimeSinceStartup - lastAnalysisTime < 0.1f)
                  {
                        return lastMetrics;
                  }

                  float startTime = Time.realtimeSinceStartup;
                  var metrics = PerformQualityAnalysis(segmentationMask);

                  // Обновляем историю
                  UpdateQualityHistory(metrics);
                  lastMetrics = metrics;
                  lastAnalysisTime = Time.realtimeSinceStartup;
                  totalAnalyzedFrames++;

                  // Обновляем статистику
                  averageQuality = Mathf.Lerp(averageQuality, metrics.overallQuality, 0.1f);

                  // Проверяем качество
                  if (!metrics.IsAcceptable(minQualityThreshold))
                  {
                        rejectedFrames++;
                        string reason = IdentifyQualityIssues(metrics);
                        OnLowQualityDetected?.Invoke(metrics, reason);
                  }

                  OnQualityAnalyzed?.Invoke(metrics);

                  float analysisTime = (Time.realtimeSinceStartup - startTime) * 1000f;
                  if (analysisTime > 5f) // Логируем если анализ занял больше 5мс
                  {
                        Debug.LogWarning($"[AdvancedQualityMetrics] Медленный анализ качества: {analysisTime:F1}ms");
                  }

                  return metrics;
            }

            /// <summary>
            /// Выполняет детальный анализ качества
            /// </summary>
            private QualityMetrics PerformQualityAnalysis(RenderTexture mask)
            {
                  // Подготавливаем данные для анализа
                  PrepareAnalysisData(mask);

                  var metrics = new QualityMetrics
                  {
                        timestamp = Time.realtimeSinceStartup,
                        resolution = new Vector2Int(mask.width, mask.height)
                  };

                  // 1. Анализ покрытия и значимых пикселей
                  AnalyzeCoverage(ref metrics);

                  // 2. Анализ краев если включен
                  if (enableEdgeAnalysis)
                  {
                        AnalyzeEdges(ref metrics);
                  }
                  else
                  {
                        metrics.edgeSharpness = 0.7f; // Умеренное значение по умолчанию
                  }

                  // 3. Анализ шума
                  AnalyzeNoise(ref metrics);

                  // 4. Анализ консистентности
                  if (enableConsistencyTracking && qualityHistory.Count > 0)
                  {
                        AnalyzeConsistency(ref metrics);
                  }
                  else
                  {
                        metrics.consistency = 0.8f; // Хорошая консистентность по умолчанию
                  }

                  // 5. Анализ стабильности
                  AnalyzeStability(ref metrics);

                  // 6. Вычисляем общее качество
                  CalculateOverallQuality(ref metrics);

                  return metrics;
            }

            /// <summary>
            /// Подготавливает данные для анализа
            /// </summary>
            private void PrepareAnalysisData(RenderTexture mask)
            {
                  // Создаем буферы если нужно
                  if (cpuTexture == null || cpuTexture.width != mask.width || cpuTexture.height != mask.height)
                  {
                        if (cpuTexture != null) DestroyImmediate(cpuTexture);
                        cpuTexture = new Texture2D(mask.width, mask.height, TextureFormat.ARGB32, false);
                        pixelBuffer = new Color32[mask.width * mask.height];
                  }

                  // Копируем данные в CPU
                  RenderTexture.active = mask;
                  cpuTexture.ReadPixels(new Rect(0, 0, mask.width, mask.height), 0, 0);
                  cpuTexture.Apply();
                  RenderTexture.active = null;

                  pixelBuffer = cpuTexture.GetPixels32();
            }

            /// <summary>
            /// Анализирует покрытие маски
            /// </summary>
            private void AnalyzeCoverage(ref QualityMetrics metrics)
            {
                  int significantPixels = 0;
                  float totalConfidence = 0f;
                  float confidenceVarianceSum = 0f;

                  foreach (var pixel in pixelBuffer)
                  {
                        float confidence = pixel.r / 255f; // Предполагаем, что маска в красном канале

                        if (confidence > 0.1f)
                        {
                              significantPixels++;
                              totalConfidence += confidence;
                        }

                        confidenceVarianceSum += confidence * confidence;
                  }

                  metrics.significantPixels = significantPixels;

                  float coverageRatio = (float)significantPixels / pixelBuffer.Length;
                  metrics.coverage = Mathf.Clamp01(coverageRatio * 2f); // Масштабируем чтобы 50% покрытия = 1.0

                  // Вычисляем вариативность уверенности
                  float meanConfidence = totalConfidence / pixelBuffer.Length;
                  float variance = (confidenceVarianceSum / pixelBuffer.Length) - (meanConfidence * meanConfidence);
                  metrics.confidenceVariance = Mathf.Clamp01(1f - variance); // Меньше вариативности = лучше
            }

            /// <summary>
            /// Анализирует качество краев
            /// </summary>
            private void AnalyzeEdges(ref QualityMetrics metrics)
            {
                  int sharpEdges = 0;
                  int totalEdges = 0;

                  int width = cpuTexture.width;
                  int height = cpuTexture.height;

                  // Простой оператор Собеля для детекции краев
                  for (int y = 1; y < height - 1; y++)
                  {
                        for (int x = 1; x < width - 1; x++)
                        {
                              float gx = GetPixelIntensity(x - 1, y - 1) * -1 + GetPixelIntensity(x - 1, y + 1) * 1 +
                                        GetPixelIntensity(x, y - 1) * -2 + GetPixelIntensity(x, y + 1) * 2 +
                                        GetPixelIntensity(x + 1, y - 1) * -1 + GetPixelIntensity(x + 1, y + 1) * 1;

                              float gy = GetPixelIntensity(x - 1, y - 1) * -1 + GetPixelIntensity(x + 1, y - 1) * 1 +
                                        GetPixelIntensity(x - 1, y) * -2 + GetPixelIntensity(x + 1, y) * 2 +
                                        GetPixelIntensity(x - 1, y + 1) * -1 + GetPixelIntensity(x + 1, y + 1) * 1;

                              float edgeStrength = Mathf.Sqrt(gx * gx + gy * gy);

                              if (edgeStrength > edgeThreshold)
                              {
                                    totalEdges++;
                                    if (edgeStrength > edgeThreshold * 2f)
                                    {
                                          sharpEdges++;
                                    }
                              }
                        }
                  }

                  metrics.edgeSharpness = totalEdges > 0 ? (float)sharpEdges / totalEdges : 0f;
            }

            /// <summary>
            /// Получает интенсивность пикселя
            /// </summary>
            private float GetPixelIntensity(int x, int y)
            {
                  if (x < 0 || x >= cpuTexture.width || y < 0 || y >= cpuTexture.height)
                        return 0f;

                  var pixel = pixelBuffer[y * cpuTexture.width + x];
                  return pixel.r / 255f;
            }

            /// <summary>
            /// Анализирует уровень шума
            /// </summary>
            private void AnalyzeNoise(ref QualityMetrics metrics)
            {
                  float noiseLevel = 0f;
                  int sampleCount = 0;

                  // Семплируем каждый 10-й пиксель для производительности
                  for (int i = 0; i < pixelBuffer.Length; i += 10)
                  {
                        var pixel = pixelBuffer[i];
                        float intensity = pixel.r / 255f;

                        // Проверяем соседние пиксели для детекции шума
                        if (i + 1 < pixelBuffer.Length && i + cpuTexture.width < pixelBuffer.Length)
                        {
                              float rightIntensity = pixelBuffer[i + 1].r / 255f;
                              float bottomIntensity = pixelBuffer[i + cpuTexture.width].r / 255f;

                              float variation = Mathf.Abs(intensity - rightIntensity) + Mathf.Abs(intensity - bottomIntensity);
                              noiseLevel += variation;
                              sampleCount++;
                        }
                  }

                  metrics.noise = sampleCount > 0 ? Mathf.Clamp01(noiseLevel / sampleCount) : 0f;
            }

            /// <summary>
            /// Анализирует консистентность с предыдущими кадрами
            /// </summary>
            private void AnalyzeConsistency(ref QualityMetrics metrics)
            {
                  if (qualityHistory.Count == 0)
                  {
                        metrics.consistency = 1f;
                        return;
                  }

                  var recentMetrics = qualityHistory.TakeLast(5).ToArray();
                  float consistencyScore = 1f;

                  foreach (var prevMetrics in recentMetrics)
                  {
                        float coverageDiff = Mathf.Abs(metrics.coverage - prevMetrics.coverage);
                        float edgeDiff = Mathf.Abs(metrics.edgeSharpness - prevMetrics.edgeSharpness);
                        float noiseDiff = Mathf.Abs(metrics.noise - prevMetrics.noise);

                        float frameDiff = (coverageDiff + edgeDiff + noiseDiff) / 3f;
                        if (frameDiff > maxConsistencyDeviation)
                        {
                              consistencyScore -= 0.2f;
                        }
                  }

                  metrics.consistency = Mathf.Clamp01(consistencyScore);
            }

            /// <summary>
            /// Анализирует стабильность во времени
            /// </summary>
            private void AnalyzeStability(ref QualityMetrics metrics)
            {
                  if (qualityHistory.Count < 3)
                  {
                        metrics.stability = 0.8f; // Умеренная стабильность для новых кадров
                        return;
                  }

                  var recentQualities = qualityHistory.TakeLast(qualityWindowSize / 4)
                                                      .Select(m => m.overallQuality)
                                                      .ToArray();

                  if (recentQualities.Length < 2)
                  {
                        metrics.stability = 0.8f;
                        return;
                  }

                  float mean = recentQualities.Average();
                  float variance = recentQualities.Select(q => (q - mean) * (q - mean)).Average();
                  float standardDeviation = Mathf.Sqrt(variance);

                  // Меньшее отклонение = выше стабильность
                  metrics.stability = Mathf.Clamp01(1f - (standardDeviation * 2f));
            }

            /// <summary>
            /// Вычисляет общую оценку качества
            /// </summary>
            private void CalculateOverallQuality(ref QualityMetrics metrics)
            {
                  float weightedSum = 0f;
                  float totalWeight = 0f;

                  // Покрытие (30%)
                  weightedSum += metrics.coverage * 0.3f;
                  totalWeight += 0.3f;

                  // Резкость краев (20%)
                  if (enableEdgeAnalysis)
                  {
                        weightedSum += metrics.edgeSharpness * 0.2f;
                        totalWeight += 0.2f;
                  }

                  // Уровень шума (20%, инвертированный)
                  weightedSum += (1f - metrics.noise) * 0.2f;
                  totalWeight += 0.2f;

                  // Консистентность (15%)
                  if (enableConsistencyTracking)
                  {
                        weightedSum += metrics.consistency * 0.15f;
                        totalWeight += 0.15f;
                  }

                  // Стабильность (15%)
                  weightedSum += metrics.stability * stabilityWeight;
                  totalWeight += stabilityWeight;

                  // Вариативность уверенности (остаток)
                  float remainingWeight = 1f - totalWeight;
                  if (remainingWeight > 0)
                  {
                        weightedSum += metrics.confidenceVariance * remainingWeight;
                  }

                  metrics.overallQuality = Mathf.Clamp01(weightedSum);
            }

            /// <summary>
            /// Обновляет историю качества
            /// </summary>
            private void UpdateQualityHistory(QualityMetrics metrics)
            {
                  qualityHistory.Enqueue(metrics);

                  while (qualityHistory.Count > qualityWindowSize)
                  {
                        qualityHistory.Dequeue();
                  }
            }

            /// <summary>
            /// Создает метрики по умолчанию
            /// </summary>
            private QualityMetrics CreateDefaultMetrics()
            {
                  return new QualityMetrics
                  {
                        overallQuality = 0.5f,
                        edgeSharpness = 0.5f,
                        consistency = 0.8f,
                        stability = 0.7f,
                        coverage = 0.5f,
                        noise = 0.3f,
                        confidenceVariance = 0.7f,
                        significantPixels = 0,
                        timestamp = Time.realtimeSinceStartup,
                        resolution = Vector2Int.zero
                  };
            }

            /// <summary>
            /// Идентифицирует проблемы качества
            /// </summary>
            private string IdentifyQualityIssues(QualityMetrics metrics)
            {
                  var issues = new List<string>();

                  if (metrics.coverage < 0.2f)
                        issues.Add("low coverage");
                  if (metrics.edgeSharpness < 0.3f)
                        issues.Add("blurry edges");
                  if (metrics.noise > 0.7f)
                        issues.Add("high noise");
                  if (metrics.consistency < 0.5f)
                        issues.Add("inconsistent");
                  if (metrics.stability < 0.4f)
                        issues.Add("unstable");

                  return issues.Count > 0 ? string.Join(", ", issues) : "general low quality";
            }

            /// <summary>
            /// Получает статистику качества
            /// </summary>
            public QualityStats GetQualityStats()
            {
                  float recentAverageQuality = qualityHistory.Count > 0
                      ? qualityHistory.Average(m => m.overallQuality)
                      : averageQuality;

                  float acceptanceRate = totalAnalyzedFrames > 0
                      ? 1f - ((float)rejectedFrames / totalAnalyzedFrames)
                      : 1f;

                  return new QualityStats
                  {
                        averageQuality = averageQuality,
                        recentAverageQuality = recentAverageQuality,
                        totalAnalyzedFrames = totalAnalyzedFrames,
                        rejectedFrames = rejectedFrames,
                        acceptanceRate = acceptanceRate,
                        currentStability = qualityHistory.Count > 0 ? qualityHistory.Last().stability : 0f
                  };
            }

            [System.Serializable]
            public struct QualityStats
            {
                  public float averageQuality;
                  public float recentAverageQuality;
                  public int totalAnalyzedFrames;
                  public int rejectedFrames;
                  public float acceptanceRate;
                  public float currentStability;
            }

            private void OnDestroy()
            {
                  if (cpuTexture != null)
                        DestroyImmediate(cpuTexture);
            }
      }
}