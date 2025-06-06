# Unity Sentis AR Integration

This project provides a robust integration of Unity Sentis machine learning framework with AR Foundation for wall segmentation and virtual painting applications.

## Requirements

- Unity 2022.3 or newer
- Unity Sentis 2.1.2 or newer
- AR Foundation 5.0 or newer
- Universal Render Pipeline (URP)

## Installation

1. **Install Unity Packages**:
   - Package Manager → Add package by name → Add these packages:
     - `com.unity.sentis` (version 2.1.2 or newer)
     - `com.unity.xr.arfoundation` (version 5.0 or newer)
     - `com.unity.render-pipelines.universal` (if not already installed)

2. **Setup Project**:
   - Make sure Universal Render Pipeline is configured in Project Settings → Graphics
   - Configure AR settings in Project Settings → XR Plug-in Management

3. **Import ML Models**:
   - Place your ONNX segmentation models in the `Assets/Resources` folder

## Key Components

### SentisInitializer

This component initializes the Sentis framework at runtime to prevent crashes:

```csharp
// Add to a persistent game object in your scene
var sentisInitializer = gameObject.AddComponent<SentisInitializer>();
sentisInitializer.initializeOnStart = true;
sentisInitializer.useAsyncInitialization = true;
```

### SentisCompat

Provides compatibility layer for Sentis API:

```csharp
// Load a model through the compatibility layer
var model = SentisCompat.LoadModel(modelAsset);

// Create a worker
var worker = SentisCompat.CreateWorker(model, 0); // 0 = CPU backend

// Convert textures to tensors
var tensor = SentisCompat.TextureToTensor(cameraTexture);

// Render tensor to texture
SentisCompat.RenderTensorToTexture(outputTensor, renderTexture);
```

### WallSegmentationModelLoader

Safely loads segmentation models without crashing Unity:

```csharp
// Add to the same GameObject as WallSegmentation
var loader = gameObject.AddComponent<WallSegmentationModelLoader>();
loader.modelAsset = Resources.Load<UnityEngine.Object>("segmentation_model.onnx");
loader.loadOnStart = true;
loader.preferredBackend = 0; // CPU
```

### WallPaintFeature

URP render feature that applies segmentation masks to the camera view:

```csharp
// Added to your URP Renderer through the inspector
// See Assets/Settings/URP-*-Renderer.asset
```

## Troubleshooting

### Common Issues

1. **Unity crashes when loading ML models**
   - Make sure SentisInitializer is added to the scene and initialized before model loading
   - Use WallSegmentationModelLoader instead of directly assigning models to components

2. **Black screen after segmentation mask is applied**
   - Check that WallPaintFeature is using a material with proper transparency settings
   - Make sure the segmentation mask texture is valid and not completely black
   - The `minTransparency` setting in WallPaintFeature prevents fully opaque rendering

3. **Texture to tensor conversion fails**
   - Verify that your texture format is compatible (RGBA32, RGB24, R8)
   - SentisCompat provides fallback methods for texture conversion

4. **"WorkerFactory type not found" error**
   - This indicates Sentis 2.1.2 is used, which replaced WorkerFactory with Worker class
   - Make sure to use SentisCompat for creating workers instead of direct API calls

### Diagnostics

Use the following code to run diagnostics on Sentis installation:

```csharp
// Run Sentis diagnostics
SentisCompat.DiagnosticReport();

// Test model loading
var sentisInit = FindObjectOfType<SentisInitializer>();
StartCoroutine(sentisInit.TestSentisModel());
```

## Performance Considerations

- CPU backend (0) is more stable but slower than GPU Compute (1)
- For mobile devices, consider using smaller models or reduce input resolution
- Set Camera.targetTexture resolution appropriately to balance quality and performance

## License

This project is licensed under the MIT License - see the LICENSE file for details.

# Оптимизация моделей ONNX для Unity Sentis 2.1.x

В этом проекте реализован набор скриптов для подготовки и оптимизации моделей ONNX для использования с Unity Sentis 2.1.x.

## Основные проблемы и их решения

При использовании моделей из внешних источников (HuggingFace, PyTorch и др.) с Unity Sentis могут возникать следующие проблемы:

1. **Несовместимость операторов**: Unity Sentis поддерживает ограниченный набор операторов ONNX.
2. **Проблемы с форматом**: Ожидается, что модель была экспортирована с Sentis 1.4+.
3. **Атрибуты операторов**: Некоторые операторы (например Unsqueeze в ONNX 13) имеют иную структуру, чем ожидает Unity.
4. **Порядок узлов**: Unity требует строгого топологического порядка узлов в графе.

## Скрипты для оптимизации

### 1. Упрощение модели (simplify_model.py)

Упрощает модель, удаляя избыточные операции и оптимизируя граф:

```shell
python3 simplify_model.py
```

### 2. Анализ совместимости (analyze_model_compatibility.py)

Проверяет совместимость операторов модели с Unity Sentis:

```shell
python3 analyze_model_compatibility.py
```

### 3. Преобразование в совместимый формат (convert_to_sentis_format.py)

Добавляет метаданные и обновляет параметры для совместимости с Sentis:

```shell
python3 convert_to_sentis_format.py
```

### 4. Исправление операторов Unsqueeze (fix_unsqueeze_operators.py)

Адаптирует операторы Unsqueeze для ONNX 13:

```shell
python3 fix_unsqueeze_operators.py
```

### 5. Исправление топологического порядка (fix_topological_order.py)

Сортирует узлы в правильном порядке:

```shell
python3 fix_topological_order.py
```

### 6. Финальная подготовка (final_preparation.py)

Исправляет последние ошибки и создает итоговую модель:

```shell
python3 final_preparation.py
```

## Использование в Unity

1. Скопируйте оптимизированную модель `model_unity_final.onnx` в папку Assets/StreamingAssets вашего проекта.
2. Переименуйте её в `model.onnx` (скрипт final_preparation.py делает это автоматически).
3. В Unity, выберите модель в Project Browser, и нажмите кнопку "Serialize To StreamingAssets" в Inspector.
4. Убедитесь, что ваш код загружает модель из правильного пути.

## Требования

- Python 3.6+
- ONNX: `pip install onnx`
- ONNX Simplifier: `pip install onnxsim`
- NetworkX: `pip install networkx`

## Возможные проблемы

Если после всех оптимизаций модель всё равно не загружается:

1. Проверьте логи Unity для выявления конкретных ошибок
2. Убедитесь, что размерность и формат входных/выходных тензоров соответствует ожидаемым в вашем коде
3. Попробуйте использовать другую модель или конвертировать её с использованием Unity Model Optimizer

## Примечания

- Скрипты создают различные промежуточные версии модели. Финальная версия - `model_unity_final.onnx`
- Для использования скриптов вам нужны права на запись в папку StreamingAssets
