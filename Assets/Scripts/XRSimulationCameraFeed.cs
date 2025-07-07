using UnityEngine;
using UnityEngine.XR;
using System.Collections;

/// <summary>
/// Компонент для получения изображений камеры в режиме XR Simulation
/// Решает критическую проблему отсутствия CPU-изображений в симуляторе
/// </summary>
public class XRSimulationCameraFeed : MonoBehaviour
{
      [Header("🎥 Настройки камеры симуляции")]
      [SerializeField] private Camera simulationCamera;
      [SerializeField] private int feedWidth = 640;
      [SerializeField] private int feedHeight = 480;
      [SerializeField] private int targetDepth = 16;

      [Header("🔧 Настройки производительности")]
      [SerializeField] private bool useAsyncReadback = true;
      [SerializeField] private float updateInterval = 0.1f; // 10 FPS для экономии ресурсов

      [Header("🐛 Отладка")]
      [SerializeField] private bool enableDebugLogging = false;

      private RenderTexture cameraFeedTexture;
      private Texture2D currentFrame;
      private bool isInitialized = false;
      private float lastUpdateTime;

      // События для интеграции с WallSegmentation
      public System.Action<Texture2D> OnFrameReady;

      private void Start()
      {
            InitializeCameraFeed();
      }

      private void InitializeCameraFeed()
      {
            try
            {
                  // Автоматический поиск камеры если не задана
                  if (simulationCamera == null)
                  {
                        FindSimulationCamera();
                  }

                  if (simulationCamera == null)
                  {
                        Debug.LogError("❌ XRSimulationCameraFeed: Не удалось найти камеру симуляции!");
                        return;
                  }

                  // Создаём RenderTexture для камеры симуляции
                  cameraFeedTexture = new RenderTexture(feedWidth, feedHeight, targetDepth);
                  cameraFeedTexture.format = RenderTextureFormat.ARGB32;
                  cameraFeedTexture.Create();

                  // Настраиваем камеру
                  simulationCamera.targetTexture = cameraFeedTexture;

                  // Создаём Texture2D для результата
                  currentFrame = new Texture2D(feedWidth, feedHeight, TextureFormat.RGB24, false);

                  isInitialized = true;

                  if (enableDebugLogging)
                  {
                        Debug.Log($"✅ XRSimulationCameraFeed инициализирован: {feedWidth}x{feedHeight}, камера: {simulationCamera.name}");
                  }
            }
            catch (System.Exception ex)
            {
                  Debug.LogError($"❌ Ошибка инициализации XRSimulationCameraFeed: {ex.Message}");
            }
      }

      private void FindSimulationCamera()
      {
            // Поиск среди всех камер
            Camera[] cameras = FindObjectsOfType<Camera>();

            foreach (Camera cam in cameras)
            {
                  // Ищем основную камеру или камеру AR
                  if (cam.name.Contains("Main Camera") ||
                      cam.name.Contains("AR Camera") ||
                      cam.name.Contains("XR Camera") ||
                      cam.CompareTag("MainCamera"))
                  {
                        simulationCamera = cam;
                        if (enableDebugLogging)
                        {
                              Debug.Log($"🔍 Найдена камера симуляции: {cam.name}");
                        }
                        break;
                  }
            }

            // Fallback - берём первую активную камеру
            if (simulationCamera == null && cameras.Length > 0)
            {
                  simulationCamera = cameras[0];
                  if (enableDebugLogging)
                  {
                        Debug.Log($"🔍 Использую fallback камеру: {simulationCamera.name}");
                  }
            }
      }

      private void Update()
      {
            if (!isInitialized) return;

            // Обновляем с заданным интервалом
            if (Time.time - lastUpdateTime >= updateInterval)
            {
                  lastUpdateTime = Time.time;

                  if (useAsyncReadback)
                  {
                        CaptureFrameAsync();
                  }
                  else
                  {
                        CaptureFrameSync();
                  }
            }
      }

      private void CaptureFrameAsync()
      {
            if (cameraFeedTexture == null || !cameraFeedTexture.IsCreated()) return;

            try
            {
                  // Используем AsyncGPUReadback для избежания блокировок
                  UnityEngine.Rendering.AsyncGPUReadback.Request(cameraFeedTexture, 0, TextureFormat.RGB24, OnAsyncReadbackComplete);
            }
            catch (System.Exception ex)
            {
                  if (enableDebugLogging)
                  {
                        Debug.LogWarning($"⚠️ AsyncGPUReadback не удался, переключаюсь на синхронный режим: {ex.Message}");
                  }
                  useAsyncReadback = false;
            }
      }

      private void OnAsyncReadbackComplete(UnityEngine.Rendering.AsyncGPUReadbackRequest request)
      {
            if (request.hasError)
            {
                  if (enableDebugLogging)
                  {
                        Debug.LogWarning("⚠️ AsyncGPUReadback завершён с ошибкой");
                  }
                  return;
            }

            try
            {
                  // Получаем данные и загружаем в Texture2D
                  var data = request.GetData<byte>();
                  if (currentFrame != null && data.Length > 0)
                  {
                        currentFrame.LoadRawTextureData(data);
                        currentFrame.Apply(false);

                        // Уведомляем подписчиков о готовности кадра
                        OnFrameReady?.Invoke(currentFrame);

                        if (enableDebugLogging)
                        {
                              Debug.Log($"📸 Кадр готов (асинхронно): {currentFrame.width}x{currentFrame.height}");
                        }
                  }
            }
            catch (System.Exception ex)
            {
                  Debug.LogError($"❌ Ошибка обработки AsyncGPUReadback: {ex.Message}");
            }
      }

      private void CaptureFrameSync()
      {
            if (cameraFeedTexture == null || !cameraFeedTexture.IsCreated()) return;

            try
            {
                  // Синхронное чтение (fallback метод)
                  RenderTexture.active = cameraFeedTexture;
                  currentFrame.ReadPixels(new Rect(0, 0, feedWidth, feedHeight), 0, 0);
                  currentFrame.Apply(false);
                  RenderTexture.active = null;

                  // Уведомляем подписчиков о готовности кадра
                  OnFrameReady?.Invoke(currentFrame);

                  if (enableDebugLogging)
                  {
                        Debug.Log($"📸 Кадр готов (синхронно): {currentFrame.width}x{currentFrame.height}");
                  }
            }
            catch (System.Exception ex)
            {
                  Debug.LogError($"❌ Ошибка синхронного чтения кадра: {ex.Message}");
            }
      }

      /// <summary>
      /// Получить текущий кадр камеры
      /// </summary>
      /// <returns>Texture2D с текущим кадром или null</returns>
      public Texture2D GetCurrentFrame()
      {
            if (!isInitialized || currentFrame == null)
            {
                  if (enableDebugLogging)
                  {
                        Debug.LogWarning("⚠️ XRSimulationCameraFeed не инициализирован или кадр не готов");
                  }
                  return null;
            }

            return currentFrame;
      }

      /// <summary>
      /// Проверка, работает ли система в режиме XR Simulation
      /// </summary>
      /// <returns>true если XR Simulation активна</returns>
      public static bool IsXRSimulationMode()
      {
#if UNITY_EDITOR
        return XRSettings.loadedDeviceName == "Mock HMD" || 
               XRSettings.loadedDeviceName == "MockHMD" ||
               XRSettings.loadedDeviceName.Contains("Simulation");
#else
            return false;
#endif
      }

      /// <summary>
      /// Получить статистику работы системы
      /// </summary>
      /// <returns>Строка с информацией о состоянии</returns>
      public string GetStatusInfo()
      {
            if (!isInitialized)
                  return "❌ Не инициализирован";

            return $"✅ Активен | Камера: {simulationCamera?.name ?? "не найдена"} | " +
                   $"Размер: {feedWidth}x{feedHeight} | " +
                   $"Режим: {(useAsyncReadback ? "Асинхронный" : "Синхронный")} | " +
                   $"XR Simulation: {IsXRSimulationMode()}";
      }

      private void OnDestroy()
      {
            // Очистка ресурсов
            if (cameraFeedTexture != null)
            {
                  if (simulationCamera != null)
                  {
                        simulationCamera.targetTexture = null;
                  }
                  cameraFeedTexture.Release();
                  cameraFeedTexture = null;
            }

            if (currentFrame != null)
            {
                  DestroyImmediate(currentFrame);
                  currentFrame = null;
            }

            if (enableDebugLogging)
            {
                  Debug.Log("🧹 XRSimulationCameraFeed: Ресурсы очищены");
            }
      }

      // Методы для отладки
      [System.Diagnostics.Conditional("UNITY_EDITOR")]
      private void OnGUI()
      {
            if (!enableDebugLogging) return;

            GUI.Label(new Rect(10, 10, 400, 60),
                     $"XRSimulationCameraFeed\n{GetStatusInfo()}\nПоследний кадр: {(currentFrame != null ? "✅" : "❌")}");
      }
}