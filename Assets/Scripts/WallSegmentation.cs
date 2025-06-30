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

      // Unity Sentis availability
      private bool isSentisAvailable = false;
      private System.Type modelLoaderType;
      private System.Type workerFactoryType;
      private System.Type textureConverterType;
      private System.Type tensorFloatType;

      private void Awake()
      {
            Debug.Log($"[WallSegmentation] 🟢 Awake() вызван, компонент: {gameObject.name}");

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
            // Если определен символ UNITY_SENTIS, то пакет должен быть доступен
#if UNITY_SENTIS
            Debug.Log("[WallSegmentation] UNITY_SENTIS define symbol detected - Sentis should be available");
            isSentisAvailable = true;
            return;
#endif

            try
            {
                  Debug.Log("[WallSegmentation] Checking Sentis availability via reflection...");

                  // Попытка загрузить основные типы Unity Sentis - пробуем разные варианты имен
                  string[] possibleAssemblyNames = { "Unity.Sentis", "Unity.Sentis.Runtime", "com.unity.sentis" };

                  foreach (string assemblyName in possibleAssemblyNames)
                  {
                        Debug.Log($"[WallSegmentation] Trying assembly: {assemblyName}");

                        modelLoaderType = System.Type.GetType($"Unity.Sentis.ModelLoader, {assemblyName}");
                        workerFactoryType = System.Type.GetType($"Unity.Sentis.WorkerFactory, {assemblyName}");
                        textureConverterType = System.Type.GetType($"Unity.Sentis.TextureConverter, {assemblyName}");
                        tensorFloatType = System.Type.GetType($"Unity.Sentis.TensorFloat, {assemblyName}");

                        Debug.Log($"[WallSegmentation] Types found in {assemblyName}:");
                        Debug.Log($"  ModelLoader: {modelLoaderType != null}");
                        Debug.Log($"  WorkerFactory: {workerFactoryType != null}");
                        Debug.Log($"  TextureConverter: {textureConverterType != null}");
                        Debug.Log($"  TensorFloat: {tensorFloatType != null}");

                        if (modelLoaderType != null && workerFactoryType != null && textureConverterType != null && tensorFloatType != null)
                        {
                              isSentisAvailable = true;
                              Debug.Log($"[WallSegmentation] ✅ Unity Sentis detected and available from assembly: {assemblyName}");
                              return;
                        }
                  }

                  // Альтернативный способ - поиск через все загруженные сборки
                  Debug.Log("[WallSegmentation] Searching through all loaded assemblies...");
                  foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
                  {
                        if (assembly.FullName.Contains("Sentis"))
                        {
                              Debug.Log($"[WallSegmentation] Found Sentis assembly: {assembly.FullName}");
                              foreach (var type in assembly.GetTypes())
                              {
                                    if (type.Name == "ModelLoader") Debug.Log($"  Found ModelLoader: {type.FullName}");
                                    if (type.Name == "WorkerFactory") Debug.Log($"  Found WorkerFactory: {type.FullName}");
                                    if (type.Name == "TextureConverter") Debug.Log($"  Found TextureConverter: {type.FullName}");
                                    if (type.Name == "TensorFloat") Debug.Log($"  Found TensorFloat: {type.FullName}");
                              }
                        }
                  }

                  Debug.LogWarning("[WallSegmentation] Unity Sentis types not found via reflection in any assembly.");
                  isSentisAvailable = false;
            }
            catch (Exception e)
            {
                  Debug.LogError($"[WallSegmentation] Unity Sentis availability check failed: {e.Message}\nStackTrace: {e.StackTrace}");
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
            if (!isSentisAvailable)
            {
                  yield break;
            }

            isInitializing = true;
            Debug.Log("[WallSegmentation] Initializing segmentation model...");

            try
            {
#if UNITY_SENTIS
                  // С определенным UNITY_SENTIS используем улучшенный reflection
                  Debug.Log("[WallSegmentation] Using improved reflection with UNITY_SENTIS enabled");

                  // Поиск всех типов Unity Sentis в загруженных сборках
                  var sentisTypes = new System.Collections.Generic.Dictionary<string, System.Type>();
                  foreach (var assembly in System.AppDomain.CurrentDomain.GetAssemblies())
                  {
                        if (assembly.FullName.Contains("Sentis"))
                        {
                              Debug.Log($"[WallSegmentation] Found Sentis assembly: {assembly.FullName}");
                              foreach (var type in assembly.GetTypes())
                              {
                                    if (type.Namespace == "Unity.Sentis")
                                    {
                                          sentisTypes[type.Name] = type;
                                          Debug.Log($"[WallSegmentation] Found type: {type.Name}");
                                    }
                              }
                        }
                  }

                  Debug.Log($"[WallSegmentation] 🔄 Начинаю загрузку модели. modelAsset: {(modelAsset != null ? "✅" : "❌")}");

                  if (modelAsset == null)
                  {
                        Debug.LogError("[WallSegmentation] ❌ ModelAsset is null! Cannot load model.");
                        throw new System.Exception("ModelAsset is null");
                  }

                  Debug.Log($"[WallSegmentation] ModelAsset name: {modelAsset.name}");

                  // Попытка загрузки модели
                  Debug.Log($"[WallSegmentation] 🔍 Checking for ModelLoader in sentisTypes. Contains: {sentisTypes.ContainsKey("ModelLoader")}");

                  if (sentisTypes.ContainsKey("ModelLoader"))
                  {
                        Debug.Log("[WallSegmentation] ✅ ModelLoader found, getting type...");
                        var modelLoaderType = sentisTypes["ModelLoader"];
                        Debug.Log($"[WallSegmentation] ModelLoader type: {modelLoaderType.FullName}");

                        Debug.Log("[WallSegmentation] 🔍 Getting Load methods...");
                        var loadMethods = modelLoaderType.GetMethods().Where(m => m.Name == "Load" && m.IsStatic).ToArray();

                        Debug.Log($"[WallSegmentation] Found {loadMethods.Length} Load methods");

                        foreach (var method in loadMethods)
                        {
                              try
                              {
                                    Debug.Log($"[WallSegmentation] Trying Load method with parameters: {string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))}");
                                    runtimeModel = method.Invoke(null, new object[] { modelAsset });
                                    Debug.Log("[WallSegmentation] Model loaded successfully with method: " + method.ToString());
                                    break;
                              }
                              catch (System.Exception ex)
                              {
                                    Debug.Log($"[WallSegmentation] Load method failed: {ex.Message}");
                                    continue;
                              }
                        }
                  }

                  if (runtimeModel == null)
                  {
                        throw new System.Exception("Failed to load model with any available Load method");
                  }

                  // Поиск способов создания Worker
                  var workerCreated = false;

                  // Способ 1: WorkerFactory
                  if (sentisTypes.ContainsKey("WorkerFactory"))
                  {
                        var workerFactoryType = sentisTypes["WorkerFactory"];
                        var createWorkerMethods = workerFactoryType.GetMethods().Where(m => m.Name == "CreateWorker" && m.IsStatic).ToArray();
                        Debug.Log($"[WallSegmentation] Found {createWorkerMethods.Length} WorkerFactory.CreateWorker methods");

                        foreach (var method in createWorkerMethods)
                        {
                              try
                              {
                                    var parameters = method.GetParameters();
                                    Debug.Log($"[WallSegmentation] Trying WorkerFactory.CreateWorker with: {string.Join(", ", parameters.Select(p => p.ParameterType.Name))}");

                                    if (parameters.Length == 2)
                                    {
                                          // Пробуем с разными backend типами
                                          var backendTypeParam = parameters[0];
                                          if (backendTypeParam.ParameterType.IsEnum)
                                          {
                                                var backendNames = System.Enum.GetNames(backendTypeParam.ParameterType);
                                                Debug.Log($"[WallSegmentation] Available backends: {string.Join(", ", backendNames)}");

                                                // Пробуем CPU backend (обычно 0 или 1)
                                                for (int i = 0; i < backendNames.Length; i++)
                                                {
                                                      try
                                                      {
                                                            var backendValue = System.Enum.ToObject(backendTypeParam.ParameterType, i);
                                                            Debug.Log($"[WallSegmentation] Trying backend: {backendNames[i]} (value: {i})");
                                                            engine = method.Invoke(null, new object[] { backendValue, runtimeModel });
                                                            workerCreated = true;
                                                            Debug.Log($"[WallSegmentation] Worker created with WorkerFactory using backend: {backendNames[i]}");
                                                            break;
                                                      }
                                                      catch (System.Exception backendEx)
                                                      {
                                                            Debug.Log($"[WallSegmentation] Backend {backendNames[i]} failed: {backendEx.Message}");
                                                            if (backendEx.InnerException != null)
                                                            {
                                                                  Debug.Log($"[WallSegmentation] Inner exception: {backendEx.InnerException.Message}");
                                                            }
                                                      }
                                                }
                                          }
                                          else
                                          {
                                                engine = method.Invoke(null, new object[] { 1, runtimeModel });
                                                workerCreated = true;
                                                Debug.Log("[WallSegmentation] Worker created with WorkerFactory (non-enum backend)");
                                          }

                                          if (workerCreated) break;
                                    }
                                    else if (parameters.Length == 1)
                                    {
                                          Debug.Log($"[WallSegmentation] Trying single parameter method with model");
                                          engine = method.Invoke(null, new object[] { runtimeModel });
                                          workerCreated = true;
                                          Debug.Log("[WallSegmentation] Worker created with WorkerFactory (single param)");
                                          break;
                                    }
                              }
                              catch (System.Exception ex)
                              {
                                    Debug.LogError($"[WallSegmentation] WorkerFactory method failed: {ex.Message}");
                                    if (ex.InnerException != null)
                                    {
                                          Debug.LogError($"[WallSegmentation] Inner exception: {ex.InnerException.Message}");
                                    }
                                    continue;
                              }
                        }
                  }
                  else
                  {
                        Debug.LogWarning("[WallSegmentation] WorkerFactory type not found in sentisTypes");
                  }

                  // Способ 2: Прямое создание worker из модели
                  if (!workerCreated && runtimeModel != null)
                  {
                        var modelType = runtimeModel.GetType();
                        Debug.Log($"[WallSegmentation] Model type: {modelType.Name}");
                        var allMethods = modelType.GetMethods().Where(m => m.Name.Contains("Worker") || m.Name.Contains("Create")).ToArray();
                        Debug.Log($"[WallSegmentation] Found {allMethods.Length} potential worker creation methods in model");

                        foreach (var method in allMethods)
                        {
                              Debug.Log($"[WallSegmentation] Available method: {method.Name}, parameters: {method.GetParameters().Length}");
                        }

                        foreach (var method in allMethods)
                        {
                              try
                              {
                                    Debug.Log($"[WallSegmentation] Trying model method: {method.Name}");
                                    var parameters = method.GetParameters();

                                    if (parameters.Length == 0)
                                    {
                                          Debug.Log($"[WallSegmentation] Calling {method.Name} with no parameters");
                                          engine = method.Invoke(runtimeModel, new object[] { });
                                          workerCreated = true;
                                          Debug.Log($"[WallSegmentation] Worker created with {method.Name}");
                                          break;
                                    }
                                    else if (parameters.Length == 1 && parameters[0].ParameterType.IsEnum)
                                    {
                                          var backendNames = System.Enum.GetNames(parameters[0].ParameterType);
                                          Debug.Log($"[WallSegmentation] Method {method.Name} has enum parameter with values: {string.Join(", ", backendNames)}");

                                          // Пробуем все доступные backend'ы
                                          for (int i = 0; i < backendNames.Length; i++)
                                          {
                                                try
                                                {
                                                      var backendValue = System.Enum.ToObject(parameters[0].ParameterType, i);
                                                      Debug.Log($"[WallSegmentation] Calling {method.Name} with backend: {backendNames[i]}");
                                                      engine = method.Invoke(runtimeModel, new object[] { backendValue });
                                                      workerCreated = true;
                                                      Debug.Log($"[WallSegmentation] Worker created with {method.Name} using backend: {backendNames[i]}");
                                                      break;
                                                }
                                                catch (System.Exception backendEx)
                                                {
                                                      Debug.Log($"[WallSegmentation] Backend {backendNames[i]} failed for {method.Name}: {backendEx.Message}");
                                                }
                                          }

                                          if (workerCreated) break;
                                    }
                                    else
                                    {
                                          Debug.Log($"[WallSegmentation] Method {method.Name} has {parameters.Length} parameters: {string.Join(", ", parameters.Select(p => p.ParameterType.Name))}");
                                    }
                              }
                              catch (System.Exception ex)
                              {
                                    Debug.LogError($"[WallSegmentation] Model method {method.Name} failed: {ex.Message}");
                                    if (ex.InnerException != null)
                                    {
                                          Debug.LogError($"[WallSegmentation] Inner exception: {ex.InnerException.Message}");
                                    }
                                    continue;
                              }
                        }
                  }
                  else if (!workerCreated)
                  {
                        Debug.LogWarning("[WallSegmentation] Runtime model is null, cannot try model methods");
                  }

                  // Способ 3: Поиск Worker класса напрямую
                  if (!workerCreated && sentisTypes.ContainsKey("Worker"))
                  {
                        Debug.Log("[WallSegmentation] Trying direct Worker class instantiation");
                        var workerType = sentisTypes["Worker"];
                        var constructors = workerType.GetConstructors();

                        foreach (var constructor in constructors)
                        {
                              try
                              {
                                    var parameters = constructor.GetParameters();
                                    Debug.Log($"[WallSegmentation] Worker constructor with: {string.Join(", ", parameters.Select(p => p.ParameterType.Name))}");

                                    if (parameters.Length == 1 && parameters[0].ParameterType.Name == "Model")
                                    {
                                          Debug.Log("[WallSegmentation] Trying Worker constructor with model only");
                                          engine = Activator.CreateInstance(workerType, runtimeModel);
                                          workerCreated = true;
                                          Debug.Log("[WallSegmentation] Worker created with direct constructor");
                                          break;
                                    }
                                    else if (parameters.Length == 2)
                                    {
                                          var backendParam = parameters.FirstOrDefault(p => p.ParameterType.IsEnum);
                                          if (backendParam != null)
                                          {
                                                var backendNames = System.Enum.GetNames(backendParam.ParameterType);
                                                Debug.Log($"[WallSegmentation] Trying Worker constructor with backends: {string.Join(", ", backendNames)}");

                                                for (int i = 0; i < backendNames.Length; i++)
                                                {
                                                      try
                                                      {
                                                            var backendValue = System.Enum.ToObject(backendParam.ParameterType, i);
                                                            if (backendParam == parameters[0])
                                                            {
                                                                  engine = Activator.CreateInstance(workerType, backendValue, runtimeModel);
                                                            }
                                                            else
                                                            {
                                                                  engine = Activator.CreateInstance(workerType, runtimeModel, backendValue);
                                                            }
                                                            workerCreated = true;
                                                            Debug.Log($"[WallSegmentation] Worker created with constructor using backend: {backendNames[i]}");
                                                            break;
                                                      }
                                                      catch (System.Exception backendEx)
                                                      {
                                                            Debug.Log($"[WallSegmentation] Constructor backend {backendNames[i]} failed: {backendEx.Message}");
                                                      }
                                                }

                                                if (workerCreated) break;
                                          }
                                    }
                              }
                              catch (System.Exception ex)
                              {
                                    Debug.LogError($"[WallSegmentation] Worker constructor failed: {ex.Message}");
                                    if (ex.InnerException != null)
                                    {
                                          Debug.LogError($"[WallSegmentation] Inner exception: {ex.InnerException.Message}");
                                    }
                              }
                        }
                  }

                  if (!workerCreated)
                  {
                        Debug.LogError("[WallSegmentation] All worker creation methods failed. Available types:");
                        foreach (var kvp in sentisTypes)
                        {
                              Debug.LogError($"[WallSegmentation]   {kvp.Key}: {kvp.Value}");
                        }
                        throw new System.Exception("Failed to create worker with any available method");
                  }

                  Debug.Log("[WallSegmentation] ✅ Sentis initialization completed successfully");
                  Debug.Log("[WallSegmentation] 🔄 Proceeding to texture initialization...");

#else
                  // Используем reflection для вызова Unity Sentis API
                  Debug.Log("[WallSegmentation] Using reflection to access Unity Sentis API");
                  var loadMethod = modelLoaderType.GetMethod("Load", new[] { typeof(UnityEngine.Object) });
                  runtimeModel = loadMethod.Invoke(null, new object[] { modelAsset });
                  
                  var createWorkerMethod = workerFactoryType.GetMethod("CreateWorker");
                  engine = createWorkerMethod.Invoke(null, new object[] { runtimeModel });
                  
                  Debug.Log($"[WallSegmentation] Sentis worker created with reflection");
#endif
            }
            catch (Exception e)
            {
                  Debug.LogError($"[WallSegmentation] ❌ CRITICAL ERROR in InitializeSegmentation: {e.Message}");
                  Debug.LogError($"[WallSegmentation] ❌ Exception type: {e.GetType().FullName}");
                  Debug.LogError($"[WallSegmentation] ❌ Stack trace: {e.StackTrace}");
                  if (e.InnerException != null)
                  {
                        Debug.LogError($"[WallSegmentation] ❌ Inner exception: {e.InnerException.Message}");
                        Debug.LogError($"[WallSegmentation] ❌ Inner stack trace: {e.InnerException.StackTrace}");
                  }

                  lastErrorMessage = e.Message;
                  isInitializationFailed = true;
                  isInitializing = false;
                  Debug.LogError("[WallSegmentation] 💀 Initialization failed, stopping coroutine");
                  yield break;
            }

            // Initialize the output texture
            Debug.Log("[WallSegmentation] 🖼️ Initializing output texture...");
            if (segmentationMaskTexture == null || segmentationMaskTexture.width != inputResolution.x || segmentationMaskTexture.height != inputResolution.y)
            {
                  if (segmentationMaskTexture != null) segmentationMaskTexture.Release();
                  segmentationMaskTexture = new RenderTexture(inputResolution.x, inputResolution.y, 0, RenderTextureFormat.RFloat);
                  segmentationMaskTexture.Create();
                  Debug.Log($"[WallSegmentation] ✅ Created output texture: {inputResolution.x}x{inputResolution.y}");
            }
            else
            {
                  Debug.Log($"[WallSegmentation] ✅ Output texture already exists: {segmentationMaskTexture.width}x{segmentationMaskTexture.height}");
            }

            Debug.Log("[WallSegmentation] 🎯 Setting final flags...");
            isModelInitialized = true;
            isInitializing = false;

            Debug.Log("[WallSegmentation] 📢 Invoking OnModelInitialized event...");
            OnModelInitialized?.Invoke();

            Debug.Log("[WallSegmentation] 🎉 Segmentation model initialized successfully! 🎉");
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
            if (arCameraManager == null)
            {
                  if (debugMode && consecutiveFailures > 5 && consecutiveFailures % 60 == 0)
                  {
                        Debug.LogWarning("[WallSegmentation] ARCameraManager is null. Cannot get camera texture.");
                  }
                  return false;
            }

            if (arCameraManager.subsystem == null)
            {
                  if (debugMode && consecutiveFailures > 5 && consecutiveFailures % 60 == 0)
                  {
                        Debug.LogWarning("[WallSegmentation] ARCameraManager subsystem is null. Cannot get camera texture.");
                  }
                  return false;
            }

            if (!arCameraManager.subsystem.running)
            {
                  if (debugMode && consecutiveFailures > 5 && consecutiveFailures % 60 == 0)
                  {
                        Debug.LogWarning("[WallSegmentation] ARCameraManager subsystem is not running. Cannot get camera texture.");
                  }
                  return false;
            }

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

            return false;
      }

      private void RunInference(Texture inputTexture)
      {
            if (!isSentisAvailable || engine == null)
            {
                  Debug.LogError($"[WallSegmentation] RunInference: Cannot run - isSentisAvailable={isSentisAvailable}, engine={engine}");
                  return;
            }

            try
            {
                  if (Time.frameCount % 60 == 0) // Логируем каждую секунду
                  {
                        Debug.Log($"[WallSegmentation] 🚀 RunInference: Starting inference with texture {inputTexture.width}x{inputTexture.height}");
                  }

                  // Используем reflection для конвертации текстуры в тензор
                  var toTensorMethod = textureConverterType.GetMethod("ToTensor", new[] { typeof(Texture), typeof(object) });
                  if (toTensorMethod == null)
                  {
                        Debug.LogError("[WallSegmentation] ❌ ToTensor method not found!");
                        return;
                  }

                  var textureTransformType = System.Type.GetType("Unity.Sentis.TextureTransform, Unity.Sentis");
                  if (textureTransformType == null)
                  {
                        Debug.LogError("[WallSegmentation] ❌ TextureTransform type not found!");
                        return;
                  }

                  var textureTransform = Activator.CreateInstance(textureTransformType);

                  // Устанавливаем размеры
                  var setDimensionsMethod = textureTransformType.GetMethod("SetDimensions");
                  if (setDimensionsMethod == null)
                  {
                        Debug.LogError("[WallSegmentation] ❌ SetDimensions method not found!");
                        return;
                  }

                  textureTransform = setDimensionsMethod.Invoke(textureTransform, new object[] { inputResolution.x, inputResolution.y, 3 });

                  var inputTensor = toTensorMethod.Invoke(null, new object[] { inputTexture, textureTransform });
                  if (inputTensor == null)
                  {
                        Debug.LogError("[WallSegmentation] ❌ Failed to convert texture to tensor!");
                        return;
                  }

                  if (Time.frameCount % 120 == 0)
                  {
                        Debug.Log("[WallSegmentation] 🔢 Tensor created, executing inference...");
                  }

                  // Выполняем инференс
                  var executeMethod = engine.GetType().GetMethod("Execute");
                  if (executeMethod == null)
                  {
                        Debug.LogError("[WallSegmentation] ❌ Execute method not found on worker!");
                        return;
                  }

                  executeMethod.Invoke(engine, new object[] { inputTensor });

                  // Получаем результат
                  var peekOutputMethod = engine.GetType().GetMethod("PeekOutput");
                  if (peekOutputMethod == null)
                  {
                        Debug.LogError("[WallSegmentation] ❌ PeekOutput method not found on worker!");
                        return;
                  }

                  var outputTensor = peekOutputMethod.Invoke(engine, null);
                  if (outputTensor == null)
                  {
                        Debug.LogError("[WallSegmentation] ❌ Output tensor is null!");
                        return;
                  }

                  if (Time.frameCount % 120 == 0)
                  {
                        Debug.Log("[WallSegmentation] ✅ Inference complete, processing result...");
                  }

                  ProcessSegmentationResult(outputTensor);

                  // Очищаем временную текстуру
                  if (inputTexture is Texture2D)
                  {
                        Destroy(inputTexture);
                  }

                  // Освобождаем тензор
                  if (inputTensor != null)
                  {
                        var disposeMethod = inputTensor.GetType().GetMethod("Dispose");
                        disposeMethod?.Invoke(inputTensor, null);
                  }
            }
            catch (Exception e)
            {
                  Debug.LogError($"[WallSegmentation] ❌ Error during inference: {e.Message}");
                  Debug.LogError($"[WallSegmentation] Stack trace: {e.StackTrace}");
                  if (e.InnerException != null)
                  {
                        Debug.LogError($"[WallSegmentation] Inner exception: {e.InnerException.Message}");
                  }
                  lastErrorMessage = e.Message;
            }
      }

      private void ProcessSegmentationResult(object outputTensor)
      {
            if (!isSentisAvailable || outputTensor == null)
            {
                  Debug.LogError($"[WallSegmentation] ProcessSegmentationResult: Cannot process - isSentisAvailable={isSentisAvailable}, outputTensor={outputTensor}");
                  return;
            }

            if (segmentationMaterial == null)
            {
                  Debug.LogError("[WallSegmentation] ❌ Segmentation material is not assigned!");
                  return;
            }

            try
            {
                  if (Time.frameCount % 120 == 0)
                  {
                        Debug.Log($"[WallSegmentation] 🎨 Processing segmentation result. Output tensor type: {outputTensor.GetType().Name}");
                  }

                  // This part assumes the model output is in a format that can be directly
                  // visualized by a shader. The shader will handle extracting the wall class.
                  segmentationMaterial.SetInt("_WallClassIndex", wallClassIndex);
                  segmentationMaterial.SetFloat("_WallConfidence", wallConfidence);

                  // Проверяем, что есть нужные типы
                  if (tensorFloatType == null)
                  {
                        Debug.LogError("[WallSegmentation] ❌ tensorFloatType is null! Looking for alternative methods...");

                        // Попробуем найти метод в самом тензоре
                        var tensorType = outputTensor.GetType();
                        var toRenderTextureMethod = tensorType.GetMethod("ToRenderTexture", new[] { typeof(RenderTexture) });

                        if (toRenderTextureMethod != null)
                        {
                              Debug.Log("[WallSegmentation] Found ToRenderTexture on tensor object itself");
                              toRenderTextureMethod.Invoke(outputTensor, new object[] { segmentationMaskTexture });
                        }
                        else
                        {
                              Debug.LogError($"[WallSegmentation] ❌ No ToRenderTexture method found on type {tensorType.Name}");
                              return;
                        }
                  }
                  else
                  {
                        // Используем reflection для конвертации тензора в RenderTexture
                        var toRenderTextureMethod = tensorFloatType.GetMethod("ToRenderTexture", new[] { tensorFloatType, typeof(RenderTexture) });
                        if (toRenderTextureMethod == null)
                        {
                              Debug.LogError("[WallSegmentation] ❌ ToRenderTexture method not found on tensorFloatType!");
                              return;
                        }

                        toRenderTextureMethod.Invoke(null, new object[] { outputTensor, segmentationMaskTexture });
                  }

                  if (Time.frameCount % 120 == 0)
                  {
                        Debug.Log($"[WallSegmentation] ✅ Segmentation mask updated: {segmentationMaskTexture.width}x{segmentationMaskTexture.height}");
                  }

                  RenderTexture finalMask = EnhanceMask(segmentationMaskTexture);

                  // Вызываем событие
                  if (OnSegmentationMaskUpdated != null)
                  {
                        OnSegmentationMaskUpdated.Invoke(finalMask);
                        if (Time.frameCount % 120 == 0)
                        {
                              Debug.Log($"[WallSegmentation] 📢 OnSegmentationMaskUpdated event invoked with {OnSegmentationMaskUpdated.GetInvocationList().Length} subscribers");
                        }
                  }
                  else
                  {
                        Debug.LogWarning("[WallSegmentation] ⚠️ OnSegmentationMaskUpdated has no subscribers!");
                  }
            }
            catch (Exception e)
            {
                  Debug.LogError($"[WallSegmentation] ❌ Error processing segmentation result: {e.Message}");
                  Debug.LogError($"[WallSegmentation] Stack trace: {e.StackTrace}");
                  if (e.InnerException != null)
                  {
                        Debug.LogError($"[WallSegmentation] Inner exception: {e.InnerException.Message}");
                  }
                  lastErrorMessage = e.Message;
            }
      }

      private RenderTexture EnhanceMask(RenderTexture inputMask)
      {
            // For now, just return the input mask. Post-processing can be added here.
            // Example: apply blur, contrast, etc. using temporary RenderTextures.
            return inputMask;
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