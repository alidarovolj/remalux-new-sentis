using UnityEngine;
using System.Collections;
using System.IO;
using Unity.Sentis;
using UnityEngine.XR.ARFoundation;
using Unity.XR.CoreUtils;

/// <summary>
/// Компонент для диагностики проблем на iOS устройствах
/// </summary>
public class iOSDiagnostics : MonoBehaviour
{
      [Header("Диагностические настройки")]
      [Tooltip("Запускать диагностику автоматически при старте")]
      public bool runOnStart = true;

      [Tooltip("Интервал между диагностическими проверками (секунды)")]
      public float diagnosticInterval = 10f;

      private void Start()
      {
            if (runOnStart)
            {
                  StartCoroutine(RunDiagnostics());

                  if (diagnosticInterval > 0)
                  {
                        StartCoroutine(PeriodicDiagnostics());
                  }
            }
      }

      /// <summary>
      /// Запускает полную диагностику системы
      /// </summary>
      [ContextMenu("Запустить диагностику")]
      public void RunDiagnosticsManual()
      {
            StartCoroutine(RunDiagnostics());
      }

      /// <summary>
      /// Корутина для запуска диагностики
      /// </summary>
      private IEnumerator RunDiagnostics()
      {
            Debug.Log("=== 🩺 ЗАПУСК iOS ДИАГНОСТИКИ ===");

            // 1. Проверка платформы
            CheckPlatform();

            // 2. Проверка шейдеров
            CheckShaders();

            // 3. Проверка ML моделей
            CheckMLModels();

            // 4. Проверка AR системы
            yield return StartCoroutine(CheckARSystem());

            // 5. Проверка памяти
            CheckMemory();

            // 6. Проверка компонентов сцены
            CheckSceneComponents();

            Debug.Log("=== ✅ ДИАГНОСТИКА ЗАВЕРШЕНА ===");
      }

      /// <summary>
      /// Проверка платформы и устройства
      /// </summary>
      private void CheckPlatform()
      {
            Debug.Log("📱 ПРОВЕРКА ПЛАТФОРМЫ:");
            Debug.Log($"  • Платформа: {Application.platform}");
            Debug.Log($"  • iOS устройство: {Application.platform == RuntimePlatform.IPhonePlayer}");
            Debug.Log($"  • Модель устройства: {SystemInfo.deviceModel}");
            Debug.Log($"  • ОС: {SystemInfo.operatingSystem}");
            Debug.Log($"  • GPU: {SystemInfo.graphicsDeviceName}");
            Debug.Log($"  • Память GPU: {SystemInfo.graphicsMemorySize}MB");
            Debug.Log($"  • Системная память: {SystemInfo.systemMemorySize}MB");
      }

      /// <summary>
      /// Проверка доступности шейдеров
      /// </summary>
      private void CheckShaders()
      {
            Debug.Log("🎨 ПРОВЕРКА ШЕЙДЕРОВ:");

            string[] requiredShaders = new string[]
            {
            "Custom/WallPaint",
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Unlit",
            "Unlit/Transparent",
            "Unlit/Color",
            "Sprites/Default",
            "Hidden/InternalErrorShader"
            };

            foreach (string shaderName in requiredShaders)
            {
                  Shader shader = Shader.Find(shaderName);
                  bool found = shader != null;
                  string status = found ? "✅" : "❌";
                  Debug.Log($"  {status} {shaderName}: {(found ? "Найден" : "НЕ НАЙДЕН")}");
            }
      }

      /// <summary>
      /// Проверка ML моделей
      /// </summary>
      private void CheckMLModels()
      {
            Debug.Log("🤖 ПРОВЕРКА ML МОДЕЛЕЙ:");

            string streamingAssetsPath = Application.streamingAssetsPath;
            Debug.Log($"  • StreamingAssets путь: {streamingAssetsPath}");

            string[] modelFiles = new string[]
            {
            "segformer-model.sentis",
            "model.sentis",
            "model.onnx"
            };

            foreach (string modelFile in modelFiles)
            {
                  string fullPath = Path.Combine(streamingAssetsPath, modelFile);
                  bool exists = File.Exists(fullPath);
                  string status = exists ? "✅" : "❌";

                  if (exists)
                  {
                        try
                        {
                              FileInfo fileInfo = new FileInfo(fullPath);
                              Debug.Log($"  {status} {modelFile}: Найден, размер {fileInfo.Length / 1024 / 1024}MB");
                        }
                        catch (System.Exception e)
                        {
                              Debug.Log($"  ⚠️ {modelFile}: Найден, но ошибка доступа: {e.Message}");
                        }
                  }
                  else
                  {
                        Debug.Log($"  {status} {modelFile}: НЕ НАЙДЕН");
                  }
            }
      }

      /// <summary>
      /// Проверка AR системы
      /// </summary>
      private IEnumerator CheckARSystem()
      {
            Debug.Log("📡 ПРОВЕРКА AR СИСТЕМЫ:");

            // Проверка AR компонентов
            ARSession arSession = FindObjectOfType<ARSession>();
            XROrigin sessionOrigin = FindObjectOfType<XROrigin>();
            ARCameraManager arCameraManager = FindObjectOfType<ARCameraManager>();
            ARPlaneManager arPlaneManager = FindObjectOfType<ARPlaneManager>();

            Debug.Log($"  • ARSession: {(arSession != null ? "✅ Найден" : "❌ НЕ НАЙДЕН")}");
            Debug.Log($"  • ARSessionOrigin: {(sessionOrigin != null ? "✅ Найден" : "❌ НЕ НАЙДЕН")}");
            Debug.Log($"  • ARCameraManager: {(arCameraManager != null ? "✅ Найден" : "❌ НЕ НАЙДЕН")}");
            Debug.Log($"  • ARPlaneManager: {(arPlaneManager != null ? "✅ Найден" : "❌ НЕ НАЙДЕН")}");

            // Проверка состояния AR сессии
            if (arSession != null)
            {
                  Debug.Log($"  • AR Session состояние: {ARSession.state}");

                  // Ждем немного для инициализации
                  yield return new WaitForSeconds(2f);

                  Debug.Log($"  • AR Session состояние (после ожидания): {ARSession.state}");
            }

            // Проверка камеры
            if (arCameraManager != null)
            {
                  Debug.Log($"  • AR Camera enabled: {arCameraManager.enabled}");
                  Debug.Log($"  • AR Camera currentConfiguration: {arCameraManager.currentConfiguration}");
            }
      }

      /// <summary>
      /// Проверка использования памяти
      /// </summary>
      private void CheckMemory()
      {
            Debug.Log("💾 ПРОВЕРКА ПАМЯТИ:");

            long totalMemory = System.GC.GetTotalMemory(false);
            float totalMemoryMB = totalMemory / 1024f / 1024f;

            Debug.Log($"  • Использование памяти GC: {totalMemoryMB:F1}MB");
            Debug.Log($"  • Доступная системная память: {SystemInfo.systemMemorySize}MB");
            Debug.Log($"  • Видеопамять: {SystemInfo.graphicsMemorySize}MB");

            // Принудительная сборка мусора для теста
            long memoryBefore = System.GC.GetTotalMemory(false);
            System.GC.Collect();
            long memoryAfter = System.GC.GetTotalMemory(true);
            float freedMB = (memoryBefore - memoryAfter) / 1024f / 1024f;

            Debug.Log($"  • Освобождено сборкой мусора: {freedMB:F1}MB");
      }

      /// <summary>
      /// Проверка компонентов сцены
      /// </summary>
      private void CheckSceneComponents()
      {
            Debug.Log("🧩 ПРОВЕРКА КОМПОНЕНТОВ СЦЕНЫ:");

            // Проверка WallSegmentation
            WallSegmentation wallSegmentation = FindObjectOfType<WallSegmentation>();
            if (wallSegmentation != null)
            {
                  Debug.Log($"  ✅ WallSegmentation найден");
                  Debug.Log($"    • Модель инициализирована: {wallSegmentation.IsModelInitialized}");
                  Debug.Log($"    • Путь к модели: {wallSegmentation.modelPath}");
                  Debug.Log($"    • Бэкенд: {wallSegmentation.selectedBackend}");
            }
            else
            {
                  Debug.Log($"  ❌ WallSegmentation НЕ НАЙДЕН");
            }

            // Проверка ARWallPaintingSystem
            ARWallPaintingSystem wallPaintingSystem = FindObjectOfType<ARWallPaintingSystem>();
            Debug.Log($"  • ARWallPaintingSystem: {(wallPaintingSystem != null ? "✅ Найден" : "❌ НЕ НАЙДЕН")}");

            // Проверка ARManagerInitializer2
            ARManagerInitializer2 arManagerInitializer = FindObjectOfType<ARManagerInitializer2>();
            Debug.Log($"  • ARManagerInitializer2: {(arManagerInitializer != null ? "✅ Найден" : "❌ НЕ НАЙДЕН")}");

            // Проверка количества камер
            Camera[] cameras = FindObjectsOfType<Camera>();
            Debug.Log($"  • Количество камер в сцене: {cameras.Length}");

            foreach (Camera cam in cameras)
            {
                  Debug.Log($"    - {cam.name}: cullingMask={cam.cullingMask}, enabled={cam.enabled}");
            }
      }

      /// <summary>
      /// Периодическая диагностика
      /// </summary>
      private IEnumerator PeriodicDiagnostics()
      {
            yield return new WaitForSeconds(diagnosticInterval);

            while (true)
            {
                  Debug.Log("🔄 ПЕРИОДИЧЕСКАЯ ПРОВЕРКА:");

                  // Быстрая проверка состояния
                  CheckMemory();

                  WallSegmentation wallSegmentation = FindObjectOfType<WallSegmentation>();
                  if (wallSegmentation != null)
                  {
                        Debug.Log($"  • WallSegmentation модель: {(wallSegmentation.IsModelInitialized ? "✅ Инициализирована" : "❌ НЕ инициализирована")}");
                  }

                  Debug.Log($"  • AR Session: {ARSession.state}");

                  yield return new WaitForSeconds(diagnosticInterval);
            }
      }

      /// <summary>
      /// Тестирование создания материала
      /// </summary>
      [ContextMenu("Тест создания материала")]
      public void TestMaterialCreation()
      {
            Debug.Log("🧪 ТЕСТ СОЗДАНИЯ МАТЕРИАЛА:");

            string[] shaderNames = new string[]
            {
            "Custom/WallPaint",
            "Universal Render Pipeline/Lit",
            "Universal Render Pipeline/Unlit",
            "Unlit/Transparent"
            };

            foreach (string shaderName in shaderNames)
            {
                  try
                  {
                        Shader shader = Shader.Find(shaderName);
                        if (shader != null)
                        {
                              Material testMaterial = new Material(shader);
                              Debug.Log($"  ✅ Материал создан успешно с шейдером: {shaderName}");

                              // Пробуем установить некоторые свойства
                              if (testMaterial.HasProperty("_Color"))
                                    testMaterial.SetColor("_Color", Color.red);
                              if (testMaterial.HasProperty("_BaseColor"))
                                    testMaterial.SetColor("_BaseColor", Color.red);
                              if (testMaterial.HasProperty("_PaintColor"))
                                    testMaterial.SetColor("_PaintColor", Color.red);

                              DestroyImmediate(testMaterial);
                        }
                        else
                        {
                              Debug.Log($"  ❌ Шейдер не найден: {shaderName}");
                        }
                  }
                  catch (System.Exception e)
                  {
                        Debug.LogError($"  💥 Ошибка создания материала с шейдером {shaderName}: {e.Message}");
                  }
            }
      }
}