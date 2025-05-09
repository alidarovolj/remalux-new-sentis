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
      public static void ShowModelLoadError(string errorMessage)
      {
            // Дополняем сообщение об ошибке проверкой Sentis
            var enhancedMessage = EnhanceErrorMessage(errorMessage);

            var dialog = ModelLoadErrorDialog.Instance;
            if (dialog != null)
            {
                  dialog.ShowError(enhancedMessage);
            }
      }

      /// <summary>
      /// Отображает ошибку загрузки модели с информацией о модели
      /// </summary>
      public static void ShowModelLoadError(string errorMessage, UnityEngine.Object modelAsset)
      {
            // Улучшаем сообщение об ошибке информацией о модели
            string enhancedMessage = EnhanceErrorMessage(errorMessage);

            // Добавляем информацию о модели
            if (modelAsset != null)
            {
                  enhancedMessage += "\n\nИнформация о модели: " + SafeModelLoader.GetRuntimeModelInfo(modelAsset);
            }

            var dialog = ModelLoadErrorDialog.Instance;
            if (dialog != null)
            {
                  dialog.ShowError(enhancedMessage);
            }
      }

      /// <summary>
      /// Улучшает сообщение об ошибке дополнительной информацией
      /// </summary>
      private static string EnhanceErrorMessage(string originalMessage)
      {
            string enhancedMessage = originalMessage;

            // Добавляем информацию о доступности Sentis
            bool sentisAvailable = SafeModelLoader.IsSentisAvailable();
            if (!sentisAvailable)
            {
                  enhancedMessage += "\n\nПакет Unity Sentis не обнаружен в проекте!";
            }

            return enhancedMessage;
      }
}