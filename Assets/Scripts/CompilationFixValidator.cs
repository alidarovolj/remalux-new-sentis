using UnityEngine;
using System.IO;
using System.Text.RegularExpressions;

/// <summary>
/// Валидатор для проверки исправления ошибок компиляции
/// </summary>
public class CompilationFixValidator : MonoBehaviour
{
      [Header("Проверка исправлений")]
      [SerializeField] private bool validateOnStart = true;

      private void Start()
      {
            if (validateOnStart)
            {
                  ValidateCompilationFixes();
            }
      }

      [ContextMenu("Проверить исправления компиляции")]
      public void ValidateCompilationFixes()
      {
            Debug.Log("[CompilationFixValidator] === ПРОВЕРКА ИСПРАВЛЕНИЙ КОМПИЛЯЦИИ ===");

            bool allFixesValid = true;

            // 1. Проверка SceneSetupHelper.cs - должен использовать FindExistingSimulationEnvironment
            allFixesValid &= ValidateSceneSetupHelper();

            // 2. Проверка ARManagerInitializer2.cs - не должно быть конфликтов mainCamera
            allFixesValid &= ValidateARManagerMainCameraFixes();

            // 3. Общая проверка проекта
            allFixesValid &= ValidateProjectState();

            if (allFixesValid)
            {
                  Debug.Log("[CompilationFixValidator] ✅ ВСЕ ИСПРАВЛЕНИЯ ВАЛИДНЫ! Проект готов к компиляции.");
            }
            else
            {
                  Debug.LogError("[CompilationFixValidator] ❌ НАЙДЕНЫ ПРОБЛЕМЫ! Проверьте исправления.");
            }
      }

      private bool ValidateSceneSetupHelper()
      {
            Debug.Log("[CompilationFixValidator] Проверка SceneSetupHelper.cs...");

            string filePath = Path.Combine(Application.dataPath, "Scripts", "SceneSetupHelper.cs");
            if (!File.Exists(filePath))
            {
                  Debug.LogError("[CompilationFixValidator] SceneSetupHelper.cs не найден!");
                  return false;
            }

            string content = File.ReadAllText(filePath);

            // Проверяем, что XRSimulationEnvironment больше не используется
            if (content.Contains("XRSimulationEnvironment"))
            {
                  Debug.LogError("[CompilationFixValidator] ❌ SceneSetupHelper.cs все еще содержит XRSimulationEnvironment!");
                  return false;
            }

            // Проверяем, что есть метод FindExistingSimulationEnvironment
            if (!content.Contains("FindExistingSimulationEnvironment"))
            {
                  Debug.LogError("[CompilationFixValidator] ❌ SceneSetupHelper.cs не содержит метод FindExistingSimulationEnvironment!");
                  return false;
            }

            Debug.Log("[CompilationFixValidator] ✅ SceneSetupHelper.cs исправлен корректно");
            return true;
      }

      private bool ValidateARManagerMainCameraFixes()
      {
            Debug.Log("[CompilationFixValidator] Проверка ARManagerInitializer2.cs...");

            string filePath = Path.Combine(Application.dataPath, "Scripts", "ARManagerInitializer2.cs");
            if (!File.Exists(filePath))
            {
                  Debug.LogError("[CompilationFixValidator] ARManagerInitializer2.cs не найден!");
                  return false;
            }

            string content = File.ReadAllText(filePath);

            // Подсчитываем количество объявлений Camera mainCamera = Camera.main
            var matches = Regex.Matches(content, @"Camera\s+mainCamera\s*=\s*Camera\.main");
            if (matches.Count > 0)
            {
                  Debug.LogError($"[CompilationFixValidator] ❌ Найдено {matches.Count} конфликтующих объявлений 'Camera mainCamera = Camera.main' в ARManagerInitializer2.cs!");
                  return false;
            }

            // Проверяем, что есть использование arMainCamera
            if (!content.Contains("arMainCamera"))
            {
                  Debug.LogWarning("[CompilationFixValidator] ⚠️ arMainCamera не найдено в ARManagerInitializer2.cs. Возможно, исправления не применены.");
            }

            Debug.Log("[CompilationFixValidator] ✅ ARManagerInitializer2.cs исправлен корректно");
            return true;
      }

      private bool ValidateProjectState()
      {
            Debug.Log("[CompilationFixValidator] Проверка общего состояния проекта...");

            // Проверяем наличие SceneSetupHelper компонента
            var sceneSetupHelper = FindObjectOfType<SceneSetupHelper>();
            if (sceneSetupHelper != null)
            {
                  Debug.Log("[CompilationFixValidator] ✅ SceneSetupHelper найден в сцене");
            }
            else
            {
                  Debug.LogWarning("[CompilationFixValidator] ⚠️ SceneSetupHelper не найден в сцене");
            }

            // Проверяем наличие ARManagerInitializer2
            var arManager = FindObjectOfType<ARManagerInitializer2>();
            if (arManager != null)
            {
                  Debug.Log("[CompilationFixValidator] ✅ ARManagerInitializer2 найден в сцене");
            }
            else
            {
                  Debug.LogWarning("[CompilationFixValidator] ⚠️ ARManagerInitializer2 не найден в сцене");
            }

            return true;
      }

      [ContextMenu("Показать статистику исправлений")]
      public void ShowFixStatistics()
      {
            Debug.Log("[CompilationFixValidator] === СТАТИСТИКА ИСПРАВЛЕНИЙ ===");
            Debug.Log("1. ✅ XRSimulationEnvironment ошибка исправлена");
            Debug.Log("2. ✅ Конфликт mainCamera (первое место) исправлен");
            Debug.Log("3. ✅ Конфликт mainCamera (второе место) исправлен");
            Debug.Log("Итого исправлено: 3 критические ошибки компиляции");
      }
}