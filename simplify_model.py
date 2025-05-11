#!/usr/bin/env python3
import os
import onnx
from onnxsim import simplify

# Пути к файлам
INPUT_MODEL = os.path.join('Assets', 'StreamingAssets', 'model.onnx')
OUTPUT_MODEL = os.path.join('Assets', 'StreamingAssets', 'model_simplified.onnx')

print(f"Загружаю модель из {INPUT_MODEL}...")
try:
    # Загружаем оригинальную модель
    model = onnx.load(INPUT_MODEL)
    print(f"Модель успешно загружена: {model.graph.name}")
    
    # Вывод информации о входах и выходах модели
    print("\nВходные тензоры:")
    for inp in model.graph.input:
        print(f"  - {inp.name}: {inp.type.tensor_type.elem_type}")
    
    print("\nВыходные тензоры:")
    for out in model.graph.output:
        print(f"  - {out.name}: {out.type.tensor_type.elem_type}")
    
    # Подсчет операторов в модели
    ops = {}
    for node in model.graph.node:
        op_type = node.op_type
        ops[op_type] = ops.get(op_type, 0) + 1
    
    print("\nОператоры в модели:")
    for op, count in ops.items():
        print(f"  - {op}: {count}")
    
    # Упрощаем модель
    print("\nУпрощаю модель...")
    simplified_model, check = simplify(model)
    
    if check:
        print("Упрощение успешно - модель валидна!")
    else:
        print("ВНИМАНИЕ: Упрощенная модель не прошла валидацию!")
    
    # Сохраняем упрощенную модель
    onnx.save(simplified_model, OUTPUT_MODEL)
    print(f"Упрощенная модель сохранена в {OUTPUT_MODEL}")
    
    # Подсчет операторов в упрощенной модели
    simplified_ops = {}
    for node in simplified_model.graph.node:
        op_type = node.op_type
        simplified_ops[op_type] = simplified_ops.get(op_type, 0) + 1
    
    print("\nОператоры в упрощенной модели:")
    for op, count in simplified_ops.items():
        print(f"  - {op}: {count}")
    
    # Показываем разницу
    print("\nРазница до и после упрощения:")
    all_ops = set(list(ops.keys()) + list(simplified_ops.keys()))
    for op in all_ops:
        before = ops.get(op, 0)
        after = simplified_ops.get(op, 0)
        diff = after - before
        if diff != 0:
            print(f"  - {op}: {before} -> {after} ({'+' if diff > 0 else ''}{diff})")
    
except Exception as e:
    print(f"Ошибка: {e}") 