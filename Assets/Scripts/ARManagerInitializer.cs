using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Unity.XR.CoreUtils;
using System.Collections;

/// <summary>
/// Класс для автоматического добавления ARSessionManager в сцену при старте игры
/// </summary>
[DefaultExecutionOrder(-10)] // Выполняется раньше других скриптов
public class ARManagerInitializer : MonoBehaviour
{
      // Синглтон для глобального доступа
      public static ARManagerInitializer Instance { get; private set; }

      // Ссылка на созданный менеджер
      public ARSessionManager SessionManager { get; private set; }

      // Метод выполняется перед стартом приложения
      [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
      private static void Initialize()
      {
            // Создаем GameObject с этим компонентом, который переживет перезагрузку сцены
            GameObject initializer = new GameObject("AR Manager Initializer");
            initializer.AddComponent<ARManagerInitializer>();

            // Объект не уничтожится при перезагрузке сцены
            DontDestroyOnLoad(initializer);
      }

      private void Awake()
      {
            // Реализация синглтона
            if (Instance != null && Instance != this)
            {
                  Destroy(gameObject);
                  return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            // Запускаем инициализацию с задержкой
            StartCoroutine(SetupARComponents());
      }

      private void Start()
      {
            // Подписываемся на события обновления маски сегментации
            SubscribeToWallSegmentation();
      }

      private IEnumerator SetupARComponents()
      {
            // Ждем один кадр
            yield return null;

            // Проверяем наличие существующего ARSessionManager
            SessionManager = FindObjectOfType<ARSessionManager>();

            if (SessionManager == null)
            {
                  Debug.Log("ARSessionManager не найден. Создаем новый...");

                  // Проверяем наличие ARSession в сцене
                  ARSession existingSession = FindObjectOfType<ARSession>();
                  GameObject arSessionObj;

                  if (existingSession == null)
                  {
                        // Создаем новый ARSession
                        arSessionObj = new GameObject("AR Session");
                        arSessionObj.AddComponent<ARSession>();
                        arSessionObj.AddComponent<ARInputManager>();
                        Debug.Log("Создан новый ARSession");
                  }
                  else
                  {
                        arSessionObj = existingSession.gameObject;
                  }

                  // Добавляем ARSessionManager к объекту AR Session
                  SessionManager = arSessionObj.AddComponent<ARSessionManager>();

                  // Находим XROrigin в сцене
                  XROrigin existingXROrigin = FindObjectOfType<XROrigin>();

                  // Если XROrigin не найден или у него нет Camera, создаем новый
                  if (existingXROrigin == null || existingXROrigin.Camera == null)
                  {
                        Debug.Log("XROrigin не найден или настроен некорректно. Создаем новый...");

                        // Если есть некорректный XROrigin, отключаем его
                        if (existingXROrigin != null)
                        {
                              existingXROrigin.gameObject.SetActive(false);
                              Debug.LogWarning("Найден некорректный XROrigin. Отключен.");
                        }

                        // Создаем новый XROrigin с правильной структурой
                        CreateARStructure();
                  }
                  else
                  {
                        Debug.Log("Найден существующий XROrigin. Проверка и настройка...");

                        // Проверяем и исправляем существующий XROrigin
                        FixExistingXROrigin(existingXROrigin);
                  }
            }
            else
            {
                  // Проверяем ссылки в ARSessionManager
                  if (SessionManager.xrOrigin == null || SessionManager.arCameraManager == null)
                  {
                        Debug.LogWarning("ARSessionManager существует, но ссылки на камеру или XROrigin отсутствуют. Исправление...");

                        // Ищем XROrigin в сцене
                        XROrigin existingXROrigin = FindObjectOfType<XROrigin>();
                        if (existingXROrigin != null && existingXROrigin.Camera != null)
                        {
                              // Исправляем существующий XROrigin
                              FixExistingXROrigin(existingXROrigin);
                        }
                        else
                        {
                              // Создаем новый XROrigin
                              CreateARStructure();
                        }
                  }
            }

            // Проверяем финальную настройку XROrigin перед запуском сессии
            VerifyXROriginSetup();

            Debug.Log("ARManagerInitializer настроен успешно");

            // Принудительно запускаем сессию
            if (SessionManager != null)
            {
                  SessionManager.StartSession();
            }

            // Настройка ARPlaneManager для автоматического назначения материалов
            SetupARPlaneManager();
      }

      // Метод для создания корректной структуры AR компонентов
      private void CreateARStructure()
      {
            Debug.Log("Создаем новую структуру AR компонентов...");

            // Создаем основной объект XROrigin
            GameObject xrOriginObj = new GameObject("XR Origin");
            XROrigin xrOrigin = xrOriginObj.AddComponent<XROrigin>();

            // Создаем объект для смещения камеры
            GameObject cameraOffsetObj = new GameObject("Camera Offset");
            cameraOffsetObj.transform.SetParent(xrOriginObj.transform, false);
            cameraOffsetObj.transform.localPosition = Vector3.zero;

            // Создаем камеру
            GameObject arCameraObj = new GameObject("AR Camera");
            arCameraObj.transform.SetParent(cameraOffsetObj.transform, false);
            arCameraObj.transform.localPosition = Vector3.zero;
            arCameraObj.transform.localRotation = Quaternion.identity;

            // Добавляем компонент Camera
            Camera arCamera = arCameraObj.AddComponent<Camera>();
            arCamera.clearFlags = CameraClearFlags.SolidColor;
            arCamera.backgroundColor = Color.black;
            arCamera.nearClipPlane = 0.1f;
            arCamera.farClipPlane = 20f;
            arCamera.tag = "MainCamera";

            // Добавляем AR компоненты к камере
            ARCameraManager cameraManager = arCameraObj.AddComponent<ARCameraManager>();
            cameraManager.enabled = true;

            ARCameraBackground cameraBackground = arCameraObj.AddComponent<ARCameraBackground>();
            cameraBackground.useCustomMaterial = false;
            cameraBackground.enabled = true;

            // Добавляем TrackedPoseDriver с Input System
            var trackedPoseDriver = arCameraObj.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
            trackedPoseDriver.enabled = true;

            // Настраиваем ссылки в XROrigin
            xrOrigin.Camera = arCamera;
            xrOrigin.CameraFloorOffsetObject = cameraOffsetObj;

            // Проверка, что ссылки на Camera и CameraFloorOffsetObject установлены правильно
            Debug.Log($"XROrigin.Camera установлена: {(xrOrigin.Camera != null ? xrOrigin.Camera.name : "null")}");
            Debug.Log($"XROrigin.CameraFloorOffsetObject установлен: {(xrOrigin.CameraFloorOffsetObject != null ? xrOrigin.CameraFloorOffsetObject.name : "null")}");

            // Убеждаемся, что xrOrigin не имеет исходного родителя
            if (xrOrigin.transform.parent != null)
            {
                  Debug.LogWarning("XROrigin имеет родителя. Отсоединяем для правильной работы AR.");
                  xrOrigin.transform.SetParent(null, true);
            }

            // Создаем трекеры для AR
            GameObject trackersObj = new GameObject("AR Trackers");
            trackersObj.transform.SetParent(xrOriginObj.transform, false);

            // Добавляем менеджеры для AR
            trackersObj.AddComponent<ARRaycastManager>();
            trackersObj.AddComponent<ARPlaneManager>();
            trackersObj.AddComponent<ARPointCloudManager>();
            trackersObj.AddComponent<ARAnchorManager>();

            // Обновляем ссылки в SessionManager
            if (SessionManager != null)
            {
                  SessionManager.xrOrigin = xrOrigin;
                  SessionManager.arCameraManager = cameraManager;
                  SessionManager.arCameraBackground = cameraBackground;

                  Debug.Log("Ссылки AR компонентов настроены успешно");
            }
            else
            {
                  Debug.LogError("SessionManager отсутствует. Ссылки не настроены.");
            }

            // Принудительно включаем все компоненты
            xrOrigin.enabled = true;
            cameraManager.enabled = true;
            cameraBackground.enabled = true;

            // Проверяем, не отключен ли объект XROrigin
            if (!xrOrigin.gameObject.activeInHierarchy)
            {
                  Debug.LogWarning("XROrigin неактивен. Активируем объект.");
                  xrOrigin.gameObject.SetActive(true);
            }

            Debug.Log("Структура AR компонентов создана успешно");
      }

      // Метод для исправления существующего XROrigin
      private void FixExistingXROrigin(XROrigin xrOrigin)
      {
            if (xrOrigin == null) return;

            Debug.Log("Исправление существующего XROrigin...");

            // Проверяем наличие Camera Offset
            Transform cameraOffset = null;
            if (xrOrigin.CameraFloorOffsetObject == null)
            {
                  // Ищем Camera Offset
                  cameraOffset = xrOrigin.transform.Find("Camera Offset");
                  if (cameraOffset == null)
                  {
                        // Создаем Camera Offset
                        GameObject offsetObj = new GameObject("Camera Offset");
                        offsetObj.transform.SetParent(xrOrigin.transform, false);
                        offsetObj.transform.localPosition = Vector3.zero;
                        cameraOffset = offsetObj.transform;

                        // Устанавливаем ссылку
                        xrOrigin.CameraFloorOffsetObject = offsetObj;
                        Debug.Log("Создан новый Camera Offset");
                  }
                  else
                  {
                        xrOrigin.CameraFloorOffsetObject = cameraOffset.gameObject;
                        Debug.Log("Найден и установлен Camera Offset");
                  }
            }
            else
            {
                  cameraOffset = xrOrigin.CameraFloorOffsetObject.transform;
            }

            // Проверяем наличие камеры
            Camera arCamera = xrOrigin.Camera;
            GameObject arCameraObj = null;

            // Проверяем, есть ли камера вообще в XROrigin
            if (arCamera == null)
            {
                  // Сначала ищем AR Camera в Camera Offset
                  Transform arCameraTrans = cameraOffset.Find("AR Camera");

                  if (arCameraTrans == null)
                  {
                        // Если камеры нет в Camera Offset, ищем по всему XROrigin
                        arCameraTrans = xrOrigin.transform.GetComponentInChildren<Camera>()?.transform;
                  }

                  if (arCameraTrans == null)
                  {
                        // Создаем новую AR Camera, так как в иерархии ее нет
                        arCameraObj = new GameObject("AR Camera");
                        arCameraObj.transform.SetParent(cameraOffset, false);
                        arCameraObj.transform.localPosition = Vector3.zero;
                        arCameraObj.transform.localRotation = Quaternion.identity;

                        // Добавляем компоненты
                        arCamera = arCameraObj.AddComponent<Camera>();
                        arCamera.clearFlags = CameraClearFlags.SolidColor;
                        arCamera.backgroundColor = Color.black;
                        arCamera.nearClipPlane = 0.1f;
                        arCamera.farClipPlane = 20f;
                        arCamera.tag = "MainCamera";

                        ARCameraManager newCameraManager = arCameraObj.AddComponent<ARCameraManager>();
                        newCameraManager.enabled = true;

                        ARCameraBackground newCameraBackground = arCameraObj.AddComponent<ARCameraBackground>();
                        newCameraBackground.useCustomMaterial = false;
                        newCameraBackground.enabled = true;

                        // Добавляем TrackedPoseDriver с Input System
                        var newTrackedPoseDriver = arCameraObj.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
                        newTrackedPoseDriver.enabled = true;

                        Debug.Log("Создана новая AR камера в Camera Offset");
                  }
                  else
                  {
                        // Если нашли существующую камеру, но она не в Camera Offset - переносим ее
                        if (arCameraTrans.parent != cameraOffset)
                        {
                              Debug.Log("Найдена камера, но она не в Camera Offset. Перемещаем...");
                              arCameraTrans.SetParent(cameraOffset, false);
                              arCameraTrans.localPosition = Vector3.zero;
                              arCameraTrans.localRotation = Quaternion.identity;
                        }

                        arCameraObj = arCameraTrans.gameObject;
                        arCamera = arCameraTrans.GetComponent<Camera>();

                        if (arCamera == null)
                        {
                              arCamera = arCameraObj.AddComponent<Camera>();
                              arCamera.clearFlags = CameraClearFlags.SolidColor;
                              arCamera.backgroundColor = Color.black;
                              arCamera.nearClipPlane = 0.1f;
                              arCamera.farClipPlane = 20f;
                              arCamera.tag = "MainCamera";
                              Debug.Log("Добавлен компонент Camera к существующему объекту AR Camera");
                        }
                  }

                  // Убеждаемся, что ссылка на камеру в XROrigin установлена
                  xrOrigin.Camera = arCamera;
                  Debug.Log("Установлена ссылка на камеру в XROrigin: " + (arCamera != null ? arCamera.name : "null"));
            }
            else
            {
                  // Камера существует в XROrigin, проверяем правильное родительское представление
                  arCameraObj = arCamera.gameObject;
                  if (arCameraObj.transform.parent != cameraOffset)
                  {
                        Debug.Log("Камера найдена, но не в Camera Offset. Перемещаем...");
                        arCameraObj.transform.SetParent(cameraOffset, false);
                        arCameraObj.transform.localPosition = Vector3.zero;
                        arCameraObj.transform.localRotation = Quaternion.identity;
                  }
            }

            // Проверяем, что у нас есть ссылка на объект камеры
            if (arCameraObj == null && arCamera != null)
            {
                  arCameraObj = arCamera.gameObject;
            }

            // Проверяем наличие ARCameraManager и ARCameraBackground
            if (arCameraObj != null)
            {
                  ARCameraManager cameraManager = arCameraObj.GetComponent<ARCameraManager>();
                  if (cameraManager == null)
                  {
                        cameraManager = arCameraObj.AddComponent<ARCameraManager>();
                        cameraManager.enabled = true;
                        Debug.Log("Добавлен компонент ARCameraManager");
                  }

                  ARCameraBackground cameraBackground = arCameraObj.GetComponent<ARCameraBackground>();
                  if (cameraBackground == null)
                  {
                        cameraBackground = arCameraObj.AddComponent<ARCameraBackground>();
                        cameraBackground.useCustomMaterial = false;
                        cameraBackground.enabled = true;
                        Debug.Log("Добавлен компонент ARCameraBackground");
                  }

                  // Проверяем наличие TrackedPoseDriver
                  var trackedPoseDriver = arCameraObj.GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
                  if (trackedPoseDriver == null)
                  {
                        trackedPoseDriver = arCameraObj.AddComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
                        trackedPoseDriver.enabled = true;
                        Debug.Log("Добавлен компонент TrackedPoseDriver");
                  }

                  // Обновляем ссылки в SessionManager
                  if (SessionManager != null)
                  {
                        SessionManager.xrOrigin = xrOrigin;
                        SessionManager.arCameraManager = cameraManager;
                        SessionManager.arCameraBackground = cameraBackground;
                        Debug.Log("Ссылки AR компонентов настроены успешно");
                  }
            }
            else
            {
                  Debug.LogError("Критическая ошибка: не удалось создать или найти AR камеру");
            }

            // Финальная проверка, что камера установлена в XROrigin
            if (xrOrigin.Camera == null)
            {
                  Debug.LogError("После всех исправлений XROrigin.Camera все еще null!");
            }
            else
            {
                  Debug.Log("XROrigin.Camera успешно установлена: " + xrOrigin.Camera.name);
            }

            Debug.Log("XROrigin исправлен успешно");
      }

      // Метод для финальной проверки и коррекции настройки XROrigin
      private void VerifyXROriginSetup()
      {
            XROrigin xrOrigin = null;

            // Проверяем, есть ли ссылка на XROrigin в нашем SessionManager
            if (SessionManager != null && SessionManager.xrOrigin != null)
            {
                  xrOrigin = SessionManager.xrOrigin;
                  Debug.Log("Проверка XROrigin из SessionManager");
            }
            else
            {
                  // Если нет, ищем в сцене
                  xrOrigin = FindObjectOfType<XROrigin>();
                  if (xrOrigin != null)
                  {
                        Debug.Log("Найден XROrigin в сцене для финальной проверки");

                        // Обновляем ссылку в SessionManager, если он существует
                        if (SessionManager != null)
                        {
                              SessionManager.xrOrigin = xrOrigin;
                        }
                  }
                  else
                  {
                        Debug.LogError("XROrigin не найден для финальной проверки. AR может работать некорректно.");
                        return;
                  }
            }

            // Проверяем критические компоненты
            if (xrOrigin.Camera == null)
            {
                  Debug.LogError("XROrigin.Camera не установлена. Попытка найти подходящую камеру в иерархии...");

                  // Пытаемся найти камеру в дочерних объектах XROrigin
                  Camera childCamera = xrOrigin.GetComponentInChildren<Camera>();
                  if (childCamera != null)
                  {
                        xrOrigin.Camera = childCamera;
                        Debug.Log("Найдена и установлена камера в XROrigin: " + childCamera.name);
                  }
                  else
                  {
                        Debug.LogError("Не удалось найти камеру в иерархии XROrigin. AR не будет работать корректно.");
                  }
            }

            // НОВАЯ ПРОВЕРКА: проверяем, не указан ли AR Trackers как CameraFloorOffsetObject
            if (xrOrigin.CameraFloorOffsetObject != null &&
                xrOrigin.CameraFloorOffsetObject.name == "AR Trackers")
            {
                  Debug.LogWarning("Обнаружена некорректная настройка: AR Trackers указан как CameraFloorOffsetObject. Исправляем...");

                  // Ищем или создаем правильный Camera Offset
                  Transform cameraOffset = xrOrigin.transform.Find("Camera Offset");
                  if (cameraOffset == null)
                  {
                        // Создаем новый Camera Offset
                        GameObject offsetObj = new GameObject("Camera Offset");
                        offsetObj.transform.SetParent(xrOrigin.transform, false);
                        offsetObj.transform.localPosition = Vector3.zero;
                        cameraOffset = offsetObj.transform;
                        Debug.Log("Создан новый Camera Offset");
                  }

                  // Если камера существует, но находится не в Camera Offset
                  if (xrOrigin.Camera != null)
                  {
                        // Если камера не находится в Camera Offset, перемещаем её туда
                        if (xrOrigin.Camera.transform.parent != cameraOffset)
                        {
                              Debug.Log("Перемещаем камеру в правильный Camera Offset");
                              xrOrigin.Camera.transform.SetParent(cameraOffset, false);
                              xrOrigin.Camera.transform.localPosition = Vector3.zero;
                              xrOrigin.Camera.transform.localRotation = Quaternion.identity;
                        }
                  }

                  // Устанавливаем правильную ссылку на Camera Offset
                  xrOrigin.CameraFloorOffsetObject = cameraOffset.gameObject;
                  Debug.Log("Исправлена ссылка на CameraFloorOffsetObject с AR Trackers на Camera Offset");
            }
            // Стандартная проверка на отсутствие CameraFloorOffsetObject
            else if (xrOrigin.CameraFloorOffsetObject == null)
            {
                  Debug.LogError("XROrigin.CameraFloorOffsetObject не установлен. Попытка найти или создать...");

                  // Ищем Camera Offset
                  Transform cameraOffset = xrOrigin.transform.Find("Camera Offset");
                  if (cameraOffset != null)
                  {
                        xrOrigin.CameraFloorOffsetObject = cameraOffset.gameObject;
                        Debug.Log("Найден и установлен Camera Offset в XROrigin");
                  }
                  else if (xrOrigin.Camera != null)
                  {
                        // Создаем новый Camera Offset и перемещаем камеру под него
                        GameObject offsetObj = new GameObject("Camera Offset");
                        offsetObj.transform.SetParent(xrOrigin.transform, false);
                        offsetObj.transform.localPosition = Vector3.zero;

                        // Перемещаем камеру под Camera Offset
                        xrOrigin.Camera.transform.SetParent(offsetObj.transform, true);

                        // Устанавливаем ссылку
                        xrOrigin.CameraFloorOffsetObject = offsetObj;
                        Debug.Log("Создан новый Camera Offset и перемещена камера");
                  }
            }

            // Проверяем AR компоненты на камере
            if (xrOrigin.Camera != null)
            {
                  GameObject cameraObj = xrOrigin.Camera.gameObject;

                  ARCameraManager cameraManager = cameraObj.GetComponent<ARCameraManager>();
                  if (cameraManager == null)
                  {
                        cameraManager = cameraObj.AddComponent<ARCameraManager>();
                        cameraManager.enabled = true;
                        Debug.Log("Добавлен компонент ARCameraManager при финальной проверке");
                  }

                  ARCameraBackground cameraBackground = cameraObj.GetComponent<ARCameraBackground>();
                  if (cameraBackground == null)
                  {
                        cameraBackground = cameraObj.AddComponent<ARCameraBackground>();
                        cameraBackground.useCustomMaterial = false;
                        cameraBackground.enabled = true;
                        Debug.Log("Добавлен компонент ARCameraBackground при финальной проверке");
                  }

                  // Обновляем ссылки в SessionManager
                  if (SessionManager != null)
                  {
                        SessionManager.arCameraManager = cameraManager;
                        SessionManager.arCameraBackground = cameraBackground;
                  }
            }

            // Выводим информацию о текущей конфигурации
            Debug.Log($"Финальная проверка XROrigin завершена: " +
                    $"Camera: {(xrOrigin.Camera != null ? xrOrigin.Camera.name : "null")}, " +
                    $"CameraFloorOffsetObject: {(xrOrigin.CameraFloorOffsetObject != null ? xrOrigin.CameraFloorOffsetObject.name : "null")}");
      }

      // Метод для настройки ARPlaneManager
      private void SetupARPlaneManager()
      {
            ARPlaneManager planeManager = FindObjectOfType<ARPlaneManager>();
            if (planeManager == null)
            {
                  Debug.LogWarning("ARPlaneManager не найден, невозможно настроить материалы для плоскостей");
                  return;
            }

            // Логируем настройки ARPlaneManager
            Debug.Log($"[ARManagerInitializer] 🔍 Конфигурация ARPlaneManager:" +
                      $"\n - requestedDetectionMode: {planeManager.requestedDetectionMode}" +
                      $"\n - planeFinding: {(planeManager.enabled ? "включено" : "выключено")}" +
                      $"\n - planePrefab: {(planeManager.planePrefab != null ? planeManager.planePrefab.name : "не установлен")}" +
                      $"\n - trackables: {planeManager.trackables.count} активных плоскостей");

            // Проверяем, установлен ли префаб для ARPlane
            if (planeManager.planePrefab == null)
            {
                  Debug.LogWarning("[ARManagerInitializer] ⚠️ planePrefab не установлен в ARPlaneManager. Плоскости не будут визуализироваться!");
            }

            // Подписываемся на событие создания плоскостей
            planeManager.planesChanged -= OnPlanesChanged; // Сначала отписываемся, чтобы избежать двойной подписки
            planeManager.planesChanged += OnPlanesChanged;

            Debug.Log("[ARManagerInitializer] ✅ ARPlaneManager настроен, подписка на события создания плоскостей активирована");
      }

      // Обработчик события изменения плоскостей
      private void OnPlanesChanged(ARPlanesChangedEventArgs args)
      {
            Debug.Log($"[ARManagerInitializer] 🔄 Изменение плоскостей: добавлено {args.added.Count}, обновлено {args.updated.Count}, удалено {args.removed.Count}");
            
            // Обработка только добавленных плоскостей
            foreach (ARPlane plane in args.added)
            {
                  Debug.Log($"[ARManagerInitializer] ➕ Добавлена новая плоскость:" +
                           $"\n - ID: {plane.trackableId}" +
                           $"\n - Тип: {plane.classification}" +
                           $"\n - Ориентация: {plane.alignment}" +
                           $"\n - Размер: {plane.size.x:F2}x{plane.size.y:F2}м" +
                           $"\n - Центр: {plane.center}");
                  
                  SetupPlaneVisualization(plane.gameObject);
            }
            
            // Также логируем информацию об обновленных плоскостях
            foreach (ARPlane plane in args.updated)
            {
                  Debug.Log($"[ARManagerInitializer] 🔄 Обновлена плоскость ID: {plane.trackableId}, размер: {plane.size.x:F2}x{plane.size.y:F2}м");
                  
                  // Обновляем маску сегментации для вертикальных плоскостей при их обновлении
                  if (plane.alignment == UnityEngine.XR.ARSubsystems.PlaneAlignment.Vertical)
                  {
                        WallSegmentation wallSegmentation = FindObjectOfType<WallSegmentation>();
                        if (wallSegmentation != null && wallSegmentation.segmentationMaskTexture != null)
                        {
                              MeshRenderer meshRenderer = plane.GetComponent<MeshRenderer>();
                              if (meshRenderer != null && meshRenderer.material != null)
                              {
                                    meshRenderer.material.SetTexture("_SegmentationMask", wallSegmentation.segmentationMaskTexture);
                                    meshRenderer.material.EnableKeyword("USE_MASK");
                              }
                        }
                  }
            }
      }

      // Настройка визуализации для одной плоскости
      private void SetupPlaneVisualization(GameObject planeObject)
      {
            if (planeObject == null) return;

            Debug.Log($"[ARManagerInitializer] 🎨 Настройка визуализации для плоскости {planeObject.name}");

            // Проверяем является ли плоскость вертикальной
            ARPlane planeComponent = planeObject.GetComponent<ARPlane>();
            bool isVerticalPlane = false;

            if (planeComponent != null)
            {
                  isVerticalPlane = planeComponent.alignment == UnityEngine.XR.ARSubsystems.PlaneAlignment.Vertical;
            }

            // Находим WallSegmentation для получения маски сегментации
            WallSegmentation wallSegmentation = FindObjectOfType<WallSegmentation>();
            RenderTexture segmentationMask = wallSegmentation?.segmentationMaskTexture;

            // Настройка MeshRenderer
            MeshRenderer meshRenderer = planeObject.GetComponent<MeshRenderer>();
            if (meshRenderer != null)
            {
                  if (meshRenderer.sharedMaterial == null)
                  {
                        // Создаем дефолтный материал с прозрачностью
                        Material defaultMaterial = new Material(Shader.Find("Custom/WallPaint"));
                        if (defaultMaterial != null)
                        {
                              // Для вертикальных плоскостей используем WallMaterial
                              if (isVerticalPlane)
                              {
                                    defaultMaterial.name = "WallMaterial";
                                    defaultMaterial.color = new Color(0.4f, 0.6f, 1.0f, 0.5f); // Голубой цвет с прозрачностью
                                    
                                    // Применяем маску сегментации к материалу, если она доступна
                                    if (segmentationMask != null)
                                    {
                                          defaultMaterial.SetTexture("_SegmentationMask", segmentationMask);
                                          defaultMaterial.EnableKeyword("USE_MASK");
                                          Debug.Log($"[ARManagerInitializer] ✅ Маска сегментации стен применена к материалу плоскости {planeObject.name}");
                                    }
                              }
                              else
                              {
                                    // Для горизонтальных плоскостей используем стандартный материал
                                    defaultMaterial.color = new Color(0.0f, 0.8f, 0.2f, 0.5f); // Зеленый цвет с прозрачностью
                              }
                              
                              meshRenderer.sharedMaterial = defaultMaterial;
                              Debug.Log($"[ARManagerInitializer] ✅ Материал для MeshRenderer назначен для {planeObject.name}");
                        }
                  }
                  else
                  {
                        Debug.Log($"[ARManagerInitializer] ℹ️ Плоскость {planeObject.name} уже имеет материал: {meshRenderer.sharedMaterial.name}");
                        
                        // Если плоскость вертикальная и уже имеет материал, применяем маску сегментации
                        if (isVerticalPlane && segmentationMask != null && meshRenderer.sharedMaterial.name.Contains("Wall"))
                        {
                              meshRenderer.sharedMaterial.SetTexture("_SegmentationMask", segmentationMask);
                              meshRenderer.sharedMaterial.EnableKeyword("USE_MASK");
                              Debug.Log($"[ARManagerInitializer] ✅ Маска сегментации стен применена к существующему материалу плоскости {planeObject.name}");
                        }
                  }
            }
            else
            {
                  Debug.LogWarning($"[ARManagerInitializer] ⚠️ Плоскость {planeObject.name} не содержит MeshRenderer!");
            }

            // Настройка LineRenderer
            LineRenderer lineRenderer = planeObject.GetComponent<LineRenderer>();
            if (lineRenderer != null)
            {
                  if (lineRenderer.sharedMaterial == null)
                  {
                        // Создаем дефолтный материал для линий
                        Material lineMaterial = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
                        if (lineMaterial != null)
                        {
                              lineMaterial.color = Color.white;
                              lineRenderer.sharedMaterial = lineMaterial;
                              Debug.Log($"[ARManagerInitializer] ✅ Материал для LineRenderer назначен для {planeObject.name}");
                        }
                  }
            }
            
            // Проверяем ARPlane компонент
            if (planeComponent != null)
            {
                  Debug.Log($"[ARManagerInitializer] ℹ️ Конфигурация ARPlane:" +
                           $"\n - GameObject: {planeComponent.gameObject.name}" +
                           $"\n - Classification: {planeComponent.classification}" + 
                           $"\n - Normal: {planeComponent.normal}" +
                           $"\n - Vertices: {planeComponent.boundary.Length} вершин");
            }
      }

      // Подписка на событие обновления маски сегментации
      private void SubscribeToWallSegmentation()
      {
            // Находим WallSegmentation в сцене
            WallSegmentation wallSegmentation = FindObjectOfType<WallSegmentation>();
            if (wallSegmentation != null)
            {
                  // Отписываемся от предыдущего события, если оно было
                  wallSegmentation.OnSegmentationMaskUpdated -= OnSegmentationMaskUpdated;
                  
                  // Подписываемся на событие обновления маски
                  wallSegmentation.OnSegmentationMaskUpdated += OnSegmentationMaskUpdated;
                  
                  Debug.Log("[ARManagerInitializer] ✅ Подписка на события обновления маски сегментации настроена");
            }
            else
            {
                  Debug.LogWarning("[ARManagerInitializer] ⚠️ WallSegmentation не найден в сцене. Подписка на события невозможна.");
                  
                  // Если WallSegmentation не найден, запустим поиск повторно через некоторое время
                  StartCoroutine(RetrySubscription());
            }
      }
      
      // Корутина для повторной попытки подписки
      private IEnumerator RetrySubscription()
      {
            yield return new WaitForSeconds(2f);
            Debug.Log("[ARManagerInitializer] 🔄 Повторная попытка подписки на WallSegmentation");
            SubscribeToWallSegmentation();
      }
      
      // Обработчик события обновления маски сегментации
      private void OnSegmentationMaskUpdated(RenderTexture mask)
      {
            if (mask == null)
            {
                  Debug.LogWarning("[ARManagerInitializer-OnSegmentationMaskUpdated] ⚠️ Получена null маска сегментации");
                  return;
            }

            Debug.Log($"[ARManagerInitializer-OnSegmentationMaskUpdated] ✅ Получена маска сегментации {mask.width}x{mask.height}");
            
            // Получаем все активные плоскости и обновляем материалы
            ARPlaneManager planeManager = FindObjectOfType<ARPlaneManager>();
            if (planeManager != null)
            {
                  int planeCount = 0;
                  int updatedPlaneCount = 0;
                  
                  // Обновляем материалы только для вертикальных плоскостей
                  foreach (ARPlane plane in planeManager.trackables)
                  {
                        planeCount++;
                        if (plane.alignment == UnityEngine.XR.ARSubsystems.PlaneAlignment.Vertical)
                        {
                              MeshRenderer meshRenderer = plane.GetComponent<MeshRenderer>();
                              if (meshRenderer != null && meshRenderer.material != null)
                              {
                                    meshRenderer.material.SetTexture("_SegmentationMask", mask);
                                    meshRenderer.material.EnableKeyword("USE_MASK");
                                    updatedPlaneCount++;
                                    
                                    // Выводим детальную информацию об обновлении
                                    Debug.Log($"[ARManagerInitializer-OnSegmentationMaskUpdated] ✓ Обновлена плоскость {plane.trackableId} с размером {plane.size.x:F2}x{plane.size.y:F2}м");
                              }
                              else
                              {
                                    Debug.LogWarning($"[ARManagerInitializer-OnSegmentationMaskUpdated] ⚠️ Не удалось обновить материал для плоскости {plane.trackableId} - MeshRenderer или Material отсутствуют");
                              }
                        }
                  }
                  
                  Debug.Log($"[ARManagerInitializer-OnSegmentationMaskUpdated] 📊 Статистика обновления: обновлено {updatedPlaneCount} из {planeCount} плоскостей");
            }
            else
            {
                  Debug.LogWarning("[ARManagerInitializer-OnSegmentationMaskUpdated] ⚠️ ARPlaneManager не найден, невозможно обновить материалы плоскостей");
            }
      }
}