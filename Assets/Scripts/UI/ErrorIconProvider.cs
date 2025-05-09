using UnityEngine;

/// <summary>
/// Предоставляет иконки для диалоговых окон ошибок
/// </summary>
public static class ErrorIconProvider
{
      // Кешированная иконка
      private static Sprite cachedErrorIcon;

      /// <summary>
      /// Получает иконку ошибки для отображения в диалоге
      /// </summary>
      public static Sprite GetErrorIcon()
      {
            // Если иконка уже создана, возвращаем её
            if (cachedErrorIcon != null)
            {
                  return cachedErrorIcon;
            }

            // Пытаемся загрузить иконку из ресурсов
            cachedErrorIcon = Resources.Load<Sprite>("UI/Icons/ErrorIcon");

            // Если иконки нет в ресурсах, создаем простую иконку программно
            if (cachedErrorIcon == null)
            {
                  // Создаем текстуру с красным кругом и белым восклицательным знаком
                  int size = 128;
                  Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);

                  // Заполняем текстуру прозрачным цветом
                  Color[] pixels = new Color[size * size];
                  for (int i = 0; i < pixels.Length; i++)
                  {
                        pixels[i] = Color.clear;
                  }

                  // Рисуем красный круг
                  Color redColor = new Color(0.9f, 0.2f, 0.2f, 1.0f);
                  for (int y = 0; y < size; y++)
                  {
                        for (int x = 0; x < size; x++)
                        {
                              // Вычисляем расстояние от центра
                              float dx = x - size / 2;
                              float dy = y - size / 2;
                              float distance = Mathf.Sqrt(dx * dx + dy * dy);

                              // Если точка внутри круга, закрашиваем её
                              if (distance < size / 2 - 2)
                              {
                                    pixels[y * size + x] = redColor;
                              }
                        }
                  }

                  // Рисуем белый восклицательный знак
                  Color whiteColor = Color.white;

                  // Вертикальная линия восклицательного знака
                  for (int y = size / 5; y < size * 3 / 5; y++)
                  {
                        for (int x = size * 2 / 5; x < size * 3 / 5; x++)
                        {
                              pixels[y * size + x] = whiteColor;
                        }
                  }

                  // Точка восклицательного знака
                  for (int y = size * 2 / 3; y < size * 3 / 4; y++)
                  {
                        for (int x = size * 2 / 5; x < size * 3 / 5; x++)
                        {
                              pixels[y * size + x] = whiteColor;
                        }
                  }

                  // Применяем пиксели к текстуре
                  texture.SetPixels(pixels);
                  texture.Apply();

                  // Создаем спрайт из текстуры
                  cachedErrorIcon = Sprite.Create(
                      texture,
                      new Rect(0, 0, size, size),
                      new Vector2(0.5f, 0.5f)
                  );

                  // Задаем имя иконке
                  cachedErrorIcon.name = "GeneratedErrorIcon";
            }

            return cachedErrorIcon;
      }
}