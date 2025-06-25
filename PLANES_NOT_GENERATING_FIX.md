# Исправление: Плоскости не генерируются из сегментации

## Проблема
- Сегментация работает (маски генерируются с 100% обнаружением стен)
- События `OnSegmentationMaskUpdated` вызываются от `WallSegmentation`
- НО: `ARManagerInitializer2` не обрабатывает маски (нет логов `ProcessSegmentationMask`)
- Плоскости не создаются (0 плоскостей от сегментации)

## Причина
В методе `OnSegmentationMaskUpdated` класса `ARManagerInitializer2` была ошибка типа параметра:
- Было: `private void OnSegmentationMaskUpdated(Texture mask)`
- Нужно: `private void OnSegmentationMaskUpdated(RenderTexture mask)`

Из-за несоответствия типов подписка на событие не работала корректно.

## Исправление
1. **Изменен тип параметра в обработчике события** в `ARManagerInitializer2.cs`:
```csharp
// Было:
private void OnSegmentationMaskUpdated(Texture mask)

// Стало:
private void OnSegmentationMaskUpdated(RenderTexture mask)
```

2. **Включено логирование** для отладки процесса создания плоскостей

## Что нужно сделать в Unity

### 1. В инспекторе `ARManagerInitializer2`:
- **Enable Detailed Raycast Logging** = ✅ (включить для отладки)
- **Enable Custom Plane Creation Logging** = ✅ (включить для отладки)
- **Min Area Size In Pixels** = 35 (или меньше, если плоскости не создаются)
- **Wall Pixel Threshold** = 100
- **Max Ray Distance** = 20

### 2. Проверьте настройки слоев:
- `SimulationEnvironment` должен существовать (слой 8)
- Стены из `SimulationEnvironmentSetup` должны быть на этом слое

### 3. Запустите сцену и проверьте логи:
Теперь вы должны увидеть:
- `[ARManagerInitializer2] Маска сегментации обновлена: 56x56`
- `[ARManagerInitializer2] Processing segmentation mask...`
- `[ARManagerInitializer2-ProcessSegmentationMask] ✅ Обработка маски сегментации`
- `[ARManagerInitializer2-CreatePlanesFromMask] ✅ Начало создания плоскостей`
- Информацию о найденных областях и созданных плоскостях

## Ожидаемый результат
После исправления:
1. ✅ Маски сегментации будут обрабатываться
2. ✅ Области стен будут обнаруживаться
3. ✅ Плоскости будут создаваться из сегментации
4. ✅ Вы сможете красить эти плоскости

## Дополнительная отладка
Если плоскости все еще не создаются после исправления:
1. Проверьте логи на наличие сообщений о найденных областях
2. Убедитесь, что области проходят фильтр размера
3. Проверьте, что рейкасты попадают в коллайдеры
4. Попробуйте уменьшить `Min Area Size In Pixels` до 20-25

## Отключение логов
После того как все заработает, отключите детальное логирование для производительности:
- **Enable Detailed Raycast Logging** = ❌
- **Enable Custom Plane Creation Logging** = ❌ 