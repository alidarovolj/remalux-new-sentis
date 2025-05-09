using UnityEngine;
using UnityEditor;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

/// <summary>
/// Редактор для безопасного назначения моделей сегментации стен
/// </summary>
[CustomEditor(typeof(MonoBehaviour))]
public class WallSegmentationModelAssigner : Editor
{
      private bool isWallSegmentation = false;
      private List<UnityEngine.Object> potentialModels = new List<UnityEngine.Object>();
      private UnityEngine.Object currentModel = null;
      private string[] modelNames = new string[0];
      private int selectedModelIndex = -1;
      private long[] modelSizes = new long[0];
      private string statusMessage = "";

      private void OnEnable()
      {
            // Проверяем, является ли текущий MonoBehaviour компонентом WallSegmentation
            isWallSegmentation = target.GetType().Name == "WallSegmentation";

            if (isWallSegmentation)
            {
                  // Получаем текущую модель
                  var modelField = target.GetType().GetField("modelAsset",
                      BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                  if (modelField != null)
                  {
                        currentModel = modelField.GetValue(target) as UnityEngine.Object;
                  }

                  // Ищем все ONNX модели в проекте
                  FindPotentialModels();
            }
      }

      private void FindPotentialModels()
      {
            potentialModels.Clear();
            statusMessage = "";

            string[] modelPaths = AssetDatabase.FindAssets("t:UnityEngine.Object")
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => path.EndsWith(".onnx"))
                .ToArray();

            // Получаем имена и размеры моделей
            modelNames = new string[modelPaths.Length];
            modelSizes = new long[modelPaths.Length];

            for (int i = 0; i < modelPaths.Length; i++)
            {
                  var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(modelPaths[i]);
                  potentialModels.Add(asset);

                  // Получаем размер файла
                  FileInfo fileInfo = new FileInfo(modelPaths[i]);
                  long sizeInMB = fileInfo.Length / (1024 * 1024);

                  modelSizes[i] = sizeInMB;
                  modelNames[i] = $"{Path.GetFileName(modelPaths[i])} ({sizeInMB} MB)";

                  // Если это текущая модель, запоминаем индекс
                  if (asset == currentModel)
                  {
                        selectedModelIndex = i;
                  }
            }

            if (modelPaths.Length == 0)
            {
                  statusMessage = "Модели ONNX не найдены в проекте";
            }
      }

      public override void OnInspectorGUI()
      {
            // Отображаем стандартный инспектор
            DrawDefaultInspector();

            // Если это не WallSegmentation, выходим
            if (!isWallSegmentation) return;

            // Рисуем разделитель
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Безопасное назначение модели", EditorStyles.boldLabel);
            EditorGUILayout.Space();

            // Отображаем текущую модель
            if (currentModel != null)
            {
                  EditorGUILayout.LabelField("Текущая модель:", currentModel.name);
            }
            else
            {
                  EditorGUILayout.HelpBox("Модель не назначена!", MessageType.Warning);
            }

            EditorGUILayout.Space();

            // Отображаем выпадающий список моделей
            if (potentialModels.Count > 0)
            {
                  EditorGUI.BeginChangeCheck();

                  int newIndex = EditorGUILayout.Popup("Выберите модель:", selectedModelIndex, modelNames);

                  if (EditorGUI.EndChangeCheck() && newIndex != selectedModelIndex && newIndex >= 0 && newIndex < potentialModels.Count)
                  {
                        selectedModelIndex = newIndex;

                        // Предупреждаем, если модель большая
                        if (modelSizes[selectedModelIndex] > 100)
                        {
                              bool proceed = EditorUtility.DisplayDialog("Предупреждение",
                                  $"Выбранная модель имеет большой размер ({modelSizes[selectedModelIndex]} MB), " +
                                  "что может вызвать проблемы при загрузке. Продолжить?",
                                  "Да, назначить модель", "Отмена");

                              if (!proceed)
                              {
                                    selectedModelIndex = -1;
                                    return;
                              }
                        }

                        // Назначаем модель через рефлексию
                        var modelField = target.GetType().GetField("modelAsset",
                            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);

                        if (modelField != null)
                        {
                              Undo.RecordObject(target, "Set Segmentation Model");

                              try
                              {
                                    modelField.SetValue(target, potentialModels[selectedModelIndex]);
                                    currentModel = potentialModels[selectedModelIndex];
                                    EditorUtility.SetDirty(target);
                                    statusMessage = $"Модель {modelNames[selectedModelIndex]} успешно назначена";
                              }
                              catch (System.Exception e)
                              {
                                    statusMessage = $"Ошибка при назначении модели: {e.Message}";
                                    Debug.LogError(statusMessage);
                              }
                        }
                  }
            }
            else if (!string.IsNullOrEmpty(statusMessage))
            {
                  EditorGUILayout.HelpBox(statusMessage, MessageType.Info);
            }

            // Кнопка для поиска моделей
            if (GUILayout.Button("Обновить список моделей"))
            {
                  FindPotentialModels();
            }

            // Отображаем статус
            if (!string.IsNullOrEmpty(statusMessage))
            {
                  EditorGUILayout.Space();
                  EditorGUILayout.HelpBox(statusMessage, MessageType.Info);
            }
      }
}