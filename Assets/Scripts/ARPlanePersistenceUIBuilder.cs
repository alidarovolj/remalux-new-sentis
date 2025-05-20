using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Utility to programmatically create a UI for the AR Plane Persistence system.
/// This can be used at runtime to create a UI if one doesn't already exist.
/// </summary>
public class ARPlanePersistenceUIBuilder : MonoBehaviour
{
      [SerializeField] private ARManagerInitializer2 arManagerInitializer;
      [SerializeField] private ARPlaneConfigurator planeConfigurator;

      [Header("UI Settings")]
      [SerializeField] private Font uiFont;
      [SerializeField] private Color buttonColor = new Color(0.2f, 0.6f, 1.0f);
      [SerializeField] private Color textColor = Color.white;

      public GameObject BuildUI()
      {
            // Create a canvas if one doesn't exist
            Canvas canvas = FindObjectOfType<Canvas>();
            if (canvas == null)
            {
                  GameObject canvasObj = new GameObject("AR UI Canvas");
                  canvas = canvasObj.AddComponent<Canvas>();
                  canvas.renderMode = RenderMode.ScreenSpaceOverlay;

                  // Add canvas scaler
                  CanvasScaler scaler = canvasObj.AddComponent<CanvasScaler>();
                  scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                  scaler.referenceResolution = new Vector2(1920, 1080);

                  // Add graphic raycaster for button interaction
                  canvasObj.AddComponent<GraphicRaycaster>();
            }

            // Create a UI container
            GameObject uiPanel = new GameObject("AR Plane Persistence UI");
            uiPanel.transform.SetParent(canvas.transform, false);

            RectTransform panelRect = uiPanel.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0, 0);
            panelRect.anchorMax = new Vector2(1, 0.2f);
            panelRect.offsetMin = new Vector2(20, 20);
            panelRect.offsetMax = new Vector2(-20, 0);

            // Add the main UI script
            ARPlanePersistenceUI ui = uiPanel.AddComponent<ARPlanePersistenceUI>();

            // Create "Save Planes" button
            GameObject saveButton = CreateButton("Save Planes", new Vector2(0.25f, 0.5f));
            saveButton.transform.SetParent(panelRect, false);

            // Create "Reset" button
            GameObject resetButton = CreateButton("Reset Planes", new Vector2(0.75f, 0.5f));
            resetButton.transform.SetParent(panelRect, false);

            // Create status text
            GameObject statusTextObj = new GameObject("Status Text");
            statusTextObj.transform.SetParent(panelRect, false);

            RectTransform statusRect = statusTextObj.AddComponent<RectTransform>();
            statusRect.anchorMin = new Vector2(0, 0.8f);
            statusRect.anchorMax = new Vector2(1, 1);
            statusRect.offsetMin = Vector2.zero;
            statusRect.offsetMax = Vector2.zero;

            TextMeshProUGUI statusText = statusTextObj.AddComponent<TextMeshProUGUI>();
            statusText.text = "Saved Planes: 0";
            statusText.color = textColor;
            statusText.fontSize = 24;
            statusText.alignment = TextAlignmentOptions.Center;

            // Setup references
            ui.arManagerInitializer = arManagerInitializer;
            ui.planeConfigurator = planeConfigurator;
            ui.saveCurrentPlanesButton = saveButton.GetComponent<Button>();
            ui.resetSavedPlanesButton = resetButton.GetComponent<Button>();
            ui.statusText = statusText;

            return uiPanel;
      }

      private GameObject CreateButton(string text, Vector2 anchorCenter)
      {
            GameObject buttonObj = new GameObject(text + " Button");

            RectTransform rect = buttonObj.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(anchorCenter.x - 0.2f, 0.1f);
            rect.anchorMax = new Vector2(anchorCenter.x + 0.2f, 0.7f);
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;

            Image image = buttonObj.AddComponent<Image>();
            image.color = buttonColor;

            Button button = buttonObj.AddComponent<Button>();
            ColorBlock colors = button.colors;
            colors.highlightedColor = new Color(buttonColor.r * 1.2f, buttonColor.g * 1.2f, buttonColor.b * 1.2f);
            colors.pressedColor = new Color(buttonColor.r * 0.8f, buttonColor.g * 0.8f, buttonColor.b * 0.8f);
            button.colors = colors;

            // Add text
            GameObject textObj = new GameObject("Text");
            textObj.transform.SetParent(buttonObj.transform, false);

            RectTransform textRect = textObj.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            TextMeshProUGUI buttonText = textObj.AddComponent<TextMeshProUGUI>();
            buttonText.text = text;
            buttonText.color = textColor;
            buttonText.fontSize = 20;
            buttonText.alignment = TextAlignmentOptions.Center;

            return buttonObj;
      }

      /// <summary>
      /// Create UI and attach it to scene
      /// </summary>
      [ContextMenu("Create UI")]
      public void CreateUI()
      {
            GameObject ui = BuildUI();
            Debug.Log("Created AR Plane Persistence UI: " + ui.name);
      }
}