using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Компонент для автоматической коррекции cullingMask у всех камер в сцене.
/// Устанавливает cullingMask в -1 (Everything) для корректного отображения ARPlane.
/// </summary>
public class CameraCullingMaskFixer : MonoBehaviour
{
    private HashSet<int> processedCameraIds = new HashSet<int>();
    private int lastCameraCount = 0;
    
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

    // Запускается в момент старта
    void Start()
    {
        Debug.Log("[CameraCullingMaskFixer] 🔥 Компонент запущен");
        
        // Устанавливаем cullingMask для всех существующих камер
        SetAllCamerasToEverything();
        
        // Запускаем периодическую проверку камер
        StartCoroutine(CheckCamerasRoutine());
        
        // Запускаем корутину для проверки поиска камер по имени
        StartCoroutine(FindCamerasByNameRoutine());
    }

    // Выполняется каждый кадр для быстрого реагирования на новые камеры
    void Update()
    {
        // Проверяем, изменилось ли количество камер
        int currentCameraCount = Camera.allCameras.Length;
        if (currentCameraCount != lastCameraCount)
        {
            Debug.Log($"[CameraCullingMaskFixer] 🔄 Обнаружено изменение количества камер: {lastCameraCount} → {currentCameraCount}");
            lastCameraCount = currentCameraCount;
            SetAllCamerasToEverything();
        }
    }
    
    // Устанавливает cullingMask в -1 (Everything) для всех камер
    private void SetAllCamerasToEverything()
    {
        Camera[] allCameras = Camera.allCameras;
        bool shouldLog = Time.frameCount % 300 == 0 || Time.frameCount < 10;
        
        bool anyChanges = false;
        int fixedCount = 0;
        
        int arPlanesLayer = LayerMask.NameToLayer("ARPlanes");

        foreach (Camera camera in allCameras)
        {
            int targetMask;
            string targetMaskDescription;

            if (camera.CompareTag("MainCamera") || camera.name == "Main Camera")
            {
                targetMask = -1; // Everything
                if (arPlanesLayer != -1)
                {
                    targetMask &= ~(1 << arPlanesLayer); // Exclude ARPlanes layer
                }
                targetMaskDescription = $"Everything except ARPlanes ({FormatCullingMask(targetMask)})";
            }
            else
            {
                targetMask = -1; // Everything
                targetMaskDescription = "Everything (-1)";
            }

            // Если эта камера еще не обработана
            if (!processedCameraIds.Contains(camera.GetInstanceID()))
            {
                // Если cullingMask не установлен в целевую маску
                if (camera.cullingMask != targetMask)
                {
                    string oldMask = FormatCullingMask(camera.cullingMask);
                    string oldName = camera.name;
                    
                    camera.cullingMask = targetMask;
                    anyChanges = true;
                    fixedCount++;
                    
                    Debug.LogWarning($"[CameraCullingMaskFixer] 🔧 Исправлен cullingMask для камеры '{oldName}': {oldMask} → {targetMaskDescription}");
                }
                
                // Помечаем камеру как обработанную
                processedCameraIds.Add(camera.GetInstanceID());
            }
            else
            {
                // Для уже обработанных камер проверяем, не изменился ли cullingMask на нецелевой
                if (camera.cullingMask != targetMask)
                {
                    string oldMask = FormatCullingMask(camera.cullingMask);
                    
                    camera.cullingMask = targetMask;
                    anyChanges = true;
                    fixedCount++;
                    
                    Debug.LogWarning($"[CameraCullingMaskFixer] 🔁 Повторно исправлен cullingMask для камеры '{camera.name}': {oldMask} → {targetMaskDescription}");
                }
            }
        }
        
        if (anyChanges)
        {
            Debug.Log($"[CameraCullingMaskFixer] ✅ Исправлено {fixedCount} камер из {allCameras.Length}");
        }
        else if (allCameras.Length > 0 && shouldLog)
        {
            Debug.Log($"[CameraCullingMaskFixer] ✓ Проверено {allCameras.Length} камер, все уже настроены корректно");
        }
    }

    // Запускает корутину для периодической проверки камер
    private IEnumerator CheckCamerasRoutine()
    {
        yield return new WaitForSeconds(2f); // Ждем 2 секунды после старта
        
        int checkCount = 0;
        while (true)
        {
            // Логируем только каждые 10 проверок
            if (checkCount % 10 == 0)
            {
                Debug.Log("[CameraCullingMaskFixer] 🔄 Периодическая проверка всех камер...");
            }
            
            SetAllCamerasToEverything();
            checkCount++;
            
            yield return new WaitForSeconds(15f); // Проверяем каждые 15 секунд
        }
    }
    
    // Корутина для поиска камер по имени
    private IEnumerator FindCamerasByNameRoutine()
    {
        string[] cameraNames = new string[] { "AR Camera", "SimulationCamera", "Main Camera", "ARCamera", "XR Origin" };
        
        yield return new WaitForSeconds(2f); // увеличиваем задержку
        
        Debug.Log("[CameraCullingMaskFixer] 🔍 Начат поиск камер по именам...");
        int arPlanesLayer = LayerMask.NameToLayer("ARPlanes");
        
        for (int i = 0; i < 3; i++) // уменьшаем количество попыток
        {
            bool foundAny = false;
            bool shouldLog = i == 0; // логируем только при первой попытке
            
            foreach (string cameraNameInList in cameraNames)
            {
                GameObject cameraObj = GameObject.Find(cameraNameInList);
                if (cameraObj != null)
                {
                    Camera camera = cameraObj.GetComponent<Camera>();
                    if (camera != null)
                    {
                        foundAny = true;
                        
                        int targetMask;
                        string targetMaskDescription;

                        if (camera.CompareTag("MainCamera") || camera.name == "Main Camera")
                        {
                            targetMask = -1; // Everything
                            if (arPlanesLayer != -1)
                            {
                                targetMask &= ~(1 << arPlanesLayer); // Exclude ARPlanes layer
                            }
                            targetMaskDescription = $"Everything except ARPlanes ({FormatCullingMask(targetMask)})";
                        }
                        else
                        {
                            targetMask = -1; // Everything
                            targetMaskDescription = "Everything (-1)";
                        }

                        // Фиксируем cullingMask, если нужно
                        if (camera.cullingMask != targetMask)
                        {
                            string oldMask = FormatCullingMask(camera.cullingMask);
                            camera.cullingMask = targetMask;
                            
                            Debug.LogWarning($"[CameraCullingMaskFixer] 🔧 Найдена и исправлена камера по имени '{cameraNameInList}': {oldMask} → {targetMaskDescription}");
                        }
                        else if (shouldLog)
                        {
                            Debug.Log($"[CameraCullingMaskFixer] ✓ Найдена камера '{cameraNameInList}', cullingMask уже установлен в {targetMaskDescription}");
                        }
                    }
                }
            }
            
            if (!foundAny && shouldLog)
            {
                Debug.Log("[CameraCullingMaskFixer] 🔍 Не найдено камер по заданным именам");
            }
            
            yield return new WaitForSeconds(5f); // увеличиваем интервал между попытками
        }
    }
    
    // Метод для ручного вызова исправления (например, после загрузки сцены)
    public void ForceFixAllCameras()
    {
        Debug.Log("[CameraCullingMaskFixer] 🔄 Ручной запуск исправления всех камер");
        processedCameraIds.Clear(); // Сбрасываем список обработанных камер
        SetAllCamerasToEverything();
    }
} 


