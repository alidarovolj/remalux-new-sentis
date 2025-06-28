using UnityEngine;
using System.Collections.Generic;

namespace WallPainting.Algorithms
{
      /// <summary>
      /// Реализация алгоритма Ear Clipping для триангуляции простых полигонов
      /// </summary>
      public static class EarClippingTriangulator
      {
            /// <summary>
            /// Триангулирует 3D полигон, определенный списком вершин
            /// </summary>
            /// <param name="vertices">Список вершин, формирующих простой полигон</param>
            /// <returns>Массив индексов треугольников</returns>
            public static int[] Triangulate(List<Vector3> vertices)
            {
                  if (vertices == null || vertices.Count < 3)
                  {
                        Debug.LogError("EarClipping: Недостаточно вершин для триангуляции");
                        return null;
                  }

                  // Для треугольника просто возвращаем индексы
                  if (vertices.Count == 3)
                  {
                        return new int[] { 0, 1, 2 };
                  }

                  List<int> indices = new List<int>();
                  List<int> activeVertices = new List<int>();

                  // Инициализируем список активных вершин
                  for (int i = 0; i < vertices.Count; i++)
                  {
                        activeVertices.Add(i);
                  }

                  // Вычисляем нормаль полигона для определения ориентации
                  Vector3 polygonNormal = CalculatePolygonNormal(vertices);

                  int maxIterations = vertices.Count * 3;
                  int iterations = 0;

                  // Основной цикл триангуляции
                  while (activeVertices.Count > 3)
                  {
                        bool earFound = false;

                        for (int i = 0; i < activeVertices.Count; i++)
                        {
                              if (IsEar(i, activeVertices, vertices, polygonNormal))
                              {
                                    // Добавляем треугольник
                                    int prev = (i == 0) ? activeVertices.Count - 1 : i - 1;
                                    int next = (i + 1) % activeVertices.Count;

                                    indices.Add(activeVertices[prev]);
                                    indices.Add(activeVertices[i]);
                                    indices.Add(activeVertices[next]);

                                    // Удаляем "ухо"
                                    activeVertices.RemoveAt(i);
                                    earFound = true;
                                    break;
                              }
                        }

                        if (!earFound)
                        {
                              Debug.LogError("EarClipping: Не удалось найти ухо. Полигон может быть вырожденным или самопересекающимся");
                              // Пытаемся завершить триангуляцию принудительно
                              break;
                        }

                        iterations++;
                        if (iterations > maxIterations)
                        {
                              Debug.LogError("EarClipping: Превышено максимальное количество итераций");
                              break;
                        }
                  }

                  // Добавляем последний треугольник
                  if (activeVertices.Count == 3)
                  {
                        indices.Add(activeVertices[0]);
                        indices.Add(activeVertices[1]);
                        indices.Add(activeVertices[2]);
                  }

                  Debug.Log($"EarClipping: Создано {indices.Count / 3} треугольников из {vertices.Count} вершин");
                  return indices.ToArray();
            }

            /// <summary>
            /// Проверяет, является ли вершина "ухом"
            /// </summary>
            private static bool IsEar(int vertexIndex, List<int> activeVertices, List<Vector3> allVertices, Vector3 polygonNormal)
            {
                  int vertexCount = activeVertices.Count;
                  int prevIndex = (vertexIndex == 0) ? vertexCount - 1 : vertexIndex - 1;
                  int nextIndex = (vertexIndex + 1) % vertexCount;

                  Vector3 prev = allVertices[activeVertices[prevIndex]];
                  Vector3 curr = allVertices[activeVertices[vertexIndex]];
                  Vector3 next = allVertices[activeVertices[nextIndex]];

                  // Проверяем, является ли вершина выпуклой
                  if (!IsConvex(prev, curr, next, polygonNormal))
                  {
                        return false;
                  }

                  // Проверяем, нет ли других вершин внутри треугольника
                  for (int i = 0; i < vertexCount; i++)
                  {
                        if (i == prevIndex || i == vertexIndex || i == nextIndex)
                              continue;

                        Vector3 testPoint = allVertices[activeVertices[i]];
                        if (IsPointInTriangle(testPoint, prev, curr, next, polygonNormal))
                        {
                              return false;
                        }
                  }

                  return true;
            }

            /// <summary>
            /// Проверяет, является ли вершина выпуклой
            /// </summary>
            private static bool IsConvex(Vector3 prev, Vector3 curr, Vector3 next, Vector3 polygonNormal)
            {
                  Vector3 edge1 = curr - prev;
                  Vector3 edge2 = next - curr;
                  Vector3 cross = Vector3.Cross(edge1, edge2);

                  return Vector3.Dot(cross, polygonNormal) >= 0;
            }

            /// <summary>
            /// Проверяет, находится ли точка внутри треугольника (с использованием барицентрических координат)
            /// </summary>
            private static bool IsPointInTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c, Vector3 normal)
            {
                  // Проецируем на наиболее подходящую плоскость
                  Vector2 p2d, a2d, b2d, c2d;

                  // Выбираем плоскость проекции на основе нормали
                  if (Mathf.Abs(normal.x) > Mathf.Abs(normal.y) && Mathf.Abs(normal.x) > Mathf.Abs(normal.z))
                  {
                        // Проецируем на YZ плоскость
                        p2d = new Vector2(p.y, p.z);
                        a2d = new Vector2(a.y, a.z);
                        b2d = new Vector2(b.y, b.z);
                        c2d = new Vector2(c.y, c.z);
                  }
                  else if (Mathf.Abs(normal.y) > Mathf.Abs(normal.z))
                  {
                        // Проецируем на XZ плоскость
                        p2d = new Vector2(p.x, p.z);
                        a2d = new Vector2(a.x, a.z);
                        b2d = new Vector2(b.x, b.z);
                        c2d = new Vector2(c.x, c.z);
                  }
                  else
                  {
                        // Проецируем на XY плоскость
                        p2d = new Vector2(p.x, p.y);
                        a2d = new Vector2(a.x, a.y);
                        b2d = new Vector2(b.x, b.y);
                        c2d = new Vector2(c.x, c.y);
                  }

                  return IsPointInTriangle2D(p2d, a2d, b2d, c2d);
            }

            /// <summary>
            /// 2D версия проверки точки в треугольнике
            /// </summary>
            private static bool IsPointInTriangle2D(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
            {
                  float sign = Sign(p, a, b);
                  float sign2 = Sign(p, b, c);
                  float sign3 = Sign(p, c, a);

                  bool hasNegative = (sign < 0) || (sign2 < 0) || (sign3 < 0);
                  bool hasPositive = (sign > 0) || (sign2 > 0) || (sign3 > 0);

                  return !(hasNegative && hasPositive);
            }

            private static float Sign(Vector2 p1, Vector2 p2, Vector2 p3)
            {
                  return (p1.x - p3.x) * (p2.y - p3.y) - (p2.x - p3.x) * (p1.y - p3.y);
            }

            /// <summary>
            /// Вычисляет нормаль полигона используя метод Ньюэлла
            /// </summary>
            private static Vector3 CalculatePolygonNormal(List<Vector3> vertices)
            {
                  Vector3 normal = Vector3.zero;

                  for (int i = 0; i < vertices.Count; i++)
                  {
                        Vector3 current = vertices[i];
                        Vector3 next = vertices[(i + 1) % vertices.Count];

                        normal.x += (current.y - next.y) * (current.z + next.z);
                        normal.y += (current.z - next.z) * (current.x + next.x);
                        normal.z += (current.x - next.x) * (current.y + next.y);
                  }

                  return normal.normalized;
            }

            /// <summary>
            /// Триангулирует полигон с отверстиями
            /// </summary>
            public static int[] TriangulateWithHoles(List<Vector3> outerContour, List<List<Vector3>> holes)
            {
                  if (holes == null || holes.Count == 0)
                  {
                        return Triangulate(outerContour);
                  }

                  // Для простоты, эта реализация пока не поддерживает отверстия
                  // В будущем здесь можно реализовать алгоритм соединения контуров мостами
                  Debug.LogWarning("EarClipping: Триангуляция с отверстиями пока не реализована. Триангулируется только внешний контур.");
                  return Triangulate(outerContour);
            }

            /// <summary>
            /// Проверяет корректность полигона перед триангуляцией
            /// </summary>
            public static bool ValidatePolygon(List<Vector3> vertices)
            {
                  if (vertices == null || vertices.Count < 3)
                  {
                        Debug.LogError("Полигон должен содержать минимум 3 вершины");
                        return false;
                  }

                  // Проверка на дублированные вершины
                  for (int i = 0; i < vertices.Count; i++)
                  {
                        Vector3 current = vertices[i];
                        Vector3 next = vertices[(i + 1) % vertices.Count];

                        if (Vector3.Distance(current, next) < 0.001f)
                        {
                              Debug.LogWarning($"Обнаружены дублированные вершины в позициях {i} и {(i + 1) % vertices.Count}");
                              return false;
                        }
                  }

                  // Проверка на коллинеарность всех точек
                  if (vertices.Count >= 3)
                  {
                        Vector3 normal = CalculatePolygonNormal(vertices);
                        if (normal.magnitude < 0.001f)
                        {
                              Debug.LogError("Все вершины полигона коллинеарны");
                              return false;
                        }
                  }

                  return true;
            }
      }
}