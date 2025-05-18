using UnityEngine;
using UnityEngine.UI;

public class DebugMaskLinker : MonoBehaviour
{
    void Start()
    {
        RawImage rawImage = GetComponent<RawImage>();
        if (rawImage == null)
        {
            Debug.LogError("[DebugMaskLinker] Компонент RawImage не найден на этом GameObject!", gameObject);
            return;
        }

        if (ARManagerInitializer2.Instance != null)
        {
            ARManagerInitializer2.Instance.УстановитьОтображениеМаскиUI(rawImage);
            Debug.Log("[DebugMaskLinker] RawImage успешно передан в ARManagerInitializer2.", gameObject);
        }
        else
        {
            Debug.LogError("[DebugMaskLinker] Не удалось найти экземпляр ARManagerInitializer2.Instance!", gameObject);
        }
    }
} 