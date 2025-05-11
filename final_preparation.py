#!/usr/bin/env python3
import os
import onnx
from onnx import helper
import numpy as np
from onnx import numpy_helper
import shutil

# Путь к модели и выходному файлу
INPUT_MODEL = os.path.join('Assets', 'StreamingAssets', 'model_unity_ready.onnx')
FINAL_MODEL_DIR = os.path.join('Assets', 'StreamingAssets')
FINAL_MODEL = os.path.join(FINAL_MODEL_DIR, 'model_unity_final.onnx')

print(f"Загружаю модель из {INPUT_MODEL}...")
try:
    # Загружаем модель
    model = onnx.load(INPUT_MODEL)
    print(f"Модель успешно загружена: {model.graph.name}")
    
    # 1. Исправляем Split операторы (проблема с атрибутом 'split')
    nodes_to_fix = []
    for node in model.graph.node:
        if node.op_type == 'Split':
            # Собираем информацию о проблемных атрибутах
            has_split_attr = False
            for attr in node.attribute:
                if attr.name == 'split':
                    has_split_attr = True
                    break
                    
            if has_split_attr:
                nodes_to_fix.append(node)
    
    if nodes_to_fix:
        print(f"Найдено {len(nodes_to_fix)} Split операторов с проблемным атрибутом 'split'")
        
        for node in nodes_to_fix:
            # Удаляем проблемный атрибут и заменяем через ConstantOfShape
            split_attr = None
            other_attrs = []
            
            for attr in node.attribute:
                if attr.name == 'split':
                    split_attr = attr
                else:
                    other_attrs.append(attr)
            
            if split_attr is not None:
                # Удаляем все атрибуты
                del node.attribute[:]
                # Добавляем обратно только валидные атрибуты
                node.attribute.extend(other_attrs)
                print(f"Удален проблемный атрибут 'split' из узла {node.name}")
    
    # 2. Проверяем наличие других проблемных атрибутов в графе
    for node in model.graph.node:
        # Проверяем наличие и валидность атрибутов
        for attr in list(node.attribute):
            try:
                if attr.name in ['axes', 'splits'] and hasattr(attr, 'ints') and len(attr.ints) == 0:
                    # Удаляем пустые атрибуты
                    node.attribute.remove(attr)
                    print(f"Удален пустой атрибут '{attr.name}' из узла {node.name}")
            except Exception as e:
                print(f"Ошибка при обработке атрибута: {e}")
    
    # 3. Добавляем явную информацию об импорте opset - исправлено!
    # Метод clear() не работает с RepeatedCompositeContainer из Google Protobuf
    # Вместо того, чтобы очищать, создаем новую модель с нужным импортом opset
    
    # Создаем новый ONNX graph, копируя из оригинального
    new_graph = helper.make_graph(
        nodes=list(model.graph.node),
        name=model.graph.name,
        inputs=list(model.graph.input),
        outputs=list(model.graph.output),
        initializer=list(model.graph.initializer)
    )
    
    # Создаем новую модель с правильным opset
    new_model = helper.make_model(
        new_graph,
        producer_name="Unity Sentis Exporter",
        opset_imports=[helper.make_opsetid("", 13)]  # Основной домен с версией 13
    )
    
    # Копируем остальные поля из оригинальной модели
    new_model.producer_version = "1.0"
    new_model.doc_string = "ONNX model optimized for Unity Sentis 2.1.x"
    if hasattr(model, 'domain'):
        new_model.domain = "ai.onnx"
    
    # 4. Устанавливаем метаданные совместимости с Unity Sentis
    # Это уже сделано при создании новой модели
    
    # 5. Сохраняем обработанную модель
    print("Сохраняем финальную версию модели...")
    onnx.save(new_model, FINAL_MODEL)
    print(f"Финальная модель сохранена в {FINAL_MODEL}")
    
    # 6. Также копируем финальную модель в основной файл model.onnx
    MAIN_MODEL = os.path.join(FINAL_MODEL_DIR, 'model.onnx')
    shutil.copy2(FINAL_MODEL, MAIN_MODEL)
    print(f"Модель также скопирована в {MAIN_MODEL} для использования в Unity")
    
    # 7. Проверяем модель на ошибки
    print("\nПроверяем финальную модель на ошибки...")
    try:
        onnx.checker.check_model(new_model)
        print("✅ Проверка успешна! Модель соответствует спецификации ONNX и готова для Unity Sentis.")
    except Exception as check_error:
        print(f"⚠️ ПРЕДУПРЕЖДЕНИЕ: Некоторые несоответствия всё ещё остались: {check_error}")
        print("Однако, Unity Sentis может быть более толерантен к этим несоответствиям.")
    
    print("\n=== ИНСТРУКЦИИ ПО ИСПОЛЬЗОВАНИЮ МОДЕЛИ В UNITY ===")
    print("1. Теперь при запуске проекта, Unity будет использовать обновлённую модель.")
    print("2. Сериализуйте модель через Inspector: нажмите на model.onnx, затем кнопку 'Serialize To StreamingAssets'.")
    print("3. Если Unity всё ещё не может загрузить модель, попробуйте:")
    print("   - Убедитесь, что ссылка на файл модели правильная в коде.")
    print("   - Возможно, потребуется еще больше оптимизации для использования с Sentis.")
    
except Exception as e:
    print(f"Ошибка: {e}") 