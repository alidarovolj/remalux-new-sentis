# 🎮 Быстрая Настройка Unity Inspector

## После исправлений компиляции требуется настроить компоненты:

### 1. 🔧 ARManagerInitializer2 (DontDestroyOnLoad)

В Inspector найдите **новую секцию "🎯 СТАБИЛИЗАЦИЯ ПОЗИЦИОНИРОВАНИЯ"**:

```
✅ Enable Position Smoothing: true
   Position Smoothing Factor: 0.3
   Orientation Smoothing Factor: 0.5  
   Stable Frames Required: 3
```

**Эффект**: Устраняет дрожание плоскостей, делает позиционирование плавным.

### 2. 📊 Добавить PlaneOrientationDebugger (НАСТОЯТЕЛЬНО РЕКОМЕНДУЕТСЯ)

**Шаги**:
1. `GameObject → Create Empty`
2. Назвать: `PlaneOrientationDebugger`
3. `Add Component → PlaneOrientationDebugger`
4. В Inspector настроить:

```
📍 Настройки отладки:
✅ Enable Normal Visualization: true
✅ Enable Boundary Visualization: true  
✅ Enable Mesh Orientation Check: true
   Normal Length: 0.3
   Wall Normal Color: Red
   Floor Normal Color: Green
   Boundary Color: Yellow

📍 Фильтрация:
   Show Only Vertical Planes: false
   Min Plane Area: 0.1

📍 Компоненты:
   AR Manager: [перетащить AR Manager Initializer 2]
```

**Эффект**: Визуализация нормалей плоскостей, проверка правильности ориентации.

### 3. ✅ Проверить ARPlaneConfigurator 

Ваши настройки уже корректны, но убедитесь:

```
✅ Reduce Plane Flickering: true  
✅ Min Plane Area To Display: 0.1 (будет автоматически 0.25м²)
✅ Show Debug Info: true
```

### 4. 🎯 Проверить Связи Компонентов

**ARManagerInitializer2** должен иметь ссылку на:
- ✅ Plane Configurator: PlaneConfiguratorManager
- ✅ Plane Manager: уже настроено
- ✅ XR Origin: уже настроено

## 🚀 После Настройки

Запустите сцену - вы должны увидеть:
- ✅ Менее дрожащие плоскости  
- ✅ Фильтрацию мелких областей
- ✅ Красные стрелки нормалей на стенах (если добавили PlaneOrientationDebugger)
- ✅ Улучшенную стабильность позиционирования

## 🔍 Диагностика

В Console должны появиться сообщения:
```
[ARPlaneConfigurator] Применены улучшенные фильтры - мин.площадь: 0.25м²
[ARManagerInitializer2] 🔄 Сглаживание позиции применено
[PlaneOrientationDebugger] ✅ ПРАВИЛЬНО ориентированная плоскость
``` 