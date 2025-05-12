using UnityEngine;
using System.IO;
using Unity.Sentis;
using System.Collections;
using System.Reflection;

public class ModelSerializer : MonoBehaviour
{
      // Путь к ONNX модели, которую хотим сериализовать
      public string onnxModelPath = "Assets/StreamingAssets/model.onnx";

      // Путь для записи сериализованной модели Sentis
      public string outputPath = "Assets/StreamingAssets/model.sentis";

      // Тайм-аут в секундах для каждого этапа обработки
      public float timeoutSeconds = 30f;

      // Запускаем сериализацию при старте
      void Start()
      {
            StartCoroutine(SerializeModelWithTimeout());
      }

      public IEnumerator SerializeModelWithTimeout()
      {
            Debug.Log($"Начинаю сериализацию модели из {onnxModelPath}");

            // Проверяем существование файла
            if (!File.Exists(onnxModelPath))
            {
                  Debug.LogError($"Файл модели не найден: {onnxModelPath}");
                  yield break;
            }

            // Сначала пытаемся загрузить как Asset
            Model model = null;
#if UNITY_EDITOR
            try
            {
                  // Пробуем загрузить как Asset
                  var asset = UnityEditor.AssetDatabase.LoadAssetAtPath<ModelAsset>(onnxModelPath);
                  if (asset != null)
                  {
                        Debug.Log("Модель загружена как Unity Asset");
                        model = ModelLoader.Load(asset);
                  }
            }
            catch (System.Exception assetEx)
            {
                  Debug.LogWarning($"Не удалось загрузить как Asset: {assetEx.Message}. Попробуем альтернативный метод.");
            }
#endif

            // Если не удалось загрузить как Asset, пробуем загрузить из файла ONNX
            if (model == null)
            {
                  try
                  {
                        Debug.Log("Пробуем загрузить ONNX напрямую из файла...");
                        byte[] onnxData = File.ReadAllBytes(onnxModelPath);

                        using (var memoryStream = new MemoryStream(onnxData))
                        {
                              model = ModelLoader.Load(memoryStream);
                              if (model != null)
                              {
                                    Debug.Log("ONNX модель успешно загружена напрямую из файла");
                              }
                        }
                  }
                  catch (System.Exception fileEx)
                  {
                        Debug.LogError($"Ошибка при загрузке файла ONNX: {fileEx.Message}");
                        yield break;
                  }
            }

            // Проверяем успешность загрузки
            if (model == null)
            {
                  Debug.LogError("Не удалось загрузить модель ни одним из способов");
                  yield break;
            }

            // Даем Unity время на обработку
            yield return new WaitForSeconds(0.5f);

            // Сериализуем модель
            try
            {
                  Debug.Log($"Сериализуем модель в {outputPath}");

                  bool success = SaveModelToFile(model, outputPath);

                  if (success)
                  {
                        Debug.Log($"Модель успешно сериализована в {outputPath}");

                        // Обновляем Asset Database, чтобы Unity увидела новый файл
#if UNITY_EDITOR
                        UnityEditor.AssetDatabase.Refresh();
#endif
                  }
                  else
                  {
                        Debug.LogError("Не удалось сериализовать модель");
                  }
            }
            catch (System.Exception e)
            {
                  Debug.LogError($"Ошибка при сериализации модели: {e.Message}");
            }
      }

      // Функция для вызова из инспектора через кнопку
      public void SerializeModel()
      {
            StartCoroutine(SerializeModelWithTimeout());
      }

      // Метод для сериализации модели в файл
      public static bool SaveModelToFile(Model model, string filePath)
      {
            try
            {
                  // Используем рефлексию для поиска метода сохранения
                  // Метод может называться по-разному в зависимости от версии Sentis
                  var methods = model.GetType().GetMethods(BindingFlags.Public | BindingFlags.Instance);

                  // Ищем методы, которые могут сохранять модель
                  foreach (var method in methods)
                  {
                        if (method.Name.Contains("Save") && method.GetParameters().Length == 1 &&
                            method.GetParameters()[0].ParameterType == typeof(string))
                        {
                              Debug.Log($"Найден метод для сохранения: {method.Name}");
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

                  // Ничего не сработало
                  Debug.LogError("Не найден подходящий метод для сохранения модели");
                  return false;
            }
            catch (System.Exception e)
            {
                  Debug.LogError($"Ошибка при сохранении модели в файл: {e.Message}");
                  return false;
            }
      }
}