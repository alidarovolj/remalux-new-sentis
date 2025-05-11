#!/usr/bin/env python3
import os
import onnx
from onnx import helper
import numpy as np
from onnx import numpy_helper

# Путь к модели и выходному файлу
INPUT_MODEL = os.path.join('Assets', 'StreamingAssets', 'model_sentis_compatible.onnx')
OUTPUT_MODEL = os.path.join('Assets', 'StreamingAssets', 'model_final.onnx')

print(f"Загружаю модель из {INPUT_MODEL}...")
try:
    # Загружаем модель
    model = onnx.load(INPUT_MODEL)
    print(f"Модель успешно загружена: {model.graph.name}")
    
    # Проверяем версию OpSet
    opset_version = 0
    for opset in model.opset_import:
        if opset.domain == "" or opset.domain == "ai.onnx":
            opset_version = opset.version
            break
    
    print(f"Версия ONNX OpSet: {opset_version}")
    
    # Фиксируем Unsqueeze операторы для соответствия ONNX 13
    if opset_version >= 13:
        print("Обнаружена OpSet версия 13+. Исправляем Unsqueeze операторы...")
        
        # Создаем счетчик для уникальных имен
        counter = 0
        
        # Проходим по всем узлам и фиксируем Unsqueeze
        nodes_to_remove = []
        nodes_to_add = []
        
        for node in model.graph.node:
            if node.op_type == 'Unsqueeze':
                if len(node.input) < 2:  # Нужно исправить
                    print(f"Исправляем Unsqueeze узел: {node.name}")
                    
                    # В ONNX 13+ axis должен быть подан как отдельный инпут
                    # Создаем константу для этого
                    counter += 1
                    axes_name = f"_axes_{counter}"
                    
                    # Пытаемся получить атрибуты axes из узла
                    axes = []
                    for attr in node.attribute:
                        if attr.name == 'axes':
                            if attr.type == 7:  # INTS
                                axes = list(attr.ints)
                            break
                    
                    # Создаем новый тензор для axes
                    axes_tensor = numpy_helper.from_array(
                        np.array(axes, dtype=np.int64),
                        name=axes_name
                    )
                    
                    # Добавляем тензор в граф
                    model.graph.initializer.append(axes_tensor)
                    
                    # Создаем новый узел с двумя входами
                    new_inputs = [node.input[0], axes_name]
                    new_node = helper.make_node(
                        'Unsqueeze',
                        inputs=new_inputs,
                        outputs=node.output,
                        name=f"{node.name}_fixed"
                    )
                    
                    # Добавляем новый узел и помечаем старый для удаления
                    nodes_to_add.append(new_node)
                    nodes_to_remove.append(node)
        
        # Удаляем старые узлы
        for node in nodes_to_remove:
            model.graph.node.remove(node)
        
        # Добавляем новые узлы
        model.graph.node.extend(nodes_to_add)
        
        print(f"Исправлено {len(nodes_to_remove)} Unsqueeze операторов")
    
    # Сохраняем исправленную модель
    onnx.save(model, OUTPUT_MODEL)
    print(f"Исправленная модель сохранена в {OUTPUT_MODEL}")
    
    # Проверяем модель на ошибки
    print("Проверяем модель на ошибки...")
    try:
        onnx.checker.check_model(model)
        print("✅ Проверка успешна! Модель соответствует спецификации ONNX.")
    except Exception as check_error:
        print(f"⚠️ ПРЕДУПРЕЖДЕНИЕ: Проверка модели выявила проблемы: {check_error}")
    
except Exception as e:
    print(f"Ошибка: {e}") 