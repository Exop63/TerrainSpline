// TerrainSplinePlugin - Editor Only
// Unity 2022.3 LTS Compatible

using UnityEngine;

namespace TerrainSplinePlugin
{
    /// <summary>
    /// Utility class for 2D polygon operations.
    /// Used by Shape mode (Fill Pen) to determine which terrain pixels
    /// are inside the closed spline area.
    /// </summary>
    public static class PolygonUtils
    {
        /// <summary>
        /// Test if a 2D point is inside a polygon using the ray casting algorithm.
        /// Works with any simple (non-self-intersecting) polygon.
        /// </summary>
        /// <param name="point">The point to test (XZ plane)</param>
        /// <param name="polygon">Array of polygon vertices in order</param>
        /// <returns>True if the point is inside the polygon</returns>
        public static bool IsPointInPolygon(Vector2 point, Vector2[] polygon)
        {
            if (polygon == null || polygon.Length < 3) return false;

            bool inside = false;
            int count = polygon.Length;

            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                if (((polygon[i].y > point.y) != (polygon[j].y > point.y)) &&
                    (point.x < (polygon[j].x - polygon[i].x) * (point.y - polygon[i].y) /
                    (polygon[j].y - polygon[i].y) + polygon[i].x))
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        /// <summary>
        /// Calculate the axis-aligned bounding box of a polygon.
        /// </summary>
        /// <param name="polygon">Array of polygon vertices</param>
        /// <param name="min">Output minimum corner</param>
        /// <param name="max">Output maximum corner</param>
        public static void GetPolygonBounds(Vector2[] polygon, out Vector2 min, out Vector2 max)
        {
            min = new Vector2(float.MaxValue, float.MaxValue);
            max = new Vector2(float.MinValue, float.MinValue);

            if (polygon == null || polygon.Length == 0) return;

            for (int i = 0; i < polygon.Length; i++)
            {
                min.x = Mathf.Min(min.x, polygon[i].x);
                min.y = Mathf.Min(min.y, polygon[i].y);
                max.x = Mathf.Max(max.x, polygon[i].x);
                max.y = Mathf.Max(max.y, polygon[i].y);
            }
        }

        /// <summary>
        /// Calculate the minimum distance from a point to the polygon edge.
        /// Used for edge falloff calculations in Shape mode.
        /// </summary>
        /// <param name="point">The point to measure from</param>
        /// <param name="polygon">Array of polygon vertices</param>
        /// <returns>Minimum distance to the closest polygon edge</returns>
        public static float DistanceToPolygonEdge(Vector2 point, Vector2[] polygon)
        {
            if (polygon == null || polygon.Length < 2) return float.MaxValue;

            float minDist = float.MaxValue;
            int count = polygon.Length;

            for (int i = 0; i < count; i++)
            {
                int j = (i + 1) % count;
                float dist = DistanceToLineSegment(point, polygon[i], polygon[j]);
                minDist = Mathf.Min(minDist, dist);
            }

            return minDist;
        }

        /// <summary>
        /// Calculate the minimum distance from a point to a line segment.
        /// </summary>
        private static float DistanceToLineSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 ab = b - a;
            float lengthSq = ab.sqrMagnitude;

            if (lengthSq < 0.0001f) return Vector2.Distance(point, a);

            float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / lengthSq);
            Vector2 projection = a + t * ab;

            return Vector2.Distance(point, projection);
        }

        /// <summary>
        /// Calculate the signed area of a polygon.
        /// Positive = counter-clockwise, negative = clockwise.
        /// </summary>
        public static float CalculateSignedArea(Vector2[] polygon)
        {
            if (polygon == null || polygon.Length < 3) return 0f;

            float area = 0f;
            int count = polygon.Length;

            for (int i = 0; i < count; i++)
            {
                int j = (i + 1) % count;
                area += polygon[i].x * polygon[j].y;
                area -= polygon[j].x * polygon[i].y;
            }

            return area * 0.5f;
        }

        public static float CalculateContinuousEdgeFalloff(bool isInside, float edgeDist, float falloffDistance)
        {
            float sd = isInside ? edgeDist : -edgeDist;
            float strength = 1f;

            if (falloffDistance > 0.001f)
            {
                float t = (sd + falloffDistance) / (2f * falloffDistance);
                t = Mathf.Clamp01(t);
                strength = Mathf.SmoothStep(0f, 1f, t);
            }
            else
            {
                strength = isInside ? 1f : 0f;
            }

            return strength;
        }
    }
}
