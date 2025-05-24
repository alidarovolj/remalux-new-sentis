# Инструкция по сборке iOS проекта Remalux

## 🎯 Основные проблемы и их решения

### Проблема 1: ArgumentNullException с шейдером
**Симптом:** `ArgumentNullException: Value cannot be null. Parameter name: shader`
**Причина:** Шейдер `Universal Render Pipeline/Lit` не найден на iOS
**Решение:** ✅ **ИСПРАВЛЕНО** - добавлена безопасная система поиска шейдеров с резервными вариантами

### Проблема 2: Модель ML инициализируется как Placeholder
**Симптом:** `[WallSegmentation] Model Initialized (Placeholder)`
**Причина:** Модель не загружается из StreamingAssets
**Решение:** ✅ **ИСПРАВЛЕНО** - добавлена настоящая инициализация ML модели

### Проблема 3: Сборка Unity завершается с killed
**Симптом:** `Message from debugger: killed`
**Причина:** Недостаток памяти или критическая ошибка
**Решение:** ✅ **ДОБАВЛЕНО** - система диагностики и мониторинга памяти

## 🔧 Предварительная настройка

### 1. Проверка настроек проекта Unity

1. **File → Build Settings**
   - Platform: iOS
   - Architecture: ARM64

2. **Edit → Project Settings → Player → iOS Settings**
   - **Target minimum iOS Version:** 13.0 или выше
   - **Architecture:** ARM64
   - **Graphics APIs:** 
     - ✅ Metal (первый в списке)
     - ❌ Убрать OpenGLES если есть
   - **Scripting Backend:** IL2CPP
   - **Api Compatibility Level:** .NET Standard 2.1

3. **Edit → Project Settings → XR Plug-in Management**
   - ✅ ARKit Provider (для iOS)

### 2. Проверка зависимостей

**Обязательные пакеты через Package Manager:**
- AR Foundation (4.2+ рекомендуется)
- ARKit XR Plugin
- Unity Sentis (для ML)
- Universal Render Pipeline

## 🛠️ Процесс сборки

### Шаг 1: Добавить компонент диагностики
1. Открыть сцену `Assets/Scenes/AR_WallPainting.unity`
2. Создать пустой GameObject с именем "iOS Diagnostics"
3. Добавить компонент `iOSDiagnostics`
4. Включить настройки:
   - ✅ Run On Start
   - Diagnostic Interval: 10 секунд

### Шаг 2: Проверить StreamingAssets
1. Убедиться что файл `Assets/StreamingAssets/segformer-model.sentis` существует (14MB)
2. Если нет - скопировать из резервной копии

### Шаг 3: Build & Run
1. **File → Build Settings**
2. Выбрать сцену `AR_WallPainting`
3. Нажать **Build And Run**
4. Выбрать папку для Xcode проекта
5. Дождаться завершения сборки Unity

### Шаг 4: Настройка Xcode
1. Открыть созданный `.xcodeproj` файл
2. **Signing & Capabilities:**
   - Добавить Team
   - Включить Automatic Signing
3. **Info.plist добавить разрешения:**
   ```xml
   <key>NSCameraUsageDescription</key>
   <string>Приложение использует камеру для AR функций</string>
   ```
4. **Build Settings:**
   - Deployment Target: iOS 13.0+
   - Architecture: arm64

### Шаг 5: Запуск и диагностика
1. Подключить iPhone/iPad с iOS 13+
2. Запустить из Xcode
3. **Следить за логами:**
   - Откроется диагностика `=== 🩺 ЗАПУСК iOS ДИАГНОСТИКИ ===`
   - Проверить что все компоненты найдены ✅
   - Убедиться что ML модель загружается

## 🩺 Диагностика проблем

### Ожидаемые логи при успешном запуске:
```
=== 🩺 ЗАПУСК iOS ДИАГНОСТИКИ ===
📱 ПРОВЕРКА ПЛАТФОРМЫ:
  • Платформа: IPhonePlayer
  • iOS устройство: True
🎨 ПРОВЕРКА ШЕЙДЕРОВ:
  ✅ Custom/WallPaint: Найден
🤖 ПРОВЕРКА ML МОДЕЛЕЙ:
  ✅ segformer-model.sentis: Найден, размер 14MB
📡 ПРОВЕРКА AR СИСТЕМЫ:
  ✅ ARSession: Найден
  ✅ ARCameraManager: Найден
🧩 ПРОВЕРКА КОМПОНЕНТОВ СЦЕНЫ:
  ✅ WallSegmentation найден
    • Модель инициализирована: True
[WallSegmentation] 🚀 Начинаем инициализацию ML модели...
[WallSegmentation] ✅ ML модель успешно инициализирована!
```

### Возможные проблемы и решения:

#### ❌ Шейдер Custom/WallPaint не найден
**Решение:** Включен резервный механизм, будет использован `Universal Render Pipeline/Unlit`

#### ❌ segformer-model.sentis не найден
**Решение:** 
1. Проверить размер файла в StreamingAssets (должен быть ~14MB)
2. Пересобрать проект с включенным StreamingAssets

#### ❌ ML модель не инициализируется
**Проверить логи:**
- `[WallSegmentation] ❌ Ошибка инициализации модели: ...`
- Возможно нужно переключиться на CPU backend (selectedBackend = 0)

#### ❌ AR Session не запускается
**Решение:**
1. Проверить разрешения камеры в Settings iOS
2. Убедиться что устройство поддерживает ARKit

## 🎯 Финальная проверка

### Признаки успешной работы:
1. ✅ Диагностика проходит без ошибок
2. ✅ AR камера показывает реальность
3. ✅ ML модель инициализирована
4. ✅ Обнаруживаются вертикальные плоскости
5. ✅ Работает покраска стен

### Команды Context Menu для тестирования:
- В `iOSDiagnostics`: "Запустить диагностику", "Тест создания материала"
- В `WallSegmentation`: "Test Segmentation", "Validate Performance"

## 🚀 Оптимизация производительности

### Рекомендуемые настройки для iOS:
1. **WallSegmentation:**
   - Target Processing Time: 50ms
   - Max Segmentation FPS: 15
   - Adaptive Resolution: ✅ Enabled
   - Preferred Backend: CPU (более стабильно)

2. **Quality Settings:**
   - Texture Quality: High
   - Anti Aliasing: Disabled or 2x MSAA
   - Realtime Reflection Probes: Disabled

3. **Player Settings:**
   - Scripting Define Symbols: Добавить `SENTIS_DEBUG` для отладки

## ⚡ Быстрые команды для отладки

### В Xcode консоли искать:
- `ArgumentNullException` - проблемы с материалами
- `[WallSegmentation] ❌` - проблемы с ML
- `=== 🩺` - вывод диагностики

### Принудительная перезагрузка ML модели:
```csharp
// В Unity Console во время выполнения
var ws = FindObjectOfType<WallSegmentation>();
ws.SetFixedResolution(320, 320); // Уменьшить разрешение для отладки
```

---

## 📞 Контакты при проблемах
При возникновении проблем сохранить полный лог из Xcode Console и приложить диагностический вывод `iOSDiagnostics`. 