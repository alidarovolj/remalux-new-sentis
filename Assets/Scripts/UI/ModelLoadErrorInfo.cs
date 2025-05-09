using UnityEngine;

/// <summary>
/// Структура для хранения информации об ошибке загрузки ML модели
/// </summary>
[System.Serializable]
public class ModelLoadErrorInfo : ModelErrorInfo
{
      /// <summary>
      /// Конструктор с основными параметрами
      /// </summary>
      public ModelLoadErrorInfo(string modelName, string errorMessage)
          : base(modelName, "Unknown", errorMessage, "Проверьте наличие файла модели и совместимость с Unity Sentis")
      {
      }

      /// <summary>
      /// Полный конструктор
      /// </summary>
      public ModelLoadErrorInfo(string modelName, string modelType, string errorMessage, string recommendation)
          : base(modelName, modelType, errorMessage, recommendation)
      {
      }

      /// <summary>
      /// Создает объект ошибки из исключения
      /// </summary>
      public static ModelLoadErrorInfo FromException(string modelName, System.Exception ex)
      {
            return new ModelLoadErrorInfo(
                modelName,
                "Runtime",
                ex.Message,
                "Проверьте совместимость модели с текущей версией Unity Sentis"
            );
      }
}