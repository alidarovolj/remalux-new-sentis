using UnityEngine;

/// <summary>
/// Инициализирует UI компоненты приложения при старте
/// </summary>
public class UIInitializer : MonoBehaviour
{
      [SerializeField] private bool initializeErrorUI = true;

      private static UIInitializer instance;

      public static UIInitializer Instance => instance;

      private void Awake()
      {
            if (instance == null)
            {
                  instance = this;

                  // Проверяем, является ли объект корневым
                  if (transform.parent != null)
                  {
                        // Отсоединяем от родителя перед вызовом DontDestroyOnLoad
                        transform.SetParent(null);
                  }

                  DontDestroyOnLoad(gameObject);
            }
            else if (instance != this)
            {
                  Destroy(gameObject);
                  return;
            }

            // Инициализируем необходимые UI компоненты
            Initialize();
      }

      private void Initialize()
      {
            if (initializeErrorUI)
            {
                  // Получаем ссылку на ModelLoadErrorUI, которая создаст его, если он отсутствует
                  var errorUI = ModelLoadErrorUI.Instance;
                  Debug.Log("ModelLoadErrorUI инициализирован");
            }
      }

      /// <summary>
      /// Отображает предупреждение о том, что пакет Sentis не установлен
      /// </summary>
      public void ShowSentisNotInstalledWarning()
      {
            var errorUI = ModelLoadErrorUI.Instance;
            if (errorUI != null)
            {
                  errorUI.ShowModelLoadError("Unity Sentis не установлен");
            }
      }

      /// <summary>
      /// Отображает ошибку загрузки модели
      /// </summary>
      /// <param name="errorMessage">Сообщение об ошибке</param>
      public void ShowModelLoadError(string errorMessage)
      {
            var errorUI = ModelLoadErrorUI.Instance;
            if (errorUI != null)
            {
                  errorUI.ShowModelLoadError(errorMessage);
            }
      }
}