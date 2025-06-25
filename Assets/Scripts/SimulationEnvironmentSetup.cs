using UnityEngine;
using System.Collections.Generic;

/// <summary>
/// Настройка окружения для XR Simulation с коллайдерами для рейкастинга
/// </summary>
public class SimulationEnvironmentSetup : MonoBehaviour
{
      [Header("Настройки комнаты")]
      [SerializeField] private bool createRoomOnStart = true;
      [SerializeField] private Vector3 roomSize = new Vector3(4f, 3f, 5f);
      [SerializeField] private Vector3 roomCenter = new Vector3(0f, 1.5f, 0f);

      [Header("Настройки материалов")]
      [SerializeField] private Material wallMaterial;
      [SerializeField] private Material floorMaterial;

      [Header("Настройки слоев")]
      [SerializeField] private string wallLayerName = "SimulatedEnvironment";
      [SerializeField] private string floorLayerName = "SimulatedEnvironment";

      private List<GameObject> createdObjects = new List<GameObject>();

      void Start()
      {
            if (createRoomOnStart)
            {
                  CreateSimulationRoom();
            }
      }

      /// <summary>
      /// Создает простую комнату с коллайдерами для симуляции
      /// </summary>
      public void CreateSimulationRoom()
      {
            Debug.Log("[SimulationEnvironmentSetup] 🏠 Создание комнаты для симуляции...");

            // Получаем слои
            int wallLayer = LayerMask.NameToLayer(wallLayerName);
            int floorLayer = LayerMask.NameToLayer(floorLayerName);

            if (wallLayer == -1)
            {
                  Debug.LogWarning($"[SimulationEnvironmentSetup] Слой '{wallLayerName}' не найден, используется Default");
                  wallLayer = 0;
            }

            if (floorLayer == -1)
            {
                  Debug.LogWarning($"[SimulationEnvironmentSetup] Слой '{floorLayerName}' не найден, используется Default");
                  floorLayer = 0;
            }

            // Создаем материалы если не назначены
            if (wallMaterial == null)
            {
                  wallMaterial = CreateDefaultMaterial(new Color(0.8f, 0.8f, 0.8f, 1f));
            }

            if (floorMaterial == null)
            {
                  floorMaterial = CreateDefaultMaterial(new Color(0.6f, 0.6f, 0.6f, 1f));
            }

            // Создаем родительский объект
            GameObject roomParent = new GameObject("SimulationRoom");
            createdObjects.Add(roomParent);

            // Пол
            GameObject floor = CreateWall("Floor",
                roomCenter - Vector3.up * (roomSize.y / 2f),
                Quaternion.Euler(90, 0, 0),
                new Vector3(roomSize.x, roomSize.z, 0.1f),
                floorMaterial,
                floorLayer);
            floor.transform.SetParent(roomParent.transform);

            // Передняя стена (Z+)
            GameObject frontWall = CreateWall("FrontWall",
                roomCenter + Vector3.forward * (roomSize.z / 2f),
                Quaternion.identity,
                new Vector3(roomSize.x, roomSize.y, 0.1f),
                wallMaterial,
                wallLayer);
            frontWall.transform.SetParent(roomParent.transform);

            // Задняя стена (Z-)
            GameObject backWall = CreateWall("BackWall",
                roomCenter - Vector3.forward * (roomSize.z / 2f),
                Quaternion.Euler(0, 180, 0),
                new Vector3(roomSize.x, roomSize.y, 0.1f),
                wallMaterial,
                wallLayer);
            backWall.transform.SetParent(roomParent.transform);

            // Левая стена (X-)
            GameObject leftWall = CreateWall("LeftWall",
                roomCenter - Vector3.right * (roomSize.x / 2f),
                Quaternion.Euler(0, 90, 0),
                new Vector3(roomSize.z, roomSize.y, 0.1f),
                wallMaterial,
                wallLayer);
            leftWall.transform.SetParent(roomParent.transform);

            // Правая стена (X+)
            GameObject rightWall = CreateWall("RightWall",
                roomCenter + Vector3.right * (roomSize.x / 2f),
                Quaternion.Euler(0, -90, 0),
                new Vector3(roomSize.z, roomSize.y, 0.1f),
                wallMaterial,
                wallLayer);
            rightWall.transform.SetParent(roomParent.transform);

            // Потолок (опционально)
            GameObject ceiling = CreateWall("Ceiling",
                roomCenter + Vector3.up * (roomSize.y / 2f),
                Quaternion.Euler(-90, 0, 0),
                new Vector3(roomSize.x, roomSize.z, 0.1f),
                floorMaterial,
                floorLayer);
            ceiling.transform.SetParent(roomParent.transform);

            Debug.Log($"[SimulationEnvironmentSetup] ✅ Комната создана: {roomSize}, центр: {roomCenter}");

            // Проверяем количество коллайдеров
            Collider[] allColliders = FindObjectsOfType<Collider>();
            Debug.Log($"[SimulationEnvironmentSetup] 📊 Всего коллайдеров в сцене после создания комнаты: {allColliders.Length}");
      }

      /// <summary>
      /// Создает стену с коллайдером
      /// </summary>
      private GameObject CreateWall(string name, Vector3 position, Quaternion rotation, Vector3 scale, Material material, int layer)
      {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = name;
            wall.transform.position = position;
            wall.transform.rotation = rotation;
            wall.transform.localScale = scale;
            wall.layer = layer;

            // Настраиваем рендерер
            MeshRenderer renderer = wall.GetComponent<MeshRenderer>();
            if (renderer != null && material != null)
            {
                  renderer.material = material;
            }

            // Убеждаемся, что есть коллайдер
            BoxCollider collider = wall.GetComponent<BoxCollider>();
            if (collider == null)
            {
                  collider = wall.AddComponent<BoxCollider>();
            }

            createdObjects.Add(wall);

            Debug.Log($"[SimulationEnvironmentSetup] ➕ Создана стена '{name}' на слое {layer} с коллайдером");

            return wall;
      }

      /// <summary>
      /// Создает простой материал
      /// </summary>
      private Material CreateDefaultMaterial(Color color)
      {
            Material mat = new Material(Shader.Find("Standard"));
            mat.color = color;
            return mat;
      }

      /// <summary>
      /// Удаляет созданные объекты
      /// </summary>
      public void ClearRoom()
      {
            foreach (GameObject obj in createdObjects)
            {
                  if (obj != null)
                  {
                        DestroyImmediate(obj);
                  }
            }
            createdObjects.Clear();

            Debug.Log("[SimulationEnvironmentSetup] 🧹 Комната удалена");
      }

      void OnDestroy()
      {
            ClearRoom();
      }
}