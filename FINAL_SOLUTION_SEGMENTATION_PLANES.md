# Финальное решение проблемы генерации плоскостей из сегментации

## Проблема
Плоскости не генерировались из масок сегментации, несмотря на то что:
- Модель сегментации работала (маски с 80-100% обнаружением стен)
- События `OnSegmentationMaskUpdated` вызывались
- Маски содержали корректные данные

## Найденные причины

### 1. Блокировка обработки масок в Update()
В методе `Update` класса `ARManagerInitializer2` была проверка состояния AR сессии:
```csharp
if (sessionManager != null && sessionManager.IsSessionInitialized())
```
Которая блокировала вызов `ProcessSegmentationMask()` в режиме симуляции.

### 2. Отсутствие коллайдеров в сцене
Для создания плоскостей используются рейкасты, которым нужны коллайдеры для попадания. В сцене было 0 коллайдеров.

## Примененные решения

### 1. Исправление логики обработки масок
В файле `Assets/Scripts/ARManagerInitializer2.cs` изменена логика в методе `Update()`:

```csharp
// Обрабатываем маску сегментации независимо от состояния AR сессии
if (maskUpdated)
{
    // Проверяем готовность основных компонентов
    if (xrOrigin != null && xrOrigin.Camera != null)
    {
        Debug.Log("[ARManagerInitializer2] Processing segmentation mask...");
        ProcessSegmentationMask();
        maskUpdated = false;
    }
}
```

### 2. Создание окружения с коллайдерами
Создан новый скрипт `SimulationEnvironmentSetup.cs` для автоматического создания комнаты с коллайдерами.

### 3. Включение отладочного логирования
Добавлены логи для отслеживания процесса:
- В `CreatePlanesFromMask()` - количество найденных областей
- В `UpdateOrCreatePlaneForWallArea()` - начало создания плоскостей
- Статистика обработки масок каждые 30 кадров

## Инструкция по применению

### 1. Добавьте SimulationEnvironmentSetup в сцену
1. Создайте пустой GameObject: `GameObject > Create Empty`
2. Назовите его "SimulationEnvironment"
3. Добавьте компонент `SimulationEnvironmentSetup`
4. Настройте параметры:
   - `Create Room On Start` = ✓
   - `Room Size` = (4, 3, 5) - размер комнаты
   - `Room Center` = (0, 1.5, 0) - центр комнаты
   - `Wall Layer Name` = "SimulatedEnvironment"

### 2. Проверьте настройки ARManagerInitializer2
В инспекторе компонента `ARManagerInitializer2`:
- `Min Area Size In Pixels` = 35 (или меньше)
- `Wall Pixel Threshold` = 100
- `Max Ray Distance` = 10
- `Raycast Layer Mask` = включите слои "SimulatedEnvironment", "Default", "Wall"

### 3. Создайте слой SimulatedEnvironment
1. Edit > Project Settings > Tags and Layers
2. Добавьте слой "SimulatedEnvironment" (если его нет)

### 4. Запустите сцену
Теперь вы должны увидеть:
- Логи обработки масок: `[ARManagerInitializer2] Processing segmentation mask...`
- Логи создания областей: `[ARManagerInitializer2-CreatePlanesFromMask] Найдено областей: X`
- Логи создания плоскостей: `[ARManagerInitializer2-UpdateOrCreatePlaneForWallArea] ⭐ НАЧАЛО`

## Дополнительная отладка

### Если плоскости все еще не создаются:
1. **Проверьте размер областей** - уменьшите `Min Area Size In Pixels` до 10-20
2. **Проверьте порог** - уменьшите `Wall Pixel Threshold` до 50-80
3. **Проверьте рейкасты** - включите `Enable Detailed Raycast Logging`
4. **Визуализация рейкастов** - в Scene view будут отображаться лучи:
   - Зеленые = попадание
   - Красные = промах

### Компонент PlaneDebugVisualizer
Добавьте в сцену для визуальной отладки:
1. Создайте GameObject
2. Добавьте компонент `PlaneDebugVisualizer`
3. Назначьте ссылку на `ARManagerInitializer2`
4. Включите `Show Raycast Debug` и `Show Plane Creation Debug`

## Результат
После применения всех исправлений:
- Маски сегментации будут обрабатываться независимо от состояния AR сессии
- Рейкасты будут попадать в коллайдеры созданной комнаты
- Плоскости будут генерироваться в местах обнаружения стен на маске сегментации 