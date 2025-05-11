using UnityEngine;
using UnityEditor;
using System.IO;
using Unity.Sentis;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

/// <summary>
/// Окно инструментов для работы с Unity Sentis
/// </summary>
public class SentisToolsWindow : EditorWindow
{
      private Vector2 scrollPosition;
      private bool showConversionSettings = true;
      private bool showDiagnostics = true;

      // Настройки конвертации
      private Object onnxModelAsset;
      private string customOnnxPath = "";
      private string outputSentisPath = "";

      // Параметры модели
      private string inputTensorName = "pixel_values";
      private string outputTensorName = "logits";
      private int inputWidth = 512;
      private int inputHeight = 512;

      [MenuItem("Tools/Sentis/Sentis Tools")]
      public static void ShowWindow()
      {
            SentisToolsWindow window = EditorWindow.GetWindow<SentisToolsWindow>("Sentis Tools");
            window.minSize = new Vector2(450, 600);
            window.Show();
      }

      private void OnGUI()
      {
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            GUILayout.Label("Unity Sentis Tools", EditorStyles.boldLabel);
            EditorGUILayout.Space(10);

            // Секция конвертации ONNX -> Sentis
            showConversionSettings = EditorGUILayout.Foldout(showConversionSettings, "ONNX → Sentis Conversion", true);
            if (showConversionSettings)
            {
                  EditorGUILayout.BeginVertical("box");

                  EditorGUILayout.LabelField("ONNX Model Source", EditorStyles.boldLabel);

                  // Опция 1: Unity Asset
                  EditorGUILayout.BeginHorizontal();
                  onnxModelAsset = EditorGUILayout.ObjectField("ONNX Model Asset", onnxModelAsset, typeof(Object), false);
                  if (GUILayout.Button("Convert Asset", GUILayout.Width(120)))
                  {
                        ConvertAsset();
                  }
                  EditorGUILayout.EndHorizontal();

                  EditorGUILayout.Space(5);

                  // Опция 2: Путь к файлу
                  EditorGUILayout.BeginHorizontal();
                  customOnnxPath = EditorGUILayout.TextField("ONNX File Path", customOnnxPath);
                  if (GUILayout.Button("Browse", GUILayout.Width(60)))
                  {
                        BrowseForOnnxFile();
                  }
                  EditorGUILayout.EndHorizontal();

                  outputSentisPath = EditorGUILayout.TextField("Output Sentis Path", outputSentisPath);

                  EditorGUILayout.Space(5);

                  if (GUILayout.Button("Convert from Path"))
                  {
                        ConvertFromPath();
                  }

                  EditorGUILayout.Space(5);

                  // Параметры входных и выходных тензоров
                  EditorGUILayout.LabelField("Input/Output Settings", EditorStyles.boldLabel);

                  inputTensorName = EditorGUILayout.TextField("Input Tensor Name", inputTensorName);
                  outputTensorName = EditorGUILayout.TextField("Output Tensor Name", outputTensorName);

                  EditorGUILayout.BeginHorizontal();
                  inputWidth = EditorGUILayout.IntField("Input Width", inputWidth);
                  inputHeight = EditorGUILayout.IntField("Input Height", inputHeight);
                  EditorGUILayout.EndHorizontal();

                  EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(15);

            // Диагностика и информация о Sentis
            showDiagnostics = EditorGUILayout.Foldout(showDiagnostics, "Sentis Diagnostics", true);
            if (showDiagnostics)
            {
                  EditorGUILayout.BeginVertical("box");

                  // Версия Sentis
                  string sentisVersion = "Unknown";
                  var sentisAssembly = System.AppDomain.CurrentDomain.GetAssemblies()
                      .FirstOrDefault(a => a.GetName().Name == "Unity.Sentis");

                  if (sentisAssembly != null)
                  {
                        sentisVersion = sentisAssembly.GetName().Version.ToString();
                  }

                  EditorGUILayout.LabelField("Sentis Version", sentisVersion);

                  // Проверка наличия основных типов
                  CheckTypeExists("Unity.Sentis.ModelLoader");
                  CheckTypeExists("Unity.Sentis.Model");
                  CheckTypeExists("Unity.Sentis.Worker");
                  CheckTypeExists("Unity.Sentis.BackendType");

                  EditorGUILayout.Space(5);

                  // Список моделей в StreamingAssets
                  EditorGUILayout.LabelField("Models in StreamingAssets", EditorStyles.boldLabel);

                  string streamingAssetsPath = Application.streamingAssetsPath;
                  if (Directory.Exists(streamingAssetsPath))
                  {
                        string[] files = Directory.GetFiles(streamingAssetsPath);
                        bool modelsFound = false;

                        foreach (string file in files)
                        {
                              string ext = Path.GetExtension(file).ToLower();
                              if (ext == ".onnx" || ext == ".sentis")
                              {
                                    modelsFound = true;
                                    EditorGUILayout.BeginHorizontal();
                                    EditorGUILayout.LabelField(Path.GetFileName(file));

                                    if (GUILayout.Button("Test", GUILayout.Width(60)))
                                    {
                                          TestModel(file);
                                    }

                                    EditorGUILayout.EndHorizontal();
                              }
                        }

                        if (!modelsFound)
                        {
                              EditorGUILayout.HelpBox("No .onnx or .sentis models found in StreamingAssets", MessageType.Info);
                        }
                  }
                  else
                  {
                        EditorGUILayout.HelpBox("StreamingAssets folder does not exist", MessageType.Warning);
                        if (GUILayout.Button("Create StreamingAssets Folder"))
                        {
                              Directory.CreateDirectory(streamingAssetsPath);
                              AssetDatabase.Refresh();
                        }
                  }

                  EditorGUILayout.Space(5);

                  if (GUILayout.Button("Run Full Diagnostics"))
                  {
                        RunFullDiagnostics();
                  }

                  EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(15);

            // Секция быстрых действий
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.LabelField("Quick Actions", EditorStyles.boldLabel);

            if (GUILayout.Button("Serialize All ONNX Models in StreamingAssets"))
            {
                  SerializeAllOnnxModels();
            }

            if (GUILayout.Button("Add ModelSerializer to Scene"))
            {
                  AddModelSerializerToScene();
            }

            EditorGUILayout.EndVertical();

            EditorGUILayout.EndScrollView();
      }

      private void BrowseForOnnxFile()
      {
            string path = EditorUtility.OpenFilePanel("Select ONNX Model", "", "onnx");
            if (!string.IsNullOrEmpty(path))
            {
                  customOnnxPath = path;

                  // Предлагаем путь по умолчанию для выходного файла
                  if (string.IsNullOrEmpty(outputSentisPath))
                  {
                        outputSentisPath = Path.Combine(
                            Path.GetDirectoryName(path),
                            Path.GetFileNameWithoutExtension(path) + ".sentis"
                        );
                  }
            }
      }

      private void ConvertAsset()
      {
            if (onnxModelAsset == null)
            {
                  EditorUtility.DisplayDialog("Error", "Please select an ONNX model asset", "OK");
                  return;
            }

            string assetPath = AssetDatabase.GetAssetPath(onnxModelAsset);
            if (string.IsNullOrEmpty(assetPath) || !assetPath.EndsWith(".onnx"))
            {
                  EditorUtility.DisplayDialog("Error", "Selected asset is not an ONNX model", "OK");
                  return;
            }

            try
            {
                  // Загружаем модель
                  ModelAsset modelAsset = AssetDatabase.LoadAssetAtPath<ModelAsset>(assetPath);
                  if (modelAsset == null)
                  {
                        EditorUtility.DisplayDialog("Error", "Failed to load model as ModelAsset", "OK");
                        return;
                  }

                  // Загружаем и сериализуем
                  Model model = ModelLoader.Load(modelAsset);
                  if (model == null)
                  {
                        EditorUtility.DisplayDialog("Error", "Failed to load model", "OK");
                        return;
                  }

                  // Определяем выходной путь
                  string outputPath = Path.Combine(
                      Application.streamingAssetsPath,
                      Path.GetFileNameWithoutExtension(assetPath) + ".sentis"
                  );

                  // Убеждаемся, что директория существует
                  Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

                  // Сохраняем модель
                  bool success = SaveModelToFile(model, outputPath);

                  if (success)
                  {
                        AssetDatabase.Refresh();
                        EditorUtility.DisplayDialog("Success", $"Model converted and saved to:\n{outputPath}", "OK");
                  }
                  else
                  {
                        EditorUtility.DisplayDialog("Error", "Failed to save model", "OK");
                  }
            }
            catch (System.Exception e)
            {
                  EditorUtility.DisplayDialog("Error", $"Error converting model: {e.Message}", "OK");
            }
      }

      private void ConvertFromPath()
      {
            if (string.IsNullOrEmpty(customOnnxPath) || !File.Exists(customOnnxPath))
            {
                  EditorUtility.DisplayDialog("Error", "ONNX file not found", "OK");
                  return;
            }

            if (string.IsNullOrEmpty(outputSentisPath))
            {
                  // Предлагаем путь по умолчанию
                  outputSentisPath = Path.Combine(
                      Application.streamingAssetsPath,
                      Path.GetFileNameWithoutExtension(customOnnxPath) + ".sentis"
                  );
            }

            try
            {
                  // Убеждаемся, что директория для вывода существует
                  Directory.CreateDirectory(Path.GetDirectoryName(outputSentisPath));

                  // Загружаем и сериализуем
                  byte[] onnxData = File.ReadAllBytes(customOnnxPath);
                  using (var ms = new MemoryStream(onnxData))
                  {
                        Model model = ModelLoader.Load(ms);
                        if (model == null)
                        {
                              EditorUtility.DisplayDialog("Error", "Failed to load model from file", "OK");
                              return;
                        }

                        bool success = SaveModelToFile(model, outputSentisPath);

                        if (success)
                        {
                              AssetDatabase.Refresh();
                              EditorUtility.DisplayDialog("Success", $"Model converted and saved to:\n{outputSentisPath}", "OK");
                        }
                        else
                        {
                              EditorUtility.DisplayDialog("Error", "Failed to save model", "OK");
                        }
                  }
            }
            catch (System.Exception e)
            {
                  EditorUtility.DisplayDialog("Error", $"Error converting model: {e.Message}", "OK");
            }
      }

      private void CheckTypeExists(string typeName)
      {
            var type = System.Type.GetType(typeName);
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(typeName);
            EditorGUILayout.LabelField(type != null ? "✓" : "✗", GUILayout.Width(30));
            EditorGUILayout.EndHorizontal();
      }

      private void TestModel(string modelPath)
      {
            try
            {
                  Model model = null;

                  if (modelPath.EndsWith(".onnx"))
                  {
                        // Для ONNX загружаем через MemoryStream
                        byte[] onnxData = File.ReadAllBytes(modelPath);
                        using (var ms = new MemoryStream(onnxData))
                        {
                              model = ModelLoader.Load(ms);
                        }
                  }
                  else if (modelPath.EndsWith(".sentis"))
                  {
                        // Для Sentis загружаем напрямую
                        model = ModelLoader.Load(modelPath);
                  }

                  if (model == null)
                  {
                        EditorUtility.DisplayDialog("Test Failed", "Failed to load model", "OK");
                        return;
                  }

                  // Выводим информацию о модели
                  string info = $"Model loaded successfully!\n\n";
                  info += $"Inputs:\n";

                  foreach (var input in model.inputs)
                  {
                        info += $"  - {input.name}: {string.Join(" × ", GetShape(input))}\n";
                  }

                  info += $"\nOutputs:\n";
                  foreach (var output in model.outputs)
                  {
                        info += $"  - {output.name}: {string.Join(" × ", GetShape(output))}\n";
                  }

                  info += $"\nLayers: {model.layers.Count}";

                  EditorUtility.DisplayDialog("Model Test Results", info, "OK");
            }
            catch (System.Exception e)
            {
                  EditorUtility.DisplayDialog("Test Failed", $"Error: {e.Message}", "OK");
            }
      }

      private void RunFullDiagnostics()
      {
            // Проверяем доступность SentisCompat
            var sentisCompatType = System.Type.GetType("SentisCompat");
            if (sentisCompatType != null)
            {
                  // Вызываем DiagnosticReport через рефлексию
                  var diagnosticMethod = sentisCompatType.GetMethod("DiagnosticReport",
                      System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static);

                  if (diagnosticMethod != null)
                  {
                        try
                        {
                              diagnosticMethod.Invoke(null, null);
                              Debug.Log("SentisCompat diagnostics executed");
                        }
                        catch (System.Exception e)
                        {
                              Debug.LogError($"Error executing SentisCompat diagnostics: {e.Message}");
                        }
                  }
            }
            else
            {
                  Debug.Log("SentisCompat not found, running basic diagnostics");

                  // Базовая диагностика
                  var sentisAssembly = System.AppDomain.CurrentDomain.GetAssemblies()
                      .FirstOrDefault(a => a.GetName().Name == "Unity.Sentis");

                  if (sentisAssembly != null)
                  {
                        Debug.Log($"Unity.Sentis found: version {sentisAssembly.GetName().Version}");

                        // Основные типы
                        var types = new[] {
                    "Unity.Sentis.ModelLoader",
                    "Unity.Sentis.Model",
                    "Unity.Sentis.Worker",
                    "Unity.Sentis.BackendType"
                };

                        foreach (var typeName in types)
                        {
                              var type = sentisAssembly.GetType(typeName);
                              Debug.Log($"{typeName}: {(type != null ? "✓" : "✗")}");
                        }
                  }
                  else
                  {
                        Debug.LogError("Unity.Sentis assembly not found");
                  }
            }
      }

      private void SerializeAllOnnxModels()
      {
            string streamingAssetsPath = Application.streamingAssetsPath;
            if (!Directory.Exists(streamingAssetsPath))
            {
                  EditorUtility.DisplayDialog("Error", "StreamingAssets folder does not exist", "OK");
                  return;
            }

            string[] onnxFiles = Directory.GetFiles(streamingAssetsPath, "*.onnx");
            if (onnxFiles.Length == 0)
            {
                  EditorUtility.DisplayDialog("No ONNX Models", "No ONNX models found in StreamingAssets", "OK");
                  return;
            }

            int successCount = 0;
            int errorCount = 0;
            List<string> errorMessages = new List<string>();

            foreach (string onnxFile in onnxFiles)
            {
                  try
                  {
                        string outputPath = Path.ChangeExtension(onnxFile, "sentis");

                        // Загружаем и сериализуем
                        byte[] onnxData = File.ReadAllBytes(onnxFile);
                        using (var ms = new MemoryStream(onnxData))
                        {
                              Model model = ModelLoader.Load(ms);
                              if (model != null && SaveModelToFile(model, outputPath))
                              {
                                    successCount++;
                              }
                              else
                              {
                                    errorCount++;
                                    errorMessages.Add($"Failed to convert {Path.GetFileName(onnxFile)}");
                              }
                        }
                  }
                  catch (System.Exception e)
                  {
                        errorCount++;
                        errorMessages.Add($"{Path.GetFileName(onnxFile)}: {e.Message}");
                  }
            }

            AssetDatabase.Refresh();

            string message = $"Converted {successCount} of {onnxFiles.Length} models";
            if (errorCount > 0)
            {
                  message += $"\n\nErrors ({errorCount}):\n" + string.Join("\n", errorMessages);
            }

            EditorUtility.DisplayDialog("Conversion Results", message, "OK");
      }

      private void AddModelSerializerToScene()
      {
            // Проверяем, существует ли уже ModelSerializer в сцене
            ModelSerializer existingSerializer = GameObject.FindObjectOfType<ModelSerializer>();

            if (existingSerializer != null)
            {
                  EditorGUIUtility.PingObject(existingSerializer.gameObject);
                  Selection.activeGameObject = existingSerializer.gameObject;
                  EditorUtility.DisplayDialog("ModelSerializer Found",
                      "A ModelSerializer component already exists in the scene", "OK");
                  return;
            }

            // Создаем новый GameObject с компонентом ModelSerializer
            GameObject serializerObject = new GameObject("ModelSerializer");
            ModelSerializer serializer = serializerObject.AddComponent<ModelSerializer>();

            // Устанавливаем пути по умолчанию
            serializer.onnxModelPath = "Assets/StreamingAssets/model.onnx";
            serializer.outputPath = "Assets/StreamingAssets/model.sentis";

            // Выбираем новый объект в редакторе
            Selection.activeGameObject = serializerObject;
            EditorGUIUtility.PingObject(serializerObject);

            EditorUtility.DisplayDialog("ModelSerializer Added",
                "ModelSerializer has been added to the scene", "OK");
      }

      // Вспомогательный метод для сохранения модели в файл
      public static bool SaveModelToFile(Model model, string filePath)
      {
            try
            {
                  // Используем рефлексию для поиска метода сохранения
                  var methods = model.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance);

                  // Ищем методы, которые могут сохранять модель
                  foreach (var method in methods)
                  {
                        if (method.Name.Contains("Save") && method.GetParameters().Length == 1 &&
                            method.GetParameters()[0].ParameterType == typeof(string))
                        {
                              method.Invoke(model, new object[] { filePath });
                              return File.Exists(filePath); // Проверяем, создан ли файл
                        }
                  }

                  // Если метод не найден, пробуем сериализовать вручную
                  string directory = Path.GetDirectoryName(filePath);
                  if (!Directory.Exists(directory))
                        Directory.CreateDirectory(directory);

                  // Для Unity Sentis 2.1+, попробуем использовать байтовую сериализацию
                  var serializeMethod = model.GetType().GetMethod("SerializeToBytes");
                  if (serializeMethod != null)
                  {
                        byte[] bytes = (byte[])serializeMethod.Invoke(model, null);
                        if (bytes != null && bytes.Length > 0)
                        {
                              File.WriteAllBytes(filePath, bytes);
                              return true;
                        }
                  }

                  Debug.LogError("Не найден подходящий метод для сохранения модели");
                  return false;
            }
            catch (System.Exception e)
            {
                  Debug.LogError($"Ошибка при сохранении модели в файл: {e.Message}");
                  return false;
            }
      }

      // Универсальный метод для получения формы тензора из разных объектов
      private int[] GetShape(object tensorInfo)
      {
            try
            {
                  if (tensorInfo == null)
                        return new int[] { 0, 0, 0, 0 };

                  // Проверяем, является ли объект массивом int[]
                  if (tensorInfo is int[] intArray)
                        return intArray;

                  // Пробуем получить shape через свойство
                  var shapeProperty = tensorInfo.GetType().GetProperty("shape");
                  if (shapeProperty != null)
                  {
                        var shape = shapeProperty.GetValue(tensorInfo);

                        // Если shape - массив, возвращаем его
                        if (shape is int[] shapeArray)
                              return shapeArray;

                        // Если shape - другой объект, рекурсивно извлекаем размерности
                        if (shape != null && shape != tensorInfo) // Предотвращаем бесконечную рекурсию
                              return GetShape(shape);
                  }

                  // Пробуем получить размерности через другие свойства
                  var dimensionsProperty = tensorInfo.GetType().GetProperty("dimensions");
                  if (dimensionsProperty != null)
                  {
                        var dimensions = dimensionsProperty.GetValue(tensorInfo);
                        if (dimensions is int[] dimArray)
                              return dimArray;
                  }

                  // Для совместимости с разными версиями Sentis пробуем получить отдельные размерности
                  var props = new[] { "rank", "batch", "height", "width", "channels" };
                  var result = new List<int>();
                  bool foundAny = false;

                  foreach (var prop in props)
                  {
                        var propInfo = tensorInfo.GetType().GetProperty(prop);
                        if (propInfo != null)
                        {
                              foundAny = true;
                              var value = propInfo.GetValue(tensorInfo);
                              if (value is int intValue)
                                    result.Add(intValue);
                        }
                  }

                  if (foundAny)
                        return result.ToArray();

                  // Если ничего не нашли, возвращаем заглушку
                  return new int[] { 0, 0, 0, 0 };
            }
            catch (System.Exception e)
            {
                  Debug.LogWarning($"Ошибка при получении размерностей: {e.Message}");
                  return new int[] { 0, 0, 0, 0 };
            }
      }
}