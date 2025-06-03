using UnityEngine;
using System.Collections.Generic;

public class CreateTestEnvironment : MonoBehaviour
{
      [Header("🏠 СОЗДАНИЕ ТЕСТОВОЙ СРЕДЫ")]
      [SerializeField] private bool createOnStart = true;
      [SerializeField] private Material wallMaterial;

      [ContextMenu("Create Test Environment")]
      public void CreateEnvironment()
      {
            Debug.Log("🏗️ === СОЗДАНИЕ ТЕСТОВОЙ СРЕДЫ ===");

            // Удаляем старую среду если есть
            GameObject oldEnv = GameObject.Find("TestEnvironment");
            if (oldEnv != null)
            {
                  Debug.Log("🗑️ Удаляем старую тестовую среду");
                  DestroyImmediate(oldEnv);
            }

            // Создаем родительский объект
            GameObject environment = new GameObject("TestEnvironment");

            // Получаем позицию камеры
            Camera mainCamera = Camera.main;
            if (mainCamera == null)
            {
                  Debug.LogError("❌ Главная камера не найдена!");
                  return;
            }

            Vector3 cameraPos = mainCamera.transform.position;
            Vector3 cameraForward = mainCamera.transform.forward;
            Vector3 cameraRight = mainCamera.transform.right;

            Debug.Log($"📷 Камера: Pos={cameraPos}, Forward={cameraForward}");

            // Создаем стены вокруг камеры
            CreateWall("Wall_Front", cameraPos + cameraForward * 3f, Vector3.forward, environment.transform);
            CreateWall("Wall_Left", cameraPos - cameraRight * 3f, Vector3.right, environment.transform);
            CreateWall("Wall_Right", cameraPos + cameraRight * 3f, -Vector3.right, environment.transform);
            CreateWall("Wall_Back", cameraPos - cameraForward * 3f, -Vector3.forward, environment.transform);

            // Создаем пол
            CreateFloor("Floor", cameraPos + Vector3.down * 1.5f, environment.transform);

            Debug.Log("✅ Тестовая среда создана!");

            // Проверяем результат
            ValidateEnvironment();
      }

      private void CreateWall(string name, Vector3 position, Vector3 normal, Transform parent)
      {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = name;
            wall.transform.parent = parent;
            wall.transform.position = position;
            wall.transform.localScale = new Vector3(4f, 3f, 0.1f); // Тонкая высокая стена

            // Поворачиваем стену лицом к камере
            wall.transform.LookAt(position + normal);

            // Настраиваем слой и компоненты
            wall.layer = LayerMask.NameToLayer("SimulatedEnvironment"); // Слой 8

            // Mesh Collider уже есть от CreatePrimitive, но перенастраиваем
            MeshCollider collider = wall.GetComponent<MeshCollider>();
            if (collider != null)
            {
                  collider.convex = false;
                  collider.isTrigger = false;
            }

            // Применяем материал если есть
            MeshRenderer renderer = wall.GetComponent<MeshRenderer>();
            if (renderer != null && wallMaterial != null)
            {
                  renderer.material = wallMaterial;
            }

            Debug.Log($"🧱 Создана стена: {name} | Pos: {position} | Layer: {wall.layer} | Коллайдер: {collider != null}");
      }

      private void CreateFloor(string name, Vector3 position, Transform parent)
      {
            GameObject floor = GameObject.CreatePrimitive(PrimitiveType.Cube);
            floor.name = name;
            floor.transform.parent = parent;
            floor.transform.position = position;
            floor.transform.localScale = new Vector3(10f, 0.1f, 10f); // Плоский широкий пол

            // Настраиваем слой
            floor.layer = LayerMask.NameToLayer("SimulatedEnvironment");

            MeshCollider collider = floor.GetComponent<MeshCollider>();
            if (collider != null)
            {
                  collider.convex = false;
                  collider.isTrigger = false;
            }

            Debug.Log($"🏢 Создан пол: {name} | Pos: {position} | Layer: {floor.layer}");
      }

      private void ValidateEnvironment()
      {
            Debug.Log("🔍 === ПРОВЕРКА СОЗДАННОЙ СРЕДЫ ===");

            var allColliders = FindObjectsOfType<Collider>();
            var wallColliders = 0;
            var environmentColliders = 0;

            foreach (var collider in allColliders)
            {
                  if (collider.gameObject.layer == LayerMask.NameToLayer("SimulatedEnvironment"))
                  {
                        environmentColliders++;
                        if (collider.name.Contains("Wall"))
                              wallColliders++;

                        Debug.Log($"✅ Валидный коллайдер: {collider.name} | Layer: {collider.gameObject.layer}");
                  }
            }

            Debug.Log($"📊 ИТОГО: Коллайдеров в SimulatedEnvironment: {environmentColliders} | Стен: {wallColliders}");

            if (environmentColliders > 0)
            {
                  Debug.Log("🎉 СРЕДА ГОТОВА! Теперь рейкасты должны работать!");
            }
            else
            {
                  Debug.LogError("❌ Что-то пошло не так - коллайдеры не созданы!");
            }
      }

      private void Start()
      {
            if (createOnStart)
            {
                  // Задержка для инициализации других систем
                  Invoke(nameof(CreateEnvironment), 1f);
            }
      }
}