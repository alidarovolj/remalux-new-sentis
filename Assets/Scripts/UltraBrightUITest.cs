using UnityEngine;
using UnityEngine.UI;

public class UltraBrightUITest : MonoBehaviour
{
      [Header("Экстремальные настройки")]
      public bool blockAllUpdates = true;
      public bool useAnimation = true;

      private RawImage rawImage;
      private Texture2D brightTexture;
      private float timer = 0f;

      void Start()
      {
            Debug.Log("🔥 [UltraBrightUITest] СОЗДАЕМ ЭКСТРЕМАЛЬНО ЗАМЕТНЫЙ UI!", gameObject);

            rawImage = GetComponent<RawImage>();
            if (rawImage == null)
            {
                  Debug.LogError("❌ [UltraBrightUITest] RawImage не найден!");
                  return;
            }

            SetupUltraBrightDisplay();
      }

      void Update()
      {
            if (rawImage == null || !useAnimation) return;

            // Анимированная смена цветов для максимальной заметности
            timer += Time.deltaTime * 5f;

            if (blockAllUpdates)
            {
                  // Принудительно блокируем любые попытки изменить текстуру
                  if (rawImage.texture != brightTexture)
                  {
                        Debug.LogWarning("🚫 [UltraBrightUITest] Заблокирована попытка изменить текстуру!");
                        rawImage.texture = brightTexture;
                  }
            }

            // Анимированный цвет
            float colorValue = Mathf.PingPong(timer, 1f);
            rawImage.color = Color.Lerp(Color.red, Color.yellow, colorValue);
      }

      private void SetupUltraBrightDisplay()
      {
            Debug.Log("🎯 [UltraBrightUITest] Настройка экстремально яркого отображения...");

            // Отключаем все другие компоненты UI на этом объекте
            DisableOtherUIComponents();

            // Принудительные настройки
            ForceMaxVisibility();

            // Создаем ультра-яркую текстуру
            CreateUltraBrightTexture();

            // Принудительно устанавливаем
            ApplyTexture();

            Debug.Log("✅ [UltraBrightUITest] Экстремально яркий дисплей готов!");
      }

      private void DisableOtherUIComponents()
      {
            // Отключаем все другие компоненты на этом GameObject
            var components = GetComponents<MonoBehaviour>();
            foreach (var comp in components)
            {
                  if (comp != this && comp.GetType().Name.Contains("Debug") || comp.GetType().Name.Contains("Force"))
                  {
                        comp.enabled = false;
                        Debug.Log($"🚫 [UltraBrightUITest] Отключен компонент: {comp.GetType().Name}");
                  }
            }
      }

      private void ForceMaxVisibility()
      {
            // Максимальный размер в центре экрана
            rawImage.rectTransform.sizeDelta = new Vector2(500, 500);
            rawImage.rectTransform.anchoredPosition = Vector2.zero;
            rawImage.rectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            rawImage.rectTransform.anchorMax = new Vector2(0.5f, 0.5f);

            // Принудительные настройки
            rawImage.enabled = true;
            rawImage.gameObject.SetActive(true);
            rawImage.material = null; // Убираем любые материалы
            rawImage.color = Color.white;

            // Максимальный Canvas SortingOrder
            Canvas canvas = GetComponentInParent<Canvas>();
            if (canvas != null)
            {
                  canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                  canvas.sortingOrder = 999;
                  canvas.overrideSorting = true;
            }
      }

      private void CreateUltraBrightTexture()
      {
            brightTexture = new Texture2D(256, 256, TextureFormat.RGBA32, false);

            // Создаем экстремально яркий паттерн
            for (int y = 0; y < 256; y++)
            {
                  for (int x = 0; x < 256; x++)
                  {
                        Color color;

                        // Крупные полосы для максимальной видимости
                        if ((x / 32 + y / 32) % 2 == 0)
                        {
                              color = Color.red;       // Ярко-красный
                        }
                        else
                        {
                              color = Color.yellow;    // Ярко-желтый
                        }

                        brightTexture.SetPixel(x, y, color);
                  }
            }

            brightTexture.Apply();
            Debug.Log("🌈 [UltraBrightUITest] Создана ультра-яркая полосатая текстура 256x256");
      }

      private void ApplyTexture()
      {
            rawImage.texture = brightTexture;

            // Принудительное обновление
            Canvas.ForceUpdateCanvases();

            Debug.Log($"🎯 [UltraBrightUITest] Текстура применена! Размер RawImage: {rawImage.rectTransform.sizeDelta}");
            Debug.Log($"📍 [UltraBrightUITest] Позиция: {rawImage.rectTransform.anchoredPosition}");
            Debug.Log($"🎨 [UltraBrightUITest] Цвет: {rawImage.color}");
            Debug.Log($"✨ [UltraBrightUITest] Активность: GameObject={rawImage.gameObject.activeInHierarchy}, Component={rawImage.enabled}");
      }

      [ContextMenu("Принудительно установить яркую текстуру")]
      public void ForceSetBrightTexture()
      {
            SetupUltraBrightDisplay();
      }

      [ContextMenu("Заблокировать обновления")]
      public void ToggleBlockUpdates()
      {
            blockAllUpdates = !blockAllUpdates;
            Debug.Log($"🔒 [UltraBrightUITest] Блокировка обновлений: {blockAllUpdates}");
      }
}