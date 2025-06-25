# 🎨 Настройка UI для Dulux Visualizer 

## 📋 Быстрая настройка

### 1. Создание Canvas
1. В Hierarchy: `Right Click` → `UI` → `Canvas`
2. На Canvas добавьте `Canvas Scaler` компонент:
   - **UI Scale Mode**: `Scale With Screen Size`
   - **Reference Resolution**: `1920x1080`

### 2. Создание ColorPicker UI
1. Создайте пустой GameObject в Canvas: `ColorPickerUI`
2. Добавьте компонент `ColorPickerUI` script
3. Создайте структуру UI:

```
Canvas
└── ColorPickerUI
    ├── ColorPickerPanel (Panel)
    │   ├── Title (Text "Выбор цвета")
    │   ├── ColorButtonsContainer (Horizontal Layout Group)
    │   ├── SelectedColorText (Text "Выбран: Белый")
    │   ├── ApplyButton (Button "Применить")
    │   └── ResetButton (Button "Сброс")
    └── ToggleButton (Button "🎨 Цвета")
```

### 3. Настройка компонентов

#### ColorPickerUI настройки:
- **Color Picker Panel**: `ColorPickerPanel`
- **Toggle Color Picker Button**: `ToggleButton`
- **Apply Color Button**: `ApplyButton`
- **Reset Color Button**: `ResetButton`
- **Selected Color Text**: `SelectedColorText`
- **Color Buttons Container**: `ColorButtonsContainer`
- **Color Button Prefab**: `Assets/Prefabs/UI/SimpleColorButton.prefab`

#### ColorButtonsContainer настройки:
- Добавьте `Horizontal Layout Group`:
  - **Spacing**: `10`
  - **Child Alignment**: `Middle Center`
  - **Child Force Expand Width**: `false`
  - **Child Force Expand Height**: `false`

### 4. Настройка WallPaintingSystem
1. Создайте пустой GameObject: `WallPaintingSystem`
2. Добавьте компонент `WallPaintingSystem` script
3. В настройках:
   - **AR Manager**: перетащите ARManagerInitializer2 из сцены
   - **Color Picker UI**: перетащите ColorPickerUI из сцены

### 5. Позиционирование UI
- **ToggleButton**: разместите в правом нижнем углу
- **ColorPickerPanel**: изначально отключен, появляется по кнопке

## 🎯 Быстрая проверка

1. Запустите приложение
2. Нажмите кнопку "🎨 Цвета"
3. Выберите любой цвет
4. Нажмите "Применить"
5. Стены должны покраситься выбранным цветом!

## ⚡ Автоматическая настройка

Если хотите автоматически создать UI, добавьте этот код в empty GameObject:

```csharp
[ContextMenu("Create Dulux UI")]
public void CreateDuluxUI()
{
    // Создание Canvas
    GameObject canvas = new GameObject("DuluxCanvas");
    canvas.AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
    canvas.AddComponent<CanvasScaler>();
    canvas.AddComponent<GraphicRaycaster>();
    
    // Создание ColorPicker UI
    GameObject colorPickerUI = new GameObject("ColorPickerUI");
    colorPickerUI.transform.SetParent(canvas.transform);
    colorPickerUI.AddComponent<ColorPickerUI>();
    
    Debug.Log("✅ Dulux UI создан автоматически!");
}
```

## 🐛 Решение проблем

### EventSystem отсутствует
- Unity автоматически создаст EventSystem при создании первого UI элемента
- Или создайте вручную: `GameObject` → `UI` → `Event System`

### Кнопки не работают
- Проверьте что есть `GraphicRaycaster` на Canvas
- Проверьте что есть `EventSystem` в сцене
- Убедитесь что кнопки имеют `Raycast Target = true`

### Цвета не применяются
- Проверьте что `ARManagerInitializer2` назначен в `WallPaintingSystem`
- Убедитесь что есть сгенерированные плоскости стен
- Проверьте консоль на ошибки

## 📱 Мобильная оптимизация

Для мобильных устройств:
- Увеличьте размер кнопок (минимум 60x60 пикселей)
- Добавьте больше отступов между элементами
- Используйте крупные шрифты (минимум 16pt)

## 🎨 Кастомизация цветов

В `ColorPickerUI.cs` измените массив `colorPresets`:

```csharp
private ColorPreset[] colorPresets = new ColorPreset[]
{
    new ColorPreset("Мой цвет", new Color(0.5f, 0.8f, 0.2f)),
    // ... добавьте свои цвета
};
``` 