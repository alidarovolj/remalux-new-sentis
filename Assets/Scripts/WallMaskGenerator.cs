using UnityEngine;
using UnityEngine.XR.ARFoundation;
using System.Collections.Generic;
using System.Collections;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Генерирует текстуру маски стен, используя информацию о плоскостях AR.
/// Эта маска может быть использована для улучшения точности покраски стен.
/// </summary>
public class WallMaskGenerator : MonoBehaviour
{
      [SerializeField] private ARPlaneManager planeManager;
      [SerializeField] private Camera arCamera;
      [SerializeField] private int textureWidth = 512;
      [SerializeField] private int textureHeight = 512;
      [SerializeField] private float updateInterval = 0.5f; // Интервал обновления в секундах
      [SerializeField] private Material wallPaintMaterial; // Материал, которому будем назначать текстуру
      [SerializeField] private string maskTexturePropertyName = "_WallMaskTexture"; // Имя свойства в шейдере

      private RenderTexture wallMaskTexture;
      private List<ARPlane> verticalPlanes = new List<ARPlane>();
      private Coroutine updateCoroutine;
      private bool isInitialized = false;

      private void Awake()
      {
            // Находим необходимые компоненты, если они не назначены
            if (planeManager == null) planeManager = FindObjectOfType<ARPlaneManager>();
            if (arCamera == null) arCamera = Camera.main;
      }

      private void Start()
      {
            // Инициализируем текстуру маски
            InitializeWallMaskTexture();

            // Подписываемся на события изменения плоскостей
            if (planeManager != null)
            {
                  planeManager.planesChanged += OnPlanesChanged;

                  // Запускаем корутину обновления маски
                  updateCoroutine = StartCoroutine(UpdateWallMaskRoutine());
            }
            else
            {
                  Debug.LogError("WallMaskGenerator: ARPlaneManager не найден!");
                  enabled = false;
            }
      }

      private void OnDestroy()
      {
            // Отписываемся от событий при уничтожении объекта
            if (planeManager != null)
            {
                  planeManager.planesChanged -= OnPlanesChanged;
            }

            // Останавливаем корутину
            if (updateCoroutine != null)
            {
                  StopCoroutine(updateCoroutine);
            }

            // Освобождаем ресурсы
            if (wallMaskTexture != null)
            {
                  wallMaskTexture.Release();
                  Destroy(wallMaskTexture);
            }
      }

      private void InitializeWallMaskTexture()
      {
            // Создаем RenderTexture с заданными размерами
            wallMaskTexture = new RenderTexture(textureWidth, textureHeight, 0, RenderTextureFormat.R8);
            wallMaskTexture.name = "WallMaskTexture";
            wallMaskTexture.filterMode = FilterMode.Bilinear;
            wallMaskTexture.wrapMode = TextureWrapMode.Clamp;
            wallMaskTexture.antiAliasing = 1;
            wallMaskTexture.Create();

            // Очищаем текстуру
            ClearWallMaskTexture();

            // Назначаем текстуру материалу
            if (wallPaintMaterial != null && wallPaintMaterial.HasProperty(maskTexturePropertyName))
            {
                  wallPaintMaterial.SetTexture(maskTexturePropertyName, wallMaskTexture);
                  Debug.Log($"WallMaskGenerator: Текстура маски создана и назначена материалу {wallPaintMaterial.name}");
            }
            else
            {
                  Debug.LogWarning("WallMaskGenerator: Материал не назначен или не содержит свойство " + maskTexturePropertyName);
            }

            isInitialized = true;
      }

      // Очищаем текстуру маски (заполняем черным цветом)
      private void ClearWallMaskTexture()
      {
            if (wallMaskTexture == null) return;

            // Сохраняем текущую активную RenderTexture
            RenderTexture prevRT = RenderTexture.active;

            // Назначаем нашу текстуру как активную
            RenderTexture.active = wallMaskTexture;

            // Очищаем её черным цветом
            GL.Clear(true, true, Color.black);

            // Восстанавливаем предыдущую активную текстуру
            RenderTexture.active = prevRT;
      }

      // Обработчик события изменения плоскостей
      private void OnPlanesChanged(ARPlanesChangedEventArgs args)
      {
            // Обновляем список вертикальных плоскостей
            UpdateVerticalPlanesList();
      }

      // Обновляем список вертикальных плоскостей
      private void UpdateVerticalPlanesList()
      {
            verticalPlanes.Clear();

            ARPlane[] allPlanes = FindObjectsOfType<ARPlane>();
            foreach (ARPlane plane in allPlanes)
            {
                  if (plane.alignment == PlaneAlignment.Vertical)
                  {
                        verticalPlanes.Add(plane);
                  }
            }

            Debug.Log($"WallMaskGenerator: Обновлен список вертикальных плоскостей, найдено {verticalPlanes.Count}");
      }

      // Корутина для периодического обновления маски стен
      private IEnumerator UpdateWallMaskRoutine()
      {
            while (true)
            {
                  if (isInitialized && verticalPlanes.Count > 0)
                  {
                        UpdateWallMaskTexture();
                  }

                  yield return new WaitForSeconds(updateInterval);
            }
      }

      // Обновляем текстуру маски стен
      private void UpdateWallMaskTexture()
      {
            if (wallMaskTexture == null || arCamera == null) return;

            // Сохраняем текущую активную RenderTexture
            RenderTexture prevRT = RenderTexture.active;

            // Назначаем нашу текстуру как активную
            RenderTexture.active = wallMaskTexture;

            // Очищаем её черным цветом перед рисованием
            GL.Clear(true, true, Color.black);

            // Начинаем новую GL группу
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, textureWidth, textureHeight, 0);

            // Рисуем все вертикальные плоскости на текстуре маски
            foreach (ARPlane plane in verticalPlanes)
            {
                  DrawPlaneOnMask(plane);
            }

            // Завершаем GL группу
            GL.PopMatrix();

            // Восстанавливаем предыдущую активную текстуру
            RenderTexture.active = prevRT;
      }

      // Рисуем плоскость на маске стен
      private void DrawPlaneOnMask(ARPlane plane)
      {
            // Получаем вершины границы плоскости
            Vector3[] boundaryPoints = new Vector3[plane.boundary.Length];
            for (int i = 0; i < plane.boundary.Length; i++)
            {
                  // Преобразуем 2D точку границы в 3D мировое пространство
                  Vector3 vertexInPlaneSpace = new Vector3(plane.boundary[i].x, 0, plane.boundary[i].y);
                  Vector3 vertexInWorldSpace = plane.transform.TransformPoint(vertexInPlaneSpace);
                  boundaryPoints[i] = vertexInWorldSpace;
            }

            // Проецируем вершины на экранное пространство
            Vector2[] screenPoints = new Vector2[boundaryPoints.Length];
            for (int i = 0; i < boundaryPoints.Length; i++)
            {
                  Vector3 screenPoint = arCamera.WorldToScreenPoint(boundaryPoints[i]);
                  // Преобразуем экранные координаты в координаты текстуры
                  screenPoints[i] = new Vector2(
                      screenPoint.x / Screen.width * textureWidth,
                      screenPoint.y / Screen.height * textureHeight
                  );
            }

            // Используем простой белый материал для рисования
            Material lineMaterial = new Material(Shader.Find("Hidden/Internal-Colored"));
            lineMaterial.hideFlags = HideFlags.HideAndDontSave;
            lineMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            lineMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            lineMaterial.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            lineMaterial.SetInt("_ZWrite", 0);

            // Применяем материал
            lineMaterial.SetPass(0);

            // Рисуем заполненный полигон (используя треугольники)
            GL.Begin(GL.TRIANGLES);
            GL.Color(Color.white); // Белый цвет для маски

            // Триангулируем многоугольник (веерная триангуляция - работает только для выпуклых многоугольников)
            for (int i = 1; i < screenPoints.Length - 1; i++)
            {
                  GL.Vertex(new Vector3(screenPoints[0].x, screenPoints[0].y, 0));
                  GL.Vertex(new Vector3(screenPoints[i].x, screenPoints[i].y, 0));
                  GL.Vertex(new Vector3(screenPoints[i + 1].x, screenPoints[i + 1].y, 0));
            }

            GL.End();

            // Уничтожаем временный материал
            if (Application.isPlaying)
            {
                  Destroy(lineMaterial);
            }
            else
            {
                  DestroyImmediate(lineMaterial);
            }
      }

      // Публичный метод для получения текстуры маски стен
      public RenderTexture GetWallMaskTexture()
      {
            return wallMaskTexture;
      }

      // Публичный метод для принудительного обновления маски
      public void ForceUpdateWallMask()
      {
            if (!isInitialized)
            {
                  InitializeWallMaskTexture();
            }

            UpdateVerticalPlanesList();
            UpdateWallMaskTexture();
      }

      // Публичный метод для ручного добавления материала для использования маски
      public void AddMaterialForMasking(Material material)
      {
            if (material != null && wallMaskTexture != null && material.HasProperty(maskTexturePropertyName))
            {
                  material.SetTexture(maskTexturePropertyName, wallMaskTexture);
                  Debug.Log($"WallMaskGenerator: Текстура маски назначена материалу {material.name}");
            }
      }
}