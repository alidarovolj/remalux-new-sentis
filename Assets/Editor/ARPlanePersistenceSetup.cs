using UnityEngine;
using UnityEditor;
using System.Collections.Generic;

/// <summary>
/// Editor utility for setting up the AR Plane Persistence system in a scene
/// </summary>
public class ARPlanePersistenceSetup : EditorWindow
{
      private ARManagerInitializer2 arManagerInitializer;
      private ARPlaneConfigurator planeConfigurator;
      private bool createUI = true;
      private Color uiButtonColor = new Color(0.2f, 0.6f, 1.0f);
      private Color uiTextColor = Color.white;

      [MenuItem("AR Tools/Setup Plane Persistence")]
      public static void ShowWindow()
      {
            GetWindow<ARPlanePersistenceSetup>("AR Plane Persistence Setup");
      }

      private void OnGUI()
      {
            EditorGUILayout.LabelField("AR Plane Persistence Setup", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            EditorGUILayout.HelpBox("This utility will set up the AR Plane Persistence system in your scene.", MessageType.Info);
            EditorGUILayout.Space();

            arManagerInitializer = EditorGUILayout.ObjectField("AR Manager Initializer", arManagerInitializer, typeof(ARManagerInitializer2), true) as ARManagerInitializer2;
            planeConfigurator = EditorGUILayout.ObjectField("AR Plane Configurator", planeConfigurator, typeof(ARPlaneConfigurator), true) as ARPlaneConfigurator;

            EditorGUILayout.Space();
            EditorGUILayout.LabelField("UI Options", EditorStyles.boldLabel);
            createUI = EditorGUILayout.Toggle("Create UI", createUI);

            if (createUI)
            {
                  EditorGUI.indentLevel++;
                  uiButtonColor = EditorGUILayout.ColorField("Button Color", uiButtonColor);
                  uiTextColor = EditorGUILayout.ColorField("Text Color", uiTextColor);
                  EditorGUI.indentLevel--;
            }

            EditorGUILayout.Space();

            GUI.enabled = arManagerInitializer != null && planeConfigurator != null;
            if (GUILayout.Button("Setup Plane Persistence"))
            {
                  SetupPlanePersistence();
            }
            GUI.enabled = true;
      }

      private void SetupPlanePersistence()
      {
            // Validate components
            if (arManagerInitializer == null)
            {
                  EditorUtility.DisplayDialog("Setup Error", "AR Manager Initializer is required.", "OK");
                  return;
            }

            if (planeConfigurator == null)
            {
                  EditorUtility.DisplayDialog("Setup Error", "AR Plane Configurator is required.", "OK");
                  return;
            }

            // Enable persistent planes in the ARManagerInitializer2
            SerializedObject so = new SerializedObject(arManagerInitializer);
            SerializedProperty usePersistentPlanesProp = so.FindProperty("usePersistentPlanes");
            SerializedProperty highlightPersistentPlanesProp = so.FindProperty("highlightPersistentPlanes");

            if (usePersistentPlanesProp != null)
            {
                  usePersistentPlanesProp.boolValue = true;
            }

            if (highlightPersistentPlanesProp != null)
            {
                  highlightPersistentPlanesProp.boolValue = true;
            }

            so.ApplyModifiedProperties();

            // Create UI if requested
            if (createUI)
            {
                  GameObject uiBuilderObj = new GameObject("AR Plane Persistence UI Builder");
                  ARPlanePersistenceUIBuilder uiBuilder = uiBuilderObj.AddComponent<ARPlanePersistenceUIBuilder>();

                  // Set references
                  SerializedObject uiBuilderSO = new SerializedObject(uiBuilder);
                  SerializedProperty arManagerProp = uiBuilderSO.FindProperty("arManagerInitializer");
                  SerializedProperty planeConfiguratorProp = uiBuilderSO.FindProperty("planeConfigurator");
                  SerializedProperty buttonColorProp = uiBuilderSO.FindProperty("buttonColor");
                  SerializedProperty textColorProp = uiBuilderSO.FindProperty("textColor");

                  if (arManagerProp != null)
                  {
                        arManagerProp.objectReferenceValue = arManagerInitializer;
                  }

                  if (planeConfiguratorProp != null)
                  {
                        planeConfiguratorProp.objectReferenceValue = planeConfigurator;
                  }

                  if (buttonColorProp != null)
                  {
                        buttonColorProp.colorValue = uiButtonColor;
                  }

                  if (textColorProp != null)
                  {
                        textColorProp.colorValue = uiTextColor;
                  }

                  uiBuilderSO.ApplyModifiedProperties();

                  // Call the method to create the UI
                  uiBuilder.CreateUI();

                  // Delete the builder after it's done its job
                  DestroyImmediate(uiBuilderObj);
            }

            EditorUtility.DisplayDialog("Setup Complete",
                "AR Plane Persistence system has been set up successfully." +
                (createUI ? " UI has been created." : ""),
                "OK");
      }
}