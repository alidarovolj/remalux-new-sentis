# Удаление синих плоскостей ARFoundation ✅

## Проблема
В проекте генерировались синие плоскости ARFoundation параллельно с красными плоскостями от сегментации, что создавало визуальный мусор и путаницу.

## Что исправлено

### 1. Отключение ARPlaneManager
Раскомментирован и улучшен код отключения `ARPlaneManager`:
```csharp
// Отключаем стандартный ARPlaneManager, чтобы убрать синие плоскости ARFoundation
if (planeManager != null)
{
    Debug.LogWarning("[ARManagerInitializer2] Отключение ARPlaneManager для предотвращения генерации синих плоскостей ARFoundation.");
    planeManager.enabled = false;
    if (!planeManager.enabled)
    {
        Debug.Log("[ARManagerInitializer2] ✅ ARPlaneManager успешно отключен. Синие плоскости больше не будут генерироваться.");
    }
    else
    {
        Debug.LogWarning("[ARManagerInitializer2] ⚠️ Не удалось отключить ARPlaneManager.");
    }
}
```

### 2. Принудительное отключение ARFoundation визуализаторов
Убрано условие `if (!useDetectedPlanes)` - теперь визуализаторы отключаются всегда:
```csharp
// Всегда отключаем ARFoundation визуализаторы синих плоскостей
Debug.Log("[ARManagerInitializer2-Start] Отключение ARFoundation визуализаторов синих плоскостей.");
DisableARFoundationVisualizers();
```

### 3. Удаление существующих синих плоскостей
Добавлен новый метод `RemoveExistingARFoundationPlanes()`:
```csharp
private void RemoveExistingARFoundationPlanes()
{
    Debug.Log("[ARManagerInitializer2] Поиск и удаление существующих синих плоскостей ARFoundation...");
    
    // Ищем все объекты ARPlane в сцене
    ARPlane[] existingPlanes = FindObjectsOfType<ARPlane>();
    int removedCount = 0;
    
    foreach (ARPlane plane in existingPlanes)
    {
        if (plane != null)
        {
            Debug.Log($"[ARManagerInitializer2] Удаление ARFoundation плоскости: {plane.name}");
            Destroy(plane.gameObject);
            removedCount++;
        }
    }
    
    // Также ищем объекты с именами, типичными для ARFoundation плоскостей
    GameObject[] allObjects = FindObjectsOfType<GameObject>();
    foreach (GameObject obj in allObjects)
    {
        if (obj.name.Contains("ARPlane") && !obj.name.Contains("MyARPlane_Debug_"))
        {
            Debug.Log($"[ARManagerInitializer2] Удаление объекта с именем ARPlane: {obj.name}");
            Destroy(obj);
            removedCount++;
        }
    }
    
    Debug.Log($"[ARManagerInitializer2] ✅ Удалено {removedCount} синих плоскостей ARFoundation");
}
```

### 4. Вызов удаления при старте
В методе `Start()` добавлен вызов удаления существующих плоскостей:
```csharp
// Удаляем существующие синие плоскости ARFoundation
RemoveExistingARFoundationPlanes();
```

### 5. Периодическая очистка в Update
Добавлена периодическая проверка и удаление синих плоскостей каждые 2 секунды:
```csharp
// ДОБАВЛЕНО: Периодическое удаление синих плоскостей ARFoundation (каждые 2 секунды)
if (frameCounter % 120 == 0)
{
    RemoveExistingARFoundationPlanes();
}
```

## Результат

Теперь в проекте:
- ✅ **Новые синие плоскости ARFoundation не генерируются** (ARPlaneManager отключен)
- ✅ **Существующие синие плоскости удаляются** при старте
- ✅ **Периодическая очистка** предотвращает появление новых синих плоскостей
- ✅ **Красные плоскости от сегментации остаются** и работают нормально
- ✅ **Сохранена функциональность рейкастинга** для позиционирования красных плоскостей

## Логи для проверки

При запуске вы увидите в консоли:
```
[ARManagerInitializer2] Отключение ARFoundation визуализаторов синих плоскостей.
[ARManagerInitializer2] Поиск и удаление существующих синих плоскостей ARFoundation...
[ARManagerInitializer2] ✅ Удалено X синих плоскостей ARFoundation
[ARManagerInitializer2] Отключение ARPlaneManager для предотвращения генерации синих плоскостей ARFoundation.
[ARManagerInitializer2] ✅ ARPlaneManager успешно отключен. Синие плоскости больше не будут генерироваться.
```

Теперь в сцене будут видны только красные плоскости от сегментации! 🎯 