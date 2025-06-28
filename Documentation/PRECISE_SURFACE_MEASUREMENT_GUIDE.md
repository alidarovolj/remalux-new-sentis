# Система точного измерения поверхностей

## Обзор

Система точного измерения поверхностей предназначена для создания AR плоскостей с размерами, максимально приближенными к реальным размерам поверхностей. Вместо приблизительных размеров от AR Foundation, система использует:

- **Многоточечное измерение** - анализ множества точек на поверхности
- **Автоматическую калибровку** - коррекция на основе эталонных объектов
- **Сглаживание измерений** - устранение шума между кадрами
- **Интеграцию с WallPainterController** - использование точных размеров при создании плоскостей

## Быстрая настройка

### 1. Автоматическая настройка

1. Откройте Unity меню: **AR Tools > Setup Precise Surface Measurement**
2. Нажмите **"🔧 Автоматическая настройка"**
3. Система автоматически:
   - Добавит `SurfaceMeasurementSystem`
   - Интегрирует с `WallPainterController`
   - Настроит оптимальные параметры

### 2. Проверка интеграции

В инспекторе `WallPainterController` должны появиться новые параметры:
- ✅ **Use Precise Surface Measurement**: включено
- ✅ **Auto Calibrate Sizes**: включено
- 🎯 **Min Measurement Confidence**: 0.6

## Компоненты системы

### SurfaceMeasurementSystem

Основной компонент для точного измерения поверхностей.

**Настройки измерения:**
- **Use Multi Point Measurement**: многоточечное измерение (рекомендуется: включено)
- **Measurement Points**: количество точек для измерения (рекомендуется: 5)
- **Size Correction Factor**: множитель коррекции размера (рекомендуется: 1.2)

**Калибровка размеров:**
- **Enable Auto Calibration**: автоматическая калибровка (рекомендуется: включено)
- **Reference Object Size**: размер эталонного объекта в метрах (по умолчанию: 0.21м для листа A4)

**Фильтрация измерений:**
- **Min Surface Size**: минимальный размер поверхности (рекомендуется: 0.3м)
- **Max Surface Size**: максимальный размер поверхности (рекомендуется: 10.0м)
- **Smooth Measurements**: сглаживание измерений (рекомендуется: включено)

## Принцип работы

### 1. Автоматическая калибровка

Система автоматически ищет объекты известного размера для калибровки:

```
✅ Обнаружен эталонный объект (лист A4): 21×29 см
🔧 Калибровка: коэффициент 1.05
📏 Теперь размеры будут точнее на 5%
```

**Подходящие объекты для автокалибровки:**
- 📄 Лист A4 (21×29.7 см)
- 📱 Смартфон (обычно 13-16 см)
- 💳 Кредитная карта (8.5×5.4 см)
- 📖 Книга стандартного размера

### 2. Многоточечное измерение

Вместо одной точки система анализирует сетку точек на поверхности:

```
🎯 Анализ поверхности:
├─ Создано 25 точек измерения (5×5)
├─ Валидных точек: 23
├─ Размер по точкам: 2.85×2.11м
└─ Уверенность: 85%
```

### 3. Сглаживание и фильтрация

Система применяет фильтры для устранения шума:

```
📊 Обработка измерения:
├─ Исходный размер: 2.80×2.15м
├─ После калибровки: 2.94×2.26м
├─ После сглаживания: 2.85×2.11м
└─ Финальный результат: 2.85×2.11м ✅
```

## Использование в коде

### Получение точных размеров

```csharp
// Получить систему измерения
var surfaceSystem = FindObjectOfType<SurfaceMeasurementSystem>();

// Получить точный размер AR плоскости
Vector2 preciseSize = surfaceSystem.GetPreciseSurfaceSize(arPlane);

Debug.Log($"Точный размер: {preciseSize.x:F2}×{preciseSize.y:F2}м");
```

### Ручная калибровка

```csharp
// Калибровка с эталонным объектом
var surfaceSystem = FindObjectOfType<SurfaceMeasurementSystem>();
surfaceSystem.ManualCalibration(referencePlane, 0.21f); // лист A4
```

### Подписка на события

```csharp
var surfaceSystem = FindObjectOfType<SurfaceMeasurementSystem>();

// Подписка на измерения
surfaceSystem.OnSurfaceMeasured += (plane, size) => {
    Debug.Log($"Измерена поверхность: {size.x:F2}×{size.y:F2}м");
};
```

## Ручная калибровка

### Через Unity Editor

1. Откройте **AR Tools > Manual Calibration Helper**
2. Выберите тип эталонного объекта или введите размер вручную
3. Поместите объект в поле зрения AR камеры
4. Запустите приложение (Play Mode)
5. Нажмите **"🎯 Начать калибровку"**

### Во время выполнения

```csharp
// Пример калибровки с листом A4
var surfaceSystem = FindObjectOfType<SurfaceMeasurementSystem>();

// Найти подходящую плоскость
ARPlane calibrationPlane = null;
var planeManager = FindObjectOfType<ARPlaneManager>();

foreach (var plane in planeManager.trackables)
{
    float avgSize = (plane.size.x + plane.size.y) / 2f;
    if (avgSize > 0.15f && avgSize < 0.35f) // Размер примерно как A4
    {
        calibrationPlane = plane;
        break;
    }
}

if (calibrationPlane != null)
{
    // Калибровка с реальным размером A4
    surfaceSystem.ManualCalibration(calibrationPlane, 0.21f);
    Debug.Log("✅ Калибровка завершена");
}
```

## Интеграция с WallPainterController

Система автоматически интегрируется с `WallPainterController` для создания плоскостей точных размеров:

```csharp
// В WallPainterController автоматически используются точные размеры
private void CreateWallPlane(List<Vector3> vertices, int[] triangles)
{
    // Если включено точное измерение
    if (usePreciseSurfaceMeasurement && surfaceMeasurementSystem != null)
    {
        // Система автоматически применяет калибровку и коррекцию
        Vector2 preciseSize = GetCalibratedSize(vertices);
        ApplySizeCorrection(vertices, preciseSize);
    }
    
    // Создание плоскости с точными размерами
    CreateMeshWithPreciseSize(vertices, triangles);
}
```

## Отладка и мониторинг

### Логи системы

```
[SurfaceMeasurementSystem] 📏 Измерена поверхность: 
  Оригинал: 2.45×1.80м → Точное: 2.85×2.11м (уверенность: 8.5)

[SurfaceMeasurementSystem] 🔧 Начинаю автоматическую калибровку...
[SurfaceMeasurementSystem] ✅ Автокалибровка завершена. 
  Коэффициент: 1.16 (найдено 2 эталонных объектов)
```

### Context Menu команды

В инспекторе `SurfaceMeasurementSystem`:
- **Показать все измерения** - вывод всех сохраненных измерений
- **Сбросить калибровку** - возврат к исходным настройкам

## Параметры производительности

### Рекомендуемые настройки

**Для хорошей производительности:**
- Measurement Points: 3-5
- Smooth Measurements: включено
- Debug Mode: выключено в релизе

**Для максимальной точности:**
- Measurement Points: 7-10
- Size Correction Factor: 1.3
- Min Measurement Confidence: 0.8

### Оптимизация

Система автоматически оптимизирует производительность:
- Измерения кэшируются для повторного использования
- Сглаживание снижает количество пересчетов
- Фильтрация исключает невалидные измерения

## Решение проблем

### Плоскости все еще неточных размеров

1. **Проверьте калибровку:**
   ```
   [SurfaceMeasurementSystem] ⚠️ Эталонные объекты для калибровки не найдены
   ```
   - Поместите лист A4 или другой объект известного размера в поле зрения
   - Выполните ручную калибровку

2. **Увеличьте Size Correction Factor:**
   - Попробуйте значения 1.3-1.5
   - Проверьте логи для анализа изменений

3. **Проверьте интеграцию:**
   - Убедитесь что `usePreciseSurfaceMeasurement = true`
   - Проверьте что `surfaceMeasurementSystem` назначен в инспекторе

### Низкая уверенность измерений

```
[SurfaceMeasurementSystem] ⚠️ Недостаточно точек для измерения: 2
```

**Решения:**
- Уменьшите `measurementPoints` до 3
- Увеличьте `maxSurfaceSize`
- Проверьте что AR плоскости стабильны

### Система не калибруется автоматически

1. **Поместите эталонный объект:**
   - Лист A4 должен быть полностью виден
   - Объект должен лежать горизонтально
   - Подождите 3-5 секунд для обнаружения

2. **Проверьте размеры объекта:**
   - AR Foundation должен распознать объект как плоскость
   - Размер должен быть 15-35 см для автокалибровки

## Примеры использования

### Создание плоскости точного размера стены

```csharp
public void CreatePreciseWallPlane(Vector2 touchPosition)
{
    // 1. Получить AR плоскость в точке касания
    var arPlane = GetARPlaneAtTouch(touchPosition);
    if (arPlane == null) return;
    
    // 2. Получить точные размеры
    var preciseSize = surfaceMeasurementSystem.GetPreciseSurfaceSize(arPlane);
    
    // 3. Создать плоскость с точными размерами
    var wallPlane = CreateWallPlane(arPlane.center, preciseSize);
    
    Debug.Log($"Создана стена размером {preciseSize.x:F2}×{preciseSize.y:F2}м");
}
```

### Сравнение точности

```csharp
public void CompareAccuracy(ARPlane plane)
{
    // Исходный размер от AR Foundation
    var originalSize = plane.size;
    
    // Точный размер от системы измерения
    var preciseSize = surfaceMeasurementSystem.GetPreciseSurfaceSize(plane);
    
    // Вычисление улучшения точности
    var improvement = Vector2.Distance(preciseSize, originalSize);
    
    Debug.Log($"Улучшение точности: {improvement:F2}м");
    Debug.Log($"Исходный: {originalSize.x:F2}×{originalSize.y:F2}м");
    Debug.Log($"Точный: {preciseSize.x:F2}×{preciseSize.y:F2}м");
}
```

## Заключение

Система точного измерения поверхностей значительно улучшает точность создаваемых AR плоскостей:

- 🎯 **Точность**: плоскости соответствуют реальным размерам стен
- 🔧 **Простота**: автоматическая настройка и калибровка
- ⚡ **Производительность**: оптимизированные алгоритмы измерения
- 🛠️ **Гибкость**: ручная калибровка и настройка параметров

После настройки системы ваши AR плоскости будут создаваться с размерами, максимально приближенными к реальным размерам поверхностей в окружающем мире. 