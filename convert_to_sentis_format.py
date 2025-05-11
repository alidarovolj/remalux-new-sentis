#!/usr/bin/env python3
import os
import onnx
import numpy as np
from onnx import numpy_helper
from onnx import helper

# Путь к упрощенной модели и выходному файлу
INPUT_MODEL = os.path.join('Assets', 'StreamingAssets', 'model_simplified.onnx')
OUTPUT_MODEL = os.path.join('Assets', 'StreamingAssets', 'model_sentis_compatible.onnx')

print(f"Загружаю модель из {INPUT_MODEL}...")
try:
    # Загружаем упрощенную модель
    model = onnx.load(INPUT_MODEL)
    print(f"Модель успешно загружена: {model.graph.name}")
    
    # 1. Добавляем метаданные для Sentis
    model.producer_name = "Unity Sentis Exporter"
    model.producer_version = "1.0"
    model.domain = "ai.onnx"
    
    # 2. Проверяем и обновляем версию IR
    if model.ir_version < 7:
        print(f"Повышаем версию IR с {model.ir_version} до 7")
        model.ir_version = 7
    
    # 3. Проверяем и обновляем версию opset
    has_onnx_domain = False
    for opset in model.opset_import:
        if opset.domain == "" or opset.domain == "ai.onnx":
            has_onnx_domain = True
            if opset.version < 13:
                print(f"Повышаем версию opset с {opset.version} до 13")
                opset.version = 13
    
    if not has_onnx_domain:
        print("Добавляем opset для ai.onnx")
        model.opset_import.extend([helper.make_opsetid("ai.onnx", 13)])
    
    # 4. Преобразуем float16 тензоры в float32, если они есть
    for initializer in model.graph.initializer:
        if initializer.data_type == 10:  # FLOAT16
            print(f"Преобразуем инициализатор {initializer.name} из float16 в float32")
            tensor = numpy_helper.to_array(initializer)
            tensor = tensor.astype(np.float32)
            new_tensor = numpy_helper.from_array(tensor, initializer.name)
            initializer.CopyFrom(new_tensor)
    
    # 5. Проверяем и обновляем, чтобы все имена были уникальными
    unique_names = set()
    name_map = {}
    
    def ensure_unique_name(name):
        if name in unique_names:
            counter = 1
            new_name = f"{name}_{counter}"
            while new_name in unique_names:
                counter += 1
                new_name = f"{name}_{counter}"
            name_map[name] = new_name
            return new_name
        unique_names.add(name)
        return name
    
    # Проверяем имена узлов
    for node in model.graph.node:
        node.name = ensure_unique_name(node.name if node.name else f"node_{len(unique_names)}")
        
        # Обновляем имена выходов узла
        for i, output in enumerate(node.output):
            if output in name_map:
                node.output[i] = name_map[output]
    
    # 6. Проверка входов и выходов модели
    if not model.graph.input:
        print("ПРЕДУПРЕЖДЕНИЕ: Граф не имеет входов")
    
    if not model.graph.output:
        print("ПРЕДУПРЕЖДЕНИЕ: Граф не имеет выходов")
    
    # 7. Добавляем docstring
    model.doc_string = "ONNX model optimized for Unity Sentis 2.1.x"
    
    # Сохраняем обработанную модель
    onnx.save(model, OUTPUT_MODEL)
    print(f"Модель, совместимая с Unity Sentis, сохранена в {OUTPUT_MODEL}")
    
    # Проверяем модель на ошибки
    print("Проверяем модель на ошибки...")
    try:
        onnx.checker.check_model(model)
        print("Проверка успешна! Модель соответствует спецификации ONNX.")
    except Exception as check_error:
        print(f"ПРЕДУПРЕЖДЕНИЕ: Проверка модели выявила проблемы: {check_error}")
    
except Exception as e:
    print(f"Ошибка: {e}") 