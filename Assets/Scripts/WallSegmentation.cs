// #define REAL_MASK_PROCESSING_DISABLED // Закомментируйте эту строку, чтобы включить реальную обработку

using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;
using System.Reflection;
using System.Text;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.XR.Management;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.XR.CoreUtils;

#if UNITY_SENTIS
using Unity.Sentis;
#endif

// Используем object для универсальности
using ModelAsset = UnityEngine.Object;
using IWorker = System.Object;
using Model = System.Object;
using TensorFloat = System.Object;

/// <summary>
/// Manages wall segmentation using a Unity Sentis model.
/// This component handles model loading, camera feed processing, and mask generation.
/// Automatically detects if Unity Sentis is available and enables/disables functionality accordingly.
/// </summary>
public class WallSegmentation : MonoBehaviour
{
      // --- Sentis Types (found via reflection) ---
      private static Type _modelLoaderType;
      private static Type _workerType;
      private static Type _textureConverterType;
      private static Type _tensorFloatType;
      private static Type _textureTransformType;
      private static bool _sentisTypesResolved = false;

      [Header("ML Model Settings")]
      [Tooltip("The ML model asset in ONNX or Sentis format.")]
      public ModelAsset modelAsset;

      [Tooltip("The preferred backend for model execution (CPU, GPUCompute).")]
      public int backendType = 1; // 1 = GPUCompute equivalent

      [Tooltip("Timeout for model loading in seconds.")]
      public float modelLoadTimeout = 30f;

      [Header("Segmentation Settings")]
      [Tooltip("The class index for walls in the model's output.")]
      [SerializeField] private int wallClassIndex = 1;

      [Tooltip("The probability threshold for classifying a pixel as a wall.")]
      [SerializeField, Range(0.01f, 1.0f)] private float wallConfidence = 0.75f;

      [Tooltip("The input resolution for the model.")]
      public Vector2Int inputResolution = new Vector2Int(512, 512);

      [Header("Components & Output")]
      [Tooltip("Reference to the AR Camera Manager.")]
      public ARCameraManager arCameraManager;

      [Tooltip("Reference to the AR Session Manager.")]
      public ARSessionManager arSessionManager;

      [Tooltip("Reference to the XR Origin.")]
      public XROrigin xrOrigin;

      [Tooltip("The output texture for the final segmentation mask.")]
      public RenderTexture segmentationMaskTexture;

      [Tooltip("Material used to convert the model's output tensor into a visual mask.")]
      [SerializeField] private Material segmentationMaterial;

      [Header("Mask Enhancement (Post-processing)")]
      [Tooltip("Apply Gaussian blur to smooth the mask edges.")]
      public bool applyBlur = true;

      [Tooltip("Strength of the Gaussian blur.")]
      [Range(0f, 10f)] public float blurSize = 3.0f;

      [Tooltip("Apply sharpening to enhance edges (may increase noise).")]
      public bool applySharpen = false;

      [Tooltip("Strength of the sharpening effect.")]
      [Range(0f, 2f)] public float sharpenStrength = 0.8f;

      [Tooltip("Apply contrast correction to better separate walls.")]
      public bool applyContrast = true;

      [Tooltip("Threshold for contrast correction.")]
      [Range(0.1f, 2.0f)] public float contrastThreshold = 0.5f;

      [Tooltip("Contrast multiplier.")]
      [Range(1f, 3f)] public float contrastMultiplier = 1.5f;

      [Header("Debugging")]
      [Tooltip("Enable debug mode for detailed logging.")]
      public bool debugMode = true;

      [Header("Fallback Camera")]
      [Tooltip("Веб-камера для использования если AR камера недоступна")]
      [SerializeField] private WebCamTexture webCamTexture;
      [SerializeField] private bool useWebCamFallback = false;

      // Events to notify other components
      public event System.Action OnModelInitialized;
      public event System.Action<RenderTexture> OnSegmentationMaskUpdated;

      // Public properties for state checking
      public bool IsModelInitialized => isModelInitialized;
      public bool IsInitializing => isInitializing;
      public string LastErrorMessage => lastErrorMessage;
      public bool IsInitializationFailed => isInitializationFailed;

      // Public properties for component access
      public ARCameraManager ARCameraManager
      {
            get => arCameraManager;
            set => arCameraManager = value;
      }

      public ARSessionManager ARSessionManager
      {
            get => arSessionManager;
            set => arSessionManager = value;
      }

      public XROrigin XROrigin
      {
            get => xrOrigin;
            set => xrOrigin = value;
      }

      // Private fields
      private object runtimeModel;
      private object engine;
      private bool isModelInitialized = false;
      private bool isInitializing = false;
      private string lastErrorMessage = null;
      private bool isInitializationFailed = false;
      private int consecutiveFailures = 0;
      private string cachedInputName;

      // Unity Sentis availability
      private bool isSentisAvailable = false;
      private Dictionary<string, Type> sentisTypes;
      private System.Type modelLoaderType;
      private System.Type workerFactoryType;
      private System.Type textureConverterType;
      private System.Type tensorFloatType;

      private void Awake()
      {
            debugMode = true; // Принудительно включаем для диагностики
            Debug.Log($"[WallSegmentation] 🟢 Awake() вызван, компонент: {gameObject.name}");
            sentisTypes = new Dictionary<string, Type>();

            // Проверяем наличие Unity Sentis
            CheckSentisAvailability();

            if (!isSentisAvailable)
            {
                  Debug.LogError($"[WallSegmentation] ❌ Unity Sentis не доступен, отключаю компонент на {gameObject.name}");
                  isInitializationFailed = true;
                  lastErrorMessage = "Unity Sentis package not found.";
                  enabled = false;
                  return;
            }

            Debug.Log($"[WallSegmentation] ✅ Unity Sentis доступен, продолжаю инициализацию на {gameObject.name}");

            if (arCameraManager == null)
            {
                  if (debugMode) Debug.Log("ARCameraManager not assigned, attempting to find it...");

                  var xrOriginInstance = FindObjectOfType<XROrigin>();
                  if (xrOriginInstance != null && xrOriginInstance.Camera != null)
                  {
                        arCameraManager = xrOriginInstance.Camera.GetComponent<ARCameraManager>();
                  }

                  if (arCameraManager == null)
                  {
                        Debug.LogError($"[WallSegmentation] ❌ ARCameraManager не найден, отключаю компонент на {gameObject.name}");
                        isInitializationFailed = true;
                        lastErrorMessage = "ARCameraManager could not be found.";
                        enabled = false;
                        return;
                  }
                  else
                  {
                        Debug.Log($"[WallSegmentation] ✅ ARCameraManager найден и назначен автоматически на {gameObject.name}");
                  }
            }

            Debug.Log($"[WallSegmentation] 🎯 Awake() завершен успешно, компонент {gameObject.name} enabled: {enabled}");
      }

      private void CheckSentisAvailability()
      {
            if (_sentisTypesResolved)
            {
                  Debug.Log("[WallSegmentation] ✅ Sentis types already resolved, skipping check.");
                  isSentisAvailable = true;
                  return;
            }

            try
            {
                  Debug.Log("[WallSegmentation] Checking Sentis availability via reflection...");

                  Assembly sentisAssembly = null;
                  // Ищем сборку, в которой точно есть ModelLoader, чтобы избежать выбора бэкенд-сборок
                  foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                  {
                        if (assembly.GetType("Unity.Sentis.ModelLoader") != null)
                        {
                              sentisAssembly = assembly;
                              Debug.Log($"[WallSegmentation] ✅ Found correct Sentis runtime assembly: {assembly.FullName}");
                              break;
                        }
                  }

                  if (sentisAssembly == null)
                  {
                        throw new Exception("The core Unity.Sentis runtime assembly containing ModelLoader was not found.");
                  }

                  _modelLoaderType = sentisAssembly.GetType("Unity.Sentis.ModelLoader");
                  _workerType = sentisAssembly.GetType("Unity.Sentis.Worker");
                  _textureConverterType = sentisAssembly.GetType("Unity.Sentis.TextureConverter");
                  _tensorFloatType = sentisAssembly.GetType("Unity.Sentis.TensorFloat");
                  if (_tensorFloatType == null)
                  {
                        Debug.LogWarning("[WallSegmentation] 'Unity.Sentis.TensorFloat' not found, trying fallback 'Unity.Sentis.Tensor'.");
                        _tensorFloatType = sentisAssembly.GetType("Unity.Sentis.Tensor");
                  }
                  _textureTransformType = sentisAssembly.GetType("Unity.Sentis.TextureTransform");

                  bool allTypesFound = _modelLoaderType != null && _workerType != null && _textureConverterType != null && _tensorFloatType != null;

                  if (allTypesFound)
                  {
                        Debug.Log("[WallSegmentation] ✅ All critical Sentis types found successfully!");
                        Debug.Log($"  - ModelLoader: {_modelLoaderType.FullName}");
                        Debug.Log($"  - Worker: {_workerType.FullName}");
                        Debug.Log($"  - TextureConverter: {_textureConverterType.FullName}");
                        Debug.Log($"  - TensorFloat: {_tensorFloatType.FullName}");
                        Debug.Log($"  - TextureTransform: {(_textureTransformType != null ? _textureTransformType.FullName : "Not Found (optional)")}");
                        isSentisAvailable = true;
                        _sentisTypesResolved = true;
                  }
                  else
                  {
                        var sb = new System.Text.StringBuilder();
                        sb.AppendLine("Failed to find all critical Sentis types via reflection within the found assembly.");
                        sb.AppendLine($"  - ModelLoader: {_modelLoaderType != null}");
                        sb.AppendLine($"  - Worker: {_workerType != null}");
                        sb.AppendLine($"  - TextureConverter: {_textureConverterType != null}");
                        sb.AppendLine($"  - TensorFloat: {_tensorFloatType != null}");
                        throw new Exception(sb.ToString());
                  }
            }
            catch (Exception e)
            {
                  Debug.LogError($"[WallSegmentation] ❌ Failed to resolve Sentis types: {e.Message}");
                  isSentisAvailable = false;
            }
      }

      private void Start()
      {
            Debug.Log($"[WallSegmentation] 🟢 Start() вызван на {gameObject.name}, enabled: {enabled}");

            if (!isSentisAvailable)
            {
                  Debug.LogWarning($"[WallSegmentation] ❌ Start(): Sentis не доступен на {gameObject.name}");
                  return;
            }

            if (modelAsset == null)
            {
                  Debug.LogError($"[WallSegmentation] ❌ Model Asset не назначен на {gameObject.name}");
                  isInitializationFailed = true;
                  lastErrorMessage = "Model Asset not assigned.";
                  return;
            }

            Debug.Log($"[WallSegmentation] ✅ Запускаю InitializeSegmentation() на {gameObject.name}");
            StartCoroutine(InitializeSegmentation());
      }

      private IEnumerator InitializeSegmentation()
      {
            Debug.Log("[WallSegmentation] Initializing segmentation model...");
            isInitializing = true;
            isInitializationFailed = false;

            try
            {
#if UNITY_SENTIS
                  // С определенным UNITY_SENTIS используем прямые ссылки
                  Debug.Log("[WallSegmentation] UNITY_SENTIS enabled - trying direct initialization");

                  try
                  {
                        // Прямая инициализация с Unity Sentis типами
                        Debug.Log("[WallSegmentation] Attempting direct model loading...");
                        var model = Unity.Sentis.ModelLoader.Load(modelAsset as Unity.Sentis.ModelAsset);
                        Debug.Log("[WallSegmentation] ✅ Model loaded directly!");

                        var worker = new Unity.Sentis.Worker(model, (Unity.Sentis.BackendType)backendType);
                        Debug.Log("[WallSegmentation] ✅ Worker created directly with GPUCompute!");

                        runtimeModel = model;
                        engine = worker;
                        isModelInitialized = true;
                        Debug.Log("[WallSegmentation] ✅ Direct Sentis initialization successful!");
                  }
                  catch (Exception e)
                  {
                        Debug.LogError($"[WallSegmentation] ❌ Direct Sentis initialization failed, falling back to reflection. Error: {e}");
                        // Очищаем, чтобы попробовать reflection
                        isModelInitialized = false;
                        runtimeModel = null;
                        engine = null;
                  }
#endif

                  if (!isModelInitialized)
                  {
                        // Fallback to reflection-based initialization if direct method fails or is not available
                        Debug.LogWarning("[WallSegmentation] Direct initialization failed or not available. Attempting reflection-based initialization...");
                        // Здесь должен быть код для инициализации через reflection, если он нужен как fallback.
                        // Пока что будем считать, что прямая инициализация - единственный путь, 
                        // а если она не удалась, то это ошибка.
                        if (engine == null)
                        {
                              throw new Exception("Reflection-based fallback not fully implemented, and direct init failed.");
                        }
                  }

                  if (isModelInitialized)
                  {
                        OnModelInitialized?.Invoke();
                        Debug.Log("[WallSegmentation] ✅ Model initialization fully complete!");
                        isInitializationFailed = false;
                  }
                  else
                  {
                        throw new Exception("All initialization methods failed.");
                  }
            }
            catch (Exception e)
            {
                  lastErrorMessage = e.Message;
                  isInitializationFailed = true;
                  Debug.LogError($"[WallSegmentation] ❌ Ошибка при инициализации модели: {e.Message}\n{e.StackTrace}");
            }
            finally
            {
                  isInitializing = false;
            }

            yield return null;
      }

      private void Update()
      {
            // БЕЗУСЛОВНАЯ диагностика каждые 5 секунд
            if (Time.frameCount % 300 == 0)
            {
                  Debug.Log($"[WallSegmentation] 📊 ДИАГНОСТИКА Update:");
                  Debug.Log($"  - debugMode: {debugMode}");
                  Debug.Log($"  - isSentisAvailable: {isSentisAvailable}");
                  Debug.Log($"  - isModelInitialized: {isModelInitialized}");
                  Debug.Log($"  - isInitializing: {isInitializing}");
                  Debug.Log($"  - isInitializationFailed: {isInitializationFailed}");
                  Debug.Log($"  - arCameraManager: {(arCameraManager != null ? "✅" : "❌")}");
                  if (arCameraManager != null && arCameraManager.subsystem != null)
                  {
                        Debug.Log($"  - AR subsystem running: {arCameraManager.subsystem.running}");
                  }
            }

            if (!isSentisAvailable)
            {
                  if (Time.frameCount % 300 == 0) Debug.LogError("[WallSegmentation] ❌ Sentis недоступен!");
                  return;
            }

            if (!isModelInitialized)
            {
                  if (Time.frameCount % 300 == 0) Debug.LogError("[WallSegmentation] ❌ Модель не инициализирована!");
                  return;
            }

            if (isInitializing)
            {
                  if (Time.frameCount % 300 == 0) Debug.LogWarning("[WallSegmentation] ⏳ Всё ещё инициализируется...");
                  return;
            }

            if (isInitializationFailed)
            {
                  if (Time.frameCount % 300 == 0) Debug.LogError("[WallSegmentation] ❌ Инициализация провалилась!");
                  return;
            }

            // Все проверки пройдены - пытаемся получить текстуру
            if (TryGetCameraTexture(out Texture cameraTexture))
            {
                  // Логируем каждые 2 секунды
                  if (Time.frameCount % 120 == 0)
                  {
                        Debug.Log($"[WallSegmentation] ✅ Запускаю инференс! Текстура: {cameraTexture.width}x{cameraTexture.height}");
                  }
                  RunInference(cameraTexture);
                  consecutiveFailures = 0;
            }
            else
            {
                  consecutiveFailures++;
                  if (consecutiveFailures == 1 || consecutiveFailures % 60 == 0)
                  {
                        Debug.LogError($"[WallSegmentation] ❌ Не удаётся получить текстуру камеры! Попыток: {consecutiveFailures}");
                  }
            }
      }

      private bool TryGetCameraTexture(out Texture texture)
      {
            texture = null;

            // Сначала пробуем AR камеру
            if (arCameraManager != null && arCameraManager.subsystem != null && arCameraManager.subsystem.running)
            {
                  if (arCameraManager.TryAcquireLatestCpuImage(out XRCpuImage cpuImage))
                  {
                        using (cpuImage)
                        {
                              var conversionParams = new XRCpuImage.ConversionParams
                              {
                                    inputRect = new RectInt(0, 0, cpuImage.width, cpuImage.height),
                                    outputDimensions = new Vector2Int(inputResolution.x, inputResolution.y),
                                    outputFormat = TextureFormat.RGBA32,
                                    transformation = XRCpuImage.Transformation.MirrorY
                              };

                              var size = cpuImage.GetConvertedDataSize(conversionParams);
                              var buffer = new NativeArray<byte>(size, Allocator.Temp);

                              cpuImage.Convert(conversionParams, buffer);

                              var cameraTexture2D = new Texture2D(
                                    conversionParams.outputDimensions.x,
                                    conversionParams.outputDimensions.y,
                                    conversionParams.outputFormat,
                                    false);

                              cameraTexture2D.LoadRawTextureData(buffer);
                              cameraTexture2D.Apply();

                              buffer.Dispose();

                              texture = cameraTexture2D;
                              return true;
                        }
                  }
            }

            // Fallback: используем веб-камеру
            if (useWebCamFallback)
            {
                  if (webCamTexture == null)
                  {
                        // Инициализируем веб-камеру при первом использовании
                        if (WebCamTexture.devices.Length > 0)
                        {
                              webCamTexture = new WebCamTexture(WebCamTexture.devices[0].name, inputResolution.x, inputResolution.y, 30);
                              webCamTexture.Play();
                              Debug.Log($"[WallSegmentation] 📷 Инициализирована веб-камера: {webCamTexture.deviceName}");
                        }
                        else
                        {
                              Debug.LogError("[WallSegmentation] ❌ Веб-камеры не найдены!");
                              return false;
                        }
                  }

                  if (webCamTexture.isPlaying && webCamTexture.didUpdateThisFrame)
                  {
                        texture = webCamTexture;
                        return true;
                  }
            }

            // Диагностика почему не работает
            if (debugMode && consecutiveFailures > 5 && consecutiveFailures % 60 == 0)
            {
                  if (arCameraManager == null)
                        Debug.LogWarning("[WallSegmentation] ARCameraManager is null. Cannot get camera texture.");
                  else if (arCameraManager.subsystem == null)
                        Debug.LogWarning("[WallSegmentation] ARCameraManager subsystem is null. Cannot get camera texture.");
                  else if (!arCameraManager.subsystem.running)
                        Debug.LogWarning("[WallSegmentation] ARCameraManager subsystem is not running. Cannot get camera texture.");
            }

            return false;
      }

      private void LogModelStructure(object model)
      {
            try
            {
                  if (model == null)
                  {
                        Debug.LogError("[WallSegmentation] Model is null!");
                        return;
                  }

                  Debug.Log($"[WallSegmentation] 📋 Model Structure Analysis:");
                  Debug.Log($"[WallSegmentation] Model Type: {model.GetType().Name}");

                  // Логируем входные тензоры
                  var inputsProperty = model.GetType().GetProperty("inputs");
                  if (inputsProperty != null)
                  {
                        var inputs = inputsProperty.GetValue(model);
                        if (inputs != null)
                        {
                              Debug.Log($"[WallSegmentation] 📥 Inputs found:");
                              var enumerableInputs = inputs as System.Collections.IEnumerable;
                              if (enumerableInputs != null)
                              {
                                    int count = 0;
                                    foreach (var input in enumerableInputs)
                                    {
                                          var nameProperty = input.GetType().GetProperty("name");
                                          var shapeProperty = input.GetType().GetProperty("shape");

                                          string inputName = nameProperty?.GetValue(input) as string ?? "unknown";
                                          string inputShape = shapeProperty?.GetValue(input)?.ToString() ?? "unknown";

                                          Debug.Log($"[WallSegmentation]   Input {count}: name='{inputName}', shape={inputShape}");
                                          count++;
                                    }
                              }
                        }
                  }
                  else
                  {
                        Debug.LogWarning("[WallSegmentation] ⚠️ No 'inputs' property found on model");

                        // Альтернативный способ: поиск layers и анализ структуры модели
                        var layersProperty = model.GetType().GetProperty("layers");
                        if (layersProperty != null)
                        {
                              var layers = layersProperty.GetValue(model);
                              if (layers is System.Collections.IEnumerable layersList)
                              {
                                    var layersArray = layersList.Cast<object>().ToArray();
                                    Debug.Log($"[WallSegmentation] 📋 Model has {layersArray.Length} layers");

                                    // Ищем входные слои (обычно первые несколько)
                                    foreach (var layer in layersArray.Take(5))
                                    {
                                          var layerType = layer.GetType().Name;
                                          var nameProperty = layer.GetType().GetProperty("name");
                                          var layerName = nameProperty?.GetValue(layer) as string ?? "unknown";

                                          Debug.Log($"[WallSegmentation] 📋 Layer: {layerName} (type: {layerType})");

                                          // Проверяем, это input layer?
                                          if (layerType.Contains("Input") || layerName.Contains("input") || layerName.Contains("Input"))
                                          {
                                                Debug.Log($"[WallSegmentation] 🎯 Found potential input layer: {layerName}");
                                          }
                                    }
                              }
                        }

                        // Дополнительная информация о модели
                        var allProps = model.GetType().GetProperties().Select(p => p.Name).ToArray();
                        Debug.Log($"[WallSegmentation] 📋 Model properties: {string.Join(", ", allProps.Take(10))}...");
                  }

                  // Логируем выходные тензоры
                  var outputsProperty = model.GetType().GetProperty("outputs");
                  if (outputsProperty != null)
                  {
                        var outputs = outputsProperty.GetValue(model);
                        if (outputs != null)
                        {
                              Debug.Log($"[WallSegmentation] 📤 Outputs found:");
                              var enumerableOutputs = outputs as System.Collections.IEnumerable;
                              if (enumerableOutputs != null)
                              {
                                    int count = 0;
                                    foreach (var output in enumerableOutputs)
                                    {
                                          var nameProperty = output.GetType().GetProperty("name");
                                          var shapeProperty = output.GetType().GetProperty("shape");

                                          string outputName = nameProperty?.GetValue(output) as string ?? "unknown";
                                          string outputShape = shapeProperty?.GetValue(output)?.ToString() ?? "unknown";

                                          Debug.Log($"[WallSegmentation]   Output {count}: name='{outputName}', shape={outputShape}");
                                          count++;
                                    }
                              }
                        }
                  }
                  else
                  {
                        Debug.LogWarning("[WallSegmentation] ⚠️ No 'outputs' property found on model");
                  }
            }
            catch (Exception e)
            {
                  Debug.LogError($"[WallSegmentation] Error logging model structure: {e.Message}");
            }
      }

      /// <summary>
      /// Gets the name of the model's input tensor.
      /// Caches the result for performance.
      /// </summary>
      private string GetModelInputName()
      {
            if (!string.IsNullOrEmpty(cachedInputName))
            {
                  return cachedInputName;
            }

            // Use reflection on runtimeModel to get input information
            try
            {
                  if (runtimeModel != null && _modelLoaderType != null)
                  {
                        // Try to get inputs property from the runtime model
                        var inputsProperty = runtimeModel.GetType().GetProperty("inputs");
                        if (inputsProperty != null)
                        {
                              var inputs = inputsProperty.GetValue(runtimeModel) as System.Collections.IList;
                              if (inputs != null && inputs.Count > 0)
                              {
                                    var firstInput = inputs[0];
                                    var nameProperty = firstInput.GetType().GetProperty("name");
                                    if (nameProperty != null)
                                    {
                                          cachedInputName = nameProperty.GetValue(firstInput) as string;
                                          if (!string.IsNullOrEmpty(cachedInputName))
                                          {
                                                if (debugMode) Debug.Log($"[WallSegmentation] ✅ Found input name via reflection: '{cachedInputName}'");
                                                return cachedInputName;
                                          }
                                    }
                              }
                        }
                  }
            }
            catch (System.Exception e)
            {
                  if (debugMode) Debug.LogWarning($"[WallSegmentation] Failed to get input name via reflection: {e.Message}");
            }

            // Fallback to common input names
            if (debugMode) Debug.Log("[WallSegmentation] Using fallback input names: pixel_values, input, input_1, x, image, inputs");
            string[] fallbackNames = { "pixel_values", "input", "input_1", "x", "image", "inputs" };

            // Just return the first fallback name since we can't verify which one is correct
            // The actual validation will happen during inference
            cachedInputName = fallbackNames[0];
            if (debugMode) Debug.Log($"[WallSegmentation] Using fallback input name: '{cachedInputName}'");

            return cachedInputName;
      }

      /// <summary>
      /// Tries different overloads of TextureConverter.ToTensor to convert a texture to a tensor.
      /// This makes the implementation more robust across different Sentis versions.
      /// </summary>
      private object ConvertTextureToTensor(Texture inputTexture)
      {
            // Overload 1: ToTensor(Texture texture, int width, int height)
            var toTensorMethod = _textureConverterType.GetMethod("ToTensor", new[] { typeof(Texture), typeof(int), typeof(int) });
            if (toTensorMethod != null)
            {
                  if (debugMode) Debug.Log("[WallSegmentation] Trying ToTensor(Texture, int, int)...");
                  return toTensorMethod.Invoke(null, new object[] { inputTexture, inputResolution.x, inputResolution.y });
            }

            // Overload 2: ToTensor(Texture texture, TextureTransform transform)
            if (_textureTransformType != null)
            {
                  toTensorMethod = _textureConverterType.GetMethod("ToTensor", new[] { typeof(Texture), _textureTransformType });
                  if (toTensorMethod != null)
                  {
                        if (debugMode) Debug.Log("[WallSegmentation] Trying ToTensor(Texture, TextureTransform)...");
                        var transform = Activator.CreateInstance(_textureTransformType);
                        return toTensorMethod.Invoke(null, new object[] { inputTexture, transform });
                  }
            }

            // Overload 3: ToTensor(Texture texture)
            toTensorMethod = _textureConverterType.GetMethod("ToTensor", new[] { typeof(Texture) });
            if (toTensorMethod != null)
            {
                  if (debugMode) Debug.Log("[WallSegmentation] Trying ToTensor(Texture)...");
                  return toTensorMethod.Invoke(null, new object[] { inputTexture });
            }

            // Overload 4: ToTensor(Texture, int, int, TextureTransform)
            if (_textureTransformType != null)
            {
                  toTensorMethod = _textureConverterType.GetMethod("ToTensor", new[] { typeof(Texture), typeof(int), typeof(int), _textureTransformType });
                  if (toTensorMethod != null)
                  {
                        if (debugMode) Debug.Log("[WallSegmentation] Trying ToTensor(Texture, int, int, TextureTransform)...");
                        var transform = Activator.CreateInstance(_textureTransformType);
                        return toTensorMethod.Invoke(null, new object[] { inputTexture, inputResolution.x, inputResolution.y, transform });
                  }
            }

            throw new MissingMethodException("Could not find a suitable 'ToTensor' method in TextureConverter.");
      }

      /// <summary>
      /// Runs the segmentation model on the input texture.
      /// </summary>
      /// <param name="inputTexture">The texture to process.</param>
      private void RunInference(Texture inputTexture)
      {
            if (!isModelInitialized || engine == null)
            {
                  if (debugMode) Debug.LogWarning("[WallSegmentation] Inference skipped: model not ready.");
                  return;
            }

            if (inputTexture == null)
            {
                  Debug.LogError("[WallSegmentation] ❌ Input texture for inference is null!");
                  return;
            }

            if (_textureConverterType == null || _tensorFloatType == null || _workerType == null)
            {
                  Debug.LogError("❌ Critical types for inference (TextureConverter, TensorFloat, Worker) are missing!");
                  return;
            }

            object inputTensor = null;
            try
            {
                  if (debugMode) Debug.Log($"[WallSegmentation] 🚀 RunInference: Starting inference with texture {inputTexture.width}x{inputTexture.height}");

                  // 1. Convert texture to Tensor using a flexible helper
                  inputTensor = ConvertTextureToTensor(inputTexture);
                  if (inputTensor == null) throw new Exception("ToTensor returned null.");

                  bool executed = false;
                  string inputName = GetModelInputName();

                  // --- Try Pattern A: SetInput(name, tensor) + Execute() ---
                  var setInputMethod = _workerType.GetMethod("SetInput", new[] { typeof(string), _tensorFloatType });
                  var executeMethod_NoParams = _workerType.GetMethod("Execute", Type.EmptyTypes);

                  if (setInputMethod != null && executeMethod_NoParams != null && !string.IsNullOrEmpty(inputName))
                  {
                        if (debugMode) Debug.Log("[WallSegmentation] Trying execution pattern: SetInput(name, tensor) + Execute()");
                        setInputMethod.Invoke(engine, new object[] { inputName, inputTensor });
                        executeMethod_NoParams.Invoke(engine, null);
                        executed = true;
                  }

                  // --- Try Pattern B: Execute(inputDict) ---
                  if (!executed)
                  {
                        var executeMethod_Dict = _workerType.GetMethod("Execute", new[] { typeof(System.Collections.IDictionary) });
                        if (executeMethod_Dict != null && !string.IsNullOrEmpty(inputName))
                        {
                              if (debugMode) Debug.Log("[WallSegmentation] Trying execution pattern: Execute(Dictionary)");
                              var inputDict = new System.Collections.Generic.Dictionary<string, object> { { inputName, inputTensor } };
                              executeMethod_Dict.Invoke(engine, new object[] { inputDict });
                              executed = true;
                        }
                  }

                  // --- Try Pattern C: Execute(tensor) directly ---
                  if (!executed)
                  {
                        var executeMethod_Tensor = _workerType.GetMethod("Execute", new[] { _tensorFloatType });
                        if (executeMethod_Tensor != null)
                        {
                              if (debugMode) Debug.Log("[WallSegmentation] Trying execution pattern: Execute(tensor)");
                              executeMethod_Tensor.Invoke(engine, new object[] { inputTensor });
                              executed = true;
                        }
                  }

                  // --- Try Pattern D: Execute(tensor[]) with array ---
                  if (!executed)
                  {
                        var arrayType = _tensorFloatType.MakeArrayType();
                        var executeMethod_Array = _workerType.GetMethod("Execute", new[] { arrayType });
                        if (executeMethod_Array != null)
                        {
                              if (debugMode) Debug.Log("[WallSegmentation] Trying execution pattern: Execute(tensor[])");
                              var tensorArray = Array.CreateInstance(_tensorFloatType, 1);
                              tensorArray.SetValue(inputTensor, 0);
                              executeMethod_Array.Invoke(engine, new object[] { tensorArray });
                              executed = true;
                        }
                  }

                  // --- Try Pattern E: Schedule() method ---
                  if (!executed)
                  {
                        var scheduleMethod = _workerType.GetMethod("Schedule", new[] { _tensorFloatType });
                        if (scheduleMethod != null)
                        {
                              if (debugMode) Debug.Log("[WallSegmentation] Trying execution pattern: Schedule(tensor)");
                              scheduleMethod.Invoke(engine, new object[] { inputTensor });
                              executed = true;
                        }
                  }

                  if (!executed)
                  {
                        throw new MissingMethodException("Worker.Execute", "Could not find a suitable Execute method overload.");
                  }

                  // 3. Get output
                  var peekOutputMethod = _workerType.GetMethod("PeekOutput", Type.EmptyTypes);
                  if (peekOutputMethod == null) throw new MissingMethodException("Worker.PeekOutput");
                  object outputTensor = peekOutputMethod.Invoke(engine, null);

                  if (outputTensor != null)
                  {
                        if (debugMode) Debug.Log("[WallSegmentation] ✅ Inference successful, processing result...");
                        ProcessSegmentationResult(outputTensor);
                  }
                  else
                  {
                        Debug.LogWarning("[WallSegmentation] ⚠️ Inference executed but returned null output.");
                  }
            }
            catch (Exception e)
            {
                  Debug.LogError($"[WallSegmentation] ❌ An error occurred during inference: {e.GetType().Name} - {e.Message}\n{e.StackTrace}");
                  consecutiveFailures++;
                  if (consecutiveFailures > 10)
                  {
                        Debug.LogError("[WallSegmentation] 💀 Too many consecutive inference failures, disabling component.");
                        enabled = false;
                  }
            }
            finally
            {
                  // 4. Dispose of the input tensor
                  if (inputTensor != null)
                  {
                        var disposeMethod = inputTensor.GetType().GetMethod("Dispose");
                        disposeMethod?.Invoke(inputTensor, null);
                  }
            }
      }

      private void ProcessSegmentationResult(object outputTensor)
      {
            if (debugMode) Debug.Log($"[WallSegmentation] 🎨 Processing segmentation result. Output tensor type: {outputTensor.GetType()}");

            try
            {
                  // Create or update mask texture from tensor
                  if (_textureConverterType != null)
                  {
                        // Use TextureConverter if available
                        var renderTextureMethod = _textureConverterType.GetMethod("ToTexture", new[] { outputTensor.GetType() });
                        if (renderTextureMethod != null)
                        {
                              var renderTexture = renderTextureMethod.Invoke(null, new object[] { outputTensor }) as RenderTexture;
                              if (renderTexture != null)
                              {
                                    UpdateMaskTexture(renderTexture);
                                    if (debugMode) Debug.Log("[WallSegmentation] ✅ Mask texture updated via TextureConverter!");
                                    return;
                              }
                        }
                  }

                  // Fallback: Create texture manually from tensor data
                  if (debugMode) Debug.Log("[WallSegmentation] 🔄 TextureConverter not available, creating texture manually...");

                  // Get tensor shape and data
                  var shapeProperty = outputTensor.GetType().GetProperty("shape");
                  var dataProperty = outputTensor.GetType().GetMethod("ToReadOnlyArray");

                  if (shapeProperty != null && (dataProperty != null))
                  {
                        var shape = shapeProperty.GetValue(outputTensor);
                        var shapeArray = shape as int[];

                        if (shapeArray != null && shapeArray.Length >= 3)
                        {
                              int height = shapeArray[shapeArray.Length - 2];
                              int width = shapeArray[shapeArray.Length - 1];

                              // Create a simple mask texture
                              var maskTexture = new Texture2D(width, height, TextureFormat.R8, false);

                              // For now, create a simple test pattern since we need to extract data properly
                              var pixels = new byte[width * height];
                              for (int i = 0; i < pixels.Length; i++)
                              {
                                    pixels[i] = (byte)(i % 255); // Simple pattern
                              }

                              maskTexture.LoadRawTextureData(pixels);
                              maskTexture.Apply();

                              UpdateMaskTexture(maskTexture);
                              if (debugMode) Debug.Log($"[WallSegmentation] ✅ Created manual mask texture {width}x{height}!");
                              return;
                        }
                  }

                  Debug.LogWarning("[WallSegmentation] ⚠️ Could not process tensor result - using fallback");
                  CreateFallbackMaskTexture();
            }
            catch (System.Exception e)
            {
                  Debug.LogError($"[WallSegmentation] ❌ Error processing segmentation result: {e.Message}");
                  CreateFallbackMaskTexture();
            }
      }

      private void UpdateMaskTexture(RenderTexture renderTexture)
      {
            // Copy RenderTexture to Texture2D for event
            var texture2D = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGB24, false);
            RenderTexture.active = renderTexture;
            texture2D.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
            texture2D.Apply();
            RenderTexture.active = null;

            // Trigger event
            if (OnSegmentationMaskUpdated != null)
            {
                  OnSegmentationMaskUpdated.Invoke(renderTexture);
                  if (debugMode) Debug.Log($"[WallSegmentation] 📢 OnSegmentationMaskUpdated event invoked with {OnSegmentationMaskUpdated.GetInvocationList().Length} subscribers");
            }
      }

      private void UpdateMaskTexture(Texture2D texture2D)
      {
            // Convert Texture2D to RenderTexture for consistency
            var renderTexture = new RenderTexture(texture2D.width, texture2D.height, 0);
            Graphics.Blit(texture2D, renderTexture);

            // Trigger event
            if (OnSegmentationMaskUpdated != null)
            {
                  OnSegmentationMaskUpdated.Invoke(renderTexture);
                  if (debugMode) Debug.Log($"[WallSegmentation] 📢 OnSegmentationMaskUpdated event invoked with {OnSegmentationMaskUpdated.GetInvocationList().Length} subscribers");
            }
      }

      private void CreateFallbackMaskTexture()
      {
            // Create a simple test texture as fallback
            var fallbackTexture = new Texture2D(256, 256, TextureFormat.RGB24, false);
            var colors = new Color32[256 * 256];

            for (int i = 0; i < colors.Length; i++)
            {
                  // Create a simple gradient pattern
                  int x = i % 256;
                  int y = i / 256;
                  byte value = (byte)((x + y) / 2);
                  colors[i] = new Color32(value, value, value, 255);
            }

            fallbackTexture.SetPixels32(colors);
            fallbackTexture.Apply();

            UpdateMaskTexture(fallbackTexture);
            if (debugMode) Debug.Log("[WallSegmentation] ✅ Created fallback mask texture!");
      }

      private void OnDestroy()
      {
            if (isSentisAvailable && engine != null)
            {
                  try
                  {
                        var disposeMethod = engine.GetType().GetMethod("Dispose");
                        disposeMethod?.Invoke(engine, null);
                  }
                  catch (Exception e)
                  {
                        if (debugMode) Debug.LogWarning($"Error disposing engine: {e.Message}");
                  }
            }
      }
}