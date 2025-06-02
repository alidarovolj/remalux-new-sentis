using UnityEngine;
using UnityEngine.XR.ARFoundation;
using System.Collections.Generic;

/// <summary>
/// Диагностический компонент для отладки ориентации плоскостей согласно техническому отчету
/// Визуализирует нормали, границы ARPlane и проверяет правильность ориентации мешей
/// </summary>
public class PlaneOrientationDebugger : MonoBehaviour
{
      [Header("Настройки отладки")]
      [SerializeField] private bool enableNormalVisualization = true;
      [SerializeField] private bool enableBoundaryVisualization = true;
      [SerializeField] private bool enableMeshOrientationCheck = true;
      [SerializeField] private float normalLength = 0.3f;
      [SerializeField] private Color wallNormalColor = Color.red;
      [SerializeField] private Color floorNormalColor = Color.green;
      [SerializeField] private Color boundaryColor = Color.yellow;

      [Header("Фильтрация")]
      [SerializeField] private bool showOnlyVerticalPlanes = true;
      [SerializeField] private float minPlaneArea = 0.1f;

      [Header("Компоненты")]
      [SerializeField] private ARPlaneManager planeManager;
      [SerializeField] private ARManagerInitializer2 arManager;

      private Dictionary<GameObject, LineRenderer[]> planeNormals = new Dictionary<GameObject, LineRenderer[]>();
      private Dictionary<GameObject, LineRenderer> planeBoundaries = new Dictionary<GameObject, LineRenderer>();

      private void Start()
      {
            if (planeManager == null)
                  planeManager = FindObjectOfType<ARPlaneManager>();

            if (arManager == null)
                  arManager = FindObjectOfType<ARManagerInitializer2>();

            if (planeManager != null)
            {
                  planeManager.planesChanged += OnPlanesChanged;
            }
      }

      private void OnPlanesChanged(ARPlanesChangedEventArgs args)
      {
            // Обрабатываем новые плоскости
            foreach (ARPlane plane in args.added)
            {
                  ProcessPlane(plane.gameObject, plane);
            }

            // Обрабатываем обновленные плоскости
            foreach (ARPlane plane in args.updated)
            {
                  UpdatePlaneVisualization(plane.gameObject, plane);
            }

            // Удаляем визуализацию для удаленных плоскостей
            foreach (ARPlane plane in args.removed)
            {
                  RemovePlaneVisualization(plane.gameObject);
            }
      }

      private void Update()
      {
            if (arManager == null) return;

            // Проверяем сгенерированные плоскости
            var planes = arManager.GeneratedPlanes;
            foreach (GameObject planeObject in planes)
            {
                  if (planeObject != null)
                  {
                        ProcessGeneratedPlane(planeObject);
                  }
            }
      }

      private void ProcessPlane(GameObject planeObject, ARPlane arPlane)
      {
            if (planeObject == null || arPlane == null) return;

            float planeArea = arPlane.size.x * arPlane.size.y;
            if (planeArea < minPlaneArea) return;

            bool isVertical = IsVerticalPlane(arPlane);
            if (showOnlyVerticalPlanes && !isVertical) return;

            // Создаем визуализацию нормали
            if (enableNormalVisualization)
            {
                  CreateNormalVisualization(planeObject, arPlane.center, arPlane.normal, isVertical);
            }

            // Создаем визуализацию границ
            if (enableBoundaryVisualization && arPlane.boundary.Length > 0)
            {
                  CreateBoundaryVisualization(planeObject, arPlane);
            }

            // Проверяем ориентацию меша
            if (enableMeshOrientationCheck)
            {
                  CheckMeshOrientation(planeObject, arPlane);
            }

            Debug.Log($"[PlaneOrientationDebugger] Обработана плоскость: {planeObject.name}, " +
                     $"Вертикальная: {isVertical}, Площадь: {planeArea:F2}м², " +
                     $"Нормаль: {arPlane.normal:F2}, Центр: {arPlane.center:F2}");
      }

      private void ProcessGeneratedPlane(GameObject planeObject)
      {
            if (planeObject == null) return;

            // Для сгенерированных плоскостей используем transform данные
            Vector3 planeNormal = planeObject.transform.forward;
            Vector3 planeCenter = planeObject.transform.position;

            bool isVertical = Mathf.Abs(Vector3.Dot(planeNormal, Vector3.up)) < 0.25f;
            if (showOnlyVerticalPlanes && !isVertical) return;

            // Создаем визуализацию нормали
            if (enableNormalVisualization)
            {
                  CreateNormalVisualization(planeObject, planeCenter, planeNormal, isVertical);
            }

            // Проверяем ориентацию меша для сгенерированных плоскостей
            if (enableMeshOrientationCheck)
            {
                  CheckGeneratedMeshOrientation(planeObject);
            }

            Debug.Log($"[PlaneOrientationDebugger] Обработана сгенерированная плоскость: {planeObject.name}, " +
                     $"Вертикальная: {isVertical}, Нормаль: {planeNormal:F2}, Центр: {planeCenter:F2}");
      }

      private void CreateNormalVisualization(GameObject planeObject, Vector3 center, Vector3 normal, bool isVertical)
      {
            if (planeNormals.ContainsKey(planeObject)) return;

            // Создаем объект для отображения нормали
            GameObject normalViz = new GameObject($"{planeObject.name}_Normal");
            normalViz.transform.SetParent(planeObject.transform);

            LineRenderer lr = normalViz.AddComponent<LineRenderer>();
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startWidth = 0.02f;
            lr.endWidth = 0.02f;
            lr.startColor = isVertical ? wallNormalColor : floorNormalColor;
            lr.endColor = isVertical ? wallNormalColor : floorNormalColor;
            lr.positionCount = 2;

            Vector3 start = center;
            Vector3 end = center + normal * normalLength;

            lr.SetPosition(0, start);
            lr.SetPosition(1, end);

            planeNormals[planeObject] = new LineRenderer[] { lr };
      }

      private void CreateBoundaryVisualization(GameObject planeObject, ARPlane arPlane)
      {
            if (planeBoundaries.ContainsKey(planeObject)) return;

            GameObject boundaryViz = new GameObject($"{planeObject.name}_Boundary");
            boundaryViz.transform.SetParent(planeObject.transform);

            LineRenderer lr = boundaryViz.AddComponent<LineRenderer>();
            lr.material = new Material(Shader.Find("Sprites/Default"));
            lr.startColor = boundaryColor;
            lr.endColor = boundaryColor;
            lr.startWidth = 0.01f;
            lr.endWidth = 0.01f;
            lr.loop = true;
            lr.positionCount = arPlane.boundary.Length;

            // Преобразуем 2D границы в 3D мировые координаты
            for (int i = 0; i < arPlane.boundary.Length; i++)
            {
                  Vector3 localPoint = new Vector3(arPlane.boundary[i].x, 0, arPlane.boundary[i].y);
                  Vector3 worldPoint = arPlane.transform.TransformPoint(localPoint);
                  lr.SetPosition(i, worldPoint);
            }

            planeBoundaries[planeObject] = lr;
      }

      private void CheckMeshOrientation(GameObject planeObject, ARPlane arPlane)
      {
            MeshRenderer renderer = planeObject.GetComponent<MeshRenderer>();
            if (renderer == null) return;

            MeshFilter meshFilter = planeObject.GetComponent<MeshFilter>();
            if (meshFilter?.mesh == null) return;

            // Проверяем направление нормалей меша
            Vector3[] meshNormals = meshFilter.mesh.normals;
            if (meshNormals.Length > 0)
            {
                  Vector3 averageMeshNormal = Vector3.zero;
                  foreach (Vector3 meshNormal in meshNormals)
                  {
                        averageMeshNormal += planeObject.transform.TransformDirection(meshNormal);
                  }
                  averageMeshNormal = (averageMeshNormal / meshNormals.Length).normalized;

                  float alignment = Vector3.Dot(averageMeshNormal, arPlane.normal);
                  bool isProperlyOriented = alignment > 0.7f;

                  string orientationStatus = isProperlyOriented ? "✅ ПРАВИЛЬНО" : "❌ НЕПРАВИЛЬНО";
                  Debug.Log($"[PlaneOrientationDebugger] Ориентация меша {planeObject.name}: {orientationStatus} " +
                           $"(выравнивание: {alignment:F2}, ARPlane.normal: {arPlane.normal:F2}, " +
                           $"средняя нормаль меша: {averageMeshNormal:F2})");

                  if (!isProperlyOriented)
                  {
                        Debug.LogWarning($"[PlaneOrientationDebugger] ⚠️ ПРОБЛЕМА ОРИЕНТАЦИИ: Плоскость {planeObject.name} " +
                                       $"может быть неправильно ориентирована для рендеринга!");
                  }
            }
      }

      private void CheckGeneratedMeshOrientation(GameObject planeObject)
      {
            MeshRenderer renderer = planeObject.GetComponent<MeshRenderer>();
            if (renderer == null) return;

            Vector3 expectedNormal = planeObject.transform.forward;
            Vector3 toCamera = (Camera.main.transform.position - planeObject.transform.position).normalized;

            float cameraAlignment = Vector3.Dot(expectedNormal, toCamera);
            bool facingCamera = cameraAlignment > 0;

            string orientationStatus = facingCamera ? "✅ СМОТРИТ НА КАМЕРУ" : "❌ СМОТРИТ ОТ КАМЕРЫ";
            Debug.Log($"[PlaneOrientationDebugger] Сгенерированная плоскость {planeObject.name}: {orientationStatus} " +
                     $"(выравнивание с камерой: {cameraAlignment:F2})");

            if (!facingCamera)
            {
                  Debug.LogWarning($"[PlaneOrientationDebugger] ⚠️ ВОЗМОЖНАЯ ПРОБЛЕМА: Сгенерированная плоскость {planeObject.name} " +
                                 $"может быть не видна из-за неправильной ориентации!");
            }
      }

      private void UpdatePlaneVisualization(GameObject planeObject, ARPlane arPlane)
      {
            // Обновляем визуализацию нормали
            if (planeNormals.ContainsKey(planeObject) && enableNormalVisualization)
            {
                  LineRenderer lr = planeNormals[planeObject][0];
                  if (lr != null)
                  {
                        Vector3 start = arPlane.center;
                        Vector3 end = arPlane.center + arPlane.normal * normalLength;
                        lr.SetPosition(0, start);
                        lr.SetPosition(1, end);
                  }
            }

            // Обновляем визуализацию границ
            if (planeBoundaries.ContainsKey(planeObject) && enableBoundaryVisualization && arPlane.boundary.Length > 0)
            {
                  LineRenderer lr = planeBoundaries[planeObject];
                  if (lr != null)
                  {
                        lr.positionCount = arPlane.boundary.Length;
                        for (int i = 0; i < arPlane.boundary.Length; i++)
                        {
                              Vector3 localPoint = new Vector3(arPlane.boundary[i].x, 0, arPlane.boundary[i].y);
                              Vector3 worldPoint = arPlane.transform.TransformPoint(localPoint);
                              lr.SetPosition(i, worldPoint);
                        }
                  }
            }
      }

      private void RemovePlaneVisualization(GameObject planeObject)
      {
            if (planeNormals.ContainsKey(planeObject))
            {
                  foreach (LineRenderer lr in planeNormals[planeObject])
                  {
                        if (lr != null) DestroyImmediate(lr.gameObject);
                  }
                  planeNormals.Remove(planeObject);
            }

            if (planeBoundaries.ContainsKey(planeObject))
            {
                  LineRenderer lr = planeBoundaries[planeObject];
                  if (lr != null) DestroyImmediate(lr.gameObject);
                  planeBoundaries.Remove(planeObject);
            }
      }

      private bool IsVerticalPlane(ARPlane plane)
      {
            // Проверка по нормали вместо PlaneAlignment
            float dotUp = Vector3.Dot(plane.normal, Vector3.up);
            return Mathf.Abs(dotUp) < 0.25f;
      }

      private void OnDestroy()
      {
            if (planeManager != null)
            {
                  planeManager.planesChanged -= OnPlanesChanged;
            }

            // Очищаем визуализации
            foreach (var kvp in planeNormals)
            {
                  foreach (LineRenderer lr in kvp.Value)
                  {
                        if (lr != null) DestroyImmediate(lr.gameObject);
                  }
            }

            foreach (var kvp in planeBoundaries)
            {
                  if (kvp.Value != null) DestroyImmediate(kvp.Value.gameObject);
            }
      }

      [ContextMenu("Analyze All Current Planes")]
      public void AnalyzeAllCurrentPlanes()
      {
            Debug.Log("=== АНАЛИЗ ВСЕХ ТЕКУЩИХ ПЛОСКОСТЕЙ ===");

            if (planeManager != null)
            {
                  foreach (ARPlane plane in planeManager.trackables)
                  {
                        ProcessPlane(plane.gameObject, plane);
                  }
            }

            if (arManager?.GeneratedPlanes != null)
            {
                  foreach (GameObject plane in arManager.GeneratedPlanes)
                  {
                        if (plane != null) ProcessGeneratedPlane(plane);
                  }
            }

            Debug.Log("=== АНАЛИЗ ЗАВЕРШЕН ===");
      }
}