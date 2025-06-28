# Быстрое решение проблемы маленьких плоскостей (ChatGPT)

## Время внедрения: 30 минут

Если вам нужно БЫСТРО исправить проблему маленьких плоскостей, следуйте этой инструкции.

## Шаг 1: Откройте файл ARManagerInitializer2.cs

Путь: `Assets/Scripts/ARManagerInitializer2.cs`

## Шаг 2: Добавьте новые параметры

Найдите блок с полями класса (около строки 30-40) и добавьте:

```csharp
[Header("Plane Size Settings")]
[SerializeField, Range(1.0f, 1.5f)]
private float planeSizeMultiplier = 1.2f;

[SerializeField] 
private float maxWallWidth = 10.0f;

[SerializeField] 
private float maxWallHeight = 10.0f;
```

## Шаг 3: Найдите метод UpdateOrCreatePlaneForWallArea

Поиск: `UpdateOrCreatePlaneForWallArea` (примерно строка 2000+)

## Шаг 4: Измените расчет размеров плоскости

Найдите строки (примерно 2195-2196):
```csharp
float finalPlaneWorldWidth  = (area.width / (float)textureWidth) * worldWidthAtActualDistance;
float finalPlaneWorldHeight = (area.height / (float)textureHeight) * worldHeightAtActualDistance;
```

Замените на:
```csharp
float finalPlaneWorldWidth  = (area.width / (float)textureWidth) 
                              * worldWidthAtActualDistance * planeSizeMultiplier;
float finalPlaneWorldHeight = (area.height / (float)textureHeight) 
                              * worldHeightAtActualDistance * planeSizeMultiplier;
```

## Шаг 5: Обновите проверку размеров

Найдите проверку (примерно строка 2240):
```csharp
if (finalPlaneWorldWidth > 5.0f || finalPlaneWorldHeight > 5.0f)
```

Замените на:
```csharp
if (finalPlaneWorldWidth > maxWallWidth || finalPlaneWorldHeight > maxWallHeight)
{
    Debug.LogWarning($"⚠️ ОТМЕНА: Плоскость слишком большая " +
                     $"(Ширина: {finalPlaneWorldWidth:F2}м, Высота: {finalPlaneWorldHeight:F2}м). " +
                     $"Лимит: {maxWallWidth}×{maxWallHeight} м");
    return false;
}
```

## Шаг 6: Увеличьте лимит дистанции

Найдите строку (примерно 2031):
```csharp
actualDistanceFromCameraForPlane = Mathf.Clamp(actualDistanceFromCameraForPlane, 
                                               minHitDistanceThreshold, 6.0f);
```

Замените на:
```csharp
actualDistanceFromCameraForPlane = Mathf.Clamp(actualDistanceFromCameraForPlane, 
                                               minHitDistanceThreshold, 10.0f);
```

## Шаг 7: Добавьте логирование

В конце метода `UpdateOrCreatePlaneForWallArea`, перед `return true;` добавьте:

```csharp
Debug.Log($"[ARManagerInitializer2] Создана плоскость '{planeObj.name}' — размеры: " +
          $"{finalPlaneWorldWidth:F2}m x {finalPlaneWorldHeight:F2}m.");
```

## Шаг 8: Сохраните и протестируйте

1. Сохраните файл (Ctrl+S)
2. Вернитесь в Unity
3. На объекте с `ARManagerInitializer2` в инспекторе появятся новые параметры:
   - **Plane Size Multiplier**: 1.2 (увеличить для бóльших плоскостей)
   - **Max Wall Width**: 10
   - **Max Wall Height**: 10

## Настройка параметров

### Если плоскости все еще маленькие:
- Увеличьте `Plane Size Multiplier` до 1.3-1.5
- Проверьте логи в консоли

### Если плоскости слишком большие:
- Уменьшите `Plane Size Multiplier` до 1.0-1.1
- Настройте `Max Wall Width/Height`

## Преимущества этого решения
- ✅ Быстро (30 минут)
- ✅ Минимальный риск
- ✅ Легко откатить

## Недостатки
- ❌ Не решает проблему производительности
- ❌ Только прямоугольники
- ❌ Временное решение

## Следующий шаг

После того как это решение заработает, рекомендуется перейти на архитектуру на основе контуров для профессионального качества. См. `SOLUTION_SUMMARY.md` 