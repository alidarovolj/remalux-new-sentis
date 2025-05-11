#!/bin/bash

# Скрипт для автоматизации процесса оптимизации ONNX моделей для Unity Sentis 2.1.x
# Проходит через все этапы оптимизации последовательно

# Проверяем зависимости
echo "=== Проверка и установка зависимостей ==="
python3 -m pip install onnx onnxsim networkx

# Выполняем все этапы оптимизации последовательно
echo -e "\n=== 1. Упрощение модели ==="
python3 simplify_model.py

echo -e "\n=== 2. Анализ совместимости ==="
python3 analyze_model_compatibility.py

echo -e "\n=== 3. Преобразование в совместимый формат ==="
python3 convert_to_sentis_format.py

echo -e "\n=== 4. Исправление операторов Unsqueeze ==="
python3 fix_unsqueeze_operators.py

echo -e "\n=== 5. Исправление топологического порядка ==="
python3 fix_topological_order.py

echo -e "\n=== 6. Финальная подготовка ==="
python3 final_preparation.py

echo -e "\n=== Оптимизация завершена! ==="
echo "Модель готова к использованию в Unity Sentis. Не забудьте выполнить 'Serialize To StreamingAssets' в Unity." 