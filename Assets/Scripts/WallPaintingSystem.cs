using UnityEngine;
using UnityEngine.XR.ARFoundation;
using System.Collections.Generic;

/// <summary>
/// Система покраски стен для AR приложения (аналог Dulux Visualizer)
/// Интегрируется с ColorPickerUI для выбора цветов
/// </summary>
public class WallPaintingSystem : MonoBehaviour
{
      [Header("Материалы")]
      [SerializeField] private Material basePaintMaterial;
      [SerializeField] private Material transparentPaintMaterial;

      [Header("Настройки покраски")]
      [SerializeField] private float paintOpacity = 0.8f;
      [Range(0f, 1f)]
      [SerializeField] private float paintBlendFactor = 0.7f;
      // [SerializeField] private bool autoApplyColorToNewPlanes = true; // Не используется пока

      [Header("Компоненты")]
      [SerializeField] private ARManagerInitializer2 arManager;
      [SerializeField] private ColorPickerUI colorPickerUI;

      // Текущий цвет покраски
      private Color currentPaintColor = Color.white;
      private string currentColorName = "Белый";

      // Карта плоскостей и их оригинальных материалов
      private Dictionary<GameObject, Material> originalMaterials = new Dictionary<GameObject, Material>();
      private Dictionary<GameObject, Material> paintedMaterials = new Dictionary<GameObject, Material>();

      // События
      public System.Action<Color, string> OnColorChanged;
      public System.Action<GameObject, Color> OnPlaneColorApplied;

      private void Start()
      {
            InitializeSystem();
            SubscribeToEvents();
            Debug.Log("[WallPaintingSystem] ✅ Система покраски стен инициализирована");
      }

      private void InitializeSystem()
      {
            // Поиск компонентов если не назначены
            if (arManager == null)
            {
                  arManager = FindObjectOfType<ARManagerInitializer2>();
            }

            if (colorPickerUI == null)
            {
                  colorPickerUI = FindObjectOfType<ColorPickerUI>();
            }

            // Создаем базовые материалы если не назначены
            CreateBaseMaterials();
      }

      private void CreateBaseMaterials()
      {
            if (basePaintMaterial == null)
            {
                  basePaintMaterial = CreatePaintMaterial(false);
                  Debug.Log("[WallPaintingSystem] ✅ Создан базовый материал для покраски");
            }

            if (transparentPaintMaterial == null)
            {
                  transparentPaintMaterial = CreatePaintMaterial(true);
                  Debug.Log("[WallPaintingSystem] ✅ Создан прозрачный материал для покраски");
            }
      }

      private Material CreatePaintMaterial(bool transparent)
      {
            // Ищем существующий WallPaint шейдер
            Shader paintShader = Shader.Find("Custom/WallPaint");
            if (paintShader == null)
            {
                  // Fallback на стандартный шейдер
                  paintShader = Shader.Find("Universal Render Pipeline/Lit");
                  if (paintShader == null)
                  {
                        paintShader = Shader.Find("Standard");
                  }
            }

            Material material = new Material(paintShader);
            material.name = transparent ? "WallPaint_Transparent" : "WallPaint_Opaque";

            // Настройки для прозрачности
            if (transparent)
            {
                  material.SetFloat("_Mode", 3); // Transparent mode for Standard shader
                  material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
                  material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
                  material.SetInt("_ZWrite", 0);
                  material.DisableKeyword("_ALPHATEST_ON");
                  material.EnableKeyword("_ALPHABLEND_ON");
                  material.DisableKeyword("_ALPHAPREMULTIPLY_ON");
                  material.renderQueue = 3000;
            }

            return material;
      }

      private void SubscribeToEvents()
      {
            // Подписываемся на события ColorPickerUI
            if (colorPickerUI != null)
            {
                  colorPickerUI.OnColorSelected += OnColorSelected;
                  colorPickerUI.OnColorApplied += OnColorApplied;
            }

            // Подписываемся на создание новых плоскостей
            if (arManager != null)
            {
                  // Здесь можно подписаться на события создания плоскостей если они есть
            }
      }

      private void OnColorSelected(Color color, string colorName)
      {
            currentPaintColor = color;
            currentColorName = colorName;

            OnColorChanged?.Invoke(color, colorName);

            Debug.Log($"[WallPaintingSystem] 🎨 Выбран цвет для покраски: {colorName}");
      }

      private void OnColorApplied(Color color)
      {
            ApplyColorToAllWalls(color);
      }

      /// <summary>
      /// Применить цвет ко всем стенам
      /// </summary>
      public void ApplyColorToAllWalls(Color color)
      {
            if (arManager?.GeneratedPlanes == null)
            {
                  Debug.LogWarning("[WallPaintingSystem] Нет доступа к сгенерированным плоскостям");
                  return;
            }

            int paintedCount = 0;

            foreach (GameObject plane in arManager.GeneratedPlanes)
            {
                  if (plane == null) continue;

                  if (ApplyColorToPlane(plane, color))
                  {
                        paintedCount++;
                  }
            }

            Debug.Log($"[WallPaintingSystem] ✅ Покрашено {paintedCount} стен цветом '{currentColorName}'");
      }

      /// <summary>
      /// Применить цвет к конкретной плоскости
      /// </summary>
      public bool ApplyColorToPlane(GameObject plane, Color color)
      {
            if (plane == null) return false;

            MeshRenderer renderer = plane.GetComponent<MeshRenderer>();
            if (renderer == null) return false;

            // Сохраняем оригинальный материал если еще не сохранен
            if (!originalMaterials.ContainsKey(plane))
            {
                  originalMaterials[plane] = renderer.material;
            }

            // Создаем новый материал для покраски
            Material paintMaterial = CreatePaintedMaterial(color);

            // Применяем материал
            renderer.material = paintMaterial;

            // Сохраняем покрашенный материал
            paintedMaterials[plane] = paintMaterial;

            // Вызываем событие
            OnPlaneColorApplied?.Invoke(plane, color);

            return true;
      }

      /// <summary>
      /// Создать материал с заданным цветом
      /// </summary>
      private Material CreatePaintedMaterial(Color color)
      {
            Material material = new Material(basePaintMaterial);
            material.color = color;

            // Настройки прозрачности
            Color materialColor = color;
            materialColor.a = paintOpacity;
            material.color = materialColor;

            // Если есть custom shader properties
            if (material.HasProperty("_PaintColor"))
            {
                  material.SetColor("_PaintColor", color);
            }

            if (material.HasProperty("_BlendFactor"))
            {
                  material.SetFloat("_BlendFactor", paintBlendFactor);
            }

            if (material.HasProperty("_Alpha"))
            {
                  material.SetFloat("_Alpha", paintOpacity);
            }

            return material;
      }

      /// <summary>
      /// Сбросить цвет плоскости к оригинальному
      /// </summary>
      public bool ResetPlaneColor(GameObject plane)
      {
            if (plane == null) return false;

            MeshRenderer renderer = plane.GetComponent<MeshRenderer>();
            if (renderer == null) return false;

            if (originalMaterials.ContainsKey(plane))
            {
                  renderer.material = originalMaterials[plane];

                  // Очищаем покрашенный материал
                  if (paintedMaterials.ContainsKey(plane))
                  {
                        paintedMaterials.Remove(plane);
                  }

                  return true;
            }

            return false;
      }

      /// <summary>
      /// Сбросить все плоскости к оригинальным цветам
      /// </summary>
      public void ResetAllColors()
      {
            int resetCount = 0;

            List<GameObject> planesToReset = new List<GameObject>(originalMaterials.Keys);

            foreach (GameObject plane in planesToReset)
            {
                  if (ResetPlaneColor(plane))
                  {
                        resetCount++;
                  }
            }

            Debug.Log($"[WallPaintingSystem] 🔄 Сброшено {resetCount} стен к оригинальным цветам");
      }

      /// <summary>
      /// Получить текущий цвет плоскости
      /// </summary>
      public Color GetPlaneColor(GameObject plane)
      {
            if (plane == null) return Color.white;

            MeshRenderer renderer = plane.GetComponent<MeshRenderer>();
            if (renderer?.material != null)
            {
                  return renderer.material.color;
            }

            return Color.white;
      }

      /// <summary>
      /// Проверить, покрашена ли плоскость
      /// </summary>
      public bool IsPlaneCustomPainted(GameObject plane)
      {
            return paintedMaterials.ContainsKey(plane);
      }

      /// <summary>
      /// Получить информацию о всех покрашенных плоскостях
      /// </summary>
      public Dictionary<GameObject, Color> GetPaintedPlanesInfo()
      {
            Dictionary<GameObject, Color> info = new Dictionary<GameObject, Color>();

            foreach (var kvp in paintedMaterials)
            {
                  if (kvp.Key != null && kvp.Value != null)
                  {
                        info[kvp.Key] = kvp.Value.color;
                  }
            }

            return info;
      }

      /// <summary>
      /// Настройка прозрачности покраски
      /// </summary>
      public void SetPaintOpacity(float opacity)
      {
            paintOpacity = Mathf.Clamp01(opacity);

            // Обновляем все покрашенные материалы
            foreach (var kvp in paintedMaterials)
            {
                  if (kvp.Value != null)
                  {
                        Color color = kvp.Value.color;
                        color.a = paintOpacity;
                        kvp.Value.color = color;

                        if (kvp.Value.HasProperty("_Alpha"))
                        {
                              kvp.Value.SetFloat("_Alpha", paintOpacity);
                        }
                  }
            }

            Debug.Log($"[WallPaintingSystem] 🎨 Прозрачность покраски установлена: {paintOpacity:F2}");
      }

      /// <summary>
      /// Настройка коэффициента смешивания
      /// </summary>
      public void SetPaintBlendFactor(float blendFactor)
      {
            paintBlendFactor = Mathf.Clamp01(blendFactor);

            // Обновляем все покрашенные материалы
            foreach (var kvp in paintedMaterials)
            {
                  if (kvp.Value != null && kvp.Value.HasProperty("_BlendFactor"))
                  {
                        kvp.Value.SetFloat("_BlendFactor", paintBlendFactor);
                  }
            }

            Debug.Log($"[WallPaintingSystem] 🎨 Коэффициент смешивания установлен: {paintBlendFactor:F2}");
      }

      // Публичные геттеры
      public Color CurrentPaintColor => currentPaintColor;
      public string CurrentColorName => currentColorName;
      public float PaintOpacity => paintOpacity;
      public float PaintBlendFactor => paintBlendFactor;

      private void OnDestroy()
      {
            // Отписываемся от событий
            if (colorPickerUI != null)
            {
                  colorPickerUI.OnColorSelected -= OnColorSelected;
                  colorPickerUI.OnColorApplied -= OnColorApplied;
            }

            // Очищаем созданные материалы
            foreach (var material in paintedMaterials.Values)
            {
                  if (material != null)
                  {
                        DestroyImmediate(material);
                  }
            }
      }
}