using UnityEngine;
using UnityEngine.UI;

// [RequireComponent(typeof(RawImage))] // Оставляем, но можно и убрать, если RawImage всегда есть
public class DebugMaskLinker : MonoBehaviour
{
    private RawImage rawImage;
    private WallSegmentation wallSegmentation;

    void Start()
    {
        rawImage = GetComponent<RawImage>();
        if (rawImage == null)
        {
            Debug.LogError("[DebugMaskLinker] Компонент RawImage не найден на этом GameObject!", gameObject);
            enabled = false; // Отключаем компонент, если нет RawImage
            return;
        }

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
        if (rawImage != null && mask != null)
        {
            if (mask.IsCreated())
            {
                rawImage.texture = mask;
                rawImage.color = Color.white; // Убедимся, что RawImage не прозрачный и полностью видим
                // Debug.Log($"[DebugMaskLinker] Текстура маски ({mask.width}x{mask.height}) обновлена на RawImage.", gameObject);
            }
            else
            {
                Debug.LogWarning($"[DebugMaskLinker] Получена маска, но она не создана (IsCreated() == false). Текстура RawImage не обновлена.", gameObject);
            }
        }
        else
        {
            if (rawImage == null) Debug.LogWarning("[DebugMaskLinker] RawImage is null в UpdateMaskTexture.", gameObject);
            if (mask == null) Debug.LogWarning("[DebugMaskLinker] Получена null маска в UpdateMaskTexture.", gameObject);
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