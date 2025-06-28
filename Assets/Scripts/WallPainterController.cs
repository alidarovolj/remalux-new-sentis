using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Collections;
using System.Collections.Generic;

/// <summary>
/// Главный контроллер для генерации процедурных плоскостей стен на основе семантической сегментации
/// Использует архитектуру на основе контуров вместо рейкастинга
/// </summary>
[RequireComponent(typeof(ARRaycastManager))]
public class WallPainterController : MonoBehaviour
{
      [Header("Основные компоненты")]
      [SerializeField] private WallSegmentation wallSegmentation;
      [SerializeField] private Camera arCamera;
      [SerializeField] private ARRaycastManager raycastManager;
      [SerializeField] private SurfaceMeasurementSystem surfaceMeasurementSystem;

      [Header("Визуализация")]
      [SerializeField] private GameObject wallPlanePrefab;
      [Tooltip("Материал для покраски стен")]
      [SerializeField] private Material wallPaintMaterial;

      [Header("Параметры сегментации")]
      [Tooltip("Порог уверенности для классификации пикселя как стена")]
      [Range(0.1f, 0.99f)]
      [SerializeField] private float segmentationConfidence = 0.75f;

      [Header("Параметры проецирования")]
      [Tooltip("Максимальное расстояние для рейкастинга")]
      [SerializeField] private float maxRaycastDistance = 5.0f;

      [Tooltip("Минимальная площадь контура в пикселях")]
      [SerializeField] private int minContourArea = 1000;

      [Header("Параметры размера плоскости")]
      [Tooltip("Множитель размера плоскости для компенсации неточностей сегментации")]
      [Range(0.8f, 1.5f)]
      [SerializeField] private float planeSizeMultiplier = 1.0f;

      [Tooltip("Максимальная ширина стены в метрах")]
      [SerializeField] private float maxWallWidth = 10.0f;

      [Tooltip("Максимальная высота стены в метрах")]
      [SerializeField] private float maxWallHeight = 10.0f;

      [Tooltip("Максимальная дистанция для расчета размера плоскости")]
      [SerializeField] private float maxPlaneCalculationDistance = 10.0f;

      [Header("Оптимизация контура")]
      [Tooltip("Допуск упрощения контура (0 = без упрощения)")]
      [Range(0f, 10f)]
      [SerializeField] private float contourSimplificationTolerance = 2.0f;

      [Header("Отладка")]
      [SerializeField] private bool debugMode = false;
      [SerializeField] private bool showContourVisualization = false;
      [SerializeField] private Material debugContourMaterial;

      [Header("Точное измерение размеров")]
      [Tooltip("Использовать систему точного измерения поверхностей")]
      [SerializeField] private bool usePreciseSurfaceMeasurement = true;

      [Tooltip("Автоматически калибровать размеры при обнаружении эталонных объектов")]
      [SerializeField] private bool autoCalibrateSizes = true;

      [Tooltip("Минимальная уверенность измерения для создания плоскости")]
      [Range(0.3f, 0.9f)]
      [SerializeField] private float minMeasurementConfidence = 0.6f;

      // Текущие созданные плоскости
      private List<GameObject> generatedPlanes = new List<GameObject>();
      private GameObject currentWallPlane;

      // Кэш для маски сегментации
      private byte[,] lastSegmentationMask;
      private RenderTexture lastMaskTexture;
      private Coroutine maskUpdateCoroutine;

      // Визуализация отладки
      private LineRenderer debugContourRenderer;

      /// <summary>
      /// Публичный доступ к списку созданных плоскостей (только для чтения)
      /// </summary>
      public List<GameObject> GeneratedPlanes => generatedPlanes;

      private void Awake()
      {
            // Получаем необходимые компоненты
            if (raycastManager == null)
                  raycastManager = GetComponent<ARRaycastManager>();

            if (arCamera == null)
                  arCamera = Camera.main;

            if (wallSegmentation == null)
                  wallSegmentation = FindObjectOfType<WallSegmentation>();

            ValidateComponents();
      }

      private void Start()
      {
            if (wallSegmentation != null)
            {
                  // Подписываемся на обновления маски сегментации
                  wallSegmentation.OnSegmentationMaskUpdated += OnSegmentationMaskUpdated;
                  Debug.Log("[WallPainterController] Подписался на обновления маски сегментации");
            }

            // Создаем компонент для отладочной визуализации
            if (debugMode && showContourVisualization)
            {
                  CreateDebugVisualization();
            }
      }

      private void OnDestroy()
      {
            if (wallSegmentation != null)
            {
                  wallSegmentation.OnSegmentationMaskUpdated -= OnSegmentationMaskUpdated;
            }

            if (maskUpdateCoroutine != null)
            {
                  StopCoroutine(maskUpdateCoroutine);
            }

            ClearAllPlanes();
      }

      private void Update()
      {
            // Обрабатываем касания пользователя
            if (Input.touchCount > 0)
            {
                  Touch touch = Input.GetTouch(0);
                  if (touch.phase == TouchPhase.Began)
                  {
                        HandleTouch(touch.position);
                  }
            }

            // Для отладки в редакторе
#if UNITY_EDITOR
            if (Input.GetMouseButtonDown(0))
            {
                  HandleTouch(Input.mousePosition);
            }
#endif
      }

      /// <summary>
      /// Обработчик обновления маски сегментации
      /// </summary>
      private void OnSegmentationMaskUpdated(RenderTexture maskTexture)
      {
            if (maskTexture == null || !maskTexture.IsCreated())
                  return;

            lastMaskTexture = maskTexture;

            // Запускаем асинхронное преобразование маски
            if (maskUpdateCoroutine != null)
                  StopCoroutine(maskUpdateCoroutine);

            maskUpdateCoroutine = StartCoroutine(UpdateSegmentationMaskAsync(maskTexture));
      }

      /// <summary>
      /// Асинхронное преобразование RenderTexture в byte[,] массив
      /// </summary>
      private IEnumerator UpdateSegmentationMaskAsync(RenderTexture maskTexture)
      {
            yield return new WaitForEndOfFrame();

            // Сохраняем текущую активную RenderTexture
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = maskTexture;

            // Создаем временную Texture2D для чтения пикселей
            Texture2D tempTexture = new Texture2D(maskTexture.width, maskTexture.height, TextureFormat.RGBA32, false);
            tempTexture.ReadPixels(new Rect(0, 0, maskTexture.width, maskTexture.height), 0, 0);
            tempTexture.Apply();

            // Восстанавливаем предыдущую RenderTexture
            RenderTexture.active = previous;

            // Конвертируем в byte[,] массив
            lastSegmentationMask = ConvertTextureToMask(tempTexture);

            // Очищаем временную текстуру
            Destroy(tempTexture);

            if (debugMode)
            {
                  Debug.Log($"[WallPainterController] Маска сегментации обновлена: {maskTexture.width}x{maskTexture.height}");
            }
      }

      /// <summary>
      /// Конвертирует Texture2D в byte[,] маску
      /// </summary>
      private byte[,] ConvertTextureToMask(Texture2D texture)
      {
            int width = texture.width;
            int height = texture.height;
            byte[,] mask = new byte[width, height];

            Color[] pixels = texture.GetPixels();

            for (int y = 0; y < height; y++)
            {
                  for (int x = 0; x < width; x++)
                  {
                        int index = y * width + x;
                        // Используем красный канал как маску стены
                        float wallProbability = pixels[index].r;
                        mask[x, y] = (wallProbability > segmentationConfidence) ? (byte)1 : (byte)0;
                  }
            }

            return mask;
      }

      /// <summary>
      /// Обработка касания экрана пользователем
      /// </summary>
      private void HandleTouch(Vector2 touchPosition)
      {
            if (lastSegmentationMask == null)
            {
                  Debug.LogWarning("[WallPainterController] Маска сегментации еще не готова");
                  return;
            }

            StartCoroutine(ProcessTouchAsync(touchPosition));
      }

      /// <summary>
      /// Асинхронная обработка касания для генерации плоскости
      /// </summary>
      private IEnumerator ProcessTouchAsync(Vector2 touchPosition)
      {
            // Этап 1: Преобразование координат касания в координаты маски
            Vector2Int maskCoords = ConvertScreenToMaskCoords(touchPosition);

            if (debugMode)
            {
                  Debug.Log($"[WallPainterController] Касание в экранных координатах: {touchPosition}, в координатах маски: {maskCoords}");
            }

            // Этап 2: Выполнение Flood Fill для выделения области
            byte[,] filledMask = FloodFill.Execute(lastSegmentationMask, maskCoords.x, maskCoords.y);

            if (filledMask == null)
            {
                  Debug.Log("[WallPainterController] Касание не попало на область стены");
                  yield break;
            }

            // Проверка минимальной площади
            int filledPixels = CountFilledPixels(filledMask);
            if (filledPixels < minContourArea)
            {
                  Debug.Log($"[WallPainterController] Область слишком маленькая: {filledPixels} пикселей (минимум: {minContourArea})");
                  yield break;
            }

            // Этап 3: Извлечение контура с помощью Marching Squares
            List<Vector2> screenContour = MarchingSquares.ExtractContour(filledMask);

            if (screenContour == null || screenContour.Count < 3)
            {
                  Debug.LogError("[WallPainterController] Не удалось извлечь контур");
                  yield break;
            }

            // Упрощение контура если необходимо
            if (contourSimplificationTolerance > 0)
            {
                  screenContour = MarchingSquares.SimplifyContour(screenContour, contourSimplificationTolerance);
                  if (debugMode)
                  {
                        Debug.Log($"[WallPainterController] Контур упрощен до {screenContour.Count} точек");
                  }
            }

            // Преобразование координат маски обратно в экранные координаты
            List<Vector2> screenPoints = ConvertMaskCoordsToScreen(screenContour);

            // Визуализация контура для отладки
            if (debugMode && showContourVisualization)
            {
                  VisualizeContour(screenPoints);
            }

            yield return null; // Даем время на отрисовку

            // Этап 4: Проецирование контура в 3D пространство
            List<Vector3> worldContour = ProjectContourTo3D(screenPoints);

            if (worldContour == null || worldContour.Count < 3)
            {
                  Debug.LogError("[WallPainterController] Не удалось спроецировать контур в 3D");
                  yield break;
            }

            // Этап 5: Триангуляция полигона
            if (!EarClippingTriangulator.ValidatePolygon(worldContour))
            {
                  Debug.LogError("[WallPainterController] Полигон не прошел валидацию");
                  yield break;
            }

            int[] triangles = EarClippingTriangulator.Triangulate(worldContour);

            if (triangles == null || triangles.Length == 0)
            {
                  Debug.LogError("[WallPainterController] Триангуляция не удалась");
                  yield break;
            }

            // Этап 6: Создание и визуализация меша
            CreateWallPlane(worldContour, triangles);

            Debug.Log($"[WallPainterController] Успешно создана плоскость стены с {worldContour.Count} вершинами и {triangles.Length / 3} треугольниками");
      }

      /// <summary>
      /// Проецирует 2D контур в экранных координатах в 3D мировое пространство
      /// </summary>
      private List<Vector3> ProjectContourTo3D(List<Vector2> screenContour)
      {
            List<Vector3> worldPoints = new List<Vector3>();
            List<ARRaycastHit> hits = new List<ARRaycastHit>();

            int successfulHits = 0;
            float totalDistance = 0f;

            // Сначала пробуем найти хотя бы одну точку попадания для определения плоскости
            Vector3? planePoint = null;
            Vector3? planeNormal = null;
            float? planeDistance = null;

            // Пробуем несколько точек из контура для поиска плоскости
            int sampleCount = Mathf.Min(10, screenContour.Count);
            int step = Mathf.Max(1, screenContour.Count / sampleCount);

            for (int i = 0; i < screenContour.Count; i += step)
            {
                  hits.Clear();
                  if (raycastManager.Raycast(screenContour[i], hits, TrackableType.PlaneWithinPolygon | TrackableType.PlaneWithinBounds | TrackableType.Depth))
                  {
                        if (hits.Count > 0)
                        {
                              ARRaycastHit hit = hits[0];
                              planePoint = hit.pose.position;
                              planeNormal = hit.pose.up;
                              planeDistance = Vector3.Distance(arCamera.transform.position, hit.pose.position);

                              if (debugMode)
                              {
                                    Debug.Log($"[WallPainterController] Найдена опорная точка плоскости на расстоянии {planeDistance:F2}м");
                              }
                              break;
                        }
                  }
            }

            // Если не нашли ни одной плоскости, пробуем альтернативный метод
            if (!planePoint.HasValue)
            {
                  if (debugMode)
                  {
                        Debug.LogWarning("[WallPainterController] AR плоскости не найдены. Используем проецирование на фиксированной дистанции");
                  }

                  // Используем фиксированную дистанцию и предполагаем вертикальную стену
                  float defaultDistance = 2.0f; // 2 метра по умолчанию
                  planeDistance = Mathf.Min(defaultDistance, maxRaycastDistance);

                  // Центр экрана
                  Vector2 centerScreen = new Vector2(Screen.width / 2f, Screen.height / 2f);
                  Vector3 centerRay = arCamera.ScreenPointToRay(centerScreen).direction;

                  planePoint = arCamera.transform.position + centerRay * planeDistance.Value;
                  planeNormal = -centerRay; // Нормаль смотрит на камеру

                  if (debugMode)
                  {
                        Debug.Log($"[WallPainterController] Создана виртуальная плоскость на расстоянии {planeDistance:F2}м");
                  }
            }

            // Теперь проецируем все точки контура на найденную плоскость
            foreach (Vector2 screenPoint in screenContour)
            {
                  // Создаем луч из камеры через точку экрана
                  Ray ray = arCamera.ScreenPointToRay(new Vector3(screenPoint.x, screenPoint.y, 0));

                  // Проецируем луч на плоскость
                  float enter;
                  Plane plane = new Plane(planeNormal.Value, planePoint.Value);

                  if (plane.Raycast(ray, out enter))
                  {
                        Vector3 worldPoint = ray.GetPoint(enter);

                        // Проверяем расстояние
                        float distance = Vector3.Distance(arCamera.transform.position, worldPoint);
                        if (distance <= maxRaycastDistance)
                        {
                              worldPoints.Add(worldPoint);
                              successfulHits++;
                              totalDistance += distance;
                        }
                  }
            }

            if (successfulHits > 0)
            {
                  float averageDistance = totalDistance / successfulHits;
                  Debug.Log($"[WallPainterController] Средняя дистанция до стены: {averageDistance:F2}м");
            }

            // Проверяем успешность проецирования с более мягкими требованиями
            float successRate = (float)successfulHits / screenContour.Count;

            if (worldPoints.Count < 3)
            {
                  Debug.LogError($"[WallPainterController] Критически мало точек спроецировано: {worldPoints.Count} из {screenContour.Count}");
                  return null;
            }

            // Снижаем требования для симуляции и сложных сцен
            float minSuccessRate = Application.isEditor ? 0.2f : 0.3f; // 20% в редакторе, 30% на устройстве

            if (successRate < minSuccessRate)
            {
                  if (debugMode)
                  {
                        Debug.LogWarning($"[WallPainterController] Низкий процент успешного проецирования: {successRate:P} ({successfulHits} из {screenContour.Count}), но продолжаем");
                  }
            }
            else
            {
                  if (debugMode)
                  {
                        Debug.Log($"[WallPainterController] Успешное проецирование: {successRate:P} ({successfulHits} из {screenContour.Count})");
                  }
            }

            return worldPoints;
      }

      /// <summary>
      /// Создает GameObject с процедурным мешем стены
      /// </summary>
      private void CreateWallPlane(List<Vector3> vertices, int[] triangles)
      {
            // Удаляем предыдущую плоскость если есть
            if (currentWallPlane != null)
            {
                  Destroy(currentWallPlane);
            }

            // Создаем новый GameObject
            currentWallPlane = wallPlanePrefab != null ?
                Instantiate(wallPlanePrefab) :
                new GameObject("ProceduralWallPlane");

            generatedPlanes.Add(currentWallPlane);

            // Получаем или добавляем необходимые компоненты
            MeshFilter meshFilter = currentWallPlane.GetComponent<MeshFilter>();
            if (meshFilter == null)
                  meshFilter = currentWallPlane.AddComponent<MeshFilter>();

            MeshRenderer meshRenderer = currentWallPlane.GetComponent<MeshRenderer>();
            if (meshRenderer == null)
                  meshRenderer = currentWallPlane.AddComponent<MeshRenderer>();

            MeshCollider meshCollider = currentWallPlane.GetComponent<MeshCollider>();
            if (meshCollider == null)
                  meshCollider = currentWallPlane.AddComponent<MeshCollider>();

            // Создаем процедурный меш
            Mesh mesh = new Mesh();
            mesh.name = "ProceduralWallMesh";

            // Устанавливаем вершины и треугольники
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);

            // Вычисляем нормали и границы
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh.RecalculateTangents();

            // Создаем UV координаты
            Vector2[] uvs = GenerateUVCoordinates(vertices);
            mesh.SetUVs(0, uvs);

            // Применяем меш
            meshFilter.mesh = mesh;
            meshCollider.sharedMesh = mesh;

            // Применяем материал
            if (wallPaintMaterial != null)
            {
                  meshRenderer.material = wallPaintMaterial;
            }

            // Добавляем компонент для интеракции если его нет
            if (currentWallPlane.GetComponent<WallInteraction>() == null)
            {
                  currentWallPlane.AddComponent<WallInteraction>();
            }

            // Вычисляем границы меша
            Bounds meshBounds = new Bounds(vertices[0], Vector3.zero);
            foreach (Vector3 vertex in vertices)
            {
                  meshBounds.Encapsulate(vertex);
            }

            // Проверяем размеры плоскости
            float wallWidth = meshBounds.size.x;
            float wallHeight = meshBounds.size.y;
            Vector3 center = meshBounds.center;

            // Применяем множитель размера если нужно
            if (planeSizeMultiplier != 1.0f && planeSizeMultiplier > 0)
            {
                  // Масштабируем вершины относительно центра
                  for (int i = 0; i < vertices.Count; i++)
                  {
                        Vector3 dir = vertices[i] - center;
                        vertices[i] = center + dir * planeSizeMultiplier;
                  }

                  wallWidth *= planeSizeMultiplier;
                  wallHeight *= planeSizeMultiplier;
            }

            // Проверяем максимальные размеры
            if (wallWidth > maxWallWidth || wallHeight > maxWallHeight)
            {
                  Debug.LogWarning($"[WallPainterController] Плоскость слишком большая: {wallWidth:F2}м x {wallHeight:F2}м. " +
                                  $"Лимит: {maxWallWidth}м x {maxWallHeight}m");

                  // Опционально: масштабируем до максимального размера
                  float scaleDown = Mathf.Min(maxWallWidth / wallWidth, maxWallHeight / wallHeight);
                  if (scaleDown < 1.0f)
                  {
                        for (int i = 0; i < vertices.Count; i++)
                        {
                              Vector3 dir = vertices[i] - center;
                              vertices[i] = center + dir * scaleDown;
                        }
                        wallWidth *= scaleDown;
                        wallHeight *= scaleDown;
                  }
            }

            // Логируем финальные размеры
            Debug.Log($"[WallPainterController] Создана плоскость стены - размеры: {wallWidth:F2}м x {wallHeight:F2}м " +
                      $"(множитель: {planeSizeMultiplier:F2}x)");
      }

      /// <summary>
      /// Генерирует UV координаты для меша
      /// </summary>
      private Vector2[] GenerateUVCoordinates(List<Vector3> vertices)
      {
            if (vertices.Count < 3)
                  return new Vector2[0];

            Vector2[] uvs = new Vector2[vertices.Count];

            // Находим границы меша
            Bounds bounds = new Bounds(vertices[0], Vector3.zero);
            foreach (Vector3 vertex in vertices)
            {
                  bounds.Encapsulate(vertex);
            }

            // Определяем плоскость проекции на основе нормали
            Vector3 normal = CalculatePlaneNormal(vertices);

            // Проецируем вершины на 2D и нормализуем
            for (int i = 0; i < vertices.Count; i++)
            {
                  Vector3 localPos = vertices[i] - bounds.center;

                  // Выбираем оси проекции на основе нормали
                  Vector2 uv;
                  if (Mathf.Abs(normal.y) > 0.5f)
                  {
                        // Горизонтальная плоскость
                        uv = new Vector2(
                            (localPos.x + bounds.extents.x) / (bounds.size.x > 0 ? bounds.size.x : 1),
                            (localPos.z + bounds.extents.z) / (bounds.size.z > 0 ? bounds.size.z : 1)
                        );
                  }
                  else if (Mathf.Abs(normal.x) > Mathf.Abs(normal.z))
                  {
                        // Вертикальная плоскость YZ
                        uv = new Vector2(
                            (localPos.z + bounds.extents.z) / (bounds.size.z > 0 ? bounds.size.z : 1),
                            (localPos.y + bounds.extents.y) / (bounds.size.y > 0 ? bounds.size.y : 1)
                        );
                  }
                  else
                  {
                        // Вертикальная плоскость XY
                        uv = new Vector2(
                            (localPos.x + bounds.extents.x) / (bounds.size.x > 0 ? bounds.size.x : 1),
                            (localPos.y + bounds.extents.y) / (bounds.size.y > 0 ? bounds.size.y : 1)
                        );
                  }

                  uvs[i] = uv;
            }

            return uvs;
      }

      /// <summary>
      /// Вычисляет нормаль плоскости по вершинам
      /// </summary>
      private Vector3 CalculatePlaneNormal(List<Vector3> vertices)
      {
            if (vertices.Count < 3)
                  return Vector3.up;

            // Используем первые три невырожденные точки
            for (int i = 0; i < vertices.Count - 2; i++)
            {
                  Vector3 v1 = vertices[i + 1] - vertices[i];
                  Vector3 v2 = vertices[i + 2] - vertices[i];
                  Vector3 normal = Vector3.Cross(v1, v2);

                  if (normal.magnitude > 0.001f)
                  {
                        return normal.normalized;
                  }
            }

            return Vector3.up;
      }

      /// <summary>
      /// Преобразует экранные координаты в координаты маски сегментации
      /// </summary>
      private Vector2Int ConvertScreenToMaskCoords(Vector2 screenPos)
      {
            if (lastSegmentationMask == null)
                  return Vector2Int.zero;

            int maskWidth = lastSegmentationMask.GetLength(0);
            int maskHeight = lastSegmentationMask.GetLength(1);

            // Нормализуем экранные координаты
            float normalizedX = screenPos.x / Screen.width;
            float normalizedY = screenPos.y / Screen.height;

            // Преобразуем в координаты маски
            int x = Mathf.Clamp((int)(normalizedX * maskWidth), 0, maskWidth - 1);
            int y = Mathf.Clamp((int)(normalizedY * maskHeight), 0, maskHeight - 1);

            return new Vector2Int(x, y);
      }

      /// <summary>
      /// Преобразует координаты маски обратно в экранные координаты
      /// </summary>
      private List<Vector2> ConvertMaskCoordsToScreen(List<Vector2> maskCoords)
      {
            if (lastSegmentationMask == null)
                  return new List<Vector2>();

            int maskWidth = lastSegmentationMask.GetLength(0);
            int maskHeight = lastSegmentationMask.GetLength(1);

            List<Vector2> screenCoords = new List<Vector2>(maskCoords.Count);

            foreach (Vector2 maskCoord in maskCoords)
            {
                  float screenX = (maskCoord.x / maskWidth) * Screen.width;
                  float screenY = (maskCoord.y / maskHeight) * Screen.height;
                  screenCoords.Add(new Vector2(screenX, screenY));
            }

            return screenCoords;
      }

      /// <summary>
      /// Подсчитывает количество заполненных пикселей в маске
      /// </summary>
      private int CountFilledPixels(byte[,] mask)
      {
            int count = 0;
            int width = mask.GetLength(0);
            int height = mask.GetLength(1);

            for (int x = 0; x < width; x++)
            {
                  for (int y = 0; y < height; y++)
                  {
                        if (mask[x, y] == 1)
                              count++;
                  }
            }

            return count;
      }

      /// <summary>
      /// Удаляет все созданные плоскости
      /// </summary>
      public void ClearAllPlanes()
      {
            foreach (GameObject plane in generatedPlanes)
            {
                  if (plane != null)
                        Destroy(plane);
            }
            generatedPlanes.Clear();
            currentWallPlane = null;
      }

      /// <summary>
      /// Проверка наличия всех необходимых компонентов
      /// </summary>
      private void ValidateComponents()
      {
            if (raycastManager == null)
            {
                  Debug.LogError("[WallPainterController] ARRaycastManager не найден!");
                  enabled = false;
                  return;
            }

            if (arCamera == null)
            {
                  Debug.LogError("[WallPainterController] AR Camera не найдена!");
                  enabled = false;
                  return;
            }

            if (wallSegmentation == null)
            {
                  Debug.LogWarning("[WallPainterController] WallSegmentation не найден. Функционал будет ограничен.");
            }

            // Проверяем систему точного измерения
            if (usePreciseSurfaceMeasurement && surfaceMeasurementSystem == null)
            {
                  surfaceMeasurementSystem = FindObjectOfType<SurfaceMeasurementSystem>();
                  if (surfaceMeasurementSystem == null)
                  {
                        Debug.LogWarning("[WallPainterController] SurfaceMeasurementSystem не найден. Создается автоматически...");
                        var measurementGO = new GameObject("SurfaceMeasurementSystem");
                        surfaceMeasurementSystem = measurementGO.AddComponent<SurfaceMeasurementSystem>();
                  }
            }

            if (wallPlanePrefab == null)
            {
                  Debug.LogWarning("[WallPainterController] Префаб плоскости не назначен. Будет создан базовый GameObject.");
            }
      }

      #region Debug Visualization

      private void CreateDebugVisualization()
      {
            GameObject debugObj = new GameObject("DebugContourVisualizer");
            debugObj.transform.SetParent(transform);
            debugContourRenderer = debugObj.AddComponent<LineRenderer>();

            if (debugContourMaterial != null)
            {
                  debugContourRenderer.material = debugContourMaterial;
            }
            else
            {
                  debugContourRenderer.material = new Material(Shader.Find("Sprites/Default"));
                  debugContourRenderer.material.color = Color.green;
            }

            debugContourRenderer.startWidth = 0.01f;
            debugContourRenderer.endWidth = 0.01f;
            debugContourRenderer.enabled = false;
      }

      private void VisualizeContour(List<Vector2> screenPoints)
      {
            if (debugContourRenderer == null)
                  return;

            debugContourRenderer.positionCount = screenPoints.Count + 1;

            for (int i = 0; i < screenPoints.Count; i++)
            {
                  Vector3 worldPoint = arCamera.ScreenToWorldPoint(new Vector3(screenPoints[i].x, screenPoints[i].y, 0.5f));
                  debugContourRenderer.SetPosition(i, worldPoint);
            }

            // Замыкаем контур
            if (screenPoints.Count > 0)
            {
                  Vector3 firstPoint = arCamera.ScreenToWorldPoint(new Vector3(screenPoints[0].x, screenPoints[0].y, 0.5f));
                  debugContourRenderer.SetPosition(screenPoints.Count, firstPoint);
            }

            debugContourRenderer.enabled = true;

            // Скрываем через несколько секунд
            StartCoroutine(HideDebugVisualization(3f));
      }

      private IEnumerator HideDebugVisualization(float delay)
      {
            yield return new WaitForSeconds(delay);

            if (debugContourRenderer != null)
            {
                  debugContourRenderer.enabled = false;
            }
      }

      #endregion
}

/// <summary>
/// Компонент для взаимодействия с плоскостью стены
/// </summary>
public class WallInteraction : MonoBehaviour
{
      private MeshRenderer meshRenderer;

      void Start()
      {
            meshRenderer = GetComponent<MeshRenderer>();
      }

      public void SetColor(Color color)
      {
            if (meshRenderer != null && meshRenderer.material != null)
            {
                  meshRenderer.material.color = color;
            }
      }

      public void SetTexture(Texture texture)
      {
            if (meshRenderer != null && meshRenderer.material != null)
            {
                  meshRenderer.material.mainTexture = texture;
            }
      }
}

// Временные встроенные алгоритмы (позже замените на отдельные файлы из папки Algorithms)
public static class FloodFill
{
      public static byte[,] Execute(byte[,] mask, int startX, int startY)
      {
            int width = mask.GetLength(0);
            int height = mask.GetLength(1);

            if (startX < 0 || startX >= width || startY < 0 || startY >= height)
                  return null;

            if (mask[startX, startY] == 0)
                  return null;

            byte[,] filledMask = new byte[width, height];
            Queue<Vector2Int> queue = new Queue<Vector2Int>();
            queue.Enqueue(new Vector2Int(startX, startY));
            filledMask[startX, startY] = 1;

            Vector2Int[] directions = {
            new Vector2Int(0, 1),
            new Vector2Int(1, 0),
            new Vector2Int(0, -1),
            new Vector2Int(-1, 0)
        };

            while (queue.Count > 0)
            {
                  Vector2Int current = queue.Dequeue();

                  foreach (var dir in directions)
                  {
                        Vector2Int neighbor = current + dir;

                        if (neighbor.x >= 0 && neighbor.x < width &&
                            neighbor.y >= 0 && neighbor.y < height)
                        {
                              if (mask[neighbor.x, neighbor.y] == 1 && filledMask[neighbor.x, neighbor.y] == 0)
                              {
                                    filledMask[neighbor.x, neighbor.y] = 1;
                                    queue.Enqueue(neighbor);
                              }
                        }
                  }
            }

            return filledMask;
      }
}

public static class MarchingSquares
{
      public static List<Vector2> ExtractContour(byte[,] mask)
      {
            int width = mask.GetLength(0);
            int height = mask.GetLength(1);

            // Найти начальную точку
            Vector2Int startPoint = new Vector2Int(-1, -1);
            for (int y = 0; y < height; y++)
            {
                  for (int x = 0; x < width; x++)
                  {
                        if (mask[x, y] == 1)
                        {
                              startPoint = new Vector2Int(x, y);
                              break;
                        }
                  }
                  if (startPoint.x != -1) break;
            }

            if (startPoint.x == -1) return null;

            List<Vector2> contour = new List<Vector2>();

            // Простое обведение контура
            Vector2Int current = startPoint;
            HashSet<Vector2Int> visited = new HashSet<Vector2Int>();
            bool found = true;

            do
            {
                  contour.Add(new Vector2(current.x, current.y));
                  visited.Add(current);

                  // Поиск следующей точки контура
                  found = false;
                  for (int dx = -1; dx <= 1 && !found; dx++)
                  {
                        for (int dy = -1; dy <= 1 && !found; dy++)
                        {
                              if (dx == 0 && dy == 0) continue;

                              Vector2Int next = current + new Vector2Int(dx, dy);
                              if (next.x >= 0 && next.x < width && next.y >= 0 && next.y < height &&
                                  mask[next.x, next.y] == 1 && !visited.Contains(next))
                              {
                                    current = next;
                                    found = true;
                              }
                        }
                  }

                  if (!found) break;

            } while (found && current != startPoint && contour.Count < width * height);

            return contour;
      }

      public static List<Vector2> SimplifyContour(List<Vector2> contour, float tolerance)
      {
            if (contour == null || contour.Count < 3) return contour;
            // Простое прореживание точек
            List<Vector2> simplified = new List<Vector2>();
            simplified.Add(contour[0]);

            for (int i = 1; i < contour.Count - 1; i += Mathf.Max(1, (int)tolerance))
            {
                  simplified.Add(contour[i]);
            }

            simplified.Add(contour[contour.Count - 1]);
            return simplified;
      }
}

public static class EarClippingTriangulator
{
      public static bool ValidatePolygon(List<Vector3> vertices)
      {
            return vertices != null && vertices.Count >= 3;
      }

      public static int[] Triangulate(List<Vector3> vertices)
      {
            if (vertices == null || vertices.Count < 3)
                  return null;

            if (vertices.Count == 3)
                  return new int[] { 0, 1, 2 };

            List<int> indices = new List<int>();
            List<int> activeVertices = new List<int>();

            for (int i = 0; i < vertices.Count; i++)
            {
                  activeVertices.Add(i);
            }

            while (activeVertices.Count > 3)
            {
                  bool earFound = false;

                  for (int i = 0; i < activeVertices.Count; i++)
                  {
                        int prev = (i == 0) ? activeVertices.Count - 1 : i - 1;
                        int next = (i + 1) % activeVertices.Count;

                        // Простая проверка на "ухо"
                        indices.Add(activeVertices[prev]);
                        indices.Add(activeVertices[i]);
                        indices.Add(activeVertices[next]);

                        activeVertices.RemoveAt(i);
                        earFound = true;
                        break;
                  }

                  if (!earFound) break;
            }

            // Добавляем последний треугольник
            if (activeVertices.Count == 3)
            {
                  indices.Add(activeVertices[0]);
                  indices.Add(activeVertices[1]);
                  indices.Add(activeVertices[2]);
            }

            return indices.ToArray();
      }
}