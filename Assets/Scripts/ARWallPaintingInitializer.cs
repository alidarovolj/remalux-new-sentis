using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.ARFoundation;
using Unity.XR.CoreUtils; // Для XROrigin

/// <summary>
/// Инициализатор системы покраски стен в AR
/// </summary>
[DefaultExecutionOrder(-20)]
public class ARWallPaintingInitializer : MonoBehaviour
{
    [Header("Ссылки на объекты сцены")]
    public GameObject стеныДляПокраски; // Родительский объект для стен
    public XROrigin xrOrigin; // Заменено ARSessionOrigin на XROrigin
    public ARPlaneManager planeManager;
    public ARRaycastManager raycastManager;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    private static void Initialize()
    {
        Debug.Log("[ARWallPaintingInitializer] Инициализирован");
        SceneManager.sceneLoaded += OnSceneLoaded;
    }

    private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        // Проверяем, есть ли уже система в сцене
        ARWallPaintingSystem[] existingSystems = Object.FindObjectsOfType<ARWallPaintingSystem>();
        if (existingSystems != null && existingSystems.Length > 0)
        {
            Debug.Log("[ARWallPaintingInitializer] Система покраски стен уже существует в сцене");
            return;
        }

        // Проверяем наличие необходимых компонентов AR
        bool hasARSessionOrigin = Object.FindObjectOfType<XROrigin>() != null;
        bool hasARSession = Object.FindObjectOfType<UnityEngine.XR.ARFoundation.ARSession>() != null;

        if (!hasARSessionOrigin || !hasARSession)
        {
            Debug.Log("[ARWallPaintingInitializer] AR компоненты не найдены, пропускаем создание системы покраски стен");
            return;
        }

        // Создаем объект для системы
        GameObject arWallPaintingSystemObj = new GameObject("AR_WallPainting");
        ARWallPaintingSystem system = arWallPaintingSystemObj.AddComponent<ARWallPaintingSystem>();

        Debug.Log("[ARWallPaintingInitializer] Автоматически создана система покраски стен AR");

        // Устанавливаем DontDestroyOnLoad
        Object.DontDestroyOnLoad(arWallPaintingSystemObj);
    }
} 