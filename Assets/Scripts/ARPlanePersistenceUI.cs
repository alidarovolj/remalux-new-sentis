using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Provides UI controls for the plane persistence system, allowing users to 
/// save planes permanently and reset saved planes.
/// </summary>
public class ARPlanePersistenceUI : MonoBehaviour
{
      [SerializeField] private ARManagerInitializer2 _arManagerInitializer;
      [SerializeField] private ARPlaneConfigurator _planeConfigurator;

      [Header("UI Elements")]
      [SerializeField] private Button _saveCurrentPlanesButton;
      [SerializeField] private Button _resetSavedPlanesButton;
      [SerializeField] private TextMeshProUGUI _statusText;

      // Public properties for our builder
      public ARManagerInitializer2 arManagerInitializer { get { return _arManagerInitializer; } set { _arManagerInitializer = value; } }
      public ARPlaneConfigurator planeConfigurator { get { return _planeConfigurator; } set { _planeConfigurator = value; } }
      public Button saveCurrentPlanesButton { get { return _saveCurrentPlanesButton; } set { _saveCurrentPlanesButton = value; } }
      public Button resetSavedPlanesButton { get { return _resetSavedPlanesButton; } set { _resetSavedPlanesButton = value; } }
      public TextMeshProUGUI statusText { get { return _statusText; } set { _statusText = value; } }

      [Header("Settings")]
      [SerializeField] private float minDistanceBetweenPlanes = 0.5f;
      [SerializeField] private float maxPlanesInScene = 20; // Limit to prevent performance issues

      private int savedPlanesCount = 0;

      private void Awake()
      {
            // Find references if not assigned in inspector
            if (_arManagerInitializer == null)
                  _arManagerInitializer = FindObjectOfType<ARManagerInitializer2>();

            if (_planeConfigurator == null)
                  _planeConfigurator = FindObjectOfType<ARPlaneConfigurator>();

            // Add button listeners
            if (_saveCurrentPlanesButton != null)
                  _saveCurrentPlanesButton.onClick.AddListener(SaveCurrentPlanes);

            if (_resetSavedPlanesButton != null)
                  _resetSavedPlanesButton.onClick.AddListener(ResetSavedPlanes);
      }

      private void Start()
      {
            UpdateStatusText();
      }

      private void OnEnable()
      {
            // Subscribe to events if needed
      }

      private void OnDisable()
      {
            // Unsubscribe from events if needed
      }

      private void Update()
      {
            // Update the status text periodically
            if (Time.frameCount % 30 == 0) // Update every 30 frames
            {
                  UpdateStatusText();
            }

            // Check for screen touch to make tapped plane persistent
            HandleTouchInput();
      }

      /// <summary>
      /// Handle touch input to make tapped planes persistent
      /// </summary>
      private void HandleTouchInput()
      {
            // Only process if there is a touch or mouse click
            if ((Input.touchCount > 0 && Input.GetTouch(0).phase == TouchPhase.Began) ||
                Input.GetMouseButtonDown(0))
            {
                  // Get the touch or mouse position
                  Vector2 screenPosition = Input.touchCount > 0 ?
                      Input.GetTouch(0).position :
                      new Vector2(Input.mousePosition.x, Input.mousePosition.y);

                  // Cast a ray from the camera through the touch position
                  Ray ray = Camera.main.ScreenPointToRay(screenPosition);
                  RaycastHit hit;

                  // Check if the ray hits something
                  if (Physics.Raycast(ray, out hit))
                  {
                        // Check if we hit an AR plane
                        GameObject hitObject = hit.collider.gameObject;
                        if (hitObject.name.StartsWith("MyARPlane_Debug_"))
                        {
                              // If the plane is not already persistent, make it persistent
                              if (!_arManagerInitializer.IsPlanePersistent(hitObject))
                              {
                                    if (_arManagerInitializer.MakePlanePersistent(hitObject))
                                    {
                                          savedPlanesCount++;
                                          UpdateStatusText();
                                          Debug.Log($"Made plane {hitObject.name} persistent by tap");
                                    }
                              }
                              else
                              {
                                    // If it's already persistent, you could toggle it off here
                                    // _arManagerInitializer.RemovePlanePersistence(hitObject);
                                    // savedPlanesCount--;
                                    // UpdateStatusText();
                                    // Debug.Log($"Removed persistence from plane {hitObject.name} by tap");
                              }
                        }
                  }
            }
      }

      /// <summary>
      /// Make all current planes persistent
      /// </summary>
      public void SaveCurrentPlanes()
      {
            if (_arManagerInitializer == null || _planeConfigurator == null)
            {
                  Debug.LogError("ARPlanePersistenceUI: Missing references to ARManagerInitializer2 or ARPlaneConfigurator");
                  return;
            }

            int newPersistentCount = 0;

            // Get all planes in the scene that match the naming pattern used by ARManagerInitializer2
            GameObject[] allPlanesInScene = GameObject.FindObjectsOfType<GameObject>();
            List<GameObject> planesToPersist = new List<GameObject>();

            foreach (GameObject obj in allPlanesInScene)
            {
                  // Check if it's an AR plane created by ARManagerInitializer2
                  if (obj.name.StartsWith("MyARPlane_Debug_"))
                  {
                        // Make sure it has the necessary components
                        if (obj.GetComponent<MeshRenderer>() != null && obj.GetComponent<MeshFilter>() != null)
                        {
                              // Skip planes that are already persistent
                              if (_arManagerInitializer.IsPlanePersistent(obj))
                                    continue;

                              planesToPersist.Add(obj);
                        }
                  }
            }

            // Make planes persistent with filtering for duplicates
            foreach (GameObject plane in planesToPersist)
            {
                  // Check if we're at the limit
                  if (savedPlanesCount >= maxPlanesInScene)
                  {
                        Debug.LogWarning($"Maximum number of persistent planes ({maxPlanesInScene}) reached");
                        break;
                  }

                  // Make this plane persistent
                  if (_arManagerInitializer.MakePlanePersistent(plane))
                  {
                        newPersistentCount++;
                        savedPlanesCount++;
                  }
            }

            Debug.Log($"Made {newPersistentCount} planes persistent");
            UpdateStatusText();
      }

      /// <summary>
      /// Reset all saved planes
      /// </summary>
      public void ResetSavedPlanes()
      {
            if (_arManagerInitializer == null)
            {
                  Debug.LogError("ARPlanePersistenceUI: Missing reference to ARManagerInitializer2");
                  return;
            }

            _arManagerInitializer.ResetAllPlanes();
            savedPlanesCount = 0;
            UpdateStatusText();
            Debug.Log("Reset all persistent planes");
      }

      /// <summary>
      /// Update the status text with the current saved planes count
      /// </summary>
      private void UpdateStatusText()
      {
            if (_statusText == null)
                  return;

            if (_planeConfigurator != null)
            {
                  var info = _planeConfigurator.GetStablePlanesInfo();
                  savedPlanesCount = info.stablePlanesCount;
                  _statusText.text = $"Saved Planes: {savedPlanesCount}";
            }
            else
            {
                  _statusText.text = $"Saved Planes: {savedPlanesCount}";
            }
      }
}