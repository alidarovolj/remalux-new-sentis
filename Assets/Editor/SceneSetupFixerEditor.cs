using UnityEngine;
using UnityEditor;
using System.IO;
using System.Text.RegularExpressions;

/// <summary>
/// Утилита для автоматического исправления ошибок в SceneSetupUtility.cs
/// </summary>
[ExecuteInEditMode]
public class SceneSetupFixerEditor : EditorWindow
{
    [MenuItem("Tools/Fix Scene Setup Utility", false, 100)]
    public static void FixSceneSetupUtilityFile()
    {
        string filePath = "Assets/Editor/SceneSetupUtility.cs";
        
        // Проверяем наличие файла
        if (!File.Exists(filePath))
        {
            Debug.LogError($"Не найден файл {filePath} для исправления.");
            return;
        }
        
        try
        {
            // Считываем содержимое файла
            string fileContent = File.ReadAllText(filePath);
            
            // Создаем резервную копию файла перед изменением
            string backupPath = filePath + ".backup";
            File.WriteAllText(backupPath, fileContent);
            Debug.Log($"Создана резервная копия файла: {backupPath}");
            
            // Ищем и заменяем проблемную строку
            string patternToFind = @"planeManager\.planeFindingMode\s*=\s*PlaneFindingMode\.Horizontal\s*\|\s*PlaneFindingMode\.Vertical\s*;";
            string replacementString = "// planeManager.planeFindingMode = PlaneFindingMode.Horizontal | PlaneFindingMode.Vertical; // Строка закомментирована автоматически";
            
            string fixedContent = Regex.Replace(fileContent, patternToFind, replacementString);
            
            // Проверяем, были ли изменения
            if (fileContent == fixedContent)
            {
                Debug.Log("Проблемная строка не найдена или уже исправлена.");
                return;
            }
            
            // Записываем исправленный файл
            File.WriteAllText(filePath, fixedContent);
            
            // Заставляем Unity перезагрузить скрипт
            AssetDatabase.ImportAsset(filePath);
            Debug.Log("Файл SceneSetupUtility.cs успешно исправлен!");
            
            // Открываем окно сообщения об успешном исправлении
            EditorUtility.DisplayDialog(
                "Исправление завершено",
                "Файл SceneSetupUtility.cs успешно исправлен.\n\n" +
                "Проблемная строка заменена на:\n" +
                replacementString + "\n\n" +
                "Резервная копия оригинального файла сохранена как:\n" +
                backupPath,
                "OK");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Ошибка при исправлении файла: {e.Message}");
            EditorUtility.DisplayDialog(
                "Ошибка",
                $"Не удалось исправить файл:\n{e.Message}\n\nПопробуйте исправить файл вручную.",
                "OK");
        }
    }

    [MenuItem("Tools/Fix AR Issues Window")]
    public static void ShowWindow()
    {
        GetWindow<SceneSetupFixerEditor>("AR Fixer");
    }

    private void OnGUI()
    {
        GUILayout.Label("Инструменты для исправления AR проблем", EditorStyles.boldLabel);
        EditorGUILayout.Space();

        if (GUILayout.Button("Исправить SceneSetupUtility.cs"))
        {
            FixSceneSetupUtilityFile();
        }

        EditorGUILayout.Space();
        EditorGUILayout.HelpBox(
            "Это окно содержит инструменты для исправления проблем в AR компонентах.\n\n" +
            "Кнопка выше автоматически исправит ошибку в SceneSetupUtility.cs, связанную с несуществующим свойством planeFindingMode.",
            MessageType.Info);
    }
} 