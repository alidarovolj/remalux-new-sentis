# Диагностика проблемы генерации плоскостей из сегментации

## Проблема
Плоскости не генерируются из результатов сегментации и рейкастинга.

## Возможные причины и решения

### 1. Модель сегментации не инициализирована
**Проверка:**
- В инспекторе `WallSegmentation` проверьте:
  - `Is Model Initialized` = true?
  - `Model Asset` назначен?
  - Нет ошибок в консоли при запуске?

**Решение:**
1. Убедитесь, что файл модели `segformer-model-new.sentis` находится в папке `StreamingAssets`
2. Проверьте, что `Model Asset` в `WallSegmentation` указывает на правильный файл
3. Проверьте параметры:
   - `Wall Class Index` = 1 (для segformer-b4-wall)
   - `Wall Confidence` = 0.6
   - `Segmentation Confidence Threshold` = 0.01

### 2. Маска сегментации не содержит данных о стенах
**Проверка:**
- Добавьте компонент `SegmentationPlaneDebugger` в сцену
- Запустите и посмотрите в консоль на "Анализ маски"
- Должны быть красные пиксели (стены)

**Решение:**
1. Уменьшите `Wall Confidence` в `WallSegmentation` до 0.3-0.4
2. Проверьте освещение сцены - модель может плохо работать в темноте
3. Убедитесь, что камера направлена на стены

### 3. Рейкасты не попадают в коллайдеры
**Проверка:**
- В `SegmentationPlaneDebugger` нажмите правой кнопкой на компоненте → "Test Raycast"
- Смотрите результат в консоли

**Решение:**
1. В `ARManagerInitializer2` проверьте настройки:
   - `Hit Layer Mask` должен включать слои: Default, SimulatedEnvironment, Wall
   - `Max Ray Distance` = 15
   - `Min Hit Distance Threshold` = 0.1
   
2. Убедитесь, что в сцене есть объекты с коллайдерами на этих слоях
3. Для XR Simulation добавьте коллайдеры к стенам вручную

### 4. Области в маске слишком маленькие
**Проверка:**
- В консоли ищите сообщения "Область слишком мала"

**Решение:**
В `ARManagerInitializer2` уменьшите:
- `Min Area Size In Pixels` = 50 (вместо 150)
- `Min Pixels Dimension For Area` = 1
- `Wall Pixel Threshold` = 60 (вместо 120)

### 5. Рейкасты фильтруются по нормалям
**Проверка:**
- Ищите в консоли "ОТФИЛЬТРОВАН по НОРМАЛИ"

**Решение:**
В `ARManagerInitializer2` увеличьте:
- `Max Wall Normal Angle Deviation` = 45 (вместо 25)

### 6. События не связаны правильно
**Проверка:**
- В `SegmentationPlaneDebugger` смотрите `Mask Update Count` - должен увеличиваться

**Решение:**
1. Убедитесь, что `ARManagerInitializer2` активен и в сцене
2. Проверьте, что `useDetectedPlanes` = false в `ARManagerInitializer2`
3. Убедитесь, что компоненты находятся в правильном порядке выполнения

## Пошаговая диагностика

1. **Добавьте `SegmentationPlaneDebugger` в сцену**
   - GameObject → Create Empty → Add Component → SegmentationPlaneDebugger

2. **Включите отладку в `ARManagerInitializer2`**
   - `Enable Detailed Raycast Logging` = true
   - `Enable Custom Plane Creation Logging` = true

3. **Запустите сцену и наблюдайте консоль**
   - Должны появиться сообщения о получении маски
   - Должны быть сообщения о рейкастах
   - Должны создаваться плоскости

4. **Если плоскости не создаются:**
   - Проверьте каждый пункт выше
   - Используйте Test Raycast для проверки коллайдеров
   - Посмотрите статистику в инспекторе `SegmentationPlaneDebugger`

## Быстрые настройки для тестирования

В `ARManagerInitializer2`:
```
Use Detected Planes = false (ВАЖНО!)
Min Plane Size In Meters = 0.1
Min Area Size In Pixels = 50
Wall Pixel Threshold = 60
Max Ray Distance = 15
Min Hit Distance Threshold = 0.1
Max Wall Normal Angle Deviation = 45
Enable Detailed Raycast Logging = true
```

В `WallSegmentation`:
```
Wall Class Index = 1
Wall Confidence = 0.4
Segmentation Confidence Threshold = 0.01
Debug Mode = true
Debug Flags = All (для полной отладки)
```

## Проверка XR Simulation

Если используете XR Simulation:
1. Убедитесь, что симулированное окружение имеет коллайдеры
2. Проверьте слои объектов (должны быть в Hit Layer Mask)
3. Добавьте MeshCollider к стенам если их нет

## Альтернативное решение

Если рейкасты не работают, можно временно отключить их:
1. В `ARManagerInitializer2.cs` найдите строку 2196:
   ```csharp
   return false; // ВРЕМЕННО ОТКЛЮЧЕНО: Не создаем эвристическую плоскость
   ```
2. Закомментируйте эту строку и раскомментируйте блок ниже для эвристического создания плоскостей 