using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Автоматический инициализатор CameraCullingMaskFixer.
/// Добавляется в сцену автоматически, если его еще нет.
/// </summary>
[DefaultExecutionOrder(-1000)] // Выполняется раньше других скриптов
public class CameraCullingMaskFixerInitializer : MonoBehaviour
{
    // Синглтон для обеспечения единственного экземпляра
    private static CameraCullingMaskFixerInitializer instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        // Проверяем, существует ли уже инициализатор
        if (instance != null)
            return;

        // Выполняем инициализацию только раз
        SceneManager.sceneLoaded += OnSceneLoaded;
        
        Debug.Log("[CameraCullingMaskFixerInitializer] Зарегистрирован для автоматической инициализации");
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Проверяем, есть ли уже CameraCullingMaskFixer в сцене
        CameraCullingMaskFixer existingFixer = Object.FindObjectOfType<CameraCullingMaskFixer>();
        
        if (existingFixer == null)
        {
            // Создаем новый GameObject для фиксера
            GameObject fixerObj = new GameObject("CameraCullingMaskFixer");
            fixerObj.AddComponent<CameraCullingMaskFixer>();
            Object.DontDestroyOnLoad(fixerObj);
            
            Debug.Log("[CameraCullingMaskFixerInitializer] Автоматически добавлен CameraCullingMaskFixer");
        }
    }
} 