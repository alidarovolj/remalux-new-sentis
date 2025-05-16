using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Специальный компонент для исправления SimulationCamera, который ищет его по имени в иерархии
/// </summary>
[DefaultExecutionOrder(-500)] // Выполняется раньше других скриптов
public class SimulationCameraFixer : MonoBehaviour
{
    private int fixCount = 0;
    
    // Преобразуем числовые значения cullingMask в имена слоев для отладки
    private static string FormatCullingMask(int mask)
    {
        if (mask == -1) return "Everything (-1)";
        if (mask == 0) return "Nothing (0)";
        
        List<string> layers = new List<string>();
        for (int i = 0; i < 32; i++)
        {
            int layerBit = 1 << i;
            if ((mask & layerBit) != 0)
            {
                string layerName = LayerMask.LayerToName(i);
                if (!string.IsNullOrEmpty(layerName))
                {
                    layers.Add(layerName);
                }
                else
                {
                    layers.Add($"Layer{i}");
                }
            }
        }
        
        return $"{mask} [{string.Join(", ", layers)}]";
    }
    
    private void Awake()
    {
        Debug.Log("[SimulationCameraFixer] 🔥 Инициализация специального фиксера для SimulationCamera...");
        
        // Сразу ищем и исправляем SimulationCamera
        bool foundSimulationCamera = FindAndFixSimulationCamera();
        
        if (foundSimulationCamera)
        {
            Debug.Log("[SimulationCameraFixer] ✅ SimulationCamera найдена и исправлена при старте");
        }
        else
        {
            Debug.Log("[SimulationCameraFixer] ⚠️ SimulationCamera не найдена при старте, будем искать позже");
        }
        
        // Запускаем непрерывную проверку
        StartCoroutine(ContinuouslyFixSimulationCamera());
    }

    private IEnumerator ContinuouslyFixSimulationCamera()
    {
        // Проверяем чаще в начале (первые 5 секунд)
        for (int i = 0; i < 5; i++)
        {
            yield return new WaitForSeconds(1f);
            FindAndFixSimulationCamera();
        }
        
        // Потом проверяем реже
        while (true)
        {
            yield return new WaitForSeconds(10f);
            FindAndFixSimulationCamera();
        }
    }
    
    private bool FindAndFixSimulationCamera()
    {
        bool found = false;
        bool shouldLog = Time.frameCount % 200 == 0 || Time.frameCount < 10;
        
        // Ищем все объекты с компонентом Camera в иерархии
        Camera[] allCameras = FindObjectsOfType<Camera>(true); // true = включая неактивные объекты
        
        foreach (Camera camera in allCameras)
        {
            // Проверяем, есть ли в имени "Simulation"
            if (camera.name.Contains("Simulation"))
            {
                found = true;
                
                // Если cullingMask не установлен в Everything (-1)
                if (camera.cullingMask != -1)
                {
                    int oldCullingMask = camera.cullingMask;
                    string oldMaskFormatted = FormatCullingMask(oldCullingMask);
                    
                    // Устанавливаем Everything (-1)
                    camera.cullingMask = -1;
                    fixCount++;
                    
                    Debug.LogWarning($"[SimulationCameraFixer] 🔧 Принудительно исправлен Culling Mask у {camera.name} (был {oldMaskFormatted}, стал Everything (-1)). Всего исправлений: {fixCount}");
                }
                else if (shouldLog)
                {
                    Debug.Log($"[SimulationCameraFixer] ✓ Проверка {camera.name} - cullingMask уже установлен в Everything (-1)");
                }
                
                // Также проверяем Far Clip Plane
                if (camera.farClipPlane < 100f)
                {
                    float oldFarClip = camera.farClipPlane;
                    camera.farClipPlane = 1000f;
                    Debug.LogWarning($"[SimulationCameraFixer] 🔧 Также исправлен Far Clip Plane у {camera.name} (был {oldFarClip}, стал 1000)");
                }
            }
        }
        
        // Если не нашли по компоненту Camera, попробуем найти по имени объекта
        if (!found)
        {
            GameObject simCameraObj = GameObject.Find("SimulationCamera");
            if (simCameraObj != null)
            {
                Camera simCamera = simCameraObj.GetComponent<Camera>();
                if (simCamera != null)
                {
                    found = true;
                    
                    // Если cullingMask не установлен в Everything (-1)
                    if (simCamera.cullingMask != -1)
                    {
                        int oldCullingMask = simCamera.cullingMask;
                        string oldMaskFormatted = FormatCullingMask(oldCullingMask);
                        
                        // Устанавливаем Everything (-1)
                        simCamera.cullingMask = -1;
                        fixCount++;
                        
                        Debug.LogWarning($"[SimulationCameraFixer] 🔧 Принудительно исправлен Culling Mask у SimulationCamera, найденной по имени (был {oldMaskFormatted}, стал Everything (-1)). Всего исправлений: {fixCount}");
                    }
                    else if (shouldLog)
                    {
                        Debug.Log("[SimulationCameraFixer] ✓ Проверка SimulationCamera (найдена по имени) - cullingMask уже установлен в Everything (-1)");
                    }
                }
                else
                {
                    Debug.LogWarning("[SimulationCameraFixer] ⚠️ Найден объект SimulationCamera, но на нем нет компонента Camera!");
                }
            }
        }
        
        // Также пытаемся найти через GetComponentsInChildren с корня сцены
        if (!found)
        {
            Camera[] sceneCameras = FindObjectsOfType<Camera>(true);
            foreach (Camera camera in sceneCameras)
            {
                if (camera.name.Contains("Simulation") || camera.gameObject.name.Contains("Simulation"))
                {
                    found = true;
                    
                    // Если cullingMask не установлен в Everything (-1)
                    if (camera.cullingMask != -1)
                    {
                        int oldCullingMask = camera.cullingMask;
                        string oldMaskFormatted = FormatCullingMask(oldCullingMask);
                        
                        // Устанавливаем Everything (-1)
                        camera.cullingMask = -1;
                        fixCount++;
                        
                        Debug.LogWarning($"[SimulationCameraFixer] 🔧 Принудительно исправлен Culling Mask у камеры, связанной с симуляцией: {camera.name} (был {oldMaskFormatted}, стал Everything (-1)). Всего исправлений: {fixCount}");
                    }
                }
            }
        }
        
        return found;
    }
    
    // Метод для ручного вызова исправления
    public void ForceFixSimulationCamera()
    {
        Debug.Log("[SimulationCameraFixer] 🔄 Ручной запуск исправления SimulationCamera");
        bool found = FindAndFixSimulationCamera();
        
        if (!found)
        {
            Debug.LogWarning("[SimulationCameraFixer] ⚠️ SimulationCamera не найдена при ручном запуске исправления");
        }
    }
} 