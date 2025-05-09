using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Unity.XR.CoreUtils;
using UnityEngine.UI;

/// <summary>
/// Класс для управления AR сессией и обеспечения правильной последовательности инициализации компонентов
/// </summary>
public class ARSessionManager : MonoBehaviour
{
      [Header("AR Components")]
      [SerializeField] public ARSession arSession;
      [SerializeField] public XROrigin xrOrigin;
      [SerializeField] public ARCameraManager arCameraManager;
      [SerializeField] public ARCameraBackground arCameraBackground;

      [Header("Settings")]
      [SerializeField] private bool autoStartSession = true;
      [SerializeField] private float initializationDelay = 0.5f; // Задержка для iOS

      private bool isSessionInitialized = false;
      private bool _dummy; // Поле для подавления предупреждений компилятора

      private void Awake()
      {
            // Подавляем предупреждение о неиспользуемом поле
            _dummy = initializationDelay > 0;

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
      }

      private void Start()
      {
            if (autoStartSession)
            {
                  StartCoroutine(InitializeARSession());
            }
      }

      /// <summary>
      /// Инициализирует AR сессию и компоненты
      /// </summary>
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

                  // ВАЖНО: Убедимся что камера не использует кастомный материал
                  // Это может вызывать черный экран
                  arCameraBackground.useCustomMaterial = false;

                  Debug.Log("AR Camera Background включен");
            }

            // Дополнительная задержка для iOS
#if UNITY_IOS && !UNITY_EDITOR
        Debug.Log($"Дополнительная задержка для инициализации на iOS: {initializationDelay} секунд");
        yield return new WaitForSeconds(initializationDelay); // Используем поле напрямую
#endif

            isSessionInitialized = true;
            Debug.Log("AR Session полностью инициализирована");

            // Проверяем проблемы с отображением после инициализации
            StartCoroutine(CheckAndFixARDisplay());
      }

      /// <summary>
      /// Проверяет и исправляет проблемы с отображением AR камеры
      /// </summary>
      private IEnumerator CheckAndFixARDisplay()
      {
            // Даем немного времени для инициализации камеры
            yield return new WaitForSeconds(0.5f);

            if (arCameraBackground != null)
            {
                  // Проверяем материал камеры
                  if (arCameraBackground.material == null)
                  {
                        Debug.LogWarning("AR Camera Background материал отсутствует. Пробуем исправить...");

                        // Отключаем и снова включаем компонент
                        arCameraBackground.enabled = false;
                        yield return null;
                        arCameraBackground.enabled = true;
                        yield return null;

                        // Принудительно устанавливаем стандартный материал
                        arCameraBackground.useCustomMaterial = false;
                  }

                  // Проверяем, не используется ли прозрачность в оформлении (она может вызывать черный экран)
                  if (Camera.main != null)
                  {
                        if (Camera.main.clearFlags == CameraClearFlags.SolidColor && Camera.main.backgroundColor.a < 1f)
                        {
                              Debug.LogWarning("Обнаружен полупрозрачный фон камеры. Исправляем...");
                              Camera.main.backgroundColor = new Color(
                                    Camera.main.backgroundColor.r,
                                    Camera.main.backgroundColor.g,
                                    Camera.main.backgroundColor.b,
                                    1f
                              );
                        }
                  }

                  Debug.Log("Проверка AR камеры завершена");
            }
      }

      /// <summary>
      /// Публичный метод для проверки состояния AR сессии
      /// </summary>
      public bool IsSessionInitialized()
      {
            return isSessionInitialized;
      }

      /// <summary>
      /// Публичный метод для ручного запуска сессии
      /// </summary>
      public void StartSession()
      {
            if (!isSessionInitialized)
            {
                  StartCoroutine(InitializeARSession());
            }
      }

      /// <summary>
      /// Публичный метод для перезапуска сессии
      /// </summary>
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