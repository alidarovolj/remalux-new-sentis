using UnityEngine;
using UnityEngine.UI;
using System.Collections;
using UnityEngine.XR.ARFoundation;
using System.Collections.Generic;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Класс для отладки системы покраски стен.
/// Позволяет визуализировать различные параметры AR и процесс обнаружения плоскостей.
/// </summary>
public class WallPaintDebugger : MonoBehaviour
{
      [Header("Ссылки на компоненты")]
      [SerializeField] private ARPlaneManager planeManager;
      [SerializeField] private ARWallPainter wallPainter;
      [SerializeField] private WallPaintEffect wallPaintEffect;
      [SerializeField] private WallSegmentation wallSegmentation;
      [SerializeField] private Text debugText;

      [Header("Testing Controls")]
      [SerializeField] private Button increaseBlendButton;
      [SerializeField] private Button decreaseBlendButton;
      [SerializeField] private Button toggleMaskButton;
      [SerializeField] private Button resetButton;
      [SerializeField] private Slider blendSlider;
      [SerializeField] private Toggle debugOverlayToggle;

      [Header("Настройки отладки")]
      [SerializeField] private bool showPlanes = true;
      [SerializeField] private bool highlightVerticalPlanes = true;
      [SerializeField] private bool showNormals = true;
      [SerializeField] private float normalLength = 0.5f;
      [SerializeField] private bool showTrackingInfo = true;

      [Header("Цвета отображения")]
      [SerializeField] private Color horizontalPlaneColor = new Color(0.0f, 0.8f, 1.0f, 0.5f);
      [SerializeField] private Color verticalPlaneColor = new Color(1.0f, 0.4f, 0.0f, 0.5f);
      [SerializeField] private Color normalColor = Color.blue;

      private bool isInitialized = false;
      private float currentBlend = 0.5f;
      private bool useMask = true;
      private Color currentColor = Color.red;

      private Dictionary<TrackableId, GameObject> debugVisuals = new Dictionary<TrackableId, GameObject>();
      private GameObject debugContainer;

      private void Awake()
      {
            // Находим необходимые компоненты, если они не указаны
            if (planeManager == null) planeManager = FindObjectOfType<ARPlaneManager>();
            if (wallPainter == null) wallPainter = FindObjectOfType<ARWallPainter>();
            if (wallPaintEffect == null) wallPaintEffect = FindObjectOfType<WallPaintEffect>();
            if (wallSegmentation == null) wallSegmentation = FindObjectOfType<WallSegmentation>();
            
            // Создаем контейнер для отладочных визуализаций
            debugContainer = new GameObject("DebugVisuals");
            debugContainer.transform.SetParent(transform);
      }

      private void OnEnable()
      {
            if (planeManager != null)
            {
                  planeManager.planesChanged += OnPlanesChanged;
            }
            else
            {
                  Debug.LogWarning("WallPaintDebugger: ARPlaneManager не найден!");
            }
      }

      private void OnDisable()
      {
            if (planeManager != null)
            {
                  planeManager.planesChanged -= OnPlanesChanged;
            }
            
            // Удаляем все отладочные визуализации
            ClearDebugVisuals();
      }

      private void Start()
      {
            if (wallPaintEffect == null)
            {
                  wallPaintEffect = FindObjectOfType<WallPaintEffect>();
            }

            if (wallSegmentation == null)
            {
                  wallSegmentation = FindObjectOfType<WallSegmentation>();
            }

            StartCoroutine(DelayedInit());

            // При включенной отладке сразу показываем существующие плоскости
            if (showPlanes && planeManager != null)
            {
                  StartCoroutine(UpdateAllPlanesWithDelay());
            }
      }

      private IEnumerator DelayedInit()
      {
            // Give time for AR session to initialize
            yield return new WaitForSeconds(2.0f);

            if (wallPaintEffect != null)
            {
                  currentBlend = wallPaintEffect.GetBlendFactor();
                  currentColor = wallPaintEffect.GetPaintColor();
                  useMask = true; // Default
            }

            if (blendSlider != null)
            {
                  blendSlider.value = currentBlend;
                  blendSlider.onValueChanged.AddListener(OnBlendSliderChanged);
            }

            if (increaseBlendButton != null)
            {
                  increaseBlendButton.onClick.AddListener(IncreaseBlend);
            }

            if (decreaseBlendButton != null)
            {
                  decreaseBlendButton.onClick.AddListener(DecreaseBlend);
            }

            if (toggleMaskButton != null)
            {
                  toggleMaskButton.onClick.AddListener(ToggleMask);
            }

            if (resetButton != null)
            {
                  resetButton.onClick.AddListener(ResetEffect);
            }

            if (debugOverlayToggle != null)
            {
                  debugOverlayToggle.onValueChanged.AddListener(ToggleDebugOverlay);
            }

            isInitialized = true;
            UpdateDebugText();
      }

      private void Update()
      {
            if (isInitialized && Time.frameCount % 30 == 0)
            {
                  UpdateDebugText();
            }
      }

      public void IncreaseBlend()
      {
            if (wallPaintEffect != null)
            {
                  currentBlend = Mathf.Clamp01(currentBlend + 0.1f);
                  wallPaintEffect.SetBlendFactor(currentBlend);
                  if (blendSlider != null) blendSlider.value = currentBlend;
                  UpdateDebugText();
            }
      }

      public void DecreaseBlend()
      {
            if (wallPaintEffect != null)
            {
                  currentBlend = Mathf.Clamp01(currentBlend - 0.1f);
                  wallPaintEffect.SetBlendFactor(currentBlend);
                  if (blendSlider != null) blendSlider.value = currentBlend;
                  UpdateDebugText();
            }
      }

      public void OnBlendSliderChanged(float value)
      {
            if (wallPaintEffect != null)
            {
                  currentBlend = value;
                  wallPaintEffect.SetBlendFactor(currentBlend);
                  UpdateDebugText();
            }
      }

      public void ToggleMask()
      {
            if (wallPaintEffect != null)
            {
                  useMask = !useMask;
                  wallPaintEffect.SetUseMask(useMask);
                  UpdateDebugText();
            }
      }

      public void SetColor(Color color)
      {
            if (wallPaintEffect != null)
            {
                  currentColor = color;
                  wallPaintEffect.SetPaintColor(color);
                  UpdateDebugText();
            }
      }

      public void ResetEffect()
      {
            if (wallPaintEffect != null)
            {
                  // Force recreate material
                  wallPaintEffect.SetBlendFactor(0.7f);
                  wallPaintEffect.SetPaintColor(Color.red);
                  wallPaintEffect.SetUseMask(true);
                  wallPaintEffect.ForceUpdateMaterial();
                  wallPaintEffect.FixMaterialTextures();

                  // Update local values
                  currentBlend = 0.7f;
                  currentColor = Color.red;
                  useMask = true;

                  if (blendSlider != null) blendSlider.value = currentBlend;

                  UpdateDebugText();
            }
      }

      public void ToggleDebugOverlay(bool enabled)
      {
            if (wallPaintEffect != null)
            {
                  if (enabled)
                  {
                        wallPaintEffect.FixRenderingMode();
                  }
                  else
                  {
                        wallPaintEffect.DisableDebugMode();
                  }
            }
      }

      private void UpdateDebugText()
      {
            if (debugText == null) return;

            string status = "WallPaint Debug Info:\n";

            // WallPaintEffect status
            status += "Effect: " + (wallPaintEffect != null ? (wallPaintEffect.IsReady() ? "Ready" : "Not Ready") : "Not Found") + "\n";

            // Material info
            if (wallPaintEffect != null)
            {
                  Material mat = wallPaintEffect.GetMaterial();
                  status += "Material: " + (mat != null ? mat.shader.name : "None") + "\n";
                  status += $"Blend: {currentBlend:F2}, Mask: {useMask}\n";
                  status += $"Color: ({currentColor.r:F1}, {currentColor.g:F1}, {currentColor.b:F1})\n";
            }

            // Segmentation status
            if (wallSegmentation != null)
            {
                  status += "Segmentation: " + (wallSegmentation.IsModelInitialized ? "Ready" : "Not Ready") + "\n";
                  status += "Mask Texture: " + (wallSegmentation.segmentationMaskTexture != null ?
                      $"{wallSegmentation.segmentationMaskTexture.width}x{wallSegmentation.segmentationMaskTexture.height}" : "None") + "\n";
            }
            else
            {
                  status += "Segmentation: Not Found\n";
            }

            debugText.text = status;
      }

      private IEnumerator UpdateAllPlanesWithDelay()
      {
            // Небольшая задержка для инициализации AR
            yield return new WaitForSeconds(1.0f);
            
            // Обновляем все существующие плоскости
            foreach (ARPlane plane in planeManager.trackables)
            {
                  UpdateDebugVisualForPlane(plane);
            }
      }

      private void OnPlanesChanged(ARPlanesChangedEventArgs args)
      {
            if (!showPlanes) return;

            // Обрабатываем новые плоскости
            foreach (ARPlane plane in args.added)
            {
                  UpdateDebugVisualForPlane(plane);
            }

            // Обновляем существующие плоскости
            foreach (ARPlane plane in args.updated)
            {
                  UpdateDebugVisualForPlane(plane);
            }

            // Удаляем визуализацию для удаленных плоскостей
            foreach (ARPlane plane in args.removed)
            {
                  RemoveDebugVisualForPlane(plane);
            }
      }

      private void UpdateDebugVisualForPlane(ARPlane plane)
      {
            if (!showPlanes) return;

            // Проверяем, существует ли уже отладочный объект для этой плоскости
            GameObject debugVisual;
            if (!debugVisuals.TryGetValue(plane.trackableId, out debugVisual))
            {
                  // Создаем новый отладочный объект
                  debugVisual = new GameObject($"DebugPlane_{plane.trackableId}");
                  debugVisual.transform.SetParent(debugContainer.transform);
                  
                  // Добавляем LineRenderer для отображения контура плоскости
                  LineRenderer lineRenderer = debugVisual.AddComponent<LineRenderer>();
                  lineRenderer.useWorldSpace = true;
                  lineRenderer.startWidth = 0.02f;
                  lineRenderer.endWidth = 0.02f;
                  lineRenderer.positionCount = 0;
                  lineRenderer.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                  
                  // Добавляем объект для отображения нормали
                  if (showNormals)
                  {
                        GameObject normalObj = new GameObject("Normal");
                        normalObj.transform.SetParent(debugVisual.transform);
                        LineRenderer normalLine = normalObj.AddComponent<LineRenderer>();
                        normalLine.useWorldSpace = true;
                        normalLine.startWidth = 0.01f;
                        normalLine.endWidth = 0.01f;
                        normalLine.positionCount = 2;
                        normalLine.material = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                        normalLine.startColor = normalColor;
                        normalLine.endColor = normalColor;
                  }
                  
                  // Сохраняем отладочный объект в словаре
                  debugVisuals.Add(plane.trackableId, debugVisual);
            }

            // Обновляем позицию и ориентацию отладочного объекта
            debugVisual.transform.position = plane.center;
            debugVisual.transform.rotation = Quaternion.LookRotation(plane.normal);

            // Определяем цвет в зависимости от ориентации плоскости
            Color planeColor = plane.alignment == PlaneAlignment.Vertical ? verticalPlaneColor : horizontalPlaneColor;
            
            // Если плоскость вертикальная и включено выделение вертикальных плоскостей
            if (highlightVerticalPlanes && IsVerticalPlane(plane))
            {
                  planeColor = verticalPlaneColor;
            }

            // Обновляем цвет LineRenderer
            LineRenderer renderer = debugVisual.GetComponent<LineRenderer>();
            renderer.startColor = planeColor;
            renderer.endColor = planeColor;
            
            // Обновляем контур плоскости
            UpdatePlaneOutline(plane, renderer);
            
            // Обновляем отображение нормали
            if (showNormals)
            {
                  Transform normalTransform = debugVisual.transform.Find("Normal");
                  if (normalTransform != null)
                  {
                        LineRenderer normalLine = normalTransform.GetComponent<LineRenderer>();
                        Vector3 center = plane.center;
                        normalLine.SetPosition(0, center);
                        normalLine.SetPosition(1, center + plane.normal * normalLength);
                  }
            }
      }

      private bool IsVerticalPlane(ARPlane plane)
      {
            // Плоскость считается вертикальной, если она имеет соответствующий тип
            if (plane.alignment == PlaneAlignment.Vertical)
                  return true;

            // Дополнительная проверка по нормали
            float dotUp = Vector3.Dot(plane.normal, Vector3.up);
            return Mathf.Abs(dotUp) < 0.707f; // cos(45°) ≈ 0.707
      }

      private void UpdatePlaneOutline(ARPlane plane, LineRenderer renderer)
      {
            // Получаем точки контура
            Vector3[] points = new Vector3[plane.boundary.Length];
            for (int i = 0; i < plane.boundary.Length; i++)
            {
                  points[i] = plane.transform.TransformPoint(new Vector3(
                        plane.boundary[i].x, 
                        0, 
                        plane.boundary[i].y));
            }
            
            // Устанавливаем позиции для LineRenderer
            renderer.positionCount = points.Length + 1; // +1 для замыкания контура
            for (int i = 0; i < points.Length; i++)
            {
                  renderer.SetPosition(i, points[i]);
            }
            
            // Замыкаем контур
            renderer.SetPosition(points.Length, points[0]);
      }

      private void RemoveDebugVisualForPlane(ARPlane plane)
      {
            GameObject debugVisual;
            if (debugVisuals.TryGetValue(plane.trackableId, out debugVisual))
            {
                  Destroy(debugVisual);
                  debugVisuals.Remove(plane.trackableId);
            }
      }

      private void ClearDebugVisuals()
      {
            foreach (var item in debugVisuals)
            {
                  Destroy(item.Value);
            }
            
            debugVisuals.Clear();
      }

      // Переключает видимость отладочной визуализации
      public void ToggleDebugVisuals()
      {
            showPlanes = !showPlanes;
            
            if (showPlanes)
            {
                  // Обновляем все существующие плоскости
                  if (planeManager != null)
                  {
                        foreach (ARPlane plane in planeManager.trackables)
                        {
                              UpdateDebugVisualForPlane(plane);
                        }
                  }
            }
            else
            {
                  // Удаляем все отладочные визуализации
                  ClearDebugVisuals();
            }
      }

      // Публичный метод для включения/отключения подсветки вертикальных плоскостей
      public void SetHighlightVerticalPlanes(bool highlight)
      {
            highlightVerticalPlanes = highlight;
            
            if (showPlanes && planeManager != null)
            {
                  foreach (ARPlane plane in planeManager.trackables)
                  {
                        UpdateDebugVisualForPlane(plane);
                        Debug.Log("UpdateDebugVisualForPlane");
                  }
            }
      }

      // Публичный метод для включения/отключения отображения нормалей
      public void SetShowNormals(bool show)
      {
            showNormals = show;
            
            if (showPlanes && planeManager != null)
            {
                  foreach (ARPlane plane in planeManager.trackables)
                  {
                        UpdateDebugVisualForPlane(plane);
                  }
            }
      }

      private void OnGUI()
      {
            if (!showTrackingInfo) return;
            
            GUIStyle style = new GUIStyle();
            style.fontSize = 20;
            style.normal.textColor = Color.white;
            style.fontStyle = FontStyle.Bold;
            style.alignment = TextAnchor.UpperLeft;
            
            int yPos = 40;
            GUI.Label(new Rect(10, yPos, 500, 30), $"Отслеживаемые плоскости: {planeManager?.trackables.count ?? 0}", style);
            yPos += 30;
            
            GUI.Label(new Rect(10, yPos, 500, 30), $"Вертикальные плоскости: {CountVerticalPlanes()}", style);
            yPos += 30;
            
            if (wallSegmentation != null)
            {
                  GUI.Label(new Rect(10, yPos, 500, 30), $"Статус сегментации: {(wallSegmentation.IsModelInitialized ? "Активна" : "Неактивна")}", style);
                  yPos += 30;
            }
      }

      private int CountVerticalPlanes()
      {
            if (planeManager == null) return 0;
            
            int count = 0;
            foreach (ARPlane plane in planeManager.trackables)
            {
                  if (IsVerticalPlane(plane))
                  {
                        count++;
                  }
            }
            
            return count;
      }
}