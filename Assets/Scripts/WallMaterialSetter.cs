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
    
    // private ARManagerInitializer2 arManager; // Не используется напрямую для изменения материалов
    
    private void Start()
    {
        if (applyOnStart)
        {            
            ApplyMaterialsToExistingPlanes();
        }
    }
    
    /// <summary>
    /// Устанавливает материалы для существующих плоскостей.
    /// Этот метод теперь не меняет материалы в ARManagerInitializer2.
    /// </summary>
    public void ApplyMaterialsToExistingPlanes() // Переименован для ясности
    {
        ARManagerInitializer2 arInitializer = ARManagerInitializer2.Instance;
        if (arInitializer == null)
        {
            Debug.LogError("[WallMaterialSetter] ❌ ARManagerInitializer2 не найден в сцене! Невозможно обновить материалы плоскостей.");
            return;
        }
        
        // Логика ниже теперь не нужна, т.к. мы не меняем материалы в ARManagerInitializer2
        // if (wallMaterial != null)
        // {
        //     // arInitializer.VerticalPlaneMaterial = wallMaterial; // ОШИБКА: Свойство только для чтения
        //     Debug.Log("[WallMaterialSetter] ✅ Материал для стен (в WallMaterialSetter) готов к использованию.");
        // }
        
        // if (floorMaterial != null)
        // {
        //     // arInitializer.HorizontalPlaneMaterial = floorMaterial; // ОШИБКА: Свойство только для чтения
        //     Debug.Log("[WallMaterialSetter] ✅ Материал для пола (в WallMaterialSetter) готов к использованию.");
        // }
        
        // Обновляем материалы существующих плоскостей, используя материалы из WallMaterialSetter
        UpdateExistingPlanesGraphics();
    }
    
    /// <summary>
    /// Обновляет графическое представление (материалы) для существующих плоскостей.
    /// </summary>
    public void UpdateExistingPlanesGraphics() // Переименован для ясности
    {
        // Важно: ARManagerInitializer2.Instance.generatedPlanes может быть более надежным источником
        // чем поиск по тегу или имени, особенно если плоскости создаются/удаляются динамически.
        // Однако, если WallMaterialSetter должен влиять и на плоскости, созданные не ARManagerInitializer2,
        // то поиск по тегу/имени остается актуальным.

        // Пока оставим поиск по тегу/имени, но рассмотрим использование generatedPlanes
        GameObject[] wallPlanes = GameObject.FindGameObjectsWithTag("WallPlane");
        if (wallPlanes.Length == 0)
        {
            wallPlanes = FindWallPlanesByName(); // Поиск по имени как фоллбэк
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
} 