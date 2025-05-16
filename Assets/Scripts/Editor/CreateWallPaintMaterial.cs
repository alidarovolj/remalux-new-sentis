using UnityEngine;
using UnityEditor;

/// <summary>
/// Редактор для создания материала на основе OptimizedWallPaint шейдера
/// </summary>
public class CreateWallPaintMaterial : Editor
{
    [MenuItem("AR Tools/Создать материал для покраски стен")]
    public static void CreateMaterial()
    {
        // Проверяем, существует ли шейдер
        Shader shader = Shader.Find("Custom/OptimizedWallPaint");
        if (shader == null)
        {
            Debug.LogError("Шейдер 'Custom/OptimizedWallPaint' не найден. Убедитесь, что он создан.");
            return;
        }
        
        // Создаем материал
        Material material = new Material(shader);
        material.name = "OptimizedWallPaint";
        
        // Настраиваем материал
        material.SetColor("_Color", Color.white);
        material.SetFloat("_GridScale", 10.0f);
        material.SetFloat("_GridLineWidth", 0.01f);
        material.SetColor("_GridColor", new Color(0.2f, 0.2f, 0.2f, 1.0f));
        material.SetFloat("_DebugMode", 0.0f);
        
        // Создаем директорию для материала, если её не существует
        if (!AssetDatabase.IsValidFolder("Assets/Materials"))
        {
            AssetDatabase.CreateFolder("Assets", "Materials");
        }
        
        // Сохраняем материал
        string path = "Assets/Materials/OptimizedWallPaint.mat";
        AssetDatabase.CreateAsset(material, path);
        AssetDatabase.SaveAssets();
        
        // Выделяем материал в Project view
        Selection.activeObject = material;
        
        Debug.Log($"Материал успешно создан: {path}");
    }
} 