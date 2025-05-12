using UnityEngine;
using UnityEngine.UI;

public class WallPaintDebugUI : MonoBehaviour
{
      [SerializeField] private GameObject debugPanel;
      [SerializeField] private Button toggleDebugButton;
      [SerializeField] private WallPaintDebugger debugger;

      [Header("Color Presets")]
      [SerializeField] private Button redColorButton;
      [SerializeField] private Button greenColorButton;
      [SerializeField] private Button blueColorButton;
      [SerializeField] private Button yellowColorButton;
      [SerializeField] private Button whiteColorButton;

      private bool panelVisible = false;

      void Start()
      {
            if (debugger == null)
            {
                  debugger = FindObjectOfType<WallPaintDebugger>();
            }

            if (toggleDebugButton != null)
            {
                  toggleDebugButton.onClick.AddListener(ToggleDebugPanel);
            }

            // Hide panel at startup
            if (debugPanel != null)
            {
                  debugPanel.SetActive(false);
            }

            // Setup color buttons
            if (redColorButton != null)
                  redColorButton.onClick.AddListener(() => SetColor(Color.red));

            if (greenColorButton != null)
                  greenColorButton.onClick.AddListener(() => SetColor(Color.green));

            if (blueColorButton != null)
                  blueColorButton.onClick.AddListener(() => SetColor(Color.blue));

            if (yellowColorButton != null)
                  yellowColorButton.onClick.AddListener(() => SetColor(Color.yellow));

            if (whiteColorButton != null)
                  whiteColorButton.onClick.AddListener(() => SetColor(Color.white));
      }

      private void ToggleDebugPanel()
      {
            if (debugPanel != null)
            {
                  panelVisible = !panelVisible;
                  debugPanel.SetActive(panelVisible);
            }
      }

      private void SetColor(Color color)
      {
            if (debugger != null)
            {
                  debugger.SetColor(color);
            }
            else
            {
                  WallPaintEffect effect = FindObjectOfType<WallPaintEffect>();
                  if (effect != null)
                  {
                        effect.SetPaintColor(color);
                        effect.ForceUpdateMaterial();
                  }
            }
      }
}