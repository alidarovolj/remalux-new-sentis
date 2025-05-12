using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Reflection;

/// <summary>
/// Emergency script to entirely disable WallPaintFeature when you encounter a black screen
/// </summary>
public class SkipWallPaintFeature : MonoBehaviour
{
      [SerializeField] private bool disableOnStart = true;
      [SerializeField] private bool reinstateARCamera = true;

      private void Start()
      {
            if (disableOnStart)
            {
                  DisableWallPaintFeature();
            }

            if (reinstateARCamera)
            {
                  FixARCamera();
            }
      }

      /// <summary>
      /// Find and disable WallPaintFeature in the URP renderer
      /// </summary>
      public void DisableWallPaintFeature()
      {
            bool found = false;

            // Access URP
            UniversalRenderPipelineAsset urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urpAsset == null)
            {
                  Debug.LogError("SkipWallPaintFeature: URP asset not found");
                  return;
            }

            // Get renderer data through reflection
            var rendererDataField = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList",
                BindingFlags.NonPublic | BindingFlags.Instance);

            if (rendererDataField == null)
            {
                  rendererDataField = typeof(UniversalRenderPipelineAsset).GetField("m_RendererData",
                      BindingFlags.NonPublic | BindingFlags.Instance);

                  if (rendererDataField == null)
                  {
                        Debug.LogError("SkipWallPaintFeature: Could not access renderer data");
                        return;
                  }
            }

            // Get renderer data list
            System.Collections.IList rendererDataList = rendererDataField.GetValue(urpAsset) as System.Collections.IList;
            if (rendererDataList == null || rendererDataList.Count == 0)
            {
                  Debug.LogError("SkipWallPaintFeature: No renderer data found");
                  return;
            }

            // Check all renderers
            foreach (var item in rendererDataList)
            {
                  var rendererData = item as ScriptableRendererData;
                  if (rendererData == null) continue;

                  // Get renderer features
                  var featuresField = typeof(ScriptableRendererData).GetField("m_RendererFeatures",
                      BindingFlags.NonPublic | BindingFlags.Instance);

                  if (featuresField == null) continue;

                  var features = featuresField.GetValue(rendererData) as System.Collections.IList;
                  if (features == null) continue;

                  // Find WallPaintFeature
                  for (int i = 0; i < features.Count; i++)
                  {
                        var feature = features[i];
                        if (feature is WallPaintFeature wallPaintFeature)
                        {
                              // Disable the feature
                              var enabledField = typeof(ScriptableRendererFeature).GetField("m_IsActive",
                                  BindingFlags.NonPublic | BindingFlags.Instance);

                              if (enabledField != null)
                              {
                                    enabledField.SetValue(wallPaintFeature, false);
                                    Debug.Log("SkipWallPaintFeature: Successfully disabled WallPaintFeature");
                                    found = true;
                              }
                              else
                              {
                                    Debug.LogError("SkipWallPaintFeature: Could not access enabled field");
                              }
                        }
                  }
            }

            if (!found)
            {
                  Debug.LogWarning("SkipWallPaintFeature: WallPaintFeature not found in URP");
            }
      }

      /// <summary>
      /// Reconfigure AR camera to fix potential issues
      /// </summary>
      public void FixARCamera()
      {
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                  Debug.Log("SkipWallPaintFeature: Fixing AR camera settings");

                  // Common AR camera settings
                  mainCamera.clearFlags = CameraClearFlags.SolidColor;
                  mainCamera.backgroundColor = Color.clear;
                  mainCamera.nearClipPlane = 0.1f;

                  // Ensure we don't have problematic components
                  var wallPaintEffect = mainCamera.GetComponent<WallPaintEffect>();
                  if (wallPaintEffect != null)
                  {
                        // Disable instead of destroying to keep references intact
                        wallPaintEffect.enabled = false;
                        Debug.Log("SkipWallPaintFeature: Disabled WallPaintEffect component");
                  }
            }
            else
            {
                  Debug.LogError("SkipWallPaintFeature: Main camera not found");
            }
      }

      /// <summary>
      /// Re-enable WallPaintFeature once issues are resolved
      /// </summary>
      public void EnableWallPaintFeature()
      {
            bool found = false;

            // Use same reflection code as DisableWallPaintFeature
            UniversalRenderPipelineAsset urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urpAsset == null) return;

            var rendererDataField = typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList",
                BindingFlags.NonPublic | BindingFlags.Instance);

            if (rendererDataField == null)
            {
                  rendererDataField = typeof(UniversalRenderPipelineAsset).GetField("m_RendererData",
                      BindingFlags.NonPublic | BindingFlags.Instance);

                  if (rendererDataField == null) return;
            }

            System.Collections.IList rendererDataList = rendererDataField.GetValue(urpAsset) as System.Collections.IList;
            if (rendererDataList == null || rendererDataList.Count == 0) return;

            foreach (var item in rendererDataList)
            {
                  var rendererData = item as ScriptableRendererData;
                  if (rendererData == null) continue;

                  var featuresField = typeof(ScriptableRendererData).GetField("m_RendererFeatures",
                      BindingFlags.NonPublic | BindingFlags.Instance);

                  if (featuresField == null) continue;

                  var features = featuresField.GetValue(rendererData) as System.Collections.IList;
                  if (features == null) continue;

                  for (int i = 0; i < features.Count; i++)
                  {
                        var feature = features[i];
                        if (feature is WallPaintFeature wallPaintFeature)
                        {
                              // Enable the feature
                              var enabledField = typeof(ScriptableRendererFeature).GetField("m_IsActive",
                                  BindingFlags.NonPublic | BindingFlags.Instance);

                              if (enabledField != null)
                              {
                                    enabledField.SetValue(wallPaintFeature, true);
                                    Debug.Log("SkipWallPaintFeature: Re-enabled WallPaintFeature");
                                    found = true;
                              }
                        }
                  }
            }

            if (!found)
            {
                  Debug.LogWarning("SkipWallPaintFeature: WallPaintFeature not found during re-enable attempt");
            }

            // Re-enable WallPaintEffect if we disabled it
            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                  var wallPaintEffect = mainCamera.GetComponent<WallPaintEffect>();
                  if (wallPaintEffect != null && !wallPaintEffect.enabled)
                  {
                        wallPaintEffect.enabled = true;
                        Debug.Log("SkipWallPaintFeature: Re-enabled WallPaintEffect component");
                  }
            }
      }
}