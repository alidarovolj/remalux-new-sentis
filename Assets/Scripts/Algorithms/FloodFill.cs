using UnityEngine;
using System.Collections.Generic;

namespace WallPainting.Algorithms
{
      /// <summary>
      /// Реализация алгоритма заливки (Flood Fill) для выделения связной области на маске сегментации
      /// </summary>
      public static class FloodFill
      {
            /// <summary>
            /// Выполняет заливку на 2D массиве байтов, используя очередь (не рекурсивный подход)
            /// </summary>
            /// <param name="mask">Входная маска, где 1 означает область для заливки</param>
            /// <param name="startX">Начальная X координата для заливки</param>
            /// <param name="startY">Начальная Y координата для заливки</param>
            /// <returns>Новая маска только с залитой областью, или null если начальная точка не заливаема</returns>
            public static byte[,] Execute(byte[,] mask, int startX, int startY)
            {
                  int width = mask.GetLength(0);
                  int height = mask.GetLength(1);

                  // Проверка валидности начальных координат
                  if (startX < 0 || startX >= width || startY < 0 || startY >= height)
                  {
                        Debug.LogWarning($"FloodFill: Начальные координаты ({startX}, {startY}) вне границ маски");
                        return null;
                  }

                  // Проверка, является ли начальная точка частью стены
                  if (mask[startX, startY] == 0)
                  {
                        Debug.Log("FloodFill: Начальная точка не является частью стены");
                        return null;
                  }

                  // Создаем новую маску для результата
                  byte[,] filledMask = new byte[width, height];
                  Queue<Vector2Int> queue = new Queue<Vector2Int>();
                  queue.Enqueue(new Vector2Int(startX, startY));

                  // Помечаем начальную точку как посещенную
                  filledMask[startX, startY] = 1;

                  // Направления для 4-связности (вверх, вправо, вниз, влево)
                  Vector2Int[] directions = {
                new Vector2Int(0, 1),
                new Vector2Int(1, 0),
                new Vector2Int(0, -1),
                new Vector2Int(-1, 0)
            };

                  int pixelsProcessed = 0;

                  while (queue.Count > 0)
                  {
                        Vector2Int current = queue.Dequeue();
                        pixelsProcessed++;

                        // Проверяем всех соседей
                        foreach (var dir in directions)
                        {
                              Vector2Int neighbor = current + dir;

                              // Проверяем границы
                              if (neighbor.x >= 0 && neighbor.x < width &&
                                  neighbor.y >= 0 && neighbor.y < height)
                              {
                                    // Если сосед - стена и еще не посещен
                                    if (mask[neighbor.x, neighbor.y] == 1 && filledMask[neighbor.x, neighbor.y] == 0)
                                    {
                                          filledMask[neighbor.x, neighbor.y] = 1;
                                          queue.Enqueue(neighbor);
                                    }
                              }
                        }
                  }

                  Debug.Log($"FloodFill: Обработано {pixelsProcessed} пикселей");
                  return filledMask;
            }

            /// <summary>
            /// Расширенная версия с поддержкой 8-связности
            /// </summary>
            public static byte[,] Execute8Connected(byte[,] mask, int startX, int startY)
            {
                  int width = mask.GetLength(0);
                  int height = mask.GetLength(1);

                  if (startX < 0 || startX >= width || startY < 0 || startY >= height)
                        return null;

                  if (mask[startX, startY] == 0)
                        return null;

                  byte[,] filledMask = new byte[width, height];
                  Queue<Vector2Int> queue = new Queue<Vector2Int>();
                  queue.Enqueue(new Vector2Int(startX, startY));
                  filledMask[startX, startY] = 1;

                  // Направления для 8-связности
                  Vector2Int[] directions = {
                new Vector2Int(0, 1),   // вверх
                new Vector2Int(1, 0),   // вправо
                new Vector2Int(0, -1),  // вниз
                new Vector2Int(-1, 0),  // влево
                new Vector2Int(1, 1),   // вправо-вверх
                new Vector2Int(1, -1),  // вправо-вниз
                new Vector2Int(-1, -1), // влево-вниз
                new Vector2Int(-1, 1)   // влево-вверх
            };

                  while (queue.Count > 0)
                  {
                        Vector2Int current = queue.Dequeue();

                        foreach (var dir in directions)
                        {
                              Vector2Int neighbor = current + dir;

                              if (neighbor.x >= 0 && neighbor.x < width &&
                                  neighbor.y >= 0 && neighbor.y < height &&
                                  mask[neighbor.x, neighbor.y] == 1 &&
                                  filledMask[neighbor.x, neighbor.y] == 0)
                              {
                                    filledMask[neighbor.x, neighbor.y] = 1;
                                    queue.Enqueue(neighbor);
                              }
                        }
                  }

                  return filledMask;
            }
      }
}