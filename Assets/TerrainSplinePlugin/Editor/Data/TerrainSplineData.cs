// TerrainSplinePlugin - Editor Only
// Unity 2022.3 LTS Compatible

using System.Collections.Generic;
using UnityEngine;

namespace TerrainSplinePlugin
{
    /// <summary>
    /// ScriptableObject that stores all data for a single terrain spline.
    /// Saved as an asset file for persistence between editor sessions.
    /// </summary>
    [CreateAssetMenu(fileName = "New Terrain Spline", menuName = "Terrain Spline Plugin/Terrain Spline Data")]
    public class TerrainSplineData : ScriptableObject
    {
        [Header("General")]
        [Tooltip("Display name for this spline")]
        public string displayName = "New Spline";

        [Tooltip("Order index for sorting in the list")]
        public int orderIndex = 0;

        [Tooltip("Path: affects along the curve. Shape: fills the closed interior.")]
        public SplineMode splineMode = SplineMode.Path;

        [Tooltip("List of spline control points")]
        public List<SplinePoint> points = new List<SplinePoint>();

        [Tooltip("Whether the spline forms a closed loop. Shape mode always uses closed loop.")]
        public bool isClosedLoop = false;

        [Header("Target")]
        [Tooltip("The terrain this spline operates on")]
        public Terrain targetTerrain;

        // ─────────────────────────────────────────────
        // Operation Toggles
        // ─────────────────────────────────────────────
        [Header("Operations")]
        [Tooltip("Enable height modification along/inside the spline")]
        public bool applyHeight = true;

        [Tooltip("Enable texture painting along/inside the spline")]
        public bool applyPaint = false;

        // ─────────────────────────────────────────────
        // Brush Settings (TerraSplines-style)
        // ─────────────────────────────────────────────
        [Header("Brush")]
        [Tooltip("Brush radius in meters")]
        [Range(0.1f, 200f)]
        public float brushSize = 5f;

        [Tooltip("Falloff curve: 0 = soft gradual, 0.5 = linear, 1 = hard sharp")]
        [Range(0f, 1f)]
        public float brushHardness = 0.5f;

        [Tooltip("Blend strength: 0 = no effect, 1 = full effect")]
        [Range(0f, 1f)]
        public float brushStrength = 1f;

        [Tooltip("Distance between sample points along the spline (meters). Lower = more detail, slower.")]
        [Range(0.05f, 30f)]
        public float sampleStep = 1f;

        // ─────────────────────────────────────────────
        // Height Settings
        // ─────────────────────────────────────────────
        [Header("Height")]
        [Tooltip("Height offset relative to spline point height")]
        public float heightOffset = 0f;

        [Tooltip("Target fill height for Shape mode (world units)")]
        public float shapeFillHeight = 0f;

        [Tooltip("Use spline point heights for Shape mode instead of flat fill")]
        public bool shapeUseSplineHeight = true;

        // ─────────────────────────────────────────────
        // Paint Settings
        // ─────────────────────────────────────────────
        [Header("Paint")]
        [Tooltip("Index of the terrain layer to paint")]
        public int paintLayerIndex = 0;

        [Tooltip("Paint blend strength")]
        [Range(0f, 1f)]
        public float paintStrength = 1f;

        [Tooltip("Texture transition smoothness")]
        [Range(0f, 1f)]
        public float paintBlend = 0.5f;

        // ─────────────────────────────────────────────
        // Shape-specific Settings (Fill Pen)
        // ─────────────────────────────────────────────
        [Header("Shape Settings")]
        [Tooltip("Edge falloff distance in meters for shape fill")]
        [Range(0f, 50f)]
        public float shapeEdgeFalloff = 2f;

        // ─────────────────────────────────────────────
        // Computed Properties
        // ─────────────────────────────────────────────

        /// <summary>
        /// Whether this spline has enough points to be valid.
        /// Path needs at least 2, Shape needs at least 3.
        /// </summary>
        public bool IsValid
        {
            get
            {
                if (splineMode == SplineMode.Shape)
                    return points.Count >= 3;
                return points.Count >= 2;
            }
        }

        /// <summary>
        /// Number of bezier segments in this spline.
        /// </summary>
        public int SegmentCount
        {
            get
            {
                if (points.Count < 2) return 0;
                return isClosedLoop ? points.Count : points.Count - 1;
            }
        }

        /// <summary>
        /// Enforce constraints based on spline mode.
        /// Shape mode always uses closed loop.
        /// </summary>
        public void EnforceModeConstraints()
        {
            if (splineMode == SplineMode.Shape)
            {
                isClosedLoop = true;
            }
        }

        /// <summary>
        /// Add a new point to the spline with auto-calculated tangents.
        /// </summary>
        public void AddPoint(Vector3 worldPosition)
        {
            SplinePoint newPoint = new SplinePoint(worldPosition);

            if (points.Count > 0)
            {
                // Auto-calculate tangent direction based on previous point
                Vector3 prevPos = points[points.Count - 1].position;
                Vector3 direction = (worldPosition - prevPos).normalized;
                float tangentLength = Vector3.Distance(worldPosition, prevPos) * 0.33f;
                tangentLength = Mathf.Clamp(tangentLength, 0.5f, 10f);

                newPoint.tangentIn = -direction * tangentLength;
                newPoint.tangentOut = direction * tangentLength;
            }

            points.Add(newPoint);
            EnforceModeConstraints();
        }

        /// <summary>
        /// Insert a point at the given index.
        /// </summary>
        public void InsertPoint(int index, Vector3 worldPosition)
        {
            SplinePoint newPoint = new SplinePoint(worldPosition);

            // Auto-calculate tangents based on neighbors
            if (points.Count >= 2 && index > 0 && index < points.Count)
            {
                Vector3 prev = points[index - 1].position;
                Vector3 next = points[index].position;
                Vector3 direction = (next - prev).normalized;
                float tangentLength = Vector3.Distance(next, prev) * 0.25f;
                tangentLength = Mathf.Clamp(tangentLength, 0.5f, 10f);

                newPoint.tangentIn = -direction * tangentLength;
                newPoint.tangentOut = direction * tangentLength;
            }

            points.Insert(index, newPoint);
            EnforceModeConstraints();
        }

        /// <summary>
        /// Remove a point at the given index.
        /// </summary>
        public void RemovePoint(int index)
        {
            if (index >= 0 && index < points.Count)
            {
                points.RemoveAt(index);
            }
        }
    }
}
