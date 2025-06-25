# Исправление проблемы генерации плоскостей из сегментации

## Проблема
Плоскости не генерировались из результатов сегментации, несмотря на то что:
- Модель сегментации работала корректно (маски генерировались с 83-99% обнаружения стен)
- Метод `OnSegmentationMaskUpdated` вызывался и получал маски
- Компонент `SegmentationPlaneDebugger` показывал высокий процент красных пикселей

## Причина
В методе `Update` класса `ARManagerInitializer2` была проверка состояния AR сессии:

```csharp
if (sessionManager != null && sessionManager.IsSessionInitialized())
{
    if (maskUpdated)
    {
        ProcessSegmentationMask();
        maskUpdated = false;
    }
}
```

Эта проверка блокировала вызов `ProcessSegmentationMask()`, потому что:
- В режиме симуляции AR сессия находилась в состоянии `SessionTracking`, а не `Ready`
- Метод `IsSessionInitialized()` возвращал `false`
- Маски сегментации накапливались, но не обрабатывались

## Решение
Изменена логика в методе `Update` для обработки масок независимо от состояния AR сессии:

```csharp
// Обрабатываем маску сегментации независимо от состояния AR сессии
// Это позволит создавать плоскости из сегментации даже в режиме симуляции
if (maskUpdated)
{
    // Проверяем готовность основных компонентов
    if (xrOrigin != null && xrOrigin.Camera != null)
    {
        Debug.Log("[ARManagerInitializer2] Processing segmentation mask...");
        ProcessSegmentationMask();
        maskUpdated = false;
    }
    else
    {
        if (frameCounter % 60 == 0) // Логировать не каждый кадр
        {
            Debug.LogWarning("[ARManagerInitializer2] Mask updated, but XROrigin or Camera not ready. Waiting...");
        }
    }
}
```

## Результат
Теперь плоскости должны генерироваться из результатов сегментации:
1. Маска сегментации обрабатывается сразу после получения
2. Не требуется полная инициализация AR сессии
3. Работает как в режиме симуляции, так и на реальном устройстве

## Проверка
После запуска приложения в консоли должны появиться логи:
- `[ARManagerInitializer2] Processing segmentation mask...`
- Логи о создании плоскостей из `CreatePlanesFromMask`
- Визуально должны появиться плоскости на стенах

## Дополнительные настройки для отладки
Если плоскости все еще не появляются, проверьте в инспекторе `ARManagerInitializer2`:
- `Wall Pixel Threshold` = 100
- `Min Area Size In Pixels` = 150
- `Min Pixels Dimension For Area` = 10
- `Max Ray Distance` = 10
- Включите флаги логирования для отладки 