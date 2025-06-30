using UnityEngine;
using UnityEngine.UI;

// [RequireComponent(typeof(RawImage))] // Оставляем, но можно и убрать, если RawImage всегда есть
public class DebugMaskLinker : MonoBehaviour
{
    [Header("Отладочные настройки")]
    [SerializeField] private bool enableMaskUpdates = true;
    [SerializeField] private bool logUpdates = true;

    private RawImage rawImage;
    private WallSegmentation wallSegmentation;
    private int updateCount = 0;
    private bool hasSavedOnce = false;
    private const int SAVE_AFTER_N_UPDATES = 5; // Уменьшено для быстрой проверки

    [Header("Материал для отображения")]
    [SerializeField] private Material segmentationDisplayMaterial;

    void Start()
    {
        Debug.Log("[DebugMaskLinker] 🔄 Start() вызван", gameObject);

        rawImage = GetComponent<RawImage>();
        if (rawImage == null)
        {
            Debug.LogError("[DebugMaskLinker] ❌ Компонент RawImage не найден на этом GameObject!", gameObject);
            enabled = false; // Отключаем компонент, если нет RawImage
            return;
        }
        else
        {
            Debug.Log($"[DebugMaskLinker] ✅ RawImage найден. Текущий размер: {rawImage.rectTransform.sizeDelta}, активен: {rawImage.gameObject.activeInHierarchy}", gameObject);
        }

        // Проверяем, есть ли UltraBrightUITest компонент
        var ultraBrightTest = GetComponent<UltraBrightUITest>();
        if (ultraBrightTest != null)
        {
            enableMaskUpdates = false;
            Debug.LogWarning("[DebugMaskLinker] ⚠️ UltraBrightUITest обнаружен - отключаем обновления масок для тестирования!", gameObject);
            return;
        }

        // Сразу увеличиваем размер и перемещаем в центр экрана для лучшей видимости
        rawImage.rectTransform.sizeDelta = new Vector2(300, 300);
        rawImage.rectTransform.anchoredPosition = Vector2.zero; // Центр экрана
        Debug.Log("[DebugMaskLinker] 📐 RawImage перемещен в центр экрана с размером 300x300", gameObject);

        // Проверяем Canvas
        Canvas canvas = rawImage.GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            Debug.Log($"[DebugMaskLinker] ✅ Canvas найден. RenderMode: {canvas.renderMode}, SortingOrder: {canvas.sortingOrder}", gameObject);
            // Убеждаемся, что Canvas в Screen Space - Overlay для максимальной видимости
            if (canvas.renderMode != RenderMode.ScreenSpaceOverlay)
            {
                Debug.LogWarning($"[DebugMaskLinker] ⚠️ Canvas не в режиме ScreenSpaceOverlay. Текущий режим: {canvas.renderMode}", gameObject);
            }
        }
        else
        {
            Debug.LogError("[DebugMaskLinker] ❌ Canvas не найден в родительских объектах!", gameObject);
        }

        wallSegmentation = FindObjectOfType<WallSegmentation>();
        if (wallSegmentation == null)
        {
            Debug.LogError("[DebugMaskLinker] ❌ Компонент WallSegmentation не найден в сцене!", gameObject);
            enabled = false; // Отключаем компонент, если нет WallSegmentation
            return;
        }
        else
        {
            Debug.Log($"[DebugMaskLinker] ✅ WallSegmentation найден. Инициализирован: {wallSegmentation.IsModelInitialized}", gameObject);
        }

        // Подписываемся на событие обновления маски
        wallSegmentation.OnSegmentationMaskUpdated += UpdateMaskTexture;
        Debug.Log("[DebugMaskLinker] ✅ Успешно подписался на OnSegmentationMaskUpdated от WallSegmentation.", gameObject);

        // Устанавливаем материал заранее
        if (segmentationDisplayMaterial != null)
        {
            // ВРЕМЕННО отключаем материал для диагностики
            // rawImage.material = segmentationDisplayMaterial;
            rawImage.material = null; // Используем стандартный UI материал
            Debug.Log("[DebugMaskLinker] ⚠️ Материал временно отключен для диагностики - используем стандартный UI", gameObject);
        }
        else
        {
            rawImage.material = null; // Убеждаемся что используется стандартный материал
            Debug.LogWarning("[DebugMaskLinker] ⚠️ segmentationDisplayMaterial не назначен в Inspector - используем стандартный UI материал", gameObject);
        }

        // Попробуем установить начальную маску, если она уже доступна
        Debug.Log("[DebugMaskLinker] 🎯 Попытка установить начальную маску.", gameObject);
        if (wallSegmentation.IsModelInitialized && wallSegmentation.segmentationMaskTexture != null)
        {
            UpdateMaskTexture(wallSegmentation.segmentationMaskTexture);
        }
        else
        {
            Debug.LogWarning($"[DebugMaskLinker] ⏳ Начальная маска не доступна при старте. IsInitialized: {wallSegmentation.IsModelInitialized}, MaskTexture: {(wallSegmentation.segmentationMaskTexture != null ? "exists" : "null")}", gameObject);

            // Создаем тестовую текстуру для проверки работоспособности
            CreateTestTexture();
        }
    }

    public void UpdateMaskTexture(RenderTexture mask)
    {
        if (!enableMaskUpdates)
        {
            if (logUpdates)
                Debug.Log("[DebugMaskLinker] 🚫 Обновления масок отключены - игнорируем UpdateMaskTexture", gameObject);
            return;
        }

        if (rawImage == null || mask == null)
        {
            Debug.LogWarning($"[DebugMaskLinker] ⚠️ Не удалось обновить маску. RawImage: {rawImage != null}, Mask: {mask != null}", gameObject);
            return;
        }

        Debug.Log($"[DebugMaskLinker] 🎯 UpdateMaskTexture вызван. Маска: {mask.width}x{mask.height}", gameObject);

        if (mask.IsCreated())
        {
            Debug.Log($"[DebugMaskLinker] ✅ Применяем маску {mask.width}x{mask.height} к RawImage. Формат: {mask.format}", gameObject);

            rawImage.texture = mask;
            rawImage.color = Color.white; // Белый для нормального отображения
            rawImage.material = null; // ПРИНУДИТЕЛЬНО убираем любой материал для UI отображения

            // Принудительно активируем GameObject
            if (!rawImage.gameObject.activeInHierarchy)
            {
                rawImage.gameObject.SetActive(true);
                Debug.Log("[DebugMaskLinker] ✅ GameObject принудительно активирован", gameObject);
            }

            // Убеждаемся, что RawImage видим
            rawImage.enabled = true;

            updateCount++;
            Debug.Log($"[DebugMaskLinker] 📊 Маска #{updateCount} успешно применена к RawImage", gameObject);

            // Каждые 10 обновлений логируем подробную информацию
            if (updateCount % 10 == 0)
            {
                LogDetailedInfo();
            }

            // Автоматическое сохранение маски для отладки
            if (!hasSavedOnce && updateCount >= SAVE_AFTER_N_UPDATES)
            {
                Debug.Log($"[DebugMaskLinker] 💾 Достигнуто {SAVE_AFTER_N_UPDATES} обновлений. Сохраняем маску...", gameObject);
                SaveRenderTextureToFile(mask, "DebugMaskOutput_Auto.png");
                hasSavedOnce = true; // Предотвращаем повторное сохранение
            }
        }
        else
        {
            Debug.LogWarning("[DebugMaskLinker] ⚠️ RenderTexture не создана (IsCreated = false). Устанавливаем желтый цвет для индикации.", gameObject);
            rawImage.texture = null;
            rawImage.color = Color.yellow; // Желтый цвет для индикации некорректной маски
        }
    }

    private void LogDetailedInfo()
    {
        Debug.Log($"[DebugMaskLinker] 🔍 Подробная диагностика:", gameObject);
        Debug.Log($"  - GameObject активен: {gameObject.activeInHierarchy}", gameObject);
        Debug.Log($"  - RawImage enabled: {rawImage.enabled}", gameObject);
        Debug.Log($"  - RawImage цвет: {rawImage.color}", gameObject);
        Debug.Log($"  - Размер: {rawImage.rectTransform.sizeDelta}", gameObject);
        Debug.Log($"  - Позиция: {rawImage.rectTransform.anchoredPosition}", gameObject);
        Debug.Log($"  - Текстура: {(rawImage.texture != null ? $"{rawImage.texture.width}x{rawImage.texture.height} ({rawImage.texture.GetType().Name})" : "NULL")}", gameObject);
        Debug.Log($"  - Материал: {(rawImage.material != null ? rawImage.material.name : "NULL")}", gameObject);

        Canvas canvas = rawImage.GetComponentInParent<Canvas>();
        if (canvas != null)
        {
            Debug.Log($"  - Canvas RenderMode: {canvas.renderMode}", gameObject);
            Debug.Log($"  - Canvas SortingOrder: {canvas.sortingOrder}", gameObject);
        }
    }

    private void CreateTestTexture()
    {
        Debug.Log("[DebugMaskLinker] 🧪 Создаем СУПЕР яркую тестовую текстуру для проверки RawImage", gameObject);

        // Создаем очень яркую и контрастную тестовую текстуру
        Texture2D testTexture = new Texture2D(256, 256, TextureFormat.RGBA32, false);
        for (int y = 0; y < 256; y++)
        {
            for (int x = 0; x < 256; x++)
            {
                // Создаем крупные квадраты с максимально контрастными цветами
                bool isWhite = ((x / 64) + (y / 64)) % 2 == 0;
                Color color;

                if (isWhite)
                {
                    // Ярко-белый
                    color = new Color(1f, 1f, 1f, 1f);
                }
                else
                {
                    // Ярко-красный для максимальной видимости
                    color = new Color(1f, 0f, 0f, 1f);
                }

                testTexture.SetPixel(x, y, color);
            }
        }
        testTexture.Apply();

        rawImage.texture = testTexture;
        rawImage.color = Color.white;
        rawImage.material = null; // Убеждаемся что никакого материала нет

        Debug.Log("[DebugMaskLinker] ✅ СУПЕР яркая тестовая текстура (красно-белые квадраты) применена к RawImage БЕЗ материала", gameObject);
    }

    // Новый метод для сохранения RenderTexture в файл
    private void SaveRenderTextureToFile(RenderTexture rt, string fileName)
    {
        RenderTexture activeRenderTexture = RenderTexture.active;
        RenderTexture.active = rt;
        Texture2D tex2D = new Texture2D(rt.width, rt.height, TextureFormat.ARGB32, false);
        tex2D.ReadPixels(new Rect(0, 0, rt.width, rt.height), 0, 0);
        tex2D.Apply();
        RenderTexture.active = activeRenderTexture;

        byte[] bytes = tex2D.EncodeToPNG();
        string filePath = System.IO.Path.Combine(Application.persistentDataPath, fileName); // Используем Path.Combine для корректного пути

        // Debug.Log($"[DebugMaskLinker] Попытка сохранить текстуру в: {filePath}", gameObject);
        try
        {
            System.IO.File.WriteAllBytes(filePath, bytes);
            // Debug.Log($"[DebugMaskLinker] Текстура УСПЕШНО сохранена в {filePath}", gameObject);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[DebugMaskLinker] ОШИБКА при сохранении текстуры в {filePath}: {e.Message}\n{e.StackTrace}", gameObject);
        }
        finally // Убедимся, что tex2D уничтожается в любом случае
        {
            Object.Destroy(tex2D); // Очищаем созданную Texture2D
        }
    }

    void OnDestroy()
    {
        if (wallSegmentation != null)
        {
            wallSegmentation.OnSegmentationMaskUpdated -= UpdateMaskTexture;
            Debug.Log("[DebugMaskLinker] Успешно отписался от OnSegmentationMaskUpdated.", gameObject);
        }
    }

    void Update()
    {
        // Проверяем и исправляем размер RawImage каждые 60 кадров
        if (Time.frameCount % 60 == 0 && rawImage != null)
        {
            // Увеличиваем размер до 200x200 для лучшей видимости
            Vector2 targetSize = new Vector2(200, 200);
            if (rawImage.rectTransform.sizeDelta != targetSize)
            {
                rawImage.rectTransform.sizeDelta = targetSize;
                Debug.Log($"[DebugMaskLinker] 📐 Размер RawImage скорректирован до {targetSize}", gameObject);
            }

            // Проверяем, что объект активен
            if (!rawImage.gameObject.activeInHierarchy)
            {
                Debug.LogWarning("[DebugMaskLinker] ⚠️ RawImage GameObject неактивен!", gameObject);
            }

            // Логируем статус текстуры
            if (Time.frameCount % 300 == 0) // Каждые 5 секунд при 60 FPS
            {
                string textureInfo = rawImage.texture != null ? $"{rawImage.texture.width}x{rawImage.texture.height}" : "NULL";
                Debug.Log($"[DebugMaskLinker] 📊 Статус: Текстура={textureInfo}, Цвет={rawImage.color}, Обновлений={updateCount}", gameObject);
            }
        }
    }
}