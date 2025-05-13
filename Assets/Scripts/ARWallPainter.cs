using UnityEngine;
using UnityEngine.XR.ARFoundation;
using System.Collections.Generic;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.EventSystems;

/// <summary>
/// Класс для окрашивания стен в AR путем взаимодействия с AR-плоскостями.
/// Работает в стиле Dulux Visualizer, привязывая цвета непосредственно к обнаруженным стенам.
/// </summary>
public class ARWallPainter : MonoBehaviour
{
      [SerializeField] private Camera arCamera;
      [SerializeField] private WallPaintEffect wallPaintEffect;
      [SerializeField] private ARRaycastManager raycastManager;
      [SerializeField] private Color defaultWallColor = Color.white;
      [SerializeField] private float defaultBlendFactor = 0.5f;
      [SerializeField] private bool raycastOnlyOnTap = true; // Выполнять рейкаст только при нажатии
      [SerializeField] private bool ignoreUIInteraction = true; // Игнорировать нажатия на UI

      // Поле для хранения информации о раскрашенных стенах
      private Dictionary<TrackableId, Material> paintedWalls = new Dictionary<TrackableId, Material>();
      private List<ARRaycastHit> raycastHits = new List<ARRaycastHit>();
      private bool isTouching = false;
      private ARPlane lastSelectedPlane = null;

      // Список для результатов рейкаста
      static readonly List<ARRaycastHit> s_Hits = new List<ARRaycastHit>();

      private void Awake()
      {
            // Находим необходимые компоненты, если они не были назначены
            if (arCamera == null) arCamera = Camera.main;
            if (wallPaintEffect == null) wallPaintEffect = FindObjectOfType<WallPaintEffect>();
            if (raycastManager == null) raycastManager = FindObjectOfType<ARRaycastManager>();
      }

      private void Start()
      {
            // Проверяем наличие необходимых компонентов
            if (arCamera == null || wallPaintEffect == null || raycastManager == null)
            {
                  Debug.LogError("ARWallPainter: Не все обязательные компоненты назначены!");
                  enabled = false;
                  return;
            }

            // Включаем режим привязки краски к AR-плоскостям
            wallPaintEffect.SetAttachToARPlanes(true);

            // Настраиваем начальные значения
            if (wallPaintEffect != null)
            {
                  wallPaintEffect.SetPaintColor(defaultWallColor);
                  wallPaintEffect.SetBlendFactor(defaultBlendFactor);
            }
      }

      private void Update()
      {
            // Проверяем ввод пользователя (касание или клик)
            if (Input.touchCount > 0)
            {
                  Touch touch = Input.GetTouch(0);

                  // Обрабатываем только начало касания и перемещение
                  if (touch.phase == TouchPhase.Began)
                  {
                        isTouching = true;
                        HandleTouchInteraction(touch.position);
                  }
                  else if (touch.phase == TouchPhase.Moved && !raycastOnlyOnTap)
                  {
                        // Если включен режим непрерывного рейкаста, обрабатываем и перемещение
                        HandleTouchInteraction(touch.position);
                  }
                  else if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                  {
                        isTouching = false;
                        lastSelectedPlane = null;
                  }
            }
            else if (Input.GetMouseButtonDown(0))
            {
                  // Обработка нажатия мыши для тестирования в редакторе
                  isTouching = true;
                  HandleTouchInteraction(Input.mousePosition);
            }
            else if (Input.GetMouseButtonUp(0))
            {
                  isTouching = false;
                  lastSelectedPlane = null;
            }
            else if (Input.GetMouseButton(0) && !raycastOnlyOnTap)
            {
                  // Непрерывное отслеживание положения мыши, если включен соответствующий режим
                  HandleTouchInteraction(Input.mousePosition);
            }
      }

      private void HandleTouchInteraction(Vector2 screenPosition)
      {
            // Проверяем, не взаимодействует ли пользователь с UI
            if (ignoreUIInteraction && EventSystem.current != null && EventSystem.current.IsPointerOverGameObject())
            {
                  return;
            }

            // Выполняем рейкаст в точку касания
            if (raycastManager.Raycast(screenPosition, s_Hits, TrackableType.PlaneWithinPolygon))
            {
                  // Находим первое попадание
                  ARRaycastHit hit = s_Hits[0];

                  // Получаем плоскость, в которую попал луч
                  ARPlane plane = GetPlaneFromHit(hit);

                  if (plane != null)
                  {
                        // Проверяем, является ли плоскость вертикальной (стеной)
                        if (plane.alignment == PlaneAlignment.Vertical)
                        {
                              // Если это первое касание или новая плоскость
                              if (lastSelectedPlane != plane)
                              {
                                    lastSelectedPlane = plane;
                                    ApplyColorToPlane(plane);
                              }

                              // Можем выполнить дополнительные действия при продолжающемся касании (например, изменение оттенка)
                              if (isTouching && !raycastOnlyOnTap)
                              {
                                    // Здесь может быть дополнительная логика для непрерывной покраски
                              }
                        }
                  }
            }
      }

      // Получаем плоскость из результата рейкаста
      private ARPlane GetPlaneFromHit(ARRaycastHit hit)
      {
            // Пытаемся получить плоскость непосредственно из хита
            ARPlane plane = null;
            TrackableId trackableId = hit.trackableId;

            // Находим все плоскости в сцене
            ARPlane[] allPlanes = FindObjectsOfType<ARPlane>();

            // Ищем плоскость с нужным ID
            foreach (ARPlane p in allPlanes)
            {
                  if (p.trackableId == trackableId)
                  {
                        plane = p;
                        break;
                  }
            }

            return plane;
      }

      // Метод для применения цвета к конкретной плоскости (стене)
      private void ApplyColorToPlane(ARPlane plane)
      {
            // Получаем MeshRenderer плоскости
            MeshRenderer renderer = plane.GetComponent<MeshRenderer>();
            if (renderer == null) return;

            // Проверяем, есть ли уже материал для этой плоскости в нашем словаре
            Material planeMaterial;
            if (!paintedWalls.TryGetValue(plane.trackableId, out planeMaterial))
            {
                  // Создаем новый материал на основе материала стены из WallPaintEffect
                  Material baseMaterial = wallPaintEffect.GetMaterial();
                  if (baseMaterial == null)
                  {
                        Debug.LogError("ARWallPainter: Не удалось получить базовый материал из WallPaintEffect");
                        return;
                  }

                  // Создаем копию материала для этой конкретной стены
                  planeMaterial = new Material(baseMaterial);
                  paintedWalls.Add(plane.trackableId, planeMaterial);
            }

            // Применяем текущий цвет к материалу плоскости
            planeMaterial.SetColor("_PaintColor", wallPaintEffect.GetPaintColor());
            planeMaterial.SetFloat("_BlendFactor", wallPaintEffect.GetBlendFactor());

            // Включаем ключевые слова для правильной работы в AR
            planeMaterial.EnableKeyword("USE_AR_WORLD_SPACE");
            planeMaterial.EnableKeyword("USE_AR_LIGHTING");

            // Назначаем материал рендереру плоскости
            renderer.material = planeMaterial;

            Debug.Log($"ARWallPainter: Применен цвет {wallPaintEffect.GetPaintColor()} к плоскости {plane.name}");
      }

      // Публичный метод для изменения цвета покраски
      public void SetPaintColor(Color color)
      {
            if (wallPaintEffect != null)
            {
                  wallPaintEffect.SetPaintColor(color);

                  // Обновляем все ранее раскрашенные стены
                  UpdateAllPaintedWalls();
            }
      }

      // Публичный метод для изменения интенсивности покраски
      public void SetBlendFactor(float factor)
      {
            if (wallPaintEffect != null)
            {
                  wallPaintEffect.SetBlendFactor(factor);

                  // Обновляем все ранее раскрашенные стены
                  UpdateAllPaintedWalls();
            }
      }

      // Обновляем все ранее покрашенные стены текущим цветом
      private void UpdateAllPaintedWalls()
      {
            Color currentColor = wallPaintEffect.GetPaintColor();
            float currentBlend = wallPaintEffect.GetBlendFactor();

            foreach (var pair in paintedWalls)
            {
                  Material material = pair.Value;
                  if (material != null)
                  {
                        material.SetColor("_PaintColor", currentColor);
                        material.SetFloat("_BlendFactor", currentBlend);
                  }
            }
      }

      // Метод для сброса всех покрашенных стен
      public void ResetAllWalls()
      {
            foreach (var pair in paintedWalls)
            {
                  // Находим плоскость по ID
                  ARPlane[] allPlanes = FindObjectsOfType<ARPlane>();
                  foreach (ARPlane plane in allPlanes)
                  {
                        if (plane.trackableId == pair.Key)
                        {
                              // Получаем компонент рендерера
                              MeshRenderer renderer = plane.GetComponent<MeshRenderer>();
                              if (renderer != null)
                              {
                                    // Сбрасываем непрозрачность краски
                                    Material material = pair.Value;
                                    material.SetFloat("_BlendFactor", 0.0f);
                                    renderer.material = material;
                              }
                              break;
                        }
                  }
            }

            // Очищаем словарь
            paintedWalls.Clear();
      }

      // Когда объект уничтожается, очищаем ссылки на материалы
      private void OnDestroy()
      {
            foreach (var pair in paintedWalls)
            {
                  Material material = pair.Value;
                  if (material != null)
                  {
                        Destroy(material);
                  }
            }

            paintedWalls.Clear();
      }
}