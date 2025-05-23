using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Автоматически добавляет симулированную среду в сцену при запуске
/// для обеспечения работы рейкастинга в системе создания AR плоскостей
/// </summary>
public class SceneSetupHelper : MonoBehaviour
{
      [Header("Симулированная среда")]
      [SerializeField] private GameObject simulationEnvironmentPrefab;
      [SerializeField] private bool autoCreateEnvironment = true;
      [SerializeField] private bool logEnvironmentSetup = true;

      private static bool environmentCreated = false;

      [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
      private static void AutoSetupScene()
      {
            // Создаем helper объект
            var helperGO = new GameObject("SceneSetupHelper");
            var helper = helperGO.AddComponent<SceneSetupHelper>();
            helper.SetupEnvironment();
      }

      private void SetupEnvironment()
      {
            if (environmentCreated || !autoCreateEnvironment)
                  return;

            if (logEnvironmentSetup)
                  Debug.Log("[SceneSetupHelper] Настройка симулированной среды...");

            // Ищем существующую среду по типичным именам и проверяем наличие коллайдеров
            GameObject existingEnvironment = FindExistingSimulationEnvironment();
            if (existingEnvironment != null && HasValidColliders(existingEnvironment))
            {
                  if (logEnvironmentSetup)
                        Debug.Log($"[SceneSetupHelper] Симулированная среда с коллайдерами уже существует: {existingEnvironment.name}");
                  environmentCreated = true;
                  return;
            }

            if (existingEnvironment != null && !HasValidColliders(existingEnvironment))
            {
                  if (logEnvironmentSetup)
                        Debug.LogWarning($"[SceneSetupHelper] Найден объект {existingEnvironment.name}, но у него нет коллайдеров. Создаю дополнительную симулированную среду.");
            }

            // Загружаем префаб симулированной среды
            if (simulationEnvironmentPrefab == null)
            {
                  simulationEnvironmentPrefab = Resources.Load<GameObject>("Prefabs/Simulation Environment");
                  if (simulationEnvironmentPrefab == null)
                  {
                        // Пробуем альтернативные пути
                        simulationEnvironmentPrefab = Resources.Load<GameObject>("SimulationEnvironment");
                  }
            }

            if (simulationEnvironmentPrefab == null)
            {
                  // Создаем простую симулированную среду
                  // CreateBasicEnvironment(); // ЗАКОММЕНТИРОВАНО
                  if (logEnvironmentSetup) Debug.LogWarning("[SceneSetupHelper] Префаб симуляционной среды не найден и CreateBasicEnvironment закомментирован. Симуляционная среда не будет создана.");
            }
            else
            {
                  // Создаем из префаба
                  var environmentInstance = Instantiate(simulationEnvironmentPrefab);
                  environmentInstance.name = "Simulation Environment (Auto-Created)";

                  if (logEnvironmentSetup)
                        Debug.Log($"[SceneSetupHelper] ✅ Создана симулированная среда из префаба: {simulationEnvironmentPrefab.name}");
            }

            environmentCreated = true;

            // Добавляем дополнительные объекты для рейкастинга
            // CreateAdditionalRaycastTargets(); // ЗАКОММЕНТИРОВАНО
            if (logEnvironmentSetup) Debug.Log("[SceneSetupHelper] CreateAdditionalRaycastTargets закомментирован. Дополнительные цели для рейкастинга не будут созданы.");
      }

      /// <summary>
      /// Проверяет наличие коллайдеров у объекта и его детей
      /// </summary>
      private bool HasValidColliders(GameObject obj)
      {
            // Проверяем коллайдеры у самого объекта
            Collider[] colliders = obj.GetComponentsInChildren<Collider>();

            if (colliders.Length == 0)
            {
                  if (logEnvironmentSetup)
                        Debug.Log($"[SceneSetupHelper] Объект {obj.name} не имеет коллайдеров");
                  return false;
            }

            // Проверяем, что хотя бы один коллайдер активен
            foreach (Collider collider in colliders)
            {
                  if (collider.enabled && collider.gameObject.activeInHierarchy)
                  {
                        if (logEnvironmentSetup)
                              Debug.Log($"[SceneSetupHelper] Найден активный коллайдер: {collider.name} ({collider.GetType().Name})");
                        return true;
                  }
            }

            if (logEnvironmentSetup)
                  Debug.Log($"[SceneSetupHelper] Объект {obj.name} имеет коллайдеры, но все отключены");
            return false;
      }

      /// <summary>
      /// Ищет существующую симулированную среду в сцене по типичным именам
      /// </summary>
      private GameObject FindExistingSimulationEnvironment()
      {
            // Поиск по типичным именам симулированных сред (без поиска по общим словам)
            string[] environmentNames = {
                  "Simulation Environment",
                  "SimulationEnvironment",
                  "Simulated Environment",
                  "XR Simulation Environment",
                  "Unity Simulation Environment",
                  "Basic Simulation Environment",
                  "Simulation Environment (Auto-Created)"
            };

            foreach (string name in environmentNames)
            {
                  GameObject found = GameObject.Find(name);
                  if (found != null)
                  {
                        if (logEnvironmentSetup)
                              Debug.Log($"[SceneSetupHelper] Найден объект симулированной среды: {found.name}");
                        return found;
                  }
            }

            if (logEnvironmentSetup)
                  Debug.Log("[SceneSetupHelper] Объекты симулированной среды не найдены");
            return null;
      }

      private void CreateBasicEnvironment()
      {
            if (logEnvironmentSetup)
                  Debug.Log("[SceneSetupHelper] Создание базовой симулированной среды...");

            var environmentParent = new GameObject("Basic Simulation Environment");

            // Создаем пол
            var floor = GameObject.CreatePrimitive(PrimitiveType.Plane);
            floor.name = "Floor";
            floor.transform.SetParent(environmentParent.transform);
            floor.transform.localPosition = Vector3.zero;
            floor.transform.localScale = Vector3.one * 5f;
            floor.layer = LayerMask.NameToLayer("Default");

            // Убеждаемся, что коллайдер включен
            var floorCollider = floor.GetComponent<Collider>();
            if (floorCollider != null)
            {
                  floorCollider.enabled = true;
                  if (logEnvironmentSetup)
                        Debug.Log($"[SceneSetupHelper] ✅ Создан пол с коллайдером: {floorCollider.GetType().Name}");
            }

            // Создаем стены
            CreateWall("Wall_Front", environmentParent.transform, new Vector3(0, 1.5f, 2.5f), new Vector3(0, 0, 0), new Vector3(5, 3, 0.1f));
            CreateWall("Wall_Back", environmentParent.transform, new Vector3(0, 1.5f, -2.5f), new Vector3(0, 180, 0), new Vector3(5, 3, 0.1f));
            CreateWall("Wall_Left", environmentParent.transform, new Vector3(-2.5f, 1.5f, 0), new Vector3(0, 90, 0), new Vector3(5, 3, 0.1f));
            CreateWall("Wall_Right", environmentParent.transform, new Vector3(2.5f, 1.5f, 0), new Vector3(0, -90, 0), new Vector3(5, 3, 0.1f));

            if (logEnvironmentSetup)
            {
                  var totalColliders = environmentParent.GetComponentsInChildren<Collider>();
                  Debug.Log($"[SceneSetupHelper] ✅ Создана базовая симулированная среда с {totalColliders.Length} коллайдерами");

                  // Логируем все созданные коллайдеры
                  foreach (var collider in totalColliders)
                  {
                        Debug.Log($"[SceneSetupHelper] Коллайдер: {collider.name} ({collider.GetType().Name}) на слое {LayerMask.LayerToName(collider.gameObject.layer)}");
                  }
            }
      }

      private void CreateWall(string name, Transform parent, Vector3 position, Vector3 rotation, Vector3 scale)
      {
            var wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = name;
            wall.transform.SetParent(parent);
            wall.transform.localPosition = position;
            wall.transform.localEulerAngles = rotation;
            wall.transform.localScale = scale;
            wall.layer = LayerMask.NameToLayer("Default");

            // Убеждаемся, что коллайдер включен
            var wallCollider = wall.GetComponent<Collider>();
            if (wallCollider != null)
            {
                  wallCollider.enabled = true;
                  if (logEnvironmentSetup)
                        Debug.Log($"[SceneSetupHelper] ✅ Создана стена {name} с коллайдером: {wallCollider.GetType().Name}");
            }

            // Добавляем материал для визуализации
            var renderer = wall.GetComponent<Renderer>();
            if (renderer != null)
            {
                  var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                  material.color = new Color(0.8f, 0.8f, 0.8f, 1f);
                  renderer.material = material;
            }
      }

      private void CreateAdditionalRaycastTargets()
      {
            if (logEnvironmentSetup)
                  Debug.Log("[SceneSetupHelper] Добавление дополнительных целей для рейкастинга...");

            var targetsParent = new GameObject("Raycast Targets");

            // Создаем несколько плоскостей на разных расстояниях и углах
            CreateRaycastTarget("Target_1", targetsParent.transform, new Vector3(0, 1f, 2f), Vector3.zero, new Vector3(2, 2, 0.1f));
            CreateRaycastTarget("Target_2", targetsParent.transform, new Vector3(1.5f, 1f, 1f), new Vector3(0, -45, 0), new Vector3(1.5f, 2, 0.1f));
            CreateRaycastTarget("Target_3", targetsParent.transform, new Vector3(-1.5f, 1f, 1f), new Vector3(0, 45, 0), new Vector3(1.5f, 2, 0.1f));

            if (logEnvironmentSetup)
                  Debug.Log("[SceneSetupHelper] ✅ Добавлены дополнительные цели для рейкастинга");
      }

      private void CreateRaycastTarget(string name, Transform parent, Vector3 position, Vector3 rotation, Vector3 scale)
      {
            var target = GameObject.CreatePrimitive(PrimitiveType.Cube);
            target.name = name;
            target.transform.SetParent(parent);
            target.transform.localPosition = position;
            target.transform.localEulerAngles = rotation;
            target.transform.localScale = scale;
            target.layer = LayerMask.NameToLayer("Default");

            // Делаем невидимым, но с коллайдером
            var renderer = target.GetComponent<Renderer>();
            if (renderer != null)
            {
                  var material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                  material.color = new Color(0.5f, 1f, 0.5f, 0.3f);
                  material.SetFloat("_Mode", 2); // Transparent
                  material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                  material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                  material.SetInt("_ZWrite", 0);
                  material.DisableKeyword("_ALPHATEST_ON");
                  material.EnableKeyword("_ALPHABLEND_ON");
                  material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                  material.renderQueue = 3000;
                  renderer.material = material;
            }
      }

      private void OnDestroy()
      {
            // Reset flag при смене сцены
            SceneManager.sceneLoaded += OnSceneLoaded;
      }

      private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
      {
            environmentCreated = false;
            SceneManager.sceneLoaded -= OnSceneLoaded;
      }
}