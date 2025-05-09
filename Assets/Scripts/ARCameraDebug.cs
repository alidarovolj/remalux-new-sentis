using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using Unity.XR.CoreUtils;

/// <summary>
/// Скрипт для отладки AR камеры и решения проблемы черного экрана
/// </summary>
public class ARCameraDebug : MonoBehaviour
{
      [Header("AR Components")]
      [Tooltip("Автоматически находить компоненты AR")]
      public bool autoFindComponents = true;

      [Tooltip("AR Camera Manager")]
      public ARCameraManager cameraManager;

      [Tooltip("AR Camera Background")]
      public ARCameraBackground cameraBackground;

      [Tooltip("XR Origin")]
      public XROrigin xrOrigin;

      [Header("Debug Options")]
      [Tooltip("Показывать информацию на экране")]
      public bool showDebugInfo = true;

      [Tooltip("Пробовать исправить черный экран при старте")]
      public bool fixBlackScreenOnStart = true;

      private string debugInfo = "";
      private int framesWithoutCamera = 0;
      private const int MAX_FRAMES_WITHOUT_CAMERA = 60; // ~1 секунда при 60 FPS

      void Start()
      {
            if (autoFindComponents)
            {
                  FindARComponents();
            }

            if (fixBlackScreenOnStart)
            {
                  StartCoroutine(FixBlackScreenCoroutine());
            }
      }

      void Update()
      {
            if (showDebugInfo)
            {
                  UpdateDebugInfo();
            }

            // Проверяем, отображается ли камера
            if (cameraManager != null && cameraBackground != null)
            {
                  if (!cameraBackground.enabled || !cameraManager.enabled)
                  {
                        framesWithoutCamera++;

                        if (framesWithoutCamera > MAX_FRAMES_WITHOUT_CAMERA)
                        {
                              Debug.LogWarning("ARCameraDebug: Камера не активна в течение длительного времени. Пробуем исправить...");
                              StartCoroutine(FixBlackScreenCoroutine());
                              framesWithoutCamera = 0;
                        }
                  }
                  else
                  {
                        framesWithoutCamera = 0;
                  }
            }
      }

      /// <summary>
      /// Находит все необходимые AR компоненты в сцене
      /// </summary>
      private void FindARComponents()
      {
            if (cameraManager == null)
            {
                  cameraManager = FindObjectOfType<ARCameraManager>();
            }

            if (cameraBackground == null && cameraManager != null)
            {
                  cameraBackground = cameraManager.GetComponent<ARCameraBackground>();
            }

            if (xrOrigin == null)
            {
                  xrOrigin = FindObjectOfType<XROrigin>();
            }

            if (cameraManager != null && xrOrigin != null && xrOrigin.Camera == null)
            {
                  xrOrigin.Camera = cameraManager.GetComponent<Camera>();
            }
      }

      /// <summary>
      /// Обновляет отладочную информацию для отображения на экране
      /// </summary>
      private void UpdateDebugInfo()
      {
            debugInfo = "=== AR Camera Debug ===\n";

            if (xrOrigin != null)
            {
                  debugInfo += $"XR Origin: Active: {xrOrigin.gameObject.activeSelf}, Enabled: {xrOrigin.enabled}\n";
                  debugInfo += $"Camera Set: {(xrOrigin.Camera != null ? "Yes" : "No")}\n";
                  debugInfo += $"Camera Offset Set: {(xrOrigin.CameraFloorOffsetObject != null ? "Yes" : "No")}\n";
            }
            else
            {
                  debugInfo += "XR Origin: Not found\n";
            }

            if (cameraManager != null)
            {
                  debugInfo += $"Camera Manager: Enabled: {cameraManager.enabled}, Active: {cameraManager.gameObject.activeSelf}\n";

                  if (cameraManager.TryGetIntrinsics(out var intrinsics))
                  {
                        debugInfo += $"Camera Intrinsics Valid: Yes, Size: {intrinsics.resolution}\n";
                  }
                  else
                  {
                        debugInfo += "Camera Intrinsics Valid: No\n";
                  }
            }
            else
            {
                  debugInfo += "Camera Manager: Not found\n";
            }

            if (cameraBackground != null)
            {
                  debugInfo += $"Camera Background: Enabled: {cameraBackground.enabled}, Use Custom Material: {cameraBackground.useCustomMaterial}\n";

                  if (cameraBackground.material != null)
                  {
                        debugInfo += $"Background Material: Valid, Shader: {cameraBackground.material.shader.name}\n";
                  }
                  else
                  {
                        debugInfo += "Background Material: Missing (black screen issue)\n";
                  }
            }
            else
            {
                  debugInfo += "Camera Background: Not found\n";
            }
      }

      /// <summary>
      /// Корутина для исправления проблемы черного экрана
      /// </summary>
      private IEnumerator FixBlackScreenCoroutine()
      {
            Debug.Log("ARCameraDebug: Запуск исправления черного экрана...");

            // Шаг 1: Убедиться, что все компоненты найдены
            FindARComponents();

            if (cameraManager == null || cameraBackground == null || xrOrigin == null)
            {
                  Debug.LogError("ARCameraDebug: Не удалось найти необходимые AR компоненты");
                  yield break;
            }

            // Шаг 2: Перезапустить AR камеру
            cameraBackground.enabled = false;
            cameraManager.enabled = false;

            yield return new WaitForSeconds(0.5f);

            // Шаг 3: Проверить и исправить материал
            cameraBackground.useCustomMaterial = false;

            yield return null;

            // Шаг 4: Включить все заново
            cameraManager.enabled = true;

            yield return null;

            cameraBackground.enabled = true;

            // Шаг 5: Проверить камеру XR Origin
            if (xrOrigin.Camera == null)
            {
                  xrOrigin.Camera = cameraManager.GetComponent<Camera>();
                  Debug.Log("ARCameraDebug: Установлена камера в XR Origin");
            }

            Debug.Log("ARCameraDebug: Исправление черного экрана завершено");
      }

      /// <summary>
      /// Показывает отладочную информацию на экране
      /// </summary>
      private void OnGUI()
      {
            if (showDebugInfo)
            {
                  GUIStyle debugStyle = new GUIStyle();
                  debugStyle.normal.textColor = Color.green;
                  debugStyle.fontSize = 16;
                  debugStyle.fontStyle = FontStyle.Bold;

                  GUI.Label(new Rect(10, 10, Screen.width - 20, Screen.height - 20), debugInfo, debugStyle);

                  // Добавляем кнопку для ручного исправления
                  if (GUI.Button(new Rect(10, Screen.height - 60, 200, 50), "Исправить черный экран"))
                  {
                        StartCoroutine(FixBlackScreenCoroutine());
                  }
            }
      }

      /// <summary>
      /// Публичный метод для запуска исправления черного экрана
      /// </summary>
      public void FixBlackScreen()
      {
            StartCoroutine(FixBlackScreenCoroutine());
      }
}