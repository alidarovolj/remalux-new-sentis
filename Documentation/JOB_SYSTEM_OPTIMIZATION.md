# Оптимизация производительности с Job System

## Применимо к нашему проекту

### Текущие узкие места:
1. **Обработка сегментации маски** - анализ пикселей (CPU intensive)
2. **Поиск связных областей** - FindConnectedArea метод
3. **Создание множества плоскостей** - массовая генерация мешей

### Предлагаемые оптимизации:

#### 1. Параллельная обработка маски сегментации
```csharp
[BurstCompile]
public struct MaskAnalysisJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<Color32> pixels;
    [ReadOnly] public int width;
    [ReadOnly] public int height;
    [ReadOnly] public byte wallThreshold;
    
    [WriteOnly] public NativeArray<bool> wallPixels;
    
    public void Execute(int index)
    {
        wallPixels[index] = pixels[index].r > wallThreshold;
    }
}
```

#### 2. Параллельный рейкастинг
```csharp
[BurstCompile] 
public struct PlaneRaycastJob : IJobParallelFor
{
    [ReadOnly] public NativeArray<Vector3> rayOrigins;
    [ReadOnly] public NativeArray<Vector3> rayDirections;
    [ReadOnly] public float maxDistance;
    
    [WriteOnly] public NativeArray<bool> hitResults;
    [WriteOnly] public NativeArray<RaycastHit> hitData;
    
    public void Execute(int index)
    {
        // Параллельный рейкастинг для множества лучей
    }
}
```

#### 3. Асинхронная генерация мешей
```csharp
public struct MeshGenerationJob : IJob
{
    public NativeArray<Vector3> vertices;
    public NativeArray<int> triangles;
    
    // Генерация меша в фоновом потоке
    public void Execute()
    {
        // Тяжелые вычисления здесь
    }
}
```

## Когда применять:
- ✅ При обработке больших масок (>512x512)  
- ✅ При создании >5 плоскостей одновременно
- ✅ При сложной геометрии стен

## Когда НЕ применять:
- ❌ Текущий проект работает плавно
- ❌ Простые прямоугольные плоскости  
- ❌ Малые размеры масок

## Решение:
**Пока оставляем как есть** - система работает хорошо. Job System можно добавить позже при необходимости. 