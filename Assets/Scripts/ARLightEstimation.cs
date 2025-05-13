using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.Rendering;

[RequireComponent(typeof(ARCameraManager))]
public class ARLightEstimation : MonoBehaviour
{
      [SerializeField] private bool enableLightEstimation = true;
      [SerializeField] private float updateInterval = 0.5f; // Интервал обновления в секундах
      [SerializeField] private List<Material> materialsToUpdate = new List<Material>();

      private ARCameraManager cameraManager;
      private Light mainDirectionalLight;
      private float lastUpdateTime = 0f;

      // Информация об освещении
      private float? brightness;
      private float? colorTemperature;
      private Color? colorCorrection;
      private Vector3? mainLightDirection;
      private Color? mainLightColor;
      private float? mainLightIntensity;
      private SphericalHarmonicsL2? sphericalHarmonics;

      void Awake()
      {
            cameraManager = GetComponent<ARCameraManager>();

            // Поиск основного направленного света в сцене
            Light[] sceneLights = FindObjectsOfType<Light>();
            foreach (Light light in sceneLights)
            {
                  if (light.type == LightType.Directional && light.gameObject.activeSelf)
                  {
                        mainDirectionalLight = light;
                        break;
                  }
            }

            if (mainDirectionalLight == null)
            {
                  Debug.LogWarning("ARLightEstimation: Не найден основной направленный свет в сцене. Создаем новый.");
                  GameObject lightGO = new GameObject("AR Directional Light");
                  mainDirectionalLight = lightGO.AddComponent<Light>();
                  mainDirectionalLight.type = LightType.Directional;
                  mainDirectionalLight.intensity = 1.0f;
                  mainDirectionalLight.color = Color.white;

                  // Устанавливаем направление - сверху вниз по умолчанию
                  lightGO.transform.rotation = Quaternion.Euler(50, -30, 0);
            }
      }

      void OnEnable()
      {
            if (cameraManager != null && enableLightEstimation)
            {
                  cameraManager.frameReceived += FrameReceived;
            }
      }

      void OnDisable()
      {
            if (cameraManager != null)
            {
                  cameraManager.frameReceived -= FrameReceived;
            }
      }

      // Метод для добавления материала в список
      public void AddMaterial(Material material)
      {
            if (material != null && !materialsToUpdate.Contains(material))
            {
                  materialsToUpdate.Add(material);

                  // Сразу применяем текущие значения освещения к новому материалу
                  UpdateMaterial(material);
            }
      }

      // Обработка информации из AR кадра
      private void FrameReceived(ARCameraFrameEventArgs args)
      {
            // Проверяем, прошло ли достаточно времени с последнего обновления
            if (Time.time - lastUpdateTime < updateInterval)
                  return;

            lastUpdateTime = Time.time;

            // Получаем оценку яркости сцены
            if (args.lightEstimation.averageBrightness.HasValue)
            {
                  brightness = args.lightEstimation.averageBrightness.Value;
            }

            // Получаем оценку цветовой температуры
            if (args.lightEstimation.averageColorTemperature.HasValue)
            {
                  colorTemperature = args.lightEstimation.averageColorTemperature.Value;
            }

            // Получаем коррекцию цвета
            if (args.lightEstimation.colorCorrection.HasValue)
            {
                  colorCorrection = args.lightEstimation.colorCorrection.Value;
            }

            // Получаем информацию о направлении основного источника света
            if (args.lightEstimation.mainLightDirection.HasValue)
            {
                  mainLightDirection = args.lightEstimation.mainLightDirection.Value;

                  // Обновляем направление основного света в сцене
                  if (mainDirectionalLight != null)
                  {
                        mainDirectionalLight.transform.rotation = Quaternion.LookRotation(mainLightDirection.Value);
                  }
            }

            // Получаем информацию о цвете основного источника света
            if (args.lightEstimation.mainLightColor.HasValue)
            {
                  mainLightColor = args.lightEstimation.mainLightColor.Value;

                  // Обновляем цвет основного света в сцене
                  if (mainDirectionalLight != null)
                  {
                        mainDirectionalLight.color = mainLightColor.Value;
                  }
            }

            // Получаем информацию об интенсивности основного источника света
            if (args.lightEstimation.mainLightIntensityLumens.HasValue)
            {
                  mainLightIntensity = args.lightEstimation.mainLightIntensityLumens.Value;

                  // Обновляем интенсивность основного света в сцене (с нормализацией)
                  if (mainDirectionalLight != null)
                  {
                        // Преобразуем люмены в относительную интенсивность (примерная формула)
                        float normalizedIntensity = Mathf.Clamp(mainLightIntensity.Value / 1000f, 0.1f, 2f);
                        mainDirectionalLight.intensity = normalizedIntensity;
                  }
            }

            // Получаем информацию о сферических гармониках (для окружающего освещения)
            if (args.lightEstimation.ambientSphericalHarmonics.HasValue)
            {
                  sphericalHarmonics = args.lightEstimation.ambientSphericalHarmonics.Value;

                  // Устанавливаем сферические гармоники в RenderSettings, если они доступны
                  if (sphericalHarmonics.HasValue)
                  {
                        RenderSettings.ambientMode = AmbientMode.Skybox;
                        RenderSettings.ambientProbe = sphericalHarmonics.Value;
                  }
            }

            // Обновляем все материалы в списке
            foreach (Material material in materialsToUpdate)
            {
                  UpdateMaterial(material);
            }
      }

      // Метод для обновления параметров освещения в материалах
      private void UpdateMaterial(Material material)
      {
            if (material == null)
                  return;

            // Проверяем, поддерживает ли материал нужные свойства
            if (brightness.HasValue && material.HasProperty("_AmbientBrightness"))
            {
                  material.SetFloat("_AmbientBrightness", brightness.Value);
            }

            if (colorCorrection.HasValue && material.HasProperty("_ColorCorrection"))
            {
                  material.SetColor("_ColorCorrection", colorCorrection.Value);
            }

            if (mainLightDirection.HasValue && material.HasProperty("_MainLightDirection"))
            {
                  material.SetVector("_MainLightDirection", mainLightDirection.Value);
            }

            if (mainLightColor.HasValue)
            {
                  // Проверяем наличие свойства _CustomLightColor (новое название)
                  if (material.HasProperty("_CustomLightColor"))
                  {
                        material.SetColor("_CustomLightColor", mainLightColor.Value);
                  }
                  // Для обратной совместимости проверяем и старое имя
                  else if (material.HasProperty("_MainLightColor"))
                  {
                        material.SetColor("_MainLightColor", mainLightColor.Value);
                  }
            }

            if (mainLightIntensity.HasValue && material.HasProperty("_MainLightIntensity"))
            {
                  material.SetFloat("_MainLightIntensity", mainLightIntensity.Value);
            }

            // Устанавливаем общий параметр яркости для всех освещенных материалов
            if (brightness.HasValue && material.HasProperty("_LightIntensity"))
            {
                  // Нормализуем яркость до диапазона [0.5, 1.5]
                  float normalizedBrightness = Mathf.Lerp(0.5f, 1.5f, brightness.Value);
                  material.SetFloat("_LightIntensity", normalizedBrightness);
            }
      }

      // Простая отладочная информация
      void OnGUI()
      {
            // Раскомментируйте для отладки
            /*
            GUIStyle style = new GUIStyle();
            style.fontSize = 20;
            style.normal.textColor = Color.white;

            int y = 40;
            if (brightness.HasValue)
                GUI.Label(new Rect(10, y, 400, 30), $"Brightness: {brightness.Value:F2}", style); y += 30;
            if (colorCorrection.HasValue)
                GUI.Label(new Rect(10, y, 400, 30), $"Color: ({colorCorrection.Value.r:F2}, {colorCorrection.Value.g:F2}, {colorCorrection.Value.b:F2})", style); y += 30;
            if (mainLightDirection.HasValue)
                GUI.Label(new Rect(10, y, 400, 30), $"Light Dir: ({mainLightDirection.Value.x:F2}, {mainLightDirection.Value.y:F2}, {mainLightDirection.Value.z:F2})", style); y += 30;
            */
      }
}