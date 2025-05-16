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
    public Material floorMaterial;
    
    [Header("Настройки")]
    [Tooltip("Применять материал автоматически при старте")]
    public bool applyOnStart = true;
    
    private ARManagerInitializer2 arManager;
    
    private void Start()
    {
        if (applyOnStart)
        {
            SetMaterials();
        }
    }
    
    /// <summary>
    /// Устанавливает материалы для стен и пола
    /// </summary>
    public void SetMaterials()
    {
        // Находим ARManagerInitializer2
        arManager = FindObjectOfType<ARManagerInitializer2>();
        if (arManager == null)
        {
            Debug.LogError("[WallMaterialSetter] ❌ ARManagerInitializer2 не найден в сцене!");
            return;
        }
        
        // Применяем материал для стен
        if (wallMaterial != null)
        {
            arManager.verticalPlaneMaterial = wallMaterial;
            Debug.Log("[WallMaterialSetter] ✅ Установлен материал для стен");
        }
        
        // Применяем материал для пола
        if (floorMaterial != null)
        {
            arManager.horizontalPlaneMaterial = floorMaterial;
            Debug.Log("[WallMaterialSetter] ✅ Установлен материал для пола");
        }
        
        // Обновляем существующие плоскости
        UpdateExistingPlanes();
    }
    
    /// <summary>
    /// Обновляет материалы для существующих плоскостей
    /// </summary>
    public void UpdateExistingPlanes()
    {
        // Обновляем материалы для всех существующих WallPlane
        GameObject[] wallPlanes = GameObject.FindGameObjectsWithTag("WallPlane");
        if (wallPlanes.Length == 0)
        {
            // Пробуем найти по имени, если тег не установлен
            wallPlanes = FindWallPlanesByName();
        }
        
        int updatedCount = 0;
        foreach (GameObject plane in wallPlanes)
        {
            MeshRenderer renderer = plane.GetComponent<MeshRenderer>();
            if (renderer != null && wallMaterial != null)
            {
                renderer.material = new Material(wallMaterial);
                updatedCount++;
            }
        }
        
        Debug.Log($"[WallMaterialSetter] Обновлено материалов: {updatedCount}/{wallPlanes.Length}");
    }
    
    /// <summary>
    /// Ищет объекты WallPlane по имени
    /// </summary>
    private GameObject[] FindWallPlanesByName()
    {
        // Находим все объекты с именем, начинающимся с "WallPlane"
        GameObject[] allObjects = GameObject.FindObjectsOfType<GameObject>();
        System.Collections.Generic.List<GameObject> wallPlanes = new System.Collections.Generic.List<GameObject>();
        
        foreach (GameObject obj in allObjects)
        {
            if (obj.name.StartsWith("WallPlane"))
            {
                wallPlanes.Add(obj);
            }
        }
        
        return wallPlanes.ToArray();
    }
} 