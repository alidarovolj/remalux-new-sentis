using UnityEngine;
using System.Collections;

/// <summary>
/// Инициализирует основные компоненты приложения
/// Помещается на объект, который загружается в начале приложения
/// </summary>
[DefaultExecutionOrder(-10000)] // Запускаем раньше всех других скриптов
public class AppInitializer : MonoBehaviour
{
      private static AppInitializer instance;

      [Header("Настройки")]
      [SerializeField] private bool initializeOnAwake = true;
      [SerializeField] private float initializationDelay = 0.1f;

      [Header("Компоненты")]
      [SerializeField] private GameObject errorDialogPrefab;

      private bool isInitialized = false;

      public static AppInitializer Instance => instance;

      private void Awake()
      {
            if (instance == null)
            {
                  instance = this;
                  DontDestroyOnLoad(gameObject);

                  // Инициализируем при запуске, если включено
                  if (initializeOnAwake)
                  {
                        StartCoroutine(InitializeWithDelay());
                  }
            }
            else
            {
                  Destroy(gameObject);
            }
      }

      /// <summary>
      /// Инициализирует компоненты с небольшой задержкой для 
      /// гарантии, что все базовые системы Unity загружены
      /// </summary>
      private IEnumerator InitializeWithDelay()
      {
            yield return new WaitForSeconds(initializationDelay);
            InitializeComponents();
      }

      /// <summary>
      /// Инициализирует все необходимые компоненты приложения
      /// </summary>
      public void InitializeComponents()
      {
            if (isInitialized) return;

            Debug.Log("AppInitializer: Начало инициализации компонентов...");

            // Инициализируем диалог ошибок
            InitializeErrorDialog();

            // Проверяем наличие Unity Sentis
            CheckSentisAvailability();

            isInitialized = true;
            Debug.Log("AppInitializer: Все компоненты инициализированы");
      }

      /// <summary>
      /// Инициализирует диалог ошибок
      /// </summary>
      private void InitializeErrorDialog()
      {
            // Проверяем наличие DialogInitializer
            if (DialogInitializer.instance == null)
            {
                  // Если есть префаб, создаем из него
                  if (errorDialogPrefab != null)
                  {
                        Instantiate(errorDialogPrefab);
                        Debug.Log("AppInitializer: Диалог ошибок создан из префаба");
                  }
                  else
                  {
                        // Создаем новый объект с DialogInitializer
                        GameObject errorDialogObj = new GameObject("ErrorDialogContainer");
                        errorDialogObj.AddComponent<DialogInitializer>();
                        DontDestroyOnLoad(errorDialogObj);
                        Debug.Log("AppInitializer: Диалог ошибок создан программно");
                  }
            }
            else
            {
                  Debug.Log("AppInitializer: Диалог ошибок уже существует");
            }
      }

      /// <summary>
      /// Проверяет наличие Unity Sentis в проекте
      /// </summary>
      private void CheckSentisAvailability()
      {
            bool sentisAvailable = SafeModelLoader.IsSentisAvailable();
            if (!sentisAvailable)
            {
                  Debug.LogWarning("AppInitializer: Unity Sentis не обнаружен в проекте!");

                  // Показываем предупреждение
                  DialogInitializer.ShowModelLoadError(
                      new ModelErrorInfo(
                          "Unity Sentis",
                          "Package",
                          "Unity Sentis не обнаружен в проекте",
                          "Функции сегментации будут недоступны. Установите пакет Unity Sentis через Package Manager."
                      )
                  );
            }
            else
            {
                  Debug.Log("AppInitializer: Unity Sentis доступен");
            }
      }
}