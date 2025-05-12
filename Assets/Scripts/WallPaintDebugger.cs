using UnityEngine;
using UnityEngine.UI;
using System.Collections;

public class WallPaintDebugger : MonoBehaviour
{
      [SerializeField] private WallPaintEffect wallPaintEffect;
      [SerializeField] private WallSegmentation wallSegmentation;
      [SerializeField] private Text debugText;

      [Header("Testing Controls")]
      [SerializeField] private Button increaseBlendButton;
      [SerializeField] private Button decreaseBlendButton;
      [SerializeField] private Button toggleMaskButton;
      [SerializeField] private Button resetButton;
      [SerializeField] private Slider blendSlider;
      [SerializeField] private Toggle debugOverlayToggle;

      private bool isInitialized = false;
      private float currentBlend = 0.5f;
      private bool useMask = true;
      private Color currentColor = Color.red;

      private void Start()
      {
            if (wallPaintEffect == null)
            {
                  wallPaintEffect = FindObjectOfType<WallPaintEffect>();
            }

            if (wallSegmentation == null)
            {
                  wallSegmentation = FindObjectOfType<WallSegmentation>();
            }

            StartCoroutine(DelayedInit());
      }

      private IEnumerator DelayedInit()
      {
            // Give time for AR session to initialize
            yield return new WaitForSeconds(2.0f);

            if (wallPaintEffect != null)
            {
                  currentBlend = wallPaintEffect.GetBlendFactor();
                  currentColor = wallPaintEffect.GetPaintColor();
                  useMask = true; // Default
            }

            if (blendSlider != null)
            {
                  blendSlider.value = currentBlend;
                  blendSlider.onValueChanged.AddListener(OnBlendSliderChanged);
            }

            if (increaseBlendButton != null)
            {
                  increaseBlendButton.onClick.AddListener(IncreaseBlend);
            }

            if (decreaseBlendButton != null)
            {
                  decreaseBlendButton.onClick.AddListener(DecreaseBlend);
            }

            if (toggleMaskButton != null)
            {
                  toggleMaskButton.onClick.AddListener(ToggleMask);
            }

            if (resetButton != null)
            {
                  resetButton.onClick.AddListener(ResetEffect);
            }

            if (debugOverlayToggle != null)
            {
                  debugOverlayToggle.onValueChanged.AddListener(ToggleDebugOverlay);
            }

            isInitialized = true;
            UpdateDebugText();
      }

      private void Update()
      {
            if (isInitialized && Time.frameCount % 30 == 0)
            {
                  UpdateDebugText();
            }
      }

      public void IncreaseBlend()
      {
            if (wallPaintEffect != null)
            {
                  currentBlend = Mathf.Clamp01(currentBlend + 0.1f);
                  wallPaintEffect.SetBlendFactor(currentBlend);
                  if (blendSlider != null) blendSlider.value = currentBlend;
                  UpdateDebugText();
            }
      }

      public void DecreaseBlend()
      {
            if (wallPaintEffect != null)
            {
                  currentBlend = Mathf.Clamp01(currentBlend - 0.1f);
                  wallPaintEffect.SetBlendFactor(currentBlend);
                  if (blendSlider != null) blendSlider.value = currentBlend;
                  UpdateDebugText();
            }
      }

      public void OnBlendSliderChanged(float value)
      {
            if (wallPaintEffect != null)
            {
                  currentBlend = value;
                  wallPaintEffect.SetBlendFactor(currentBlend);
                  UpdateDebugText();
            }
      }

      public void ToggleMask()
      {
            if (wallPaintEffect != null)
            {
                  useMask = !useMask;
                  wallPaintEffect.SetUseMask(useMask);
                  UpdateDebugText();
            }
      }

      public void SetColor(Color color)
      {
            if (wallPaintEffect != null)
            {
                  currentColor = color;
                  wallPaintEffect.SetPaintColor(color);
                  UpdateDebugText();
            }
      }

      public void ResetEffect()
      {
            if (wallPaintEffect != null)
            {
                  // Force recreate material
                  wallPaintEffect.SetBlendFactor(0.7f);
                  wallPaintEffect.SetPaintColor(Color.red);
                  wallPaintEffect.SetUseMask(true);
                  wallPaintEffect.ForceUpdateMaterial();
                  wallPaintEffect.FixMaterialTextures();

                  // Update local values
                  currentBlend = 0.7f;
                  currentColor = Color.red;
                  useMask = true;

                  if (blendSlider != null) blendSlider.value = currentBlend;

                  UpdateDebugText();
            }
      }

      public void ToggleDebugOverlay(bool enabled)
      {
            if (wallPaintEffect != null)
            {
                  if (enabled)
                  {
                        wallPaintEffect.FixRenderingMode();
                  }
                  else
                  {
                        wallPaintEffect.DisableDebugMode();
                  }
            }
      }

      private void UpdateDebugText()
      {
            if (debugText == null) return;

            string status = "WallPaint Debug Info:\n";

            // WallPaintEffect status
            status += "Effect: " + (wallPaintEffect != null ? (wallPaintEffect.IsReady() ? "Ready" : "Not Ready") : "Not Found") + "\n";

            // Material info
            if (wallPaintEffect != null)
            {
                  Material mat = wallPaintEffect.GetMaterial();
                  status += "Material: " + (mat != null ? mat.shader.name : "None") + "\n";
                  status += $"Blend: {currentBlend:F2}, Mask: {useMask}\n";
                  status += $"Color: ({currentColor.r:F1}, {currentColor.g:F1}, {currentColor.b:F1})\n";
            }

            // Segmentation status
            if (wallSegmentation != null)
            {
                  status += "Segmentation: " + (wallSegmentation.IsModelInitialized ? "Ready" : "Not Ready") + "\n";
                  status += "Mask Texture: " + (wallSegmentation.segmentationMaskTexture != null ?
                      $"{wallSegmentation.segmentationMaskTexture.width}x{wallSegmentation.segmentationMaskTexture.height}" : "None") + "\n";
            }
            else
            {
                  status += "Segmentation: Not Found\n";
            }

            debugText.text = status;
      }
}