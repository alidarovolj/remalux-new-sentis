# Финальное решение: Плоскости не генерируются из сегментации

## Статус проблемы

После всех исправлений:
- ✅ Сегментация работает (маски генерируются с 70-100% обнаружением стен)
- ✅ Обработка масок теперь не блокируется проверкой AR сессии
- ✅ Коллайдеры создаются SimulationEnvironmentSetup (6 коллайдеров)
- ✅ Логирование включено для диагностики
- ❌ Плоскости все еще не создаются

## Диагностированные проблемы

1. **Основная проблема**: `ARManagerInitializer2` не получает события от `WallSegmentation`
   - Несмотря на то, что подписка настроена, события не доходят
   - В логах нет сообщений о вызове `OnSegmentationMaskUpdated` в `ARManagerInitializer2`

## Действия для решения

### 1. Проверьте в Unity Inspector

На компоненте **ARManagerInitializer2**:
- ✅ `Use Detected Planes` = **включен** (должна быть галочка)
- `Min Area Size In Pixels` = **35** или меньше
- `Wall Pixel Threshold` = **100**
- `Max Ray Distance` = **10**
- `Min Hit Distance Threshold` = **0.1**
- `Raycast Layer Mask` = **Everything** или включите слои "Default" и "SimulatedEnvironment"

### 2. Включите Debug флаги

В секции **Debug Settings** компонента **ARManagerInitializer2**:
- ✅ Enable Detailed Plane Logging
- ✅ Enable Detailed Cleanup Logging
- ✅ Enable Detailed Raycast Logging

### 3. Проверьте компонент WallSegmentation

На компоненте **WallSegmentation**:
- `Wall Class Index` = **1**
- `Wall Confidence` = **0.6**
- `Segmentation Confidence Threshold` = **0.01**

### 4. Запустите сцену и проверьте логи

После включения логирования вы должны увидеть:
1. `[ARManagerInitializer2] Попытка подписки на события WallSegmentation...`
2. `[ARManagerInitializer2] Найден экземпляр WallSegmentation: ... Подписка на OnSegmentationMaskUpdated.`
3. `[ARManagerInitializer2] ✅ Подписка на события OnSegmentationMaskUpdated настроена`
4. `[ARManagerInitializer2-OnSegmentationMaskUpdated] ✅ Маска сегментации получена: ...`
5. `[ARManagerInitializer2] Processing segmentation mask...`
6. `[ARManagerInitializer2-ProcessSegmentationMask] ✅ Обработка маски сегментации ...`
7. `[ARManagerInitializer2-CreatePlanesFromMask] ✅ Начало создания плоскостей из маски...`

### 5. Альтернативное решение

Если события все еще не работают, можно попробовать прямое обращение:

1. Добавьте в `ARManagerInitializer2` прямую проверку в Update():

```csharp
// В методе Update(), после существующего кода
if (frameCounter % 30 == 0) // Каждые 30 кадров
{
    WallSegmentation ws = FindObjectOfType<WallSegmentation>();
    if (ws != null && ws.GetLatestMask() != null)
    {
        Debug.Log("[ARManagerInitializer2] Прямая проверка: WallSegmentation имеет маску!");
        OnSegmentationMaskUpdated(ws.GetLatestMask());
    }
}
```

## Важные настройки

1. **Слои коллайдеров**: Убедитесь, что созданные `SimulationEnvironmentSetup` коллайдеры находятся на слое, включенном в `Raycast Layer Mask`

2. **Размеры областей**: Если области слишком малы, уменьшите `Min Area Size In Pixels` до 10-20

3. **Материалы**: Убедитесь, что `Vertical Plane Material` и `Horizontal Plane Material` назначены или будут созданы автоматически

## Контрольный чек-лист

- [ ] `ARManagerInitializer2` активен в сцене
- [ ] `WallSegmentation` активен в сцене
- [ ] `SimulationEnvironmentSetup` создал коллайдеры
- [ ] Debug флаги включены
- [ ] В консоли появляются логи о подписке
- [ ] В консоли появляются логи о получении масок
- [ ] В консоли появляются логи о создании плоскостей 