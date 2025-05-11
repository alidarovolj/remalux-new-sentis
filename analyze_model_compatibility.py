#!/usr/bin/env python3
import os
import onnx

# Путь к оптимизированной модели
MODEL_PATH = os.path.join('Assets', 'StreamingAssets', 'model_simplified.onnx')

# Список операторов, поддерживаемых в Unity Sentis 2.1.x
# Источник: https://docs.unity3d.com/Packages/com.unity.sentis@2.1/manual/SupportedOperators.html
SUPPORTED_OPERATORS = {
    # Tensor operators
    'Cast', 'Concat', 'ConstantOfShape', 'Expand', 'Flatten', 'Gather', 'Identity',
    'OneHot', 'Reshape', 'Slice', 'Split', 'Squeeze', 'Tile', 'Transpose', 'Unsqueeze',
    
    # Math operators
    'Add', 'BitShift', 'Div', 'Exp', 'Greater', 'GreaterOrEqual', 'Less', 'LessOrEqual',
    'Log', 'MatMul', 'Max', 'Mean', 'Min', 'Mul', 'Neg', 'Pow', 'ReduceL1', 'ReduceL2',
    'ReduceLogSum', 'ReduceLogSumExp', 'ReduceMax', 'ReduceMean', 'ReduceMin', 'ReduceProd',
    'ReduceSum', 'ReduceSumSquare', 'Relu', 'Sigmoid', 'Sign', 'Sin', 'Softmax', 'Softplus',
    'Softsign', 'Sqrt', 'Sub', 'Sum', 'Tanh', 'Where',
    
    # Neural network operators
    'AveragePool', 'Conv', 'ConvTranspose', 'GlobalAveragePool', 'GlobalMaxPool', 'InstanceNormalization',
    'MaxPool',
    
    # Control flow operators
    'Loop', 'Scan',
    
    # Logical operators
    'And', 'Equal', 'Greater', 'GreaterOrEqual', 'Less', 'LessOrEqual', 'Not', 'Or', 'Xor',
    
    # Constant
    'Constant',
    
    # Randomizer
    'RandomUniform', 'RandomUniformLike', 'RandomNormal', 'RandomNormalLike',
    
    # Элементы, которые могут быть поддержаны в новых версиях
    'Gemm', 'Clip', 'BatchNormalization', 'Shape', 'Erf', 'Resize', 'Pad', 'LSTM'
}

# Загрузка модели
print(f"Загружаю модель из {MODEL_PATH}...")
try:
    model = onnx.load(MODEL_PATH)
    print(f"Модель успешно загружена: {model.graph.name}")
    
    # Анализ операторов
    ops = {}
    unsupported_ops = {}
    
    for node in model.graph.node:
        op_type = node.op_type
        ops[op_type] = ops.get(op_type, 0) + 1
        
        if op_type not in SUPPORTED_OPERATORS:
            unsupported_ops[op_type] = unsupported_ops.get(op_type, 0) + 1
    
    print("\nСтатистика операторов в модели:")
    print(f"Всего операторов: {sum(ops.values())}")
    print(f"Уникальных типов операторов: {len(ops)}")
    
    if unsupported_ops:
        print("\n❌ ВНИМАНИЕ: Обнаружены неподдерживаемые операторы:")
        for op, count in unsupported_ops.items():
            print(f"  - {op}: {count} операторов")
        print(f"\nВсего неподдерживаемых операторов: {sum(unsupported_ops.values())} из {sum(ops.values())} ({round(sum(unsupported_ops.values())/sum(ops.values())*100, 2)}%)")
        print("Модель может не работать в Unity Sentis без дополнительной конвертации.")
    else:
        print("\n✅ Все операторы поддерживаются Unity Sentis!")
        print("Модель должна быть совместима с Unity Sentis.")
    
    # Подробная информация по всем операторам
    print("\nПолный список операторов в модели:")
    for op, count in sorted(ops.items()):
        status = "✅" if op in SUPPORTED_OPERATORS else "❌"
        print(f"  - {status} {op}: {count}")
    
except Exception as e:
    print(f"Ошибка при анализе модели: {e}") 