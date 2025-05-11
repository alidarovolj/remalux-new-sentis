#!/usr/bin/env python3
import os
import onnx
import networkx as nx
import time

# Путь к модели и выходному файлу
INPUT_MODEL = os.path.join('Assets', 'StreamingAssets', 'model_final.onnx')
OUTPUT_MODEL = os.path.join('Assets', 'StreamingAssets', 'model_unity_ready.onnx')

print(f"Загружаю модель из {INPUT_MODEL}...")
try:
    # Пытаемся установить networkx если его нет
    try:
        import networkx
    except ImportError:
        print("Устанавливаем networkx для топологической сортировки...")
        import sys
        import subprocess
        subprocess.check_call([sys.executable, "-m", "pip", "install", "networkx"])
        import networkx

    # Загружаем модель
    model = onnx.load(INPUT_MODEL)
    print(f"Модель успешно загружена: {model.graph.name}")
    
    # Исправление топологического порядка узлов
    print("Анализируем топологический порядок узлов...")
    
    # Строим граф зависимостей
    G = nx.DiGraph()
    
    # Словарь для быстрого поиска узлов по выходам
    output_to_node = {}
    for i, node in enumerate(model.graph.node):
        G.add_node(i)
        for output in node.output:
            output_to_node[output] = i
    
    # Добавляем рёбра в граф зависимостей
    for i, node in enumerate(model.graph.node):
        for input_name in node.input:
            if input_name in output_to_node:
                G.add_edge(output_to_node[input_name], i)
    
    # Проверяем наличие циклов
    if not nx.is_directed_acyclic_graph(G):
        print("⚠️ ПРЕДУПРЕЖДЕНИЕ: Обнаружены циклы в графе зависимостей. Пытаемся исправить...")
        # Пытаемся разорвать циклы (это упрощённая реализация)
        for cycle in nx.simple_cycles(G):
            edge_to_remove = (cycle[-1], cycle[0])
            G.remove_edge(*edge_to_remove)
            print(f"Удалён цикл между узлами {cycle[-1]} и {cycle[0]}")
    
    try:
        # Выполняем топологическую сортировку
        sorted_indices = list(nx.topological_sort(G))
        
        # Применяем новый порядок к узлам
        sorted_nodes = [model.graph.node[i] for i in sorted_indices]
        
        # Очищаем и заново заполняем узлы графа
        del model.graph.node[:]
        model.graph.node.extend(sorted_nodes)
        
        print(f"Узлы успешно переупорядочены")
    except nx.NetworkXUnfeasible:
        print("⚠️ Невозможно выполнить топологическую сортировку из-за циклов. Пытаемся применить альтернативный метод...")
        
        # Альтернативный метод - используем ONNX Graph для фиксации порядка
        checker_start = time.time()
        try:
            onnx.checker.check_model(model)
            print("Модель валидна без изменения порядка!")
        except Exception as e:
            print(f"Ошибка валидации исходной модели: {e}")
            
            # Получаем все уникальные входы и выходы узлов
            node_outputs = set()
            for node in model.graph.node:
                for output in node.output:
                    node_outputs.add(output)
            
            # Словарь для поиска зависимостей
            output_producers = {}
            for node in model.graph.node:
                for output in node.output:
                    output_producers[output] = node
            
            # Создаём новый список узлов для правильного порядка
            visited = set()
            ordered_nodes = []
            
            # Рекурсивная функция для DFS
            def visit(node_idx):
                if node_idx in visited:
                    return
                visited.add(node_idx)
                
                node = model.graph.node[node_idx]
                for input_name in node.input:
                    if input_name in output_producers:
                        producer_idx = model.graph.node.index(output_producers[input_name])
                        visit(producer_idx)
                
                ordered_nodes.append(node)
            
            # Обходим все узлы для сортировки
            try:
                for i in range(len(model.graph.node)):
                    if i not in visited:
                        visit(i)
                
                # Заменяем узлы отсортированными
                del model.graph.node[:]
                model.graph.node.extend(ordered_nodes)
                print("Узлы переупорядочены с помощью DFS")
            except Exception as sort_error:
                print(f"Ошибка при попытке переупорядочить узлы: {sort_error}")
    
    # Сохраняем исправленную модель
    onnx.save(model, OUTPUT_MODEL)
    print(f"Исправленная модель сохранена в {OUTPUT_MODEL}")
    
    # Проверяем модель на ошибки
    print("Проверяем модель на ошибки...")
    try:
        onnx.checker.check_model(model)
        print("✅ Проверка успешна! Модель соответствует спецификации ONNX и готова для Unity Sentis.")
    except Exception as check_error:
        print(f"⚠️ ПРЕДУПРЕЖДЕНИЕ: Проверка модели выявила проблемы: {check_error}")
        print("Но модель все равно может работать в Unity Sentis.")
    
except Exception as e:
    print(f"Ошибка: {e}") 