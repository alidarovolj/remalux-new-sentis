using UnityEngine;

/// <summary>
/// Инициализирует диалоги ошибок
/// </summary>
public class DialogInitializer : MonoBehaviour
{
      public static DialogInitializer instance;

      private void Awake()
      {
            if (instance == null)
            {
                  instance = this;
                  DontDestroyOnLoad(gameObject);

                  // Инициализируем диалог
                  var errorDialog = ModelLoadErrorDialog.Instance;
                  Debug.Log("Диалог ошибок инициализирован");
            }
            else
            {
                  Destroy(gameObject);
            }
      }

      /// <summary>
      /// Отображает ошибку загрузки модели
      /// </summary>
      /// <param name="error">Информация об ошибке</param>
      public static void ShowModelLoadError(ModelLoadErrorInfo error)
      {
            ModelLoadErrorDialog.Instance.ShowModelLoadError(error);
      }

      /// <summary>
      /// Отображает простое сообщение об ошибке
      /// </summary>
      /// <param name="errorMessage">Текст ошибки</param>
      public static void ShowError(string errorMessage)
      {
            ModelLoadErrorDialog.Instance.ShowError(errorMessage);
      }
}