using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Unity.XR.CoreUtils;

/// <summary>
/// Класс для управления AR сессией и обеспечения правильной последовательности инициализации компонентов
/// </summary>
public class ARSessionManager : MonoBehaviour
{
      [SerializeField] public ARSession arSession;
      [SerializeField] public ARCameraManager arCameraManager;
      [SerializeField] public ARCameraBackground arCameraBackground;
      [SerializeField] public XROrigin xrOrigin;

      [SerializeField] private bool autoStartSession = true;
      [SerializeField] private float initializationDelay = 0.5f; // Задержка для iOS

      private bool isSessionInitialized = false;

      private void Awake()
      {
            // Автоматически найти компоненты, если они не заданы
            if (arSession == null)
            {
                  arSession = FindObjectOfType<ARSession>();
            }

            if (xrOrigin == null)
            {
                  xrOrigin = FindObjectOfType<XROrigin>();
            }

            if (xrOrigin != null && arCameraManager == null)
            {
                  // Получаем камеру из XROrigin
                  Camera arCamera = xrOrigin.Camera;
                  if (arCamera != null)
                  {
                        arCameraManager = arCamera.GetComponent<ARCameraManager>();
                        arCameraBackground = arCamera.GetComponent<ARCameraBackground>();
                  }
            }

            // Валидация компонентов
            if (arSession == null)
            {
                  Debug.LogError("ARSession не найден. Добавьте AR Session объект в сцену.");
            }

            if (xrOrigin == null)
            {
                  Debug.LogError("XROrigin не найден. Добавьте XR Origin объект в сцену.");
            }

            if (arCameraManager == null)
            {
                  Debug.LogWarning("ARCameraManager не найден. AR камера может работать неправильно.");
            }
      }

      private void Start()
      {
            if (autoStartSession)
            {
                  // Запускаем инициализацию с задержкой
                  StartCoroutine(InitializeARSession());
            }
      }

      private IEnumerator InitializeARSession()
      {
            // Ждем один кадр
            yield return null;

            // Убедимся, что все компоненты AR включены
            if (arSession != null && !arSession.enabled)
            {
                  arSession.enabled = true;
                  Debug.Log("AR Session включен");
            }

            if (arCameraManager != null && !arCameraManager.enabled)
            {
                  arCameraManager.enabled = true;
                  Debug.Log("AR Camera Manager включен");
            }

            if (arCameraBackground != null && !arCameraBackground.enabled)
            {
                  arCameraBackground.enabled = true;
                  arCameraBackground.useCustomMaterial = false;
                  Debug.Log("AR Camera Background включен");
            }

            // Дополнительная задержка для iOS
#if UNITY_IOS && !UNITY_EDITOR
        Debug.Log($"Дополнительная задержка для инициализации на iOS: {initializationDelay} секунд");
        yield return new WaitForSeconds(initializationDelay);
#endif

            // Проверяем правильность ссылок в XROrigin
            if (xrOrigin != null)
            {
                  if (xrOrigin.Camera == null && arCameraManager != null)
                  {
                        xrOrigin.Camera = arCameraManager.GetComponent<Camera>();
                        Debug.Log("Установлена ссылка на камеру в XROrigin");
                  }

                  if (xrOrigin.CameraFloorOffsetObject == null)
                  {
                        // Ищем Camera Offset в иерархии XROrigin
                        Transform cameraOffset = xrOrigin.transform.Find("Camera Offset");
                        if (cameraOffset != null)
                        {
                              xrOrigin.CameraFloorOffsetObject = cameraOffset.gameObject;
                              Debug.Log("Установлена ссылка на Camera Offset в XROrigin");
                        }
                  }
            }

            // Отмечаем сессию как инициализированную
            isSessionInitialized = true;
            Debug.Log("AR сессия инициализирована успешно");

            // Уведомляем другие компоненты, что AR сессия готова
            BroadcastMessage("OnARSessionInitialized", SendMessageOptions.DontRequireReceiver);
      }

      // Публичный метод для проверки готовности AR сессии
      public bool IsSessionInitialized()
      {
            return isSessionInitialized;
      }

      // Публичный метод для ручного запуска сессии
      public void StartSession()
      {
            if (!isSessionInitialized)
            {
                  StartCoroutine(InitializeARSession());
            }
      }

      // Публичный метод для перезапуска сессии
      public void RestartSession()
      {
            if (arSession != null)
            {
                  arSession.Reset();
                  Debug.Log("AR сессия перезапущена");
                  isSessionInitialized = false;
                  StartCoroutine(InitializeARSession());
            }
      }
}