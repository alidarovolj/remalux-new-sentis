using UnityEngine;
using UnityEngine.XR.ARFoundation;
using System.Collections;
using System.Reflection;
using Unity.XR.CoreUtils;

/// <summary>
/// Этот класс объединяет компоненты для реализации системы покраски стен в AR.
/// Добавьте этот компонент на GameObject для быстрой настройки всей системы покраски стен.
/// </summary>
[RequireComponent(typeof(WallPaintEffect))]
[RequireComponent(typeof(ARWallPainter))]
public class ARWallPainterSystem : MonoBehaviour
{
      // Ссылки на компоненты системы
      [SerializeField] private WallPaintEffect wallPaintEffect;
      [SerializeField] private ARWallPainter wallPainter;
      [SerializeField] private WallSegmentation wallSegmentation;
      [SerializeField] private WallMaskGenerator wallMaskGenerator;

      // Настройки инициализации
      [SerializeField] private bool autoFindComponents = true;
      [SerializeField] private bool autoCreateMissingComponents = true;
      [SerializeField] private float initializationDelay = 1.0f;

      // AR компоненты, которые должны быть в сцене
      private XROrigin xrOrigin;
      private ARPlaneManager arPlaneManager;
      private ARRaycastManager arRaycastManager;
      private Camera arCamera;

      private void Awake()
      {
            // Находим или создаем компоненты, если необходимо
            if (autoFindComponents)
            {
                  FindRequiredComponents();
            }
      }

      private void Start()
      {
            // Запускаем инициализацию с задержкой, чтобы убедиться, что AR компоненты инициализированы
            StartCoroutine(InitializeWithDelay());
      }

      private IEnumerator InitializeWithDelay()
      {
            // Ждем указанное время для инициализации AR компонентов
            yield return new WaitForSeconds(initializationDelay);

            // Проверяем и инициализируем компоненты
            InitializeComponents();

            Debug.Log("ARWallPainterSystem инициализирована и готова к работе");
      }

      private void FindRequiredComponents()
      {
            // Находим основные AR компоненты
            xrOrigin = FindObjectOfType<XROrigin>();
            if (xrOrigin == null)
            {
                  Debug.LogError("ARWallPainterSystem: XROrigin не найден в сцене!");
                  return;
            }

            arCamera = xrOrigin.Camera;
            if (arCamera == null)
            {
                  Debug.LogError("ARWallPainterSystem: AR Camera не найдена!");
                  return;
            }

            arPlaneManager = FindObjectOfType<ARPlaneManager>();
            if (arPlaneManager == null)
            {
                  Debug.LogError("ARWallPainterSystem: ARPlaneManager не найден!");
                  return;
            }

            arRaycastManager = FindObjectOfType<ARRaycastManager>();
            if (arRaycastManager == null)
            {
                  Debug.LogError("ARWallPainterSystem: ARRaycastManager не найден!");
                  return;
            }

            // Находим компоненты системы покраски стен
            if (wallPaintEffect == null) wallPaintEffect = GetComponent<WallPaintEffect>();
            if (wallPainter == null) wallPainter = GetComponent<ARWallPainter>();
            if (wallSegmentation == null) wallSegmentation = FindObjectOfType<WallSegmentation>();
            if (wallMaskGenerator == null) wallMaskGenerator = FindObjectOfType<WallMaskGenerator>();

            // Создаем отсутствующие компоненты, если это разрешено
            if (autoCreateMissingComponents)
            {
                  if (wallPaintEffect == null) wallPaintEffect = gameObject.AddComponent<WallPaintEffect>();
                  if (wallPainter == null) wallPainter = gameObject.AddComponent<ARWallPainter>();

                  if (wallSegmentation == null)
                  {
                        GameObject segmentationObj = new GameObject("WallSegmentation");
                        wallSegmentation = segmentationObj.AddComponent<WallSegmentation>();
                        Debug.Log("ARWallPainterSystem: Создан компонент WallSegmentation");
                  }

                  if (wallMaskGenerator == null)
                  {
                        GameObject maskGeneratorObj = new GameObject("WallMaskGenerator");
                        wallMaskGenerator = maskGeneratorObj.AddComponent<WallMaskGenerator>();
                        Debug.Log("ARWallPainterSystem: Создан компонент WallMaskGenerator");
                  }
            }
      }

      private void InitializeComponents()
      {
            // Проверяем, что все необходимые компоненты найдены
            if (wallPaintEffect == null || wallPainter == null)
            {
                  Debug.LogError("ARWallPainterSystem: Отсутствуют обязательные компоненты!");
                  return;
            }

            // Настраиваем WallPainter
            SetupWallPainter();

            // Настраиваем WallPaintEffect
            SetupWallPaintEffect();

            // Настраиваем WallMaskGenerator, если он есть
            if (wallMaskGenerator != null)
            {
                  SetupWallMaskGenerator();
            }

            // Активируем сегментацию стен, если она доступна
            if (wallSegmentation != null)
            {
                  SetupWallSegmentation();
            }
      }

      private void SetupWallPainter()
      {
            // Устанавливаем ссылки на необходимые компоненты напрямую или через рефлексию
            if (wallPainter != null)
            {
                  // Используем рефлексию для установки приватных полей
                  SetFieldValue(wallPainter, "arCamera", arCamera);
                  SetFieldValue(wallPainter, "wallPaintEffect", wallPaintEffect);
                  SetFieldValue(wallPainter, "raycastManager", arRaycastManager);
            }
      }

      private void SetupWallPaintEffect()
      {
            // Устанавливаем ссылки на необходимые компоненты
            if (wallPaintEffect != null && wallSegmentation != null)
            {
                  SetFieldValue(wallPaintEffect, "wallSegmentation", wallSegmentation);

                  // Включаем привязку к AR плоскостям
                  wallPaintEffect.SetAttachToARPlanes(true);
            }
      }

      private void SetupWallMaskGenerator()
      {
            // Устанавливаем ссылки на необходимые компоненты
            if (wallMaskGenerator != null)
            {
                  SetFieldValue(wallMaskGenerator, "planeManager", arPlaneManager);
                  SetFieldValue(wallMaskGenerator, "arCamera", arCamera);

                  // Если есть материал, устанавливаем его
                  if (wallPaintEffect != null)
                  {
                        Material material = wallPaintEffect.GetMaterial();
                        if (material != null)
                        {
                              SetFieldValue(wallMaskGenerator, "wallPaintMaterial", material);
                        }
                  }

                  // Заставляем маску обновиться в начале работы
                  wallMaskGenerator.ForceUpdateWallMask();
            }
      }

      private void SetupWallSegmentation()
      {
            // Убеждаемся, что сегментация использует корректный XROrigin
            if (wallSegmentation != null && xrOrigin != null)
            {
                  SetFieldValue(wallSegmentation, "xrOrigin", xrOrigin);
            }
      }

      // Вспомогательный метод для установки значения поля через рефлексию
      private void SetFieldValue(object targetObject, string fieldName, object value)
      {
            if (targetObject == null || string.IsNullOrEmpty(fieldName) || value == null)
                  return;

            System.Type type = targetObject.GetType();
            FieldInfo fieldInfo = null;

            // Ищем поле в текущем типе и всех базовых типах
            while (type != null)
            {
                  fieldInfo = type.GetField(fieldName,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                  if (fieldInfo != null)
                        break;

                  type = type.BaseType;
            }

            if (fieldInfo != null)
            {
                  try
                  {
                        fieldInfo.SetValue(targetObject, value);
                        Debug.Log($"Успешно установлено значение поля {fieldName}");
                  }
                  catch (System.Exception ex)
                  {
                        Debug.LogError($"Ошибка при установке значения поля {fieldName}: {ex.Message}");
                  }
            }
            else
            {
                  Debug.LogWarning($"Поле {fieldName} не найдено в объекте типа {targetObject.GetType().Name}");
            }
      }

      // Публичные методы для управления системой

      /// <summary>
      /// Устанавливает цвет для покраски стен
      /// </summary>
      public void SetWallColor(Color color)
      {
            if (wallPainter != null)
            {
                  wallPainter.SetPaintColor(color);
            }
      }

      /// <summary>
      /// Устанавливает интенсивность покраски (0-1)
      /// </summary>
      public void SetPaintIntensity(float intensity)
      {
            if (wallPainter != null)
            {
                  wallPainter.SetBlendFactor(Mathf.Clamp01(intensity));
            }
      }

      /// <summary>
      /// Сбрасывает все покрашенные стены
      /// </summary>
      public void ResetWalls()
      {
            if (wallPainter != null)
            {
                  wallPainter.ResetAllWalls();
            }
      }

      /// <summary>
      /// Включает/отключает маскирование с использованием сегментации
      /// </summary>
      public void SetUseMask(bool useMask)
      {
            if (wallPaintEffect != null)
            {
                  wallPaintEffect.SetUseMask(useMask);
            }
      }

      /// <summary>
      /// Обновляет маску стен вручную
      /// </summary>
      public void UpdateWallMask()
      {
            if (wallMaskGenerator != null)
            {
                  wallMaskGenerator.ForceUpdateWallMask();
            }
      }
}