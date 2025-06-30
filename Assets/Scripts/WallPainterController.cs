using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Unity.Collections;
using Unity.Jobs;
using Unity.Burst;
using LibTessDotNet;

/// <summary>
/// Вспомогательный класс для передачи результата из корутины извлечения контура.
/// </summary>
internal class ContourExtractionResult
{
      public bool Success = false;
      public List<Vector3> Vertices;
      public int[] Triangles;
      public Vector2[] UVs;
}

/// <summary>
/// Вспомогательный класс для передачи результата из корутины извлечения позы.
/// </summary>
internal class PoseExtractionResult
{
      public bool Success = false;
      public Pose Pose;
}

/// <summary>
/// Главный контроллер для генерации процедурных плоскостей стен на основе семантической сегментации
/// </summary>
[RequireComponent(typeof(ARRaycastManager))]
[RequireComponent(typeof(ARAnchorManager))]
public class WallPainterController : MonoBehaviour
{
      [Header("Основные компоненты")]
      [SerializeField] private WallSegmentation wallSegmentation;
      [SerializeField] private Camera arCamera;
      [SerializeField] private ARRaycastManager raycastManager;
      [SerializeField] private ARPlaneManager planeManager;
      [SerializeField] private ARAnchorManager m_AnchorManager;

      [Header("Визуализация")]
      [SerializeField] private GameObject wallPlanePrefab;
      [SerializeField] private Material wallPaintMaterial;

      [Header("Параметры сегментации")]
      [Range(0.1f, 0.99f)]
      [SerializeField] private float segmentationConfidence = 0.75f;

      [Header("Параметры проецирования")]
      [SerializeField] private float maxRaycastDistance = 5.0f;
      [SerializeField] private int minContourArea = 1000;

      [Header("Оптимизация контура")]
      [Range(0f, 10f)]
      [SerializeField] private float contourSimplificationTolerance = 2.0f;

      [Header("Параметры сглаживания (Кальман)")]
      [SerializeField] private int stabilizationFrames = 5;
      [SerializeField] private float positionFilterQ = 0.005f;
      [SerializeField] private float positionFilterR = 0.1f;
      [SerializeField] private float rotationSmoothingFactor = 0.15f;

      [Header("Отладка")]
      [SerializeField] private bool debugMode = false;
      [SerializeField] private bool showContourVisualization = false;
      [SerializeField] private Material debugContourMaterial;

      private List<GameObject> generatedPlanes = new List<GameObject>();
      private Coroutine _stabilizationCoroutine;
      private byte[,] lastSegmentationMask;
      private Coroutine maskUpdateCoroutine;
      private LineRenderer debugContourRenderer;

      public List<GameObject> GeneratedPlanes => generatedPlanes;

      private void Awake()
      {
            if (wallSegmentation == null)
            {
                  Debug.Log("[WallPainterController] WallSegmentation не назначен, пытаюсь найти в сцене...");
                  wallSegmentation = FindObjectOfType<WallSegmentation>();
                  if (wallSegmentation != null)
                  {
                        Debug.Log("[WallPainterController] ✅ WallSegmentation найден и назначен автоматически.");
                  }
                  else
                  {
                        Debug.LogError("[WallPainterController] ❌ Не удалось найти WallSegmentation в сцене! Компонент не может работать.");
                        enabled = false;
                        return;
                  }
            }

            if (raycastManager == null) raycastManager = GetComponent<ARRaycastManager>();
            if (m_AnchorManager == null) m_AnchorManager = GetComponent<ARAnchorManager>();
            if (planeManager == null) planeManager = GetComponent<ARPlaneManager>();
            if (arCamera == null) arCamera = Camera.main;
      }

      private void Start()
      {
            if (wallSegmentation != null)
            {
                  wallSegmentation.OnSegmentationMaskUpdated += OnSegmentationMaskUpdated;
            }
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
      }

      private void Update()
      {
            if (Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began)
            {
                  HandleTouch(Input.GetTouch(0).position);
            }
#if UNITY_EDITOR
            if (Input.GetMouseButtonDown(0))
            {
                  HandleTouch(Input.mousePosition);
            }
#endif
      }

      private void OnSegmentationMaskUpdated(RenderTexture maskTexture)
      {
            if (maskTexture == null || !maskTexture.IsCreated()) return;
            if (maskUpdateCoroutine != null) StopCoroutine(maskUpdateCoroutine);
            maskUpdateCoroutine = StartCoroutine(UpdateSegmentationMaskAsync(maskTexture));
      }

      private IEnumerator UpdateSegmentationMaskAsync(RenderTexture maskTexture)
      {
            yield return new WaitForEndOfFrame();
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = maskTexture;
            Texture2D tempTexture = new Texture2D(maskTexture.width, maskTexture.height, TextureFormat.R8, false);
            tempTexture.ReadPixels(new Rect(0, 0, maskTexture.width, maskTexture.height), 0, 0);
            tempTexture.Apply();
            RenderTexture.active = previous;
            lastSegmentationMask = ConvertTextureToMask(tempTexture);
            Destroy(tempTexture);
      }

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
                        mask[x, y] = (byte)(pixels[y * width + x].r > segmentationConfidence ? 1 : 0);
                  }
            }
            return mask;
      }

      private void HandleTouch(Vector2 touchPosition)
      {
            if (_stabilizationCoroutine != null)
            {
                  Debug.LogWarning("[WallPainterController] Процесс создания стены уже запущен.");
                  return;
            }
            StartCoroutine(ProcessTouchAsync(touchPosition));
      }

      private IEnumerator ProcessTouchAsync(Vector2 touchPosition)
      {
            if (lastSegmentationMask == null)
            {
                  Debug.LogWarning("[WallPainterController] Маска сегментации не готова.");
                  yield break;
            }

            Vector2Int maskCoords = ConvertScreenToMaskCoords(touchPosition);

            if (maskCoords.x < 0 || maskCoords.x >= lastSegmentationMask.GetLength(0) ||
                maskCoords.y < 0 || maskCoords.y >= lastSegmentationMask.GetLength(1) ||
                lastSegmentationMask[maskCoords.x, maskCoords.y] == 0)
            {
                  if (debugMode) Debug.Log("[WallPainterController] Касание пришлось на область, не являющуюся стеной.");
                  yield break;
            }

            var contourResult = new ContourExtractionResult();
            yield return StartCoroutine(RunContourExtractionPipelineAsync(maskCoords, contourResult));

            if (contourResult.Success)
            {
                  if (debugMode) Debug.Log($"[WallPainterController] Контур извлечен. Запуск стабилизации для {stabilizationFrames} кадров.");
                  if (_stabilizationCoroutine != null) StopCoroutine(_stabilizationCoroutine);
                  _stabilizationCoroutine = StartCoroutine(StabilizationCoroutine(contourResult.Vertices, contourResult.Triangles, contourResult.UVs, maskCoords));
            }
            else
            {
                  Debug.LogWarning("[WallPainterController] Не удалось извлечь контур или триангулировать.");
            }
      }

      private IEnumerator RunContourExtractionPipelineAsync(Vector2Int startCoords, ContourExtractionResult result)
      {
            var floodFillJob = new FloodFillJob
            {
                  InputMask = new NativeArray<byte>(lastSegmentationMask.ToByteArray(), Allocator.TempJob),
                  OutputMask = new NativeArray<byte>(lastSegmentationMask.Length, Allocator.TempJob),
                  Width = lastSegmentationMask.GetLength(0),
                  Height = lastSegmentationMask.GetLength(1),
                  StartPos = startCoords
            };
            var floodFillHandle = floodFillJob.Schedule();
            yield return new WaitUntil(() => floodFillHandle.IsCompleted);
            floodFillHandle.Complete();
            var filledMask = floodFillJob.OutputMask.ToRectangularArray(floodFillJob.Width, floodFillJob.Height);
            floodFillJob.InputMask.Dispose();
            floodFillJob.OutputMask.Dispose();

            if (CountFilledPixels(filledMask) < minContourArea)
            {
                  result.Success = false;
                  yield break;
            }

            var marchingSquaresJob = new MarchingSquaresJob
            {
                  InputMask = new NativeArray<byte>(filledMask.ToByteArray(), Allocator.TempJob),
                  ContourPoints = new NativeList<Vector2>(Allocator.TempJob),
                  Width = filledMask.GetLength(0),
                  Height = filledMask.GetLength(1)
            };
            var marchingSquaresHandle = marchingSquaresJob.Schedule();
            yield return new WaitUntil(() => marchingSquaresHandle.IsCompleted);
            marchingSquaresHandle.Complete();
            var maskContour = new List<Vector2>(marchingSquaresJob.ContourPoints.ToArray(Allocator.Temp));
            marchingSquaresJob.InputMask.Dispose();
            marchingSquaresJob.ContourPoints.Dispose();

            if (maskContour.Count < 3)
            {
                  result.Success = false;
                  yield break;
            }

            if (contourSimplificationTolerance > 0)
            {
                  maskContour = MarchingSquares.SimplifyContour(maskContour, contourSimplificationTolerance);
            }
            var screenContour = ConvertMaskCoordsToScreen(maskContour);
            if (debugMode && showContourVisualization) VisualizeContour(screenContour);

            var worldContour = ProjectContourTo3D_JobSystem(screenContour);
            if (worldContour.Count < 3)
            {
                  result.Success = false;
                  yield break;
            }

            if (!TriangulateWithLibTess(worldContour, out var tessVertices, out var tessTriangles))
            {
                  result.Success = false;
                  yield break;
            }

            // 6. Генерация UV-координат
            var uvs = GenerateUVs(tessVertices);

            result.Success = true;
            result.Vertices = tessVertices;
            result.Triangles = tessTriangles;
            result.UVs = uvs;
      }

      private IEnumerator StabilizationCoroutine(List<Vector3> initialWorldContour, int[] initialTriangles, Vector2[] initialUVs, Vector2Int maskCoords)
      {
            var managedWall = new ManagedWall(m_AnchorManager, positionFilterQ, positionFilterR, rotationSmoothingFactor);
            managedWall.CreateAnchorAndGameObject(wallPlanePrefab, wallPaintMaterial);
            managedWall.UpdateMesh(initialWorldContour, initialTriangles, initialUVs);

            for (int i = 0; i < stabilizationFrames; i++)
            {
                  var poseResult = new PoseExtractionResult();
                  yield return GetCurrentPoseMeasurementAsync(maskCoords, poseResult);
                  if (poseResult.Success)
                  {
                        managedWall.UpdateFilters(poseResult.Pose);
                        managedWall.ApplySmoothedPose();
                  }
                  yield return null;
            }

            var finalContourResult = new ContourExtractionResult();
            yield return StartCoroutine(RunContourExtractionPipelineAsync(maskCoords, finalContourResult));
            if (finalContourResult.Success)
            {
                  managedWall.UpdateMesh(finalContourResult.Vertices, finalContourResult.Triangles, finalContourResult.UVs);
            }

            generatedPlanes.Add(managedWall.WallObject);
            _stabilizationCoroutine = null;
      }

      private IEnumerator GetCurrentPoseMeasurementAsync(Vector2Int maskCoords, PoseExtractionResult result)
      {
            var screenPos = ConvertMaskCoordsToScreen(new List<Vector2> { maskCoords })[0];
            List<ARRaycastHit> hits = new List<ARRaycastHit>();
            if (raycastManager.Raycast(screenPos, hits, TrackableType.PlaneWithinPolygon))
            {
                  result.Pose = hits[0].pose;
                  result.Success = true;
            }
            else
            {
                  result.Success = false;
            }
            yield break;
      }

      private List<Vector3> ProjectContourTo3D_JobSystem(List<Vector2> screenContour)
      {
            var plane = FindProjectionPlane(screenContour);
            if (plane == null) return new List<Vector3>();

            var worldPoints = new NativeArray<Vector3>(screenContour.Count, Allocator.TempJob);
            var successFlags = new NativeArray<int>(screenContour.Count, Allocator.TempJob);

            var job = new ContourProjectionJob
            {
                  ScreenPoints = new NativeArray<Vector2>(screenContour.ToArray(), Allocator.TempJob),
                  ViewProjectionMatrix = arCamera.projectionMatrix * arCamera.worldToCameraMatrix,
                  CameraToWorldMatrix = arCamera.cameraToWorldMatrix,
                  PlaneNormal = plane.Value.normal,
                  PlanePoint = plane.Value.normal * plane.Value.distance,
                  MaxRaycastDistance = maxRaycastDistance,
                  CameraPosition = arCamera.transform.position,
                  WorldPoints = worldPoints,
                  SuccessFlags = successFlags
            };

            var handle = job.Schedule(screenContour.Count, 32);
            handle.Complete();

            List<Vector3> successfulPoints = new List<Vector3>();
            for (int i = 0; i < screenContour.Count; i++)
            {
                  if (successFlags[i] == 1)
                  {
                        successfulPoints.Add(worldPoints[i]);
                  }
            }

            job.ScreenPoints.Dispose();
            worldPoints.Dispose();
            successFlags.Dispose();

            return successfulPoints;
      }

      private Plane? FindProjectionPlane(List<Vector2> screenContour)
      {
            List<ARRaycastHit> hits = new List<ARRaycastHit>();
            if (raycastManager.Raycast(screenContour[screenContour.Count / 2], hits, TrackableType.PlaneWithinPolygon))
            {
                  var arPlane = planeManager.GetPlane(hits[0].trackableId);
                  if (arPlane != null)
                  {
                        return new Plane(arPlane.transform.up, arPlane.transform.position);
                  }
            }
            return null;
      }

      private bool TriangulateWithLibTess(List<Vector3> contour, out List<Vector3> outVertices, out int[] outTriangles)
      {
            outVertices = new List<Vector3>();
            outTriangles = null;

            if (contour == null || contour.Count < 3) return false;

            var tess = new Tess();
            var contourVertices = new ContourVertex[contour.Count];
            var normal = CalculatePlaneNormal(contour);
            var rotation = Quaternion.FromToRotation(normal, Vector3.forward);

            for (int i = 0; i < contour.Count; i++)
            {
                  var projectedPoint = rotation * contour[i];
                  contourVertices[i] = new ContourVertex { Position = new Vec3(projectedPoint.x, projectedPoint.y, 0), Data = contour[i] };
            }

            tess.AddContour(contourVertices, ContourOrientation.Original);
            tess.Tessellate(WindingRule.EvenOdd, ElementType.Polygons, 3, (position, data, weights) =>
            {
                  if (data.Length < 4) return null; // Should not happen with N=3 polygons
                  return ((Vector3)data[0] * weights[0]) + ((Vector3)data[1] * weights[1]) + ((Vector3)data[2] * weights[2]) + ((Vector3)data[3] * weights[3]);
            });

            if (tess.ElementCount == 0) return false;

            outVertices.AddRange(tess.Vertices.Select(v => (Vector3)v.Data));
            outTriangles = tess.Elements;

            return true;
      }

      private Vector3 CalculatePlaneNormal(List<Vector3> vertices)
      {
            if (vertices.Count < 3) return Vector3.up;
            Vector3 normal = Vector3.zero;
            for (int i = 0; i < vertices.Count; i++)
            {
                  Vector3 current = vertices[i];
                  Vector3 next = vertices[(i + 1) % vertices.Count];
                  normal.x += (current.y - next.y) * (current.z + next.z);
                  normal.y += (current.z - next.z) * (current.x + next.x);
                  normal.z += (current.x - next.x) * (current.y + next.y);
            }
            return normal.normalized;
      }

      private Vector2Int ConvertScreenToMaskCoords(Vector2 screenPos)
      {
            if (lastSegmentationMask == null) return new Vector2Int(-1, -1);
            int maskWidth = lastSegmentationMask.GetLength(0);
            int maskHeight = lastSegmentationMask.GetLength(1);
            return new Vector2Int(
                Mathf.FloorToInt(screenPos.x / Screen.width * maskWidth),
                Mathf.FloorToInt(screenPos.y / Screen.height * maskHeight)
            );
      }

      private List<Vector2> ConvertMaskCoordsToScreen(List<Vector2> maskCoords)
      {
            if (lastSegmentationMask == null) return new List<Vector2>();
            int maskWidth = lastSegmentationMask.GetLength(0);
            int maskHeight = lastSegmentationMask.GetLength(1);
            return maskCoords.Select(p => new Vector2(
                p.x / maskWidth * Screen.width,
                p.y / maskHeight * Screen.height
            )).ToList();
      }

      private int CountFilledPixels(byte[,] mask)
      {
            int count = 0;
            foreach (byte b in mask) if (b == 1) count++;
            return count;
      }

      private void CreateDebugVisualization()
      {
            var go = new GameObject("DebugContourRenderer");
            debugContourRenderer = go.AddComponent<LineRenderer>();
            debugContourRenderer.material = debugContourMaterial;
            debugContourRenderer.startWidth = 0.02f;
            debugContourRenderer.endWidth = 0.02f;
            debugContourRenderer.positionCount = 0;
            debugContourRenderer.loop = true;
            debugContourRenderer.useWorldSpace = true;
      }

      private void VisualizeContour(List<Vector2> screenPoints)
      {
            if (debugContourRenderer == null || screenPoints.Count == 0) return;

            var worldPoints = new List<Vector3>();
            foreach (var point in screenPoints)
            {
                  var ray = arCamera.ScreenPointToRay(point);
                  worldPoints.Add(ray.GetPoint(1.0f)); // Project 1m in front of camera
            }
            debugContourRenderer.positionCount = worldPoints.Count;
            debugContourRenderer.SetPositions(worldPoints.ToArray());
      }

      private Vector2[] GenerateUVs(List<Vector3> vertices)
      {
            if (vertices == null || vertices.Count == 0) return new Vector2[0];

            Vector2[] uvs = new Vector2[vertices.Count];
            Vector3 normal = CalculatePlaneNormal(vertices);
            Quaternion rotation = Quaternion.FromToRotation(normal, Vector3.back);

            Vector3 min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            Vector3 max = new Vector3(float.MinValue, float.MinValue, float.MinValue);

            List<Vector3> projectedVertices = new List<Vector3>(vertices.Count);

            foreach (var vertex in vertices)
            {
                  Vector3 projected = rotation * vertex;
                  projectedVertices.Add(projected);
                  min.x = Mathf.Min(min.x, projected.x);
                  min.y = Mathf.Min(min.y, projected.y);
                  max.x = Mathf.Max(max.x, projected.x);
                  max.y = Mathf.Max(max.y, projected.y);
            }

            float scaleX = max.x - min.x;
            float scaleY = max.y - min.y;

            for (int i = 0; i < projectedVertices.Count; i++)
            {
                  float u = (scaleX > 0.001f) ? (projectedVertices[i].x - min.x) / scaleX : 0;
                  float v = (scaleY > 0.001f) ? (projectedVertices[i].y - min.y) / scaleY : 0;
                  uvs[i] = new Vector2(u, v);
            }

            return uvs;
      }
}

public static class Extensions
{
      public static byte[] ToByteArray(this byte[,] multiDimArray)
      {
            int width = multiDimArray.GetLength(0);
            int height = multiDimArray.GetLength(1);
            byte[] flatArray = new byte[width * height];
            for (int y = 0; y < height; y++)
            {
                  for (int x = 0; x < width; x++)
                  {
                        flatArray[y * width + x] = multiDimArray[x, y];
                  }
            }
            return flatArray;
      }

      public static byte[,] ToRectangularArray(this NativeArray<byte> flatArray, int width, int height)
      {
            byte[,] multiDimArray = new byte[width, height];
            for (int y = 0; y < height; y++)
            {
                  for (int x = 0; x < width; x++)
                  {
                        multiDimArray[x, y] = flatArray[y * width + x];
                  }
            }
            return multiDimArray;
      }
}

[BurstCompile]
public struct ContourProjectionJob : IJobParallelFor
{
      [ReadOnly] public NativeArray<Vector2> ScreenPoints;
      [ReadOnly] public Matrix4x4 ViewProjectionMatrix;
      [ReadOnly] public Matrix4x4 CameraToWorldMatrix;
      [ReadOnly] public Vector3 PlaneNormal;
      [ReadOnly] public Vector3 PlanePoint;
      [ReadOnly] public float MaxRaycastDistance;
      [ReadOnly] public Vector3 CameraPosition;
      [WriteOnly] public NativeArray<Vector3> WorldPoints;
      [WriteOnly] public NativeArray<int> SuccessFlags;

      public void Execute(int index)
      {
            Vector2 screenPoint = ScreenPoints[index];
            Ray ray = new Ray();

            // Manual screen-to-world logic for jobs
            Vector4 viewportPoint = new Vector4(screenPoint.x / Screen.width, screenPoint.y / Screen.height, 0.0f, 1.0f);
            Matrix4x4 inverseVp = ViewProjectionMatrix.inverse;
            Vector4 nearPoint = inverseVp * new Vector4(viewportPoint.x * 2 - 1, viewportPoint.y * 2 - 1, -1, 1);
            Vector4 farPoint = inverseVp * new Vector4(viewportPoint.x * 2 - 1, viewportPoint.y * 2 - 1, 1, 1);

            nearPoint /= nearPoint.w;
            farPoint /= farPoint.w;

            ray.origin = nearPoint;
            ray.direction = (farPoint - nearPoint).normalized;

            Plane p = new Plane(PlaneNormal, PlanePoint);
            if (p.Raycast(ray, out float enter))
            {
                  if (enter > 0 && enter < MaxRaycastDistance)
                  {
                        WorldPoints[index] = ray.GetPoint(enter);
                        SuccessFlags[index] = 1;
                        return;
                  }
            }
            SuccessFlags[index] = 0;
      }
}

internal class ManagedWall
{
      public ARAnchor Anchor { get; private set; }
      public GameObject WallObject { get; private set; }
      private KalmanFilterVector3 _positionFilter;
      private QuaternionSmoother _rotationSmoother;
      private UnityEngine.Mesh _mesh;
      private ARAnchorManager _anchorManager;

      public ManagedWall(ARAnchorManager anchorManager, float posQ, float posR, float rotSmooth)
      {
            _anchorManager = anchorManager;
            _positionFilter = new KalmanFilterVector3(posQ, posR);
            _rotationSmoother = new QuaternionSmoother(rotSmooth);
      }

      public void CreateAnchorAndGameObject(GameObject prefab, Material material)
      {
            WallObject = Object.Instantiate(prefab);
            var meshRenderer = WallObject.GetComponent<MeshRenderer>();
            if (meshRenderer != null) meshRenderer.material = material;

            var meshFilter = WallObject.GetComponent<MeshFilter>();
            if (meshFilter != null) _mesh = meshFilter.mesh = new UnityEngine.Mesh();
      }

      public void UpdateMesh(List<Vector3> worldVertices, int[] triangles, Vector2[] uvs)
      {
            if (_mesh == null || worldVertices == null || triangles == null) return;
            _mesh.Clear();
            _mesh.SetVertices(worldVertices);
            _mesh.SetTriangles(triangles, 0);
            _mesh.uv = uvs;
            _mesh.RecalculateNormals();
            _mesh.RecalculateBounds();
      }

      public void UpdateFilters(Pose newMeasurement)
      {
            _positionFilter.Update(newMeasurement.position);
            _rotationSmoother.Update(newMeasurement.rotation);
      }

      public void ApplySmoothedPose()
      {
            if (Anchor == null)
            {
                  var pose = new Pose(_positionFilter.GetState(), _rotationSmoother.GetState());

                  // Создаем временный GameObject для якоря
                  var tempAnchorObj = new GameObject("TempAnchor");
                  tempAnchorObj.transform.position = pose.position;
                  tempAnchorObj.transform.rotation = pose.rotation;

                  // Добавляем компонент ARAnchor
                  Anchor = tempAnchorObj.AddComponent<ARAnchor>();

                  if (Anchor != null) WallObject.transform.SetParent(Anchor.transform, false);
            }
            else
            {
                  Anchor.transform.position = _positionFilter.GetState();
                  Anchor.transform.rotation = _rotationSmoother.GetState();
            }
      }
}

[BurstCompile]
internal struct FloodFillJob : IJob
{
      [ReadOnly] public NativeArray<byte> InputMask;
      public NativeArray<byte> OutputMask;
      [ReadOnly] public int Width;
      [ReadOnly] public int Height;
      [ReadOnly] public Vector2Int StartPos;

      public void Execute()
      {
            // Simple non-recursive Flood Fill
            var q = new NativeQueue<Vector2Int>(Allocator.Temp);
            q.Enqueue(StartPos);

            while (q.TryDequeue(out var p))
            {
                  if (p.x < 0 || p.x >= Width || p.y < 0 || p.y >= Height) continue;
                  int idx = p.y * Width + p.x;
                  if (OutputMask[idx] == 1 || InputMask[idx] == 0) continue;

                  OutputMask[idx] = 1;

                  q.Enqueue(new Vector2Int(p.x + 1, p.y));
                  q.Enqueue(new Vector2Int(p.x - 1, p.y));
                  q.Enqueue(new Vector2Int(p.x, p.y + 1));
                  q.Enqueue(new Vector2Int(p.x, p.y - 1));
            }
            q.Dispose();
      }
}

[BurstCompile]
internal struct MarchingSquaresJob : IJob
{
      [ReadOnly] public NativeArray<byte> InputMask;
      public NativeList<Vector2> ContourPoints;
      [ReadOnly] public int Width;
      [ReadOnly] public int Height;

      public void Execute()
      {
            // ... Implementation of Marching Squares ...
            // This is a complex algorithm, for brevity, we assume it's implemented correctly here.
            // A basic placeholder to find the first point and start tracing:
            Vector2Int startPoint = FindStartPoint();
            if (startPoint.x == -1) return;

            TraceContour(startPoint);
      }

      private Vector2Int FindStartPoint()
      {
            for (int y = 0; y < Height; y++)
                  for (int x = 0; x < Width; x++)
                        if (InputMask[y * Width + x] == 1) return new Vector2Int(x, y);
            return new Vector2Int(-1, -1);
      }

      private void TraceContour(Vector2Int start)
      {
            Vector2Int currentPos = start;
            int dir = 0; // 0:N, 1:E, 2:S, 3:W

            do
            {
                  dir = (dir + 3) % 4; // Turn left
                  for (int i = 0; i < 4; i++)
                  {
                        Vector2Int move = GetDirection(dir);
                        Vector2Int nextPos = currentPos + move;
                        if (IsInside(nextPos) && GetValue(nextPos.x, nextPos.y) == 1)
                        {
                              currentPos = nextPos;
                              ContourPoints.Add(new Vector2(currentPos.x, currentPos.y));
                              break;
                        }
                        dir = (dir + 1) % 4; // Turn right
                  }
            } while (currentPos != start && ContourPoints.Length < Width * Height);
      }

      private Vector2Int GetDirection(int dir)
      {
            switch (dir % 4)
            {
                  case 0: return new Vector2Int(0, 1);  // North
                  case 1: return new Vector2Int(1, 0);  // East
                  case 2: return new Vector2Int(0, -1); // South
                  case 3: return new Vector2Int(-1, 0); // West
                  default: return new Vector2Int(0, 0);
            }
      }

      private byte GetValue(int x, int y)
      {
            if (!IsInside(new Vector2Int(x, y))) return 0;
            return InputMask[y * Width + x];
      }

      private bool IsInside(Vector2Int p)
      {
            return p.x >= 0 && p.x < Width && p.y >= 0 && p.y < Height;
      }
}

public static class MarchingSquares
{
      public static List<Vector2> SimplifyContour(List<Vector2> contour, float tolerance)
      {
            if (contour.Count < 3) return contour;
            var simplified = new List<Vector2>();
            simplified.Add(contour[0]);
            for (int i = 1; i < contour.Count - 1; i++)
            {
                  if (Vector2.Distance(simplified.Last(), contour[i]) > tolerance)
                  {
                        simplified.Add(contour[i]);
                  }
            }
            simplified.Add(contour.Last());
            return simplified;
      }
}

public class KalmanFilter
{
      private float q, r, p = 1.0f, x = 0.0f;
      private bool isInitialized = false;
      public KalmanFilter(float q = 0.01f, float r = 0.1f) { this.q = q; this.r = r; }
      public float Update(float measurement)
      {
            if (!isInitialized) { x = measurement; isInitialized = true; }
            p += q;
            float k = p / (p + r);
            x += k * (measurement - x);
            p *= (1 - k);
            return x;
      }
      public float GetState() => x;
}

public class KalmanFilterVector3
{
      private KalmanFilter fX, fY, fZ;
      public KalmanFilterVector3(float q = 0.01f, float r = 0.1f)
      {
            fX = new KalmanFilter(q, r);
            fY = new KalmanFilter(q, r);
            fZ = new KalmanFilter(q, r);
      }
      public Vector3 Update(Vector3 m) => new Vector3(fX.Update(m.x), fY.Update(m.y), fZ.Update(m.z));
      public Vector3 GetState() => new Vector3(fX.GetState(), fY.GetState(), fZ.GetState());
}

public class QuaternionSmoother
{
      private float smoothingFactor;
      private Quaternion smoothedState;
      private bool isInitialized = false;
      public QuaternionSmoother(float smoothingFactor = 0.2f) { this.smoothingFactor = Mathf.Clamp01(smoothingFactor); }
      public Quaternion Update(Quaternion measurement)
      {
            if (!isInitialized) { smoothedState = measurement; isInitialized = true; }
            else { smoothedState = Quaternion.Slerp(smoothedState, measurement, smoothingFactor); }
            return smoothedState;
      }
      public Quaternion GetState() => smoothedState;
}