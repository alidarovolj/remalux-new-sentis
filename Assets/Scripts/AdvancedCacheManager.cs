using System.Collections.Generic;
using UnityEngine;

namespace RemaluxAR.Optimization
{
      /// <summary>
      /// Продвинутая система кеширования для ML результатов
      /// Часть Спринт 4: Продвинутые оптимизации
      /// </summary>
      public class AdvancedCacheManager : MonoBehaviour
      {
            [Header("Cache Settings")]
            [Tooltip("Максимальный размер кеша в МБ")]
            [Range(10, 200)]
            public int maxCacheSizeMB = 50;

            [Tooltip("Время жизни кеша в секундах")]
            [Range(5, 300)]
            public float cacheLifetimeSeconds = 60f;

            [Tooltip("Включить кеширование на основе сходства кадров")]
            public bool enableSimilarityBasedCaching = true;

            [Tooltip("Порог сходства для переиспользования кеша (0-1)")]
            [Range(0.7f, 0.99f)]
            public float similarityThreshold = 0.85f;

            // Структура для хранения кешированного результата
            [System.Serializable]
            public class CachedResult
            {
                  public Texture2D inputFrame;
                  public RenderTexture segmentationMask;
                  public float timestamp;
                  public float confidence;
                  public Vector2Int resolution;
                  public string frameHash;
                  public float lastAccessTime;
                  public int accessCount;

                  public bool IsExpired(float currentTime, float lifetime)
                  {
                        return (currentTime - timestamp) > lifetime;
                  }
            }

            // Кеш результатов
            private Dictionary<string, CachedResult> cache = new Dictionary<string, CachedResult>();

            // Статистика
            private int cacheHits = 0;
            private int cacheMisses = 0;
            private int totalRequests = 0;
            // private float totalCacheSizeBytes = 0f; // Закомментировано из-за CS0414 (не используется в текущей логике)

            private void Start()
            {
                  Debug.Log($"[AdvancedCacheManager] Инициализация с максимальным размером кеша: {maxCacheSizeMB}MB");

                  // Запускаем периодическую очистку кеша
                  InvokeRepeating(nameof(CleanupExpiredEntries), 10f, 10f);
            }

            /// <summary>
            /// Ищет похожий кадр в кеше
            /// </summary>
            public CachedResult FindSimilarFrame(Texture2D inputFrame, out float similarity)
            {
                  similarity = 0f;
                  totalRequests++;

                  if (inputFrame == null || cache.Count == 0)
                  {
                        cacheMisses++;
                        return null;
                  }

                  string currentFrameHash = CalculateFrameHash(inputFrame);

                  // Прямое попадание
                  if (cache.ContainsKey(currentFrameHash))
                  {
                        var directHit = cache[currentFrameHash];
                        if (!directHit.IsExpired(Time.realtimeSinceStartup, cacheLifetimeSeconds))
                        {
                              UpdateAccessInfo(currentFrameHash);
                              similarity = 1.0f;
                              cacheHits++;
                              return directHit;
                        }
                        else
                        {
                              RemoveFromCache(currentFrameHash);
                        }
                  }

                  cacheMisses++;
                  return null;
            }

            /// <summary>
            /// Добавляет результат в кеш
            /// </summary>
            public void AddToCache(Texture2D inputFrame, RenderTexture segmentationMask, float confidence)
            {
                  if (inputFrame == null || segmentationMask == null) return;

                  string frameHash = CalculateFrameHash(inputFrame);

                  // Создаем копию текстур для кеша
                  Texture2D cachedFrame = new Texture2D(inputFrame.width, inputFrame.height, inputFrame.format, false);
                  Graphics.CopyTexture(inputFrame, cachedFrame);

                  RenderTexture cachedMask = new RenderTexture(segmentationMask.width, segmentationMask.height, 0, segmentationMask.format);
                  cachedMask.Create();
                  Graphics.Blit(segmentationMask, cachedMask);

                  var cachedResult = new CachedResult
                  {
                        inputFrame = cachedFrame,
                        segmentationMask = cachedMask,
                        timestamp = Time.realtimeSinceStartup,
                        confidence = confidence,
                        resolution = new Vector2Int(inputFrame.width, inputFrame.height),
                        frameHash = frameHash,
                        lastAccessTime = Time.realtimeSinceStartup,
                        accessCount = 1
                  };

                  cache[frameHash] = cachedResult;
                  Debug.Log($"[AdvancedCacheManager] Добавлен кадр в кеш: {frameHash}");
            }

            /// <summary>
            /// Вычисляет хеш кадра
            /// </summary>
            private string CalculateFrameHash(Texture2D frame)
            {
                  // Упрощенный хеш на основе размера и нескольких пикселей
                  var pixels = frame.GetPixels32();
                  int hash = frame.width.GetHashCode() ^ frame.height.GetHashCode();

                  // Семплируем каждый 100-й пиксель для хеша
                  for (int i = 0; i < pixels.Length; i += 100)
                  {
                        var pixel = pixels[i];
                        hash ^= pixel.r << 16 | pixel.g << 8 | pixel.b;
                  }

                  return hash.ToString("X8");
            }

            /// <summary>
            /// Обновляет информацию о доступе к кешу
            /// </summary>
            private void UpdateAccessInfo(string frameHash)
            {
                  if (cache.ContainsKey(frameHash))
                  {
                        var entry = cache[frameHash];
                        entry.lastAccessTime = Time.realtimeSinceStartup;
                        entry.accessCount++;
                  }
            }

            /// <summary>
            /// Удаляет запись из кеша
            /// </summary>
            private void RemoveFromCache(string frameHash)
            {
                  if (cache.ContainsKey(frameHash))
                  {
                        var entry = cache[frameHash];

                        if (entry.inputFrame != null)
                              DestroyImmediate(entry.inputFrame);
                        if (entry.segmentationMask != null)
                              entry.segmentationMask.Release();

                        cache.Remove(frameHash);
                  }
            }

            /// <summary>
            /// Очищает просроченные записи
            /// </summary>
            private void CleanupExpiredEntries()
            {
                  var expiredKeys = new List<string>();
                  float currentTime = Time.realtimeSinceStartup;

                  foreach (var kvp in cache)
                  {
                        if (kvp.Value.IsExpired(currentTime, cacheLifetimeSeconds))
                        {
                              expiredKeys.Add(kvp.Key);
                        }
                  }

                  foreach (var key in expiredKeys)
                  {
                        RemoveFromCache(key);
                  }

                  if (expiredKeys.Count > 0)
                  {
                        Debug.Log($"[AdvancedCacheManager] Очищено {expiredKeys.Count} просроченных записей кеша");
                  }
            }

            /// <summary>
            /// Очищает весь кеш
            /// </summary>
            public void ClearCache()
            {
                  foreach (var kvp in cache)
                  {
                        if (kvp.Value.inputFrame != null)
                              DestroyImmediate(kvp.Value.inputFrame);
                        if (kvp.Value.segmentationMask != null)
                              kvp.Value.segmentationMask.Release();
                  }

                  cache.Clear();
                  // totalCacheSizeBytes = 0f; // Также закомментировано, так как поле закомментировано выше

                  Debug.Log("[AdvancedCacheManager] Кеш полностью очищен");
            }

            private void OnDestroy()
            {
                  ClearCache();
            }
      }
}