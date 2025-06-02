using UnityEngine;
using System.Collections;

/// <summary>
/// Простой скрипт для принудительного крепления плоскостей к стенам
/// </summary>
public class WallPlaneAttacher : MonoBehaviour
{
      [Header("Настройки крепления к стенам")]
      [SerializeField] private bool enableWallAttachment = true;
      [SerializeField] private LayerMask wallLayerMask = 1; // Default layer
      [SerializeField] private float maxRayDistance = 10f;
      [SerializeField] private Material temporaryWallMaterial;

      private void Start()
      {
            if (enableWallAttachment)
            {
                  StartCoroutine(CheckAndCreateWalls());
            }
      }

      private IEnumerator CheckAndCreateWalls()
      {
            yield return new WaitForSeconds(2f); // Ждём инициализации

            // Проверяем есть ли коллайдеры в сцене
            Collider[] allColliders = FindObjectsOfType<Collider>();
            Debug.Log($"[WallPlaneAttacher] Найдено коллайдеров: {allColliders.Length}");

            if (allColliders.Length == 0)
            {
                  Debug.LogWarning("[WallPlaneAttacher] Коллайдеры отсутствуют! Создаём временные стены...");
                  CreateTemporaryWalls();
            }
            else
            {
                  Debug.Log("[WallPlaneAttacher] Коллайдеры найдены, рейкасты должны работать");
            }
      }

      private void CreateTemporaryWalls()
      {
            Camera mainCamera = Camera.main;
            if (mainCamera == null) return;

            Vector3 cameraPos = mainCamera.transform.position;
            Vector3 forward = mainCamera.transform.forward;
            Vector3 right = mainCamera.transform.right;

            // Создаём 4 стены вокруг камеры
            CreateWall("TempWall_Front", cameraPos + forward * 3f, Quaternion.LookRotation(-forward));
            CreateWall("TempWall_Back", cameraPos - forward * 3f, Quaternion.LookRotation(forward));
            CreateWall("TempWall_Left", cameraPos - right * 3f, Quaternion.LookRotation(right));
            CreateWall("TempWall_Right", cameraPos + right * 3f, Quaternion.LookRotation(-right));

            Debug.Log("[WallPlaneAttacher] ✅ Созданы 4 временные стены для рейкастов");
      }

      private void CreateWall(string name, Vector3 position, Quaternion rotation)
      {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = name;
            wall.transform.position = position;
            wall.transform.rotation = rotation;
            wall.transform.localScale = new Vector3(4f, 3f, 0.1f); // Широкая тонкая стена

            // Настройка коллайдера
            BoxCollider collider = wall.GetComponent<BoxCollider>();
            if (collider != null)
            {
                  collider.material = null;
            }

            // Настройка визуала
            MeshRenderer renderer = wall.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                  if (temporaryWallMaterial != null)
                  {
                        renderer.material = temporaryWallMaterial;
                  }
                  else
                  {
                        Material mat = new Material(Shader.Find("Standard"));
                        mat.color = new Color(1, 0, 0, 0.2f); // Полупрозрачный красный
                        renderer.material = mat;
                  }

                  // Скрываем стену через 5 секунд
                  StartCoroutine(HideWallAfterDelay(renderer, 5f));
            }

            // Устанавливаем слой для рейкастов
            wall.layer = 0; // Default layer
      }

      private IEnumerator HideWallAfterDelay(MeshRenderer renderer, float delay)
      {
            yield return new WaitForSeconds(delay);

            if (renderer != null)
            {
                  renderer.enabled = false;
                  Debug.Log($"[WallPlaneAttacher] Скрыта временная стена: {renderer.gameObject.name}");
            }
      }
}