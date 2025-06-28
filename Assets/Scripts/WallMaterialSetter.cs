using UnityEngine;

/// <summary>
/// Компонент для установки материала для генерируемых стен (WallPlane)
/// </summary>
public class WallMaterialSetter : MonoBehaviour
{
    [Header("Материалы для плоскостей")]
    [Tooltip("Материал для стен (вертикальных плоскостей)")]
    public Material wallMaterial;

    [Tooltip("Материал для пола (горизонтальных плоскостей)")]
    public Material floorMaterial; // Пока не используется для обновления существующих, но может понадобиться

    [Header("Настройки")]
    [Tooltip("Применять материал автоматически при старте")]
    public bool applyOnStart = true;

    private ARManagerInitializer2 arManager;
    private WallPainterController wallPainterController;

    private void Start()
    {
        if (applyOnStart)
        {
            ApplyMaterialsToExistingPlanes();
        }
    }

    /// <summary>
    /// Устанавливает материалы для существующих плоскостей.
    /// Работает как с новой системой WallPainterController, так и со старой ARManagerInitializer2.
    /// </summary>
    public void ApplyMaterialsToExistingPlanes()
    {
        // Ищем обе системы
        wallPainterController = FindObjectOfType<WallPainterController>();
        arManager = FindObjectOfType<ARManagerInitializer2>();

        if (wallPainterController == null && arManager == null)
        {
            Debug.LogError("[WallMaterialSetter] ❌ Не найдены ни WallPainterController, ни ARManagerInitializer2!");
            return;
        }

        string activeSystem = wallPainterController != null ? "WallPainterController (новая)" : "ARManagerInitializer2 (старая)";
        Debug.Log($"[WallMaterialSetter] ✅ Подключен к системе: {activeSystem}");

        // Обновляем материалы существующих плоскостей
        UpdateExistingPlanesGraphics();
    }

    /// <summary>
    /// Обновляет графическое представление (материалы) для существующих плоскостей.
    /// </summary>
    public void UpdateExistingPlanesGraphics()
    {
        var allPlanes = GetAllPlanes();
        GameObject[] wallPlanes = allPlanes.ToArray();

        // Если плоскости не найдены через системы, пробуем поиск по тегу/имени
        if (wallPlanes.Length == 0)
        {
            wallPlanes = GameObject.FindGameObjectsWithTag("WallPlane");
            if (wallPlanes.Length == 0)
            {
                wallPlanes = FindWallPlanesByName(); // Поиск по имени как фоллбэк
            }
        }

        Debug.Log($"[WallMaterialSetter] Найдено {wallPlanes.Length} плоскостей для возможного обновления материала.");

        int updatedCount = 0;
        foreach (GameObject plane in wallPlanes)
        {
            MeshRenderer renderer = plane.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                // ПРИМЕЧАНИЕ: Здесь мы должны решить, какой материал применять.
                // Если это вертикальная плоскость (стена), используем wallMaterial.
                // Если горизонтальная (пол), используем floorMaterial.
                // Для этого нужна информация о типе плоскости.
                // Пока что, для простоты, все обновляются wallMaterial.
                // TODO: Добавить логику определения типа плоскости, если это необходимо.
                if (wallMaterial != null)
                {
                    renderer.material = new Material(wallMaterial); // Создаем новый экземпляр материала
                    updatedCount++;
                }
                else
                {
                    Debug.LogWarning($"[WallMaterialSetter] wallMaterial не назначен. Невозможно обновить материал для {plane.name}");
                }
            }
        }

        if (updatedCount > 0)
        {
            Debug.Log($"[WallMaterialSetter] Обновлены материалы для {updatedCount} из {wallPlanes.Length} найденных плоскостей.");
        }
    }

    /// <summary>
    /// Ищет объекты WallPlane по имени (должны начинаться с 'MyARPlane_Debug_')
    /// </summary>
    private GameObject[] FindWallPlanesByName()
    {
        GameObject[] allObjects = FindObjectsOfType<GameObject>(); // Используем неустаревший метод
        System.Collections.Generic.List<GameObject> foundPlanes = new System.Collections.Generic.List<GameObject>();

        foreach (GameObject obj in allObjects)
        {
            // Плоскости, создаваемые ARManagerInitializer2, начинаются с "MyARPlane_Debug_"
            if (obj.name.StartsWith("MyARPlane_Debug_"))
            {
                foundPlanes.Add(obj);
            }
        }
        // Debug.Log($"[WallMaterialSetter] Найдено по имени (FindWallPlanesByName): {foundPlanes.Count} плоскостей.");
        return foundPlanes.ToArray();
    }

    /// <summary>
    /// Получает все плоскости из активной системы (WallPainterController или ARManagerInitializer2)
    /// </summary>
    private System.Collections.Generic.List<GameObject> GetAllPlanes()
    {
        var planes = new System.Collections.Generic.List<GameObject>();

        // Сначала пробуем новую систему
        if (wallPainterController != null)
        {
            planes.AddRange(wallPainterController.GeneratedPlanes);
        }

        // Затем старую систему
        if (arManager != null)
        {
            planes.AddRange(arManager.GeneratedPlanes);
        }

        return planes;
    }
}