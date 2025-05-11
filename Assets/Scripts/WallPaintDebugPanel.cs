using UnityEngine;

[RequireComponent(typeof(WallPaintEffect))]
public class WallPaintDebugPanel : MonoBehaviour
{
      private WallPaintEffect wallPaintEffect;
      private bool showDebugPanel = true;
      private bool showAdvanced = false;
      private Color paintColor = Color.magenta;

      void Start()
      {
            wallPaintEffect = GetComponent<WallPaintEffect>();
            if (wallPaintEffect == null)
            {
                  Debug.LogError("WallPaintDebugPanel: No WallPaintEffect component found!");
                  enabled = false;
            }
      }

      void OnGUI()
      {
            if (!showDebugPanel) return;

            // Create a panel in the top-right corner
            GUILayout.BeginArea(new Rect(Screen.width - 230, 10, 220, showAdvanced ? 700 : 360));
            GUILayout.BeginVertical("box");

            GUILayout.Label("Wall Paint Debug", GUI.skin.box);

            // Toggle panel visibility
            if (GUILayout.Button(showAdvanced ? "Hide Advanced Options" : "Show Advanced Options"))
            {
                  showAdvanced = !showAdvanced;
            }

            GUILayout.Space(10);
            GUILayout.Label("Quick Fixes:", GUI.skin.box);

            // Fix rendering mode button
            if (GUILayout.Button("Fix Rendering Mode"))
            {
                  wallPaintEffect.FixRenderingMode();
            }

            // Toggle debug visualization
            if (GUILayout.Button(IsDebugOverlayEnabled() ? "Disable Checkerboard" : "Enable Checkerboard"))
            {
                  ToggleDebugOverlay();
            }

            // Toggle mask usage
            if (GUILayout.Button(IsMaskEnabled() ? "Disable Mask" : "Enable Mask"))
            {
                  wallPaintEffect.SetUseMaskMode(!IsMaskEnabled());
            }

            // Force update material
            if (GUILayout.Button("Force Update Material"))
            {
                  wallPaintEffect.ForceUpdateMaterial();
            }

            // Color adjustments
            GUILayout.Space(10);
            GUILayout.Label("Color Settings:", GUI.skin.box);

            // Red color button
            if (GUILayout.Button("Red (30%)"))
            {
                  wallPaintEffect.SetColorAndOpacity(Color.red, 0.3f);
            }

            // Green color button
            if (GUILayout.Button("Green (30%)"))
            {
                  wallPaintEffect.SetColorAndOpacity(Color.green, 0.3f);
            }

            // Blue color button
            if (GUILayout.Button("Blue (30%)"))
            {
                  wallPaintEffect.SetColorAndOpacity(Color.blue, 0.3f);
            }

            // Magenta color button
            if (GUILayout.Button("Magenta (30%)"))
            {
                  wallPaintEffect.SetColorAndOpacity(Color.magenta, 0.3f);
            }

            // Advanced options
            if (showAdvanced)
            {
                  GUILayout.Space(10);
                  GUILayout.Label("Advanced Settings:", GUI.skin.box);

                  // Blend factor slider
                  GUILayout.BeginHorizontal();
                  GUILayout.Label("Blend: ", GUILayout.Width(50));
                  float blendFactor = wallPaintEffect.GetBlendFactor();
                  float newBlendFactor = GUILayout.HorizontalSlider(blendFactor, 0.0f, 1.0f);
                  if (newBlendFactor != blendFactor)
                  {
                        wallPaintEffect.SetBlendFactor(newBlendFactor);
                  }
                  GUILayout.Label(newBlendFactor.ToString("F2"), GUILayout.Width(40));
                  GUILayout.EndHorizontal();

                  // Color picker
                  GUILayout.BeginHorizontal();
                  GUILayout.Label("R: ", GUILayout.Width(20));
                  paintColor.r = GUILayout.HorizontalSlider(paintColor.r, 0f, 1f);
                  GUILayout.EndHorizontal();

                  GUILayout.BeginHorizontal();
                  GUILayout.Label("G: ", GUILayout.Width(20));
                  paintColor.g = GUILayout.HorizontalSlider(paintColor.g, 0f, 1f);
                  GUILayout.EndHorizontal();

                  GUILayout.BeginHorizontal();
                  GUILayout.Label("B: ", GUILayout.Width(20));
                  paintColor.b = GUILayout.HorizontalSlider(paintColor.b, 0f, 1f);
                  GUILayout.EndHorizontal();

                  if (GUILayout.Button("Apply Custom Color"))
                  {
                        wallPaintEffect.SetPaintColor(paintColor);
                  }

                  GUILayout.Space(10);
                  GUILayout.Label("Diagnostic Actions:", GUI.skin.box);

                  if (GUILayout.Button("Log Debug Info"))
                  {
                        Debug.Log($"Current Color: {wallPaintEffect.GetPaintColor()}, Blend: {wallPaintEffect.GetBlendFactor()}");
                        wallPaintEffect.ForceUpdateMaterial();
                  }

                  if (GUILayout.Button("Fix Material Textures"))
                  {
                        wallPaintEffect.FixMaterialTextures();
                  }

                  if (GUILayout.Button("Reinitialize Material"))
                  {
                        wallPaintEffect.SetPaintColor(wallPaintEffect.GetPaintColor());
                        wallPaintEffect.SetBlendFactor(wallPaintEffect.GetBlendFactor());
                        wallPaintEffect.ForceUpdateMaterial();
                        Debug.Log("Material reinitialized");
                  }
            }

            GUILayout.Space(10);
            if (GUILayout.Button("Hide Debug Panel"))
            {
                  showDebugPanel = false;
            }

            GUILayout.EndVertical();
            GUILayout.EndArea();

            // Small button to re-show panel if hidden
            if (!showDebugPanel && GUI.Button(new Rect(Screen.width - 120, 10, 110, 30), "Show Debug"))
            {
                  showDebugPanel = true;
            }
      }

      private bool IsDebugOverlayEnabled()
      {
            var material = wallPaintEffect.GetMaterial();
            if (material != null && material.HasProperty("_DebugOverlay"))
            {
                  return material.IsKeywordEnabled("DEBUG_OVERLAY");
            }
            return false;
      }

      private void ToggleDebugOverlay()
      {
            var material = wallPaintEffect.GetMaterial();
            if (material != null && material.HasProperty("_DebugOverlay"))
            {
                  if (material.IsKeywordEnabled("DEBUG_OVERLAY"))
                  {
                        wallPaintEffect.DisableDebugMode();
                  }
                  else
                  {
                        material.EnableKeyword("DEBUG_OVERLAY");
                        wallPaintEffect.ForceUpdateMaterial();
                  }
            }
      }

      private bool IsMaskEnabled()
      {
            var material = wallPaintEffect.GetMaterial();
            if (material != null)
            {
                  return material.IsKeywordEnabled("USE_MASK");
            }
            return false;
      }
}