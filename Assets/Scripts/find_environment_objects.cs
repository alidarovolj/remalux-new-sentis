using UnityEngine;
using System.Collections.Generic;

public class FindEnvironmentObjects : MonoBehaviour
{
      [ContextMenu("Find All Environment Objects")]
      public void FindAllEnvironmentObjects()
      {
            Debug.Log("=== ПОИСК ВСЕХ ОБЪЕКТОВ СРЕДЫ ===");

            // 1. Найти все объекты с MeshRenderer
            var meshRenderers = FindObjectsOfType<MeshRenderer>(true); // включая неактивные
            Debug.Log($"Найдено MeshRenderer объектов: {meshRenderers.Length}");

            foreach (var renderer in meshRenderers)
            {
                  GameObject obj = renderer.gameObject;
                  string layerName = LayerMask.LayerToName(obj.layer);
                  bool hasCollider = obj.GetComponent<Collider>() != null;

                  Debug.Log($"📦 MeshRenderer: '{obj.name}' | Слой: {obj.layer} ({layerName}) | " +
                           $"Активен: {obj.activeInHierarchy} | Коллайдер: {hasCollider} | " +
                           $"Позиция: {obj.transform.position}");
            }

            // 2. Найти все объекты с "Environment" или "Simulation" в названии
            var allObjects = FindObjectsOfType<Transform>(true);
            Debug.Log($"\n=== ПОИСК ПО КЛЮЧЕВЫМ СЛОВАМ ===");

            foreach (var obj in allObjects)
            {
                  string name = obj.name.ToLower();
                  if (name.Contains("environment") || name.Contains("simulation") ||
                      name.Contains("wall") || name.Contains("floor") ||
                      name.Contains("room") || name.Contains("scene"))
                  {
                        bool hasRenderer = obj.GetComponent<MeshRenderer>() != null;
                        bool hasCollider = obj.GetComponent<Collider>() != null;
                        string layerName = LayerMask.LayerToName(obj.gameObject.layer);

                        Debug.Log($"🎯 Найден: '{obj.name}' | Слой: {obj.gameObject.layer} ({layerName}) | " +
                                 $"Активен: {obj.gameObject.activeInHierarchy} | " +
                                 $"MeshRenderer: {hasRenderer} | Коллайдер: {hasCollider}");

                        // Проверяем дочерние объекты
                        for (int i = 0; i < obj.childCount; i++)
                        {
                              var child = obj.GetChild(i);
                              bool childHasRenderer = child.GetComponent<MeshRenderer>() != null;
                              bool childHasCollider = child.GetComponent<Collider>() != null;

                              if (childHasRenderer || childHasCollider)
                              {
                                    Debug.Log($"  └─ Дочерний: '{child.name}' | MeshRenderer: {childHasRenderer} | Коллайдер: {childHasCollider}");
                              }
                        }
                  }
            }

            // 3. Автоматически добавить коллайдеры
            Debug.Log($"\n=== АВТОМАТИЧЕСКОЕ ДОБАВЛЕНИЕ КОЛЛАЙДЕРОВ ===");
            int addedColliders = 0;

            foreach (var renderer in meshRenderers)
            {
                  if (renderer.GetComponent<Collider>() == null)
                  {
                        var collider = renderer.gameObject.AddComponent<MeshCollider>();
                        renderer.gameObject.layer = LayerMask.NameToLayer("SimulatedEnvironment"); // Слой 8
                        addedColliders++;
                        Debug.Log($"✅ Добавлен MeshCollider к: '{renderer.name}' | Установлен слой: SimulatedEnvironment");
                  }
            }

            Debug.Log($"🎉 Добавлено коллайдеров: {addedColliders}");
            Debug.Log("=== КОНЕЦ ПОИСКА ===");
      }
}