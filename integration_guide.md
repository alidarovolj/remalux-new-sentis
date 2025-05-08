# Руководство по интеграции модели сегментации с Unity Sentis

## Характеристики модели сегментации

Анализ модели `Assets/Models/model.onnx` показал следующие характеристики:

- **Входной тензор**: 
  - Имя: `pixel_values`
  - Форма: `[batch_size, num_channels, height, width]`
  - Фактические размеры для инференса: `[1, 3, 320, 320]`
  - Тип данных: `float32`

- **Выходной тензор**:
  - Имя: `logits`
  - Форма: `[batch_size, num_labels, height, width]`
  - Фактические размеры: `[1, 150, 80, 80]`
  - Тип данных: `float32`
  - Количество классов: 150
  - Масштаб понижения разрешения: 4x (от 320x320 до 80x80)

## Интеграция в WallSegmentation.cs

Для интеграции модели с существующим скриптом `WallSegmentation.cs`, необходимо внести следующие изменения:

1. **Обновление определения класса**:

```csharp
using Unity.Sentis;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class WallSegmentation : MonoBehaviour
{
    [SerializeField] private ModelAsset modelAsset;
    [SerializeField] private ARCameraManager arCameraManager;
    [SerializeField] private RenderTexture segmentationMaskTexture;
    
    // Индекс класса "стена" в модели сегментации (необходимо определить после анализа модели)
    [SerializeField] private int wallClassIndex = 1; // Предполагаемый индекс "стены" - требует уточнения
    
    // Порог вероятности для определения класса (при необходимости)
    [SerializeField] private float confidenceThreshold = 0.5f;
    
    private Model runtimeModel;
    private Worker engine;
    private Tensor<float> inputTensor;
    private Texture2D tempTexture;
    
    // Константы для модели
    private const int INPUT_WIDTH = 320;
    private const int INPUT_HEIGHT = 320;
    private const int INPUT_CHANNELS = 3;
    private const int OUTPUT_WIDTH = 80;
    private const int OUTPUT_HEIGHT = 80;
    private const int NUM_CLASSES = 150;
```

2. **Инициализация в Start()**:

```csharp
private void Start()
{
    if (modelAsset == null)
    {
        Debug.LogError("ModelAsset is not assigned in the inspector. Disabling script.");
        this.enabled = false;
        return;
    }

    // Инициализация модели Sentis
    runtimeModel = ModelLoader.Load(modelAsset);
    engine = new Worker(runtimeModel, BackendType.GPUCompute);
    
    // Создаем временную текстуру для преобразования изображения камеры
    tempTexture = new Texture2D(INPUT_WIDTH, INPUT_HEIGHT, TextureFormat.RGBA32, false);
    
    // Создаем выходную текстуру для маски, если она не назначена
    if (segmentationMaskTexture == null)
    {
        segmentationMaskTexture = new RenderTexture(OUTPUT_WIDTH, OUTPUT_HEIGHT, 0, RenderTextureFormat.ARGBFloat);
        segmentationMaskTexture.enableRandomWrite = true;
        segmentationMaskTexture.Create();
    }
}
```

3. **Обработка в Update()**:

```csharp
void Update()
{
    if (engine == null) return; // Guard if Start failed
    if (arCameraManager == null || !arCameraManager.TryAcquireLatestCpuImage(out XRCpuImage cpuImage))
    {
        return;
    }

    using (cpuImage)
    {
        // Конвертируем XRCpuImage в формат, совместимый с моделью
        var conversionParams = new XRCpuImage.ConversionParams
        {
            inputRect = new RectInt(0, 0, cpuImage.width, cpuImage.height),
            outputDimensions = new Vector2Int(INPUT_WIDTH, INPUT_HEIGHT),
            outputFormat = TextureFormat.RGBA32,
            transformation = XRCpuImage.Transformation.MirrorY
        };

        // Выделяем массив для данных изображения
        var rawTextureData = new byte[cpuImage.GetConvertedDataSize(conversionParams)];
        
        // Конвертируем изображение
        cpuImage.Convert(conversionParams, rawTextureData);
        
        // Загружаем данные в текстуру
        tempTexture.LoadRawTextureData(rawTextureData);
        tempTexture.Apply();

        // Преобразуем текстуру в тензор для входа модели
        // Для этого используем Sentis TensorFromTexture
        inputTensor = TextureConverter.ToTensor(tempTexture);
        
        // Требуется нормализация изображения (к диапазону [0,1] или [-1,1], в зависимости от модели)
        // Например, если нужна нормализация [0,1]:
        var normalizeTensor = new TensorFloat(inputTensor.shape);
        engine.Execute(new ScaleBias(inputTensor, 1f/255f, 0f), ref normalizeTensor);
        
        // Запускаем инференс
        engine.Schedule(normalizeTensor);
        
        // Получаем выходной тензор
        // ВАЖНО: Замените "logits" на имя выходного тензора вашей модели, если оно отличается
        Tensor<float> outputTensor = engine.PeekOutput("logits") as Tensor<float>;
        
        // Обрабатываем выходной тензор для получения маски сегментации стен
        ProcessSegmentationOutput(outputTensor);
    }
}
```

4. **Обработка выхода модели**:

```csharp
private void ProcessSegmentationOutput(Tensor<float> outputTensor)
{
    // Создаем тензор для хранения маски сегментации стен
    var wallMaskTensor = new TensorFloat(new TensorShape(1, 1, OUTPUT_HEIGHT, OUTPUT_WIDTH));
    
    // Используем ArgMax для определения класса с наибольшей вероятностью для каждого пикселя
    var argMaxTensor = new TensorInt(new TensorShape(1, OUTPUT_HEIGHT, OUTPUT_WIDTH));
    engine.Execute(new ArgMax(outputTensor, 1), ref argMaxTensor);
    
    // Создаем маску для стен, где 1 = стена, 0 = не стена
    // Это можно сделать с помощью Equal(argMaxTensor, wallClassIndex) и преобразовать в float
    var wallMaskInt = new TensorInt(new TensorShape(1, OUTPUT_HEIGHT, OUTPUT_WIDTH));
    engine.Execute(new Equal(argMaxTensor, wallClassIndex), ref wallMaskInt);
    
    // Преобразуем Int в Float
    engine.Execute(new Cast(wallMaskInt, DataType.Float), ref wallMaskTensor);
    
    // Преобразуем тензор в текстуру для визуализации или использования в шейдере
    wallMaskTensor.ToRenderTexture(segmentationMaskTexture);
}
```

## Примечания по интеграции

1. **Определение классов**:
   - Модель имеет 150 классов, необходимо определить индекс класса "стена" (или других нужных объектов).
   - Возможно потребуется создание таблицы соответствия индексов классов семантическим категориям.

2. **Оптимизация**:
   - Для повышения производительности можно использовать меньшее разрешение входного изображения.
   - Для ARCore/ARKit можно использовать GPU для преобразования и обработки изображений.

3. **Предобработка и постобработка**:
   - Проверьте требуемую нормализацию входных данных (диапазон [0,1] или [-1,1] и т.д.).
   - При необходимости примените сглаживание или фильтрацию к выходной маске.

4. **Интеграция с шейдером WallPaint**:
   - Используйте полученную маску сегментации как входной параметр для шейдера окраски стен.
   - Примените маску только к соответствующим областям с использованием умножения в шейдере. 