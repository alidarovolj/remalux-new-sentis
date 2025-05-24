using UnityEngine;
using UnityEngine.XR.ARFoundation;

public class MeshChangeLogger : MonoBehaviour
{
      private ARMeshManager arMeshManager;

      void Awake()
      {
            arMeshManager = FindObjectOfType<ARMeshManager>();
            if (arMeshManager == null)
            {
                  Debug.LogError("MeshChangeLogger: ARMeshManager not found in scene!");
                  enabled = false;
                  return;
            }
            Debug.Log("MeshChangeLogger: ARMeshManager found.");
      }

      void OnEnable()
      {
            if (arMeshManager != null)
            {
                  arMeshManager.meshesChanged += OnMeshesChanged;
                  Debug.Log("MeshChangeLogger: Subscribed to ARMeshManager.meshesChanged.");
            }
      }

      void OnDisable()
      {
            if (arMeshManager != null)
            {
                  arMeshManager.meshesChanged -= OnMeshesChanged;
                  Debug.Log("MeshChangeLogger: Unsubscribed from ARMeshManager.meshesChanged.");
            }
      }

      private void OnMeshesChanged(ARMeshesChangedEventArgs args)
      {
            Debug.Log($"MeshChangeLogger: OnMeshesChanged CALLED! Added: {args.added.Count}, Updated: {args.updated.Count}, Removed: {args.removed.Count}");

            if (args.added.Count > 0)
            {
                  foreach (var meshFilter in args.added)
                  {
                        Debug.Log($"MeshChangeLogger: Added MeshFilter: {meshFilter.name}, Vertices: {meshFilter.mesh.vertexCount}");
                        // Дополнительно можно добавить MeshCollider, если нужно видеть меши или взаимодействовать с ними
                        if (meshFilter.gameObject.GetComponent<MeshCollider>() == null)
                        {
                              meshFilter.gameObject.AddComponent<MeshCollider>();
                              Debug.Log($"MeshChangeLogger: Added MeshCollider to {meshFilter.name}");
                        }
                  }
            }
            if (args.updated.Count > 0)
            {
                  foreach (var meshFilter in args.updated)
                  {
                        Debug.Log($"MeshChangeLogger: Updated MeshFilter: {meshFilter.name}, Vertices: {meshFilter.mesh.vertexCount}");
                  }
            }
      }
}