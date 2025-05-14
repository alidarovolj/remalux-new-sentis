using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems; // Добавляем для доступа к PlaneDetectionMode
using UnityEngine.Rendering.Universal;
using System.IO;
using UnityEngine.EventSystems;
using System.Reflection;
using UnityEngine.UI;
using Unity.XR.CoreUtils;
using UnityEngine.InputSystem;
using UnityEngine.XR.ARFoundation.InternalUtils;
using System.Linq;
using System;
#if UNITY_IOS
using UnityEngine.XR.ARKit;
#endif

// Это ИСПРАВЛЕННАЯ копия SceneSetupUtility.cs с удалением строки planeFindingMode
// Данный файл служит только для демонстрации исправления

// ВАЖНО: Не используйте этот файл напрямую.
// Используйте SceneSetupFixerEditor для автоматического исправления или
// внесите изменения в оригинальный файл вручную.

/// <summary>
/// Пример того, как должен выглядеть исправленный фрагмент кода в оригинальном SceneSetupUtility.cs
/// </summary>
public static class ARSceneSetupFixer
{
    /// <summary>
    /// Этот метод просто демонстрирует правильный вариант кода
    /// </summary>
    public static void ShowFixedCodeExample()
    {
        EditorUtility.DisplayDialog(
            "Пример исправленного кода",
            "Правильный фрагмент кода в SceneSetupUtility.cs для метода SetupARComponents:\n\n" +
            "// Добавляем AR Plane Manager\n" +
            "var planeManager = trackersObj.AddComponent<ARPlaneManager>();\n" +
            "planeManager.enabled = true; // Явно включаем\n\n" +
            "// Важно: настраиваем обнаружение вертикальных плоскостей\n" +
            "planeManager.requestedDetectionMode = PlaneDetectionMode.Vertical | PlaneDetectionMode.Horizontal;\n" +
            "Debug.Log(\"ARPlaneManager настроен на обнаружение вертикальных И горизонтальных плоскостей\");\n\n" +
            "// Следующая строка была удалена, т.к. она вызывает ошибку компиляции:\n" +
            "// planeManager.planeFindingMode = PlaneFindingMode.Horizontal | PlaneFindingMode.Vertical;\n\n" +
            "// Пытаемся использовать префаб плоскости из конфигурации\n" +
            "if (sceneConfig != null && sceneConfig.arPlanePrefab != null)\n" +
            "{\n" +
            "    planeManager.planePrefab = sceneConfig.arPlanePrefab;\n" +
            "}\n",
            "OK");
    }

    [MenuItem("Tools/Показать пример исправленного кода")]
    public static void ShowExample()
    {
        ShowFixedCodeExample();
    }
} 