using UnityEngine;
using UnityEditor;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
using System.Linq;
using Unity.XR.CoreUtils;
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using InputSystemTrackedPoseDriver = UnityEngine.InputSystem.XR.TrackedPoseDriver;
#endif
using UnityEngine.SpatialTracking;
using SpatialTrackedPoseDriver = UnityEngine.SpatialTracking.TrackedPoseDriver;

/// <summary>
/// Редактор для быстрой настройки AR сцены с правильной иерархией.
/// </summary>
public class ARSceneSetup : EditorWindow
{
    [MenuItem("AR Tools/Настроить AR Сцену")]
    public static void CreateARScene()
    {
        // 1. Создаем AR Session
        GameObject arSession = new GameObject("AR Session");
        arSession.AddComponent<ARSession>();
        
        // 2. Создаем XR Origin
        GameObject xrOrigin = new GameObject("XR Origin");
        XROrigin originComponent = xrOrigin.AddComponent<XROrigin>();
        
        // 3. Создаем AR Camera
        GameObject arCamera = new GameObject("AR Camera");
        Camera cameraComponent = arCamera.AddComponent<Camera>();
        arCamera.AddComponent<ARCameraManager>();
        arCamera.AddComponent<ARCameraBackground>();
        
        // Добавляем компонент для отслеживания позиции устройства
        #if ENABLE_INPUT_SYSTEM
        // Для новых версий Unity используем TrackedPoseDriver из пакета Input System
        if (HasInputSystemPackage())
        {
            arCamera.AddComponent<InputSystemTrackedPoseDriver>();
            Debug.Log("Добавлен TrackedPoseDriver из Input System");
        }
        else
        {
            // Используем TrackedPoseDriver из пакета SpatialTracking для обратной совместимости
            arCamera.AddComponent<SpatialTrackedPoseDriver>();
            Debug.Log("Добавлен TrackedPoseDriver из SpatialTracking");
        }
        #else
        // Для старых версий Unity
        arCamera.AddComponent<SpatialTrackedPoseDriver>();
        #endif
        
        // Устанавливаем тег "MainCamera" и настраиваем камеру
        arCamera.tag = "MainCamera";
        cameraComponent.clearFlags = CameraClearFlags.SolidColor;
        cameraComponent.backgroundColor = Color.black;
        cameraComponent.nearClipPlane = 0.1f;
        cameraComponent.farClipPlane = 30f;
        
        // Делаем камеру дочерним объектом XROrigin
        arCamera.transform.SetParent(xrOrigin.transform);
        
        // Создаем Camera Offset как родителя для камеры
        GameObject cameraOffset = new GameObject("Camera Offset");
        cameraOffset.transform.SetParent(xrOrigin.transform);
        cameraOffset.transform.localPosition = Vector3.zero;
        cameraOffset.transform.localRotation = Quaternion.identity;
        
        // Перемещаем камеру под Camera Offset
        arCamera.transform.SetParent(cameraOffset.transform);
        arCamera.transform.localPosition = Vector3.zero;
        arCamera.transform.localRotation = Quaternion.identity;
        
        // Настраиваем XROrigin
        originComponent.Camera = cameraComponent;
        originComponent.CameraFloorOffsetObject = cameraOffset;
        
        // 4. Добавляем AR Plane Manager к XR Origin
        ARPlaneManager planeManager = xrOrigin.AddComponent<ARPlaneManager>();
        planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
        
        // 5. Добавляем AR Anchor Manager
        ARAnchorManager anchorManager = xrOrigin.AddComponent<ARAnchorManager>();
        
        // 6. Добавляем AR Raycast Manager
        xrOrigin.AddComponent<ARRaycastManager>();
        
        // 7. Создаем префаб для плоскостей, если его еще нет
        CreateDefaultPlanePrefab(planeManager);
        
        Debug.Log("AR сцена настроена успешно!");
    }
    
    private static void CreateDefaultPlanePrefab(ARPlaneManager planeManager)
    {
        // Проверяем, существует ли предопределенный префаб ARPlaneVisualization
        string planePrefabPath = "Assets/Prefabs/ARPlaneVisualization.prefab";
        GameObject existingPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(planePrefabPath);
        
        if (existingPrefab != null)
        {
            planeManager.planePrefab = existingPrefab;
            Debug.Log("Используется существующий префаб плоскости: " + planePrefabPath);
            return;
        }
        
        // Создаем новый префаб плоскости
        GameObject planePrefab = new GameObject("AR Plane Visualization");
        planePrefab.AddComponent<ARPlaneMeshVisualizer>();
        MeshFilter meshFilter = planePrefab.AddComponent<MeshFilter>();
        MeshRenderer meshRenderer = planePrefab.AddComponent<MeshRenderer>();
        
        // Создаем материал для визуализации плоскостей
        Material planeMaterial = new Material(Shader.Find("Unlit/Transparent"));
        planeMaterial.color = new Color(1f, 1f, 1f, 0.3f);
        meshRenderer.material = planeMaterial;
        
        // Создаем директорию для префаба, если её не существует
        if (!AssetDatabase.IsValidFolder("Assets/Prefabs"))
        {
            AssetDatabase.CreateFolder("Assets", "Prefabs");
        }
        
        // Сохраняем префаб
        PrefabUtility.SaveAsPrefabAsset(planePrefab, planePrefabPath);
        GameObject.DestroyImmediate(planePrefab);
        
        // Назначаем префаб для AR Plane Manager
        planeManager.planePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(planePrefabPath);
        Debug.Log("Создан новый префаб плоскости: " + planePrefabPath);
    }
    
    [MenuItem("AR Tools/Исправить существующую AR сцену")]
    public static void FixExistingARScene()
    {
        // Пытаемся найти сначала современный XROrigin, если нет - ищем устаревший ARSessionOrigin
        XROrigin xrOrigin = FindObjectOfType<XROrigin>();
        
        // Если нет XROrigin, проверяем наличие устаревшего ARSessionOrigin и конвертируем его
        if (xrOrigin == null)
        {
            // Используем #pragma warning disable для подавления предупреждений об устаревших типах
            #pragma warning disable 0618
            ARSessionOrigin oldOrigin = FindObjectOfType<ARSessionOrigin>();
            #pragma warning restore 0618
            
            if (oldOrigin != null)
            {
                // Преобразуем ARSessionOrigin в XROrigin
                xrOrigin = ConvertARSessionOriginToXROrigin(oldOrigin);
            }
            else
            {
                Debug.LogError("Ни XROrigin, ни ARSessionOrigin не найдены в сцене! Сначала создайте AR сцену.");
                return;
            }
        }
        
        // Находим ARSession в сцене
        ARSession session = FindObjectOfType<ARSession>();
        if (session == null)
        {
            GameObject arSessionObj = new GameObject("AR Session");
            arSessionObj.AddComponent<ARSession>();
            Debug.Log("AR Session добавлен в сцену.");
        }
        
        // Проверяем, что камера настроена правильно
        Camera arCamera = xrOrigin.Camera;
        if (arCamera == null)
        {
            Debug.LogError("AR камера не настроена в XROrigin! Проверьте настройки камеры.");
            return;
        }
        
        // Проверяем наличие Camera Offset
        if (xrOrigin.CameraFloorOffsetObject == null)
        {
            GameObject cameraOffset = new GameObject("Camera Offset");
            cameraOffset.transform.SetParent(xrOrigin.transform);
            cameraOffset.transform.localPosition = Vector3.zero;
            cameraOffset.transform.localRotation = Quaternion.identity;
            
            // Перемещаем камеру под Camera Offset
            arCamera.transform.SetParent(cameraOffset.transform);
            arCamera.transform.localPosition = Vector3.zero;
            arCamera.transform.localRotation = Quaternion.identity;
            
            xrOrigin.CameraFloorOffsetObject = cameraOffset;
            Debug.Log("Camera Offset создан и настроен.");
        }
        
        // Проверяем наличие необходимых компонентов на камере
        GameObject cameraObj = arCamera.gameObject;
        if (cameraObj != null)
        {
            if (cameraObj.GetComponent<ARCameraManager>() == null)
                cameraObj.AddComponent<ARCameraManager>();
                
            if (cameraObj.GetComponent<ARCameraBackground>() == null)
                cameraObj.AddComponent<ARCameraBackground>();
                
            // Проверяем и добавляем TrackedPoseDriver
            bool hasPoseDriver = false;
            
            #if ENABLE_INPUT_SYSTEM
            // Проверяем новый TrackedPoseDriver из Input System
            if (cameraObj.GetComponent<InputSystemTrackedPoseDriver>() != null)
            {
                hasPoseDriver = true;
                Debug.Log("Найден TrackedPoseDriver из Input System");
            }
            #endif
            
            // Проверяем старый TrackedPoseDriver
            if (!hasPoseDriver && cameraObj.GetComponent<SpatialTrackedPoseDriver>() == null)
            {
                cameraObj.AddComponent<SpatialTrackedPoseDriver>();
                Debug.Log("Добавлен компонент TrackedPoseDriver из SpatialTracking");
            }
            else if (!hasPoseDriver)
            {
                hasPoseDriver = true;
                Debug.Log("Найден TrackedPoseDriver из SpatialTracking");
            }
        }
        
        // Добавляем или находим ARPlaneManager
        ARPlaneManager planeManager = xrOrigin.GetComponent<ARPlaneManager>();
        if (planeManager == null)
        {
            planeManager = xrOrigin.gameObject.AddComponent<ARPlaneManager>();
            planeManager.requestedDetectionMode = PlaneDetectionMode.Horizontal | PlaneDetectionMode.Vertical;
            CreateDefaultPlanePrefab(planeManager);
        }
        else if (planeManager.planePrefab == null)
        {
            CreateDefaultPlanePrefab(planeManager);
        }
        
        // Добавляем ARAnchorManager, если отсутствует
        if (xrOrigin.GetComponent<ARAnchorManager>() == null)
        {
            xrOrigin.gameObject.AddComponent<ARAnchorManager>();
        }
        
        // Добавляем ARRaycastManager, если отсутствует
        if (xrOrigin.GetComponent<ARRaycastManager>() == null)
        {
            xrOrigin.gameObject.AddComponent<ARRaycastManager>();
        }
        
        Debug.Log("Существующая AR сцена успешно исправлена!");
    }
    
    /// <summary>
    /// Конвертация устаревшего ARSessionOrigin в современный XROrigin
    /// </summary>
    private static XROrigin ConvertARSessionOriginToXROrigin(
        #pragma warning disable 0618
        ARSessionOrigin oldOrigin
        #pragma warning restore 0618
    )
    {
        // Получаем GameObject
        GameObject originObject = oldOrigin.gameObject;
        
        // Удаляем устаревший компонент
        DestroyImmediate(oldOrigin);
        
        // Добавляем новый компонент
        XROrigin newOrigin = originObject.AddComponent<XROrigin>();
        
        // Переименовываем объект для ясности
        originObject.name = "XR Origin";
        
        // Настраиваем Camera
        Camera arCamera = null;
        
        // Ищем камеру среди дочерних объектов
        foreach (Transform child in originObject.transform)
        {
            Camera camera = child.GetComponent<Camera>();
            if (camera != null)
            {
                arCamera = camera;
                break;
            }
        }
        
        if (arCamera != null)
        {
            // Создаем Camera Offset
            GameObject cameraOffset = new GameObject("Camera Offset");
            cameraOffset.transform.SetParent(originObject.transform);
            cameraOffset.transform.localPosition = Vector3.zero;
            cameraOffset.transform.localRotation = Quaternion.identity;
            
            // Перемещаем камеру под Camera Offset
            arCamera.transform.SetParent(cameraOffset.transform);
            arCamera.transform.localPosition = Vector3.zero;
            arCamera.transform.localRotation = Quaternion.identity;
            
            // Настраиваем XROrigin
            newOrigin.Camera = arCamera;
            newOrigin.CameraFloorOffsetObject = cameraOffset;
        }
        
        Debug.Log("ARSessionOrigin успешно конвертирован в XROrigin!");
        return newOrigin;
    }
    
    /// <summary>
    /// Проверяет, установлен ли Input System Package
    /// </summary>
    private static bool HasInputSystemPackage()
    {
        #if ENABLE_INPUT_SYSTEM
        return true;
        #else
        return false;
        #endif
    }
} 