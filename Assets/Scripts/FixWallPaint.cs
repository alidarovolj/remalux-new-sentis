using UnityEngine;
using System.Collections;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Reflection;

public class FixWallPaint : MonoBehaviour
{
      [SerializeField] private WallPaintEffect wallPaintEffect;
      [SerializeField] private WallSegmentation wallSegmentation;
      [SerializeField] private float initialBlendFactor = 0.3f; // Reduced to be more transparent
      [SerializeField] private Color initialColor = new Color(1f, 0f, 0f, 0.5f); // Added transparency to the color
      [SerializeField] private bool autoFix = true;

      private bool isFixed = false;

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

            if (autoFix)
            {
                  // Allow time for AR session to initialize
                  StartCoroutine(AutoFixAfterDelay());
            }
      }

      private IEnumerator AutoFixAfterDelay()
      {
            // First attempt quickly
            yield return new WaitForSeconds(0.5f);
            FixWallPaintEffect();

            // Additional attempts with increasing delays
            yield return new WaitForSeconds(1.0f);
            if (!isFixed)
            {
                  FixWallPaintEffect();
            }

            yield return new WaitForSeconds(2.0f);
            if (!isFixed)
            {
                  FixWallPaintEffect();
            }
      }

      public void FixWallPaintEffect()
      {
            if (wallPaintEffect == null)
            {
                  Debug.LogError("FixWallPaint: WallPaintEffect component not found!");
                  return;
            }

            Debug.Log("FixWallPaint: Attempting to fix wall paint effect...");

            // Create correct material for wall painting
            Material wallMaterial = CreateWallPaintMaterial();

            // If we created a material successfully, apply it to the WallPaintEffect
            if (wallMaterial != null)
            {
                  // Directly set the material in WallPaintEffect (accessing private field)
                  SetWallPaintMaterialDirectly(wallPaintEffect, wallMaterial);

                  // 1. Force a lower blend factor so it's more transparent
                  wallPaintEffect.SetBlendFactor(initialBlendFactor);

                  // 2. Set a color with transparency
                  wallPaintEffect.SetPaintColor(initialColor);

                  // 3. Try to disable mask if segmentation isn't ready
                  if (wallSegmentation == null || !wallSegmentation.IsModelInitialized)
                  {
                        wallPaintEffect.SetUseMask(false);
                        Debug.Log("FixWallPaint: Disabled mask as segmentation is not ready");
                  }

                  // 4. Force material update
                  wallPaintEffect.ForceUpdateMaterial();

                  // 5. Fix material textures
                  wallPaintEffect.FixMaterialTextures();

                  // 6. Force disable debug mode, then enable it to refresh rendering
                  wallPaintEffect.DisableDebugMode();
                  wallPaintEffect.FixRenderingMode();

                  isFixed = true;
                  Debug.Log("FixWallPaint: All fixes applied. Material has been properly created and applied.");
            }

            // 7. Check if we can find and fix WallPaintFeature directly
            FixWallPaintFeatureDirectly();
      }

      private Material CreateWallPaintMaterial()
      {
            // First try to create a transparent unlit material - this is likely to work best
            Shader transparentShader = Shader.Find("Universal Render Pipeline/Unlit");

            // If that doesn't exist, try alternatives
            if (transparentShader == null)
            {
                  string[] potentialShaders = new string[]
                  {
                        "Unlit/Transparent",
                        "Unlit/Color",
                        "Universal Render Pipeline/Simple Lit",
                        "Sprites/Default"
                  };

                  foreach (string shaderName in potentialShaders)
                  {
                        transparentShader = Shader.Find(shaderName);
                        if (transparentShader != null)
                        {
                              Debug.Log($"FixWallPaint: Using alternative shader: {shaderName}");
                              break;
                        }
                  }

                  // Last resort - use Hidden/InternalErrorShader
                  if (transparentShader == null)
                  {
                        transparentShader = Shader.Find("Hidden/InternalErrorShader");
                        Debug.LogWarning("FixWallPaint: Using InternalErrorShader as last resort");
                  }

                  if (transparentShader == null)
                  {
                        Debug.LogError("FixWallPaint: Could not find any suitable shader!");
                        return null;
                  }
            }

            // Create material with found shader
            Material wallMaterial = new Material(transparentShader);

            // Configure material properties
            if (wallMaterial.HasProperty("_BaseColor"))
                  wallMaterial.SetColor("_BaseColor", initialColor);
            if (wallMaterial.HasProperty("_Color"))
                  wallMaterial.SetColor("_Color", initialColor);
            if (wallMaterial.HasProperty("_PaintColor"))
                  wallMaterial.SetColor("_PaintColor", initialColor);

            // Configure blend settings
            if (wallMaterial.HasProperty("_BlendFactor"))
                  wallMaterial.SetFloat("_BlendFactor", initialBlendFactor);

            // Make sure material is transparent
            if (wallMaterial.HasProperty("_Surface"))
                  wallMaterial.SetFloat("_Surface", 1f); // 1 = Transparent in URP

            // IMPORTANT: Set ZWrite OFF to not block the camera feed
            wallMaterial.SetInt("_ZWrite", 0);

            // Set basic blend modes for transparency
            if (wallMaterial.HasProperty("_SrcBlend") && wallMaterial.HasProperty("_DstBlend"))
            {
                  wallMaterial.SetInt("_SrcBlend", (int)BlendMode.SrcAlpha);
                  wallMaterial.SetInt("_DstBlend", (int)BlendMode.OneMinusSrcAlpha);
            }

            // Try to set the blend mode to Additive if available
            if (wallMaterial.HasProperty("_BlendOp"))
            {
                  wallMaterial.SetInt("_BlendOp", (int)BlendOp.Add);
            }

            // Set render queue to transparent
            wallMaterial.renderQueue = 3000;

            // Create main texture if needed
            Texture2D mainTex = new Texture2D(4, 4, TextureFormat.RGBA32, false);
            Color32[] pixels = new Color32[16];
            for (int i = 0; i < pixels.Length; i++)
            {
                  pixels[i] = new Color32(255, 255, 255, 128); // Semi-transparent white
            }
            mainTex.SetPixels32(pixels);
            mainTex.Apply();

            // Apply main texture
            if (wallMaterial.HasProperty("_MainTex"))
                  wallMaterial.SetTexture("_MainTex", mainTex);
            if (wallMaterial.HasProperty("_BaseMap"))
                  wallMaterial.SetTexture("_BaseMap", mainTex);

            // Set cull mode to Off to render both sides
            wallMaterial.SetInt("_Cull", (int)CullMode.Off);

            Debug.Log($"FixWallPaint: Created wall paint material with shader {transparentShader.name}, blendFactor={initialBlendFactor}");
            return wallMaterial;
      }

      // Use reflection to set the material directly in WallPaintEffect (private field)
      private void SetWallPaintMaterialDirectly(WallPaintEffect effect, Material material)
      {
            try
            {
                  // Get the field
                  var field = typeof(WallPaintEffect).GetField("wallPaintMaterial",
                                    BindingFlags.NonPublic | BindingFlags.Instance);

                  if (field != null)
                  {
                        // Set the field value directly
                        field.SetValue(effect, material);
                        Debug.Log("FixWallPaint: Material set successfully through reflection");
                  }
                  else
                  {
                        Debug.LogWarning("FixWallPaint: Could not find wallPaintMaterial field with reflection");
                  }
            }
            catch (System.Exception ex)
            {
                  Debug.LogError($"FixWallPaint: Error setting material: {ex.Message}");
            }
      }

      private void FixWallPaintFeatureDirectly()
      {
            // Try to find WallPaintFeature
            UniversalRenderPipelineAsset urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urpAsset == null) return;

            // Get renderer data through reflection
            var fieldInfo = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList",
                                            BindingFlags.NonPublic | BindingFlags.Instance);

            if (fieldInfo == null)
            {
                  fieldInfo = typeof(UniversalRenderPipelineAsset).GetField("m_RendererData",
                                              BindingFlags.NonPublic | BindingFlags.Instance);
                  if (fieldInfo == null) return;
            }

            var rendererDataList = fieldInfo.GetValue(urpAsset) as System.Collections.IList;
            if (rendererDataList == null || rendererDataList.Count == 0) return;

            // Check all renderers
            foreach (var item in rendererDataList)
            {
                  ScriptableRendererData rendererData = item as ScriptableRendererData;
                  if (rendererData == null) continue;

                  // Get features through reflection
                  var featuresField = typeof(ScriptableRendererData).GetField("m_RendererFeatures",
                                              BindingFlags.NonPublic | BindingFlags.Instance);

                  if (featuresField == null) continue;

                  var features = featuresField.GetValue(rendererData) as System.Collections.IList;
                  if (features == null) continue;

                  // Look for WallPaintFeature
                  foreach (var feature in features)
                  {
                        if (feature is WallPaintFeature wallPaintFeature)
                        {
                              Debug.Log("FixWallPaint: Found WallPaintFeature, applying direct fixes");

                              // Create and set a proper material
                              Material material = CreateWallPaintMaterial();
                              if (material != null)
                              {
                                    wallPaintFeature.SetPassMaterial(material);
                                    Debug.Log("FixWallPaint: Applied better material directly to WallPaintFeature");
                              }

                              // Ensure fallback is enabled
                              wallPaintFeature.SetUseFallbackMaterial(true);

                              // Adjust transparency limit
                              wallPaintFeature.SetMinTransparency(0.05f);

                              break;
                        }
                  }
            }
      }
}