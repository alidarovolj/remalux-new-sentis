"""
Скрипт для определения классов сегментации модели ADE20K

Модель model.onnx использует набор данных ADE20K, который содержит 150 классов.
Этот скрипт позволяет получить список классов и индекс класса "стена" (wall).
"""

ADE20K_CLASSES = [
    'wall', 'building', 'sky', 'floor', 'tree', 
    'ceiling', 'road', 'bed', 'windowpane', 'grass', 
    'cabinet', 'sidewalk', 'person', 'earth', 'door', 
    'table', 'mountain', 'plant', 'curtain', 'chair', 
    'car', 'water', 'painting', 'sofa', 'shelf', 
    'house', 'sea', 'mirror', 'rug', 'field', 
    'armchair', 'seat', 'fence', 'desk', 'rock', 
    'wardrobe', 'lamp', 'bathtub', 'railing', 'cushion', 
    'base', 'box', 'column', 'signboard', 'chest of drawers', 
    'counter', 'sand', 'sink', 'skyscraper', 'fireplace', 
    'refrigerator', 'grandstand', 'path', 'stairs', 'runway', 
    'case', 'pool table', 'pillow', 'screen door', 'stairway', 
    'river', 'bridge', 'bookcase', 'blind', 'coffee table', 
    'toilet', 'flower', 'book', 'hill', 'bench', 
    'countertop', 'stove', 'palm', 'kitchen island', 'computer', 
    'swivel chair', 'boat', 'bar', 'arcade machine', 'hovel', 
    'bus', 'towel', 'light', 'truck', 'tower', 
    'chandelier', 'awning', 'streetlight', 'booth', 'television receiver', 
    'airplane', 'dirt track', 'apparel', 'pole', 'land', 
    'bannister', 'escalator', 'ottoman', 'bottle', 'buffet', 
    'poster', 'stage', 'van', 'ship', 'fountain', 
    'conveyer belt', 'canopy', 'washer', 'plaything', 'swimming pool', 
    'stool', 'barrel', 'basket', 'waterfall', 'tent', 
    'bag', 'minibike', 'cradle', 'oven', 'ball', 
    'food', 'step', 'tank', 'trade name', 'microwave', 
    'pot', 'animal', 'bicycle', 'lake', 'dishwasher', 
    'screen', 'blanket', 'sculpture', 'hood', 'sconce', 
    'vase', 'traffic light', 'tray', 'ashcan', 'fan', 
    'pier', 'crt screen', 'plate', 'monitor', 'bulletin board', 
    'shower', 'radiator', 'glass', 'clock', 'flag'
]

def main():
    # Индекс начинается с 0, поэтому wall имеет индекс 0
    wall_index = ADE20K_CLASSES.index('wall')
    print(f"Всего классов: {len(ADE20K_CLASSES)}")
    print(f"Индекс класса 'wall': {wall_index}")
    
    # Выводим все классы с индексами
    print("\nПолный список классов:")
    for i, class_name in enumerate(ADE20K_CLASSES):
        print(f"{i}: {class_name}")
    
    # Выводим архитектурные элементы, которые могут быть полезны для перекраски
    architectural_elements = ['wall', 'building', 'floor', 'ceiling', 'door', 
                             'windowpane', 'cabinet', 'fence', 'house', 'column']
    
    print("\nАрхитектурные элементы (для перекраски):")
    for element in architectural_elements:
        if element in ADE20K_CLASSES:
            index = ADE20K_CLASSES.index(element)
            print(f"{index}: {element}")

if __name__ == "__main__":
    main() 