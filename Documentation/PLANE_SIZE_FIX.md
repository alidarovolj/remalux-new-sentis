# Исправление размеров сегментированных плоскостей ✅

## Проблема
Плоскости от сегментации создавались слишком маленькими и не покрывали всю видимую стену. На скриншоте было видно, что красные плоскости занимают только небольшую часть стен.

## Исправления

### 1. Увеличены лимиты размеров плоскостей
```csharp
// БЫЛО
maxPlaneSize = 2.5f;
maxWallHeight = 2.5f;
maxWallWidth = 3.0f;

// СТАЛО
maxPlaneSize = 10.0f;     // Увеличено в 4 раза
maxWallHeight = 6.0f;     // Увеличено в 2.4 раза
maxWallWidth = 8.0f;      // Увеличено в 2.7 раза
```

### 2. Увеличен множитель размера плоскостей
```csharp
// БЫЛО
planeSizeMultiplier = 0.4f;  // Плоскости были в 2.5 раза меньше

// СТАЛО  
planeSizeMultiplier = 1.5f;  // Плоскости теперь в 1.5 раза больше
```

### 3. Добавлено применение множителя в расчете размеров
В методе `UpdateOrCreatePlaneForWallArea` теперь размеры умножаются на `planeSizeMultiplier`:
```csharp
finalPlaneWorldWidth = (topWidth + bottomWidth) / 2f * planeSizeMultiplier;
finalPlaneWorldHeight = (leftHeight + rightHeight) / 2f * planeSizeMultiplier;
```

### 4. Увеличено максимальное расстояние рейкастинга
```csharp
// БЫЛО
actualDistanceFromCameraForPlane = Mathf.Clamp(..., 6.0f);

// СТАЛО
actualDistanceFromCameraForPlane = Mathf.Clamp(..., 12.0f);
```

### 5. Ослаблены ограничения
```csharp
// БЫЛО
minPlaneSize = 0.5f;
maxAspectRatio = 4.0f;

// СТАЛО
minPlaneSize = 0.3f;      // Более гибкий минимум
maxAspectRatio = 8.0f;    // Разрешены более вытянутые плоскости
```

## Результат
- **Плоскости теперь покрывают всю видимую стену** вместо небольших фрагментов
- Увеличенный `planeSizeMultiplier` (1.5f) делает плоскости на 50% больше расчетного размера
- Поддержка больших помещений с расстоянием до 12 метров
- Более гибкие ограничения позволяют создавать плоскости для длинных стен

## Настройка в инспекторе
Все параметры доступны для настройки в Unity Inspector:
- `Max Plane Size`: 10.0f
- `Min Plane Size`: 0.3f  
- `Max Aspect Ratio`: 8.0f
- `Max Wall Height`: 6.0f
- `Max Wall Width`: 8.0f
- `Plane Size Multiplier`: 1.5f

При необходимости можно увеличить `Plane Size Multiplier` до 2.0f для еще большего покрытия стены. 