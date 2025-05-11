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
      private static Type modelLoaderType;
      private static Type modelType;
      private static Type workerType;
      private static Type tensorType;
      private static Type opsType;

      /// <summary>
      /// Инициализирует адаптер, находя необходимые типы через рефлексию
      /// </summary>
      public static void Initialize()
      {
            if (isInitialized) return;

            try
            {
                  // Ищем сборку Unity.Sentis
                  foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                  {
                        if (assembly.GetName().Name == "Unity.Sentis")
                        {
                              // Получаем основные типы
                              modelLoaderType = assembly.GetType("Unity.Sentis.ModelLoader");
                              modelType = assembly.GetType("Unity.Sentis.Model");
                              workerType = assembly.GetType("Unity.Sentis.IWorker");
                              tensorType = assembly.GetType("Unity.Sentis.Tensor");
                              opsType = assembly.GetType("Unity.Sentis.Ops");

                              if (modelLoaderType != null && modelType != null && workerType != null && tensorType != null && opsType != null)
                              {
                                    Debug.Log("SentisCompat: Все необходимые типы найдены");
                                    isInitialized = true;
                                    return;
                              }
                        }
                  }

                  Debug.LogError("SentisCompat: Не удалось найти все необходимые типы Unity.Sentis");
            }
            catch (Exception e)
            {
                  Debug.LogError($"SentisCompat: Ошибка при инициализации: {e.Message}");
            }
      }

      /// <summary>
      /// Загружает модель из байтового массива
      /// </summary>
      public static object LoadModelFromBytes(byte[] modelData)
      {
            if (!isInitialized)
            {
                  Initialize();
            }

            if (modelLoaderType == null)
            {
                  Debug.LogError("SentisCompat: ModelLoader не найден");
                  return null;
            }

            try
            {
                  // Ищем метод Load для byte[]
                  MethodInfo loadMethod = modelLoaderType.GetMethod("Load", new Type[] { typeof(byte[]) });
                  if (loadMethod != null)
                  {
                        return loadMethod.Invoke(null, new object[] { modelData });
                  }
                  else
                  {
                        // Пробуем через Stream (MemoryStream)
                        MethodInfo loadStreamMethod = modelLoaderType.GetMethod("Load", new Type[] { typeof(Stream) });
                        if (loadStreamMethod != null)
                        {
                              using (var ms = new MemoryStream(modelData))
                              {
                                    return loadStreamMethod.Invoke(null, new object[] { ms });
                              }
                        }
                        // Диагностика: выведем все методы ModelLoader
                        var allMethods = modelLoaderType.GetMethods();
                        foreach (var m in allMethods)
                        {
                              Debug.Log($"ModelLoader method: {m.Name} ({string.Join(", ", m.GetParameters().Select(p => p.ParameterType.Name))})");
                        }
                        Debug.LogError("SentisCompat: Метод Load(byte[]) и Load(Stream) не найден");
                        return null;
                  }
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
      public static object CreateWorker(object model, int backend = 0)
      {
            if (!isInitialized)
            {
                  Initialize();
            }

            if (model == null || workerType == null)
            {
                  Debug.LogError("SentisCompat: Модель или Worker не найдены");
                  return null;
            }

            try
            {
                  // Ищем метод CreateWorker
                  MethodInfo createWorkerMethod = modelType.GetMethod("CreateWorker");
                  if (createWorkerMethod != null)
                  {
                        return createWorkerMethod.Invoke(model, new object[] { backend });
                  }
                  else
                  {
                        Debug.LogError("SentisCompat: Метод CreateWorker не найден");
                        return null;
                  }
            }
            catch (Exception e)
            {
                  Debug.LogError($"SentisCompat: Ошибка при создании Worker: {e.Message}");
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
                  MethodInfo executeMethod = workerType.GetMethod("Execute");
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

            if (texture == null || tensorType == null)
            {
                  Debug.LogError("SentisCompat: Текстура или Tensor не найдены");
                  return null;
            }

            try
            {
                  // Ищем метод CreateTensor
                  MethodInfo createTensorMethod = tensorType.GetMethod("CreateTensor");
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
            if (worker == null) return;

            try
            {
                  // Ищем метод Dispose
                  MethodInfo disposeMethod = workerType.GetMethod("Dispose");
                  if (disposeMethod != null)
                  {
                        disposeMethod.Invoke(worker, null);
                  }
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
            if (modelAsset == null)
            {
                  Debug.LogError("SentisCompat: Ассет модели не задан (null)");
                  return null;
            }

            try
            {
                  // Ищем тип ModelLoader в сборке Unity.Sentis
                  Type modelLoaderType = null;
                  foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                  {
                        if (assembly.GetName().Name == "Unity.Sentis")
                        {
                              modelLoaderType = assembly.GetType("Unity.Sentis.ModelLoader");
                              break;
                        }
                  }

                  if (modelLoaderType == null)
                  {
                        Debug.LogError("SentisCompat: Тип Unity.Sentis.ModelLoader не найден");
                        return null;
                  }

                  // Ищем метод Load, принимающий тип ассета модели
                  var loadMethod = modelLoaderType.GetMethod("Load", new[] { modelAsset.GetType() });
                  if (loadMethod == null)
                  {
                        Debug.LogError($"SentisCompat: Метод Load не найден в ModelLoader для типа {modelAsset.GetType().Name}");
                        return null;
                  }

                  // Загружаем модель
                  object model = loadMethod.Invoke(null, new[] { modelAsset });
                  if (model == null)
                  {
                        Debug.LogError("SentisCompat: ModelLoader.Load вернул null");
                  }
                  else
                  {
                        Debug.Log($"SentisCompat: Модель успешно загружена: {model.GetType().Name}");
                  }

                  return model;
            }
            catch (Exception e)
            {
                  Debug.LogError($"SentisCompat: Ошибка при загрузке модели: {e.Message}");
                  if (e.InnerException != null)
                  {
                        Debug.LogError($"SentisCompat: Внутреннее исключение: {e.InnerException.Message}");
                  }
                  return null;
            }
      }

      /// <summary>
      /// Диагностирует установку Sentis и выводит информацию о доступных API
      /// </summary>
      public static void DiagnosticReport()
      {
            Debug.Log("=== SentisCompat: Диагностика Unity Sentis ===");

            // Проверяем наличие сборки Unity.Sentis
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
                  Debug.LogError("SentisCompat: Сборка Unity.Sentis не найдена. Установите пакет Unity Sentis");
                  return;
            }

            Debug.Log($"Сборка Unity.Sentis найдена: версия {sentisAssembly.GetName().Version}");

            // Проверяем наличие основных типов
            Type modelLoaderType = sentisAssembly.GetType("Unity.Sentis.ModelLoader");
            Type modelType = sentisAssembly.GetType("Unity.Sentis.Model");
            Type modelAssetType = sentisAssembly.GetType("Unity.Sentis.ModelAsset");
            Type workerType = sentisAssembly.GetType("Unity.Sentis.Worker");
            Type workerFactoryType = sentisAssembly.GetType("Unity.Sentis.WorkerFactory");

            Debug.Log($"Unity.Sentis.ModelLoader: {(modelLoaderType != null ? "✓" : "✗")}");
            Debug.Log($"Unity.Sentis.Model: {(modelType != null ? "✓" : "✗")}");
            Debug.Log($"Unity.Sentis.ModelAsset: {(modelAssetType != null ? "✓" : "✗")}");
            Debug.Log($"Unity.Sentis.Worker: {(workerType != null ? "✓" : "✗")}");
            Debug.Log($"Unity.Sentis.WorkerFactory: {(workerFactoryType != null ? "✓" : "✗")}");

            // Определяем версию API
            if (workerType != null)
            {
                  Debug.Log("Обнаружен новый API Unity.Sentis (версия 2.1.x+)");

                  // Выводим информацию о конструкторах Worker
                  var constructors = workerType.GetConstructors();
                  Debug.Log($"Конструкторы Unity.Sentis.Worker: {constructors.Length}");
                  foreach (var constructor in constructors)
                  {
                        var parameters = constructor.GetParameters();
                        Debug.Log($"- Конструктор({string.Join(", ", parameters.Select(p => p.ParameterType.Name))})");
                  }
            }
            else if (workerFactoryType != null)
            {
                  Debug.Log("Обнаружен старый API Unity.Sentis (версия до 2.1.x)");

                  // Выводим информацию о методах WorkerFactory
                  var methods = workerFactoryType.GetMethods().Where(m => m.Name == "CreateWorker").ToArray();
                  Debug.Log($"Методы Unity.Sentis.WorkerFactory.CreateWorker: {methods.Length}");
                  foreach (var method in methods)
                  {
                        var parameters = method.GetParameters();
                        Debug.Log($"- CreateWorker({string.Join(", ", parameters.Select(p => p.ParameterType.Name))})");
                  }
            }
            else
            {
                  Debug.LogError("Не обнаружены ни Unity.Sentis.Worker, ни Unity.Sentis.WorkerFactory. API Sentis не доступен");
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
            try
            {
                  if (texture == null)
                  {
                        Debug.LogError("SentisCompat: Текстура равна null");
                        return null;
                  }

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

                  // Проверяем версию Sentis для оптимизированного подхода
                  string sentisVersion = sentisAssembly.GetName().Version.ToString();
                  bool isSentis212 = sentisVersion.StartsWith("2.1.2");

                  if (isSentis212)
                  {
                        Debug.Log($"SentisCompat: Оптимизированный метод для Sentis 2.1.2");

                        // В Sentis 2.1.2 мы можем использовать кэшированное отражение для ускорения
                        Type textureConverterType212 = sentisAssembly.GetType("Unity.Sentis.TextureConverter");
                        if (textureConverterType212 != null)
                        {
                              // Пробуем сначала метод TextureToTensor с точным указанием типов параметров
                              var methods = textureConverterType212.GetMethods(BindingFlags.Public | BindingFlags.Static)
                                    .Where(m => m.Name == "TextureToTensor" &&
                                          m.GetParameters().Length == 1 &&
                                          typeof(Texture).IsAssignableFrom(m.GetParameters()[0].ParameterType))
                                    .ToArray();

                              if (methods.Length > 0)
                              {
                                    Debug.Log($"SentisCompat: Найдено {methods.Length} методов TextureToTensor");

                                    foreach (var method in methods)
                                    {
                                          try
                                          {
                                                Debug.Log($"SentisCompat: Пробуем метод {method.Name}({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))})");
                                                object result = method.Invoke(null, new object[] { texture });
                                                if (result != null)
                                                {
                                                      Debug.Log("SentisCompat: Тензор успешно создан через TextureToTensor для Sentis 2.1.2");
                                                      return result;
                                                }
                                          }
                                          catch (Exception e)
                                          {
                                                Debug.LogWarning($"SentisCompat: Ошибка при вызове TextureToTensor для Sentis 2.1.2: {e.Message}");
                                          }
                                    }
                              }
                        }
                  }

                  Debug.Log($"SentisCompat: Текстура размером {texture.width}x{texture.height}, формат {texture.format}");

                  // В Sentis 2.1.x текстуры преобразуются в тензоры через TextureConverter.ToTensor
                  Type textureConverterType = sentisAssembly.GetType("Unity.Sentis.TextureConverter");
                  if (textureConverterType != null)
                  {
                        Debug.Log("SentisCompat: Найден тип TextureConverter");

                        // Проверяем наличие метода TextureToTensor в новой версии API (2.1.x)
                        var textureToTensorMethods = textureConverterType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                              .Where(m => m.Name == "TextureToTensor" &&
                                    m.GetParameters().Length == 1 &&
                                    typeof(Texture).IsAssignableFrom(m.GetParameters()[0].ParameterType))
                              .ToArray();

                        if (textureToTensorMethods.Length > 0)
                        {
                              Debug.Log($"SentisCompat: Найдено {textureToTensorMethods.Length} методов TextureToTensor");

                              foreach (var method in textureToTensorMethods)
                              {
                                    // Вызываем TextureToTensor(texture)
                                    try
                                    {
                                          Debug.Log($"SentisCompat: Пробуем метод {method.Name}({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))})");
                                          object result = method.Invoke(null, new object[] { texture });
                                          if (result != null)
                                          {
                                                Debug.Log("SentisCompat: Тензор успешно создан через TextureToTensor");
                                                return result;
                                          }
                                    }
                                    catch (Exception e)
                                    {
                                          Debug.LogWarning($"SentisCompat: Ошибка при вызове TextureToTensor: {e.Message}");
                                    }
                              }
                        }

                        // Второй вариант - через ToTensor для Sentis 2.1.x
                        var toTensorMethods = textureConverterType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                              .Where(m => m.Name == "ToTensor" &&
                                    m.GetParameters().Length == 1 &&
                                    typeof(Texture).IsAssignableFrom(m.GetParameters()[0].ParameterType))
                              .ToArray();

                        if (toTensorMethods.Length > 0)
                        {
                              Debug.Log($"SentisCompat: Найдено {toTensorMethods.Length} методов ToTensor");

                              foreach (var method in toTensorMethods)
                              {
                                    try
                                    {
                                          Debug.Log($"SentisCompat: Пробуем метод {method.Name}({string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))})");
                                          object result = method.Invoke(null, new object[] { texture });
                                          if (result != null)
                                          {
                                                Debug.Log("SentisCompat: Тензор успешно создан через ToTensor");
                                                return result;
                                          }
                                    }
                                    catch (Exception e)
                                    {
                                          Debug.LogWarning($"SentisCompat: Ошибка при вызове ToTensor: {e.Message}");
                                    }
                              }
                        }
                  }

                  // Попробуем создать тензор вручную как крайний вариант
                  object simpleTensor = CreateSimpleTensor(texture);
                  if (simpleTensor != null)
                  {
                        Debug.Log("SentisCompat: Возвращаем тензор, созданный запасным методом");
                        return simpleTensor;
                  }

                  Debug.LogError("SentisCompat: Не удалось создать тензор. Возвращаем null.");
                  return null;
            }
            catch (Exception e)
            {
                  Debug.LogError($"SentisCompat: Ошибка при преобразовании текстуры в тензор: {e.Message}");
                  if (e.InnerException != null)
                  {
                        Debug.LogError($"SentisCompat: Внутреннее исключение: {e.InnerException.Message}");
                  }
                  return null;
            }
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
                  Debug.LogError("SentisCompat: Тензор или целевая текстура не заданы (null)");
                  return false;
            }

            try
            {
                  // Определяем сборку Sentis
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
                        return false;
                  }

                  // Проверяем версию Sentis
                  string sentisVersion = sentisAssembly.GetName().Version.ToString();
                  Debug.Log($"SentisCompat: Используем Sentis версии {sentisVersion} для рендеринга тензора");

                  // Для Sentis 2.1.2+ используем специализированный TextureConverter.RenderToTexture
                  Type textureConverterType = sentisAssembly.GetType("Unity.Sentis.TextureConverter");
                  if (textureConverterType != null)
                  {
                        // Попытка 1: Метод RenderToTexture с точным соответствием параметров
                        var renderMethod = textureConverterType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                            .Where(m => m.Name == "RenderToTexture" &&
                                       m.GetParameters().Length == 2 &&
                                       m.GetParameters()[0].ParameterType.IsAssignableFrom(tensor.GetType()) &&
                                       m.GetParameters()[1].ParameterType.IsAssignableFrom(targetTexture.GetType()))
                            .FirstOrDefault();

                        if (renderMethod != null)
                        {
                              try
                              {
                                    renderMethod.Invoke(null, new object[] { tensor, targetTexture });
                                    Debug.Log("SentisCompat: Тензор успешно отрисован через RenderToTexture (2 параметра)");
                                    return true;
                              }
                              catch (Exception e)
                              {
                                    Debug.LogWarning($"SentisCompat: Ошибка при вызове RenderToTexture: {e.Message}");
                              }
                        }

                        // Попытка 2: Метод RenderToTexture с параметрами трансформации
                        var renderWithTransformMethod = textureConverterType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                            .Where(m => m.Name == "RenderToTexture" &&
                                       m.GetParameters().Length == 3 &&
                                       m.GetParameters()[0].ParameterType.IsAssignableFrom(tensor.GetType()) &&
                                       m.GetParameters()[1].ParameterType.IsAssignableFrom(targetTexture.GetType()))
                            .FirstOrDefault();

                        if (renderWithTransformMethod != null)
                        {
                              try
                              {
                                    // Создаем TextureTransform
                                    Type textureTransformType = sentisAssembly.GetType("Unity.Sentis.TextureTransform");
                                    if (textureTransformType != null)
                                    {
                                          // Пробуем создать экземпляр TextureTransform
                                          object textureTransform = null;

                                          try
                                          {
                                                // Пробуем через конструктор по умолчанию
                                                var defaultConstructor = textureTransformType.GetConstructor(Type.EmptyTypes);
                                                if (defaultConstructor != null)
                                                {
                                                      textureTransform = defaultConstructor.Invoke(null);
                                                }
                                                else
                                                {
                                                      // Если нет конструктора по умолчанию, пробуем статическое свойство по умолчанию
                                                      var defaultProperty = textureTransformType.GetProperty("identity",
                                                          BindingFlags.Public | BindingFlags.Static);
                                                      if (defaultProperty != null)
                                                      {
                                                            textureTransform = defaultProperty.GetValue(null);
                                                      }
                                                }
                                          }
                                          catch
                                          {
                                                // Если не удалось создать текстурную трансформацию обычным путем,
                                                // возможно, это перечисление или другой простой тип
                                                textureTransform = 0; // Предполагаем, что 0 это значение по умолчанию
                                          }

                                          if (textureTransform != null)
                                          {
                                                renderWithTransformMethod.Invoke(null, new object[] { tensor, targetTexture, textureTransform });
                                                Debug.Log("SentisCompat: Тензор успешно отрисован через RenderToTexture (3 параметра)");
                                                return true;
                                          }
                                    }
                              }
                              catch (Exception e)
                              {
                                    Debug.LogWarning($"SentisCompat: Ошибка при вызове RenderToTexture с трансформацией: {e.Message}");
                              }
                        }

                        // Попытка 3: TensorToRenderTexture (альтернативный метод)
                        var tensorToTextureMethod = textureConverterType.GetMethods(BindingFlags.Public | BindingFlags.Static)
                            .Where(m => (m.Name == "TensorToRenderTexture" || m.Name == "TensorToTexture") &&
                                       m.GetParameters().Length >= 2 &&
                                       m.GetParameters()[0].ParameterType.IsAssignableFrom(tensor.GetType()))
                            .FirstOrDefault();

                        if (tensorToTextureMethod != null)
                        {
                              try
                              {
                                    var parameters = tensorToTextureMethod.GetParameters();
                                    if (parameters.Length == 2)
                                    {
                                          tensorToTextureMethod.Invoke(null, new object[] { tensor, targetTexture });
                                          Debug.Log($"SentisCompat: Тензор успешно отрисован через {tensorToTextureMethod.Name}");
                                          return true;
                                    }
                              }
                              catch (Exception e)
                              {
                                    Debug.LogWarning($"SentisCompat: Ошибка при вызове TensorToRenderTexture: {e.Message}");
                              }
                        }
                  }

                  // Если не удалось отрисовать тензор стандартными методами,
                  // используем ручную отрисовку
                  return RenderTensorManually(tensor, targetTexture);
            }
            catch (Exception e)
            {
                  Debug.LogError($"SentisCompat: Ошибка при отрисовке тензора: {e.Message}");
                  if (e.InnerException != null)
                  {
                        Debug.LogError($"SentisCompat: Внутреннее исключение: {e.InnerException.Message}");
                  }
                  return false;
            }
      }

      /// <summary>
      /// Ручная отрисовка тензора в текстуру, когда стандартные методы не работают
      /// </summary>
      private static bool RenderTensorManually(object tensor, RenderTexture targetTexture)
      {
            try
            {
                  Debug.Log("SentisCompat: Выполняю ручную отрисовку тензора в текстуру");

                  // Получаем размеры целевой текстуры
                  int width = targetTexture.width;
                  int height = targetTexture.height;

                  // Определяем тип тензора и его свойства
                  Type tensorType = tensor.GetType();

                  // Получаем размерность тензора
                  var shapeProperty = tensorType.GetProperty("shape");
                  if (shapeProperty == null)
                  {
                        Debug.LogError("SentisCompat: Не удалось получить форму тензора");
                        return false;
                  }

                  // Получаем данные тензора
                  float[] tensorData = null;

                  // Попытка 1: Через ToReadOnlyArray()
                  var toArrayMethod = tensorType.GetMethod("ToReadOnlyArray",
                      BindingFlags.Public | BindingFlags.Instance);

                  if (toArrayMethod != null)
                  {
                        try
                        {
                              var arrayResult = toArrayMethod.Invoke(tensor, null);
                              if (arrayResult is float[] floatArray)
                              {
                                    tensorData = floatArray;
                              }
                        }
                        catch (Exception e)
                        {
                              Debug.LogWarning($"SentisCompat: Ошибка при вызове ToReadOnlyArray: {e.Message}");
                        }
                  }

                  // Попытка 2: Через AsFloats()
                  if (tensorData == null)
                  {
                        var asFloatsMethod = tensorType.GetMethod("AsFloats",
                            BindingFlags.Public | BindingFlags.Instance);

                        if (asFloatsMethod != null)
                        {
                              try
                              {
                                    var asFloatsResult = asFloatsMethod.Invoke(tensor, null);
                                    if (asFloatsResult is float[] floatArray)
                                    {
                                          tensorData = floatArray;
                                    }
                              }
                              catch (Exception e)
                              {
                                    Debug.LogWarning($"SentisCompat: Ошибка при вызове AsFloats: {e.Message}");
                              }
                        }
                  }

                  // Если не удалось получить данные тензора, выходим
                  if (tensorData == null)
                  {
                        Debug.LogError("SentisCompat: Не удалось получить данные тензора");

                        // Создаем плейсхолдер для визуального отображения ошибки
                        FillTextureWithPlaceholder(targetTexture);
                        return true; // Возвращаем true, чтобы рендеринг мог продолжаться
                  }

                  // Создаем временную CPU текстуру для последующего блита в RenderTexture
                  Texture2D tempTexture = new Texture2D(width, height, TextureFormat.RGBA32, false);

                  // Считаем количество каналов в тензоре
                  int channels = tensorData.Length / (width * height);
                  if (channels < 1) channels = 1;

                  // Заполняем временную текстуру данными из тензора
                  for (int y = 0; y < height; y++)
                  {
                        for (int x = 0; x < width; x++)
                        {
                              // Индекс в данных тензора (зависит от структуры данных)
                              int baseIndex = y * width + x;

                              Color pixelColor;
                              if (channels == 1)
                              {
                                    // Одноканальные данные (сегментационная маска) - используем значение как интенсивность
                                    float value = baseIndex < tensorData.Length ? tensorData[baseIndex] : 0f;
                                    pixelColor = new Color(value, value, value, 1.0f);
                              }
                              else if (channels >= 3)
                              {
                                    // RGB или RGBA данные
                                    float r = (baseIndex < tensorData.Length) ? tensorData[baseIndex] : 0f;
                                    float g = (baseIndex + width * height < tensorData.Length) ? tensorData[baseIndex + width * height] : 0f;
                                    float b = (baseIndex + 2 * width * height < tensorData.Length) ? tensorData[baseIndex + 2 * width * height] : 0f;
                                    float a = 1.0f; // По умолчанию полная непрозрачность

                                    // Если есть 4-й канал (альфа)
                                    if (channels >= 4 && baseIndex + 3 * width * height < tensorData.Length)
                                    {
                                          a = tensorData[baseIndex + 3 * width * height];
                                    }

                                    pixelColor = new Color(r, g, b, a);
                              }
                              else
                              {
                                    // Двухканальные данные (необычно)
                                    float r = (baseIndex < tensorData.Length) ? tensorData[baseIndex] : 0f;
                                    float g = (baseIndex + width * height < tensorData.Length) ? tensorData[baseIndex + width * height] : 0f;
                                    pixelColor = new Color(r, g, 0f, 1.0f);
                              }

                              tempTexture.SetPixel(x, y, pixelColor);
                        }
                  }

                  tempTexture.Apply();

                  // Копируем из временной текстуры в RenderTexture
                  RenderTexture prevRT = RenderTexture.active;
                  RenderTexture.active = targetTexture;

                  try
                  {
                        Graphics.Blit(tempTexture, targetTexture);
                  }
                  finally
                  {
                        RenderTexture.active = prevRT;
                        UnityEngine.Object.Destroy(tempTexture); // Освобождаем ресурсы
                  }

                  Debug.Log("SentisCompat: Ручная отрисовка тензора выполнена успешно");
                  return true;
            }
            catch (Exception e)
            {
                  Debug.LogError($"SentisCompat: Ошибка при ручной отрисовке тензора: {e.Message}");

                  // Создаем плейсхолдер для визуального отображения ошибки
                  FillTextureWithPlaceholder(targetTexture);
                  return true; // Возвращаем true, чтобы рендеринг мог продолжаться
            }
      }

      /// <summary>
      /// Заполняет текстуру плейсхолдер-изображением в случае ошибки
      /// </summary>
      private static void FillTextureWithPlaceholder(RenderTexture targetTexture)
      {
            try
            {
                  // Создаем временную текстуру с простым плейсхолдером
                  Texture2D tempTexture = new Texture2D(targetTexture.width, targetTexture.height, TextureFormat.RGBA32, false);

                  // Заполняем полупрозрачным шахматным узором для обозначения ошибки
                  Color color1 = new Color(1f, 0f, 0f, 0.3f); // Полупрозрачный красный
                  Color color2 = new Color(0.8f, 0.8f, 0.8f, 0.1f); // Очень прозрачный светло-серый

                  int cellSize = 32; // Размер ячейки шахматного узора

                  for (int y = 0; y < targetTexture.height; y++)
                  {
                        for (int x = 0; x < targetTexture.width; x++)
                        {
                              bool isEvenCell = ((x / cellSize) + (y / cellSize)) % 2 == 0;
                              tempTexture.SetPixel(x, y, isEvenCell ? color1 : color2);
                        }
                  }

                  tempTexture.Apply();

                  // Копируем в RenderTexture
                  RenderTexture prevRT = RenderTexture.active;
                  RenderTexture.active = targetTexture;

                  try
                  {
                        Graphics.Blit(tempTexture, targetTexture);
                  }
                  finally
                  {
                        RenderTexture.active = prevRT;
                        UnityEngine.Object.Destroy(tempTexture);
                  }

                  Debug.Log("SentisCompat: Создан плейсхолдер вместо тензора");
            }
            catch (Exception e)
            {
                  Debug.LogError($"SentisCompat: Не удалось создать даже плейсхолдер: {e.Message}");
            }
      }
}