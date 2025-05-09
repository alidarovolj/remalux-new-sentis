using UnityEngine;
using UnityEditor;
using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Reflection;

/// <summary>
/// Редактор для диагностики и исправления проблем с установкой Unity Sentis
/// </summary>
public class SentisInstallationFix : EditorWindow
{
      [MenuItem("Remalux/Sentis Installation Fix")]
      public static void ShowWindow()
      {
            GetWindow<SentisInstallationFix>("Sentis Fix");
      }

      private string statusMessage = "Ожидание диагностики...";
      private string packageVersion = "Not detected";
      private bool sentisDetected = false;
      private bool workerFactoryDetected = false;
      private bool modelLoaderDetected = false;
      private List<string> diagnosticMessages = new List<string>();
      private Vector2 scrollPosition;

      private void OnGUI()
      {
            GUILayout.Label("Диагностика и исправление проблем с Unity Sentis", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            GUILayout.Label($"Статус Sentis: {(sentisDetected ? "Обнаружен ✅" : "Не обнаружен ❌")}");
            GUILayout.Label($"Версия пакета: {packageVersion}");
            GUILayout.Label($"ModelLoader API: {(modelLoaderDetected ? "Обнаружен ✅" : "Не обнаружен ❌")}");
            GUILayout.Label($"WorkerFactory API: {(workerFactoryDetected ? "Обнаружен ✅" : "Не обнаружен ❌")}");

            EditorGUILayout.Space();

            if (GUILayout.Button("Запустить диагностику"))
            {
                  RunDiagnostics();
            }

            EditorGUILayout.Space();

            // Кнопки для исправления проблем, активны только если Sentis обнаружен
            EditorGUI.BeginDisabledGroup(!sentisDetected);

            if (GUILayout.Button("Обновить конфигурацию проекта"))
            {
                  UpdateProjectConfiguration();
            }

            EditorGUILayout.Space();

            if (GUILayout.Button("Переустановить Sentis"))
            {
                  ReinstallSentis();
            }

            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space();

            // Отображаем сообщения диагностики в прокручиваемой области
            GUILayout.Label("Детали диагностики:", EditorStyles.boldLabel);
            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition, GUILayout.ExpandHeight(true));
            foreach (var message in diagnosticMessages)
            {
                  GUILayout.Label(message, EditorStyles.wordWrappedLabel);
            }
            EditorGUILayout.EndScrollView();

            // Отображаем статус текущей операции внизу
            EditorGUILayout.Space();
            EditorGUILayout.HelpBox(statusMessage, MessageType.Info);
      }

      private void RunDiagnostics()
      {
            diagnosticMessages.Clear();
            statusMessage = "Запуск диагностики...";
            Repaint();

            try
            {
                  // Проверяем наличие Sentis в пакетах
                  diagnosticMessages.Add("Проверка пакета Unity Sentis...");
                  string manifestPath = "Packages/manifest.json";
                  bool packageFound = false;

                  if (File.Exists(manifestPath))
                  {
                        string manifest = File.ReadAllText(manifestPath);
                        if (manifest.Contains("com.unity.sentis"))
                        {
                              int index = manifest.IndexOf("com.unity.sentis");
                              int versionStart = manifest.IndexOf(":", index) + 1;
                              int versionEnd = manifest.IndexOf(",", versionStart);
                              if (versionEnd == -1) versionEnd = manifest.IndexOf("}", versionStart);

                              if (versionStart > 0 && versionEnd > versionStart)
                              {
                                    packageVersion = manifest.Substring(versionStart, versionEnd - versionStart).Trim().Replace("\"", "");
                                    packageFound = true;
                                    diagnosticMessages.Add($"✅ Пакет Unity Sentis найден в manifest.json: версия {packageVersion}");
                              }
                        }
                        else
                        {
                              diagnosticMessages.Add("❌ Пакет Unity Sentis не найден в manifest.json");
                        }
                  }
                  else
                  {
                        diagnosticMessages.Add("⚠️ Файл manifest.json не найден");
                  }

                  // Проверяем наличие Assembly Unity.Sentis
                  diagnosticMessages.Add("Проверка сборки Unity.Sentis...");
                  sentisDetected = false;
                  var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                  var sentisAssembly = assemblies.FirstOrDefault(a => a.GetName().Name == "Unity.Sentis");

                  if (sentisAssembly != null)
                  {
                        sentisDetected = true;
                        diagnosticMessages.Add($"✅ Сборка Unity.Sentis найдена: версия {sentisAssembly.GetName().Version}");

                        // Проверяем наличие основных типов
                        var modelType = sentisAssembly.GetType("Unity.Sentis.Model");
                        var modelAssetType = sentisAssembly.GetType("Unity.Sentis.ModelAsset");
                        var modelLoaderType = sentisAssembly.GetType("Unity.Sentis.ModelLoader");
                        var workerFactoryType = sentisAssembly.GetType("Unity.Sentis.WorkerFactory");

                        modelLoaderDetected = modelLoaderType != null;
                        workerFactoryDetected = workerFactoryType != null;

                        diagnosticMessages.Add($"Тип Unity.Sentis.Model: {(modelType != null ? "✅ Найден" : "❌ Не найден")}");
                        diagnosticMessages.Add($"Тип Unity.Sentis.ModelAsset: {(modelAssetType != null ? "✅ Найден" : "❌ Не найден")}");
                        diagnosticMessages.Add($"Тип Unity.Sentis.ModelLoader: {(modelLoaderDetected ? "✅ Найден" : "❌ Не найден")}");
                        diagnosticMessages.Add($"Тип Unity.Sentis.WorkerFactory: {(workerFactoryDetected ? "✅ Найден" : "❌ Не найден")}");

                        // Если WorkerFactory найден, проверяем его методы
                        if (workerFactoryType != null)
                        {
                              var createWorkerMethods = workerFactoryType.GetMethods().Where(m => m.Name == "CreateWorker").ToArray();
                              diagnosticMessages.Add($"Найдено методов CreateWorker: {createWorkerMethods.Length}");

                              foreach (var method in createWorkerMethods)
                              {
                                    var parameters = method.GetParameters();
                                    diagnosticMessages.Add($"- CreateWorker метод с параметрами: {string.Join(", ", parameters.Select(p => p.ParameterType.Name))}");
                              }
                        }
                  }
                  else
                  {
                        diagnosticMessages.Add("❌ Сборка Unity.Sentis не найдена");

                        // Пробуем через Type.GetType
                        var modelType = Type.GetType("Unity.Sentis.Model, Unity.Sentis");
                        diagnosticMessages.Add($"Прямой поиск Unity.Sentis.Model: {(modelType != null ? "✅ Найден" : "❌ Не найден")}");
                  }

                  // Проверяем системные требования
                  diagnosticMessages.Add("\nПроверка системных требований:");
                  diagnosticMessages.Add($"Compute Shaders поддерживаются: {(SystemInfo.supportsComputeShaders ? "✅ Да" : "❌ Нет")}");
                  diagnosticMessages.Add($"Версия Unity: {Application.unityVersion}");
                  diagnosticMessages.Add($"Платформа: {Application.platform}");
                  diagnosticMessages.Add($"Операционная система: {SystemInfo.operatingSystem}");
                  diagnosticMessages.Add($"Графический API: {SystemInfo.graphicsDeviceType}");

                  // Проверяем API Level для Android
                  if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.Android)
                  {
                        AndroidSdkVersions minApiLevel = PlayerSettings.Android.minSdkVersion;
                        int apiLevelValue = (int)minApiLevel;
                        diagnosticMessages.Add($"Android Minimum API Level: {minApiLevel} {(apiLevelValue >= 24 ? "✅" : "❌ (рекомендуется 24+)")}");
                  }

                  // Проверяем минимальную версию iOS
                  if (EditorUserBuildSettings.activeBuildTarget == BuildTarget.iOS)
                  {
                        string minVersion = PlayerSettings.iOS.targetOSVersionString;
                        diagnosticMessages.Add($"iOS Minimum Version: {minVersion}");
                        // Проверка версии iOS для Sentis
                        try
                        {
                              Version v = new Version(minVersion);
                              if (v.Major >= 13)
                              {
                                    diagnosticMessages.Add("✅ iOS версия поддерживается");
                              }
                              else
                              {
                                    diagnosticMessages.Add("❌ Рекомендуется версия iOS 13.0 или выше для Sentis");
                              }
                        }
                        catch
                        {
                              diagnosticMessages.Add("⚠️ Не удалось проверить совместимость версии iOS");
                        }
                  }

                  // Подводим итоги и даем рекомендации
                  if (sentisDetected && workerFactoryDetected && modelLoaderDetected)
                  {
                        statusMessage = "Sentis установлен корректно ✅";
                        diagnosticMessages.Add("\n✅ Unity Sentis установлен и работает нормально.");
                  }
                  else if (sentisDetected && (!workerFactoryDetected || !modelLoaderDetected))
                  {
                        statusMessage = "Sentis установлен, но некоторые API отсутствуют ⚠️";
                        diagnosticMessages.Add("\n⚠️ Unity Sentis установлен, но не все API доступны.");
                        diagnosticMessages.Add("Рекомендации:");
                        diagnosticMessages.Add("- Переустановите пакет Unity Sentis");
                        diagnosticMessages.Add("- Перезагрузите проект Unity");
                        diagnosticMessages.Add("- Убедитесь, что версия Sentis совместима с версией Unity");
                  }
                  else if (packageFound && !sentisDetected)
                  {
                        statusMessage = "Sentis в manifest.json, но сборка не загружена ⚠️";
                        diagnosticMessages.Add("\n⚠️ Пакет Unity Sentis указан в manifest.json, но сборка не загружена.");
                        diagnosticMessages.Add("Рекомендации:");
                        diagnosticMessages.Add("- Обновите конфигурацию проекта кнопкой выше");
                        diagnosticMessages.Add("- Перезагрузите проект Unity");
                        diagnosticMessages.Add("- Переустановите пакет Unity Sentis");
                  }
                  else
                  {
                        statusMessage = "Sentis не установлен ❌";
                        diagnosticMessages.Add("\n❌ Unity Sentis не установлен в проекте.");
                        diagnosticMessages.Add("Рекомендации:");
                        diagnosticMessages.Add("- Установите пакет через Window > Package Manager");
                        diagnosticMessages.Add("- Выберите 'Add package by name' и введите 'com.unity.sentis'");
                        diagnosticMessages.Add("- Убедитесь, что версия Sentis совместима с версией Unity");
                  }
            }
            catch (Exception e)
            {
                  statusMessage = "Ошибка при диагностике ❌";
                  diagnosticMessages.Add($"Ошибка: {e.Message}");
                  if (e.InnerException != null)
                  {
                        diagnosticMessages.Add($"Внутреннее исключение: {e.InnerException.Message}");
                  }
            }

            Repaint();
      }

      private void UpdateProjectConfiguration()
      {
            try
            {
                  statusMessage = "Обновление конфигурации проекта...";
                  diagnosticMessages.Add("\nОбновление конфигурации проекта:");
                  Repaint();

                  // Обновляем сборки через API
                  UnityEditor.EditorUtility.RequestScriptReload();

                  // Немного подождем
                  EditorApplication.delayCall += () =>
                  {
                        statusMessage = "Конфигурация проекта обновлена ✅";
                        diagnosticMessages.Add("✅ Запрос на перезагрузку скриптов отправлен");
                        Repaint();
                  };
            }
            catch (Exception e)
            {
                  statusMessage = "Ошибка при обновлении конфигурации ❌";
                  diagnosticMessages.Add($"Ошибка: {e.Message}");
                  Repaint();
            }
      }

      private void ReinstallSentis()
      {
            if (EditorUtility.DisplayDialog("Переустановить Unity Sentis",
                "Это действие удалит и заново установит пакет Unity Sentis.\n\n" +
                "ВНИМАНИЕ: Unity может перезапуститься после этой операции.\n" +
                "Сохраните все изменения перед продолжением.\n\n" +
                "Продолжить?", "Переустановить", "Отмена"))
            {
                  try
                  {
                        statusMessage = "Переустановка Sentis...";
                        diagnosticMessages.Add("\nПереустановка Unity Sentis:");
                        Repaint();

                        // Сохраняем текущую версию
                        string versionToInstall = string.IsNullOrEmpty(packageVersion) || packageVersion == "Not detected" ?
                            "2.1.2" : packageVersion;

                        diagnosticMessages.Add($"Начинаем переустановку Unity Sentis {versionToInstall}...");
                        Repaint();

                        // Удаляем текущий пакет
                        UnityEditor.PackageManager.Client.Remove("com.unity.sentis");
                        diagnosticMessages.Add("Запрос на удаление пакета отправлен");
                        Repaint();

                        // Добавляем небольшую задержку перед установкой
                        EditorApplication.delayCall += () =>
                        {
                              diagnosticMessages.Add($"Установка Sentis {versionToInstall}...");
                              statusMessage = $"Установка Sentis {versionToInstall}...";
                              Repaint();

                              UnityEditor.PackageManager.Client.Add($"com.unity.sentis@{versionToInstall}");

                              // Показываем сообщение после установки
                              EditorApplication.delayCall += () =>
                              {
                                    statusMessage = "Переустановка завершена ✅";
                                    diagnosticMessages.Add("✅ Переустановка Sentis завершена");
                                    diagnosticMessages.Add("Рекомендуется перезапустить Unity после установки");
                                    Repaint();
                              };
                        };
                  }
                  catch (Exception e)
                  {
                        statusMessage = "Ошибка при переустановке ❌";
                        diagnosticMessages.Add($"Ошибка: {e.Message}");
                        Repaint();
                  }
            }
      }
}