using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;
using Unity.XR.CoreUtils; // Для XROrigin

/// <summary>
/// Обеспечивает правильную конфигурацию сцены для работы WallPainterController.
/// Находит и отключает конфликтующие или устаревшие системы.
/// </summary>
[DefaultExecutionOrder(-100)] // Выполняется одним из первых
public class SceneConfigurator : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void ConfigureScene()
    {
        Debug.Log("<b>[SceneConfigurator]</b> 🚀 Запуск конфигурации сцены...");

        // --- Шаг 1: Найти основной контроллер ---
        var wallPainterController = FindComponentOfType<WallPainterController>();
        if (wallPainterController == null)
        {
            Debug.LogWarning("<b>[SceneConfigurator]</b> ⚠️ WallPainterController не найден. Конфигурация не будет выполнена.");
            return;
        }
        Debug.Log("<b>[SceneConfigurator]</b> ✅ WallPainterController найден.");

        // --- Шаг 2: Отключить конфликтующие системы ---
        DisableComponent<ARManagerInitializer2>();
        DisableComponent<ARPlaneConfigurator>();
        DisableComponent<WallPaintingSystem>();

        // --- Шаг 3: Отключить отладочные компоненты, чтобы не мешали ---
        // Эти компоненты могут быть полезны, но для чистого теста их лучше отключить
        DisableComponent<PlaneOrientationDebugger>();
        DisableComponent<PlaneDebugVisualizer>();
        DisableComponent<SegmentationPlaneDebugger>();
        DisableAndDestroyComponent<PlaneGeometryAnalyzer>();

        // --- Шаг 4: Убедиться, что WallPainterController активен ---
        if (!wallPainterController.gameObject.activeInHierarchy || !wallPainterController.enabled)
        {
            wallPainterController.gameObject.SetActive(true);
            wallPainterController.enabled = true;
            Debug.Log("<b>[SceneConfigurator]</b> ✅ WallPainterController принудительно активирован.");
        }

        Debug.Log("<b>[SceneConfigurator]</b> 🎉 Сцена успешно сконфигурирована для WallPainterController.");
    }

    /// <summary>
    /// Находит компонент в сцене и отключает его GameObject.
    /// </summary>
    private static void DisableComponent<T>() where T : Component
    {
        T component = FindComponentOfType<T>();
        if (component != null)
        {
            component.gameObject.SetActive(false);
            Debug.Log($"<b>[SceneConfigurator]</b> ⛔️ Компонент '{typeof(T).Name}' и его GameObject были отключены.");
        }
        else
        {
            Debug.Log($"<b>[SceneConfigurator]</b> ✔️ Компонент '{typeof(T).Name}' не найден в сцене (это нормально).");
        }
    }

    private static void DisableAndDestroyComponent<T>() where T : Component
    {
        T component = FindComponentOfType<T>();
        if (component != null)
        {
            Object.Destroy(component.gameObject);
            Debug.Log($"<b>[SceneConfigurator]</b> ⛔️ Компонент '{typeof(T).Name}' и его GameObject были удалены.");
        }
        else
        {
            Debug.Log($"<b>[SceneConfigurator]</b> ✔️ Компонент '{typeof(T).Name}' не найден в сцене (это нормально).");
        }
    }


    /// <summary>
    /// Находит объект по типу в сцене.
    /// </summary>
    private static T FindComponentOfType<T>() where T : Component
    {
#if UNITY_2023_1_OR_NEWER
        return Object.FindAnyObjectByType<T>();
#else
        return Object.FindObjectOfType<T>();
#endif
    }
}