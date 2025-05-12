using UnityEngine;
using UnityEngine.UI;

public class WallPaintControlPanel : MonoBehaviour
{
      [SerializeField] private GameObject controlPanel;
      [SerializeField] private Button togglePanelButton;

      [Header("Controls")]
      [SerializeField] private Slider opacitySlider;
      [SerializeField] private Toggle useMaskToggle;
      [SerializeField] private Button redButton;
      [SerializeField] private Button greenButton;
      [SerializeField] private Button blueButton;
      [SerializeField] private Button yellowButton;
      [SerializeField] private Button whiteButton;
      [SerializeField] private Button fixButton;

      private WallPaintEffect wallPaintEffect;
      private bool isPanelVisible = false;

      void Start()
      {
            // Find components
            wallPaintEffect = FindObjectOfType<WallPaintEffect>();

            // Setup panel toggle
            if (togglePanelButton != null)
            {
                  togglePanelButton.onClick.AddListener(TogglePanel);
            }

            // Setup controls
            if (opacitySlider != null && wallPaintEffect != null)
            {
                  opacitySlider.value = wallPaintEffect.GetBlendFactor();
                  opacitySlider.onValueChanged.AddListener(OnOpacityChanged);
            }

            if (useMaskToggle != null && wallPaintEffect != null)
            {
                  useMaskToggle.isOn = true; // Default to on
                  useMaskToggle.onValueChanged.AddListener(OnUseMaskChanged);
            }

            // Color buttons
            if (redButton != null)
                  redButton.onClick.AddListener(() => SetColor(Color.red));

            if (greenButton != null)
                  greenButton.onClick.AddListener(() => SetColor(Color.green));

            if (blueButton != null)
                  blueButton.onClick.AddListener(() => SetColor(Color.blue));

            if (yellowButton != null)
                  yellowButton.onClick.AddListener(() => SetColor(Color.yellow));

            if (whiteButton != null)
                  whiteButton.onClick.AddListener(() => SetColor(Color.white));

            // Fix button
            if (fixButton != null)
            {
                  fixButton.onClick.AddListener(FixWallPaint);
            }

            // Hide panel at start
            if (controlPanel != null)
            {
                  controlPanel.SetActive(false);
            }
      }

      private void TogglePanel()
      {
            if (controlPanel != null)
            {
                  isPanelVisible = !isPanelVisible;
                  controlPanel.SetActive(isPanelVisible);
            }
      }

      private void OnOpacityChanged(float value)
      {
            if (wallPaintEffect != null)
            {
                  wallPaintEffect.SetBlendFactor(value);
                  wallPaintEffect.ForceUpdateMaterial();
            }
      }

      private void OnUseMaskChanged(bool value)
      {
            if (wallPaintEffect != null)
            {
                  wallPaintEffect.SetUseMask(value);
                  wallPaintEffect.ForceUpdateMaterial();
            }
      }

      private void SetColor(Color color)
      {
            if (wallPaintEffect != null)
            {
                  wallPaintEffect.SetPaintColor(color);
                  wallPaintEffect.ForceUpdateMaterial();
            }
      }

      private void FixWallPaint()
      {
            // Find the FixWallPaint component and use it
            FixWallPaint fixer = FindObjectOfType<FixWallPaint>();
            if (fixer != null)
            {
                  fixer.FixWallPaintEffect();
            }
            else
            {
                  // If no fixer exists, create one
                  GameObject fixerObj = new GameObject("WallPaintFixer");
                  fixer = fixerObj.AddComponent<FixWallPaint>();
                  fixer.FixWallPaintEffect();
            }
      }
}