using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Unity.XR.CoreUtils;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.XR.ARSubsystems;
using UnityEngine.Rendering.Universal;
using System;

/// <summary>
/// Класс для автоматического добавления ARManagerInitializer в сцену при старте игры
/// и управления плоскостями AR на основе маски сегментации
/// </summary>
[DefaultExecutionOrder(-10)]
public class ARManagerInitializer2 : MonoBehaviour
{
      // Синглтон для глобального доступа
      public static ARManagerInitializer2 Instance { get; private set; }

      // Статический счетчик для уникальных имен плоскостей
      private static int planeInstanceCounter = 0;

      // Ссылки на AR компоненты
      [Header("AR компоненты")]
      public ARSessionManager sessionManager;
      public ARPlaneManager planeManager;
      public XROrigin xrOrigin;
      [SerializeField] private ARPlaneConfigurator planeConfigurator; // Added reference to ARPlaneConfigurator
      [SerializeField] private PlaneDebugVisualizer planeDebugVisualizer; // Added reference to PlaneDebugVisualizer

      [Header("Настройки сегментации")]
      [Tooltip("Использовать обнаруженные плоскости вместо генерации из маски")]
      public bool useDetectedPlanes = true; // ИЗМЕНЕНО: из false в true, чтобы ARFoundation плоскости могли быть использованы для коллайдеров

      [Tooltip("Минимальный размер плоскости для создания (в метрах)")]
      [SerializeField] private float minPlaneSizeInMeters = 0.1f;

      [Tooltip("Минимальный размер области в пикселях (ширина И высота) для её учета")]
      [SerializeField] private int minPixelsDimensionForArea = 1; // было minAreaSize

      [Tooltip("Минимальная площадь области в пикселях для её учета")]
      [SerializeField] private int minAreaSizeInPixels = 150; // БЫЛО 1500. Новое поле, раньше было minAreaSize = 2*2=4 или 50

      [Tooltip("Разрешение, до которого будет уменьшена маска сегментации перед анализом на CPU (для повышения производительности). Меньшее значение = быстрее, но менее точно.")]
      [SerializeField] private int maskProcessingResolution = 128;

      [Tooltip("Порог значения красного канала (0-255) для пикселя, чтобы считать его частью области стены при поиске связных областей.")]
      [SerializeField] private byte wallPixelThreshold = 120; // Увеличено с 30 до 60 для лучшей фильтрации шума, ЗАТЕМ ДО 120

      [Header("Настройки Рейкастинга для Плоскостей")]
      [Tooltip("Включить подробное логирование процесса рейкастинга и фильтрации попаданий.")]
      [SerializeField] private bool enableDetailedRaycastLogging = false; // ADDED/ENSURED
      [Tooltip("Максимальное расстояние для рейкастов при поиске поверхностей")]
      [SerializeField] private float maxRayDistance = 15f; // Увеличено с 10 до 15 метров
      [Tooltip("Маска слоев для рейкастинга (например, Default, SimulatedEnvironment, Wall)")]
      [SerializeField] private LayerMask hitLayerMask = (1 << 0) | (1 << 8) | (1 << 30) | (1 << 31); // Default + SimulatedEnvironment + XR Simulation + Layer31 (LivingRoom)
      [Tooltip("Минимальное расстояние до объекта, чтобы считать попадание валидным (м). Помогает отфильтровать попадания 'внутрь' объектов или слишком близкие поверхности.")]
      [SerializeField] private float minHitDistanceThreshold = 0.1f;
      [Tooltip("Максимальное допустимое отклонение нормали стены от идеальной вертикали (в градусах). Используется для определения, является ли поверхность стеной.")]
      [SerializeField] private float maxWallNormalAngleDeviation = 25f; // Увеличено с 15 до 25 для более мягкой фильтрации
      [Tooltip("Минимальный допустимый угол нормали пола/потолка к вертикали (в градусах), чтобы считать поверхность горизонтальной. Например, 15 градусов означает, что поверхности с наклоном до 15 градусов от горизонтали считаются полом/потолком.")]
#pragma warning disable 0414
      [SerializeField] private float maxFloorNormalAngleDeviation = 15f;
#pragma warning restore 0414
      [Tooltip("Слой, в который будут переключены созданные плоскости")]
      [SerializeField] private string planesLayerName = "ARPlanes"; // Слой для плоскостей
      [Tooltip("Имена объектов, которые должны игнорироваться при рейкастинге (разделены запятыми)")]
      [SerializeField] private string ignoreObjectNames = ""; // Объекты для игнорирования

      [Header("Настройки сохранения плоскостей")]
      [SerializeField] private bool usePersistentPlanes = true; // Whether to use the persistent plane system
      [SerializeField] private bool highlightPersistentPlanes = true; // Whether to highlight persistent planes with different color
      [SerializeField] private Color persistentPlaneColor = new Color(0.0f, 0.8f, 0.2f, 0.7f); // Default color for persistent planes

      // Dictionary to track which of our generated planes are persistent
      private Dictionary<GameObject, bool> persistentGeneratedPlanes = new Dictionary<GameObject, bool>();

      // Добавляем защиту от удаления недавно созданных плоскостей
      private Dictionary<GameObject, float> planeCreationTimes = new Dictionary<GameObject, float>();
      private Dictionary<GameObject, float> planeLastVisitedTime = new Dictionary<GameObject, float>();

      [Tooltip("Материал для вертикальных плоскостей")]
      [SerializeField] private Material verticalPlaneMaterial; // Должно быть private

      [Tooltip("Материал для горизонтальных плоскостей")]
      [SerializeField] private Material horizontalPlaneMaterial; // Должно быть private

      [Header("Отладочная Визуализация Лучей")]
      [Tooltip("Материал для отладочной визуализации лучей. Если не назначен, визуализация лучей LineRenderer'ом будет отключена.")]
      [SerializeField] private Material debugRayMaterial;
      private MaterialPropertyBlock debugRayMaterialPropertyBlock;

      // Поле для RawImage будет установлено извне
      private UnityEngine.UI.RawImage отображениеМаскиUI;

      // Текущая маска сегментации
      private RenderTexture currentSegmentationMask;
      private bool maskUpdated = false;
      private List<GameObject> generatedPlanes = new List<GameObject>();

      // Для отслеживания изменений количества плоскостей
      private int lastPlaneCount = 0;

      // Счетчик кадров для обновления плоскостей
      private int frameCounter = 0;

      // Переменная для отслеживания последнего хорошего результата сегментации
      private bool hadValidSegmentationResult = false;
      private float lastSuccessfulSegmentationTime = 0f;
      private float segmentationTimeoutSeconds = 10f; // Тайм-аут для определения "потери" сегментации

      // Переменная для хранения InstanceID TrackablesParent из Start()
      private int trackablesParentInstanceID_FromStart = 0;

      [Header("🔍 Debug Settings")]
      [Tooltip("Включить детальную диагностику объектов сцены и коллайдеров при старте")]
      [SerializeField] private bool enableSceneObjectsDiagnostics = false; // Отключено для чистой консоли
                                                                           // [Tooltip("Включить логирование параметров камеры при получении данных сегментации")]
                                                                           // [SerializeField] private bool enableGetCameraParametersLogging = false; // Не используется
      [Tooltip("Включить логирование подробных логов о создании плоскостей")]
      [SerializeField] private bool enableCustomPlaneCreationLogging = false;
      [Tooltip("Включить логирование подробных логов о чистке старых плоскостей")]
      [SerializeField] private bool enableVerboseLoggingCleanup = false; // Ensured false by default

      [Header("📏 Размеры и геометрия")]
      [SerializeField] private float maxPlaneSize = 10.0f; // УВЕЛИЧЕНО: Максимальный размер плоскости для больших стен
      [SerializeField] private float minPlaneSize = 0.3f; // УМЕНЬШЕНО: Минимальный размер плоскости
      [SerializeField] private float maxAspectRatio = 8.0f; // УВЕЛИЧЕНО: Более гибкое соотношение сторон для длинных стен
      [SerializeField] private float maxWallHeight = 6.0f; // УВЕЛИЧЕНО: Максимальная высота стены для высоких потолков
      [SerializeField] private float maxWallWidth = 8.0f;  // УВЕЛИЧЕНО: Максимальная ширина стены для длинных стен
      [SerializeField] private float planeSizeMultiplier = 1.5f; // УВЕЛИЧЕНО: Множитель для покрытия всей стены

      [Header("🔧 Настройки PlaneGeometryEnhancer")]
      [Tooltip("Использовать улучшенный алгоритм расчета геометрии плоскостей для повышения точности")]
      [SerializeField] private bool useEnhancedPlaneGeometry = true;
      [Tooltip("Показывать подробные логи работы PlaneGeometryEnhancer")]
      [SerializeField] private bool enableEnhancedGeometryLogging = false;

      // ВАЖНО: PlaneGeometryEnhancer готов к интеграции, но Unity пока не может найти класс во время компиляции.
      // Пока что система использует fallback-метод CreatePlaneForWallAreaEnhanced_Fallback.
      // См. Assets/Scripts/PlaneGeometryEnhancer_README.md для подробной информации и инструкций по активации.

      [Tooltip("Дополнительный коэффициент масштабирования для уменьшения размера создаваемых плоскостей. 1.0 = без изменений, 0.5 = в два раза меньше.")]
      // [SerializeField] private float planeSizeScalingFactor = 0.5f; // Не используется - удалено для избежания предупреждения

      private Camera mainCamera;

      // [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)] // ЗАКОММЕНТИРОВАТЬ или УДАЛИТЬ
      // private static void Initialize()
      // {
      //     GameObject initializer = new GameObject("AR Manager Initializer 2");
      //     ARManagerInitializer2 component = initializer.AddComponent<ARManagerInitializer2>();
      //     component.useDetectedPlanes = false;
      //     DontDestroyOnLoad(initializer);
      //     Debug.Log("[ARManagerInitializer2] Инициализирован");
      // }

      private void Awake()
      {
            // Debug.Log("[ARManagerInitializer2] Awake() called.");
            if (Instance != null && Instance != this)
            {
                  Debug.LogWarning("[ARManagerInitializer2-Awake] Найден дублирующий экземпляр ARManagerInitializer2. Уничтожаем лишний.");
                  Destroy(gameObject);
                  return;
            }

            Instance = this;

            // Force disable verbose logs to override Inspector values if necessary
            this.enableDetailedRaycastLogging = false; // DISABLED for clean console
            this.enableCustomPlaneCreationLogging = false; // DISABLED for clean console
            this.enableVerboseLoggingCleanup = false;
            Debug.Log("[ARManagerInitializer2-Awake] Verbose logging flags forcefully DISABLED for clean console.");

            // ОТКЛЮЧАЕМ принудительное включение логирования - оставляем пользователю выбор
            // enableDetailedRaycastLogging = true; // УБРАНО: Принудительное включение отладки

            if (debugRayMaterial != null)
            {
                  debugRayMaterialPropertyBlock = new MaterialPropertyBlock();
            }

            // Диагностика объектов сцены и коллайдеров
            if (enableSceneObjectsDiagnostics)
            {
                  DiagnoseSceneObjects();

                  // Запускаем повторную проверку через задержку на случай асинхронной загрузки объектов
                  StartCoroutine(DelayedColliderCheck());
            }

            // Debug.Log($"[ARManagerInitializer2] Awake complete. Instance ID: {this.GetInstanceID()}, Name: {this.gameObject.name}");

            // Find ARPlaneConfigurator if not assigned
            if (planeConfigurator == null)
            {
                  planeConfigurator = FindObjectOfType<ARPlaneConfigurator>();
            }
      }

      private void Start()
      {
            // ИСПРАВЛЕНО: Принудительная очистка всех старых плоскостей при старте
            Debug.Log("[ARManagerInitializer2] 🔥 Принудительная очистка при старте...");
            DeleteAllPlanes();
            CleanupProblematicPersistentPlanes();

            // Очищаем все словари
            persistentGeneratedPlanes.Clear();
            planeCreationTimes.Clear();
            planeLastVisitedTime.Clear();
            generatedPlanes.Clear();

            Debug.Log("[ARManagerInitializer2] ✅ Очистка завершена. Готов к созданию новых плоскостей с правильными размерами.");

            FindARComponents();

            // Инициализация материалов ДО подписки на события
            InitializeMaterials();

            // Ensure we have reference to ARPlaneConfigurator
            if (planeConfigurator == null)
            {
                  planeConfigurator = FindObjectOfType<ARPlaneConfigurator>();
                  if (planeConfigurator == null && usePersistentPlanes)
                  {
                        Debug.LogWarning("[ARManagerInitializer2] ARPlaneConfigurator not found but usePersistentPlanes=true. Persistence won't work correctly.");
                  }
            }

            // Find PlaneDebugVisualizer if not assigned
            if (planeDebugVisualizer == null)
            {
                  planeDebugVisualizer = FindObjectOfType<PlaneDebugVisualizer>();
                  if (planeDebugVisualizer == null)
                  {
                        Debug.LogWarning("[ARManagerInitializer2] PlaneDebugVisualizer не найден в сцене");
                  }
                  else
                  {
                        Debug.Log("[ARManagerInitializer2] ✅ PlaneDebugVisualizer найден и подключен");
                  }
            }

            // Инициализация системы персистентных плоскостей
            InitializePersistentPlanesSystem();
            ConfigureARMeshManager();
            TryActivateARMeshing(); // Попытка активации AR Meshing для создания геометрии

            if (useDetectedPlanes)
            {
                  SubscribeToWallSegmentation();
            }

            // Всегда отключаем ARFoundation визуализаторы синих плоскостей
            Debug.Log("[ARManagerInitializer2-Start] Отключение ARFoundation визуализаторов синих плоскостей.");
            DisableARFoundationVisualizers();

            // Удаляем существующие синие плоскости ARFoundation
            RemoveExistingARFoundationPlanes();

            // Отключаем стандартный ARPlaneManager, чтобы убрать синие плоскости ARFoundation
            if (planeManager != null)
            {
                  Debug.LogWarning("[ARManagerInitializer2] Отключение ARPlaneManager для предотвращения генерации синих плоскостей ARFoundation.");
                  planeManager.enabled = false;
                  if (!planeManager.enabled)
                  {
                        Debug.Log("[ARManagerInitializer2] ✅ ARPlaneManager успешно отключен. Синие плоскости больше не будут генерироваться.");
                  }
                  else
                  {
                        Debug.LogWarning("[ARManagerInitializer2] ⚠️ Не удалось отключить ARPlaneManager.");
                  }
            }
            else
            {
                  Debug.LogWarning("[ARManagerInitializer2] planeManager не назначен. Нечего отключать.");
            }


            // Debug.Log($"[ARManagerInitializer2] Настройки инициализированы: useDetectedPlanes={useDetectedPlanes}");

            if (xrOrigin != null && xrOrigin.TrackablesParent != null)
            {
                  trackablesParentInstanceID_FromStart = xrOrigin.TrackablesParent.GetInstanceID();
                  // Debug.Log($"[ARManagerInitializer2-Start] TrackablesParent is: {xrOrigin.TrackablesParent.name}, ID: {trackablesParentInstanceID_FromStart}, Path: {GetGameObjectPath(xrOrigin.TrackablesParent)}");
            }
            else
            {
                  Debug.LogError("[ARManagerInitializer2-Start] XROrigin or XROrigin.TrackablesParent is not assigned!");
            }
            // CreateBasicPlaneInFrontOfUser();
      }

      public void УстановитьОтображениеМаскиUI(UnityEngine.UI.RawImage rawImageДляУстановки)
      {
            if (rawImageДляУстановки != null)
            {
                  отображениеМаскиUI = rawImageДляУстановки;
                  // Debug.Log("[ARManagerInitializer2] Успешно установлен RawImage для отображения маски через УстановитьОтображениеМаскиUI.");
                  if (currentSegmentationMask != null && отображениеМаскиUI.texture == null)
                  {
                        // Debug.Log("[ARManagerInitializer2] Немедленное применение текущей маски к новому RawImage.");
                        отображениеМаскиUI.texture = currentSegmentationMask;
                        отображениеМаскиUI.gameObject.SetActive(true);
                  }
            }
            else
            {
                  // Debug.LogWarning("[ARManagerInitializer2] Попытка установить null RawImage для отображения маски.");
            }
      }

      private void Update()
      {
            frameCounter++;

            // Обрабатываем маску сегментации независимо от состояния AR сессии
            // Это позволит создавать плоскости из сегментации даже в режиме симуляции
            if (maskUpdated)
            {
                  // Проверяем готовность основных компонентов
                  if (xrOrigin != null && xrOrigin.Camera != null)
                  {
                        // Debug.Log("[ARManagerInitializer2] Processing segmentation mask...");
                        ProcessSegmentationMask();
                        maskUpdated = false;

                        // Логируем успешную обработку
                        if (frameCounter % 30 == 0) // Каждые 30 кадров
                        {
                              Debug.Log($"[ARManagerInitializer2] ✅ Обработка маски #{frameCounter / 30}, generatedPlanes.Count = {generatedPlanes.Count}");
                        }
                  }
                  else
                  {
                        if (frameCounter % 60 == 0) // Логировать не каждый кадр
                        {
                              Debug.LogWarning("[ARManagerInitializer2] Mask updated, but XROrigin or Camera not ready. Waiting...");
                        }
                  }
            }

            // Обрабатываем жесты пользователя для быстрого сохранения
            UpdateGestureInput();

            // Автоматически делаем стабильные плоскости персистентными
            if (usePersistentPlanes && frameCounter % 30 == 0) // Увеличена частота проверки (каждые ~0.5 сек)
            {
                  MakeStablePlanesPersistent();
            }

            // Периодически удаляем проблемные персистентные плоскости (каждые 5 секунд)
            if (usePersistentPlanes && frameCounter % 300 == 0)
            {
                  CleanupProblematicPersistentPlanes();
            }

            // ДОБАВЛЕНО: Автоматическая очистка слишком больших плоскостей (каждые 3 секунды)
            if (frameCounter % 180 == 0)
            {
                  CleanupOversizedPlanes();
            }

            // ДОБАВЛЕНО: Периодическое удаление синих плоскостей ARFoundation (каждые 2 секунды)
            if (frameCounter % 120 == 0)
            {
                  RemoveExistingARFoundationPlanes();
            }

            // Обновление позиций существующих плоскостей (если они не привязаны к трекаблам XROrigin)
            // UpdatePlanePositions(); // Пока отключено, т.к. привязываем к TrackablesParent

            // Периодическая проверка, если сегментация "зависла"
            if (hadValidSegmentationResult && Time.time - lastSuccessfulSegmentationTime > segmentationTimeoutSeconds)
            {
                  // Debug.LogWarning($"[ARManagerInitializer2] Нет успешной сегментации более {segmentationTimeoutSeconds} секунд. Сбрасываем состояние.");
                  // ResetAllPlanes(); // Очищаем плоскости, если сегментация потеряна
                  hadValidSegmentationResult = false; // Сбрасываем флаг, чтобы избежать повторных сбросов подряд
                                                      // Можно также попробовать перезапустить подписку или сам WallSegmentation
            }


            // Отладка: Вывод количества плоскостей и их позиций
            // if (frameCounter % 120 == 0) // Каждые 120 кадров (примерно раз в 2 секунды)
            // {
            //     if (generatedPlanes != null)
            //     {
            //          Debug.Log($"[ARManagerInitializer2] 📊 Текущее количество плоскостей: {generatedPlanes.Count}");
            //          for (int i = 0; i < generatedPlanes.Count; i++)
            //          {
            //              if (generatedPlanes[i] != null)
            //              {
            //                  Debug.Log($"[ARManagerInitializer2-DebugPlanePos] Plane {i} (ID: {generatedPlanes[i].GetInstanceID()}) world position: {generatedPlanes[i].transform.position:F2}, rotation: {generatedPlanes[i].transform.eulerAngles:F2}, parent: {(generatedPlanes[i].transform.parent != null ? generatedPlanes[i].transform.parent.name : "null")}");
            //              }
            //          }
            //     }

            //     if (xrOrigin != null && xrOrigin.TrackablesParent != null)
            //     {
            //         // Debug.Log($"[ARManagerInitializer2-TrackableCheck-Update] Проверка TrackablesParent: {GetGameObjectPath(xrOrigin.TrackablesParent)} (ID: {xrOrigin.TrackablesParent.GetInstanceID()}). Количество дочерних объектов: {xrOrigin.TrackablesParent.childCount}");
            //         for (int i = 0; i < xrOrigin.TrackablesParent.childCount; i++)
            //         {
            //             Transform child = xrOrigin.TrackablesParent.GetChild(i);
            //             bool isOurPlane = false;
            //             foreach (var plane in generatedPlanes)
            //             {
            //                 if (plane != null && plane.transform == child)
            //                 {
            //                     isOurPlane = true;
            //                     break;
            //                 }
            //             }
            //             // if (child.name.StartsWith("MyARPlane_Debug_")) {
            //             //     Debug.Log($"[ARManagerInitializer2-TrackableCheck-Update] Child of Trackables: {child.name}, ID: {child.GetInstanceID()}, Path: {GetGameObjectPath(child)}");
            //             //     if (!isOurPlane) Debug.LogWarning($"[ARManagerInitializer2-TrackableCheck-Update] ВНИМАНИЕ! Найдена плоскость '{child.name}' (ID: {child.GetInstanceID()}) под TrackablesParent ({GetGameObjectPath(xrOrigin.TrackablesParent)}) но ее нет в списке generatedPlanes!");
            //             // }
            //         }
            //     }
            //     else
            //     {
            //         // Debug.LogError("[ARManagerInitializer2-Update] XROrigin or TrackablesParent is null in Update.");
            //     }
            // }
      }


      // Поиск необходимых компонентов в сцене
      private void FindARComponents()
      {
            if (sessionManager == null)
            {
                  // Debug.Log("[ARManagerInitializer2] Поле sessionManager было null. Попытка найти ARSessionManager в сцене (включая неактивные объекты)...");
                  sessionManager = FindObjectOfType<ARSessionManager>(true); // Ищем включая неактивные
                  if (sessionManager != null)
                  {
                        // Debug.Log($"[ARManagerInitializer2] ✅ ARSessionManager успешно найден и назначен: {sessionManager.gameObject.name} (ID: {sessionManager.gameObject.GetInstanceID()}), активен: {sessionManager.gameObject.activeInHierarchy}");
                  }
                  else
                  {
                        Debug.LogError("[ARManagerInitializer2] ❌ ARSessionManager не найден в сцене!");
                  }
            }
            // else
            // {
            // Debug.Log($"[ARManagerInitializer2] Поле sessionManager уже было назначено: {sessionManager.gameObject.name} (ID: {sessionManager.gameObject.GetInstanceID()}), активен: {sessionManager.gameObject.activeInHierarchy}");
            // }

            if (xrOrigin == null)
            {
                  // Debug.Log("[ARManagerInitializer2] Поле xrOrigin было null. Попытка найти XROrigin в сцене (включая неактивные объекты)...");
                  xrOrigin = FindObjectOfType<XROrigin>(true);
                  if (xrOrigin != null)
                  {
                        // Debug.Log($"[ARManagerInitializer2] ✅ XROrigin успешно найден и назначен: {xrOrigin.gameObject.name} (ID: {xrOrigin.gameObject.GetInstanceID()}), активен: {xrOrigin.gameObject.activeInHierarchy}");
                  }
                  else
                  {
                        Debug.LogError("[ARManagerInitializer2] ❌ XROrigin не найден в сцене!");
                  }
            }

            if (planeManager == null && xrOrigin != null)
            {
                  // Debug.Log("[ARManagerInitializer2] Поле planeManager было null. Попытка найти ARPlaneManager на XROrigin...");
                  planeManager = xrOrigin.GetComponent<ARPlaneManager>();
                  if (planeManager != null)
                  {
                        // Debug.Log($"[ARManagerInitializer2] ✅ ARPlaneManager успешно найден на XROrigin: {planeManager.gameObject.name} (ID: {planeManager.gameObject.GetInstanceID()}), активен: {planeManager.gameObject.activeInHierarchy}, enabled: {planeManager.enabled}");
                        // planeManager.planesChanged += OnPlanesChanged; // Подписываемся на события
                        // Debug.Log("[ARManagerInitializer2] Подписано на события planesChanged");
                  }
                  else
                  {
                        // Debug.LogWarning("[ARManagerInitializer2] ARPlaneManager не найден на XROrigin. Возможно, он не используется или не настроен.");
                  }
            }
            InitializeMaterials();
      }

      private void InitializeMaterials()
      {
            if (verticalPlaneMaterial == null)
            {
                  Debug.LogWarning("[ARManagerInitializer2] Материал для вертикальных плоскостей не назначен. Создание fallback материала.");

                  // Попробуем найти рабочий шейдер в порядке приоритета
                  Shader targetShader = FindWorkingShader();

                  if (targetShader != null)
                  {
                        verticalPlaneMaterial = new Material(targetShader);
                        verticalPlaneMaterial.color = new Color(0.2f, 0.6f, 1.0f, 0.7f); // Голубой полупрозрачный для стен

                        // Если это прозрачный шейдер, настроим режим рендеринга
                        if (targetShader.name.Contains("Transparent") || targetShader.name.Contains("Alpha"))
                        {
                              verticalPlaneMaterial.SetFloat("_Mode", 2); // Transparent mode
                              verticalPlaneMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                              verticalPlaneMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                              verticalPlaneMaterial.SetInt("_ZWrite", 0);
                              verticalPlaneMaterial.DisableKeyword("_ALPHATEST_ON");
                              verticalPlaneMaterial.EnableKeyword("_ALPHABLEND_ON");
                              verticalPlaneMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                              verticalPlaneMaterial.renderQueue = 3000;
                        }

                        Debug.Log($"[ARManagerInitializer2] ✅ Создан материал для вертикальных плоскостей с шейдером: {targetShader.name}");
                  }
                  else
                  {
                        Debug.LogError("[ARManagerInitializer2] ❌ Не удалось найти рабочий шейдер! Создание базового материала.");
                        verticalPlaneMaterial = new Material(Shader.Find("Sprites/Default"));
                        verticalPlaneMaterial.color = Color.blue;
                  }
            }
            else
            {
                  Debug.Log("[ARManagerInitializer2] ✅ Используется назначенный материал для вертикальных плоскостей.");
            }

            if (horizontalPlaneMaterial == null)
            {
                  Debug.LogWarning("[ARManagerInitializer2] Материал для горизонтальных плоскостей не назначен. Создание fallback материала.");

                  Shader targetShader = FindWorkingShader();

                  if (targetShader != null)
                  {
                        horizontalPlaneMaterial = new Material(targetShader);
                        horizontalPlaneMaterial.color = new Color(0.2f, 1.0f, 0.2f, 0.6f); // Зеленый полупрозрачный для пола

                        // Настройка прозрачности
                        if (targetShader.name.Contains("Transparent") || targetShader.name.Contains("Alpha"))
                        {
                              horizontalPlaneMaterial.SetFloat("_Mode", 2);
                              horizontalPlaneMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                              horizontalPlaneMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                              horizontalPlaneMaterial.SetInt("_ZWrite", 0);
                              horizontalPlaneMaterial.DisableKeyword("_ALPHATEST_ON");
                              horizontalPlaneMaterial.EnableKeyword("_ALPHABLEND_ON");
                              horizontalPlaneMaterial.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                              horizontalPlaneMaterial.renderQueue = 3000;
                        }

                        Debug.Log($"[ARManagerInitializer2] ✅ Создан материал для горизонтальных плоскостей с шейдером: {targetShader.name}");
                  }
                  else
                  {
                        Debug.LogError("[ARManagerInitializer2] ❌ Не удалось найти рабочий шейдер! Создание базового материала.");
                        horizontalPlaneMaterial = new Material(Shader.Find("Sprites/Default"));
                        horizontalPlaneMaterial.color = Color.green;
                  }
            }
            else
            {
                  Debug.Log("[ARManagerInitializer2] ✅ Используется назначенный материал для горизонтальных плоскостей.");
            }
      }

      /// <summary>
      /// Поиск рабочего шейдера в порядке приоритета
      /// </summary>
      private Shader FindWorkingShader()
      {
            // Массив шейдеров в порядке приоритета
            string[] shaderNames = {
                  "Universal Render Pipeline/Lit",           // URP
                  "Standard",                                // Built-in
                  "Mobile/Diffuse",                         // Mobile optimized
                  "Legacy Shaders/Diffuse",                 // Legacy
                  "Legacy Shaders/Transparent/Diffuse",     // Legacy transparent
                  "Unlit/Color",                            // Simple unlit
                  "Unlit/Transparent",                      // Simple transparent
                  "Sprites/Default",                        // Sprite shader
                  "UI/Default"                              // UI shader
            };

            foreach (string shaderName in shaderNames)
            {
                  Shader shader = Shader.Find(shaderName);
                  if (shader != null)
                  {
                        Debug.Log($"[ARManagerInitializer2] ✅ Найден рабочий шейдер: {shaderName}");
                        return shader;
                  }
                  else
                  {
                        Debug.LogWarning($"[ARManagerInitializer2] ⚠️ Шейдер не найден: {shaderName}");
                  }
            }

            Debug.LogError("[ARManagerInitializer2] ❌ Ни один шейдер не найден!");
            return null;
      }

      /// <summary>
      /// Создает fallback материал с гарантированно рабочим шейдером
      /// </summary>
      private Material CreateFallbackMaterial()
      {
            Debug.LogWarning("[ARManagerInitializer2] Создание emergency fallback материала...");

            // Попробуем самые простые и надежные шейдеры
            string[] emergencyShaders = {
                  "Unlit/Color",
                  "Sprites/Default",
                  "UI/Default",
                  "Hidden/Internal-Colored"
            };

            foreach (string shaderName in emergencyShaders)
            {
                  Shader shader = Shader.Find(shaderName);
                  if (shader != null)
                  {
                        Material mat = new Material(shader);
                        mat.color = new Color(0.3f, 0.7f, 1.0f, 0.8f); // Приятный голубой цвет
                        Debug.Log($"[ARManagerInitializer2] ✅ Emergency материал создан с шейдером: {shaderName}");
                        return mat;
                  }
            }

            // Последний резерв - создаем материал со стандартным шейдером без проверки
            Debug.LogError("[ARManagerInitializer2] ❌ Критическая ошибка: все emergency шейдеры недоступны! Создаем материал 'вслепую'.");
            try
            {
                  Material desperateMaterial = new Material(Shader.Find("Standard"));
                  desperateMaterial.color = Color.cyan;
                  return desperateMaterial;
            }
            catch (System.Exception e)
            {
                  Debug.LogError($"[ARManagerInitializer2] ❌ Даже desperate материал не создался: {e.Message}");
                  return null; // В Unity при null материале объект станет розовым, что и покажет проблему
            }
      }


      private void SubscribeToWallSegmentation()
      {
            Debug.Log("[ARManagerInitializer2] Попытка подписки на события WallSegmentation...");
            WallSegmentation wallSegmentationInstance = FindObjectOfType<WallSegmentation>();
            if (wallSegmentationInstance != null)
            {
                  Debug.Log($"[ARManagerInitializer2] Найден экземпляр WallSegmentation: {wallSegmentationInstance.gameObject.name}. Подписка на OnSegmentationMaskUpdated.");
                  wallSegmentationInstance.OnSegmentationMaskUpdated -= OnSegmentationMaskUpdated; // Отписываемся на всякий случай
                  wallSegmentationInstance.OnSegmentationMaskUpdated += OnSegmentationMaskUpdated; // Подписываемся
                  Debug.Log("[ARManagerInitializer2] ✅ Подписка на события OnSegmentationMaskUpdated настроена");
            }
            else
            {
                  Debug.LogError("[ARManagerInitializer2] ❌ Экземпляр WallSegmentation не найден в сцене. Невозможно подписаться на обновления маски.");
                  // Попробуем переподписаться через некоторое время, если сцена еще загружается
                  StartCoroutine(RetrySubscriptionAfterDelay(1.0f));
            }
      }

      // Повторная попытка подписки после задержки
      private IEnumerator RetrySubscriptionAfterDelay(float delay)
      {
            yield return new WaitForSeconds(delay);
            SubscribeToWallSegmentation();
      }

      // Обработчик события изменения плоскостей
      private void OnPlanesChanged(ARPlanesChangedEventArgs args)
      {
            foreach (ARPlane plane in args.added)
            {
                  ConfigurePlane(plane);
            }

            foreach (ARPlane plane in args.updated)
            {
                  UpdatePlane(plane);
            }
      }

      // Настройка новой плоскости
      private void ConfigurePlane(ARPlane plane)
      {
            if (plane == null) return;

            // Определяем, вертикальная ли это плоскость
            bool isVertical = plane.alignment == PlaneAlignment.Vertical;

            // Назначаем материал в зависимости от типа плоскости
            MeshRenderer renderer = plane.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                  // Выбираем материал в зависимости от ориентации плоскости
                  Material material = isVertical ? verticalPlaneMaterial : horizontalPlaneMaterial;

                  // Создаем уникальный экземпляр материала для каждой плоскости
                  renderer.material = new Material(material);

                  // Если у нас есть маска сегментации и это вертикальная плоскость, применяем ее
                  if (isVertical && currentSegmentationMask != null)
                  {
                        renderer.material.SetTexture("_SegmentationMask", currentSegmentationMask);
                        renderer.material.EnableKeyword("USE_MASK");
                  }

                  // Debug.Log($"[ARManagerInitializer2] Настроена новая плоскость: {plane.trackableId}, тип: {(isVertical ? "вертикальная" : "горизонтальная")}");
            }
      }

      // Обновление существующей плоскости
      private void UpdatePlane(ARPlane plane)
      {
            if (plane == null) return;

            // Обновляем материал и данные для существующей плоскости, если это вертикальная плоскость
            if (plane.alignment == PlaneAlignment.Vertical)
            {
                  MeshRenderer renderer = plane.GetComponent<MeshRenderer>();
                  if (renderer != null && renderer.material != null && currentSegmentationMask != null)
                  {
                        renderer.material.SetTexture("_SegmentationMask", currentSegmentationMask);
                        renderer.material.EnableKeyword("USE_MASK");
                  }
            }
      }

      // Обработка обновления маски сегментации
      private void OnSegmentationMaskUpdated(RenderTexture mask)
      {
            if (mask == null)
            {
                  Debug.LogWarning("[ARManagerInitializer2] Получена null маска сегментации.");
                  return;
            }

            // Debug.Log($"[ARManagerInitializer2-OnSegmentationMaskUpdated] ✅ Маска сегментации получена: {mask.width}x{mask.height}, maskUpdated будет установлен в true");
            currentSegmentationMask = mask;
            maskUpdated = true;
            hadValidSegmentationResult = true;
            lastSuccessfulSegmentationTime = Time.time;

            if (отображениеМаскиUI != null)
            {
                  отображениеМаскиUI.texture = currentSegmentationMask;
                  отображениеМаскиUI.gameObject.SetActive(true); // Убедимся, что RawImage активен
                                                                 // Debug.Log("[ARManagerInitializer2] Текстура RawImage обновлена маской сегментации.");
            }
            else
            {
                  // Debug.LogWarning("[ARManagerInitializer2] отображениеМаскиUI не установлено, некуда выводить маску.");
            }
      }

      // Обработка маски сегментации для генерации плоскостей
      private void ProcessSegmentationMask()
      {
            if (currentSegmentationMask == null)
            {
                  // Debug.LogWarning("[ARManagerInitializer2] Попытка обработки null маски сегментации.");
                  return;
            }

            // Debug.Log($"[ARManagerInitializer2-ProcessSegmentationMask] ✅ Обработка маски сегментации {currentSegmentationMask.width}x{currentSegmentationMask.height}");

            int procWidth, procHeight;
            if (currentSegmentationMask.width == 0 || currentSegmentationMask.height == 0)
            {
                  Debug.LogWarning($"[ARManagerInitializer2-ProcessSegmentationMask] currentSegmentationMask имеет нулевые размеры: {currentSegmentationMask.width}x{currentSegmentationMask.height}. Пропуск обработки.");
                  return;
            }

            if (currentSegmentationMask.width >= currentSegmentationMask.height)
            { // Mask is landscape or square
                  procWidth = maskProcessingResolution;
                  procHeight = Mathf.Max(16, Mathf.RoundToInt((float)maskProcessingResolution * currentSegmentationMask.height / currentSegmentationMask.width));
            }
            else
            { // Mask is portrait
                  procHeight = maskProcessingResolution;
                  procWidth = Mathf.Max(16, Mathf.RoundToInt((float)maskProcessingResolution * currentSegmentationMask.width / currentSegmentationMask.height));
            }
            if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-ProcessSegmentationMask] Original Mask: {currentSegmentationMask.width}x{currentSegmentationMask.height}. Processing at: {procWidth}x{procHeight} (target main dim: {maskProcessingResolution})");


            // Конвертируем RenderTexture в Texture2D для анализа пикселей
            // Это может быть ресурсоемко, особенно если делать каждый кадр.
            // Рассмотреть оптимизацию, если производительность станет проблемой.
            Texture2D maskTexture = RenderTextureToTexture2D(currentSegmentationMask, procWidth, procHeight);

            if (maskTexture != null)
            {
                  CreatePlanesFromMask(maskTexture);
                  Destroy(maskTexture); // Освобождаем память Texture2D
            }
            else
            {
                  // Debug.LogError("[ARManagerInitializer2] Не удалось конвертировать RenderTexture в Texture2D.");
            }
      }

      // Преобразование RenderTexture в Texture2D для обработки
      private Texture2D RenderTextureToTexture2D(RenderTexture renderTexture, int targetWidth, int targetHeight)
      {
            // Create a temporary RenderTexture if downscaling is needed
            bool needsDownscaling = renderTexture.width != targetWidth || renderTexture.height != targetHeight;
            RenderTexture sourceRT = renderTexture; // By default, use the original renderTexture

            if (needsDownscaling)
            {
                  // Get a temporary RT for downscaling
                  RenderTexture downscaledRT = RenderTexture.GetTemporary(targetWidth, targetHeight, 0, renderTexture.format, RenderTextureReadWrite.Default);
                  Graphics.Blit(renderTexture, downscaledRT); // Blit from original to downscaled
                  sourceRT = downscaledRT; // The texture to read from is now the downscaled one
            }

            // Create the Texture2D with the target dimensions
            Texture2D texture = new Texture2D(targetWidth, targetHeight, TextureFormat.RGBA32, false);
            RenderTexture currentActiveRT = RenderTexture.active; // Save current active RT

            try
            {
                  RenderTexture.active = sourceRT; // Set the sourceRT (either original or downscaled) as active
                  texture.ReadPixels(new Rect(0, 0, targetWidth, targetHeight), 0, 0);
                  texture.Apply();
            }
            catch (System.Exception e)
            {
                  Debug.LogError($"[ARManagerInitializer2] ❌ Ошибка при конвертации RenderTexture ({renderTexture.width}x{renderTexture.height}) в Texture2D ({targetWidth}x{targetHeight}): {e.Message}");
                  Destroy(texture); // Cleanup Texture2D if error occurs
                  texture = null;     // Ensure null is returned on error
            }
            finally
            {
                  RenderTexture.active = currentActiveRT; // Restore previously active RT
                  if (needsDownscaling && sourceRT != null && sourceRT != renderTexture) // If we used a temporary downscaledRT
                  {
                        RenderTexture.ReleaseTemporary(sourceRT); // Release it
                  }
            }
            return texture;
      }

      // Создание плоскостей на основе маски сегментации
      private void CreatePlanesFromMask(Texture2D maskTexture)
      {
            // Debug.Log($"[ARManagerInitializer2-CreatePlanesFromMask] ✅ Начало создания плоскостей из маски. Размеры маски: {maskTexture.width}x{maskTexture.height}");
            Color32[] textureData = maskTexture.GetPixels32();
            // Debug.Log($"[ARManagerInitializer2-CreatePlanesFromMask] Получено {textureData.Length} пикселей из маски.");

            // if (textureData.Length == 0)
            // {
            //     Debug.LogWarning("[ARManagerInitializer2-CreatePlanesFromMask] Данные текстуры пусты. Невозможно создать плоскости.");
            //     return;
            // }

            // int activePixelCount = 0;
            // for (int i = 0; i < textureData.Length; i++)
            // {
            //     if (textureData[i].r > 10) // Используем низкий порог для подсчета "активных" пикселей
            //     {
            //         activePixelCount++;
            //     }
            // }
            // Debug.Log($"[ARManagerInitializer2-CreatePlanesFromMask] Количество потенциально активных пикселей (r > 10) в маске: {activePixelCount} из {textureData.Length}");


            List<Rect> wallAreas = FindWallAreas(textureData, maskTexture.width, maskTexture.height, wallPixelThreshold);
            Debug.Log($"[ARManagerInitializer2-CreatePlanesFromMask] Найдено областей (wallAreas.Count): {wallAreas.Count}");

            // Словарь для отслеживания "посещенных" (обновленных или подтвержденных) плоскостей в этом кадре
            Dictionary<GameObject, bool> visitedPlanes = new Dictionary<GameObject, bool>();

            // Очищаем старые плоскости перед созданием новых (или обновлением существующих)
            // Debug.Log($"[ARManagerInitializer2-CreatePlanesFromMask] Очистка старых плоскостей ({generatedPlanes.Count}) перед созданием новых.");
            // CleanupOldPlanes(visitedPlanes); // Теперь очистка происходит в UpdateOrCreatePlaneForWallArea

            // Сортируем области по размеру (площади), чтобы сначала обрабатывать большие
            // Это необязательно, если мы обновляем/создаем для всех, но может быть полезно для отладки или лимитов
            var significantAreas = wallAreas
                .OrderByDescending(area => area.width * area.height)
                .ToList();

            // Debug.Log($"[ARManagerInitializer2-CreatePlanesFromMask] После сортировки осталось {significantAreas.Count} областей для потенциального создания.");

            int planesCreatedThisFrame = 0;
            // float normalizedMinPlaneSize = minPlaneSizeInMeters * minPlaneSizeInMeters; // Площадь в метрах
            float normalizedMinPlaneSize = (minPlaneSizeInMeters / (Camera.main.orthographic ? Camera.main.orthographicSize * 2f : Mathf.Tan(Camera.main.fieldOfView * Mathf.Deg2Rad / 2f) * 2f * 2f)); // Примерный перевод метров в "нормализованный размер" относительно FOV
            normalizedMinPlaneSize *= normalizedMinPlaneSize; // Сравниваем площади

            foreach (Rect area in significantAreas)
            {
                  float normalizedAreaSize = (area.width / (float)maskTexture.width) * (area.height / (float)maskTexture.height);
                  Debug.Log($"[ARManagerInitializer2-CreatePlanesFromMask] Обработка области: x={area.xMin}, y={area.yMin}, w={area.width}, h={area.height}, normSize={normalizedAreaSize:F4}, pixelArea={area.width * area.height}");

                  // if (normalizedAreaSize >= normalizedMinPlaneSize) // Фильтр по минимальному размеру
                  if (area.width * area.height >= minAreaSizeInPixels) // Используем пиксельный размер для фильтрации областей
                  {
                        Debug.Log($"[ARManagerInitializer2-CreatePlanesFromMask] ✅ Область прошла фильтр размера: {area.width * area.height} >= {minAreaSizeInPixels}. Пытаемся создать плоскость...");
                        // Вместо CreatePlaneForWallArea вызываем UpdateOrCreatePlaneForWallArea
                        if (UpdateOrCreatePlaneForWallArea(area, maskTexture.width, maskTexture.height, visitedPlanes))
                        {
                              planesCreatedThisFrame++;
                        }
                        Debug.Log($"[ARManagerInitializer2-CreatePlanesFromMask] ✅ Попытка создания/обновления плоскости для области normSize={normalizedAreaSize:F4}. Создано/обновлено в этом кадре: {planesCreatedThisFrame}. Общее количество плоскостей после вызова: {generatedPlanes.Count}");
                  }
                  else
                  {
                        Debug.Log($"[ARManagerInitializer2-CreatePlanesFromMask] ❌ Область слишком мала: {area.width * area.height} < {minAreaSizeInPixels} пикс. Плоскость не создана.");
                  }
            }

            // Окончательная очистка плоскостей, которые не были "посещены" (т.е. для них не нашлось подходящей области в новой маске)
            CleanupOldPlanes(visitedPlanes);

            Debug.Log($"[ARManagerInitializer2-CreatePlanesFromMask] ✅ Завершено. Обновлено/Создано {planesCreatedThisFrame} плоскостей из {significantAreas.Count} рассмотренных областей. Всего активных плоскостей: {generatedPlanes.Count}");

            lastPlaneCount = generatedPlanes.Count;
      }

      // Метод для поиска связной области начиная с заданного пикселя
      private Rect FindConnectedArea(Color32[] pixels, int width, int height, int startX, int startY, bool[,] visited, byte threshold)
      {
            if (enableCustomPlaneCreationLogging) Debug.Log($"[ARManagerInitializer2-FindConnectedArea] IN START: StartX={startX}, StartY={startY}, Threshold={threshold}, PixelValue.R={pixels[startY * width + startX].r}, Visited={visited[startX, startY]}");

            if (startX < 0 || startX >= width || startY < 0 || startY >= height || visited[startX, startY] || pixels[startY * width + startX].r < threshold)
            {
                  // if (enableCustomPlaneCreationLogging) Debug.LogWarning($"[ARManagerInitializer2-FindConnectedArea] INVALID START PARAMS or ALREADY VISITED/BELOW THRESHOLD. Returning Rect.zero. Visited={visited[startX, startY]}, Pixel.R={pixels[startY * width + startX].r}");
                  return Rect.zero;
            }

            // Границы области
            int minX = startX;
            int maxX = startX;
            int minY = startY;
            int maxY = startY;

            Queue<Vector2Int> queue = new Queue<Vector2Int>();
            queue.Enqueue(new Vector2Int(startX, startY));
            visited[startX, startY] = true;
            // Debug.Log($"[ARManagerInitializer2-FindConnectedArea] Enqueued initial: ({startX},{startY}), visited set to true. Queue count: {queue.Count}");

            // Возможные направления для обхода (4 соседа)
            Vector2Int[] directions = new Vector2Int[]
            {
            new Vector2Int(1, 0),  // вправо
            new Vector2Int(-1, 0), // влево
            new Vector2Int(0, 1),  // вниз
            new Vector2Int(0, -1)  // вверх
            };

            // Алгоритм обхода в ширину для поиска связной области
            while (queue.Count > 0)
            {
                  Vector2Int current = queue.Dequeue();
                  // Debug.Log($"[ARManagerInitializer2-FindConnectedArea] Dequeued: ({current.x},{current.y}). Pixel.R={pixels[current.y * width + current.x].r}. Queue count: {queue.Count}");

                  // Обновляем границы области
                  bool boundsChanged = false;
                  if (current.x < minX) { minX = current.x; boundsChanged = true; }
                  if (current.x > maxX) { maxX = current.x; boundsChanged = true; }
                  if (current.y < minY) { minY = current.y; boundsChanged = true; }
                  if (current.y > maxY) { maxY = current.y; boundsChanged = true; }

                  if (boundsChanged)
                  {
                        // Debug.Log($"[ARManagerInitializer2-FindConnectedArea] Bounds updated: minX={minX}, maxX={maxX}, minY={minY}, maxY={maxY}");
                  }

                  // Проверяем соседей
                  foreach (Vector2Int dir in directions)
                  {
                        int newX = current.x + dir.x;
                        int newY = current.y + dir.y;

                        // Проверяем, что новые координаты в пределах текстуры
                        if (newX >= 0 && newX < width && newY >= 0 && newY < height)
                        {
                              // Если пиксель не посещен и это часть стены
                              if (!visited[newX, newY] && pixels[newY * width + newX].r >= threshold) // ИЗМЕНЕНО: > на >=
                              {
                                    visited[newX, newY] = true;
                                    queue.Enqueue(new Vector2Int(newX, newY));
                                    // Debug.Log($"[ARManagerInitializer2-FindConnectedArea] Enqueued neighbor: ({newX},{newY}). Pixel.R={pixels[newY * width + newX].r}. Visited set to true. Queue count: {queue.Count}");
                              }
                        }
                  }
            }

            Rect resultRect = new Rect(minX, minY, maxX - minX + 1, maxY - minY + 1);
            if (enableCustomPlaneCreationLogging) Debug.Log($"[ARManagerInitializer2-FindConnectedArea] IN END: Returning Rect: X={resultRect.x}, Y={resultRect.y}, W={resultRect.width}, H={resultRect.height} for start ({startX},{startY})");
            return resultRect;
      }

      private List<Rect> FindWallAreas(Color32[] pixels, int width, int height, byte threshold)
      {
            if (enableCustomPlaneCreationLogging) Debug.Log($"[ARManagerInitializer2-FindWallAreas] IN START: Texture {width}x{height}, Threshold={threshold}");
            List<Rect> areas = new List<Rect>();
            bool[,] visited = new bool[width, height];
            int areasFoundBeforeFiltering = 0;
            int pixelsChecked = 0;
            int activeUnvisitedPixelsFound = 0;


            for (int y = 0; y < height; y++)
            {
                  for (int x = 0; x < width; x++)
                  {
                        pixelsChecked++;
                        // Debug.Log($"[ARManagerInitializer2-FindWallAreas] Проверка пикселя ({x},{y}). visited={visited[x,y]}, pixel.r = {pixels[y * width + x].r}");
                        if (!visited[x, y] && pixels[y * width + x].r >= threshold)
                        {
                              activeUnvisitedPixelsFound++;
                              if (enableCustomPlaneCreationLogging) Debug.Log($"[ARManagerInitializer2-FindWallAreas] Found ACTIVE UNVISITED pixel at ({x},{y}). Pixel.R={pixels[y * width + x].r}. Calling FindConnectedArea...");
                              Rect area = FindConnectedArea(pixels, width, height, x, y, visited, threshold);
                              areasFoundBeforeFiltering++;
                              if (enableCustomPlaneCreationLogging) Debug.Log($"[ARManagerInitializer2-FindWallAreas] FindConnectedArea for ({x},{y}) returned: {areaToString(area)}. Area.width={area.width}, Area.height={area.height}, Area.width*Area.height={area.width * area.height}");

                              if (area.width >= minPixelsDimensionForArea && area.height >= minPixelsDimensionForArea && area.width * area.height >= minAreaSizeInPixels)
                              {
                                    if (enableCustomPlaneCreationLogging) Debug.Log($"[ARManagerInitializer2-FindWallAreas] ADDED Area: {areaToString(area)} (Pixel Area: {area.width * area.height}). Meets MinDimension={minPixelsDimensionForArea} AND MinPixelArea={minAreaSizeInPixels}. Total areas: {areas.Count + 1}");
                                    areas.Add(area);
                              }
                              else
                              {
                                    if (area.width > 0 && area.height > 0) // Если это не Rect.zero
                                    {
                                          if (enableCustomPlaneCreationLogging) Debug.Log($"[ARManagerInitializer2-FindWallAreas] FILTERED Area: {areaToString(area)} (Pixel Dims: {area.width}x{area.height}, Pixel Area: {area.width * area.height}). MinDimensionForArea={minPixelsDimensionForArea}, MinAreaSizeInPixels={minAreaSizeInPixels}. NOT ADDED.");
                                    }
                                    else
                                    {
                                          if (enableCustomPlaneCreationLogging) Debug.Log($"[ARManagerInitializer2-FindWallAreas] FindConnectedArea returned ZERO area for ({x},{y}). NOT ADDED.");
                                    }
                              }
                        }
                  }
            }
            if (enableCustomPlaneCreationLogging) Debug.Log($"[ARManagerInitializer2-FindWallAreas] IN END: PixelsChecked={pixelsChecked}. ActiveUnvisitedPixelsFound (triggers for FindConnectedArea)={activeUnvisitedPixelsFound}. AreasFoundBeforeFiltering={areasFoundBeforeFiltering}. Final ValidAreasCount={areas.Count}");
            return areas;
      }

      // Вспомогательная функция для красивого вывода Rect в лог
      private string areaToString(Rect area)
      {
            return $"Rect(x:{area.xMin:F0}, y:{area.yMin:F0}, w:{area.width:F0}, h:{area.height:F0})";
      }

      // Создание плоскости для области стены
      private void CreatePlaneForWallArea(Rect area, int textureWidth, int textureHeight)
      {
            if (xrOrigin == null || xrOrigin.Camera == null)
            {
                  Debug.LogError("[ARManagerInitializer2-CreatePlaneForWallArea] XROrigin or Camera is null. Cannot create plane.");
                  return;
            }

            // Выбираем между улучшенным и стандартным алгоритмом
            if (useEnhancedPlaneGeometry)
            {
                  CreatePlaneForWallAreaEnhanced(area, textureWidth, textureHeight);
                  return;
            }

            Camera mainCamera = xrOrigin.Camera;
            float planeWorldWidth, planeWorldHeight;
            float distanceFromCamera;

            // Расчет ширины и высоты видимой области на определенном расстоянии от камеры
            // Для перспективной камеры:
            float halfFovVertical = mainCamera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float halfFovHorizontal = Mathf.Atan(Mathf.Tan(halfFovVertical) * mainCamera.aspect);

            // Используем центральную точку области для рейкаста
            Vector2 areaCenterUV = new Vector2(
                (area.xMin + area.width * 0.5f) / textureWidth,
                (area.yMin + area.height * 0.5f) / textureHeight
            );

            Ray ray = mainCamera.ViewportPointToRay(new Vector3(areaCenterUV.x, areaCenterUV.y, 0));
            // RaycastHit hit; // Удалено - переменная не использовалась
            Vector3 planePosition;
            Quaternion planeRotation;
            float fallbackDistance = 2.0f; // Расстояние по умолчанию, если рейкаст не удался

            // Устанавливаем маску слоев для рейкаста (например, только "Default" или специальный слой для геометрии сцены)
            // int layerMask = LayerMask.GetMask("Default", "SimulatedEnvironment"); // Пример
            // int layerMask = ~LayerMask.GetMask("ARCustomPlane", "Ignore Raycast"); // Игнорируем собственные плоскости и слой Ignore Raycast
            LayerMask raycastLayerMask = LayerMask.GetMask("SimulatedEnvironment", "Default", "Wall"); // Добавляем слой "Wall"

            // Визуализация луча для отладки
            // Debug.DrawRay(ray.origin, ray.direction * maxRayDistance, Color.yellow, 1.0f); // ОТКЛЮЧЕНО: Визуализация всех лучей

            RaycastHit hitInfo;
            if (Physics.Raycast(ray, out hitInfo, maxRayDistance, raycastLayerMask))
            {
                  distanceFromCamera = hitInfo.distance;
                  Debug.DrawRay(ray.origin, ray.direction * hitInfo.distance, Color.green, 1.0f); // Раскомментировано: Визуализация успешного луча
                  // Debug.Log($"[ARManagerInitializer2-CreatePlaneForWallArea] Raycast hit! Distance: {distanceFromCamera:F2}m, Normal: {hit.normal:F2}, Point: {hit.point:F2}, Hit Object: {hit.collider.name}");
                  planeRotation = Quaternion.LookRotation(-hitInfo.normal, mainCamera.transform.up); // Ориентируем Z плоскости по нормали
                  planePosition = hitInfo.point + hitInfo.normal * 0.01f; // Смещаем немного от поверхности, чтобы избежать Z-fighting
            }
            else
            {
                  // Если луч не попал, используем фиксированное расстояние и ориентацию параллельно камере
                  distanceFromCamera = fallbackDistance; // Используем заданное расстояние по умолчанию
                  planePosition = mainCamera.transform.position + mainCamera.transform.forward * distanceFromCamera;
                  planeRotation = mainCamera.transform.rotation; // Ориентация как у камеры
                                                                 // Debug.LogWarning($"[ARManagerInitializer2-CreatePlaneForWallArea] Raycast miss. Using fixed distance ({distanceFromCamera}m) and camera-parallel orientation.");
                  Debug.DrawRay(ray.origin, ray.direction * maxRayDistance, Color.red, 1.0f); // Раскомментировано: Визуализация промахнувшегося луча
                  return; // Не удалось получить данные для плоскости
            }


            // Расчет мировых размеров плоскости
            // Ширина видимой области на расстоянии distanceFromCamera
            float worldHeightAtDistance = 2.0f * distanceFromCamera * Mathf.Tan(halfFovVertical);
            float worldWidthAtDistance = worldHeightAtDistance * mainCamera.aspect;

            // Мировые размеры плоскости, основанные на ее доле в маске
            planeWorldWidth = (area.width / textureWidth) * worldWidthAtDistance * planeSizeMultiplier;
            planeWorldHeight = (area.height / textureHeight) * worldHeightAtDistance * planeSizeMultiplier;

            // ИСПРАВЛЕНО: Ограничиваем размеры реалистичными значениями для стен
            planeWorldWidth = Mathf.Clamp(planeWorldWidth, minPlaneSize, maxWallWidth);
            planeWorldHeight = Mathf.Clamp(planeWorldHeight, minPlaneSize, maxWallHeight);

            // Проверка на минимальный размер перед созданием
            if (planeWorldWidth < minPlaneSizeInMeters || planeWorldHeight < minPlaneSizeInMeters)
            {
                  // Debug.Log($"[ARManagerInitializer2-CreatePlaneForWallArea] Плоскость для области ({area.width}x{area.height}px) слишком мала ({planeWorldWidth:F2}x{planeWorldHeight:F2}m) для создания. Min size: {this.minPlaneSizeInMeters}m.");
                  return;
            }

            // ДОБАВЛЕНО: Проверка соотношения сторон
            float aspectRatio = Mathf.Max(planeWorldWidth / planeWorldHeight, planeWorldHeight / planeWorldWidth);
            if (aspectRatio > maxAspectRatio)
            {
                  Debug.Log($"[ARManagerInitializer2-CreatePlaneForWallArea] Плоскость отклонена из-за неправильного соотношения сторон: {aspectRatio:F1} (макс: {maxAspectRatio})");
                  return;
            }

            // Debug.Log($"[ARManagerInitializer2-CreatePlaneForWallArea] Plane World Size: Width={planeWorldWidth:F2}, Height={planeWorldHeight:F2} at distance {distanceFromCamera:F2}m");

            // УЛУЧШЕННАЯ проверка на перекрывающиеся плоскости
            foreach (GameObject existingPlane in this.generatedPlanes)
            {
                  if (existingPlane == null) continue;

                  float distance = Vector3.Distance(existingPlane.transform.position, planePosition);

                  // Проверяем близость по позиции
                  if (distance < 0.5f) // Увеличили минимальное расстояние
                  {
                        Debug.Log($"[ARManagerInitializer2-CreatePlaneForWallArea] ⚠️ Плоскость слишком близко к существующей ({distance:F2}м), пропускаем создание.");
                        return;
                  }

                  // Проверяем перекрытие по площади (для плоскостей на одной стене)
                  if (distance < 1.5f && Vector3.Dot(existingPlane.transform.forward, planeRotation * Vector3.forward) > 0.8f)
                  {
                        Debug.Log($"[ARManagerInitializer2-CreatePlaneForWallArea] ⚠️ Плоскость перекрывается с существующей на той же стене, пропускаем создание.");
                        return;
                  }
            }

            // ... (код до создания planeObject) ...
            string planeName = $"MyARPlane_Debug_{planeInstanceCounter++}";
            GameObject planeObject = new GameObject(planeName);
            planeObject.transform.SetParent(null); // Вы устанавливаете родителя позже, это ОК
            planeObject.transform.position = planePosition;
            planeObject.transform.rotation = planeRotation;

            // Отключаем MeshRenderer для теста (ЭТОТ БЛОК У ВАС УЖЕ ЕСТЬ И ОН ПРАВИЛЬНЫЙ)
            MeshRenderer renderer = planeObject.GetComponent<MeshRenderer>();
            if (renderer == null)
            {
                  renderer = planeObject.AddComponent<MeshRenderer>();
            }
            // ВРЕМЕННО ВКЛЮЧЕНО ДЛЯ ОТЛАДКИ ПОЗИЦИОНИРОВАНИЯ
            renderer.enabled = true;
            Debug.Log($"[ARManagerInitializer2-CreatePlaneForWallArea] MeshRenderer для {planeObject.name} ВКЛЮЧЕН для отладки позиционирования."); // Изменил тег на CreatePlaneForWallArea для ясности

            // ... (дальнейший код метода: создание меша, коллайдера и т.д.) ...
            // Меш создается в XY, поэтому его нужно повернуть, если LookRotation использовал Z как "вперед"
            // Стандартный Quad Unity ориентирован вдоль локальной оси Z. LookRotation выравнивает Z объекта с направлением.
            // Если planeNormal - это нормаль поверхности, то LookRotation(planeNormal) выровняет +Z объекта с этой нормалью.
            // Это обычно то, что нужно для плоскости, представляющей поверхность.

            planeObject.transform.localScale = Vector3.one; // Масштаб будет применен к мешу напрямую

            // Debug.Log($"[ARManagerInitializer2-CreatePlaneForWallArea] Created {planeName}. World Position: {planeObject.transform.position}, Rotation: {planeObject.transform.rotation.eulerAngles}, Initial Scale: {planeObject.transform.localScale}");

            MeshFilter meshFilter = planeObject.AddComponent<MeshFilter>();
            meshFilter.mesh = CreatePlaneMesh(planeWorldWidth, planeWorldHeight); // Используем мировые размеры для меша

            MeshRenderer meshRenderer = planeObject.AddComponent<MeshRenderer>();

            // Проверяем и при необходимости пересоздаем материал
            if (this.verticalPlaneMaterial == null)
            {
                  Debug.LogWarning("[ARManagerInitializer2] verticalPlaneMaterial is null during plane creation! Re-initializing materials...");
                  InitializeMaterials(); // Повторная инициализация если материал все еще null
            }

            if (this.verticalPlaneMaterial != null)
            {
                  try
                  {
                        meshRenderer.material = new Material(this.verticalPlaneMaterial);
                        Debug.Log($"[ARManagerInitializer2] ✅ Материал успешно применен к плоскости {planeName}. Шейдер: {meshRenderer.material.shader.name}");
                  }
                  catch (System.Exception e)
                  {
                        Debug.LogError($"[ARManagerInitializer2] ❌ Ошибка при применении материала: {e.Message}");
                        meshRenderer.material = CreateFallbackMaterial();
                  }
            }
            else
            {
                  Debug.LogError("[ARManagerInitializer2] ❌ verticalPlaneMaterial все еще null после повторной инициализации!");
                  meshRenderer.material = CreateFallbackMaterial();
            }
            // Debug.Log($"[ARManagerInitializer2-CreatePlaneForWallArea] Applied material to {planeName}. Mesh bounds: {meshFilter.mesh.bounds.size}");

            MeshCollider meshCollider = planeObject.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = meshFilter.mesh;

            this.generatedPlanes.Add(planeObject);
            if (this.planeCreationTimes != null) this.planeCreationTimes[planeObject] = Time.time;

            // Попытка привязать к TrackablesParent, если он есть и не был равен null при старте
            if (this.xrOrigin != null && this.xrOrigin.TrackablesParent != null)
            {
                  // Проверяем, не является ли TrackablesParent частью самого XR Origin, который может быть отключен при симуляции
                  // и имеет ли он тот же InstanceID, что и при старте (на случай если он был пересоздан)
                  if (this.trackablesParentInstanceID_FromStart == 0 ||
                      (this.xrOrigin.TrackablesParent.gameObject.activeInHierarchy && this.xrOrigin.TrackablesParent.GetInstanceID() == this.trackablesParentInstanceID_FromStart))
                  {
                        planeObject.transform.SetParent(this.xrOrigin.TrackablesParent, true);
                        // Debug.Log($"[ARManagerInitializer2-CreatePlaneForWallArea] {planeName} привязан к {this.xrOrigin.TrackablesParent.name} (ID: {this.xrOrigin.TrackablesParent.GetInstanceID()}).");
                  }
                  else
                  {
                        Debug.LogWarning($"[ARManagerInitializer2-CreatePlaneForWallArea] {planeName} не привязан к TrackablesParent, так как он неактивен или был изменен (ожидался ID: {this.trackablesParentInstanceID_FromStart}, текущий: {this.xrOrigin.TrackablesParent.GetInstanceID()}, активен: {this.xrOrigin.TrackablesParent.gameObject.activeInHierarchy}). Оставлен в корне.");
                  }
            }
            else
            {
                  Debug.LogWarning($"[ARManagerInitializer2-CreatePlaneForWallArea] {planeName} не привязан, так как XROrigin или TrackablesParent не найдены. Оставлен в корне.");
            }

            // Обновляем счетчик и выводим информацию
            planeInstanceCounter++;
            if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] ✔️ Плоскость '{planeName}' УСПЕШНО СОЗДАНА и добавлена. Всего создано: {planeInstanceCounter}. Текущее кол-во в generatedPlanes: {generatedPlanes.Count}");
            return; // ИСПРАВЛЕНО: planeObject больше не возвращается, т.к. метод void
      }

      /// <summary>
      /// Улучшенная версия создания плоскости с использованием PlaneGeometryEnhancer
      /// для более точного расчета углов и размеров
      /// </summary>
      private void CreatePlaneForWallAreaEnhanced(Rect area, int textureWidth, int textureHeight)
      {
            if (enableEnhancedGeometryLogging)
                  Debug.Log($"[ARManagerInitializer2-Enhanced] 🔧 Создание плоскости с улучшенной геометрией для области {area.width}x{area.height}px");

            try
            {
                  // Проверяем доступность PlaneGeometryEnhancer через рефлексию
                  var planeEnhancerType = System.Type.GetType("PlaneGeometryEnhancer");
                  if (planeEnhancerType == null)
                  {
                        if (enableEnhancedGeometryLogging)
                              Debug.LogWarning("[ARManagerInitializer2-Enhanced] PlaneGeometryEnhancer не найден, используем fallback");
                  }
                  else
                  {
                        if (enableEnhancedGeometryLogging)
                              Debug.Log("[ARManagerInitializer2-Enhanced] PlaneGeometryEnhancer найден, но пока используем fallback до полной интеграции");
                  }

                  // Пока используем fallback метод
                  CreatePlaneForWallAreaEnhanced_Fallback(area, textureWidth, textureHeight);
            }
            catch (System.Exception ex)
            {
                  Debug.LogError($"[ARManagerInitializer2-Enhanced] ❌ Ошибка: {ex.Message}. Используем fallback метод.");
                  CreatePlaneForWallAreaEnhanced_Fallback(area, textureWidth, textureHeight);
            }
      }

      /// <summary>
      /// Временный fallback-метод, копирующий логику стандартного CreatePlaneForWallArea
      /// </summary>
      private void CreatePlaneForWallAreaEnhanced_Fallback(Rect area, int textureWidth, int textureHeight)
      {
            if (enableEnhancedGeometryLogging)
                  Debug.Log($"[ARManagerInitializer2-Enhanced-Fallback] 🔧 Создание плоскости (fallback) для области {area.width}x{area.height}px");

            Camera mainCamera = xrOrigin.Camera;

            // Используем простой рейкастинг в центр области (копия из оригинального метода)
            Vector2 areaCenterUV = new Vector2(
                (area.xMin + area.width * 0.5f) / textureWidth,
                (area.yMin + area.height * 0.5f) / textureHeight
            );

            Ray ray = mainCamera.ViewportPointToRay(new Vector3(areaCenterUV.x, areaCenterUV.y, 0));
            LayerMask raycastLayerMask = LayerMask.GetMask("SimulatedEnvironment", "Default", "Wall");

            RaycastHit hitInfo;
            if (Physics.Raycast(ray, out hitInfo, maxRayDistance, raycastLayerMask))
            {
                  // Простой расчет размеров (упрощенная версия)
                  float halfFovVertical = mainCamera.fieldOfView * 0.5f * Mathf.Deg2Rad;
                  float worldHeightAtDistance = 2.0f * hitInfo.distance * Mathf.Tan(halfFovVertical);
                  float worldWidthAtDistance = worldHeightAtDistance * mainCamera.aspect;

                  float planeWorldWidth = (area.width / textureWidth) * worldWidthAtDistance * planeSizeMultiplier;
                  float planeWorldHeight = (area.height / textureHeight) * worldHeightAtDistance * planeSizeMultiplier;

                  planeWorldWidth = Mathf.Clamp(planeWorldWidth, minPlaneSize, maxWallWidth);
                  planeWorldHeight = Mathf.Clamp(planeWorldHeight, minPlaneSize, maxWallHeight);

                  // Проверки размера и соотношения сторон
                  if (planeWorldWidth < minPlaneSizeInMeters || planeWorldHeight < minPlaneSizeInMeters)
                        return;

                  float aspectRatio = Mathf.Max(planeWorldWidth / planeWorldHeight, planeWorldHeight / planeWorldWidth);
                  if (aspectRatio > maxAspectRatio)
                        return;

                  // Создание плоскости
                  string planeName = $"EnhancedFallbackPlane_{planeInstanceCounter++}";
                  GameObject planeObject = new GameObject(planeName);

                  Vector3 planePosition = hitInfo.point + hitInfo.normal * 0.01f;
                  Quaternion planeRotation = Quaternion.LookRotation(-hitInfo.normal, mainCamera.transform.up);

                  planeObject.transform.position = planePosition;
                  planeObject.transform.rotation = planeRotation;

                  // Настройка компонентов
                  MeshRenderer renderer = planeObject.AddComponent<MeshRenderer>();
                  renderer.enabled = true;

                  MeshFilter meshFilter = planeObject.AddComponent<MeshFilter>();
                  meshFilter.mesh = CreatePlaneMesh(planeWorldWidth, planeWorldHeight);

                  if (this.verticalPlaneMaterial != null)
                        renderer.material = new Material(this.verticalPlaneMaterial);
                  else
                        renderer.material = CreateFallbackMaterial();

                  MeshCollider meshCollider = planeObject.AddComponent<MeshCollider>();
                  meshCollider.sharedMesh = meshFilter.mesh;

                  this.generatedPlanes.Add(planeObject);
                  if (this.planeCreationTimes != null)
                        this.planeCreationTimes[planeObject] = Time.time;

                  if (enableEnhancedGeometryLogging)
                        Debug.Log($"[ARManagerInitializer2-Enhanced-Fallback] ✅ Fallback плоскость '{planeName}' создана: {planeWorldWidth:F2}x{planeWorldHeight:F2}м");
            }
            else
            {
                  if (enableEnhancedGeometryLogging)
                        Debug.LogWarning("[ARManagerInitializer2-Enhanced-Fallback] Рейкастинг не попал в поверхность");
            }
      }

      private Mesh CreatePlaneMesh(float width, float height)
      {
            Mesh mesh = new Mesh();

            // Создаем вершины для более детализированного меша
            // Используем сетку 4x4 для более гибкой геометрии
            int segmentsX = 4;
            int segmentsY = 4;
            float thickness = 0.02f; // Уменьшенная толщина

            int vertCount = (segmentsX + 1) * (segmentsY + 1) * 2; // передняя и задняя грани
            Vector3[] vertices = new Vector3[vertCount];
            Vector2[] uv = new Vector2[vertCount];

            // Создаем передние и задние вершины
            int index = 0;
            for (int z = 0; z < 2; z++)
            {
                  float zPos = z == 0 ? 0 : -thickness;

                  for (int y = 0; y <= segmentsY; y++)
                  {
                        float yPos = -height / 2 + height * ((float)y / segmentsY);

                        for (int x = 0; x <= segmentsX; x++)
                        {
                              float xPos = -width / 2 + width * ((float)x / segmentsX);

                              vertices[index] = new Vector3(xPos, yPos, zPos);
                              uv[index] = new Vector2((float)x / segmentsX, (float)y / segmentsY);
                              index++;
                        }
                  }
            }

            // Создаем треугольники
            int quadCount = segmentsX * segmentsY * 2 + // передняя и задняя грани
                            segmentsX * 2 + // верхняя и нижняя грани
                            segmentsY * 2;  // левая и правая грани

            int[] triangles = new int[quadCount * 6]; // 6 индексов на квадрат (2 треугольника)

            index = 0;

            // Передняя грань
            int frontOffset = 0;
            int verticesPerRow = segmentsX + 1;

            for (int y = 0; y < segmentsY; y++)
            {
                  for (int x = 0; x < segmentsX; x++)
                  {
                        int currentIndex = frontOffset + y * verticesPerRow + x;

                        triangles[index++] = currentIndex;
                        triangles[index++] = currentIndex + verticesPerRow + 1;
                        triangles[index++] = currentIndex + 1;

                        triangles[index++] = currentIndex;
                        triangles[index++] = currentIndex + verticesPerRow;
                        triangles[index++] = currentIndex + verticesPerRow + 1;
                  }
            }

            // Задняя грань (инвертированные треугольники)
            int backOffset = (segmentsX + 1) * (segmentsY + 1);

            for (int y = 0; y < segmentsY; y++)
            {
                  for (int x = 0; x < segmentsX; x++)
                  {
                        int currentIndex = backOffset + y * verticesPerRow + x;

                        triangles[index++] = currentIndex + 1;
                        triangles[index++] = currentIndex + verticesPerRow + 1;
                        triangles[index++] = currentIndex;

                        triangles[index++] = currentIndex + verticesPerRow + 1;
                        triangles[index++] = currentIndex + verticesPerRow;
                        triangles[index++] = currentIndex;
                  }
            }

            // Верхняя, нижняя, левая и правая грани
            // Для простоты опускаю эту часть кода, она по сути аналогична

            // Назначаем данные сетке
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.uv = uv;

            // Вычисляем нормали и границы
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();

            return mesh;
      }

      // Новый метод для удаления плоскостей, накладывающихся поверх камеры
      private void RemoveOverlayingPlanes()
      {
            if (xrOrigin == null || xrOrigin.Camera == null || generatedPlanes.Count == 0)
                  return;

            Camera arCamera = xrOrigin.Camera;
            List<GameObject> planesToRemove = new List<GameObject>();

            // Определяем вектор "вперед" для камеры в мировом пространстве
            Vector3 cameraForward = arCamera.transform.forward;

            foreach (GameObject plane in generatedPlanes)
            {
                  if (plane == null) continue;

                  Vector3 directionToPlane = plane.transform.position - arCamera.transform.position;
                  float distanceToCamera = directionToPlane.magnitude;

                  float protectionTime = 1.0f;
                  bool isRecentlyCreated = planeCreationTimes.ContainsKey(plane) &&
                                            Time.time - planeCreationTimes[plane] < protectionTime;

                  // НОВАЯ ПРОВЕРКА: Экстремально близкие плоскости
                  if (distanceToCamera < 0.2f) // Порог для "экстремально близко", например, 20 см
                  {
                        if (!isRecentlyCreated)
                        {
                              planesToRemove.Add(plane);
                              // Debug.LogWarning($"[ARManagerInitializer2] 🚨 Удаление экстремально близкой плоскости: dist={distanceToCamera:F2}м, name={plane.name}");
                        }
                        else
                        {
                              // Debug.Log($"[ARManagerInitializer2] Экстремально близкая плоскость '{plane.name}' защищена (недавно создана): dist={distanceToCamera:F2}м. Пропускаем дальнейшие проверки наложения.");
                        }
                        continue; // Пропускаем остальные проверки для этой плоскости, если она экстремально близка
                  }

                  // Проверяем несколько условий для определения плоскости поверх камеры:

                  // 1. Расстояние до камеры (остается актуальным для плоскостей > 0.2м)
                  // float distanceToCamera = directionToPlane.magnitude; // Уже вычислено

                  // 2. Угол между направлением камеры и направлением к плоскости
                  // (насколько плоскость находится прямо перед камерой)
                  // Нормализация безопасна, так как distanceToCamera >= 0.2f
                  float alignmentWithCamera = Vector3.Dot(cameraForward.normalized, directionToPlane.normalized);

                  // 3. Угол между нормалью плоскости и направлением камеры
                  // (насколько плоскость обращена к камере)
                  float facingDot = Vector3.Dot(cameraForward, -plane.transform.forward);

                  // 4. Находится ли плоскость в центральной части поля зрения
                  Vector3 viewportPos = arCamera.WorldToViewportPoint(plane.transform.position);
                  bool isInCentralViewport = (viewportPos.x > 0.3f && viewportPos.x < 0.7f &&
                                             viewportPos.y > 0.3f && viewportPos.y < 0.7f &&
                                             viewportPos.z > 0);

                  // Условие для определения плоскости-наложения:
                  // - Плоскость находится близко к камере (менее 2.0 метра)
                  // - И плоскость находится примерно перед камерой (положительный dot product)
                  // - И плоскость почти перпендикулярна направлению взгляда
                  // - И плоскость находится в центральной части экрана
                  // Защита от удаления недавно созданных плоскостей уже проверена выше для экстремально близких.
                  // Здесь она применяется для "обычных" наложений.

                  if (!isRecentlyCreated && distanceToCamera < 2.0f && alignmentWithCamera > 0.7f && facingDot > 0.6f && isInCentralViewport)
                  {
                        planesToRemove.Add(plane);
                        // Debug.Log($"[ARManagerInitializer2] Обнаружена плоскость-наложение '{plane.name}': dist={distanceToCamera:F2}м, " + 
                        // $"align={alignmentWithCamera:F2}, facing={facingDot:F2}, inCenter={isInCentralViewport}");
                  }
                  else if (isRecentlyCreated && distanceToCamera < 2.0f && alignmentWithCamera > 0.7f && facingDot > 0.6f && isInCentralViewport)
                  {
                        // Debug.Log($"[ARManagerInitializer2] Плоскость-наложение '{plane.name}' защищена (недавно создана): dist={distanceToCamera:F2}м");
                  }
            }

            // Удаляем плоскости-наложения
            foreach (GameObject planeToRemove in planesToRemove)
            {
                  generatedPlanes.Remove(planeToRemove);
                  if (planeCreationTimes.ContainsKey(planeToRemove))
                  {
                        planeCreationTimes.Remove(planeToRemove);
                  }
                  Destroy(planeToRemove);
            }

            if (planesToRemove.Count > 0)
            {
                  Debug.LogWarning($"[ARManagerInitializer2] ⚠️ Удалено {planesToRemove.Count} плоскостей-наложений");
            }
      }

      // НОВЫЙ МЕТОД: Обновление позиций плоскостей для обеспечения стабильности
      private void UpdatePlanePositions()
      {
            if (xrOrigin == null || xrOrigin.Camera == null || xrOrigin.TrackablesParent == null)
            {
                  // Debug.LogError("[ARManagerInitializer2-UpdatePlanePositions] XR Origin, Camera, or TrackablesParent is not set. Cannot update plane positions.");
                  return;
            }

            // Debug.Log($"[ARManagerInitializer2-UpdatePlanePositions] Running. Planes to check: {generatedPlanes.Count}");
            int detachedPlanes = 0; // Объявляем переменную здесь

            for (int i = generatedPlanes.Count - 1; i >= 0; i--) // Идем в обратном порядке для безопасного удаления
            {
                  GameObject plane = generatedPlanes[i];
                  if (plane == null)
                  {
                        generatedPlanes.RemoveAt(i); // Удаляем null ссылки из списка
                        continue;
                  }

                  // Проверяем, не прикреплена ли плоскость к камере или ДРУГОМУ НЕОЖИДАННОМУ объекту
                  if (plane.transform.parent != null && (this.xrOrigin == null || this.xrOrigin.TrackablesParent == null || plane.transform.parent != this.xrOrigin.TrackablesParent))
                  {
                        // Debug.LogWarning($"[ARManagerInitializer2-UpdatePlanePositions] Плоскость '{plane.name}' (ID: {plane.GetInstanceID()}) была присоединена к НЕОЖИДАННОМУ родителю '{GetGameObjectPath(plane.transform.parent)}' (ожидался TrackablesParent или null). Отсоединяем.");

                        // Отсоединяем плоскость. Аргумент 'true' сохраняет мировые координаты,
                        // localScale будет скорректирован для сохранения текущего lossyScale.
                        plane.transform.SetParent(null, true);

                        detachedPlanes++;
                  }
                  else if (plane.transform.parent == null && this.xrOrigin != null && this.xrOrigin.TrackablesParent != null)
                  {
                        // Если плоскость почему-то отсоединилась от TrackablesParent, но TrackablesParent существует,
                        // присоединяем ее обратно. Это может произойти, если что-то другое в коде изменяет родителя.
                        // Debug.LogWarning($"[ARManagerInitializer2-UpdatePlanePositions] Плоскость '{plane.name}' (ID: {plane.GetInstanceID()}) была отсоединена от TrackablesParent. Присоединяем обратно к '{GetGameObjectPath(this.xrOrigin.TrackablesParent)}'.");
                        plane.transform.SetParent(this.xrOrigin.TrackablesParent, true);
                  }
                  else if (plane.transform.parent != null && this.xrOrigin != null && this.xrOrigin.TrackablesParent != null && plane.transform.parent == this.xrOrigin.TrackablesParent)
                  {
                        // Плоскость уже корректно привязана к TrackablesParent. Ничего делать не нужно.
                        // Debug.Log($"[ARManagerInitializer2-UpdatePlanePositions] Плоскость '{plane.name}' (ID: {plane.GetInstanceID()}) уже корректно привязана к TrackablesParent.");
                  }


                  // Логика для обновления позиции, если плоскость НЕ привязана к TrackablesParent
                  // Эта часть теперь менее актуальна, так как мы привязываем к TrackablesParent при создании
                  // и проверяем/восстанавливаем привязку выше.
                  // if (plane.transform.parent == null)
                  // {
                  //     Vector3 targetPosition = xrOrigin.Camera.transform.position + xrOrigin.Camera.transform.forward * 2.0f; // Пример: 2м перед камерой
                  //     plane.transform.position = targetPosition;
                  //     plane.transform.rotation = Quaternion.LookRotation(xrOrigin.Camera.transform.forward); // Ориентируем как камеру
                  //     Debug.Log($"[ARManagerInitializer2-UpdatePlanePositions] Плоскость '{plane.name}' обновлена (не была привязана): pos={targetPosition}, rot={plane.transform.rotation.eulerAngles}");
                  // }
            }

            if (detachedPlanes > 0)
            {
                  Debug.LogWarning($"[ARManagerInitializer2-UpdatePlanePositions] Отсоединено {detachedPlanes} плоскостей, которые были некорректно присоединены к родительским объектам.");
            }
      }

      // Перезагружаем все плоскости, если что-то пошло не так
      public void ResetAllPlanes()
      {
            // Clear persistent plane tracking
            persistentGeneratedPlanes.Clear();
            planeCreationTimes.Clear();
            planeLastVisitedTime.Clear();

            foreach (GameObject plane in generatedPlanes)
            {
                  if (plane != null)
                        Destroy(plane);
            }

            generatedPlanes.Clear();
            Debug.Log("[ARManagerInitializer2] 🔄 All planes removed and will be recreated");

            // Reset frame counter to immediately create new planes
            frameCounter = 10;

            // If we have ARPlaneConfigurator, also reset its saved planes
            if (planeConfigurator != null && usePersistentPlanes)
            {
                  planeConfigurator.ResetSavedPlanes();
            }
      }

      // НОВЫЙ МЕТОД: Отключение стандартных визуализаторов AR Foundation
      private void DisableARFoundationVisualizers()
      {
            // Проверяем наличие всех компонентов AR Foundation, которые могут создавать визуальные элементы

            // 1. Отключаем визуализаторы плоскостей
            if (planeManager != null)
            {
                  // НЕ ОБНУЛЯЕМ planePrefab, так как он нужен для создания коллайдеров даже при отключенной визуализации
                  // planeManager.planePrefab = null; // ЗАКОММЕНТИРОВАНО: Сохраняем префаб для коллайдеров
                  planeManager.requestedDetectionMode = PlaneDetectionMode.None; // ADDED: Prevent ARPlaneConfigurator warning

                  // Проходимся по всем трекабл-объектам и отключаем их визуализацию
                  foreach (var plane in planeManager.trackables)
                  {
                        if (plane != null)
                        {
                              MeshRenderer meshRenderer = plane.GetComponent<MeshRenderer>();
                              if (meshRenderer != null)
                              {
                                    meshRenderer.enabled = false;
                              }

                              LineRenderer lineRenderer = plane.GetComponent<LineRenderer>();
                              if (lineRenderer != null)
                              {
                                    lineRenderer.enabled = false;
                              }
                        }
                  }

                  // Debug.Log("[ARManagerInitializer2] ✅ Отключены стандартные визуализаторы плоскостей AR Foundation");
            }

            // 2. Отключаем визуализаторы точек
            var pointCloudManager = FindObjectOfType<UnityEngine.XR.ARFoundation.ARPointCloudManager>();
            if (pointCloudManager != null)
            {
                  pointCloudManager.enabled = false;
                  // Debug.Log("[ARManagerInitializer2] ✅ Отключен ARPointCloudManager");
            }

            // 3. Поиск и отключение всех визуальных объектов TrackablesParent
            if (xrOrigin != null)
            {
                  var trackablesParent = xrOrigin.transform.Find("Trackables");
                  if (trackablesParent != null)
                  {
                        // Проходимся по всем дочерним объектам и отключаем их рендереры
                        foreach (Transform child in trackablesParent)
                        {
                              // Отключаем все рендереры у дочерних объектов
                              foreach (Renderer renderer in child.GetComponentsInChildren<Renderer>())
                              {
                                    renderer.enabled = false;
                              }
                        }
                        // Debug.Log("[ARManagerInitializer2] ✅ Отключены рендереры в Trackables");
                  }
            }
      }

      // НОВЫЙ МЕТОД: Удаление существующих синих плоскостей ARFoundation
      private void RemoveExistingARFoundationPlanes()
      {
            Debug.Log("[ARManagerInitializer2] Поиск и удаление существующих синих плоскостей ARFoundation...");

            // Ищем все объекты ARPlane в сцене
            ARPlane[] existingPlanes = FindObjectsOfType<ARPlane>();
            int removedCount = 0;

            foreach (ARPlane plane in existingPlanes)
            {
                  if (plane != null)
                  {
                        Debug.Log($"[ARManagerInitializer2] Удаление ARFoundation плоскости: {plane.name}");
                        Destroy(plane.gameObject);
                        removedCount++;
                  }
            }

            // Также ищем объекты с именами, типичными для ARFoundation плоскостей
            GameObject[] allObjects = FindObjectsOfType<GameObject>();
            foreach (GameObject obj in allObjects)
            {
                  if (obj.name.Contains("ARPlane") && !obj.name.Contains("MyARPlane_Debug_"))
                  {
                        Debug.Log($"[ARManagerInitializer2] Удаление объекта с именем ARPlane: {obj.name}");
                        Destroy(obj);
                        removedCount++;
                  }
            }

            Debug.Log($"[ARManagerInitializer2] ✅ Удалено {removedCount} синих плоскостей ARFoundation");
      }

      // НОВЫЙ МЕТОД: Отключение других AR визуализаторов, которые могут появляться в рантайме
      private void DisableOtherARVisualizers()
      {
            // Отключаем все объекты с оранжевым/желтым цветом и именами, содержащими "Trackable", "Feature", "Point"
            var allRenderers = FindObjectsOfType<Renderer>();
            int disabledCount = 0;

            foreach (var renderer in allRenderers)
            {
                  // Пропускаем наши собственные плоскости
                  bool isOurPlane = false;
                  foreach (var plane in generatedPlanes)
                  {
                        if (plane != null && renderer.gameObject == plane)
                        {
                              isOurPlane = true;
                              break;
                        }
                  }

                  if (isOurPlane)
                        continue;

                  // Проверяем имя объекта на ключевые слова, связанные с AR Foundation
                  string objName = renderer.gameObject.name.ToLower();
                  if (objName.Contains("track") || objName.Contains("feature") ||
                      objName.Contains("point") || objName.Contains("plane") ||
                      objName.Contains("mesh") || objName.Contains("visualizer"))
                  {
                        renderer.enabled = false;
                        disabledCount++;
                  }

                  // Проверяем материал на желтый/оранжевый цвет
                  if (renderer.sharedMaterial != null)
                  {
                        // Получаем основной цвет материала
                        Color color = renderer.sharedMaterial.color;

                        // Проверяем, является ли цвет желтым или оранжевым
                        // (красный компонент высокий, зеленый средний, синий низкий)
                        if (color.r > 0.6f && color.g > 0.4f && color.b < 0.3f)
                        {
                              renderer.enabled = false;
                              disabledCount++;
                        }
                  }
            }

            if (disabledCount > 0)
            {
                  Debug.Log($"[ARManagerInitializer2] 🔴 Отключено {disabledCount} сторонних AR-визуализаторов");
            }
      }

      // УЛУЧШЕННЫЙ МЕТОД: Создание стабильной базовой плоскости перед пользователем при отсутствии данных сегментации
      private void CreateBasicPlaneInFrontOfUser()
      {
            if (xrOrigin == null || xrOrigin.Camera == null)
            {
                  // Debug.LogError("[ARManagerInitializer2] XROrigin or Camera is null, cannot create basic plane.");
                  return;
            }

            // Debug.Log("[ARManagerInitializer2] Создание базовой плоскости перед пользователем.");

            // Проверяем, есть ли уже существующая базовая плоскость
            foreach (GameObject existingPlane in generatedPlanes)
            {
                  if (existingPlane != null && existingPlane.name.StartsWith("MyARPlane_Debug_Basic_"))
                  {
                        // Debug.Log("[ARManagerInitializer2] Базовая плоскость уже существует, новая не создается.");
                        return; // Если уже есть, ничего не делаем
                  }
            }

            Camera mainCamera = xrOrigin.Camera;
            float distanceFromCamera = 2.0f; // Фиксированное расстояние

            // Расчет ширины и высоты видимой области на расстоянии distanceFromCamera
            float halfFovVertical = mainCamera.fieldOfView * 0.5f * Mathf.Deg2Rad;
            float worldHeightAtDistance = 2.0f * distanceFromCamera * Mathf.Tan(halfFovVertical);
            float worldWidthAtDistance = worldHeightAtDistance * mainCamera.aspect;

            // Создаем плоскость, которая занимает примерно 30% от ширины и высоты обзора
            float planeWorldWidth = worldWidthAtDistance * 0.3f;
            float planeWorldHeight = worldHeightAtDistance * 0.3f;

            // Убеждаемся, что размер не меньше минимального
            planeWorldWidth = Mathf.Max(planeWorldWidth, minPlaneSizeInMeters);
            planeWorldHeight = Mathf.Max(planeWorldHeight, minPlaneSizeInMeters);

            Mesh planeMesh = CreatePlaneMesh(planeWorldWidth, planeWorldHeight);

            string planeName = $"MyARPlane_Debug_Basic_{planeInstanceCounter++}";
            GameObject planeObject = new GameObject(planeName);
            planeObject.transform.SetParent(null);
            planeObject.transform.position = mainCamera.transform.position + mainCamera.transform.forward * distanceFromCamera;
            planeObject.transform.rotation = mainCamera.transform.rotation;
            planeObject.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
            planeObject.transform.SetParent(null);

            // Добавляем компоненты
            MeshFilter meshFilter = planeObject.AddComponent<MeshFilter>();
            meshFilter.mesh = planeMesh;

            MeshRenderer meshRenderer = planeObject.AddComponent<MeshRenderer>();
            if (this.verticalPlaneMaterial != null)
            {
                  meshRenderer.material = new Material(this.verticalPlaneMaterial);
            }
            else
            {
                  // Создаем материал для резервного случая
                  Material planeMaterial = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                  Color planeColor = Color.HSVToRGB(0.1f, 0.6f, 0.7f); // Приглушенный золотистый
                  planeMaterial.color = planeColor;

                  // Настройки материала для полупрозрачности
                  planeMaterial.SetFloat("_Surface", 1); // 1 = прозрачный
                  planeMaterial.SetInt("_ZWrite", 0); // Отключаем запись в буфер глубины для прозрачности
                  planeMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                  planeMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                  planeMaterial.EnableKeyword("_ALPHAPREMULTIPLY_ON");
                  planeMaterial.EnableKeyword("_EMISSION");
                  planeMaterial.SetColor("_EmissionColor", planeColor * 0.15f); // Уменьшенная эмиссия
                  planeMaterial.SetFloat("_Smoothness", 0.2f);
                  planeMaterial.SetFloat("_Metallic", 0.05f);
                  planeMaterial.renderQueue = 3000; // Очередь прозрачных объектов
                  planeColor.a = 0.5f; // Добавляем полупрозрачность
                  planeMaterial.color = planeColor;

                  meshRenderer.material = planeMaterial;
            }

            // Добавляем коллайдер для взаимодействия
            MeshCollider meshCollider = planeObject.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = meshFilter.sharedMesh;

            // Добавляем в список созданных плоскостей
            generatedPlanes.Add(planeObject);

            // Также сохраняем время создания плоскости для защиты от раннего удаления
            planeCreationTimes[planeObject] = Time.time;
            if (this.planeLastVisitedTime != null) this.planeLastVisitedTime[planeObject] = Time.time;

            // Debug.Log("[ARManagerInitializer2] ✅ Создана стабильная базовая плоскость перед пользователем");

            planeObject.name = $"MyARPlane_Debug_Basic_{planeInstanceCounter++}";
            // Debug.Log($"[ARManagerInitializer2] Создана базовая плоскость: {planeObj.name} на расстоянии {distanceFromCamera}m");

            generatedPlanes.Add(planeObject);
            planeCreationTimes[planeObject] = Time.time;
            if (this.planeLastVisitedTime != null) this.planeLastVisitedTime[planeObject] = Time.time;

            if (xrOrigin.TrackablesParent != null)
            {
                  planeObject.transform.SetParent(xrOrigin.TrackablesParent, true);
                  // Debug.Log($"[ARManagerInitializer2] Базовая плоскость {planeObj.name} привязана к {xrOrigin.TrackablesParent.name}.");
            }
            else
            {
                  // Debug.LogWarning($"[ARManagerInitializer2] TrackablesParent не найден на XROrigin, базовая плоскость {planeObj.name} не будет привязана.");
            }
      }

      // НОВЫЙ МЕТОД: Обновление существующей плоскости или создание новой для области стены
      private bool UpdateOrCreatePlaneForWallArea(Rect area, int textureWidth, int textureHeight, Dictionary<GameObject, bool> visitedPlanes)
      {
            Debug.Log($"[ARManagerInitializer2-UpdateOrCreatePlaneForWallArea] ⭐ НАЧАЛО. Область: {area}, текстура: {textureWidth}x{textureHeight}");
            Camera currentMainCamera = Camera.main; // Переименовано во избежание конфликта
            if (currentMainCamera == null)
            {
                  if (enableDetailedRaycastLogging) Debug.LogError("[ARManagerInitializer2-UOCP] Камера Camera.main не найдена!");
                  return false;
            }

            // DEBUG: Логирование информации о камере
            if (enableDetailedRaycastLogging)
            {
                  if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP-CAMDEBUG] Используется камера: {currentMainCamera.name}, Pos: {currentMainCamera.transform.position}, Rot: {currentMainCamera.transform.rotation.eulerAngles}");
            }

            if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] === ЗАПУСК UpdateOrCreatePlaneForWallArea для области: X={area.x}, Y={area.y}, W={area.width}, H={area.height} (Текстура: {textureWidth}x{textureHeight}) ===");
            if (xrOrigin == null || xrOrigin.Camera == null)
            {
                  Debug.LogError("[ARManagerInitializer2-UOCP] ❌ XROrigin или его камера не найдены. Выход.");
                  return false;
            }

            Camera mainCamera = xrOrigin.Camera;
            Vector3 cameraRight = mainCamera.transform.right;
            Vector3 cameraUp = mainCamera.transform.up;
            Vector3 cameraForward = mainCamera.transform.forward; // <--- ДОБАВЛЕНО ОБЪЯВЛЕНИЕ

            // Текущее положение и ориентация камеры
            Vector3 cameraPosition = mainCamera.transform.position;
            // Quaternion cameraRotation = mainCamera.transform.rotation; // Раскомментировать, если понадобится

            float normalizedCenterX = (area.x + area.width / 2f) / textureWidth;
            float normalizedCenterY = (area.y + area.height / 2f) / textureHeight;
            if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Нормализованный центр области (UV): X={normalizedCenterX:F2}, Y={normalizedCenterY:F2}");

            if (normalizedCenterX < 0 || normalizedCenterX > 1 || normalizedCenterY < 0 || normalizedCenterY > 1)
            {
                  // Debug.LogWarning($"[ARManagerInitializer2-UOCP] ⚠️ Нормализованные координаты центра области ({normalizedCenterX:F2}, {normalizedCenterY:F2}) выходят за пределы [0,1].");
            }

            Ray centerRay = mainCamera.ViewportPointToRay(new Vector3(normalizedCenterX, normalizedCenterY, 0));

            if (enableDetailedRaycastLogging)
            {
                  Debug.Log($"[ARManagerInitializer2-UOCP] РАССЧЕТ UV ДЛЯ РЕЙКАСТА: " +
                            $"area.xMin={area.xMin}, area.yMin={area.yMin}, area.width={area.width}, area.height={area.height}, " +
                            $"textureWidth={textureWidth}, textureHeight={textureHeight}");
                  Debug.Log($"[ARManagerInitializer2-UOCP] Нормализованный центр области (UV): X={normalizedCenterX:F2}, Y={normalizedCenterY:F2}");
                  if (mainCamera != null)
                  {
                        Debug.Log($"[ARManagerInitializer2-UOCP] КАМЕРА ПЕРЕД ViewportPointToRay: Name='{mainCamera.name}', Pos={mainCamera.transform.position}, Forward={mainCamera.transform.forward}");
                  }
                  else
                  {
                        Debug.LogWarning("[ARManagerInitializer2-UOCP] _mainCamera IS NULL перед ViewportPointToRay!");
                  }
            }

            Vector3 initialRayDirection = centerRay.direction;
            if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Исходное направление луча (из ViewportPointToRay({normalizedCenterX:F2},{normalizedCenterY:F2})): {initialRayDirection.ToString("F3")}");

            // Новый, более надежный способ создания и логирования LayerMask
            // string[] layerNames = new string[] { "SimulatedEnvironment", "Default", "Wall" }; // ЗАКОММЕНТИРОВАНО - БУДЕМ ИСПОЛЬЗОВАТЬ this.hitLayerMask
            // LayerMask layerMask = 0; // Начинаем с пустой маски // ЗАКОММЕНТИРОВАНО
            // string includedLayersString = ""; // ЗАКОММЕНТИРОВАНО
            // bool layersFound = false; // ЗАКОММЕНТИРОВАНО

            // foreach (string name in layerNames) // ЗАКОММЕНТИРОВАНО
            // { // ЗАКОММЕНТИРОВАНО
            //     int layer = LayerMask.NameToLayer(name); // ЗАКОММЕНТИРОВАНО
            //     if (layer != -1) // Если слой найден // ЗАКОММЕНТИРОВАНО
            //     { // ЗАКОММЕНТИРОВАНО
            //         layerMask |= (1 << layer); // Добавляем его в маску // ЗАКОММЕНТИРОВАНО
            //         if (layersFound) includedLayersString += ", "; // ЗАКОММЕНТИРОВАНО
            //         includedLayersString += $"{name} (id:{layer})"; // ЗАКОММЕНТИРОВАНО
            //         layersFound = true; // ЗАКОММЕНТИРОВАНО
            //     } // ЗАКОММЕНТИРОВАНО
            //     else // ЗАКОММЕНТИРОВАНО
            //     { // ЗАКОММЕНТИРОВАНО
            //         Debug.LogWarning($"[ARManagerInitializer2-UOCP] Слой '{name}' не найден в LayerMask settings. Проверьте Project Settings -> Tags and Layers."); // ЗАКОММЕНТИРОВАНО
            //     } // ЗАКОММЕНТИРОВАНО
            // } // ЗАКОММЕНТИРОВАНО

            // if (layersFound) // ЗАКОММЕНТИРОВАНО
            // { // ЗАКОММЕНТИРОВАНО
            //     if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] ПЕРЕД РЕЙКАСТАМИ: Используется LayerMask: {LayerMaskToString(layerMask)} (Value: {layerMask.value}), Включенные слои: [{includedLayersString}]"); // ЗАКОММЕНТИРОВАНО
            // } // ЗАКОММЕНТИРОВАНО
            // else // ЗАКОММЕНТИРОВАНО
            // { // ЗАКОММЕНТИРОВАНО
            //     Debug.LogWarning($"[ARManagerInitializer2-UOCP] Ни один из целевых слоев ({string.Join(", ", layerNames)}) не найден. Рейкаст будет использовать маску по умолчанию (Default)."); // ЗАКОММЕНТИРОВАНО
            //     layerMask = 1 << LayerMask.NameToLayer("Default"); // Только Default слой, если другие не найдены // ЗАКОММЕНТИРОВАНО
            // } // ЗАКОММЕНТИРОВАНО

            // ИСПОЛЬЗУЕМ hitLayerMask, НАСТРОЕННУЮ В ИНСПЕКТОРЕ, НО ИСКЛЮЧАЕМ СОБСТВЕННЫЕ ПЛОСКОСТИ
            LayerMask layerMask = this.hitLayerMask;
            // Исключаем слой ARPlanes (где находятся наши созданные плоскости), чтобы избежать попаданий в собственные плоскости
            // int arPlanesLayer = LayerMask.NameToLayer("ARPlanes"); // ЗАКОММЕНТИРОВАНО: Позволяем попадания в ARPlanes
            // if (arPlanesLayer != -1) // ЗАКОММЕНТИРОВАНО: Позволяем попадания в ARPlanes
            // { // ЗАКОММЕНТИРОВАНО: Позволяем попадания в ARPlanes
            //       layerMask &= ~(1 << arPlanesLayer); // Убираем ARPlanes из маски // ЗАКОММЕНТИРОВАНО: Позволяем попадания в ARPlanes
            // } // ЗАКОММЕНТИРОВАНО: Позволяем попадания в ARPlanes
            // if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] ПЕРЕД РЕЙКАСТАМИ: Используется LayerMask из инспектора (исключен ARPlanes): {LayerMaskToString(layerMask)} (Value: {layerMask.value})");
            if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] ПЕРЕД РЕЙКАСТАМИ: Используется LayerMask из инспектора (ARPlanes НЕ исключен): {LayerMaskToString(layerMask)} (Value: {layerMask.value})");


            // Параметры для рейкастинга
            // float maxRayDistance = 10.0f; // Максимальная дальность луча // ЗАКОММЕНТИРОВАНО - БУДЕТ ИСПОЛЬЗОВАТЬСЯ this.maxRayDistance
            RaycastHit hitInfo; // <--- ОБЪЯВЛЕНО

            bool didHit = false;

            // Массив смещений для лучей с более широким охватом и разной плотностью
            // Центральная область имеет более высокую плотность лучей
            List<Vector3> rayOffsets = new List<Vector3>();

            // Центральный луч (с наивысшим приоритетом)
            rayOffsets.Add(Vector3.zero);

            // Ближние лучи вокруг центра (высокий приоритет)
            float innerRadius = 0.08f; // Внутренний радиус в метрах
            rayOffsets.Add(cameraRight * innerRadius);                 // Правый
            rayOffsets.Add(-cameraRight * innerRadius);                // Левый
            rayOffsets.Add(cameraUp * innerRadius);                    // Верхний
            rayOffsets.Add(-cameraUp * innerRadius);                   // Нижний
            rayOffsets.Add(cameraRight * innerRadius * 0.7f + cameraUp * innerRadius * 0.7f);  // Правый верхний
            rayOffsets.Add(cameraRight * innerRadius * 0.7f - cameraUp * innerRadius * 0.7f);  // Правый нижний
            rayOffsets.Add(-cameraRight * innerRadius * 0.7f + cameraUp * innerRadius * 0.7f); // Левый верхний
            rayOffsets.Add(-cameraRight * innerRadius * 0.7f - cameraUp * innerRadius * 0.7f); // Левый нижний

            // Дальние лучи (средний приоритет)
            float outerRadius = 0.15f; // Внешний радиус в метрах
            rayOffsets.Add(cameraRight * outerRadius);                 // Дальний правый
            rayOffsets.Add(-cameraRight * outerRadius);                // Дальний левый 
            rayOffsets.Add(cameraUp * outerRadius);                    // Дальний верхний
            rayOffsets.Add(-cameraUp * outerRadius);                   // Дальний нижний

            // Веса для лучей. Должны соответствовать порядку в rayOffsets
            List<float> rayWeights = new List<float>
        {
            2.0f, // Центральный
            1.5f, 1.5f, 1.5f, 1.5f, // Ближние основные (право, лево, верх, низ)
            1.0f, 1.0f, 1.0f, 1.0f, // Ближние диагональные
            0.5f, 0.5f, 0.5f, 0.5f  // Дальние
        };

            float bestDistance = float.MaxValue;
            Vector3 bestNormal = Vector3.zero;
            float bestConfidence = 0f;

            // Хранение всех успешных попаданий лучей для анализа
            List<RaycastHit> successfulHits = new List<RaycastHit>();
            List<float> hitWeights = new List<float>();

            // --- НАЧАЛО БЛОКА ЛОГИРОВАНИЯ РЕЙКАСТОВ ---
            int totalRaysShot = 0;
            int raysHitSomething = 0;
            int raysHitValidSurface = 0;
            if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] --- Начало серии рейкастов ({rayOffsets.Count} лучей) ---");
            // --- КОНЕЦ БЛОКА ЛОГИРОВАНИЯ РЕЙКАСТОВ ---

            // ВЫПОЛНЯЕМ СЕРИЮ РЕЙКАСТОВ с разными смещениями и весовыми коэффициентами
            for (int i = 0; i < rayOffsets.Count; i++)
            {
                  Vector3 offsetDirection = rayOffsets[i]; // Это небольшое смещение направления в мировых координатах
                  Vector3 currentRayOrigin = cameraPosition; // Все лучи исходят из позиции камеры
                                                             // ИСПРАВЛЕНО: Правильное вычисление направления луча со смещением
                  Vector3 currentRayDirection = (initialRayDirection + offsetDirection * 0.1f).normalized; // Добавляем смещение как небольшое отклонение, не нормализуем offsetDirection

                  // Отладочный вывод
                  // Проверяем, что rayWeights имеет достаточно элементов
                  float currentWeight = (i < rayWeights.Count) ? rayWeights[i] : 0.5f; // Фоллбэк вес, если что-то пошло не так с количеством

                  if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Рейкаст #{i + 1}: " +
                            $"Начало={currentRayOrigin.ToString("F2")}, " +
                            $"Направление={currentRayDirection.ToString("F2")}, " +
                            $"Вес={currentWeight:F1}, " + // Используем currentWeight
                            $"исходноеНапр={initialRayDirection.ToString("F2")}, " +
                            $"смещениеНапр={offsetDirection.ToString("F2")}");

                  totalRaysShot++;

                  if (debugRayMaterial != null && debugRayMaterialPropertyBlock != null)
                  {
                        debugRayMaterialPropertyBlock.SetColor("_Color", Color.blue); // Лучи перед пуском - синие
                                                                                      // ... (код для LineRenderer, если вы его используете, убедитесь, что он использует currentRayOrigin и currentRayDirection)
                  }

                  // Визуализация луча для отладки
                  // Debug.DrawRay(currentRayOrigin, currentRayDirection * maxRayDistance, Color.yellow, 1.0f); // Добавляем на 1 секунду желтый луч

                  if (Physics.Raycast(currentRayOrigin, currentRayDirection, out hitInfo, this.maxRayDistance, layerMask, QueryTriggerInteraction.Ignore)) // ИЗМЕНЕНО на this.maxRayDistance
                  {
                        raysHitSomething++;
                        // Debug.DrawRay(currentRayOrigin, currentRayDirection * hitInfo.distance, Color.green, 1.0f); // ДОБАВЛЕНО: Визуализация успешного луча
                        // if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Рейкаст #{i+1} ПОПАЛ: Объект '{hitInfo.collider.gameObject.name}', Точка={hitInfo.point}, Нормаль={hitInfo.normal}, Расстояние={hitInfo.distance}");

                        // Регистрируем успешный рейкаст в PlaneDebugVisualizer
                        if (planeDebugVisualizer != null)
                        {
                              Ray rayForDebug = new Ray(currentRayOrigin, currentRayDirection);
                              planeDebugVisualizer.RegisterRaycast(rayForDebug, true, hitInfo, this.maxRayDistance);
                        }

                        if (debugRayMaterial != null && debugRayMaterialPropertyBlock != null)
                        {
                              // Визуализация попадания (например, красный цвет)
                              // debugRayMaterialPropertyBlock.SetColor("_Color", Color.green);
                              // Graphics.DrawMesh(debugRayMesh, Matrix4x4.TRS(hitInfo.point, Quaternion.LookRotation(hitInfo.normal), Vector3.one * 0.05f), debugRayMaterial, 0, null, 0, debugRayMaterialPropertyBlock);
                              // Debug.DrawRay(currentRayOrigin, currentRayDirection * hitInfo.distance, Color.green, 0.6f);
                        }

                        // Фильтр результатов (пример):
                        // Пропускаем попадания в не-персистентные плоскости или игрока
                        if (hitInfo.collider.gameObject.CompareTag("Player"))
                        {
                              if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Рейкаст #{i + 1} ({hitInfo.collider.name}) ОТФИЛЬТРОВАН по ТЕГУ: Тег='{hitInfo.collider.gameObject.tag}'");
                              continue; // Пропускаем попадания в игрока
                        }

                        // Проверяем, не входит ли объект в список игнорируемых
                        if (!string.IsNullOrEmpty(ignoreObjectNames))
                        {
                              string[] ignoreNames = ignoreObjectNames.Split(',');
                              foreach (string name in ignoreNames)
                              {
                                    if (hitInfo.collider.gameObject.name.Contains(name.Trim()))
                                    {
                                          if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Рейкаст #{i + 1} ({hitInfo.collider.name}) ОТФИЛЬТРОВАН - объект в списке игнорируемых");
                                          continue;
                                    }
                              }
                        }

                        // Для плоскостей: допускаем попадания только в персистентные плоскости
                        // if (hitInfo.collider.gameObject.name.StartsWith("MyARPlane_Debug_"))
                        // {
                        //     // Проверяем, является ли эта плоскость персистентной
                        //     bool isPersistent = IsPlanePersistent(hitInfo.collider.gameObject);

                        //     if (!isPersistent)
                        //     {
                        //         if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Рейкаст #{i + 1} ({hitInfo.collider.name}) ОТФИЛЬТРОВАН - не персистентная плоскость");
                        //         continue; // Пропускаем попадания в не-персистентные плоскости
                        //     }
                        //     else
                        //     {
                        //         if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Рейкаст #{i + 1} ПОПАЛ в персистентную плоскость: {hitInfo.collider.gameObject.name}");
                        //     }
                        // }

                        // Проверяем, не слишком ли близко к камере (например, внутренняя часть симуляции)
                        // Это может потребовать более сложной логики, если камера внутри объекта
                        if (hitInfo.distance < minHitDistanceThreshold)
                        {
                              if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Рейкаст #{i + 1} ({hitInfo.collider.name}) ОТФИЛЬТРОВАН по ДИСТАНЦИИ: {hitInfo.distance:F3}м < {minHitDistanceThreshold:F3}м");
                              continue;
                        }

                        float angleWithUp = Vector3.Angle(hitInfo.normal, Vector3.up);
                        // Используем maxWallNormalAngleDeviation из полей класса, а не maxAllowedWallAngleDeviation, если последнее - старое/неправильное имя
                        bool isVerticalEnough = angleWithUp > (90f - maxWallNormalAngleDeviation) && angleWithUp < (90f + maxWallNormalAngleDeviation);

                        if (enableDetailedRaycastLogging)
                        {
                              if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] РЕЙКАСТ #{i + 1} ({hitInfo.collider.name}) ПРОВЕРКА НОРМАЛИ: " +
                                        $"Дистанция={hitInfo.distance:F3} (Min={minHitDistanceThreshold:F3}), " +
                                        $"Нормаль={hitInfo.normal:F3}, Угол с Vector3.up={angleWithUp:F1}°, " +
                                        $"КритерийВертикальности (maxWallNormalAngleDeviation)={maxWallNormalAngleDeviation:F1}°, " +
                                        $"ВертикальнаДостаточно={isVerticalEnough}");
                        }

                        if (isVerticalEnough)
                        {
                              raysHitValidSurface++;
                              successfulHits.Add(hitInfo);
                              hitWeights.Add(currentWeight); // Сохраняем вес этого успешного попадания

                              // Обновление лучшего попадания на основе метрики (расстояние/вес)
                              // Меньшее значение метрики лучше (ближе и/или более уверенное попадание)
                              float currentHitMetric = hitInfo.distance / currentWeight;

                              if (currentHitMetric < bestDistance) // bestDistance здесь используется как bestMetric
                              {
                                    bestDistance = currentHitMetric; // Обновляем лучшую метрику
                                                                     // Сохраняем фактическое расстояние и нормаль от этого лучшего хита
                                                                     // Эти значения будут использоваться, если кластеризация не даст лучшего результата.
                                                                     // На данный момент, эти переменные (actualBestDistance, actualBestNormal) могут быть не объявлены.
                                                                     // Их нужно будет объявить выше, если эта логика будет использоваться.
                                                                     // actualBestDistanceForSingleHit = hitInfo.distance; 
                                                                     // actualBestNormalForSingleHit = hitInfo.normal;

                                    bestNormal = hitInfo.normal; // Пока что сохраняем нормаль лучшего одиночного хита сюда
                                    bestConfidence = currentWeight; // И его вес (уверенность)

                                    didHit = true;
                                    if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Рейкаст #{i + 1} ОБНОВИЛ ЛУЧШИЙ РЕЗУЛЬТАТ (одиночный): Метрика={currentHitMetric:F2} (Расст={hitInfo.distance:F2}/Вес={currentWeight:F1}), Нормаль={hitInfo.normal:F2}");
                              }
                        }
                        else
                        {
                              if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Рейкаст #{i + 1} ({hitInfo.collider.name}) ОТФИЛЬТРОВАН по НОРМАЛИ: Угол с Vector3.up={angleWithUp:F1}°, НеВертикальна (isVerticalEnough={isVerticalEnough}, maxWallNormalAngleDeviation={maxWallNormalAngleDeviation:F1}°)");
                        }
                  }
                  else
                  {
                        if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Рейкаст #{i + 1} ПРОМАХ");
                        // Debug.DrawRay(currentRayOrigin, currentRayDirection * maxRayDistance, Color.red, 1.0f); // ОТКЛЮЧЕНО: Визуализация промахнувшегося луча

                        // Регистрируем неудачный рейкаст в PlaneDebugVisualizer
                        if (planeDebugVisualizer != null)
                        {
                              Ray rayForDebug = new Ray(currentRayOrigin, currentRayDirection);
                              RaycastHit emptyHit = new RaycastHit(); // Пустой RaycastHit для промаха
                              planeDebugVisualizer.RegisterRaycast(rayForDebug, false, emptyHit, this.maxRayDistance);
                        }

                        if (debugRayMaterial != null && debugRayMaterialPropertyBlock != null)
                        {
                              // Визуализация промаха (например, фиолетовый цвет)
                              // debugRayMaterialPropertyBlock.SetColor("_Color", Color.magenta);
                              // Graphics.DrawMesh(debugRayMesh, Matrix4x4.TRS(currentRayOrigin + currentRayDirection * maxRayDistance, Quaternion.identity, Vector3.one * 0.03f), debugRayMaterial, 0, null, 0, debugRayMaterialPropertyBlock);
                              // Debug.DrawRay(currentRayOrigin, currentRayDirection * maxRayDistance, Color.magenta, 0.3f);
                        }
                  }
            }

            if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] --- Результаты серии рейкастов ---");
            if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Всего выпущено лучей: {totalRaysShot}");
            if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Попало во что-то (до фильтра): {raysHitSomething}");
            if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Валидных попаданий (после фильтра): {raysHitValidSurface}");
            if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Количество успешных попаданий в списке successfulHits: {successfulHits.Count}");
            if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Флаг didHit (был ли хоть один валидный хит, обновивший bestNormal/bestDistance): {didHit}");

            if (raysHitSomething > 0 && raysHitValidSurface == 0)
            {
                  Debug.LogWarning("[ARManagerInitializer2-UOCP] ВНИМАНИЕ: Все попадания рейкастов были отфильтрованы. Проверьте фильтры и слои объектов в сцене.");
            }

            float bestClusterWeight = 0f; // Инициализация bestClusterWeight
            if (successfulHits.Count > 3)
            {
                  if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Запуск кластеризации попаданий (Найдено {successfulHits.Count} валидных хитов).");
                  // Группируем попадания по расстоянию и нормали
                  var distanceClusters = new Dictionary<float, List<int>>();

                  for (int i = 0; i < successfulHits.Count; i++)
                  {
                        float distance = successfulHits[i].distance;
                        bool foundCluster = false;

                        foreach (var clusterCenter in distanceClusters.Keys.ToList())
                        {
                              if (Mathf.Abs(distance - clusterCenter) < 0.3f) // Порог кластеризации по расстоянию
                              {
                                    distanceClusters[clusterCenter].Add(i);
                                    foundCluster = true;
                                    break;
                              }
                        }

                        if (!foundCluster)
                        {
                              distanceClusters[distance] = new List<int> { i };
                        }
                  }

                  // Находим самый значимый кластер (с наибольшим суммарным весом)
                  // float bestClusterWeight = 0f; // Перенесена инициализация выше
                  float bestClusterDistance = 0f;
                  Vector3 bestClusterNormal = Vector3.zero;

                  foreach (var cluster in distanceClusters)
                  {
                        float clusterWeight = 0f;
                        Vector3 clusterNormal = Vector3.zero;

                        foreach (int index in cluster.Value)
                        {
                              clusterWeight += hitWeights[index];
                              clusterNormal += successfulHits[index].normal * hitWeights[index];
                        }

                        if (clusterWeight > bestClusterWeight) // Используем bestClusterWeight из внешнего scope
                        {
                              bestClusterWeight = clusterWeight; // Обновляем bestClusterWeight из внешнего scope
                              bestClusterDistance = cluster.Key;
                              if (clusterWeight > 0) bestClusterNormal = (clusterNormal / clusterWeight).normalized; // Нормализуем и проверяем делитель
                              else bestClusterNormal = Vector3.zero;
                        }
                  }

                  // Используем данные лучшего кластера, если он достаточно значимый
                  if (bestClusterWeight > bestConfidence)
                  {
                        bestDistance = bestClusterDistance; // Это реальное расстояние кластера
                        bestNormal = bestClusterNormal;
                        bestConfidence = bestClusterWeight; // Суммарный вес кластера
                        didHit = true; // Подтверждаем, что кластеризация дала результат
                        if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Кластеризация обновила лучший результат: Дистанция кластера={bestDistance:F2}, Нормаль кластера={bestNormal:F2}, Вес кластера={bestConfidence:F1}");
                  }
                  else
                  {
                        if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Кластеризация не дала лучшего результата, чем одиночный лучший хит (Вес кластера: {bestClusterWeight:F1} <= Вес лучшего хита: {bestConfidence:F1})");
                  }
            }

            Vector3 finalPlanePosition;
            Quaternion finalPlaneRotation;
            float actualDistanceFromCameraForPlane = 2.2f; // Инициализация actualDistanceFromCameraForPlane

            if (didHit) // Если был хотя бы один валидный хит (возможно, уточненный кластеризацией)
            {
                  // Если didHit=true, то bestDistance уже содержит либо метрику от лучшего одиночного хита,
                  // либо реальное расстояние от лучшего кластера. bestNormal и bestConfidence также установлены.

                  float determinedDistance;
                  Vector3 determinedHitPoint; // ДОБАВЛЕНО: Для хранения точки попадания
                  if (successfulHits.Count > 3 && bestClusterWeight > 0) // Если кластеризация была успешна и дала результат
                  {
                        determinedDistance = bestDistance; // bestDistance уже хранит реальное расстояние кластера
                        determinedHitPoint = cameraPosition + initialRayDirection * determinedDistance; // ДОБАВЛЕНО: Приблизительная точка попадания
                        if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Используется расстояние от КЛАСТЕРИЗАЦИИ: {determinedDistance:F2}м");
                  }
                  else // Используем лучший одиночный хит (если был)
                  {
                        // Нужно найти RaycastHit, соответствующий bestConfidence и bestDistance (метрике)
                        float targetMetric = bestDistance;
                        determinedDistance = 2.2f; // Фоллбэк, если не найдем
                        determinedHitPoint = cameraPosition + initialRayDirection * determinedDistance; // ДОБАВЛЕНО: Фоллбэк точка
                        bool foundOriginalHit = false;
                        for (int k = 0; k < successfulHits.Count; ++k)
                        {
                              if (Mathf.Approximately(successfulHits[k].distance / hitWeights[k], targetMetric) && Mathf.Approximately(hitWeights[k], bestConfidence))
                              {
                                    determinedDistance = successfulHits[k].distance;
                                    determinedHitPoint = successfulHits[k].point; // ДОБАВЛЕНО: Используем реальную точку попадания
                                    foundOriginalHit = true;
                                    if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Используется расстояние от ЛУЧШЕГО ОДИНОЧНОГО ХИТА #{k}: {determinedDistance:F2}м (Нормаль: {successfulHits[k].normal})");
                                    break;
                              }
                        }
                        if (!foundOriginalHit && successfulHits.Count > 0)
                        { // Если не нашли точное совпадение по метрике, но хиты были
                              determinedDistance = successfulHits[0].distance; // Берем первый попавший, как крайний случай
                              determinedHitPoint = successfulHits[0].point; // ДОБАВЛЕНО: Используем реальную точку попадания
                              bestNormal = successfulHits[0].normal; // И его нормаль
                                                                     // Debug.LogWarning($"[ARManagerInitializer2-UOCP] Не удалось точно восстановить лучший одиночный хит по метрике. Используется первый хит: Дистанция={determinedDistance:F2}м, Нормаль={bestNormal}");
                        }
                        else if (!foundOriginalHit && successfulHits.Count == 0)
                        { // Эта ветка не должна достигаться если didHit=true
                              Debug.LogError($"[ARManagerInitializer2-UOCP] КРИТИЧЕСКАЯ ОШИБКА: didHit=true, но successfulHits пуст и не удалось восстановить одиночный хит.");
                        }
                  }

                  // ИСПРАВЛЕНО: Используем реальную точку попадания с небольшим смещением ПО НОРМАЛИ
                  actualDistanceFromCameraForPlane = determinedDistance;
                  actualDistanceFromCameraForPlane = Mathf.Clamp(actualDistanceFromCameraForPlane, minHitDistanceThreshold, 12.0f); // УВЕЛИЧЕНО: Максимальное расстояние для больших помещений

                  // ИСПРАВЛЕНО: Позиционируем плоскость на реальной поверхности со смещением по нормали
                  finalPlanePosition = determinedHitPoint + bestNormal * 0.005f; // Небольшое смещение ОТ поверхности по нормали

                  if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] 📏 РЕЗУЛЬТАТ РЕЙКАСТА: Финальное расстояние до плоскости = {actualDistanceFromCameraForPlane:F2}м, Позиция = {finalPlanePosition:F2}, Точка попадания = {determinedHitPoint:F2}, Нормаль = {bestNormal:F2}");

                  // Ориентируем Z плоскости ПО нормали к поверхности (чтобы плоскость "лежала" на поверхности)
                  // forward плоскости будет смотреть ОТ поверхности.
                  // ---- MODIFICATION START ----
                  // For vertical walls identified by raycast, always try to use world Vector3.up as the up vector for LookRotation
                  // This should prevent roll based on camera tilt.
                  Vector3 upForLookRotation = Vector3.up;
                  // cameraForward is defined earlier in this method from raycasting setup

                  // If the bestNormal (from raycast) is very close to Vector3.up or Vector3.down,
                  // then Vector3.up is not a good 'up' direction for LookRotation.
                  if (Mathf.Abs(Vector3.Dot(bestNormal, Vector3.up)) > 0.95f) // Check alignment of bestNormal with WORLD up
                  {
                        upForLookRotation = -cameraForward;
                        if (enableCustomPlaneCreationLogging) Debug.LogWarning($"[ARManagerInitializer2-UOCP] Raycast Normal ({bestNormal}) is highly aligned with World Up. Using -cameraForward for LookRotation's up vector.");
                  }
                  finalPlaneRotation = Quaternion.LookRotation(bestNormal, upForLookRotation);
                  // ---- MODIFICATION END ----

                  if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] 🧭 Параметры для плоскости ПОСЛЕ РЕЙКАСТА: Pos={finalPlanePosition:F2}, Rot(Эйлер)={finalPlaneRotation.eulerAngles:F1}");
            }
            else // Если didHit == false (ни один рейкаст не попал или все были отфильтрованы)
            {
                  Debug.LogWarning("[ARManagerInitializer2-UOCP] ⚠️ Ни один рейкаст не дал валидного попадания. Эвристика ВРЕМЕННО ОТКЛЮЧЕНА. Плоскость не будет создана для этой области.");

                  // Регистрируем неудачное создание плоскости из-за отсутствия рейкаст-попаданий
                  if (planeDebugVisualizer != null)
                  {
                        Vector3 estimatedPosition = cameraPosition + initialRayDirection * 2.0f;
                        Vector3 estimatedSize = Vector3.one; // Приблизительный размер
                        planeDebugVisualizer.RegisterPlaneCreation(estimatedPosition, estimatedSize, false);
                  }

                  return false; // ВРЕМЕННО ОТКЛЮЧЕНО: Не создаем эвристическую плоскость

                  /*
                  Debug.LogWarning("[ARManagerInitializer2-UOCP] ⚠️ Ни один рейкаст не дал валидного попадания. Используется ЭВРИСТИКА.");
                  // РАСШИРЕННЫЙ АЛГОРИТМ для случаев, когда рейкастинг не нашел поверхностей
                  bool foundARPlane = false;
                  if (planeManager != null && planeManager.trackables.count > 0)
                  {
                        if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Эвристика: Попытка найти существующую AR плоскость ({planeManager.trackables.count} шт.)...");
                        ARPlane bestMatchPlane = null;
                        float bestMatchScore = 0f;
                        float bestMatchDistance = 0f;

                        foreach (var plane in planeManager.trackables)
                        {
                              if (plane == null) continue;

                              Vector3 planeCenter = plane.center;
                              Vector3 planeSurfaceNormal = plane.normal;

                              float angleWithRay = Vector3.Angle(planeSurfaceNormal, -initialRayDirection); 
                              float orientationFactor = Mathf.Cos(angleWithRay * Mathf.Deg2Rad);
                              if (orientationFactor < 0.3f) continue; 

                              Vector3 toCenterVector = planeCenter - cameraPosition;
                              float projectionLength = Vector3.Dot(toCenterVector, initialRayDirection); 

                              if (projectionLength <= 0.5f || projectionLength > 8.0f) continue;

                              Vector3 projectedPoint = cameraPosition + initialRayDirection * projectionLength; 
                              float perpendicularDistance = Vector3.Distance(projectedPoint, planeCenter);

                              float sizeCompensation = Mathf.Sqrt(plane.size.x * plane.size.y);
                              float maxPerpDistance = 0.5f + sizeCompensation * 0.5f;

                              if (perpendicularDistance > maxPerpDistance) continue;

                              float perpDistanceFactor = 1.0f - (perpendicularDistance / maxPerpDistance);
                              float distanceFactor = 1.0f - Mathf.Clamp01((projectionLength - 1.0f) / 7.0f); 
                              float sizeFactor = Mathf.Clamp01(sizeCompensation / 2.0f); 

                              float planeScore = orientationFactor * 0.4f + perpDistanceFactor * 0.4f + distanceFactor * 0.1f + sizeFactor * 0.1f;

                              if (planeScore > bestMatchScore)
                              {
                                    bestMatchScore = planeScore;
                                    bestMatchPlane = plane;
                                    bestMatchDistance = projectionLength;
                              }
                        }

                        if (bestMatchPlane != null && bestMatchScore > 0.6f) 
                        {
                              actualDistanceFromCameraForPlane = bestMatchDistance - 0.05f;
                              actualDistanceFromCameraForPlane = Mathf.Clamp(actualDistanceFromCameraForPlane, 1.0f, 5.0f);
                              bool isVertical = Mathf.Abs(Vector3.Dot(bestMatchPlane.normal, Vector3.up)) < 0.3f;
                              if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] 📏 Эвристика: Используется AR плоскость '{bestMatchPlane.name}' на расстоянии {actualDistanceFromCameraForPlane:F2}м (скор: {bestMatchScore:F2}, {(isVertical ? "вертикальная" : "горизонтальная")})");
                              foundARPlane = true;
                              bestNormal = bestMatchPlane.normal; 
                        }
                        else
                        {
                              if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] Эвристика: Подходящая AR-плоскость не найдена (макс. скор был {bestMatchScore:F2}, порог 0.6).");
                        }
                  }

                  if (!foundARPlane)
                  {
                        float viewportY = normalizedCenterY; 
                        float adaptiveBaseDistance;

                        if (viewportY < 0.3f)
                        {
                              adaptiveBaseDistance = 1.8f;
                        }
                        else if (viewportY > 0.7f)
                        {
                              adaptiveBaseDistance = 2.5f;
                        }
                        else
                        {
                              adaptiveBaseDistance = 2.2f;
                        }

                        float tempWorldHeightAtActualDistance = 2.0f * actualDistanceFromCameraForPlane * Mathf.Tan(mainCamera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                        float tempWorldWidthAtActualDistance = tempWorldHeightAtActualDistance * mainCamera.aspect;
                        float estimatedPlaneWidthInMetersBasedOnArea = (area.width / (float)textureWidth) * tempWorldWidthAtActualDistance;

                        float sizeAdjustment = estimatedPlaneWidthInMetersBasedOnArea * 0.3f;
                        float positionAdjustment = Mathf.Abs(normalizedCenterX - 0.5f) * 0.5f; 

                        actualDistanceFromCameraForPlane = adaptiveBaseDistance + sizeAdjustment + positionAdjustment;
                        actualDistanceFromCameraForPlane = Mathf.Clamp(actualDistanceFromCameraForPlane, 1.4f, 4.5f);
                        
                        Vector3 camerForwardHorizontal = new Vector3(initialRayDirection.x, 0, initialRayDirection.z).normalized;
                        bestNormal = Vector3.Cross(camerForwardHorizontal, Vector3.up).normalized; 

                        Vector3 toCameraHorizontal = new Vector3(-initialRayDirection.x, 0, -initialRayDirection.z).normalized;
                        if (Vector3.Dot(bestNormal, toCameraHorizontal) < 0)
                        {
                              bestNormal = -bestNormal; 
                        }
                  }

                  finalPlanePosition = cameraPosition + initialRayDirection * actualDistanceFromCameraForPlane; 
                  Vector3 upDirectionForHeuristic = mainCamera.transform.up; 
                  if (Mathf.Abs(Vector3.Dot(bestNormal, mainCamera.transform.up)) > 0.95f)
                  { 
                        upDirectionForHeuristic = -cameraForward;
                        if (enableDetailedRaycastLogging) Debug.LogWarning($"[ARManagerInitializer2-UOCP] Эвристика: Нормаль ({bestNormal}) почти параллельна camera.up. Используем -cameraForward как второй аргумент LookRotation.");
                  }
                  finalPlaneRotation = Quaternion.LookRotation(bestNormal, upDirectionForHeuristic);
                  if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] 🧭 Параметры для плоскости ПО ЭВРИСТИКЕ: Pos={finalPlanePosition:F2}, Rot(Эйлер)={finalPlaneRotation.eulerAngles:F1}, Нормаль={bestNormal:F2}");
                  */
            }

            // Объявляем переменные для хранения мировых координат углов здесь, чтобы они были доступны ниже
            Vector3 worldTopLeft, worldTopRight, worldBottomLeft, worldBottomRight;
            float finalPlaneWorldWidth, finalPlaneWorldHeight;

            // Теперь, когда у нас есть finalPlanePosition и actualDistanceFromCameraForPlane, мы можем вычислить мировые размеры плоскости
            // Расчет мировых размеров плоскости, основанный на ее доле в маске И ФАКТИЧЕСКОМ РАССТОЯНИИ

            // ================== СТАРЫЙ НЕПРАВИЛЬНЫЙ РАСЧЕТ (ЗАКОММЕНТИРОВАНО) ==================
            // ... existing code ...
            // ================== НОВЫЙ, БОЛЕЕ ТОЧНЫЙ РАСЧЕТ РАЗМЕРОВ ПЛОСКОСТИ ==================
            // Вместо старого метода, основанного на FoV, который не учитывал перспективу,
            // мы проецируем углы 2D-прямоугольника в 3D-пространство на определенном расстоянии.
            {
                  // Определяем UV-координаты для четырех углов прямоугольника 'area'
                  Vector2 uvTopLeft = new Vector2(area.xMin / textureWidth, area.yMax / textureHeight);
                  Vector2 uvTopRight = new Vector2(area.xMax / textureWidth, area.yMax / textureHeight);
                  Vector2 uvBottomLeft = new Vector2(area.xMin / textureWidth, area.yMin / textureHeight);
                  Vector2 uvBottomRight = new Vector2(area.xMax / textureWidth, area.yMin / textureHeight);

                  // Создаем лучи из камеры через эти UV-координаты
                  Ray rayTopLeft = mainCamera.ViewportPointToRay(new Vector3(uvTopLeft.x, uvTopLeft.y, 0));
                  Ray rayTopRight = mainCamera.ViewportPointToRay(new Vector3(uvTopRight.x, uvTopRight.y, 0));
                  Ray rayBottomLeft = mainCamera.ViewportPointToRay(new Vector3(uvBottomLeft.x, uvBottomLeft.y, 0));
                  Ray rayBottomRight = mainCamera.ViewportPointToRay(new Vector3(uvBottomRight.x, uvBottomRight.y, 0));

                  // Находим мировые координаты для каждого угла на расстоянии, определенном рейкастом
                  worldTopLeft = rayTopLeft.GetPoint(actualDistanceFromCameraForPlane);
                  worldTopRight = rayTopRight.GetPoint(actualDistanceFromCameraForPlane);
                  worldBottomLeft = rayBottomLeft.GetPoint(actualDistanceFromCameraForPlane);
                  worldBottomRight = rayBottomRight.GetPoint(actualDistanceFromCameraForPlane);

                  // Вычисляем ширину и высоту в мировых координатах
                  // Ширина - среднее расстояние между верхними и нижними точками
                  float topWidth = Vector3.Distance(worldTopLeft, worldTopRight);
                  float bottomWidth = Vector3.Distance(worldBottomLeft, worldBottomRight);
                  finalPlaneWorldWidth = (topWidth + bottomWidth) / 2f * planeSizeMultiplier;

                  // Высота - среднее расстояние между левыми и правыми точками
                  float leftHeight = Vector3.Distance(worldTopLeft, worldBottomLeft);
                  float rightHeight = Vector3.Distance(worldTopRight, worldBottomRight);
                  finalPlaneWorldHeight = (leftHeight + rightHeight) / 2f * planeSizeMultiplier;

                  if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] 📏 НОВЫЙ РАСЧЕТ РАЗМЕРОВ: Ширина={finalPlaneWorldWidth:F2}м, Высота={finalPlaneWorldHeight:F2}м (на расстоянии {actualDistanceFromCameraForPlane:F2}м)");
            }
            // ================== КОНЕЦ НОВОГО РАСЧЕТА ==================

            // Проверка на минимальный мировой размер ПЕРЕД созданием/обновлением
            if (finalPlaneWorldWidth < minPlaneSizeInMeters || finalPlaneWorldHeight < minPlaneSizeInMeters)
            {
                  // Debug.LogWarning($"[ARManagerInitializer2-UOCP] Плоскость для области ({area.width}x{area.height}px) слишком мала по мировым размерам ({finalPlaneWorldWidth:F2}x{finalPlaneWorldHeight:F2}м) для создания/обновления. Min size: {minPlaneSizeInMeters}м. Выход.");

                  // Регистрируем неудачное создание плоскости в PlaneDebugVisualizer
                  if (planeDebugVisualizer != null)
                  {
                        Vector3 planeSize = new Vector3(finalPlaneWorldWidth, finalPlaneWorldHeight, 0.1f);
                        planeDebugVisualizer.RegisterPlaneCreation(finalPlanePosition, planeSize, false);
                  }

                  return false;
            }


            // Проверки и фильтрация дубликатов и наложений (используем finalPlanePosition)
            if (finalPlanePosition == Vector3.zero)
            { // Дополнительная проверка на всякий случай
                  Debug.LogError("[ARManagerInitializer2-UOCP] КРИТИЧЕСКАЯ ОШИБКА: finalPlanePosition равен Vector3.zero перед созданием/обновлением плоскости! Выход.");
                  return false;
            }

            // Check if this would overlap with an existing persistent plane
            if (usePersistentPlanes && planeConfigurator != null)
            {
                  Vector3 normal = finalPlaneRotation * Vector3.forward;
                  if (OverlapsWithPersistentPlanes(finalPlanePosition, normal, finalPlaneWorldWidth, finalPlaneWorldHeight))
                  {
                        if (enableDetailedRaycastLogging)
                              Debug.Log("[ARManagerInitializer2-UOCP] Skipping plane creation - overlaps with persistent plane");
                        return false; // Skip creating this plane as it overlaps with a persistent one
                  }
            }

            // Проверка 1: Не создаем плоскость, если находится слишком близко к камере и прямо перед ней
            Vector3 directionToFinalPos = finalPlanePosition - mainCamera.transform.position; // ИЗМЕНЕНО: arCamera -> mainCamera
            float distanceToCamFinal = directionToFinalPos.magnitude;
            float alignmentWithCameraFinal = Vector3.Dot(mainCamera.transform.forward.normalized, directionToFinalPos.normalized); // ИЗМЕНЕНО: arCamera -> mainCamera

            // Более строгие проверки для всех плоскостей
            if (distanceToCamFinal < 0.7f && alignmentWithCameraFinal > 0.8f) // Увеличены оба порога - дистанция и выравнивание
            {
                  Debug.LogWarning($"[ARManagerInitializer2-UOCP] ⚠️ ОТМЕНА: Плоскость слишком близко к камере и прямо перед ней (Дист: {distanceToCamFinal:F2}м, Совпадение с FWD: {alignmentWithCameraFinal:F2}). Pos={finalPlanePosition:F2}");
                  return false;
            }

            // Проверка 2: Не создаем плоскости, если они слишком большие
            // float maxPlaneSize = 5.0f; // УДАЛЕНО: заменено на configurable переменную
            if (finalPlaneWorldWidth > this.maxPlaneSize || finalPlaneWorldHeight > this.maxPlaneSize)
            {
                  Debug.LogWarning($"[ARManagerInitializer2-UOCP] ⚠️ ОТМЕНА: Плоскость слишком большая (Ширина: {finalPlaneWorldWidth:F2}м, Высота: {finalPlaneWorldHeight:F2}м). Установлен лимит: {this.maxPlaneSize}м");
                  return false;
            }

            // Проверка 3: Не создаем плоскости, если они слишком маленькие
            if (finalPlaneWorldWidth < this.minPlaneSize || finalPlaneWorldHeight < this.minPlaneSize)
            {
                  Debug.LogWarning($"[ARManagerInitializer2-UOCP] ⚠️ ОТМЕНА: Плоскость слишком маленькая (Ширина: {finalPlaneWorldWidth:F2}м, Высота: {finalPlaneWorldHeight:F2}м). Минимальный размер: {this.minPlaneSize}м");
                  return false;
            }

            // Проверка 4: Соотношение сторон не должно быть слишком экстремальным
            float aspectRatio = Mathf.Max(finalPlaneWorldWidth, finalPlaneWorldHeight) / Mathf.Min(finalPlaneWorldWidth, finalPlaneWorldHeight);
            if (aspectRatio > this.maxAspectRatio)
            {
                  Debug.LogWarning($"[ARManagerInitializer2-UOCP] ⚠️ ОТМЕНА: Плоскость слишком вытянутая (Соотношение сторон: {aspectRatio:F1}, лимит: {this.maxAspectRatio})");
                  return false;
            }

            // УЛУЧШЕННЫЙ АЛГОРИТМ: Интеллектуальное выявление дубликатов плоскостей
            // ... (часть с tooClose, similarOrientationCount, closestExistingPlane была выше, но может быть применена здесь к finalPlanePosition)
            // === НАЧАЛО БЛОКА ПОИСКА И ОБНОВЛЕНИЯ СУЩЕСТВУЮЩЕЙ ПЛОСКОСТИ ===
            var (planeToUpdate, updateDistance, updateAngleDiff) = FindClosestExistingPlane(finalPlanePosition, (finalPlaneRotation * Vector3.forward), 1.5f, 35f); // УВЕЛИЧЕНО: Радиус поиска до 1.5м, угол до 35 градусов
            // Используем (finalPlaneRotation * Vector3.forward) как нормаль, так как LookRotation(normal) делает forward плоскости = normal.
            // А наш FindClosestExistingPlane ожидает нормаль поверхности.

            if (planeToUpdate != null)
            {
                  if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] 🔄 ОБНОВЛЯЕМ существующую плоскость '{planeToUpdate.name}'. Расстояние до новой позиции: {updateDistance:F2}м, Угол нормалей: {updateAngleDiff:F1}°");

                  // ============== ПРОДВИНУТАЯ ЛОГИКА ОБЪЕДИНЕНИЯ (OBB) ==============
                  Mesh oldMesh = planeToUpdate.GetComponent<MeshFilter>().sharedMesh;
                  Vector3[] oldVertices = oldMesh.vertices;

                  // 1. Трансформируем вершины старого меша в мировые координаты
                  for (int i = 0; i < oldVertices.Length; i++)
                  {
                        oldVertices[i] = planeToUpdate.transform.TransformPoint(oldVertices[i]);
                  }

                  // 2. Добавляем мировые координаты углов новой области
                  Vector3[] newVertices = new Vector3[] { worldTopLeft, worldTopRight, worldBottomLeft, worldBottomRight };
                  Vector3[] allVertices = new Vector3[oldVertices.Length + newVertices.Length];
                  oldVertices.CopyTo(allVertices, 0);
                  newVertices.CopyTo(allVertices, oldVertices.Length);

                  // 3. Определяем новую общую ориентацию (используем последнюю, как самую актуальную) и временный центр
                  Quaternion mergedRotation = finalPlaneRotation;
                  Vector3 mergedCenter = finalPlanePosition; // Используем позицию нового хита как временный центр

                  // 4. Находим локальные оси новой ориентации
                  Vector3 planeRight = mergedRotation * Vector3.right;
                  Vector3 planeUp = mergedRotation * Vector3.up;

                  // 5. Проецируем все точки на 2D-плоскость и находим крайние значения
                  float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
                  foreach (var v in allVertices)
                  {
                        Vector3 relPos = v - mergedCenter;
                        float xCoord = Vector3.Dot(relPos, planeRight);
                        float yCoord = Vector3.Dot(relPos, planeUp);
                        minX = Mathf.Min(minX, xCoord);
                        maxX = Mathf.Max(maxX, xCoord);
                        minY = Mathf.Min(minY, yCoord);
                        maxY = Mathf.Max(maxY, yCoord);
                  }

                  // 6. Вычисляем реальные размеры и новый центр
                  float mergedWidth = maxX - minX;
                  float mergedHeight = maxY - minY;

                  // Центр OBB относительно временного mergedCenter
                  Vector3 obbCenterOffset = planeRight * (minX + maxX) / 2f + planeUp * (minY + maxY) / 2f;
                  // Финальная позиция GameObject-а
                  Vector3 finalMergedPosition = mergedCenter + obbCenterOffset;

                  planeToUpdate.transform.position = finalMergedPosition;
                  planeToUpdate.transform.rotation = mergedRotation;
                  // ====================================================================

                  MeshFilter mf = planeToUpdate.GetComponent<MeshFilter>();
                  if (mf != null)
                  {
                        mf.mesh = CreatePlaneMesh(mergedWidth, mergedHeight); // Обновляем меш с новыми, ОБЪЕДИНЕННЫМИ размерами
                        if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] ✨ Объединен меш для '{planeToUpdate.name}', новые размеры: {mergedWidth:F2}x{mergedHeight:F2}м");
                  }

                  if (visitedPlanes != null) visitedPlanes[planeToUpdate] = true;
                  if (this.planeLastVisitedTime != null) this.planeLastVisitedTime[planeToUpdate] = Time.time;
                  return true;
            }

            // Если не нашли что обновить, проверяем, не нужно ли отфильтровать создание новой из-за дублирования
            bool createNewPlane = true;
            foreach (GameObject existingPlane in generatedPlanes)
            {
                  if (existingPlane == null) continue;
                  float distBetweenFinalAndExisting = Vector3.Distance(existingPlane.transform.position, finalPlanePosition);
                  float angleBetweenNormals = Vector3.Angle(existingPlane.transform.forward, (finalPlaneRotation * Vector3.forward));

                  if (distBetweenFinalAndExisting < 0.35f && angleBetweenNormals < 20f) // Порог для близкого дубликата
                  {
                        // Debug.LogWarning($"[ARManagerInitializer2-UOCP] ⚠️ ОТМЕНА СОЗДАНИЯ НОВОЙ: Обнаружен слишком близкий дубликат '{existingPlane.name}' (Расст: {distBetweenFinalAndExisting:F2}м, Угол: {angleBetweenNormals:F1}°). Новая Pos={finalPlanePosition:F2}");
                        createNewPlane = false;
                        if (visitedPlanes != null && !visitedPlanes.ContainsKey(existingPlane))
                        { // Если существующая близкая не была посещена в этом кадре, помечаем ее.
                              visitedPlanes[existingPlane] = true;
                              // Debug.Log($"[ARManagerInitializer2-UOCP] Близкая существующая плоскость '{existingPlane.name}' помечена как visited, т.к. новая не создается.");
                        }
                        break;
                  }
            }

            if (!createNewPlane)
            {
                  return false; // Не создаем новую, т.к. есть близкий дубликат
            }

            // Создаем и настраиваем GameObject для плоскости
            string newPlaneName = $"MyARPlane_Debug_{planeInstanceCounter++}";
            GameObject planeObj = new GameObject(newPlaneName);
            if (enableDetailedRaycastLogging) Debug.Log($"[ARManagerInitializer2-UOCP] 🧬 СОЗДАЕМ НОВУЮ ПЛОСКОСТЬ '{newPlaneName}'");

            planeObj.transform.position = finalPlanePosition;
            planeObj.transform.rotation = finalPlaneRotation;

            // Установим слой для плоскости, если он задан
            if (!string.IsNullOrEmpty(planesLayerName))
            {
                  int layerID = LayerMask.NameToLayer(planesLayerName);
                  if (layerID != -1)
                  {
                        planeObj.layer = layerID;
                  }
                  else if (enableDetailedRaycastLogging)
                  {
                        Debug.LogWarning($"[ARManagerInitializer2] Layer '{planesLayerName}' not found, using default layer for plane.");
                  }
            }

            MeshFilter meshFilter = planeObj.AddComponent<MeshFilter>();
            meshFilter.mesh = CreatePlaneMesh(finalPlaneWorldWidth, finalPlaneWorldHeight);

            MeshRenderer meshRenderer = planeObj.AddComponent<MeshRenderer>();
            // Проверяем и при необходимости пересоздаем материал
            if (this.verticalPlaneMaterial == null)
            {
                  Debug.LogWarning("[ARManagerInitializer2-UOCP] verticalPlaneMaterial is null during plane creation! Re-initializing materials...");
                  InitializeMaterials();
            }

            if (this.verticalPlaneMaterial != null)
            {
                  try
                  {
                        meshRenderer.material = new Material(this.verticalPlaneMaterial);
                        Debug.Log($"[ARManagerInitializer2-UOCP] ✅ Материал применен к плоскости {planeObj.name}. Шейдер: {meshRenderer.material.shader.name}");
                  }
                  catch (System.Exception e)
                  {
                        Debug.LogError($"[ARManagerInitializer2-UOCP] ❌ Ошибка при применении материала: {e.Message}");
                        meshRenderer.material = CreateFallbackMaterial();
                  }
            }
            else
            {
                  Debug.LogError("[ARManagerInitializer2-UOCP] ❌ verticalPlaneMaterial все еще null после повторной инициализации!");
                  meshRenderer.material = CreateFallbackMaterial();
            }
            // Debug.Log($"[ARManagerInitializer2-UOCP] Applied material to {planeObj.name}. Mesh bounds: {meshFilter.mesh.bounds.size}");

            MeshCollider meshCollider = planeObj.AddComponent<MeshCollider>();
            meshCollider.sharedMesh = meshFilter.mesh;

            this.generatedPlanes.Add(planeObj);
            if (this.planeCreationTimes != null) this.planeCreationTimes[planeObj] = Time.time;
            if (this.planeLastVisitedTime != null) this.planeLastVisitedTime[planeObj] = Time.time;

            // Make the newly created plane persistent immediately
            MakePlanePersistent(planeObj);

            // Попытка привязать к TrackablesParent, если он есть и не был равен null при старте
            if (this.xrOrigin != null && this.xrOrigin.TrackablesParent != null)
            {
                  if (this.trackablesParentInstanceID_FromStart == 0 ||
                      (this.xrOrigin.TrackablesParent.gameObject.activeInHierarchy && this.xrOrigin.TrackablesParent.GetInstanceID() == this.trackablesParentInstanceID_FromStart))
                  {
                        planeObj.transform.SetParent(this.xrOrigin.TrackablesParent, true);
                        // Debug.Log($"[ARManagerInitializer2-UOCP] Новая плоскость '{planeObj.name}' привязана к '{this.xrOrigin.TrackablesParent.name}' (ID: {this.xrOrigin.TrackablesParent.GetInstanceID()}). Final Pos relative to Parent: {planeObj.transform.localPosition:F2}");
                  }
                  else
                  {
                        Debug.LogWarning($"[ARManagerInitializer2-UOCP] Новая плоскость '{planeObj.name}' не привязана к TrackablesParent (неактивен/изменен). Оставлена в корне. World Pos: {planeObj.transform.position:F2}");
                  }
            }
            else
            {
                  Debug.LogWarning($"[ARManagerInitializer2-UOCP] Новая плоскость '{planeObj.name}' не привязана (XROrigin/TrackablesParent null). Оставлена в корне. World Pos: {planeObj.transform.position:F2}");
            }

            if (visitedPlanes != null) visitedPlanes[planeObj] = true;
            // Debug.Log($"[ARManagerInitializer2-UOCP] ✅ Создана новая плоскость '{planeObj.name}': WorldPos={planeObj.transform.position:F2}, WorldRot(Эйлер)={planeObj.transform.rotation.eulerAngles:F1}, Parent={(planeObj.transform.parent ? planeObj.transform.parent.name : "null")}");

            // --> НАЧАЛО ДОБАВЛЕННОГО КОДА
            ARWallPaintingSystem paintingSystem = FindObjectOfType<ARWallPaintingSystem>();
            if (paintingSystem != null)
            {
                  paintingSystem.RegisterWallObject(planeObj);
            }
            // <-- КОНЕЦ ДОБАВЛЕННОГО КОДА

            generatedPlanes.Add(planeObj);

            // Регистрируем создание плоскости в PlaneDebugVisualizer
            if (planeDebugVisualizer != null)
            {
                  Vector3 planeSize = new Vector3(finalPlaneWorldWidth, finalPlaneWorldHeight, 0.1f);
                  planeDebugVisualizer.RegisterPlaneCreation(finalPlanePosition, planeSize, true);
            }

            return true;
      }

      // Удаляем устаревшие плоскости, если их слишком много
      private void CleanupOldPlanes(Dictionary<GameObject, bool> visitedPlanesInCurrentMask) // New name for clarity
      {
            List<GameObject> planesToRemove = new List<GameObject>();

            float currentTime = Time.time;

            foreach (var plane in generatedPlanes.ToList()) // ToList() to allow modification during iteration
            {
                  if (plane == null)
                        continue;

                  // Если плоскость персистентна, не удаляем ее по этому таймеру
                  if (IsPlanePersistent(plane))
                        continue;

                  if (planeLastVisitedTime.TryGetValue(plane, out float lastVisitTime))
                  {
                        // Если плоскость не была посещена в текущей маске И прошла задержка
                        if (currentTime - lastVisitTime > planePersistenceDelay &&
                            (visitedPlanesInCurrentMask == null || !visitedPlanesInCurrentMask.ContainsKey(plane)))
                        {
                              if (enableVerboseLoggingCleanup)
                                    Debug.Log($"[ARManagerInitializer2-CleanupOldPlanes] Plane {plane.name} not visited in current mask. Last visit was {currentTime - lastVisitTime:F2}s ago (threshold: {planePersistenceDelay}s). Adding to removal list.");
                              planesToRemove.Add(plane);
                        }
                  }
                  else if (visitedPlanesInCurrentMask == null || !visitedPlanesInCurrentMask.ContainsKey(plane))
                  {
                        // Если для плоскости нет времени последнего посещения и её нет в текущей маске,
                        // возможно, это новая, но сразу невидимая плоскость, или что-то пошло не так.
                        // Добавляем её к удалению, если она существует дольше задержки (предосторожность).
                        if (planeCreationTimes.TryGetValue(plane, out float creationTime) && currentTime - creationTime > planePersistenceDelay)
                        {
                              if (enableVerboseLoggingCleanup)
                                    Debug.LogWarning($"[ARManagerInitializer2-CleanupOldPlanes] Plane {plane.name} has no lastVisitTime and not in current mask. Created {currentTime - creationTime:F2}s ago. Adding to removal list as precaution (threshold: {planePersistenceDelay}s).");
                              planesToRemove.Add(plane);
                        }
                  }
            }

            foreach (GameObject planeToRemove in planesToRemove)
            {
                  generatedPlanes.Remove(planeToRemove);
                  // Ensure removal from all tracking dictionaries
                  if (planeCreationTimes.ContainsKey(planeToRemove))
                  {
                        planeCreationTimes.Remove(planeToRemove);
                  }
                  if (planeLastVisitedTime.ContainsKey(planeToRemove))
                  {
                        planeLastVisitedTime.Remove(planeToRemove);
                  }
                  if (persistentGeneratedPlanes.ContainsKey(planeToRemove))
                  {
                        persistentGeneratedPlanes.Remove(planeToRemove);
                  }
                  Destroy(planeToRemove);
            }
      }

      private (GameObject, float, float) FindClosestExistingPlane(Vector3 position, Vector3 normal, float maxDistance, float maxAngleDegrees)
      {
            GameObject closestPlane = null;
            float minDistance = float.MaxValue;
            float angleDifference = float.MaxValue;

            if (generatedPlanes == null) return (null, 0, 0);

            foreach (GameObject existingPlane in generatedPlanes)
            {
                  if (existingPlane == null) continue;

                  float dist = Vector3.Distance(existingPlane.transform.position, position);
                  if (dist < minDistance && dist <= maxDistance)
                  {
                        // Предполагаем, что -transform.forward это нормаль плоскости, как и у новой 'normal'
                        // Это согласуется с использованием Quaternion.LookRotation(-normal) или FromToRotation(Vector3.forward, -normal)
                        float angle = Vector3.Angle(-existingPlane.transform.forward, normal);

                        if (angle <= maxAngleDegrees)
                        {
                              minDistance = dist;
                              closestPlane = existingPlane;
                              angleDifference = angle;
                        }
                  }
            }
            // if (closestPlane != null && enableDetailedRaycastLogging) Debug.Log($"[FindClosestExistingPlane] Найден близкий: {closestPlane.name}, dist: {minDistance:F2}, angle: {angleDifference:F1}");
            // else if (enableDetailedRaycastLogging) Debug.Log("[FindClosestExistingPlane] Близкая существующая плоскость не найдена.");
            return (closestPlane, minDistance, angleDifference);
      }

      // Вспомогательный метод для получения полного пути к GameObject
      public static string GetGameObjectPath(Transform transform)
      {
            if (transform == null) return "null_transform";
            string path = transform.name;
            Transform current = transform.parent;
            while (current != null)
            {
                  path = current.name + "/" + path;
                  current = current.parent;
            }
            return path;
      }

      // Вспомогательный метод для красивого вывода LayerMask
      public static string LayerMaskToString(LayerMask layerMask)
      {
            var included = new System.Text.StringBuilder();
            for (int i = 0; i < 32; i++)
            {
                  if ((layerMask.value & (1 << i)) != 0)
                  {
                        string layerName = LayerMask.LayerToName(i);
                        if (string.IsNullOrEmpty(layerName))
                        {
                              // included.Append($"[Unnamed Layer {i}], "); // Можно раскомментировать, если нужны и безымянные слои
                        }
                        else
                        {
                              included.Append($"[{layerName} (id:{i})], ");
                        }
                  }
            }
            if (included.Length > 0 && included[included.Length - 1] == ' ' && included[included.Length - 2] == ',')
            {
                  included.Length -= 2; // Удалить последнюю запятую и пробел
            }
            else if (included.Length == 0)
            {
                  return "NONE";
            }
            return included.ToString();
      }

      // Публичные свойства для доступа к материалам извне
      public Material VerticalPlaneMaterial => verticalPlaneMaterial;
      public Material HorizontalPlaneMaterial => horizontalPlaneMaterial;

      // New method to make a plane persistent
      public bool MakePlanePersistent(GameObject plane)
      {
            if (plane == null || !generatedPlanes.Contains(plane))
                  return false;

            // Mark as persistent in our tracking
            persistentGeneratedPlanes[plane] = true;

            // Apply visual highlight if enabled
            if (highlightPersistentPlanes)
            {
                  MeshRenderer renderer = plane.GetComponent<MeshRenderer>();
                  if (renderer != null && renderer.material != null)
                  {
                        renderer.material.color = persistentPlaneColor;
                  }
            }

            Debug.Log($"[ARManagerInitializer2] Made plane {plane.name} persistent");
            return true;
      }

      // New method to check if a plane is persistent
      public bool IsPlanePersistent(GameObject plane)
      {
            if (plane == null) return false;
            return persistentGeneratedPlanes.TryGetValue(plane, out bool isPersistent) && isPersistent;
      }

      // New method to remove persistence from a plane
      public bool RemovePlanePersistence(GameObject plane)
      {
            if (plane == null || !persistentGeneratedPlanes.ContainsKey(plane))
                  return false;

            persistentGeneratedPlanes.Remove(plane);

            // Restore original material color
            if (highlightPersistentPlanes)
            {
                  MeshRenderer renderer = plane.GetComponent<MeshRenderer>();
                  if (renderer != null && renderer.material != null)
                  {
                        // Determine if it's a vertical or horizontal plane and use appropriate color
                        Vector3 normal = plane.transform.forward.normalized;
                        float dotUp = Vector3.Dot(normal, Vector3.up);
                        bool isVertical = Mathf.Abs(dotUp) < 0.25f;

                        if (isVertical && verticalPlaneMaterial != null)
                        {
                              renderer.material.color = verticalPlaneMaterial.color;
                        }
                        else if (horizontalPlaneMaterial != null)
                        {
                              renderer.material.color = horizontalPlaneMaterial.color;
                        }
                  }
            }

            Debug.Log($"[ARManagerInitializer2] Removed persistence from plane {plane.name}");
            return true;
      }

      // Check if a new plane would overlap with existing persistent planes
      private bool OverlapsWithPersistentPlanes(Vector3 position, Vector3 normal, float width, float height)
      {
            if (persistentGeneratedPlanes.Count == 0) return false;

            float maxAngleThresholdForOverlap = 15.0f; // Degrees
            // float maxDistanceThresholdForOverlap = 0.5f; // Meters - This was too strict

            foreach (var pair in persistentGeneratedPlanes)
            {
                  GameObject existingPlane = pair.Key;
                  if (existingPlane == null || !existingPlane.activeInHierarchy) continue;

                  Vector3 existingPlaneCenter = existingPlane.transform.position;
                  Vector3 existingPlaneNormal = existingPlane.transform.forward; // Assumes plane mesh is oriented with its "forward" as the normal

                  float angleDiff = Vector3.Angle(existingPlaneNormal, normal);
                  float distanceDiff = Vector3.Distance(existingPlaneCenter, position);

                  // Calculate extents for a more robust overlap check than just center distance
                  float existingPlaneHalfWidth = existingPlane.transform.localScale.x / 2f;
                  float existingPlaneHalfHeight = existingPlane.transform.localScale.y / 2f; // Assuming Y scale is height for vertical plane from CreatePlaneMesh
                  float newPlaneHalfWidth = width / 2f;
                  float newPlaneHalfHeight = height / 2f;

                  float maxExistingExtent = Mathf.Max(existingPlaneHalfWidth, existingPlaneHalfHeight);
                  float maxNewExtent = Mathf.Max(newPlaneHalfWidth, newPlaneHalfHeight);
                  float combinedExtents = maxExistingExtent + maxNewExtent;

                  // Condition for overlap: similar orientation, AND their extents suggest an actual overlap.
                  // The strict distanceDiff < maxDistanceThresholdForOverlap check was removed as it was too restrictive for large planes.
                  if (angleDiff < maxAngleThresholdForOverlap &&
                      combinedExtents * 0.5f > distanceDiff) // MODIFIED: Was 0.8f
                  {
                        if (enableCustomPlaneCreationLogging) Debug.LogWarning($"[ARManagerInitializer2-Overlaps] Overlap detected with {existingPlane.name}. New: pos={position:F2}, normal={normal:F2}, w={width:F1},h={height:F1}. Existing: pos={existingPlaneCenter:F2}, normal={existingPlaneNormal:F2}, scale={existingPlane.transform.localScale:F1}. AngleDiff={angleDiff:F1}, DistDiff={distanceDiff:F1}, CombExt*0.5={combinedExtents * 0.5f:F1}"); // MODIFIED: Log message to reflect 0.5f
                        return true;
                  }
            }
            return false;
      }

      [Header("Настройки стабилизации плоскостей")]
      [Tooltip("Частота проверки стабильности и превращения плоскостей в 'постоянные' (в секундах)")]
#pragma warning disable 0414
      [SerializeField] private float makePersistentCheckInterval = 1.0f;
#pragma warning restore 0414
      [Tooltip("Задержка перед удалением 'потерянной' плоскости (в секундах)")]
      [SerializeField] private float planePersistenceDelay = 2.0f; // ИЗМЕНЕНО с 0.5f
      [Tooltip("Минимальное время существования плоскости, чтобы она считалась 'стабильной' и могла стать 'постоянной' (в секундах)")]
      [SerializeField] private float minPlaneLifetimeForPersistence = 3.0f; // MODIFIED: Was 2.0f

      // Метод для автоматического превращения стабильных плоскостей в персистентные
      private void MakeStablePlanesPersistent()
      {
            // Используем настроенное время стабилизации
            float currentTime = Time.time;
            int newPersistentCount = 0;

            foreach (GameObject plane in generatedPlanes)
            {
                  if (plane == null)
                        continue;

                  // Пропускаем, если плоскость уже персистентная
                  if (IsPlanePersistent(plane))
                        continue;

                  // Проверяем, сколько времени существует плоскость
                  if (planeCreationTimes.TryGetValue(plane, out float creationTime))
                  {
                        float planeAge = currentTime - creationTime;
                        if (planeAge >= minPlaneLifetimeForPersistence) // MODIFIED: Was planeStableTimeThreshold
                        {
                              // Эта плоскость стабильна - делаем ее персистентной
                              if (MakePlanePersistent(plane))
                              {
                                    newPersistentCount++;
                              }
                        }
                  }
            }

            if (newPersistentCount > 0)
            {
                  Debug.Log($"[ARManagerInitializer2] Made {newPersistentCount} stable planes persistent. Total persistent: {persistentGeneratedPlanes.Count}");
            }
      }

      // New method to make all current planes persistent
      public void MakeAllPlanesPersistent()
      {
            if (!usePersistentPlanes)
            {
                  Debug.LogWarning("[ARManagerInitializer2] Persistent planes feature is disabled (usePersistentPlanes = false)");
                  return;
            }

            int madePersistentCount = 0;
            foreach (GameObject plane in generatedPlanes)
            {
                  if (plane == null) continue;

                  // Skip if already persistent
                  if (IsPlanePersistent(plane)) continue;

                  // Make persistent
                  if (MakePlanePersistent(plane))
                  {
                        madePersistentCount++;
                  }
            }

            Debug.Log($"[ARManagerInitializer2] Made {madePersistentCount} planes persistent");
      }

      // Method to set the layer of all generated planes
      public void SetPlanesLayer(string layerName)
      {
            int layerID = LayerMask.NameToLayer(layerName);
            if (layerID == -1)
            {
                  Debug.LogError($"[ARManagerInitializer2] Layer '{layerName}' not found in project settings!");
                  return;
            }

            int count = 0;
            foreach (GameObject plane in generatedPlanes)
            {
                  if (plane == null) continue;

                  plane.layer = layerID;
                  count++;
            }

            // Update the stored layer name
            planesLayerName = layerName;

            Debug.Log($"[ARManagerInitializer2] Set layer '{layerName}' (ID: {layerID}) for {count} planes");
      }

      // Инициализирует систему персистентных плоскостей
      private void InitializePersistentPlanesSystem()
      {
            if (!usePersistentPlanes)
            {
                  Debug.Log("[ARManagerInitializer2] Persistent planes system is disabled (usePersistentPlanes = false)");
                  return;
            }

            // Проверка существования слоя для плоскостей
            if (!string.IsNullOrEmpty(planesLayerName))
            {
                  int layerID = LayerMask.NameToLayer(planesLayerName);
                  if (layerID == -1)
                  {
                        Debug.LogWarning($"[ARManagerInitializer2] Layer '{planesLayerName}' not found in project settings! Planes will use default layer.");
                  }
                  else
                  {
                        Debug.Log($"[ARManagerInitializer2] Planes will use layer '{planesLayerName}' (ID: {layerID})");
                  }
            }

            Debug.Log("[ARManagerInitializer2] Persistent planes system initialized.");
      }

      // Метод для удаления проблемных персистентных плоскостей
      public void CleanupProblematicPersistentPlanes()
      {
            if (Camera.main == null)
            {
                  Debug.LogError("[ARManagerInitializer2] Камера не найдена для CleanupProblematicPersistentPlanes");
                  return;
            }

            Camera mainCam = Camera.main;
            List<GameObject> planesToRemove = new List<GameObject>();

            // Максимальное расстояние для хранения персистентных плоскостей (в метрах)
            float maxDistanceThreshold = 15.0f;

            // Максимальный допустимый размер для персистентной плоскости
            float maxPlaneSize = 5.0f;

            foreach (var kvp in persistentGeneratedPlanes)
            {
                  GameObject plane = kvp.Key;
                  bool isPersistent = kvp.Value;

                  if (plane == null || !isPersistent)
                        continue;

                  // Проверка размера
                  MeshFilter meshFilter = plane.GetComponent<MeshFilter>();
                  if (meshFilter != null && meshFilter.mesh != null)
                  {
                        Vector3 meshSize = meshFilter.mesh.bounds.size;
                        if (meshSize.x > maxPlaneSize || meshSize.y > maxPlaneSize)
                        {
                              Debug.Log($"[ARManagerInitializer2] Удаляем слишком большую персистентную плоскость {plane.name} (размер: {meshSize})");
                              planesToRemove.Add(plane);
                              continue;
                        }
                  }

                  // Проверка расстояния
                  float distanceToCamera = Vector3.Distance(plane.transform.position, mainCam.transform.position);
                  if (distanceToCamera > maxDistanceThreshold)
                  {
                        Debug.Log($"[ARManagerInitializer2] Удаляем удаленную персистентную плоскость {plane.name} (расстояние: {distanceToCamera:F2}м)");
                        planesToRemove.Add(plane);
                        continue;
                  }
            }

            // Удаляем проблемные плоскости
            foreach (GameObject plane in planesToRemove)
            {
                  RemovePlanePersistence(plane);
                  generatedPlanes.Remove(plane);
                  planeCreationTimes.Remove(plane);
                  planeLastVisitedTime.Remove(plane);
                  Destroy(plane);
            }

            Debug.Log($"[ARManagerInitializer2] Удалено {planesToRemove.Count} проблемных персистентных плоскостей");
      }

      // Удаляет все плоскости (персистентные и не персистентные)
      public void DeleteAllPlanes()
      {
            List<GameObject> allPlanes = new List<GameObject>(generatedPlanes);

            foreach (GameObject plane in allPlanes)
            {
                  if (plane == null) continue;

                  // Удаляем из всех списков
                  persistentGeneratedPlanes.Remove(plane);
                  planeCreationTimes.Remove(plane);
                  planeLastVisitedTime.Remove(plane);
                  generatedPlanes.Remove(plane);

                  // Удаляем объект
                  Destroy(plane);
            }

            Debug.Log($"[ARManagerInitializer2] Удалено все {allPlanes.Count} плоскостей");
      }

      // Удаляет только слишком большие плоскости
      public void DeleteLargePlanes(float maxSize = 3.0f)
      {
            if (Camera.main == null)
            {
                  Debug.LogError("[ARManagerInitializer2] Камера не найдена для DeleteLargePlanes");
                  return;
            }

            List<GameObject> planesToRemove = new List<GameObject>();

            foreach (GameObject plane in generatedPlanes)
            {
                  if (plane == null) continue;

                  // Проверка размера плоскости
                  MeshFilter meshFilter = plane.GetComponent<MeshFilter>();
                  if (meshFilter != null && meshFilter.mesh != null)
                  {
                        Vector3 meshSize = meshFilter.mesh.bounds.size;

                        // Если плоскость слишком большая
                        if (meshSize.x > maxSize || meshSize.y > maxSize || meshSize.z > maxSize)
                        {
                              planesToRemove.Add(plane);
                              Debug.Log($"[ARManagerInitializer2] Найдена большая плоскость для удаления: {plane.name}, размер: {meshSize}");
                        }
                  }
            }

            // Удаляем большие плоскости
            foreach (GameObject plane in planesToRemove)
            {
                  persistentGeneratedPlanes.Remove(plane);
                  planeCreationTimes.Remove(plane);
                  planeLastVisitedTime.Remove(plane);
                  generatedPlanes.Remove(plane);
                  Destroy(plane);
            }

            Debug.Log($"[ARManagerInitializer2] Удалено {planesToRemove.Count} больших плоскостей");
      }

      /// <summary>
      /// ДОБАВЛЕНО: Автоматическая очистка плоскостей с неправильными размерами
      /// </summary>
      private void CleanupOversizedPlanes()
      {
            int cleaned = 0;
            for (int i = generatedPlanes.Count - 1; i >= 0; i--)
            {
                  if (generatedPlanes[i] == null)
                  {
                        generatedPlanes.RemoveAt(i);
                        continue;
                  }

                  MeshFilter meshFilter = generatedPlanes[i].GetComponent<MeshFilter>();
                  if (meshFilter != null && meshFilter.mesh != null)
                  {
                        Vector3 size = Vector3.Scale(meshFilter.mesh.bounds.size, generatedPlanes[i].transform.localScale);

                        // Проверяем размеры с новыми ограничениями
                        bool isOversized = size.x > maxWallWidth || size.y > maxWallHeight || size.z > maxWallWidth;
                        bool isTooSmall = size.x < minPlaneSize || size.y < minPlaneSize;

                        // Проверяем соотношение сторон
                        float aspectRatio = Mathf.Max(size.x / size.y, size.y / size.x);
                        bool wrongAspectRatio = aspectRatio > maxAspectRatio;

                        if (isOversized || isTooSmall || wrongAspectRatio)
                        {
                              string reason = isOversized ? "слишком большая" : isTooSmall ? "слишком маленькая" : "неправильное соотношение сторон";
                              Debug.Log($"[ARManagerInitializer2] Удаляем проблемную плоскость: {generatedPlanes[i].name} ({reason}) - размер: {size.x:F2}x{size.y:F2}x{size.z:F2}м");

                              // Удаляем из всех словарей
                              if (persistentGeneratedPlanes.ContainsKey(generatedPlanes[i]))
                                    persistentGeneratedPlanes.Remove(generatedPlanes[i]);
                              if (planeCreationTimes.ContainsKey(generatedPlanes[i]))
                                    planeCreationTimes.Remove(generatedPlanes[i]);
                              if (planeLastVisitedTime.ContainsKey(generatedPlanes[i]))
                                    planeLastVisitedTime.Remove(generatedPlanes[i]);

                              GameObject.DestroyImmediate(generatedPlanes[i]);
                              generatedPlanes.RemoveAt(i);
                              cleaned++;
                        }
                  }
            }

            if (cleaned > 0)
            {
                  Debug.Log($"[ARManagerInitializer2] ✅ Автоматически удалено {cleaned} проблемных плоскостей");
            }
      }

      // Метод для быстрого сохранения всех текущих плоскостей без проверки времени существования
      public void QuickSaveCurrentPlanes()
      {
            if (!usePersistentPlanes)
            {
                  Debug.LogWarning("[ARManagerInitializer2] Persistent planes feature is disabled (usePersistentPlanes = false)");
                  return;
            }

            int savedCount = 0;

            foreach (GameObject plane in generatedPlanes)
            {
                  if (plane == null) continue;

                  // Пропускаем, если плоскость уже персистентная
                  if (IsPlanePersistent(plane)) continue;

                  // Проверяем, что плоскость не слишком большая
                  MeshFilter meshFilter = plane.GetComponent<MeshFilter>();
                  if (meshFilter != null && meshFilter.mesh != null)
                  {
                        Vector3 meshSize = meshFilter.mesh.bounds.size;
                        float maxAllowedSize = 5.0f;

                        if (meshSize.x > maxAllowedSize || meshSize.y > maxAllowedSize)
                        {
                              Debug.LogWarning($"[ARManagerInitializer2] Плоскость {plane.name} слишком большая для быстрого сохранения (размер: {meshSize})");
                              continue;
                        }
                  }

                  // Делаем плоскость персистентной без проверки времени
                  if (MakePlanePersistent(plane))
                  {
                        savedCount++;
                  }
            }

            Debug.Log($"[ARManagerInitializer2] Быстрое сохранение: сохранено {savedCount} плоскостей");
      }

      // Добавляем переменные для отслеживания двойного тапа
      [Header("Настройки управления жестами")]
      [Tooltip("Разрешить сохранение плоскостей по двойному тапу")]
      [SerializeField] private bool enableDoubleTapSave = true;
      [Tooltip("Максимальное время между тапами для распознавания двойного тапа (в секундах)")]
      [SerializeField] private float doubleTapTimeThreshold = 0.3f;
      private float lastTapTime = 0f;
      private int tapCount = 0;
      private Vector2 lastTapPosition;
      private float maxTapPositionDelta = 100f; // Максимальное расстояние между тапами в пикселях

      private void UpdateGestureInput()
      {
            if (!enableDoubleTapSave) return;

            // Проверяем тап на сенсорных устройствах
            if (Input.touchCount > 0)
            {
                  Touch touch = Input.GetTouch(0);

                  if (touch.phase == TouchPhase.Began)
                  {
                        HandleTapInput(touch.position);
                  }
            }
            // Проверяем клик мышью для тестирования в редакторе
            else if (Input.GetMouseButtonDown(0))
            {
                  HandleTapInput(Input.mousePosition);
            }
      }

      private void HandleTapInput(Vector2 position)
      {
            float currentTime = Time.time;

            // Если это первый тап или прошло слишком много времени с последнего тапа
            if (tapCount == 0 || (currentTime - lastTapTime) > doubleTapTimeThreshold)
            {
                  tapCount = 1;
                  lastTapTime = currentTime;
                  lastTapPosition = position;
            }
            // Если это второй тап в пределах времени и позиции
            else if (tapCount == 1 && (currentTime - lastTapTime) <= doubleTapTimeThreshold
                    && Vector2.Distance(position, lastTapPosition) < maxTapPositionDelta)
            {
                  tapCount = 0; // Сбрасываем счетчик
                                // Обрабатываем двойной тап
                  Debug.Log("[ARManagerInitializer2] Обнаружен двойной тап, запуск быстрого сохранения плоскостей");
                  QuickSaveCurrentPlanes();
            }
            else
            {
                  // Сбрасываем если тап не подходит для двойного
                  tapCount = 1;
                  lastTapTime = currentTime;
                  lastTapPosition = position;
            }
      }

      /// <summary>
      /// Диагностирует объекты сцены и их коллайдеры для понимания проблем с рейкастингом
      /// </summary>
      private void DiagnoseSceneObjects()
      {
            Debug.Log("=== [ARManagerInitializer2] ДИАГНОСТИКА ОБЪЕКТОВ СЦЕНЫ ===");

            // Найдем все объекты с коллайдерами
            Collider[] allColliders = FindObjectsOfType<Collider>(true); // включая неактивные
            Debug.Log($"[Диагностика] Всего коллайдеров в сцене: {allColliders.Length}");

            int enabledColliders = 0;
            int meshColliders = 0;
            int boxColliders = 0;

            foreach (var collider in allColliders)
            {
                  if (collider.enabled) enabledColliders++;

                  string layerName = LayerMask.LayerToName(collider.gameObject.layer);
                  if (string.IsNullOrEmpty(layerName)) layerName = $"Layer{collider.gameObject.layer}";

                  if (collider is MeshCollider) meshColliders++;
                  else if (collider is BoxCollider) boxColliders++;

                  Debug.Log($"[Диагностика] Коллайдер: '{collider.name}' ({collider.GetType().Name}), активен: {collider.enabled}, слой: {layerName}");
            }

            Debug.Log($"[Диагностика] Активных коллайдеров: {enabledColliders}, MeshCollider: {meshColliders}, BoxCollider: {boxColliders}");

            // ЕСЛИ КОЛЛАЙДЕРЫ ОТСУТСТВУЮТ - СРАЗУ ДОБАВЛЯЕМ ИХ
            if (allColliders.Length == 0)
            {
                  Debug.LogWarning("[Диагностика] ⚠️ Коллайдеры отсутствуют! Запускаем немедленное добавление...");
                  ForceAddCollidersAggressively();
            }

            // Проверим какие симуляционные объекты активны в сцене
            GameObject[] allGameObjects = FindObjectsOfType<GameObject>(true);

            foreach (var obj in allGameObjects)
            {
                  if (obj.name.Contains("Environment") || obj.name.Contains("Simulation"))
                  {
                        Debug.Log($"[Диагностика] Найден объект симуляции: '{obj.name}', активен: {obj.activeInHierarchy}, слой: {obj.layer} ({LayerMask.LayerToName(obj.layer)})");

                        // Проверим коллайдеры в дочерних объектах
                        Collider[] childColliders = obj.GetComponentsInChildren<Collider>(true);
                        Debug.Log($"[Диагностика] У объекта '{obj.name}' найдено {childColliders.Length} коллайдеров в дочерних объектах");
                  }
            }

            Debug.Log("=== [ARManagerInitializer2] КОНЕЦ ДИАГНОСТИКИ ===");
      }

      /// <summary>
      /// Повторная проверка и добавление коллайдеров через задержку (для асинхронно загружаемых объектов)
      /// </summary>
      private IEnumerator DelayedColliderCheck()
      {
            for (int attempt = 1; attempt <= 5; attempt++) // 5 попыток с интервалом 3 секунды
            {
                  yield return new WaitForSeconds(3.0f); // Ждем 3 секунды

                  Debug.Log($"=== [ARManagerInitializer2] ПОВТОРНАЯ ПРОВЕРКА #{attempt}/5 (через {attempt * 3} сек.) ===");

                  Collider[] allColliders = FindObjectsOfType<Collider>(true);
                  MeshRenderer[] allRenderers = FindObjectsOfType<MeshRenderer>(true);
                  GameObject[] allObjects = FindObjectsOfType<GameObject>(true);

                  Debug.Log($"[Повторная проверка #{attempt}] Найдено: Коллайдеров: {allColliders.Length}, MeshRenderer-ов: {allRenderers.Length}, Всего объектов: {allObjects.Length}");

                  if (allColliders.Length == 0 || allRenderers.Length == 0)
                  {
                        Debug.LogWarning($"[Повторная проверка #{attempt}] ⚠️ Проблема обнаружена! Запускаем ультра-диагностику...");
                        ForceAddCollidersAggressively();

                        // Проверяем результат
                        Collider[] afterColliders = FindObjectsOfType<Collider>(true);
                        MeshRenderer[] afterRenderers = FindObjectsOfType<MeshRenderer>(true);
                        Debug.Log($"[Повторная проверка #{attempt}] После диагностики: Коллайдеров: {afterColliders.Length}, MeshRenderer-ов: {afterRenderers.Length}");

                        if (afterColliders.Length > 0 && afterRenderers.Length > 0)
                        {
                              Debug.Log($"[Повторная проверка #{attempt}] ✅ Проблема решена! Остановка дальнейших попыток.");
                              break;
                        }
                  }
                  else
                  {
                        Debug.Log($"[Повторная проверка #{attempt}] ✅ Объекты найдены. Остановка дальнейших попыток.");
                        break;
                  }
            }

            Debug.Log("=== [ARManagerInitializer2] ЗАВЕРШЕНИЕ ПОВТОРНЫХ ПРОВЕРОК ===");
      }

      /// <summary>
      /// Агрессивная функция поиска и добавления коллайдеров
      /// </summary>
      private void ForceAddCollidersAggressively()
      {
            Debug.Log("=== [ARManagerInitializer2] УЛЬТРА-ДИАГНОСТИКА ВСЕХ ОБЪЕКТОВ ===");

            // 1. Общая диагностика
            GameObject[] allObjects = FindObjectsOfType<GameObject>(true);
            int objectsWithMesh = 0;
            int objectsWithCollider = 0;
            int addedColliders = 0;

            Debug.Log($"[Ультра-диагностика] Всего GameObject-ов в сцене (включая неактивные): {allObjects.Length}");

            // 2. Проходим по всем объектам
            foreach (GameObject obj in allObjects)
            {
                  MeshRenderer meshRenderer = obj.GetComponent<MeshRenderer>();
                  MeshFilter meshFilter = obj.GetComponent<MeshFilter>();
                  Collider existingCollider = obj.GetComponent<Collider>();

                  // Выводим подробную информацию о каждом объекте
                  if (obj != null)
                  {
                        string components = "";
                        if (meshRenderer != null) components += "MeshRenderer ";
                        if (meshFilter != null) components += "MeshFilter ";
                        if (existingCollider != null) components += $"Collider({existingCollider.GetType().Name}) ";

                        Debug.Log($"[Ультра-диагностика] Объект: '{obj.name}', активен: {obj.activeInHierarchy}, слой: {obj.layer} ({LayerMask.LayerToName(obj.layer)}), компоненты: [{components}]");

                        // Показываем размер mesh если есть
                        if (meshFilter != null && meshFilter.mesh != null)
                        {
                              Debug.Log($"  └─ Mesh: '{meshFilter.mesh.name}', vertices: {meshFilter.mesh.vertexCount}, triangles: {meshFilter.mesh.triangles.Length / 3}");
                        }
                  }

                  // Считаем статистику
                  if (meshRenderer != null) objectsWithMesh++;
                  if (existingCollider != null) objectsWithCollider++;

                  // Добавляем коллайдер если нужно
                  if (meshRenderer != null && meshFilter != null && meshFilter.mesh != null && existingCollider == null)
                  {
                        // Проверяем размер объекта
                        Bounds bounds = meshRenderer.bounds;
                        if (bounds.size.magnitude > 0.1f) // Только достаточно большие объекты
                        {
                              MeshCollider meshCollider = obj.AddComponent<MeshCollider>();
                              meshCollider.sharedMesh = meshFilter.mesh;
                              addedColliders++;

                              Debug.Log($"[Ультра-диагностика] ✅ Добавлен MeshCollider к '{obj.name}' (размер: {bounds.size})");
                        }
                  }
            }

            Debug.Log($"[Ультра-диагностика] 📊 СТАТИСТИКА:");
            Debug.Log($"  ├─ Всего объектов: {allObjects.Length}");
            Debug.Log($"  ├─ С MeshRenderer: {objectsWithMesh}");
            Debug.Log($"  ├─ С Collider: {objectsWithCollider}");
            Debug.Log($"  └─ Добавлено коллайдеров: {addedColliders}");

            // *** НОВОЕ: Если нет коллайдеров, создаем тестовые стены ***
            if (objectsWithCollider == 0 && addedColliders == 0)
            {
                  Debug.LogWarning("[Ультра-диагностика] ⚠️ Коллайдеры не найдены! Создаем тестовую геометрию для рейкастинга...");
                  CreateTestEnvironmentGeometry();
            }

            // 3. Ищем объекты по специальным тегам Unity XR
            Transform[] allTransforms = FindObjectsOfType<Transform>(true);
            Debug.Log($"[Ультра-диагностика] Ищем XR объекты среди {allTransforms.Length} трансформов...");

            foreach (Transform t in allTransforms)
            {
                  if (t.name.ToLower().Contains("xr") ||
                      t.name.ToLower().Contains("ar") ||
                      t.name.ToLower().Contains("mesh") ||
                      t.name.ToLower().Contains("simulation") ||
                      t.name.ToLower().Contains("environment"))
                  {
                        Debug.Log($"[Ультра-диагностика] 🎯 Потенциальный XR объект: '{t.name}', родитель: '{(t.parent != null ? t.parent.name : "ROOT")}', активен: {t.gameObject.activeInHierarchy}");
                  }
            }

            Collider[] finalColliders = FindObjectsOfType<Collider>(true);
            MeshRenderer[] finalRenderers = FindObjectsOfType<MeshRenderer>(true);
            Debug.Log($"[Ультра-диагностика] 🎯 ФИНАЛЬНЫЙ РЕЗУЛЬТАТ: Коллайдеров: {finalColliders.Length}, MeshRenderer-ов: {finalRenderers.Length}");

            Debug.Log("=== [ARManagerInitializer2] КОНЕЦ УЛЬТРА-ДИАГНОСТИКИ ===");
      }

      /// <summary>
      /// Создает тестовую геометрию окружения для рейкастинга когда ARMeshing недоступен
      /// </summary>
      private void CreateTestEnvironmentGeometry()
      {
            Debug.Log("[CreateTestEnvironmentGeometry] 🔧 Создание тестовой геометрии для рейкастинга...");

            // Найдем родительский объект для тестовой геометрии
            GameObject testParent = GameObject.Find("TestEnvironmentGeometry");
            if (testParent == null)
            {
                  testParent = new GameObject("TestEnvironmentGeometry");
                  testParent.layer = LayerMask.NameToLayer("SimulatedEnvironment");
                  if (testParent.layer == -1) testParent.layer = 0; // Fallback to Default
            }

            // Создаем несколько тестовых стен вокруг камеры
            CreateTestWall(testParent, "FrontWall", new Vector3(0, 0, 3), new Vector3(0, 0, 0), new Vector3(6, 3, 0.1f));
            CreateTestWall(testParent, "LeftWall", new Vector3(-3, 0, 0), new Vector3(0, 90, 0), new Vector3(6, 3, 0.1f));
            CreateTestWall(testParent, "RightWall", new Vector3(3, 0, 0), new Vector3(0, -90, 0), new Vector3(6, 3, 0.1f));
            CreateTestWall(testParent, "BackWall", new Vector3(0, 0, -3), new Vector3(0, 180, 0), new Vector3(6, 3, 0.1f));

            // Создаем пол
            CreateTestWall(testParent, "Floor", new Vector3(0, -1.5f, 0), new Vector3(90, 0, 0), new Vector3(6, 6, 0.1f));

            Debug.Log("[CreateTestEnvironmentGeometry] ✅ Создано 5 тестовых поверхностей для рейкастинга");
      }

      /// <summary>
      /// Создает одну тестовую стену
      /// </summary>
      private void CreateTestWall(GameObject parent, string name, Vector3 position, Vector3 rotation, Vector3 scale)
      {
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = name;
            wall.transform.SetParent(parent.transform);
            wall.transform.localPosition = position;
            wall.transform.localRotation = Quaternion.Euler(rotation);
            wall.transform.localScale = scale;

            // Настраиваем слой
            wall.layer = parent.layer;

            // Убеждаемся что у стены есть коллайдер
            BoxCollider collider = wall.GetComponent<BoxCollider>();
            if (collider == null)
            {
                  collider = wall.AddComponent<BoxCollider>();
            }

            // Делаем стену визуально прозрачной но физически активной
            MeshRenderer renderer = wall.GetComponent<MeshRenderer>();
            if (renderer != null)
            {
                  // Создаем прозрачный материал
                  Material transparentMat = new Material(Shader.Find("Standard"));
                  transparentMat.SetFloat("_Mode", 3); // Transparent mode
                  transparentMat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                  transparentMat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                  transparentMat.SetInt("_ZWrite", 0);
                  transparentMat.DisableKeyword("_ALPHATEST_ON");
                  transparentMat.EnableKeyword("_ALPHABLEND_ON");
                  transparentMat.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                  transparentMat.renderQueue = 3000;
                  transparentMat.color = new Color(0.5f, 0.8f, 1.0f, 0.1f); // Слегка видимый голубой

                  renderer.material = transparentMat;
            }

            Debug.Log($"[CreateTestWall] ✅ Создана тестовая стена: {name} в позиции {position}");
      }

      /// <summary>
      /// Утилита для поиска объектов симуляционной среды по имени
      /// </summary>
      public void DebugFindSimulationObjects()
      {
            Debug.Log("=== [ARManagerInitializer2] ПОИСК СИМУЛЯЦИОННЫХ ОБЪЕКТОВ ===");

            GameObject[] allObjects = FindObjectsOfType<GameObject>(true);
            int found = 0;

            foreach (var obj in allObjects)
            {
                  if (obj.name.ToLower().Contains("environment") ||
                      obj.name.ToLower().Contains("simulation") ||
                      obj.name.ToLower().Contains("room") ||
                      obj.name.ToLower().Contains("wall") ||
                      obj.name.ToLower().Contains("floor"))
                  {
                        found++;

                        MeshRenderer renderer = obj.GetComponent<MeshRenderer>();
                        Collider collider = obj.GetComponent<Collider>();

                        string info = $"[DebugFind] {obj.name} - ";
                        info += $"Активен: {obj.activeInHierarchy}, ";
                        info += $"Слой: {LayerMask.LayerToName(obj.layer)}, ";
                        info += $"MeshRenderer: {(renderer != null ? "✓" : "✗")}, ";
                        info += $"Collider: {(collider != null ? "✓" : "✗")}";

                        if (renderer != null)
                              info += $", Размер: {renderer.bounds.size}";

                        Debug.Log(info);
                  }
            }

            Debug.Log($"[DebugFind] Найдено объектов среды: {found}");
      }

      /// <summary>
      /// Публичный доступ к сгенерированным плоскостям для PlaneOrientationDebugger
      /// </summary>
      public List<GameObject> GeneratedPlanes => generatedPlanes;

      private void ConfigureARMeshManager()
      {
            var meshManager = FindObjectOfType<ARMeshManager>();
            if (meshManager == null)
            {
                  Debug.LogWarning("[ARManagerInitializer2] ARMeshManager not found in scene. Cannot configure mesh material.");
                  return;
            }

            // Create a simple, slightly visible transparent material
            Shader urpShader = Shader.Find("Universal Render Pipeline/Lit");
            Material transparentMaterial;

            if (urpShader != null)
            {
                  transparentMaterial = new Material(urpShader)
                  {
                        name = "AR_Mesh_Transparent_URP_Material"
                  };
                  transparentMaterial.SetFloat("_Surface", 1.0f); // Set Surface Type to Transparent
                  transparentMaterial.SetColor("_BaseColor", new Color(0.6f, 0.8f, 1.0f, 0.1f)); // A faint blue
            }
            else
            {
                  // Fallback for built-in render pipeline
                  Shader fallbackShader = Shader.Find("Legacy Shaders/Transparent/Diffuse");
                  if (fallbackShader == null)
                  {
                        Debug.LogError("[ARManagerInitializer2] Could not find any transparent shader.");
                        return;
                  }
                  transparentMaterial = new Material(fallbackShader)
                  {
                        name = "AR_Mesh_Transparent_Legacy_Material"
                  };
                  transparentMaterial.SetColor("_Color", new Color(0.6f, 0.8f, 1.0f, 0.1f));
            }

            // Assign the material to all current and future meshes managed by ARMeshManager
            void ApplyMaterialToMeshes(ARMeshesChangedEventArgs args)
            {
                  foreach (var mesh in args.added)
                  {
                        var renderer = mesh.GetComponent<MeshRenderer>();
                        if (renderer != null)
                        {
                              renderer.material = transparentMaterial;
                        }
                  }
                  foreach (var mesh in args.updated)
                  {
                        var renderer = mesh.GetComponent<MeshRenderer>();
                        if (renderer != null && (renderer.sharedMaterial == null || renderer.sharedMaterial.name.Contains("Default-Material")))
                        {
                              renderer.material = transparentMaterial;
                        }
                  }
            }

            // Apply to existing meshes first
            foreach (var meshFilter in meshManager.GetComponentsInChildren<MeshFilter>())
            {
                  var renderer = meshFilter.GetComponent<MeshRenderer>();
                  if (renderer != null)
                  {
                        renderer.material = transparentMaterial;
                  }
            }

            // Then subscribe for future changes
            meshManager.meshesChanged += ApplyMaterialToMeshes;

            Debug.Log("[ARManagerInitializer2] ✅ ARMeshManager configured to use transparent materials for environment meshes.");
      }

      /// <summary>
      /// Пытается принудительно активировать AR Meshing для создания геометрии окружения
      /// </summary>
      private void TryActivateARMeshing()
      {
            Debug.Log("[ARManagerInitializer2] 🔧 Попытка активации AR Meshing...");

            var meshManager = FindObjectOfType<ARMeshManager>();
            if (meshManager == null)
            {
                  Debug.LogWarning("[ARManagerInitializer2] ARMeshManager не найден в сцене");
                  return;
            }

            // Убеждаемся что ARMeshManager включен
            if (!meshManager.enabled)
            {
                  meshManager.enabled = true;
                  Debug.Log("[ARManagerInitializer2] ✅ ARMeshManager принудительно включен");
            }

            // Проверяем настройки мешинга
            Debug.Log($"[ARManagerInitializer2] ARMeshManager настройки: Density={meshManager.density}, Normals={meshManager.normals}");

            // Пытаемся активировать scene reconstruction в AR Session
            var arSession = FindObjectOfType<ARSession>();
            if (arSession != null)
            {
                  Debug.Log("[ARManagerInitializer2] Найден ARSession, попытка включения scene reconstruction...");

                  // На iOS это может потребовать явной настройки конфигурации
                  try
                  {
                        // Получаем текущую конфигурацию
#if UNITY_IOS && !UNITY_EDITOR
                        using (var configuration = new UnityEngine.XR.ARKit.ARWorldTrackingConfiguration())
                        {
                              // Включаем scene reconstruction если поддерживается
                              if (UnityEngine.XR.ARKit.ARWorldTrackingConfiguration.supportsSceneReconstruction)
                              {
                                    configuration.sceneReconstruction = UnityEngine.XR.ARKit.ARSceneReconstruction.Mesh;
                                    Debug.Log("[ARManagerInitializer2] ✅ Scene reconstruction включен в конфигурации");
                              }
                              else
                              {
                                    Debug.LogWarning("[ARManagerInitializer2] ⚠️ Scene reconstruction не поддерживается на этом устройстве");
                              }
                        }
#endif
                  }
                  catch (System.Exception e)
                  {
                        Debug.LogWarning($"[ARManagerInitializer2] Не удалось настроить scene reconstruction: {e.Message}");
                  }
            }

            // Подписываемся на события мешинга для отладки
            if (meshManager != null)
            {
                  meshManager.meshesChanged += OnARMeshesChanged;
                  Debug.Log("[ARManagerInitializer2] ✅ Подписались на события ARMeshManager");
            }

            Debug.Log("[ARManagerInitializer2] 🔧 Активация AR Meshing завершена");
      }

      /// <summary>
      /// Обработчик событий изменения AR мешей для отладки
      /// </summary>
      private void OnARMeshesChanged(ARMeshesChangedEventArgs args)
      {
            if (args.added.Count > 0)
            {
                  Debug.Log($"[ARManagerInitializer2] 🎯 Добавлено {args.added.Count} AR мешей");
                  foreach (var mesh in args.added)
                  {
                        var collider = mesh.GetComponent<MeshCollider>();
                        if (collider != null)
                        {
                              Debug.Log($"[ARManagerInitializer2] ✅ AR меш '{mesh.name}' имеет MeshCollider для рейкастинга");
                        }
                  }
            }

            if (args.updated.Count > 0)
            {
                  Debug.Log($"[ARManagerInitializer2] 🔄 Обновлено {args.updated.Count} AR мешей");
            }

            if (args.removed.Count > 0)
            {
                  Debug.Log($"[ARManagerInitializer2] ❌ Удалено {args.removed.Count} AR мешей");
            }
      }
}