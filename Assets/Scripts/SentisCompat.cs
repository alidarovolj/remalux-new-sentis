using UnityEngine;
using System.Reflection;
using System;
using System.Linq;
using System.IO;

/// <summary>
/// Адаптер для совместимости с разными версиями Unity Sentis API
/// </summary>
public static class SentisCompat
{
      private static bool isInitialized = false;

      // Изменяем поля на public static свойства с private set
      public static Type ModelLoaderType { get; private set; }
      public static Type ModelType { get; private set; }
      public static Type WorkerType { get; private set; }
      public static Type TensorType { get; private set; }
      public static Type OpsType { get; private set; }
      // Добавим для TextureConverterType, так как он тоже используется
      public static Type TextureConverterType { get; private set; }

      private static bool debugLogging = true; // Можно выключить для релиза

      /// <summary>
      /// Инициализирует адаптер, находя необходимые типы через рефлексию
      /// </summary>
      public static void Initialize()
      {
            if (isInitialized) return;
            if (debugLogging) Debug.Log("SentisCompat: Попытка инициализации...");

            Assembly sentisAssembly = null;
            try
            {
                  foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                  {
                        if (assembly.GetName().Name == "Unity.Sentis")
                        {
                              sentisAssembly = assembly;
                              if (debugLogging) Debug.Log($"SentisCompat: Найдена сборка Unity.Sentis: {sentisAssembly.FullName}");
                              break;
                        }
                  }

                  if (sentisAssembly == null)
                  {
                        Debug.LogError("SentisCompat: Сборка Unity.Sentis не найдена!");
                        return;
                  }

                  ModelLoaderType = sentisAssembly.GetType("Unity.Sentis.ModelLoader");
                  ModelType = sentisAssembly.GetType("Unity.Sentis.Model");
                  WorkerType = sentisAssembly.GetType("Unity.Sentis.Worker") ?? sentisAssembly.GetType("Unity.Sentis.IWorker");
                  TensorType = sentisAssembly.GetType("Unity.Sentis.Tensor");
                  OpsType = sentisAssembly.GetType("Unity.Sentis.Ops");
                  TextureConverterType = sentisAssembly.GetType("Unity.Sentis.TextureConverter"); // Инициализируем TextureConverterType

                  if (debugLogging) Debug.Log($"SentisCompat: ModelLoader Type: {(ModelLoaderType != null ? ModelLoaderType.FullName : "НЕ НАЙДЕН")}");
                  if (debugLogging) Debug.Log($"SentisCompat: Model Type: {(ModelType != null ? ModelType.FullName : "НЕ НАЙДЕН")}");
                  if (debugLogging) Debug.Log($"SentisCompat: Worker/IWorker Type: {(WorkerType != null ? WorkerType.FullName : "НЕ НАЙДЕН")}");
                  if (debugLogging) Debug.Log($"SentisCompat: Tensor Type: {(TensorType != null ? TensorType.FullName : "НЕ НАЙДЕН")}");
                  if (debugLogging) Debug.Log($"SentisCompat: Ops Type: {(OpsType != null ? OpsType.FullName : "НЕ НАЙДЕН")}");
                  if (debugLogging) Debug.Log($"SentisCompat: TextureConverter Type: {(TextureConverterType != null ? TextureConverterType.FullName : "НЕ НАЙДЕН")}");

                  // Дополнительная диагностика для TextureTransform
                  if (TextureConverterType != null) // Предполагаем, что TextureTransform в той же сборке
                  {
                        Type textureTransformType = sentisAssembly.GetType("Unity.Sentis.TextureTransform");
                        if (textureTransformType != null)
                        {
                              if (debugLogging) Debug.Log($"SentisCompat: TextureTransform Type: {textureTransformType.FullName} (Найден)");
                              LogMethods(textureTransformType, BindingFlags.Public | BindingFlags.Static | BindingFlags.GetProperty, "TextureTransform Public Static Properties");
                              LogMethods(textureTransformType, BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly, "TextureTransform Public Constructors"); // Конструкторы - это Instance
                        }
                        else
                        {
                              if (debugLogging) Debug.LogWarning("SentisCompat: TextureTransform Type: НЕ НАЙДЕН в сборке Unity.Sentis.");
                        }
                  }

                  if (ModelLoaderType != null && ModelType != null)
                  {
                        if (debugLogging) Debug.Log("SentisCompat: Базовые типы (ModelLoader, Model) найдены. Инициализация считается успешной для загрузки и попытки создания worker.");
                        isInitialized = true;
                        if (WorkerType == null) Debug.LogWarning("SentisCompat: Тип Worker/IWorker не найден, Execute/Dispose могут не работать.");
                        if (TensorType == null) Debug.LogWarning("SentisCompat: Тип Tensor не найден, работа с тензорами может быть нарушена.");
                        if (OpsType == null) Debug.LogWarning("SentisCompat: Тип Ops не найден, операции с тензорами могут быть нарушены.");
                        if (TextureConverterType == null) Debug.LogWarning("SentisCompat: Тип TextureConverter не найден, конвертация текстур может быть нарушена.");
                  }
                  else
                  {
                        Debug.LogError("SentisCompat: Не удалось найти критичные типы (ModelLoader или Model). Инициализация провалена.");
                  }
            }
            catch (Exception e)
            {
                  Debug.LogError($"SentisCompat: Исключение при инициализации: {e.Message}");
            }
      }

      /// <summary>
      /// Загружает модель из байтового массива
      /// </summary>
      public static object LoadModelFromBytes(byte[] modelData)
      {
            if (!isInitialized) Initialize();
            if (ModelLoaderType == null)
            {
                  Debug.LogError("SentisCompat: ModelLoader не найден");
                  return null;
            }
            try
            {
                  MethodInfo loadMethod = ModelLoaderType.GetMethod("Load", new Type[] { typeof(byte[]) });
                  if (loadMethod != null) return loadMethod.Invoke(null, new object[] { modelData });

                  MethodInfo loadStreamMethod = ModelLoaderType.GetMethod("Load", new Type[] { typeof(Stream) });
                  if (loadStreamMethod != null)
                  {
                        using (var ms = new MemoryStream(modelData))
                        {
                              return loadStreamMethod.Invoke(null, new object[] { ms });
                        }
                  }
                  Debug.LogError("SentisCompat: Метод Load(byte[]) и Load(Stream) не найден");
                  return null;
            }
            catch (Exception e)
            {
                  Debug.LogError($"SentisCompat: Ошибка при загрузке модели: {e.Message}");
                  return null;
            }
      }

      /// <summary>
      /// Создает Worker для модели
      /// </summary>
      public static object CreateWorker(object model, int backend = 0) // backend: 0 for CPU, 1 for GPUCompute
      {
            if (!isInitialized) Initialize();
            if (!isInitialized || WorkerType == null)
            {
                  Debug.LogError("SentisCompat: Инициализация не завершена или тип Worker не найден.");
                  DiagnosticReport();
                  return null;
            }
            if (model == null)
            {
                  Debug.LogError("SentisCompat: Модель не найдена (null)");
                  return null;
            }
            if (ModelType == null || !ModelType.IsInstanceOfType(model))
            {
                  Debug.LogError($"SentisCompat: Предоставленный объект модели имеет неверный тип ({model.GetType().FullName}). Ожидался {ModelType?.FullName ?? "Unity.Sentis.Model"}.");
                  return null;
            }
            try
            {
                  if (debugLogging) Debug.Log($"SentisCompat: Попытка создать Worker через конструктор: new {WorkerType.FullName}(Model, BackendType={(Unity.Sentis.BackendType)backend})");
                  ConstructorInfo workerConstructor = WorkerType.GetConstructor(new Type[] { ModelType, typeof(Unity.Sentis.BackendType) });
                  if (workerConstructor != null)
                  {
                        object backendTypeValue = Enum.ToObject(typeof(Unity.Sentis.BackendType), backend);
                        object workerInstance = workerConstructor.Invoke(new object[] { model, backendTypeValue });
                        if (debugLogging) Debug.Log("SentisCompat: Worker успешно создан через конструктор (Model, BackendType).");
                        return workerInstance;
                  }
                  else
                  {
                        Debug.LogError($"SentisCompat: Не найден конструктор {WorkerType.FullName}({ModelType.Name}, Unity.Sentis.BackendType). Проверьте API Sentis.");
                        DiagnosticReport();
                        return null;
                  }
            }
            catch (Exception e)
            {
                  Debug.LogError($"SentisCompat: Ошибка при создании Worker через конструктор: {e.Message}\n{e.StackTrace}");
                  DiagnosticReport();
                  return null;
            }
      }

      /// <summary>
      /// Выполняет инференс модели
      /// </summary>
      public static object Execute(object worker, object inputTensor)
      {
            if (!isInitialized)
            {
                  Initialize();
            }

            if (worker == null || inputTensor == null)
            {
                  Debug.LogError("SentisCompat: Worker или входной тензор не найдены");
                  return null;
            }

            try
            {
                  // Ищем метод Execute
                  MethodInfo executeMethod = WorkerType.GetMethod("Execute");
                  if (executeMethod != null)
                  {
                        return executeMethod.Invoke(worker, new object[] { inputTensor });
                  }
                  else
                  {
                        Debug.LogError("SentisCompat: Метод Execute не найден");
                        return null;
                  }
            }
            catch (Exception e)
            {
                  Debug.LogError($"SentisCompat: Ошибка при выполнении инференса: {e.Message}");
                  return null;
            }
      }

      /// <summary>
      /// Создает тензор из текстуры
      /// </summary>
      public static object CreateTensorFromTexture(Texture2D texture)
      {
            if (!isInitialized)
            {
                  Initialize();
            }

            if (texture == null || TensorType == null)
            {
                  Debug.LogError("SentisCompat: Текстура или Tensor не найдены");
                  return null;
            }

            try
            {
                  // Ищем метод CreateTensor
                  MethodInfo createTensorMethod = TensorType.GetMethod("CreateTensor");
                  if (createTensorMethod != null)
                  {
                        return createTensorMethod.Invoke(null, new object[] { texture });
                  }
                  else
                  {
                        Debug.LogError("SentisCompat: Метод CreateTensor не найден");
                        return null;
                  }
            }
            catch (Exception e)
            {
                  Debug.LogError($"SentisCompat: Ошибка при создании тензора: {e.Message}");
                  return null;
            }
      }

      /// <summary>
      /// Освобождает ресурсы Worker
      /// </summary>
      public static void DisposeWorker(object worker)
      {
            if (worker == null || WorkerType == null) return;
            try
            {
                  MethodInfo disposeMethod = WorkerType.GetMethod("Dispose");
                  disposeMethod?.Invoke(worker, null);
            }
            catch (Exception e)
            {
                  Debug.LogError($"SentisCompat: Ошибка при освобождении Worker: {e.Message}");
            }
      }

      /// <summary>
      /// Проверяет, использует ли проект новую версию API Sentis (2.1.x+)
      /// </summary>
      public static bool IsNewSentisAPI()
      {
            Type workerType = null;

            // Ищем тип Worker в сборке Unity.Sentis
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                  if (assembly.GetName().Name == "Unity.Sentis")
                  {
                        workerType = assembly.GetType("Unity.Sentis.Worker");
                        break;
                  }
            }

            // Проверяем наличие класса Worker
            return workerType != null;
      }

      /// <summary>
      /// Загружает модель Sentis независимо от версии API
      /// </summary>
      /// <param name="modelAsset">Ассет модели</param>
      /// <returns>Загруженная модель или null в случае ошибки</returns>
      public static object LoadModel(UnityEngine.Object modelAsset)
      {
            if (!isInitialized) Initialize();
            if (modelAsset == null) { Debug.LogError("SentisCompat: Ассет модели не задан (null)"); return null; }
            if (ModelLoaderType == null) { Debug.LogError("SentisCompat: Тип Unity.Sentis.ModelLoader не найден"); return null; }
            try
            {
                  var loadMethod = ModelLoaderType.GetMethod("Load", new[] { modelAsset.GetType() });
                  if (loadMethod == null) { Debug.LogError($"SentisCompat: Метод Load не найден в ModelLoader для типа {modelAsset.GetType().Name}"); return null; }
                  object model = loadMethod.Invoke(null, new[] { modelAsset });
                  if (model == null) Debug.LogError("SentisCompat: ModelLoader.Load вернул null");
                  else if (debugLogging) Debug.Log($"SentisCompat: Модель успешно загружена: {model.GetType().Name}");
                  return model;
            }
            catch (Exception e)
            {
                  Debug.LogError($"SentisCompat: Ошибка при загрузке модели: {e.Message}");
                  if (e.InnerException != null) Debug.LogError($"SentisCompat: Внутреннее исключение: {e.InnerException.Message}");
                  return null;
            }
      }

      /// <summary>
      /// Диагностирует установку Sentis и выводит информацию о доступных API
      /// </summary>
      public static void DiagnosticReport()
      {
            Debug.Log("=== SentisCompat: Диагностика Unity Sentis ===");
            Assembly sentisAssembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(asm => asm.GetName().Name == "Unity.Sentis");
            if (sentisAssembly == null) { Debug.LogError("SentisCompat: Сборка Unity.Sentis не найдена."); return; }
            Debug.Log($"Сборка Unity.Sentis найдена: версия {sentisAssembly.GetName().Version}");

            Type localModelLoaderType = ModelLoaderType;
            Type localModelType = ModelType;
            Type localWorkerType = WorkerType;
            Type localOpsType = OpsType;
            Type localTensorType = TensorType;

            Debug.Log($"Unity.Sentis.ModelLoader: {(localModelLoaderType != null ? "✓" : "✗")}");
            Debug.Log($"Unity.Sentis.Model: {(localModelType != null ? "✓" : "✗")}");
            Debug.Log($"Unity.Sentis.Worker: {(localWorkerType != null ? "✓" : "✗")}");
            Debug.Log($"Unity.Sentis.Ops: {(localOpsType != null ? "✓" : "✗")}");
            Debug.Log($"Unity.Sentis.Tensor: {(localTensorType != null ? "✓" : "✗")}");

            if (localWorkerType != null)
            {
                  Debug.Log("Обнаружен API Unity.Sentis (Worker существует)");
                  var constructors = localWorkerType.GetConstructors();
                  Debug.Log($"Конструкторы Unity.Sentis.Worker: {constructors.Length}");
                  foreach (var c in constructors) Debug.Log($"- {c}");
            }
            if (localOpsType != null)
            {
                  Debug.Log("Методы в Unity.Sentis.Ops:");
                  LogMethods(localOpsType, BindingFlags.Public | BindingFlags.Static, "Ops Public Static");
            }
            Debug.Log("=== Конец диагностики Unity Sentis ===");
      }

      /// <summary>
      /// Создает простой тензор из текстуры как запасной вариант
      /// </summary>
      private static object CreateSimpleTensor(Texture2D texture)
      {
            try
            {
                  Debug.Log("SentisCompat: Создаем простой тензор как запасной вариант");

                  // Получаем сборку Sentis
                  Assembly sentisAssembly = null;
                  foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                  {
                        if (assembly.GetName().Name == "Unity.Sentis")
                        {
                              sentisAssembly = assembly;
                              break;
                        }
                  }

                  if (sentisAssembly == null)
                  {
                        Debug.LogError("SentisCompat: Сборка Unity.Sentis не найдена");
                        return null;
                  }

                  // Проверяем, что текстура действительна
                  if (texture == null || texture.width <= 0 || texture.height <= 0)
                  {
                        Debug.LogError("SentisCompat: Некорректная текстура для создания тензора");
                        return null;
                  }

                  // Попробуем получить пиксели несколькими способами
                  Color32[] pixels = null;
                  try
                  {
                        pixels = texture.GetPixels32();
                  }
                  catch (Exception pixelEx)
                  {
                        Debug.LogWarning($"SentisCompat: Не удалось получить пиксели текстуры через GetPixels32: {pixelEx.Message}");

                        // Попытка №2: создать новую текстуру, скопировать в неё данные и получить пиксели
                        try
                        {
                              Texture2D tempTexture = new Texture2D(texture.width, texture.height, TextureFormat.RGBA32, false);
                              RenderTexture tempRT = RenderTexture.GetTemporary(texture.width, texture.height, 0, RenderTextureFormat.ARGB32);

                              Graphics.Blit(texture, tempRT);
                              RenderTexture prevRT = RenderTexture.active;
                              RenderTexture.active = tempRT;
                              tempTexture.ReadPixels(new Rect(0, 0, tempRT.width, tempRT.height), 0, 0);
                              tempTexture.Apply();
                              RenderTexture.active = prevRT;
                              RenderTexture.ReleaseTemporary(tempRT);

                              pixels = tempTexture.GetPixels32();
                              UnityEngine.Object.Destroy(tempTexture);
                        }
                        catch (Exception tempEx)
                        {
                              Debug.LogError($"SentisCompat: Не удалось создать временную текстуру: {tempEx.Message}");
                              return null;
                        }
                  }

                  if (pixels == null || pixels.Length == 0)
                  {
                        Debug.LogError("SentisCompat: Не удалось получить данные пикселей");
                        return null;
                  }

                  // Найдем тип TensorFloat и попробуем создать тензор напрямую
                  Type tensorFloatType = sentisAssembly.GetType("Unity.Sentis.TensorFloat");
                  if (tensorFloatType != null)
                  {
                        // Преобразуем данные в одномерный массив
                        float[] floatData = new float[pixels.Length * 3]; // RGB

                        // Разложим RGB каналы
                        for (int i = 0; i < pixels.Length; i++)
                        {
                              floatData[i] = pixels[i].r / 255.0f;                              // R channel
                              floatData[i + pixels.Length] = pixels[i].g / 255.0f;            // G channel
                              floatData[i + pixels.Length * 2] = pixels[i].b / 255.0f;       // B channel
                        }

                        // Создаем shape [1, 3, height, width]
                        Type shapeType = sentisAssembly.GetType("Unity.Sentis.TensorShape");
                        if (shapeType != null)
                        {
                              try
                              {
                                    var constructor = shapeType.GetConstructor(new[] { typeof(int[]) });
                                    if (constructor != null)
                                    {
                                          var shape = constructor.Invoke(new object[] { new[] { 1, 3, texture.height, texture.width } });

                                          // Теперь пробуем создать тензор
                                          var tensorConstructor = tensorFloatType.GetConstructor(new[] { shapeType, typeof(float[]) });
                                          if (tensorConstructor != null)
                                          {
                                                var tensor = tensorConstructor.Invoke(new[] { shape, floatData });
                                                Debug.Log("SentisCompat: Успешно создан простой тензор");
                                                return tensor;
                                          }
                                          else
                                          {
                                                Debug.LogWarning("SentisCompat: Не найден конструктор TensorFloat(shape, float[])");
                                          }
                                    }
                                    else
                                    {
                                          Debug.LogWarning("SentisCompat: Не найден конструктор TensorShape(int[])");
                                    }
                              }
                              catch (Exception shapeEx)
                              {
                                    Debug.LogWarning($"SentisCompat: Ошибка при создании TensorShape: {shapeEx.Message}");
                              }

                              // Резервный вариант - попробуем другой конструктор, если доступен
                              try
                              {
                                    // Ищем другие конструкторы
                                    var constructors = tensorFloatType.GetConstructors();
                                    Debug.Log($"SentisCompat: Доступно {constructors.Length} конструкторов TensorFloat");

                                    foreach (var ctor in constructors)
                                    {
                                          var parameters = ctor.GetParameters();
                                          if (parameters.Length == 1 && parameters[0].ParameterType == typeof(float[]))
                                          {
                                                // Простой массив float[]
                                                var tensor = ctor.Invoke(new object[] { floatData });
                                                Debug.Log("SentisCompat: Создан тензор через альтернативный конструктор");
                                                return tensor;
                                          }
                                    }
                              }
                              catch (Exception altEx)
                              {
                                    Debug.LogWarning($"SentisCompat: Ошибка при использовании альтернативного конструктора: {altEx.Message}");
                              }
                        }
                        else
                        {
                              Debug.LogWarning("SentisCompat: Тип Unity.Sentis.TensorShape не найден");
                        }
                  }
                  else
                  {
                        Debug.LogWarning("SentisCompat: Тип Unity.Sentis.TensorFloat не найден");
                  }

                  Debug.LogWarning("SentisCompat: Не удалось создать простой тензор");
                  return null;
            }
            catch (Exception e)
            {
                  Debug.LogError($"SentisCompat: Ошибка при создании простого тензора: {e.Message}");
                  return null;
            }
      }

      /// <summary>
      /// Преобразует Texture2D в тензор для использования в нейронной сети
      /// </summary>
      public static object TextureToTensor(Texture2D texture)
      {
            if (!isInitialized) Initialize();
            if (texture == null) { Debug.LogError("SentisCompat: Текстура равна null"); return null; }
            if (!isInitialized) { Debug.LogError("SentisCompat: Инициализация не удалась, не могу продолжить TextureToTensor."); return null; }

            Assembly sentisAssembly = ModelLoaderType?.Assembly;
            if (sentisAssembly == null)
            {
                  // Fallback if modelLoaderType was somehow null
                  sentisAssembly = AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(asm => asm.GetName().Name == "Unity.Sentis");
                  if (sentisAssembly == null) { Debug.LogError("SentisCompat: Сборка Unity.Sentis не найдена."); return null; }
            }

            if (debugLogging) Debug.Log($"SentisCompat: Используется сборка {sentisAssembly.FullName} для TextureToTensor.");
            if (debugLogging) Debug.Log($"SentisCompat: Текстура размером {texture.width}x{texture.height}, формат {texture.format}");

            if (TextureConverterType != null)
            {
                  if (debugLogging) Debug.Log("SentisCompat: Найден тип Unity.Sentis.TextureConverter.");
                  MethodInfo toTensorMethod = null;
                  string foundMethodSignature = "";

                  // Try specific overloads first
                  toTensorMethod = TextureConverterType.GetMethod("ToTensor", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(Texture2D) }, null);
                  if (toTensorMethod != null) foundMethodSignature = "ToTensor(Texture2D)";
                  else
                  {
                        toTensorMethod = TextureConverterType.GetMethod("ToTensor", BindingFlags.Public | BindingFlags.Static, null, new Type[] { typeof(Texture) }, null);
                        if (toTensorMethod != null) foundMethodSignature = "ToTensor(Texture)";
                  }

                  if (toTensorMethod != null)
                  {
                        if (debugLogging) Debug.Log($"SentisCompat: Найден метод TextureConverter.{foundMethodSignature}. Попытка вызова...");
                        try
                        {
                              object result = toTensorMethod.Invoke(null, new object[] { texture });
                              if (result != null) { if (debugLogging) Debug.Log($"SentisCompat: Тензор успешно создан через TextureConverter.{foundMethodSignature}."); return result; }
                              else Debug.LogWarning($"SentisCompat: TextureConverter.{foundMethodSignature} вернул null.");
                        }
                        catch (Exception e) { Debug.LogWarning($"SentisCompat: Ошибка при вызове TextureConverter.{foundMethodSignature}: {e.Message}\n{e.StackTrace}"); }
                  }
                  else Debug.LogWarning("SentisCompat: Методы TextureConverter.ToTensor(Texture2D) и ToTensor(Texture) не найдены прямым поиском. Попытка поиска по перегрузкам...");

                  // Fallback to iterating all ToTensor methods
                  if (toTensorMethod == null)
                  {
                        var allToTensorMethods = TextureConverterType.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.Name == "ToTensor").ToArray();
                        if (debugLogging) Debug.Log($"SentisCompat: Найдено {allToTensorMethods.Length} методов с именем ToTensor. Поиск подходящего...");
                        foreach (var methodCandidate in allToTensorMethods)
                        {
                              var parameters = methodCandidate.GetParameters();
                              if (parameters.Length > 0 && typeof(Texture).IsAssignableFrom(parameters[0].ParameterType))
                              {
                                    object[] args = new object[parameters.Length];
                                    args[0] = texture;
                                    bool canInvoke = true;
                                    for (int i = 1; i < parameters.Length; i++)
                                    {
                                          var paramInfo = parameters[i];
                                          if (paramInfo.Name.ToLower().Contains("width")) args[i] = texture.width;
                                          else if (paramInfo.Name.ToLower().Contains("height")) args[i] = texture.height;
                                          else if (paramInfo.Name.ToLower().Contains("channels")) args[i] = 3;
                                          else if (paramInfo.ParameterType == typeof(int) && paramInfo.Name.ToLower().Contains("batch")) args[i] = 1;
                                          else if (paramInfo.ParameterType == typeof(bool) && paramInfo.Name.ToLower().Contains("linear")) args[i] = false;
                                          else if (paramInfo.ParameterType.Name.Contains("TextureTransform"))
                                          {
                                                try { args[i] = Activator.CreateInstance(paramInfo.ParameterType); } // Basic instance
                                                catch { Debug.LogWarning($"SentisCompat: Не удалось создать TextureTransform для {paramInfo.Name}"); canInvoke = false; break; }
                                          }
                                          else if (paramInfo.HasDefaultValue) args[i] = paramInfo.DefaultValue;
                                          else { Debug.LogWarning($"SentisCompat: Не удалось предоставить аргумент для '{paramInfo.Name}' ({paramInfo.ParameterType.Name})"); canInvoke = false; break; }
                                    }
                                    if (canInvoke)
                                    {
                                          if (debugLogging) Debug.Log($"SentisCompat: Пробуем метод {methodCandidate.Name}({string.Join(", ", parameters.Select(p => p.ParameterType.Name + " " + p.Name))})");
                                          try
                                          {
                                                object result = methodCandidate.Invoke(null, args);
                                                if (result != null) { if (debugLogging) Debug.Log("SentisCompat: Тензор успешно создан через одну из перегрузок TextureConverter.ToTensor."); return result; }
                                          }
                                          catch (Exception e) { Debug.LogWarning($"SentisCompat: Ошибка при вызове {methodCandidate.Name}: {e.InnerException?.Message ?? e.Message}"); }
                                    }
                              }
                        }
                  }
            }
            else Debug.LogError("SentisCompat: Тип Unity.Sentis.TextureConverter не найден.");

            Debug.LogError("SentisCompat: Не удалось создать тензор через TextureConverter. Возвращаем null.");
            return null;
      }

      /// <summary>
      /// Отрисовывает тензор в RenderTexture, работает с любой версией Sentis API
      /// </summary>
      /// <param name="tensor">Тензор с результатом сегментации</param>
      /// <param name="targetTexture">Целевая текстура для отрисовки</param>
      /// <returns>true, если отрисовка успешна, иначе false</returns>
      public static bool RenderTensorToTexture(object tensor, RenderTexture targetTexture)
      {
            if (tensor == null || targetTexture == null)
            {
                  Debug.LogError("SentisCompat: Тензор или целевая текстура не заданы (null).");
                  return false;
            }
            if (!isInitialized) Initialize();

            // Эти типы должны быть инициализированы в Initialize()
            Type actualTextureConverterType = TextureConverterType;
            Type actualTensorType = TensorType;
            Type actualTextureTransformType = null;

            // Получаем TextureTransformType из той же сборки, что и TextureConverterType
            if (actualTextureConverterType != null)
            {
                  actualTextureTransformType = actualTextureConverterType.Assembly.GetType("Unity.Sentis.TextureTransform");
            }
            else
            {
                  // Попытка найти TextureTransformType через другую известную сборку, если TextureConverterType был null
                  Assembly sentisAssembly = ModelType?.Assembly ?? AppDomain.CurrentDomain.GetAssemblies().FirstOrDefault(asm => asm.GetName().Name == "Unity.Sentis");
                  if (sentisAssembly != null)
                  {
                        actualTextureTransformType = sentisAssembly.GetType("Unity.Sentis.TextureTransform");
                  }
            }

            // Проверки на null для критичных типов
            if (actualTextureConverterType == null)
            {
                  Debug.LogError("SentisCompat: TextureConverterType не инициализирован. Невозможно выполнить RenderTensorToTexture.");
                  FillTextureWithPlaceholder(targetTexture);
                  return false;
            }
            if (actualTensorType == null)
            {
                  Debug.LogError("SentisCompat: TensorType не инициализирован. Невозможно выполнить RenderTensorToTexture.");
                  FillTextureWithPlaceholder(targetTexture);
                  return false;
            }
            if (!actualTensorType.IsInstanceOfType(tensor))
            {
                  Debug.LogError($"SentisCompat: Предоставленный тензор имеет неверный тип ({tensor.GetType().FullName}). Ожидался {actualTensorType.FullName}.");
                  FillTextureWithPlaceholder(targetTexture);
                  return false;
            }
            if (actualTextureTransformType == null && debugLogging)
            {
                  Debug.LogWarning("SentisCompat: Тип Unity.Sentis.TextureTransform не найден. Попытка продолжить без него.");
            }

            if (debugLogging) Debug.Log($"SentisCompat: RenderTensorToTexture: Using TextureConverter: {actualTextureConverterType.FullName}, Tensor: {actualTensorType.FullName}");

            if (debugLogging)
            {
                  Debug.Log("SentisCompat: --- Detailed TextureConverter Method Diagnostics START ---");
                  LogMethods(actualTextureConverterType, BindingFlags.Public | BindingFlags.Static, "TextureConverter Public Static");
                  LogMethods(actualTextureConverterType, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance, "TextureConverter ALL Methods");
                  Debug.Log("SentisCompat: --- Detailed TextureConverter Method Diagnostics END ---");
            }

            object textureTransformInstance = null;
            if (actualTextureTransformType != null)
            {
                  PropertyInfo identityProperty = actualTextureTransformType.GetProperty("identity", BindingFlags.Public | BindingFlags.Static);
                  if (identityProperty != null)
                  {
                        try
                        {
                              textureTransformInstance = identityProperty.GetValue(null);
                              if (debugLogging) Debug.Log(textureTransformInstance != null ? "SentisCompat: Успешно получен TextureTransform.identity." : "SentisCompat: TextureTransform.identity is null after GetValue.");
                        }
                        catch (Exception ex) { if (debugLogging) Debug.LogWarning($"SentisCompat: Ошибка при получении TextureTransform.identity: {ex.Message}"); }
                  }
                  else { if (debugLogging) Debug.LogWarning("SentisCompat: Property TextureTransform.identity not found."); }

                  if (textureTransformInstance == null) // Если identity не сработало, пытаемся создать через конструктор
                  {
                        if (debugLogging) Debug.LogWarning("SentisCompat: TextureTransform.identity не доступен. Попытка создать экземпляр TextureTransform через конструктор по умолчанию.");
                        ConstructorInfo constructor = actualTextureTransformType.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);
                        if (constructor != null)
                        {
                              try
                              {
                                    textureTransformInstance = constructor.Invoke(null);
                                    if (debugLogging) Debug.Log("SentisCompat: Успешно создан экземпляр TextureTransform через конструктор по умолчанию.");
                              }
                              catch (Exception ex) { if (debugLogging) Debug.LogWarning($"SentisCompat: Ошибка при создании TextureTransform через конструктор: {ex.Message}"); }
                        }
                        else { if (debugLogging) Debug.LogWarning("SentisCompat: Default constructor for TextureTransform not found."); }
                  }
            }

            if (textureTransformInstance == null && debugLogging) Debug.LogWarning("SentisCompat: Не удалось получить или создать экземпляр TextureTransform. Он будет null. Это может повлиять на некоторые перегрузки RenderToTexture.");

            var methods = actualTextureConverterType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                                      .Where(m => m.Name == "RenderToTexture")
                                      .OrderByDescending(m => m.GetParameters().Length) // Предпочитаем более специфичные (больше параметров)
                                      .ToList();

            if (debugLogging && methods.Any())
            {
                  Debug.Log($"SentisCompat: Найдено {methods.Count} методов с именем RenderToTexture в {actualTextureConverterType.FullName}. Поиск подходящего...");
                  foreach (var m in methods)
                  {
                        var parameters = m.GetParameters();
                        string paramString = string.Join(", ", parameters.Select(p => $"{p.ParameterType.Name} {p.Name}"));
                        Debug.Log($"SentisCompat:   Кандидат: RenderToTexture({paramString})");
                  }
            }
            else if (debugLogging)
            {
                  Debug.LogWarning($"SentisCompat: Не найдено методов с именем RenderToTexture в {actualTextureConverterType.FullName}.");
            }

            // Попытка 1: RenderToTexture(Tensor, RenderTexture, TextureTransform)
            MethodInfo renderMethod = methods.FirstOrDefault(m =>
            {
                  var parameters = m.GetParameters();
                  return parameters.Length == 3 &&
                         actualTensorType.IsAssignableFrom(parameters[0].ParameterType) &&
                         parameters[1].ParameterType == typeof(RenderTexture) &&
                         actualTextureTransformType != null && parameters[2].ParameterType.IsAssignableFrom(actualTextureTransformType);
            });

            if (renderMethod != null)
            {
                  if (debugLogging) Debug.Log("SentisCompat: Попытка вызова RenderToTexture(Tensor, RenderTexture, TextureTransform).");
                  if (textureTransformInstance == null && debugLogging) Debug.LogWarning("SentisCompat: textureTransformInstance is null, но все равно пытаемся вызвать перегрузку с 3 параметрами.");
                  try
                  {
                        renderMethod.Invoke(null, new object[] { tensor, targetTexture, textureTransformInstance });
                        if (debugLogging) Debug.Log("SentisCompat: Текстура успешно отрисована через TextureConverter.RenderToTexture(Tensor, RenderTexture, TextureTransform).");
                        return true;
                  }
                  catch (Exception e)
                  {
                        Debug.LogError($"SentisCompat: Ошибка при вызове TextureConverter.RenderToTexture(Tensor, RenderTexture, TextureTransform): {e.Message}\\\\n{e.StackTrace}");
                        // Ошибка с 3-параметрической версией, продолжаем к 2-параметрической
                  }
            }

            // Попытка 2: RenderToTexture(Tensor, RenderTexture)
            renderMethod = methods.FirstOrDefault(m =>
            {
                  var parameters = m.GetParameters();
                  return parameters.Length == 2 &&
                         actualTensorType.IsAssignableFrom(parameters[0].ParameterType) &&
                         parameters[1].ParameterType == typeof(RenderTexture);
            });

            if (renderMethod != null)
            {
                  if (debugLogging) Debug.Log("SentisCompat: Попытка вызова RenderToTexture(Tensor, RenderTexture).");
                  try
                  {
                        renderMethod.Invoke(null, new object[] { tensor, targetTexture });
                        if (debugLogging) Debug.Log("SentisCompat: Текстура успешно отрисована через TextureConverter.RenderToTexture(Tensor, RenderTexture).");
                        return true;
                  }
                  catch (Exception e)
                  {
                        Debug.LogError($"SentisCompat: Ошибка при вызове TextureConverter.RenderToTexture(Tensor, RenderTexture): {e.Message}\\\\n{e.StackTrace}");
                  }
            }

            Debug.LogError($"SentisCompat: Не найдено подходящих методов RenderToTexture в {actualTextureConverterType.FullName} после всех попыток.");
            FillTextureWithPlaceholder(targetTexture);
            return false;
      }

      /// <summary>
      /// Ручная отрисовка тензора в текстуру, когда стандартные методы не работают
      /// </summary>
      private static bool RenderTensorManually(object tensor, RenderTexture targetTexture)
      {
            // Этот метод теперь является устаревшим и не должен вызываться,
            // так как Ops.RenderToTexture должен быть основным способом.
            // Оставляем его для истории или крайней отладки, но он не должен быть частью основного потока.
            Debug.LogWarning("SentisCompat: Вызван устаревший метод RenderTensorManually. Это не должно происходить в штатном режиме.");
            FillTextureWithPlaceholder(targetTexture);
            return true; // Возвращаем true, чтобы не сломать логику, если кто-то его все же вызовет, но результат будет плейсхолдером
      }

      /// <summary>
      /// Заполняет текстуру плейсхолдер-изображением в случае ошибки
      /// </summary>
      private static void FillTextureWithPlaceholder(RenderTexture targetTexture)
      {
            if (targetTexture == null) return;
            RenderTexture active = RenderTexture.active;
            RenderTexture.active = targetTexture;
            GL.Clear(true, true, Color.magenta); // Bright color to indicate error/placeholder
            RenderTexture.active = active;
            if (debugLogging) Debug.Log("SentisCompat: Создан плейсхолдер вместо тензора (заливка magenta)");
      }

      // Helper method to log methods of a type
      private static void LogMethods(Type type, BindingFlags flags, string description)
      {
            if (type == null) { Debug.LogWarning($"SentisCompat: LogMethods: Type for '{description}' is null."); return; }
            if (debugLogging) Debug.Log($"SentisCompat: LogMethods: Searching for [{description}] methods in [{type.FullName}] from assembly [{type.Assembly.GetName().FullName}] with flags [{flags}]...");

            MethodInfo[] methods = type.GetMethods(flags);
            if (methods.Length == 0)
            {
                  Debug.LogWarning($"SentisCompat: LogMethods: No [{description}] methods found in [{type.FullName}] with flags [{flags}].");
            }
            else
            {
                  if (debugLogging) Debug.Log($"SentisCompat: LogMethods: Found {methods.Length} [{description}] method(s) in [{type.FullName}] with flags [{flags}]:");
                  foreach (var method in methods)
                  {
                        var parameters = method.GetParameters().Select(p => $"{p.ParameterType.Name} {p.Name}").ToArray(); // Simplified parameter type name for brevity
                        if (debugLogging) Debug.Log($"SentisCompat:   - {(method.ReturnType.IsGenericType ? method.ReturnType.Name.Split('`')[0] + "<" + string.Join(", ", method.ReturnType.GetGenericArguments().Select(ga => ga.Name)) + ">" : method.ReturnType.Name)} {method.Name}({string.Join(", ", parameters)})");
                  }
            }
      }
}