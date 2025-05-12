using UnityEngine;
using UnityEngine.UI;
using UnityEngine.Rendering;

public class EmergencyUIFix : MonoBehaviour
{
      [SerializeField] private Button fixBlackScreenButton;
      [SerializeField] private Button toggleCameraButton;
      [SerializeField] private Button decreaseOpacityButton;
      [SerializeField] private Button resetCameraButton;
      [SerializeField] private Text statusText;

      private WallPaintEffect wallPaintEffect;
      private Camera mainCamera;
      private float defaultNearClipPlane = 0.1f;
      private float currentOpacity = 0.3f;

      void Start()
      {
            wallPaintEffect = FindObjectOfType<WallPaintEffect>();
            mainCamera = Camera.main;

            // Setup buttons
            if (fixBlackScreenButton != null)
            {
                  fixBlackScreenButton.onClick.AddListener(FixBlackScreen);
            }

            if (toggleCameraButton != null)
            {
                  toggleCameraButton.onClick.AddListener(ToggleCameraClipPlane);
            }

            if (decreaseOpacityButton != null)
            {
                  decreaseOpacityButton.onClick.AddListener(DecreaseOpacity);
            }

            if (resetCameraButton != null)
            {
                  resetCameraButton.onClick.AddListener(ResetCamera);
            }

            if (statusText != null)
            {
                  UpdateStatus();
            }
      }

      private void FixBlackScreen()
      {
            if (wallPaintEffect != null)
            {
                  // Emergency black screen fix
                  EnsureWallPaintFixerExists();

                  // Directly reduce blend factor
                  currentOpacity = 0.2f;
                  wallPaintEffect.SetBlendFactor(currentOpacity);
                  wallPaintEffect.SetUseMask(false);
                  wallPaintEffect.ForceUpdateMaterial();

                  // First try disabling the effect completely, then gradually bring it back
                  Material material = wallPaintEffect.GetMaterial();
                  if (material != null)
                  {
                        material.SetInt("_ZWrite", 0); // Ensure ZWrite is OFF

                        // Try to modify render mode
                        if (material.HasProperty("_SrcBlend") && material.HasProperty("_DstBlend"))
                        {
                              material.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                              material.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
                        }

                        // Set to additive blending if possible
                        if (material.HasProperty("_BlendOp"))
                        {
                              material.SetInt("_BlendOp", (int)BlendOp.Add);
                        }
                  }

                  if (statusText != null)
                  {
                        statusText.text = "Applied emergency fix for black screen";
                  }
            }
      }

      private void ToggleCameraClipPlane()
      {
            if (mainCamera != null)
            {
                  // Sometimes the near clip plane causes black screens in AR
                  if (mainCamera.nearClipPlane == defaultNearClipPlane)
                  {
                        // Change to a very small value
                        mainCamera.nearClipPlane = 0.01f;
                        if (statusText != null)
                        {
                              statusText.text = "Reduced near clip plane to 0.01";
                        }
                  }
                  else
                  {
                        // Reset to default
                        mainCamera.nearClipPlane = defaultNearClipPlane;
                        if (statusText != null)
                        {
                              statusText.text = "Reset near clip plane to " + defaultNearClipPlane;
                        }
                  }
            }
      }

      private void DecreaseOpacity()
      {
            if (wallPaintEffect != null)
            {
                  // Decrease opacity by 0.1 each time
                  currentOpacity = Mathf.Max(0.05f, currentOpacity - 0.1f);
                  wallPaintEffect.SetBlendFactor(currentOpacity);
                  wallPaintEffect.ForceUpdateMaterial();

                  if (statusText != null)
                  {
                        statusText.text = "Decreased opacity to " + currentOpacity.ToString("F2");
                  }
            }
      }

      private void ResetCamera()
      {
            if (mainCamera != null)
            {
                  mainCamera.clearFlags = CameraClearFlags.Skybox;
                  mainCamera.backgroundColor = Color.black;
                  mainCamera.nearClipPlane = defaultNearClipPlane;

                  if (statusText != null)
                  {
                        statusText.text = "Reset camera settings";
                  }
            }
      }

      private void UpdateStatus()
      {
            if (statusText != null)
            {
                  string status = "Status: ";

                  if (wallPaintEffect != null)
                  {
                        Material mat = wallPaintEffect.GetMaterial();
                        status += "WallPaintEffect OK, ";
                        status += mat != null ? "Material OK" : "No material";
                  }
                  else
                  {
                        status += "WallPaintEffect missing";
                  }

                  if (mainCamera != null)
                  {
                        status += ", Camera OK";
                  }
                  else
                  {
                        status += ", Camera missing";
                  }

                  statusText.text = status;
            }
      }

      private void EnsureWallPaintFixerExists()
      {
            // Check if FixWallPaint component exists
            FixWallPaint fixer = FindObjectOfType<FixWallPaint>();
            if (fixer == null)
            {
                  // Create the fixer component
                  GameObject fixerObject = new GameObject("WallPaintFixer");
                  fixer = fixerObject.AddComponent<FixWallPaint>();
            }

            // Run the fix
            fixer.FixWallPaintEffect();
      }
}