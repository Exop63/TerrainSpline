// TerrainSplinePlugin - Editor Only
// Unity 2022.3 LTS Compatible

using System.Collections.Generic;
using UnityEngine;

namespace TerrainSplinePlugin
{
    /// <summary>
    /// Represents a fully evaluated point on the spline including rotation and scale.
    /// </summary>
    public struct SplineSample
    {
        public Vector3 position;
        public Vector3 tangent;
        public Quaternion rotation;
        public Vector3 scale;
    }

    /// <summary>
    /// Utility class for cubic bezier spline calculations.
    /// All math operations for sampling, interpolation, and measurement.
    /// </summary>
    public static class SplineUtils
    {
        /// <summary>
        /// Evaluate a cubic bezier curve at parameter t.
        /// </summary>
        /// <param name="p0">Start point</param>
        /// <param name="p1">Start tangent (world space)</param>
        /// <param name="p2">End tangent (world space)</param>
        /// <param name="p3">End point</param>
        /// <param name="t">Parameter [0, 1]</param>
        public static Vector3 CubicBezier(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float u = 1f - t;
            float tt = t * t;
            float uu = u * u;
            float uuu = uu * u;
            float ttt = tt * t;

            return uuu * p0 + 3f * uu * t * p1 + 3f * u * tt * p2 + ttt * p3;
        }

        /// <summary>
        /// Evaluate the first derivative (tangent) of a cubic bezier at parameter t.
        /// </summary>
        public static Vector3 CubicBezierDerivative(Vector3 p0, Vector3 p1, Vector3 p2, Vector3 p3, float t)
        {
            float u = 1f - t;
            float uu = u * u;
            float tt = t * t;

            return 3f * uu * (p1 - p0) + 6f * u * t * (p2 - p1) + 3f * tt * (p3 - p2);
        }

        /// <summary>
        /// Get the four control points for a bezier segment between two spline points.
        /// </summary>
        public static void GetSegmentControlPoints(SplinePoint a, SplinePoint b,
            out Vector3 p0, out Vector3 p1, out Vector3 p2, out Vector3 p3)
        {
            p0 = a.position;
            p1 = a.TangentOutWorld;
            p2 = b.TangentInWorld;
            p3 = b.position;
        }

        /// <summary>
        /// Approximate the arc length of a single bezier segment using subdivision.
        /// </summary>
        public static float SegmentLength(SplinePoint a, SplinePoint b, int subdivisions = 20)
        {
            GetSegmentControlPoints(a, b, out var p0, out var p1, out var p2, out var p3);

            float length = 0f;
            Vector3 prev = p0;
            for (int i = 1; i <= subdivisions; i++)
            {
                float t = (float)i / subdivisions;
                Vector3 current = CubicBezier(p0, p1, p2, p3, t);
                length += Vector3.Distance(prev, current);
                prev = current;
            }
            return length;
        }

        /// <summary>
        /// Calculate the total arc length of the entire spline.
        /// </summary>
        public static float CalculateSplineLength(TerrainSplineData data)
        {
            if (data.points.Count < 2) return 0f;

            float totalLength = 0f;
            int segCount = data.SegmentCount;

            for (int i = 0; i < segCount; i++)
            {
                SplinePoint a = data.points[i];
                SplinePoint b = data.points[(i + 1) % data.points.Count];
                totalLength += SegmentLength(a, b);
            }

            return totalLength;
        }

        /// <summary>
        /// Sample the spline at regular world-space intervals.
        /// Returns a list of world-space positions along the spline.
        /// </summary>
        /// <param name="data">The spline data to sample</param>
        /// <param name="sampleStep">Distance between samples in meters</param>
        /// <returns>List of sampled world positions</returns>
        public static List<Vector3> SampleSpline(TerrainSplineData data, float sampleStep)
        {
            List<Vector3> samples = new List<Vector3>();
            if (data.points.Count < 2) return samples;

            int segCount = data.SegmentCount;
            float stepSafe = Mathf.Max(sampleStep, 0.05f);

            for (int seg = 0; seg < segCount; seg++)
            {
                SplinePoint a = data.points[seg];
                SplinePoint b = data.points[(seg + 1) % data.points.Count];
                GetSegmentControlPoints(a, b, out var p0, out var p1, out var p2, out var p3);

                float segLength = SegmentLength(a, b);
                int sampleCount = Mathf.Max(1, Mathf.CeilToInt(segLength / stepSafe));

                for (int i = 0; i < sampleCount; i++)
                {
                    float t = (float)i / sampleCount;
                    samples.Add(CubicBezier(p0, p1, p2, p3, t));
                }
            }

            // Add the last point (unless closed loop, where it connects back)
            if (!data.isClosedLoop && data.points.Count > 0)
            {
                samples.Add(data.points[data.points.Count - 1].position);
            }

            return samples;
        }

        /// <summary>
        /// Sample the spline and return both positions and tangent directions.
        /// </summary>
        public static void SampleSplineWithTangents(TerrainSplineData data, float sampleStep,
            out List<Vector3> positions, out List<Vector3> tangents)
        {
            positions = new List<Vector3>();
            tangents = new List<Vector3>();

            if (data.points.Count < 2) return;

            int segCount = data.SegmentCount;
            float stepSafe = Mathf.Max(sampleStep, 0.05f);

            for (int seg = 0; seg < segCount; seg++)
            {
                SplinePoint a = data.points[seg];
                SplinePoint b = data.points[(seg + 1) % data.points.Count];
                GetSegmentControlPoints(a, b, out var p0, out var p1, out var p2, out var p3);

                float segLength = SegmentLength(a, b);
                int sampleCount = Mathf.Max(1, Mathf.CeilToInt(segLength / stepSafe));

                for (int i = 0; i < sampleCount; i++)
                {
                    float t = (float)i / sampleCount;
                    positions.Add(CubicBezier(p0, p1, p2, p3, t));
                    Vector3 tangent = CubicBezierDerivative(p0, p1, p2, p3, t);
                    tangents.Add(tangent.normalized);
                }
            }

            if (!data.isClosedLoop && data.points.Count > 0)
            {
                positions.Add(data.points[data.points.Count - 1].position);
                // Use the last segment's end tangent
                SplinePoint la = data.points[data.points.Count - 2];
                SplinePoint lb = data.points[data.points.Count - 1];
                GetSegmentControlPoints(la, lb, out var lp0, out var lp1, out var lp2, out var lp3);
                tangents.Add(CubicBezierDerivative(lp0, lp1, lp2, lp3, 1f).normalized);
            }
        }

        /// <summary>
        /// Sample the spline returning full SplineSample data (position, tangent, rotation, scale).
        /// </summary>
        public static List<SplineSample> SampleSplineDetailed(TerrainSplineData data, float sampleStep)
        {
            List<SplineSample> samples = new List<SplineSample>();
            if (data.points.Count < 2) return samples;

            int segCount = data.SegmentCount;
            float stepSafe = Mathf.Max(sampleStep, 0.05f);

            for (int seg = 0; seg < segCount; seg++)
            {
                SplinePoint a = data.points[seg];
                SplinePoint b = data.points[(seg + 1) % data.points.Count];
                GetSegmentControlPoints(a, b, out var p0, out var p1, out var p2, out var p3);

                float segLength = SegmentLength(a, b);
                int sampleCount = Mathf.Max(1, Mathf.CeilToInt(segLength / stepSafe));

                for (int i = 0; i < sampleCount; i++)
                {
                    float t = (float)i / sampleCount;
                    Vector3 pos = CubicBezier(p0, p1, p2, p3, t);
                    Vector3 tan = CubicBezierDerivative(p0, p1, p2, p3, t).normalized;
                    
                    // Fix uninitialized quaternions for older assets
                    bool aIsZero = a.rotation.x == 0 && a.rotation.y == 0 && a.rotation.z == 0 && a.rotation.w == 0;
                    bool bIsZero = b.rotation.x == 0 && b.rotation.y == 0 && b.rotation.z == 0 && b.rotation.w == 0;
                    Quaternion rotA = aIsZero ? Quaternion.identity : a.rotation;
                    Quaternion rotB = bIsZero ? Quaternion.identity : b.rotation;
                    
                    Quaternion rot = Quaternion.Slerp(rotA, rotB, t);
                    Vector3 scale = Vector3.Lerp(a.scale, b.scale, t);
                    
                    samples.Add(new SplineSample
                    {
                        position = pos,
                        tangent = tan,
                        rotation = rot,
                        scale = scale
                    });
                }
            }

            if (!data.isClosedLoop && data.points.Count > 0)
            {
                SplinePoint last = data.points[data.points.Count - 1];
                SplinePoint prev = data.points.Count > 1 ? data.points[data.points.Count - 2] : last;
                GetSegmentControlPoints(prev, last, out var p0, out var p1, out var p2, out var p3);
                
                bool lastIsZero = last.rotation.x == 0 && last.rotation.y == 0 && last.rotation.z == 0 && last.rotation.w == 0;
                Quaternion rotLast = lastIsZero ? Quaternion.identity : last.rotation;
                
                samples.Add(new SplineSample
                {
                    position = last.position,
                    tangent = CubicBezierDerivative(p0, p1, p2, p3, 1f).normalized,
                    rotation = rotLast,
                    scale = last.scale
                });
            }

            return samples;
        }

        /// <summary>
        /// Generate a 2D polygon (XZ projection) from a closed spline for Shape mode.
        /// Used for point-in-polygon testing.
        /// </summary>
        public static Vector2[] GeneratePolygonFromSpline(TerrainSplineData data, float sampleStep)
        {
            List<Vector3> samples = SampleSpline(data, sampleStep);
            Vector2[] polygon = new Vector2[samples.Count];
            for (int i = 0; i < samples.Count; i++)
            {
                polygon[i] = new Vector2(samples[i].x, samples[i].z);
            }
            return polygon;
        }

        /// <summary>
        /// Get the height at a specific point along the spline polygon.
        /// Interpolates between sampled heights.
        /// </summary>
        public static float GetHeightAtPolygonPoint(TerrainSplineData data, float sampleStep, Vector2 xzPoint)
        {
            List<Vector3> samples = SampleSpline(data, sampleStep);
            if (samples.Count == 0) return 0f;

            // Find the closest sample point and return its height
            float minDist = float.MaxValue;
            float height = samples[0].y;

            for (int i = 0; i < samples.Count; i++)
            {
                float dist = Vector2.Distance(xzPoint, new Vector2(samples[i].x, samples[i].z));
                if (dist < minDist)
                {
                    minDist = dist;
                    height = samples[i].y;
                }
            }

            return height;
        }

        /// <summary>
        /// Evaluate a point on a specific segment at parameter t.
        /// </summary>
        public static Vector3 EvaluateSegment(TerrainSplineData data, int segmentIndex, float t)
        {
            SplinePoint a = data.points[segmentIndex];
            SplinePoint b = data.points[(segmentIndex + 1) % data.points.Count];
            GetSegmentControlPoints(a, b, out var p0, out var p1, out var p2, out var p3);
            return CubicBezier(p0, p1, p2, p3, t);
        }

        /// <summary>
        /// Calculate the falloff value based on distance and hardness.
        /// Returns 1 at center, 0 at edge, with curve controlled by hardness.
        /// </summary>
        public static float CalculateFalloff(float distance, float radius, float hardness)
        {
            if (radius <= 0f) return 0f;
            float normalizedDist = Mathf.Clamp01(distance / radius);

            // Hardness controls the falloff curve
            // 0 = very soft (gradual), 1 = very hard (sharp edge)
            float innerRadius = hardness;
            if (normalizedDist <= innerRadius)
                return 1f;

            float t = (normalizedDist - innerRadius) / (1f - innerRadius + 0.0001f);
            return 1f - Mathf.SmoothStep(0f, 1f, t);
        }

        /// <summary>
        /// Gets the distance to the closest sample point and its height.
        /// Useful for distance field operations.
        /// </summary>
        public static void GetClosestPointInfo(List<Vector3> samples, Vector2 xzPoint, out float minDistance, out float height)
        {
            float minDistSq = float.MaxValue;
            height = 0f;

            if (samples == null || samples.Count == 0)
            {
                minDistance = float.MaxValue;
                return;
            }

            for (int i = 0; i < samples.Count; i++)
            {
                float dx = samples[i].x - xzPoint.x;
                float dz = samples[i].z - xzPoint.y;
                float distSq = dx * dx + dz * dz;
                if (distSq < minDistSq)
                {
                    minDistSq = distSq;
                    height = samples[i].y;
                }
            }
            minDistance = Mathf.Sqrt(minDistSq);
        }

        /// <summary>
        /// Gets the distance to the closest SplineSample based on 2D (XZ) distance.
        /// </summary>
        public static void GetClosestSampleInfo(List<SplineSample> samples, Vector2 xzPoint, out float minDistance, out SplineSample closestSample)
        {
            float minDistSq = float.MaxValue;
            closestSample = default;

            if (samples == null || samples.Count == 0)
            {
                minDistance = float.MaxValue;
                return;
            }

            for (int i = 0; i < samples.Count; i++)
            {
                float dx = samples[i].position.x - xzPoint.x;
                float dz = samples[i].position.z - xzPoint.y;
                float distSq = dx * dx + dz * dz;
                if (distSq < minDistSq)
                {
                    minDistSq = distSq;
                    closestSample = samples[i];
                }
            }
            minDistance = Mathf.Sqrt(minDistSq);
        }

        /// <summary>
        /// Gets a blended SplineSample by weighting nearby samples using inverse-distance.
        /// This avoids hard kinks at curves where adjacent samples "fight" each other.
        /// </summary>
        public static void GetBlendedSampleInfo(List<SplineSample> samples, Vector2 xzPoint, float blendRadius, out float minDistance, out SplineSample blendedSample)
        {
            blendedSample = default;
            minDistance = float.MaxValue;

            if (samples == null || samples.Count == 0) return;

            // First pass: find the closest distance
            float minDistSq = float.MaxValue;
            int closestIdx = 0;
            for (int i = 0; i < samples.Count; i++)
            {
                float dx = samples[i].position.x - xzPoint.x;
                float dz = samples[i].position.z - xzPoint.y;
                float distSq = dx * dx + dz * dz;
                if (distSq < minDistSq)
                {
                    minDistSq = distSq;
                    closestIdx = i;
                }
            }
            minDistance = Mathf.Sqrt(minDistSq);

            // Second pass: blend nearby samples weighted by inverse distance
            float blendRadiusSq = blendRadius * blendRadius;
            float totalWeight = 0f;
            Vector3 blendPos = Vector3.zero;
            float blendRotX = 0f, blendRotY = 0f, blendRotZ = 0f, blendRotW = 0f;
            Vector3 blendScale = Vector3.zero;
            Vector3 blendTangent = Vector3.zero;

            // Only check samples near the closest one for performance
            int searchStart = Mathf.Max(0, closestIdx - 8);
            int searchEnd = Mathf.Min(samples.Count - 1, closestIdx + 8);

            for (int i = searchStart; i <= searchEnd; i++)
            {
                float dx = samples[i].position.x - xzPoint.x;
                float dz = samples[i].position.z - xzPoint.y;
                float distSq = dx * dx + dz * dz;

                if (distSq > blendRadiusSq) continue;

                float dist = Mathf.Sqrt(distSq);
                float w = 1f / (dist + 0.01f);
                w = w * w; // Sharpen weighting

                totalWeight += w;
                blendPos += samples[i].position * w;
                blendScale += samples[i].scale * w;
                blendTangent += samples[i].tangent * w;

                // Flip quaternion if needed to avoid opposite hemisphere blending
                Quaternion q = samples[i].rotation;
                if (i > searchStart)
                {
                    Quaternion first = samples[searchStart].rotation;
                    if (Quaternion.Dot(first, q) < 0)
                    {
                        q = new Quaternion(-q.x, -q.y, -q.z, -q.w);
                    }
                }
                blendRotX += q.x * w;
                blendRotY += q.y * w;
                blendRotZ += q.z * w;
                blendRotW += q.w * w;
            }

            if (totalWeight > 0f)
            {
                float invW = 1f / totalWeight;
                blendedSample.position = blendPos * invW;
                blendedSample.scale = blendScale * invW;
                blendedSample.tangent = (blendTangent * invW).normalized;

                Quaternion rawQ = new Quaternion(blendRotX * invW, blendRotY * invW, blendRotZ * invW, blendRotW * invW);
                float mag = Mathf.Sqrt(rawQ.x * rawQ.x + rawQ.y * rawQ.y + rawQ.z * rawQ.z + rawQ.w * rawQ.w);
                if (mag > 0.0001f)
                    blendedSample.rotation = new Quaternion(rawQ.x / mag, rawQ.y / mag, rawQ.z / mag, rawQ.w / mag);
                else
                    blendedSample.rotation = Quaternion.identity;
            }
            else
            {
                blendedSample = samples[closestIdx];
            }
        }
    }
}
