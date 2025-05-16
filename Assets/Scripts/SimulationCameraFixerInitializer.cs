using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Автоматически инициализирует SimulationCameraFixer при загрузке сцены
/// </summary>
public static class SimulationCameraFixerInitializer
{
    private static GameObject fixerGameObject;
    
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        // Подписываемся на событие загрузки сцены
        SceneManager.sceneLoaded += OnSceneLoaded;
        Debug.Log("[SimulationCameraFixerInitializer] Инициализирован");
    }
    
    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Проверяем, есть ли уже SimulationCameraFixer
        SimulationCameraFixer existingFixer = Object.FindObjectOfType<SimulationCameraFixer>();
        
        if (existingFixer == null)
        {
            // Если фиксера еще нет, создаем его
            if (fixerGameObject == null)
            {
                fixerGameObject = new GameObject("SimulationCameraFixer_AutoInit");
                fixerGameObject.AddComponent<SimulationCameraFixer>();
                Object.DontDestroyOnLoad(fixerGameObject);
                
                Debug.Log("[SimulationCameraFixerInitializer] Автоматически создан SimulationCameraFixer");
            }
        }
        else
        {
            // Фиксер уже существует, проверяем, не нужно ли уничтожить наш
            if (fixerGameObject != null && existingFixer.gameObject != fixerGameObject)
            {
                Object.Destroy(fixerGameObject);
                fixerGameObject = null;
                Debug.Log("[SimulationCameraFixerInitializer] Использовать существующий SimulationCameraFixer");
            }
        }
    }
} 