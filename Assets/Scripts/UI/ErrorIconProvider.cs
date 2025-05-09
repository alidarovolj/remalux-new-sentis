using UnityEngine;

/// <summary>
/// Создает и предоставляет иконки для UI ошибок программно
/// </summary>
public static class ErrorIconProvider
{
      private static Sprite errorIconSprite;

      /// <summary>
      /// Генерирует и возвращает иконку ошибки
      /// </summary>
      /// <returns>Спрайт с иконкой ошибки</returns>
      public static Sprite GetErrorIcon()
      {
            if (errorIconSprite == null)
            {
                  CreateErrorIcon();
            }
            return errorIconSprite;
      }

      // Создает иконку ошибки программно
      private static void CreateErrorIcon()
      {
            // Создаем текстуру 128x128 пикселей
            Texture2D texture = new Texture2D(128, 128, TextureFormat.RGBA32, false);

            // Заполняем текстуру прозрачным цветом
            Color[] colors = new Color[128 * 128];
            for (int i = 0; i < colors.Length; i++)
            {
                  colors[i] = Color.clear;
            }
            texture.SetPixels(colors);

            // Рисуем иконку ошибки (гексагон с восклицательным знаком)
            DrawHexagon(texture, new Color(0.6f, 0.6f, 0.6f));
            DrawExclamationMark(texture, Color.white);

            // Применяем изменения
            texture.Apply();

            // Создаем спрайт
            errorIconSprite = Sprite.Create(
                texture,
                new Rect(0, 0, texture.width, texture.height),
                new Vector2(0.5f, 0.5f)
            );
      }

      // Рисует гексагон в текстуре
      private static void DrawHexagon(Texture2D texture, Color color)
      {
            int width = texture.width;
            int height = texture.height;
            int centerX = width / 2;
            int centerY = height / 2;
            int radius = Mathf.Min(width, height) / 2 - 8;

            // Рисуем гексагон
            for (int x = 0; x < width; x++)
            {
                  for (int y = 0; y < height; y++)
                  {
                        // Рассчитываем расстояние от центра
                        float dx = (x - centerX) / (float)radius;
                        float dy = (y - centerY) / (float)radius;

                        // Формула для гексагона
                        float hex = Mathf.Max(
                            Mathf.Abs(dx),
                            Mathf.Abs(dx * 0.5f + dy * 0.866f),
                            Mathf.Abs(dx * 0.5f - dy * 0.866f)
                        );

                        if (hex < 1.0f && hex > 0.8f)
                        {
                              texture.SetPixel(x, y, color);
                        }
                  }
            }
      }

      // Рисует восклицательный знак в текстуре
      private static void DrawExclamationMark(Texture2D texture, Color color)
      {
            int width = texture.width;
            int height = texture.height;
            int centerX = width / 2;
            int centerY = height / 2;

            // Верхняя часть восклицательного знака
            int exclamationWidth = width / 8;
            int exclamationHeight = height / 2;

            for (int x = centerX - exclamationWidth / 2; x < centerX + exclamationWidth / 2; x++)
            {
                  for (int y = centerY - exclamationHeight / 4; y < centerY + exclamationHeight / 2; y++)
                  {
                        texture.SetPixel(x, y, color);
                  }
            }

            // Точка восклицательного знака
            int dotSize = width / 12;
            for (int x = centerX - dotSize / 2; x < centerX + dotSize / 2; x++)
            {
                  for (int y = centerY - exclamationHeight / 2; y < centerY - exclamationHeight / 3; y++)
                  {
                        texture.SetPixel(x, y, color);
                  }
            }
      }
}