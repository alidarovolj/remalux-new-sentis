# ✅ Контрольный список настройки AR Wall Painter

## ⚡ САМЫЙ БЫСТРЫЙ СПОСОБ

**Используйте автоматическую настройку:**
```
Unity Editor → AR Tools → Quick Setup Wall Painter
```
Это автоматически создаст префаб, материал и настроит всё за вас!

---

## 🔴 КРИТИЧЕСКИЕ ПРОБЛЕМЫ (исправить обязательно!)

### 1. WallPainterController - отсутствуют важные настройки
**Компонент:** `XR Origin (AR Rig) > WallPainterController`

**Что сделать:**
```
Wall Plane Prefab: [СОЗДАТЬ И НАЗНАЧИТЬ]
Wall Paint Material: [СОЗДАТЬ И НАЗНАЧИТЬ]
```

**Как исправить:**
1. Создайте префаб плоскости:
   - GameObject > Create Empty
   - Добавьте MeshFilter, MeshRenderer, MeshCollider
   - Сохраните как префаб в `Assets/Prefabs/WallPlanePrefab.prefab`
   - Назначьте в поле Wall Plane Prefab

2. Создайте материал:
   - Assets > Create > Material
   - Назовите "WallPaintMaterial"
   - Shader: Universal Render Pipeline/Lit
   - Base Color: белый или светло-серый
   - Назначьте в поле Wall Paint Material

### 2. Конфликт систем генерации плоскостей
**Проблема:** Одновременно активны старая (ARManagerInitializer2) и новая (WallPainterController) системы

**Что сделать:**
```
AR Manager Initializer 2: [ОТКЛЮЧИТЬ]
```

**Как исправить:**
- Снимите галочку с компонента `ARManagerInitializer2` на объекте `AR Manager Initializer 2`
- ИЛИ отключите весь GameObject

## 🟡 ВАЖНЫЕ НАСТРОЙКИ (рекомендуется)

### 3. WallPaintingSystem - пустые материалы
**Компонент:** `WallPaintingSystem`

**Что сделать:**
```
Base Paint Material: [НАЗНАЧИТЬ WallPaintMaterial]
Transparent Paint Material: [СОЗДАТЬ И НАЗНАЧИТЬ]
```

**Как исправить:**
- Base Paint Material: используйте тот же WallPaintMaterial
- Transparent Paint Material: создайте копию с прозрачностью

### 4. Включить режим отладки для тестирования
**Компонент:** `WallPainterController`

**Что сделать:**
```
Debug Mode: ✅
Show Contour Visualization: ✅
```

## 🟢 ОПТИМИЗАЦИЯ (опционально)

### 5. Отключить лишние отладочные компоненты
**Что отключить:**
- `PlaneDebugVisualizer` (на GeometrySet)
- `PlaneOrientationDebugger` (если не нужна визуализация нормалей)
- `SegmentationDebugger` (после успешного тестирования)

### 6. Настроить параметры WallPainterController
**Рекомендуемые значения:**
```
Segmentation Confidence: 0.75 ✅ (уже настроено)
Max Raycast Distance: 5 ✅ (уже настроено)
Min Contour Area: 1000 ✅ (уже настроено)
Plane Size Multiplier: 1.2 (увеличить с 1 до 1.2)
Contour Simplification: 2 ✅ (уже настроено)
```

## 📋 ИТОГОВЫЙ ЧЕКЛИСТ

- [ ] **Создать префаб WallPlanePrefab**
- [ ] **Создать материал WallPaintMaterial**
- [ ] **Назначить префаб и материал в WallPainterController**
- [ ] **Отключить ARManagerInitializer2**
- [ ] **Настроить материалы в WallPaintingSystem**
- [ ] **Включить Debug Mode для тестирования**
- [ ] **Увеличить Plane Size Multiplier до 1.2**
- [ ] **Отключить лишние отладочные компоненты**

## 🚀 БЫСТРАЯ НАСТРОЙКА

### Вариант 1: Автоматически (РЕКОМЕНДУЕТСЯ)
```
Unity Editor → AR Tools → Quick Setup Wall Painter
```

### Вариант 2: Отдельные компоненты
```
Unity Editor → AR Tools → Create Wall Plane Prefab
Unity Editor → AR Tools → Create Wall Paint Material
```

### Вариант 3: Вручную
**Создание префаба:**
```
1. GameObject > Create Empty
2. Rename to "WallPlanePrefab"
3. Add Component > Mesh Filter
4. Add Component > Mesh Renderer
5. Add Component > Mesh Collider
6. Drag to Project > Save as Prefab
```

**Создание материала:**
```
1. Assets > Create > Material
2. Name: "WallPaintMaterial"
3. Shader: Universal Render Pipeline/Lit
4. Base Map > Color: (0.9, 0.9, 0.9, 1.0)
5. Smoothness: 0.3
```

## ⚠️ ВАЖНЫЕ ЗАМЕЧАНИЯ

1. **Wall Segmentation** настроен правильно ✅
2. **XR Origin** настроен правильно ✅
3. **CustomARSessionController** настроен правильно ✅

## 🔍 ПРОВЕРКА ПОСЛЕ НАСТРОЙКИ

1. Запустите сцену
2. В консоли должны появиться логи:
   ```
   [WallPainterController] Инициализация...
   [WallPainterController] Маска сегментации обновлена: 256x256
   ```
3. При касании стены должна создаваться полноразмерная плоскость
4. В режиме Debug Mode должен быть виден зеленый контур

---

**После выполнения всех пунктов система должна работать корректно!** 