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
      [SerializeField] private ARAnchorManager anchorManager;
      [SerializeField] private Color defaultWallColor = Color.white;
      [SerializeField] private float defaultBlendFactor = 0.5f;
      [SerializeField] private bool raycastOnlyOnTap = true; // Выполнять рейкаст только при нажатии
      [SerializeField] private bool ignoreUIInteraction = true; // Игнорировать нажатия на UI
      [SerializeField] private bool preferVerticalPlanesOnly = true; // Предпочитать только вертикальные плоскости  
      [SerializeField] private Color selectedColor = Color.red; // Выбранный цвет для покраски
      [SerializeField] private float blendFactor = 0.5f; // Фактор смешивания цвета
      [SerializeField] private bool showDebugMarkers = false; // Показывать маркеры для отладки
      [SerializeField] private bool persistPaintBetweenSessions = true; // Сохранять окрашенные стены между сессиями

      // Поле для хранения информации о раскрашенных стенах
      private Dictionary<TrackableId, Material> paintedWalls = new Dictionary<TrackableId, Material>();
      private Dictionary<TrackableId, ARAnchor> wallAnchors = new Dictionary<TrackableId, ARAnchor>(); // Для хранения якорей стен
      private List<ARRaycastHit> raycastHits = new List<ARRaycastHit>();
      private bool isTouching = false;
      private ARPlane lastSelectedPlane = null;

      // Список для результатов рейкаста
      static readonly List<ARRaycastHit> s_Hits = new List<ARRaycastHit>();

      // Кэш уже обнаруженных AR плоскостей
      private Dictionary<TrackableId, ARPlane> planeCache = new Dictionary<TrackableId, ARPlane>();
      // Кэш созданных якорей
      private Dictionary<TrackableId, ARAnchor> anchorCache = new Dictionary<TrackableId, ARAnchor>();
      
      // Новые поля для отслеживания мировых трансформаций
      private Matrix4x4[] planeToWorldMatrices = new Matrix4x4[10]; // Кэш матриц перехода из локального в мировое пространство
      private int planeMatrixCount = 0;

      private void Awake()
      {
            // Находим необходимые компоненты, если они не были назначены
            if (arCamera == null) arCamera = Camera.main;
            if (wallPaintEffect == null) wallPaintEffect = FindObjectOfType<WallPaintEffect>();
            if (raycastManager == null) raycastManager = FindObjectOfType<ARRaycastManager>();
            if (anchorManager == null) anchorManager = FindObjectOfType<ARAnchorManager>();
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

            // Проверяем ARAnchorManager
            if (anchorManager == null)
            {
                  Debug.LogWarning("ARWallPainter: ARAnchorManager не найден, якоря не будут использоваться!");
            }

            // ВАЖНО: Проверяем конфигурацию AR-плоскостей
            ARPlaneManager planeManagerConfig = FindObjectOfType<ARPlaneManager>();
            if (planeManagerConfig != null)
            {
                  Debug.LogError($"КОНФИГУРАЦИЯ AR PLANE MANAGER: detectionMode={planeManagerConfig.requestedDetectionMode}, " + 
                               $"prefab={planeManagerConfig.planePrefab != null}, " + 
                               $"trackables={planeManagerConfig.trackables.count}");
                               
                  // Дополнительная настройка для более стабильных вертикальных плоскостей
                  planeManagerConfig.requestedDetectionMode = UnityEngine.XR.ARSubsystems.PlaneDetectionMode.Vertical;
            }

            // Включаем режим привязки краски к AR-плоскостям
            wallPaintEffect.SetAttachToARPlanes(true);
            
            // КРИТИЧНО: Включаем режим отладки в WallPaintEffect для диагностики
            wallPaintEffect.debugMode = true;

            // Настраиваем начальные значения
            if (wallPaintEffect != null)
            {
                  wallPaintEffect.SetPaintColor(defaultWallColor);
                  wallPaintEffect.SetBlendFactor(defaultBlendFactor);
                  
                  // Принудительное обновление всех материалов
                  wallPaintEffect.ForceUpdateMaterial();
            }

            // Подписываемся на события изменения AR плоскостей, чтобы обновлять кэш
            ARPlaneManager planeManager = FindObjectOfType<ARPlaneManager>();
            if (planeManager != null)
            {
                  // ВАЖНО: Отписываемся перед подпиской, чтобы избежать двойных вызовов
                  planeManager.planesChanged -= OnPlanesChanged;
                  planeManager.planesChanged += OnPlanesChanged;
                  Debug.Log("ARWallPainter: Подписка на события ARPlaneManager.planesChanged выполнена");
            }
            else
            {
                  Debug.LogWarning("ARWallPainter: ARPlaneManager не найден! Некоторые функции могут работать некорректно.");
            }

            // Устанавливаем значения по умолчанию
            if (blendFactor <= 0)
            {
                  blendFactor = defaultBlendFactor;
            }
            
            if (selectedColor == Color.clear)
            {
                  selectedColor = defaultWallColor;
            }
            
            // Инициализация якорей для существующих плоскостей
            InitializeExistingPlanes();
            
            // ВАЖНО: Запускаем таймер для диагностики привязки плоскостей
            InvokeRepeating("DiagnoseARPlanes", 1.0f, 5.0f);
      }
      
      // Новый метод для диагностики плоскостей и якорей
      private void DiagnoseARPlanes()
      {
            ARPlane[] existingPlanes = FindObjectsOfType<ARPlane>();
            Debug.LogError($"ДИАГНОСТИКА AR-ПЛОСКОСТЕЙ: Найдено {existingPlanes.Length} плоскостей");
            
            foreach (ARPlane plane in existingPlanes)
            {
                  if (plane != null)
                  {
                        // Проверяем наличие якоря
                        ARAnchor attachedAnchor = plane.gameObject.GetComponent<ARAnchor>();
                        string anchorInfo = attachedAnchor != null ? 
                                          $"ARAnchor: {attachedAnchor.trackableId}, Pos: {attachedAnchor.transform.position}" : 
                                          "Якорь отсутствует!";
                        
                        // Проверяем материал
                        MeshRenderer renderer = plane.GetComponent<MeshRenderer>();
                        string materialInfo = "Нет рендерера";
                        if (renderer != null && renderer.material != null)
                        {
                              Material mat = renderer.material;
                              materialInfo = $"Шейдер: {mat.shader.name}, UseARSpace: {mat.IsKeywordEnabled("USE_AR_WORLD_SPACE")}";
                        }
                        
                        Debug.LogError($"ПЛОСКОСТЬ: {plane.trackableId}\n" +
                                     $"Позиция: {plane.transform.position}, Нормаль: {plane.normal}\n" +
                                     $"{anchorInfo}\n" +
                                     $"Материал: {materialInfo}");
                                     
                        // Если нет якоря, добавляем его
                        if (attachedAnchor == null)
                        {
                              attachedAnchor = plane.gameObject.AddComponent<ARAnchor>();
                              Debug.LogError($"ДОБАВЛЕН ЯКОРЬ к плоскости {plane.trackableId}");
                        }
                  }
            }
            
            // Проверяем согласованность нашего кэша
            Debug.LogError($"КЭШИ: planeCache: {planeCache.Count}, anchorCache: {anchorCache.Count}, paintedWalls: {paintedWalls.Count}");
      }

      // Метод для инициализации якорей для уже существующих плоскостей
      private void InitializeExistingPlanes()
      {
            ARPlane[] existingPlanes = FindObjectsOfType<ARPlane>();
            if (existingPlanes.Length > 0)
            {
                  Debug.Log($"ARWallPainter: Найдено {existingPlanes.Length} существующих AR плоскостей");
                  
                  foreach (ARPlane plane in existingPlanes)
                  {
                        // Добавляем плоскость в кэш
                        if (!planeCache.ContainsKey(plane.trackableId))
                        {
                              planeCache.Add(plane.trackableId, plane);
                              
                              // Создаем якорь для плоскости
                              if (IsVerticalPlane(plane))
                              {
                                    CreateOrGetAnchorForPlane(plane);
                              }
                        }
                  }
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
            
            // Обновляем матрицы трансформации для всех активных плоскостей
            UpdatePlaneTransforms();
      }
      
      // Новый метод для обновления матриц трансформации плоскостей
      private void UpdatePlaneTransforms()
      {
            planeMatrixCount = 0;
            
            foreach (var pair in planeCache)
            {
                  ARPlane plane = pair.Value;
                  if (plane != null && plane.gameObject.activeInHierarchy)
                  {
                        // Сохраняем мировую матрицу плоскости
                        if (planeMatrixCount < planeToWorldMatrices.Length)
                        {
                              planeToWorldMatrices[planeMatrixCount] = plane.transform.localToWorldMatrix;
                              planeMatrixCount++;
                        }
                  }
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
                        bool isVertical = IsVerticalPlane(plane);
                        
                        // Проверяем, является ли плоскость вертикальной (стеной)
                        // и применяем фильтр preferVerticalPlanesOnly, если он включен
                        if (!preferVerticalPlanesOnly || isVertical)
                        {
                              // Если это первое касание или новая плоскость
                              if (lastSelectedPlane != plane)
                              {
                                    lastSelectedPlane = plane;
                                    
                                    // Улучшенный метод применения цвета к плоскости с привязкой к мировым координатам
                                    ApplyColorToPlane(plane, hit.pose.position, selectedColor, blendFactor);
                                    
                                    // Добавляем отладочную визуализацию точки касания
                                    if (showDebugMarkers)
                                    {
                                          CreateDebugMarker(hit.pose.position);
                                    }
                              }

                              // Можем выполнить дополнительные действия при продолжающемся касании (например, изменение оттенка)
                              if (isTouching && !raycastOnlyOnTap)
                              {
                                    // Здесь может быть дополнительная логика для непрерывной покраски
                              }
                        }
                        else
                        {
                              Debug.Log("Плоскость проигнорирована, так как она не вертикальная, а preferVerticalPlanesOnly = true");
                  }
            }
            }
      }
      
      // Метод для создания визуального маркера в точке касания (для отладки)
      private void CreateDebugMarker(Vector3 position)
      {
            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            marker.transform.position = position;
            marker.transform.localScale = Vector3.one * 0.05f; // Маленький шарик
            
            // Создаем яркий материал для видимости
            Material markerMat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            markerMat.color = new Color(1f, 0.3f, 0.3f, 0.8f); // Яркий красный
            marker.GetComponent<Renderer>().material = markerMat;
            
            // Автоматически удаляем через 5 секунд
            Destroy(marker, 5f);
      }

      // Проверяем, является ли плоскость вертикальной (стеной)
      private bool IsVerticalPlane(ARPlane plane)
      {
            if (plane.alignment == PlaneAlignment.Vertical)
                  return true;
            
            // Дополнительная проверка по нормали (плоскость почти вертикальна)
            float dotUp = Vector3.Dot(plane.normal, Vector3.up);
            return Mathf.Abs(dotUp) < 0.25f; // Более строгое значение для определения вертикальности
      }

      // Получаем плоскость из результата рейкаста
      private ARPlane GetPlaneFromHit(ARRaycastHit hit)
      {
            // Пытаемся получить плоскость непосредственно из хита
            ARPlane plane = null;
            TrackableId trackableId = hit.trackableId;

            // Сначала проверяем кэш плоскостей
            if (planeCache.TryGetValue(trackableId, out plane))
            {
                  return plane;
            }

            // Если не нашли в кэше, ищем среди всех плоскостей
            ARPlane[] allPlanes = FindObjectsOfType<ARPlane>();
            foreach (ARPlane p in allPlanes)
            {
                  if (p.trackableId == trackableId)
                  {
                        plane = p;
                        // Добавляем найденную плоскость в кэш
                        planeCache[trackableId] = p;
                        break;
                  }
            }

            return plane;
      }

      // Улучшенный метод для применения цвета к конкретной плоскости
      private void ApplyColorToPlane(ARPlane plane, Vector3 hitPosition, Color color, float blend)
      {
            if (plane == null)
            {
                  Debug.LogWarning("ARWallPainter: Попытка применить цвет к null плоскости");
                  return;
            }

            // Создаем якорь для плоскости, чтобы закрепить эффект в AR пространстве
            ARAnchor anchor = CreateOrGetAnchorForPlane(plane);
            
            // Если якорь создан успешно
                  if (anchor != null)
            {
                  // Используем усовершенствованный метод WallPaintEffect для окрашивания плоскости
                  if (wallPaintEffect != null)
                  {
                        // Используем метод PaintPlane из WallPaintEffect для окрашивания AR плоскости
                        wallPaintEffect.PaintPlane(plane, color, blend);
                        Debug.Log($"ARWallPainter: Применен цвет к плоскости {plane.trackableId} с якорем {anchor.trackableId}");
                  }
                  else
                  {
                        // Если нет доступа к WallPaintEffect, используем прямое присвоение материала
                        MeshRenderer renderer = plane.GetComponent<MeshRenderer>();
                        if (renderer != null)
                        {
                              // Получаем или создаем материал для этой плоскости
            Material planeMaterial;
            if (!paintedWalls.TryGetValue(plane.trackableId, out planeMaterial))
            {
                                    // Создаем новый материал с привязкой к мировым координатам
                                    Material baseMaterial = new Material(Shader.Find("Custom/WallPaint"));
                                    planeMaterial = new Material(baseMaterial);
                                    paintedWalls[plane.trackableId] = planeMaterial;
                              }
                              
                              // Настраиваем свойства материала
                              planeMaterial.SetColor("_PaintColor", color);
                              planeMaterial.SetFloat("_BlendFactor", blend);
                              
                              // Включаем режим привязки к AR пространству
                              planeMaterial.EnableKeyword("USE_AR_WORLD_SPACE");
                              
                              // Устанавливаем материал для рендерера
                              renderer.material = planeMaterial;
                              
                              Debug.Log($"ARWallPainter: Применен материал напрямую к плоскости {plane.trackableId}");
                        }
                  }
            }
            else
                  {
                  Debug.LogWarning($"ARWallPainter: Не удалось создать якорь для плоскости {plane.trackableId}");
            }
      }
      
      // Улучшенный метод для создания или получения якоря для плоскости
      private ARAnchor CreateOrGetAnchorForPlane(ARPlane plane)
      {
            if (plane == null) return null;
            
            // ВАЖНОЕ ИЗМЕНЕНИЕ: Всегда проверяем, есть ли уже прикрепленный якорь
            ARAnchor attachedAnchor = plane.gameObject.GetComponent<ARAnchor>();
            if (attachedAnchor != null && attachedAnchor.gameObject.activeInHierarchy)
            {
                  Debug.Log($"ARWallPainter: Используем существующий якорь, прикрепленный к плоскости {plane.trackableId}");
                  // Обновляем кэш
                  anchorCache[plane.trackableId] = attachedAnchor;
                  
                  // Сохраняем данные, если нужно, даже для существующих якорей
                  if (persistPaintBetweenSessions)
                  {
                        SaveAnchorData(attachedAnchor, plane.trackableId);
                  }
                  
                  return attachedAnchor;
            }
            
            // Проверяем, есть ли уже якорь для этой плоскости в кэше
            ARAnchor anchor;
            if (anchorCache.TryGetValue(plane.trackableId, out anchor))
            {
                  // Проверяем, что якорь все еще активен
                  if (anchor != null && anchor.gameObject.activeInHierarchy)
                  {
                        // Сохраняем данные, если нужно
                        if (persistPaintBetweenSessions)
                        {
                              SaveAnchorData(anchor, plane.trackableId);
                        }
                        return anchor;
                  }
            }

            // ПРИНЦИПИАЛЬНОЕ ИЗМЕНЕНИЕ:
            // Вместо создания отдельного якоря, привязанного к плоскости,
            // напрямую добавляем компонент ARAnchor к самой плоскости.
            // Это обеспечит, что плоскость сама становится якорем и будет
            // стабильно привязана к реальному миру
            
            if (attachedAnchor == null)
            {
                  try
                  {
                        attachedAnchor = plane.gameObject.AddComponent<ARAnchor>();
                        Debug.LogError($"ЯКОРЬ: Создан прямой якорь для плоскости {plane.trackableId}");
                        
                        // Сохраняем в кэш для будущего использования
                        anchorCache[plane.trackableId] = attachedAnchor;
                        
                        // Сохраняем данные якоря, если включена соответствующая опция
                        if (persistPaintBetweenSessions)
                        {
                              SaveAnchorData(attachedAnchor, plane.trackableId);
                        }
                        
                        // Отладочная информация
                        Debug.LogError($"ЯКОРЬ INFO: position={attachedAnchor.transform.position}, rotation={attachedAnchor.transform.rotation.eulerAngles}");
                        
                        return attachedAnchor;
                  }
                  catch (System.Exception ex)
                  {
                        Debug.LogError($"ОШИБКА создания якоря: {ex.Message}");
                        return null;
                  }
            }
            
            // Проблема - код не должен дойти до этой точки
            Debug.LogError($"НЕОЖИДАННАЯ СИТУАЦИЯ: не смогли создать или найти якорь для {plane.trackableId}");
            return null;
      }
      
      // Метод для сохранения данных о якоре для будущего использования
      private void SaveAnchorData(ARAnchor anchor, TrackableId planeId)
      {
            // Сохраняем данные только если включена опция persistPaintBetweenSessions
            if (!persistPaintBetweenSessions) return;
            
            // В реальном приложении здесь можно сохранить данные в PlayerPrefs или файл
            string anchorKey = $"anchor_{planeId}";
            string colorKey = $"color_{planeId}";
            string blendKey = $"blend_{planeId}";
            
            try
            {
                  // Сохраняем позицию и поворот якоря
                  string positionData = JsonUtility.ToJson(anchor.transform.position);
                  string rotationData = JsonUtility.ToJson(anchor.transform.rotation);
                  PlayerPrefs.SetString(anchorKey + "_position", positionData);
                  PlayerPrefs.SetString(anchorKey + "_rotation", rotationData);
                  
                  // Сохраняем текущий цвет и прозрачность
                  string colorData = JsonUtility.ToJson(selectedColor);
                  PlayerPrefs.SetString(colorKey, colorData);
                  PlayerPrefs.SetFloat(blendKey, blendFactor);
                  
                  PlayerPrefs.Save();
                  Debug.Log($"ARWallPainter: Сохранены данные якоря {anchor.trackableId} для плоскости {planeId}");
            }
            catch (System.Exception ex)
            {
                  Debug.LogError($"Ошибка при сохранении данных якоря: {ex.Message}");
            }
      }

      // Обработчик изменений AR плоскостей
      private void OnPlanesChanged(ARPlanesChangedEventArgs args)
      {
            // Обрабатываем добавленные плоскости
            foreach (ARPlane plane in args.added)
            {
                  // Добавляем в кэш
                  planeCache[plane.trackableId] = plane;
                  
                  // Если это вертикальная плоскость, автоматически создаем для нее якорь
                  if (IsVerticalPlane(plane))
                  {
                        Debug.Log($"ARWallPainter: Обнаружена новая вертикальная плоскость {plane.trackableId}");
                        CreateOrGetAnchorForPlane(plane);
                  }
            }
            
            // Обрабатываем обновленные плоскости
            foreach (ARPlane plane in args.updated)
            {
                  // Обновляем в кэше
                  planeCache[plane.trackableId] = plane;
                  
                  // Проверяем, изменился ли тип плоскости (например, из горизонтальной в вертикальную)
                  if (IsVerticalPlane(plane) && !anchorCache.ContainsKey(plane.trackableId))
                  {
                        Debug.Log($"ARWallPainter: Плоскость {plane.trackableId} стала вертикальной после обновления");
                        CreateOrGetAnchorForPlane(plane);
                  }
            }
            
            // Обрабатываем удаленные плоскости
            foreach (ARPlane plane in args.removed)
            {
                  TrackableId planeId = plane.trackableId;
                  
                  // Удаляем из кэша плоскостей
                  planeCache.Remove(planeId);
                  
                  // Удаляем связанный якорь если он есть
                  if (anchorCache.TryGetValue(planeId, out ARAnchor anchor) && anchor != null)
                  {
                        Debug.Log($"ARWallPainter: Удаляем якорь для удаленной плоскости {planeId}");
                        anchorCache.Remove(planeId);
                  }
                  
                  // Удаляем материал из словаря покрашенных стен
                  paintedWalls.Remove(planeId);
            }
      }

      // Публичный метод для установки цвета покраски
      public void SetPaintColor(Color color)
      {
            selectedColor = color;
            
            // Если есть WallPaintEffect, обновляем цвет и там
            if (wallPaintEffect != null)
            {
                  wallPaintEffect.SetPaintColor(color);
            }
            
            // Обновляем все ранее покрашенные стены
            UpdateAllPaintedWalls();
      }

      // Публичный метод для установки фактора смешивания
      public void SetBlendFactor(float factor)
      {
            blendFactor = Mathf.Clamp01(factor);
            
            // Если есть WallPaintEffect, обновляем фактор и там
            if (wallPaintEffect != null)
            {
                  wallPaintEffect.SetBlendFactor(factor);
            }
            
            // Обновляем все ранее покрашенные стены
            UpdateAllPaintedWalls();
      }

      // Метод для обновления всех покрашенных стен
      private void UpdateAllPaintedWalls()
      {
            // Обновляем каждую покрашенную стену
            foreach (var pair in paintedWalls)
            {
                  TrackableId planeId = pair.Key;
                  Material material = pair.Value;
                  
                  // Пытаемся найти плоскость по ID
                  ARPlane plane = GetPlaneFromCache(planeId);
                  if (plane != null)
                  {
                        // Обновляем настройки материала
                        material.SetColor("_PaintColor", selectedColor);
                        material.SetFloat("_BlendFactor", blendFactor);
                  }
            }
      }

      // Метод для сброса всех покрашенных стен
      public void ResetAllWalls()
      {
            // Очищаем все словари
            paintedWalls.Clear();
            
            // Не удаляем якоря, так как они могут использоваться для других целей
            
            // Сбрасываем последнюю выбранную плоскость
            lastSelectedPlane = null;
            
            // Если есть WallPaintEffect, запрашиваем принудительное обновление
            if (wallPaintEffect != null)
                  {
                  wallPaintEffect.ForceRefreshARPlanes();
                  }
            
            Debug.Log("ARWallPainter: Все стены сброшены");
      }

      // Публичный метод для выполнения рейкаста из указанной точки экрана
      public void PerformRaycast(Vector2 screenPosition)
      {
            HandleTouchInteraction(screenPosition);
                  }
                  
      // Получение плоскости из кэша по ID
      private ARPlane GetPlaneFromCache(TrackableId trackableId)
      {
            ARPlane plane;
            if (planeCache.TryGetValue(trackableId, out plane))
            {
                  return plane;
            }
            
            // Если не нашли в кэше, пробуем найти среди всех плоскостей (метод запасной)
            ARPlane[] allPlanes = FindObjectsOfType<ARPlane>();
            foreach (ARPlane p in allPlanes)
            {
                  if (p.trackableId == trackableId)
                  {
                        // Добавляем в кэш и возвращаем
                        planeCache[trackableId] = p;
                        return p;
                  }
            }
            
            return null;
      }

      private void OnDestroy()
      {
            // Отписываемся от событий ARPlaneManager при уничтожении объекта
            ARPlaneManager planeManager = FindObjectOfType<ARPlaneManager>();
            if (planeManager != null)
            {
                  planeManager.planesChanged -= OnPlanesChanged;
            }
            
            // Очищаем ресурсы
            foreach (var material in paintedWalls.Values)
            {
                  if (material != null)
                  {
                        // Уничтожаем материалы, созданные в рантайме
                        Destroy(material);
                  }
            }
            
            paintedWalls.Clear();
            planeCache.Clear();
            anchorCache.Clear();
      }
}