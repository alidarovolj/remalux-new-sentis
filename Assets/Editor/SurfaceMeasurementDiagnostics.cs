using UnityEngine;
using UnityEditor;
using UnityEngine.XR.ARFoundation;

/// <summary>
/// Диагностический инструмент для системы точного измерения поверхностей
/// </summary>
public class SurfaceMeasurementDiagnostics : EditorWindow
{
      [MenuItem("AR Tools/Diagnostics/Surface Measurement Issues")]
      public static void ShowWindow()
      {
            var window = GetWindow<SurfaceMeasurementDiagnostics>("Surface Measurement Diagnostics");
            window.minSize = new Vector2(500, 400);
            window.Show();
      }

      private Vector2 scrollPosition;

      private void OnGUI()
      {
            GUILayout.Label("🔍 Диагностика системы измерения поверхностей", EditorStyles.boldLabel);

            EditorGUILayout.Space();

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            // Проверка компонентов
            DrawComponentsStatus();

            EditorGUILayout.Space();

            // Проверка настроек
            DrawSettingsStatus();

            EditorGUILayout.Space();

            // Быстрые исправления
            DrawQuickFixes();

            EditorGUILayout.Space();

            // Решение частых проблем
            DrawCommonIssues();

            EditorGUILayout.EndScrollView();
      }

      private void DrawComponentsStatus()
      {
            EditorGUILayout.LabelField("📋 Статус компонентов", EditorStyles.boldLabel);

            var wallPainter = FindObjectOfType<WallPainterController>();
            var surfaceSystem = FindObjectOfType<SurfaceMeasurementSystem>();
            var planeManager = FindObjectOfType<ARPlaneManager>();
            var planeAnalyzer = FindObjectOfType<PlaneGeometryAnalyzer>();

            DrawStatusItem("WallPainterController", wallPainter != null,
                "Главный контроллер для создания плоскостей");
            DrawStatusItem("SurfaceMeasurementSystem", surfaceSystem != null,
                "Система точного измерения размеров");
            DrawStatusItem("ARPlaneManager", planeManager != null,
                "AR Foundation компонент для обнаружения плоскостей");
            DrawStatusItem("PlaneGeometryAnalyzer", planeAnalyzer != null,
                "Анализатор геометрии созданных плоскостей");

            if (wallPainter != null && surfaceSystem != null)
            {
                  EditorGUILayout.HelpBox("✅ Все основные компоненты найдены", MessageType.Info);
            }
            else
            {
                  EditorGUILayout.HelpBox("❌ Отсутствуют критически важные компоненты", MessageType.Error);
            }
      }

      private void DrawSettingsStatus()
      {
            EditorGUILayout.LabelField("⚙️ Проверка настроек", EditorStyles.boldLabel);

            var wallPainter = FindObjectOfType<WallPainterController>();
            var surfaceSystem = FindObjectOfType<SurfaceMeasurementSystem>();

            if (wallPainter != null)
            {
                  var serializedWallPainter = new SerializedObject(wallPainter);

                  var usePrecise = serializedWallPainter.FindProperty("usePreciseSurfaceMeasurement");
                  var surfaceSystemRef = serializedWallPainter.FindProperty("surfaceMeasurementSystem");

                  bool usePreciseValue = usePrecise?.boolValue ?? false;
                  bool hasReference = surfaceSystemRef?.objectReferenceValue != null;

                  DrawStatusItem("Use Precise Surface Measurement", usePreciseValue,
                      "Включено точное измерение поверхностей");
                  DrawStatusItem("Surface System Reference", hasReference,
                      "Назначена ссылка на SurfaceMeasurementSystem");
            }

            if (surfaceSystem != null)
            {
                  var serializedSurfaceSystem = new SerializedObject(surfaceSystem);

                  var enableAutoCalibration = serializedSurfaceSystem.FindProperty("enableAutoCalibration");
                  var debugMode = serializedSurfaceSystem.FindProperty("debugMode");
                  var useMultiPoint = serializedSurfaceSystem.FindProperty("useMultiPointMeasurement");

                  bool autoCalibValue = enableAutoCalibration?.boolValue ?? false;
                  bool debugValue = debugMode?.boolValue ?? false;
                  bool multiPointValue = useMultiPoint?.boolValue ?? false;

                  DrawStatusItem("Auto Calibration", autoCalibValue,
                      "Автоматическая калибровка размеров");
                  DrawStatusItem("Debug Mode", debugValue,
                      "Подробные логи для отладки");
                  DrawStatusItem("Multi Point Measurement", multiPointValue,
                      "Многоточечное измерение");
            }
      }

      private void DrawQuickFixes()
      {
            EditorGUILayout.LabelField("🔧 Быстрые исправления", EditorStyles.boldLabel);

            if (GUILayout.Button("🎯 Исправить все проблемы автоматически"))
            {
                  FixAllIssues();
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("📏 Оптимизировать для Unity симуляции"))
            {
                  OptimizeForSimulation();
            }

            if (GUILayout.Button("🔄 Сбросить настройки к значениям по умолчанию"))
            {
                  ResetToDefaults();
            }

            if (GUILayout.Button("🧹 Отключить лишние предупреждения"))
            {
                  DisableVerboseWarnings();
            }
      }

      private void DrawCommonIssues()
      {
            EditorGUILayout.LabelField("❗ Частые проблемы и решения", EditorStyles.boldLabel);

            EditorGUILayout.HelpBox(
                "🔴 Плоскости с нулевой высотой (0.00м)\n" +
                "→ Исправлено: улучшен алгоритм определения размеров в PlaneGeometryAnalyzer",
                MessageType.Info);

            EditorGUILayout.HelpBox(
                "🔴 Недостаточно точек для измерения\n" +
                "→ Исправлено: снижены требования для симуляции Unity",
                MessageType.Info);

            EditorGUILayout.HelpBox(
                "🔴 Не удалось спроецировать контур в 3D\n" +
                "→ Исправлено: улучшены fallback механизмы и снижены требования к успешности",
                MessageType.Info);

            EditorGUILayout.HelpBox(
                "🔴 Эталонные объекты для калибровки не найдены\n" +
                "→ Исправлено: автокалибровка отключена в симуляции Unity",
                MessageType.Info);

            EditorGUILayout.Space();

            EditorGUILayout.LabelField("💡 Рекомендации для тестирования:", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "1. В Unity Editor система работает в упрощенном режиме\n" +
                "2. Многоточечное измерение отключается автоматически\n" +
                "3. Используется Size Correction Factor для коррекции размеров\n" +
                "4. Минимальные требования к количеству точек проецирования",
                MessageType.Info);
      }

      private void DrawStatusItem(string name, bool status, string description)
      {
            EditorGUILayout.BeginHorizontal();

            var color = status ? Color.green : Color.red;
            var icon = status ? "✅" : "❌";

            var originalColor = GUI.color;
            GUI.color = color;
            EditorGUILayout.LabelField($"{icon} {name}", GUILayout.Width(250));
            GUI.color = originalColor;

            EditorGUILayout.LabelField(description, EditorStyles.miniLabel);

            EditorGUILayout.EndHorizontal();
      }

      private void FixAllIssues()
      {
            Debug.Log("[SurfaceMeasurementDiagnostics] 🔧 Начинаю автоматическое исправление всех проблем...");

            try
            {
                  // 1. Оптимизация для симуляции
                  OptimizeForSimulation();

                  // 2. Исправление настроек
                  FixWallPainterSettings();

                  // 3. Исправление SurfaceMeasurementSystem
                  FixSurfaceSystemSettings();

                  // 4. Отключение лишних предупреждений
                  DisableVerboseWarnings();

                  EditorUtility.DisplayDialog("Исправления завершены",
                      "✅ Все найденные проблемы исправлены!\n\n" +
                      "Изменения:\n" +
                      "• Оптимизированы настройки для Unity симуляции\n" +
                      "• Снижены требования к точности измерений\n" +
                      "• Отключены избыточные предупреждения\n" +
                      "• Настроены fallback механизмы",
                      "OK");

                  Debug.Log("[SurfaceMeasurementDiagnostics] ✅ Автоматическое исправление завершено успешно!");
            }
            catch (System.Exception e)
            {
                  Debug.LogError($"[SurfaceMeasurementDiagnostics] ❌ Ошибка при исправлении: {e.Message}");
                  EditorUtility.DisplayDialog("Ошибка", $"Произошла ошибка: {e.Message}", "OK");
            }
      }

      private void OptimizeForSimulation()
      {
            var surfaceSystem = FindObjectOfType<SurfaceMeasurementSystem>();
            if (surfaceSystem != null)
            {
                  var serializedObject = new SerializedObject(surfaceSystem);

                  // Настройки для симуляции
                  SetPropertyValue(serializedObject, "useMultiPointMeasurement", false);
                  SetPropertyValue(serializedObject, "enableAutoCalibration", false);
                  SetPropertyValue(serializedObject, "sizeCorrectionFactor", 1.2f);
                  SetPropertyValue(serializedObject, "minSurfaceSize", 0.1f);
                  SetPropertyValue(serializedObject, "maxSurfaceSize", 20.0f);
                  SetPropertyValue(serializedObject, "debugMode", false); // Уменьшаем количество логов

                  serializedObject.ApplyModifiedProperties();

                  Debug.Log("[SurfaceMeasurementDiagnostics] ✅ Настройки оптимизированы для Unity симуляции");
            }
      }

      private void FixWallPainterSettings()
      {
            var wallPainter = FindObjectOfType<WallPainterController>();
            var surfaceSystem = FindObjectOfType<SurfaceMeasurementSystem>();

            if (wallPainter != null)
            {
                  var serializedObject = new SerializedObject(wallPainter);

                  var surfaceSystemProperty = serializedObject.FindProperty("surfaceMeasurementSystem");
                  if (surfaceSystemProperty != null && surfaceSystem != null)
                  {
                        surfaceSystemProperty.objectReferenceValue = surfaceSystem;
                  }

                  var usePreciseProperty = serializedObject.FindProperty("usePreciseSurfaceMeasurement");
                  if (usePreciseProperty != null)
                  {
                        usePreciseProperty.boolValue = true;
                  }

                  serializedObject.ApplyModifiedProperties();

                  Debug.Log("[SurfaceMeasurementDiagnostics] ✅ Настройки WallPainterController исправлены");
            }
      }

      private void FixSurfaceSystemSettings()
      {
            var surfaceSystem = FindObjectOfType<SurfaceMeasurementSystem>();
            if (surfaceSystem != null)
            {
                  var serializedObject = new SerializedObject(surfaceSystem);

                  // Оптимальные настройки
                  SetPropertyValue(serializedObject, "measurementPoints", 3); // Меньше точек для стабильности
                  SetPropertyValue(serializedObject, "smoothMeasurements", true);

                  serializedObject.ApplyModifiedProperties();

                  Debug.Log("[SurfaceMeasurementDiagnostics] ✅ Настройки SurfaceMeasurementSystem исправлены");
            }
      }

      private void DisableVerboseWarnings()
      {
            var planeAnalyzer = FindObjectOfType<PlaneGeometryAnalyzer>();
            if (planeAnalyzer != null)
            {
                  var serializedObject = new SerializedObject(planeAnalyzer);

                  var showDetailedLogsProperty = serializedObject.FindProperty("showDetailedLogs");
                  if (showDetailedLogsProperty != null)
                  {
                        showDetailedLogsProperty.boolValue = false;
                  }

                  serializedObject.ApplyModifiedProperties();

                  Debug.Log("[SurfaceMeasurementDiagnostics] ✅ Отключены избыточные предупреждения PlaneGeometryAnalyzer");
            }
      }

      private void ResetToDefaults()
      {
            var surfaceSystem = FindObjectOfType<SurfaceMeasurementSystem>();
            if (surfaceSystem != null)
            {
                  var serializedObject = new SerializedObject(surfaceSystem);

                  // Значения по умолчанию
                  SetPropertyValue(serializedObject, "useMultiPointMeasurement", true);
                  SetPropertyValue(serializedObject, "measurementPoints", 5);
                  SetPropertyValue(serializedObject, "sizeCorrectionFactor", 1.2f);
                  SetPropertyValue(serializedObject, "enableAutoCalibration", true);
                  SetPropertyValue(serializedObject, "referenceObjectSize", 0.21f);
                  SetPropertyValue(serializedObject, "minSurfaceSize", 0.3f);
                  SetPropertyValue(serializedObject, "maxSurfaceSize", 10.0f);
                  SetPropertyValue(serializedObject, "smoothMeasurements", true);
                  SetPropertyValue(serializedObject, "debugMode", true);

                  serializedObject.ApplyModifiedProperties();

                  Debug.Log("[SurfaceMeasurementDiagnostics] 🔄 Настройки сброшены к значениям по умолчанию");
            }

            EditorUtility.DisplayDialog("Сброс настроек",
                "✅ Настройки сброшены к значениям по умолчанию",
                "OK");
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
}