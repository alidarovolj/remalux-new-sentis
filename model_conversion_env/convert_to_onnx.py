from optimum.exporters.onnx import main_export
from pathlib import Path

def convert_model_to_onnx(model_id, output_path):
    """
    Конвертирует модель из Hugging Face Hub в ONNX формат.

    Args:
        model_id (str): Идентификатор модели на Hugging Face (например, 'leftattention/segformer-b4-wall').
        output_path (str): Путь для сохранения сконвертированной ONNX модели (включая имя файла .onnx).
    """
    print(f"Начало конвертации модели: {model_id}")
    print(f"Выходной путь для ONNX модели: {output_path}")

    try:
        # Определяем задачу. Для Segformer это 'semantic-segmentation'.
        # Для других моделей задача может отличаться (например, 'image-classification', 'object-detection').
        # Точное название задачи можно обычно найти в документации Transformers или Optimum для конкретного типа модели.
        task = "semantic-segmentation"
        print(f"Определена задача: {task}")

        # Убедимся, что директория для сохранения существует
        Path(output_path).parent.mkdir(parents=True, exist_ok=True)

        # Выполняем экспорт
        # main_export поддерживает множество аргументов для тонкой настройки.
        # Здесь мы используем базовый вариант.
        # framework="pt" означает PyTorch.
        # op_version можно уточнить, если требуются специфичные версии операторов ONNX (по умолчанию обычно подходит)
        main_export(
            model_name_or_path=model_id,
            output=Path(output_path),
            task=task,
            framework="pt", # Указываем, что исходная модель - PyTorch
            # Вы можете раскомментировать и указать opset, если это необходимо
            # opset=11, # или 12, 13, 14... в зависимости от требований Sentis и модели
            # Вы также можете указать input_shapes, если они известны и нужны для статических размеров
            # например, для Segformer: input_shapes={"pixel_values": [1, 3, 512, 512]}
            # но optimum часто может определить их автоматически.
        )
        print(f"Модель успешно сконвертирована и сохранена в: {output_path}")

    except Exception as e:
        print(f"Ошибка во время конвертации модели: {e}")
        print("---------------------------------------------------------------------")
        print("Возможные причины и решения:")
        print("- Убедитесь, что модель {model_id} существует и доступна на Hugging Face Hub.")
        print("- Проверьте, что задача '{task}' корректна для этой модели.")
        print("- Убедитесь, что все необходимые зависимости установлены (transformers, torch, optimum, onnx).")
        print("- Иногда для некоторых моделей требуются специфические версии библиотек или дополнительные параметры экспорта.")
        print("- Если ошибка связана с отсутствием 'config.json' или 'preprocessor_config.json', убедитесь, что модель полная.")
        print("- Попробуйте указать конкретный opset (например, --opset 11 или opset=11 в скрипте).")
        print("- Для моделей с динамическими размерами входа/выхода могут потребоваться дополнительные флаги.")
        print("---------------------------------------------------------------------")

if __name__ == "__main__":
    MODEL_ID = "leftattention/segformer-b4-wall"
    ONNX_OUTPUT_PATH = "onnx_models/segformer-b4-wall/model.onnx" # Сохраняем в подпапку

    # Создаем директорию для ONNX моделей, если ее нет
    Path("onnx_models/segformer-b4-wall").mkdir(parents=True, exist_ok=True)

    convert_model_to_onnx(MODEL_ID, ONNX_OUTPUT_PATH) 