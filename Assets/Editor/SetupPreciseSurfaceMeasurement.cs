using UnityEngine;
using UnityEditor;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// Инструмент для автоматической настройки системы точного измерения поверхностей
/// </summary>
public class SetupPreciseSurfaceMeasurement : EditorWindow
{
      [MenuItem("AR Tools/Setup Precise Surface Measurement")]
      public static void ShowWindow()
      {
            var window = GetWindow<SetupPreciseSurfaceMeasurement>("Surface Measurement Setup");
            window.minSize = new Vector2(400, 300);
            window.Show();
      }

      private void OnGUI()
      {
            GUILayout.Label("Настройка точного измерения поверхностей", EditorStyles.boldLabel);

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Этот инструмент поможет настроить систему точного измерения поверхностей " +
                "для создания AR плоскостей точных размеров.",
                MessageType.Info);

            EditorGUILayout.Space();

            // Проверка текущего состояния
            var wallPainterController = FindObjectOfType<WallPainterController>();
            var surfaceMeasurement = FindObjectOfType<SurfaceMeasurementSystem>();
            var arPlaneManager = FindObjectOfType<ARPlaneManager>();

            EditorGUILayout.LabelField("Статус компонентов:", EditorStyles.boldLabel);

            DrawStatusLine("WallPainterController", wallPainterController != null);
            DrawStatusLine("SurfaceMeasurementSystem", surfaceMeasurement != null);
            DrawStatusLine("ARPlaneManager", arPlaneManager != null);

            EditorGUILayout.Space();

            if (GUILayout.Button("🔧 Автоматическая настройка", GUILayout.Height(30)))
            {
                  SetupAutomatically();
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("📏 Добавить SurfaceMeasurementSystem"))
            {
                  AddSurfaceMeasurementSystem();
            }

            if (GUILayout.Button("🎯 Интегрировать с WallPainterController"))
            {
                  IntegrateWithWallPainter();
            }

            EditorGUILayout.Space();

            EditorGUILayout.HelpBox(
                "После настройки:\n" +
                "1. Система автоматически калибруется при обнаружении эталонных объектов\n" +
                "2. Размеры плоскостей будут более точными\n" +
                "3. Поддерживается многоточечное измерение\n" +
                "4. Доступна ручная калибровка",
                MessageType.Info);
      }

      private void DrawStatusLine(string componentName, bool exists)
      {
            var color = exists ? Color.green : Color.red;
            var icon = exists ? "✅" : "❌";
            var status = exists ? "Найден" : "Не найден";

            var originalColor = GUI.color;
            GUI.color = color;
            EditorGUILayout.LabelField($"{icon} {componentName}: {status}");
            GUI.color = originalColor;
      }

      private void SetupAutomatically()
      {
            Debug.Log("[SetupPreciseSurfaceMeasurement] 🔄 Начинаю автоматическую настройку...");

            try
            {
                  // 1. Добавление SurfaceMeasurementSystem
                  var surfaceSystem = AddSurfaceMeasurementSystem();

                  // 2. Интеграция с WallPainterController
                  IntegrateWithWallPainter();

                  // 3. Настройка параметров по умолчанию
                  ConfigureDefaultSettings(surfaceSystem);

                  EditorUtility.DisplayDialog(
                      "Настройка завершена",
                      "✅ Система точного измерения поверхностей настроена!\n\n" +
                      "Теперь плоскости будут создаваться с более точными размерами.\n" +
                      "Система автоматически калибруется при обнаружении эталонных объектов размером 15-35см.",
                      "OK");

                  Debug.Log("[SetupPreciseSurfaceMeasurement] ✅ Автоматическая настройка завершена успешно!");
            }
            catch (System.Exception e)
            {
                  Debug.LogError($"[SetupPreciseSurfaceMeasurement] ❌ Ошибка при настройке: {e.Message}");
                  EditorUtility.DisplayDialog("Ошибка", $"Произошла ошибка: {e.Message}", "OK");
            }
      }

      private SurfaceMeasurementSystem AddSurfaceMeasurementSystem()
      {
            var existing = FindObjectOfType<SurfaceMeasurementSystem>();
            if (existing != null)
            {
                  Debug.Log("[SetupPreciseSurfaceMeasurement] SurfaceMeasurementSystem уже существует");
                  return existing;
            }

            // Ищем подходящий объект для добавления компонента
            GameObject targetObject = null;

            // Приоритет 1: Объект с WallPainterController
            var wallPainter = FindObjectOfType<WallPainterController>();
            if (wallPainter != null)
            {
                  targetObject = wallPainter.gameObject;
            }
            // Приоритет 2: Объект с ARPlaneManager
            else
            {
                  var planeManager = FindObjectOfType<ARPlaneManager>();
                  if (planeManager != null)
                  {
                        targetObject = planeManager.gameObject;
                  }
            }

            // Приоритет 3: Создать новый объект
            if (targetObject == null)
            {
                  targetObject = new GameObject("SurfaceMeasurementSystem");
                  targetObject.transform.SetAsFirstSibling();
            }

            var surfaceSystem = targetObject.AddComponent<SurfaceMeasurementSystem>();

            // Настройка ссылок
            var planeManagerComponent = FindObjectOfType<ARPlaneManager>();
            if (planeManagerComponent != null)
            {
                  // Используем reflection для установки приватного поля
                  var planeManagerField = typeof(SurfaceMeasurementSystem).GetField("planeManager",
                      System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                  planeManagerField?.SetValue(surfaceSystem, planeManagerComponent);
            }

            var camera = Camera.main ?? FindObjectOfType<Camera>();
            if (camera != null)
            {
                  var cameraField = typeof(SurfaceMeasurementSystem).GetField("arCamera",
                      System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                  cameraField?.SetValue(surfaceSystem, camera);
            }

            Debug.Log($"[SetupPreciseSurfaceMeasurement] ✅ SurfaceMeasurementSystem добавлен к {targetObject.name}");
            return surfaceSystem;
      }

      private void IntegrateWithWallPainter()
      {
            var wallPainter = FindObjectOfType<WallPainterController>();
            if (wallPainter == null)
            {
                  Debug.LogWarning("[SetupPreciseSurfaceMeasurement] WallPainterController не найден для интеграции");
                  return;
            }

            var surfaceSystem = FindObjectOfType<SurfaceMeasurementSystem>();
            if (surfaceSystem == null)
            {
                  Debug.LogWarning("[SetupPreciseSurfaceMeasurement] SurfaceMeasurementSystem не найден для интеграции");
                  return;
            }

            // Используем SerializedObject для установки приватных полей
            var serializedObject = new SerializedObject(wallPainter);

            var surfaceSystemProperty = serializedObject.FindProperty("surfaceMeasurementSystem");
            if (surfaceSystemProperty != null)
            {
                  surfaceSystemProperty.objectReferenceValue = surfaceSystem;
            }

            var usePreciseMeasurementProperty = serializedObject.FindProperty("usePreciseSurfaceMeasurement");
            if (usePreciseMeasurementProperty != null)
            {
                  usePreciseMeasurementProperty.boolValue = true;
            }

            var autoCalibrateSizesProperty = serializedObject.FindProperty("autoCalibrateSizes");
            if (autoCalibrateSizesProperty != null)
            {
                  autoCalibrateSizesProperty.boolValue = true;
            }

            serializedObject.ApplyModifiedProperties();

            Debug.Log("[SetupPreciseSurfaceMeasurement] ✅ Интеграция с WallPainterController завершена");
      }

      private void ConfigureDefaultSettings(SurfaceMeasurementSystem surfaceSystem)
      {
            if (surfaceSystem == null) return;

            var serializedObject = new SerializedObject(surfaceSystem);

            // Настройка параметров по умолчанию
            SetPropertyValue(serializedObject, "useMultiPointMeasurement", true);
            SetPropertyValue(serializedObject, "measurementPoints", 5);
            SetPropertyValue(serializedObject, "sizeCorrectionFactor", 1.2f);
            SetPropertyValue(serializedObject, "enableAutoCalibration", true);
            SetPropertyValue(serializedObject, "referenceObjectSize", 0.21f); // A4 лист
            SetPropertyValue(serializedObject, "minSurfaceSize", 0.3f);
            SetPropertyValue(serializedObject, "maxSurfaceSize", 10.0f);
            SetPropertyValue(serializedObject, "smoothMeasurements", true);
            SetPropertyValue(serializedObject, "debugMode", true);

            serializedObject.ApplyModifiedProperties();

            Debug.Log("[SetupPreciseSurfaceMeasurement] ✅ Настройки по умолчанию применены");
      }

      private void SetPropertyValue(SerializedObject serializedObject, string propertyName, object value)
      {
            var property = serializedObject.FindProperty(propertyName);
            if (property == null) return;

            switch (value)
            {
                  case bool boolValue:
                        property.boolValue = boolValue;
                        break;
                  case int intValue:
                        property.intValue = intValue;
                        break;
                  case float floatValue:
                        property.floatValue = floatValue;
                        break;
                  case string stringValue:
                        property.stringValue = stringValue;
                        break;
            }
      }

      [MenuItem("AR Tools/Manual Calibration Helper")]
      public static void ShowCalibrationHelper()
      {
            var window = GetWindow<CalibrationHelperWindow>("Calibration Helper");
            window.minSize = new Vector2(350, 200);
            window.Show();
      }
}

/// <summary>
/// Окно помощника калибровки
/// </summary>
public class CalibrationHelperWindow : EditorWindow
{
      private float realObjectSize = 0.21f; // A4 лист по умолчанию

      private void OnGUI()
      {
            GUILayout.Label("Помощник ручной калибровки", EditorStyles.boldLabel);

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Для ручной калибровки:\n" +
                "1. Поместите объект известного размера в поле зрения AR\n" +
                "2. Убедитесь что объект обнаружен как плоскость\n" +
                "3. Введите реальный размер объекта\n" +
                "4. Нажмите 'Калибровать'",
                MessageType.Info);

            EditorGUILayout.Space();

            GUILayout.Label("Стандартные размеры:");
            if (GUILayout.Button("📄 Лист A4 (21 см)")) realObjectSize = 0.21f;
            if (GUILayout.Button("📱 iPhone (14.7 см)")) realObjectSize = 0.147f;
            if (GUILayout.Button("💳 Кредитная карта (8.5 см)")) realObjectSize = 0.085f;

            EditorGUILayout.Space();
            realObjectSize = EditorGUILayout.FloatField("Реальный размер (м):", realObjectSize);

            EditorGUILayout.Space();

            if (GUILayout.Button("🎯 Начать калибровку", GUILayout.Height(30)))
            {
                  StartManualCalibration();
            }

            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(
                "Система найдет подходящую плоскость и автоматически калибрирует размеры.",
                MessageType.Info);
      }

      private void StartManualCalibration()
      {
            var surfaceSystem = FindObjectOfType<SurfaceMeasurementSystem>();
            if (surfaceSystem == null)
            {
                  EditorUtility.DisplayDialog("Ошибка",
                      "SurfaceMeasurementSystem не найден!\nСначала настройте систему измерения.",
                      "OK");
                  return;
            }

            // В режиме Play можно выполнить калибровку
            if (Application.isPlaying)
            {
                  var planeManager = FindObjectOfType<ARPlaneManager>();
                  if (planeManager != null)
                  {
                        foreach (var plane in planeManager.trackables)
                        {
                              var planeSize = (plane.size.x + plane.size.y) / 2f;
                              if (planeSize > 0.05f && planeSize < 0.5f) // Подходящий размер для калибровки
                              {
                                    surfaceSystem.ManualCalibration(plane, realObjectSize);
                                    EditorUtility.DisplayDialog("Калибровка",
                                        $"✅ Калибровка выполнена с объектом размером {realObjectSize:F3}м",
                                        "OK");
                                    return;
                              }
                        }
                  }

                  EditorUtility.DisplayDialog("Калибровка",
                      "⚠️ Подходящие плоскости для калибровки не найдены.\n" +
                      "Убедитесь что объект известного размера виден камере.",
                      "OK");
            }
            else
            {
                  EditorUtility.DisplayDialog("Калибровка",
                      "⚠️ Для калибровки нужно запустить приложение (Play Mode).",
                      "OK");
            }
      }
}