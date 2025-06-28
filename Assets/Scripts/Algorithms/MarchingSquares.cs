using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace WallPainting.Algorithms
{
      /// <summary>
      /// Реализация алгоритма Marching Squares для извлечения контура из бинарной маски
      /// </summary>
      public static class MarchingSquares
      {
            // Таблица поиска для определения направления следующего шага
            // Ключ - тип ячейки (0-15), значение - изменение направления
            private static readonly Dictionary<int, int> nextStepLookup = new Dictionary<int, int>
        {
            {0, 0}, {1, 3}, {2, 0}, {3, 3}, {4, 1}, {5, 0}, {6, 1}, {7, 3},
            {8, 3}, {9, 2}, {10, 0}, {11, 2}, {12, 2}, {13, 1}, {14, 1}, {15, 0}
        };

            // Смещения для движения в 4 направлениях: вправо, вниз, влево, вверх
            private static readonly Vector2Int[] stepOffsets = {
            new Vector2Int(1, 0),   // вправо
            new Vector2Int(0, -1),  // вниз
            new Vector2Int(-1, 0),  // влево
            new Vector2Int(0, 1)    // вверх
        };

            /// <summary>
            /// Извлекает все контуры из бинарной маски
            /// </summary>
            /// <param name="mask">Бинарная маска (1 - объект, 0 - фон)</param>
            /// <returns>Список контуров, каждый контур - список точек Vector2</returns>
            public static List<List<Vector2>> ExtractAllContours(byte[,] mask)
            {
                  int width = mask.GetLength(0);
                  int height = mask.GetLength(1);
                  bool[,] visited = new bool[width, height];
                  List<List<Vector2>> allContours = new List<List<Vector2>>();

                  // Ищем все контуры
                  for (int y = 0; y < height; y++)
                  {
                        for (int x = 0; x < width; x++)
                        {
                              if (mask[x, y] == 1 && !visited[x, y] && IsEdgePixel(mask, x, y, width, height))
                              {
                                    var contour = TraceContour(mask, new Vector2Int(x, y), visited, width, height);
                                    if (contour != null && contour.Count > 3)
                                    {
                                          allContours.Add(contour);
                                    }
                              }
                        }
                  }

                  return allContours;
            }

            /// <summary>
            /// Извлекает основной (самый длинный) контур из бинарной маски
            /// </summary>
            public static List<Vector2> ExtractMainContour(byte[,] mask)
            {
                  var allContours = ExtractAllContours(mask);
                  if (allContours.Count == 0) return null;

                  // Возвращаем самый длинный контур
                  return allContours.OrderByDescending(c => c.Count).First();
            }

            /// <summary>
            /// Упрощенная версия для извлечения одного контура (оригинальная из отчета)
            /// </summary>
            public static List<Vector2> ExtractContour(byte[,] mask)
            {
                  int width = mask.GetLength(0);
                  int height = mask.GetLength(1);
                  Vector2Int startPoint = FindStartPoint(mask, width, height);

                  if (startPoint.x == -1)
                  {
                        Debug.LogWarning("MarchingSquares: Контур не найден");
                        return null;
                  }

                  List<Vector2> contour = new List<Vector2>();
                  Vector2Int currentPoint = startPoint;
                  int direction = 0; // 0: вправо, 1: вниз, 2: влево, 3: вверх
                  int maxIterations = width * height * 2; // Защита от бесконечного цикла
                  int iterations = 0;

                  do
                  {
                        contour.Add(new Vector2(currentPoint.x, currentPoint.y));

                        int cellType = GetCellType(currentPoint, mask, width, height);
                        int stepChange = nextStepLookup[cellType];

                        // Обновляем направление
                        direction = (direction + stepChange) % 4;

                        // Двигаемся в новом направлении
                        currentPoint += stepOffsets[direction];

                        iterations++;
                        if (iterations > maxIterations)
                        {
                              Debug.LogError("MarchingSquares: Превышено максимальное количество итераций");
                              break;
                        }

                  } while (currentPoint != startPoint);

                  Debug.Log($"MarchingSquares: Извлечен контур с {contour.Count} точками");
                  return contour;
            }

            /// <summary>
            /// Трассировка контура с учетом посещенных точек
            /// </summary>
            private static List<Vector2> TraceContour(byte[,] mask, Vector2Int start, bool[,] visited, int width, int height)
            {
                  List<Vector2> contour = new List<Vector2>();
                  Vector2Int current = start;
                  Vector2Int previous = new Vector2Int(-1, -1);

                  do
                  {
                        contour.Add(new Vector2(current.x, current.y));
                        visited[current.x, current.y] = true;

                        Vector2Int next = GetNextEdgePoint(mask, current, previous, width, height);
                        if (next.x == -1) break;

                        previous = current;
                        current = next;

                  } while (current != start && contour.Count < width * height);

                  return contour;
            }

            /// <summary>
            /// Находит следующую точку контура
            /// </summary>
            private static Vector2Int GetNextEdgePoint(byte[,] mask, Vector2Int current, Vector2Int previous, int width, int height)
            {
                  // Проверяем все 8 соседей по часовой стрелке
                  Vector2Int[] neighbors = {
                new Vector2Int(1, 0), new Vector2Int(1, -1), new Vector2Int(0, -1), new Vector2Int(-1, -1),
                new Vector2Int(-1, 0), new Vector2Int(-1, 1), new Vector2Int(0, 1), new Vector2Int(1, 1)
            };

                  foreach (var offset in neighbors)
                  {
                        Vector2Int next = current + offset;

                        if (next != previous &&
                            IsInBounds(next.x, next.y, width, height) &&
                            mask[next.x, next.y] == 1 &&
                            IsEdgePixel(mask, next.x, next.y, width, height))
                        {
                              return next;
                        }
                  }

                  return new Vector2Int(-1, -1);
            }

            /// <summary>
            /// Проверяет, является ли пиксель граничным
            /// </summary>
            private static bool IsEdgePixel(byte[,] mask, int x, int y, int width, int height)
            {
                  if (mask[x, y] == 0) return false;

                  // Проверяем 4-связных соседей
                  if (x > 0 && mask[x - 1, y] == 0) return true;
                  if (x < width - 1 && mask[x + 1, y] == 0) return true;
                  if (y > 0 && mask[x, y - 1] == 0) return true;
                  if (y < height - 1 && mask[x, y + 1] == 0) return true;

                  // Граница изображения
                  if (x == 0 || x == width - 1 || y == 0 || y == height - 1) return true;

                  return false;
            }

            /// <summary>
            /// Находит начальную точку для трассировки контура
            /// </summary>
            private static Vector2Int FindStartPoint(byte[,] mask, int width, int height)
            {
                  // Сканируем сверху вниз, слева направо
                  for (int y = 0; y < height; y++)
                  {
                        for (int x = 0; x < width; x++)
                        {
                              if (mask[x, y] == 1 && IsEdgePixel(mask, x, y, width, height))
                              {
                                    return new Vector2Int(x, y);
                              }
                        }
                  }
                  return new Vector2Int(-1, -1);
            }

            /// <summary>
            /// Определяет тип ячейки для алгоритма Marching Squares
            /// </summary>
            private static int GetCellType(Vector2Int p, byte[,] mask, int width, int height)
            {
                  int type = 0;

                  // Биты: TL(8), TR(4), BR(2), BL(1)
                  if (IsInBounds(p.x, p.y, width, height) && mask[p.x, p.y] == 1) type |= 1;        // BL
                  if (IsInBounds(p.x + 1, p.y, width, height) && mask[p.x + 1, p.y] == 1) type |= 2;    // BR
                  if (IsInBounds(p.x + 1, p.y + 1, width, height) && mask[p.x + 1, p.y + 1] == 1) type |= 4;  // TR
                  if (IsInBounds(p.x, p.y + 1, width, height) && mask[p.x, p.y + 1] == 1) type |= 8;    // TL

                  return type;
            }

            /// <summary>
            /// Проверяет, находятся ли координаты в границах массива
            /// </summary>
            private static bool IsInBounds(int x, int y, int width, int height)
            {
                  return x >= 0 && x < width && y >= 0 && y < height;
            }

            /// <summary>
            /// Упрощает контур с помощью алгоритма Рамера-Дугласа-Пекера
            /// </summary>
            public static List<Vector2> SimplifyContour(List<Vector2> contour, float tolerance)
            {
                  if (contour == null || contour.Count < 3) return contour;
                  if (tolerance <= 0) return new List<Vector2>(contour);

                  List<Vector2> simplified = new List<Vector2>();
                  SimplifyDouglasPeucker(contour, 0, contour.Count - 1, tolerance, simplified);

                  // Убеждаемся, что первая и последняя точки включены
                  if (!simplified.Contains(contour[0])) simplified.Insert(0, contour[0]);
                  if (!simplified.Contains(contour[contour.Count - 1])) simplified.Add(contour[contour.Count - 1]);

                  return simplified;
            }

            private static void SimplifyDouglasPeucker(List<Vector2> points, int start, int end, float tolerance, List<Vector2> simplified)
            {
                  if (end <= start + 1)
                  {
                        if (!simplified.Contains(points[start])) simplified.Add(points[start]);
                        if (!simplified.Contains(points[end])) simplified.Add(points[end]);
                        return;
                  }

                  float maxDistance = 0;
                  int maxIndex = start;

                  // Находим точку с максимальным расстоянием от линии
                  for (int i = start + 1; i < end; i++)
                  {
                        float distance = PointToLineDistance(points[i], points[start], points[end]);
                        if (distance > maxDistance)
                        {
                              maxDistance = distance;
                              maxIndex = i;
                        }
                  }

                  // Если максимальное расстояние больше допуска, рекурсивно упрощаем
                  if (maxDistance > tolerance)
                  {
                        SimplifyDouglasPeucker(points, start, maxIndex, tolerance, simplified);
                        SimplifyDouglasPeucker(points, maxIndex, end, tolerance, simplified);
                  }
                  else
                  {
                        if (!simplified.Contains(points[start])) simplified.Add(points[start]);
                        if (!simplified.Contains(points[end])) simplified.Add(points[end]);
                  }
            }

            private static float PointToLineDistance(Vector2 point, Vector2 lineStart, Vector2 lineEnd)
            {
                  Vector2 line = lineEnd - lineStart;
                  float lineLength = line.magnitude;
                  if (lineLength == 0) return Vector2.Distance(point, lineStart);

                  float t = Mathf.Clamp01(Vector2.Dot(point - lineStart, line) / (lineLength * lineLength));
                  Vector2 projection = lineStart + t * line;
                  return Vector2.Distance(point, projection);
            }
      }
}