# Dialog System для AR приложения

## Обзор
Dialog System представляет собой набор компонентов для отображения диалоговых окон и уведомлений в AR приложении. Система предназначена для информирования пользователя о статусе загрузки ML моделей и возможных ошибках.

## Компоненты
- **DialogManager**: Основной компонент для управления диалоговыми окнами
- **ErrorInfo**: Класс с информацией об ошибке загрузки ML модели

## Установка

### Вариант 1: Автоматическая установка
1. Добавьте GameObject с компонентом `DialogManager` в вашу сцену
2. Компонент автоматически создаст необходимые UI элементы для диалогового окна

### Вариант 2: Через префабы
1. Создайте префаб диалогового окна с использованием Unity UI
2. Перетащите этот префаб в поле `Dialog Prefab` компонента `DialogManager`

## Использование

### Показ ошибки загрузки модели
```csharp
// Получение доступа к DialogManager
DialogManager dialogManager = DialogManager.instance;

// Показ ошибки
if (dialogManager != null)
{
    dialogManager.ShowModelLoadError("MyModel", "Не удалось загрузить модель из-за ошибки формата");
}
```

### Показ произвольного сообщения
```csharp
DialogManager.instance?.ShowErrorDialog("Заголовок", "Текст сообщения для пользователя");
```

### Скрытие диалога
```csharp
DialogManager.instance?.HideDialog();
```

## Интеграция с AR Session Manager

Для работы с AR Session Manager добавьте компонент `DialogManager` на тот же GameObject, что и `ARSessionManager`, или создайте отдельный GameObject с компонентом `DialogManager` в вашей сцене. 