using UnityEngine;
using UnityEngine.UI;

// [RequireComponent(typeof(RawImage))] // Оставляем, но можно и убрать, если RawImage всегда есть
public class DebugMaskLinker : MonoBehaviour
{
    private RawImage rawImage;
    private WallSegmentation wallSegmentation;
    private int updateCounter = 0;
    private bool hasSavedOnce = false;
    private const int SAVE_AFTER_N_UPDATES = 5; // Уменьшено для быстрой проверки

    void Start()
    {
        rawImage = GetComponent<RawImage>();
        if (rawImage == null)
        {
            Debug.LogError("[DebugMaskLinker] Компонент RawImage не найден на этом GameObject!", gameObject);
            enabled = false; // Отключаем компонент, если нет RawImage
            return;
        }

        // ИСПРАВЛЕНО: Отключаем Raycast Target для предотвращения блокировки касаний по экрану
        rawImage.raycastTarget = false;
        Debug.Log("[DebugMaskLinker] Raycast Target отключен для предотвращения блокировки AR-взаимодействия.", gameObject);

        wallSegmentation = FindObjectOfType<WallSegmentation>();
        if (wallSegmentation == null)
        {
            Debug.LogError("[DebugMaskLinker] Компонент WallSegmentation не найден в сцене!", gameObject);
            enabled = false; // Отключаем компонент, если нет WallSegmentation
            return;
        }

        // Подписываемся на событие обновления маски
        wallSegmentation.OnSegmentationMaskUpdated += UpdateMaskTexture;
        Debug.Log("[DebugMaskLinker] Успешно подписался на OnSegmentationMaskUpdated от WallSegmentation.", gameObject);

        // Попытка установить начальную маску, если она уже есть и модель инициализирована
        // Это полезно, если WallSegmentation инициализируется раньше, чем DebugMaskLinker
        if (wallSegmentation.IsModelInitialized && wallSegmentation.segmentationMaskTexture != null && wallSegmentation.segmentationMaskTexture.IsCreated())
        {
            Debug.Log("[DebugMaskLinker] Попытка установить начальную маску.", gameObject);
            UpdateMaskTexture(wallSegmentation.segmentationMaskTexture);
        }
        else
        {
            Debug.LogWarning("[DebugMaskLinker] Начальная маска не доступна или модель не инициализирована при старте DebugMaskLinker. Ожидание события...", gameObject);
        }
    }

    private void UpdateMaskTexture(RenderTexture mask)
    {
        if (rawImage == null)
        {
            Debug.LogError("[DebugMaskLinker] RawImage компонент не найден (null) в UpdateMaskTexture. Невозможно обновить текстуру.", gameObject);
            return;
        }

        if (mask == null)
        {
            Debug.LogWarning("[DebugMaskLinker] Получена null маска в UpdateMaskTexture. Текстура RawImage не будет обновлена.", gameObject);
            // Можно сделать RawImage черным или скрыть его, если маска null
            // rawImage.texture = null;
            // rawImage.color = Color.clear; // или new Color(0,0,0,0) для полной прозрачности
            return;
        }

        if (mask.IsCreated())
        {
            Debug.Log($"[DebugMaskLinker] Обновление текстуры RawImage: Маска ({mask.width}x{mask.height}, формат: {mask.format}, isReadable: {mask.isReadable}), RawImage InstanceID: {rawImage.GetInstanceID()}", gameObject);
            rawImage.texture = mask;
            rawImage.color = Color.white; // Устанавливаем белый цвет, чтобы убрать влияние альфа-канала самого RawImage
            Debug.Log($"[DebugMaskLinker] Текстура RawImage назначена. Текущая текстура RawImage: {(rawImage.texture == null ? "null" : rawImage.texture.name + " (" + rawImage.texture.GetInstanceID() + ")")}", gameObject);

            updateCounter++;

            // Отладочный лог перед условием сохранения
            Debug.Log($"[DebugMaskLinker] Проверка условия сохранения: hasSavedOnce = {hasSavedOnce}, updateCounter = {updateCounter}, SAVE_AFTER_N_UPDATES = {SAVE_AFTER_N_UPDATES}", gameObject);

            // Автоматическое сохранение маски для отладки
            if (!hasSavedOnce && updateCounter >= SAVE_AFTER_N_UPDATES)
            {
                Debug.Log($"[DebugMaskLinker] Достигнуто {SAVE_AFTER_N_UPDATES} обновлений ({updateCounter}). Автоматическое сохранение маски DebugMaskOutput_Auto.png...", gameObject);
                SaveRenderTextureToFile(mask, "DebugMaskOutput_Auto.png");
                hasSavedOnce = true; // Предотвращаем повторное сохранение
            }
        }
        else
        {
            Debug.LogWarning($"[DebugMaskLinker] Получена маска ({mask.width}x{mask.height}), но она не создана (IsCreated() == false). Текстура RawImage не обновлена.", gameObject);
        }
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

        Debug.Log($"[DebugMaskLinker] Попытка сохранить текстуру в: {filePath}", gameObject);
        try
        {
            System.IO.File.WriteAllBytes(filePath, bytes);
            Debug.Log($"[DebugMaskLinker] Текстура УСПЕШНО сохранена в {filePath}", gameObject);
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
}