using UnityEngine;
using UnityEditor;

/// <summary>
/// Добавляет элемент меню для ручного добавления CameraCullingMaskFixer в сцену
/// </summary>
public static class CameraCullingMaskFixerMenu
{
    [MenuItem("Remalux/AR Tools/Добавить Camera Culling Mask Fixer")]
    public static void AddCullingMaskFixer()
    {
        // Проверяем, есть ли уже CameraCullingMaskFixer в сцене
        CameraCullingMaskFixer existingFixer = Object.FindObjectOfType<CameraCullingMaskFixer>();
        
        if (existingFixer != null)
        {
            // Фиксер уже существует, показываем диалог
            bool replaceExisting = EditorUtility.DisplayDialog(
                "CameraCullingMaskFixer уже существует",
                "В сцене уже есть компонент CameraCullingMaskFixer. Хотите создать новый?",
                "Да, создать новый", "Нет, использовать существующий");
                
            if (!replaceExisting)
            {
                // Выбираем существующий объект в иерархии
                Selection.activeGameObject = existingFixer.gameObject;
                return;
            }
        }
        
        // Создаем новый GameObject с компонентом CameraCullingMaskFixer
        GameObject fixerObj = new GameObject("CameraCullingMaskFixer");
        fixerObj.AddComponent<CameraCullingMaskFixer>();
        
        // Выбираем созданный объект в иерархии
        Selection.activeGameObject = fixerObj;
        
        Debug.Log("CameraCullingMaskFixer успешно добавлен в сцену");
    }
    
    [MenuItem("Remalux/AR Tools/Исправить Culling Mask всех камер")]
    public static void FixAllCameras()
    {
        // Получаем все камеры в сцене
        Camera[] allCameras = Object.FindObjectsOfType<Camera>();
        int fixedCount = 0;
        
        foreach (Camera cam in allCameras)
        {
            // Если маска не установлена на Everything
            if (cam.cullingMask != -1)
            {
                int oldMask = cam.cullingMask;
                cam.cullingMask = -1; // Everything
                fixedCount++;
                
                // Пометку сцены как измененную для сохранения изменений
                EditorUtility.SetDirty(cam);
            }
            
            // Исправляем также Far Clipping Plane
            if (cam.farClipPlane < 100f)
            {
                float oldFar = cam.farClipPlane;
                cam.farClipPlane = 1000f;
                
                Debug.Log($"Исправлена камера {cam.name}: farClipPlane {oldFar} -> 1000");
                EditorUtility.SetDirty(cam);
            }
        }
        
        // Сохраняем сцену, чтобы изменения сохранились
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(
            UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene());
            
        Debug.Log($"Исправлено {fixedCount} камер: Culling Mask установлен на Everything (-1)");
        
        // Показываем диалог с результатами
        EditorUtility.DisplayDialog(
            "Исправление Culling Mask",
            $"Исправлено {fixedCount} камер: Culling Mask установлен на Everything (-1).\n\nНе забудьте сохранить сцену!",
            "ОК");
    }
} 