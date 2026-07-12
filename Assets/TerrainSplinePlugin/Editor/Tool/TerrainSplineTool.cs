// TerrainSplinePlugin - Editor Only
// Unity 2022.3 LTS Compatible

using System.Collections.Generic;
using TerrainSplinePlugin.Editor.Core;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace TerrainSplinePlugin
{
    /// <summary>
    /// Scene View interactive spline editing tool.
    /// Handles node creation (Ctrl+Click on terrain), node selection,
    /// position handles, tangent handles, and node deletion.
    /// </summary>
    public class TerrainSplineTool
    {
        // ─────────────────────────────────────────────
        // Static State (shared with TerrainSplineWindow)
        // ─────────────────────────────────────────────

        private static TerrainSplineData _activeSpline;
        private static bool _needsPreviewUpdate = false;
        public static bool ShowPreview = true;
        public static bool ShowTangentHandles = true;
        
        /// <summary>The spline currently being edited.</summary>
        public static TerrainSplineData ActiveSpline
        {
            get => _activeSpline;
            set
            {
                if (_activeSpline != value)
                {
                    if (_activeSpline != null)
                        TerrainPreviewManager.RestoreAndClear();

                    _activeSpline = value;

                    if (_activeSpline != null)
                    {
                        if (ShowPreview)
                        {
                            TerrainPreviewManager.BeginPreviewUpdate(_activeSpline);
                            TerrainPreviewManager.ApplyPreview();
                        }
                        _needsPreviewUpdate = false;
                    }
                }
            }
        }

        /// <summary>Reference to the owner window.</summary>
        public static TerrainSplineWindow OwnerWindow;

        /// <summary>Index of the currently selected node (-1 = none).</summary>
        public static int SelectedNodeIndex = -1;

        /// <summary>Whether the tool is actively editing.</summary>
        public static bool IsEditing => ActiveSpline != null;

        // ─────────────────────────────────────────────
        // Colors
        // ─────────────────────────────────────────────
        private static readonly Color SplineColorPath = new Color(1f, 0.85f, 0.2f);
        private static readonly Color SplineColorShape = new Color(0.3f, 1f, 0.5f);
        private static readonly Color NodeColorDefault = new Color(1f, 1f, 1f, 0.9f);
        private static readonly Color NodeColorSelected = new Color(0.2f, 1f, 0.4f);
        private static readonly Color NodeGlowColor = new Color(0.3f, 1f, 0.5f, 0.15f);
        private static readonly Color NodeRingColor = new Color(0.3f, 1f, 0.5f, 0.5f);
        private static readonly Color TangentColor = new Color(0.4f, 0.6f, 1f, 0.9f);
        private static readonly Color TangentHandleColor = new Color(0.5f, 0.7f, 1f, 0.95f);
        private static readonly Color WidthPreviewColor = new Color(0.3f, 0.7f, 1f, 0.15f);
        private static readonly Color ShapeFillColor = new Color(0.3f, 1f, 0.3f, 0.1f);

        // ─────────────────────────────────────────────
        // Registration
        // ─────────────────────────────────────────────

        [InitializeOnLoadMethod]
        private static void Register()
        {
            SceneView.duringSceneGui += OnSceneGUI;
        }

        // ─────────────────────────────────────────────
        // Main Scene GUI
        // ─────────────────────────────────────────────

        private static void OnSceneGUI(SceneView sceneView)
        {
            if (ActiveSpline == null) return;

            Event e = Event.current;

            // Draw the spline
            DrawSpline(ActiveSpline);

            // Draw width/fill preview
            DrawPreview(ActiveSpline);

            // Handle input
            HandleInput(e, sceneView);

            // Draw nodes and handles
            DrawNodesAndHandles(ActiveSpline);

            // Apply preview when dragging finishes
            if (GUIUtility.hotControl == 0 && _needsPreviewUpdate)
            {
                if (ShowPreview)
                {
                    TerrainPreviewManager.BeginPreviewUpdate(ActiveSpline);
                    TerrainPreviewManager.ApplyPreview();
                }
                _needsPreviewUpdate = false;
            }

            // Keep scene view updating
            if (e.type == EventType.MouseMove || e.type == EventType.MouseDrag)
                sceneView.Repaint();
        }

        // ─────────────────────────────────────────────
        // Input Handling
        // ─────────────────────────────────────────────

        private static void HandleInput(Event e, SceneView sceneView)
        {
            // Ctrl + Click: Add new node to the end
            if (e.type == EventType.MouseDown && e.button == 0 && e.control && !e.shift && !e.alt)
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                if (RaycastTerrain(ActiveSpline, ray, out Vector3 hitPoint, out Vector3 hitNormal))
                {
                    Undo.RecordObject(ActiveSpline, "Add Spline Point");
                    ActiveSpline.AddPoint(hitPoint);
                    
                    int idx = ActiveSpline.points.Count - 1;
                    var pt = ActiveSpline.points[idx];
                    pt.rotation = Quaternion.FromToRotation(Vector3.up, hitNormal);
                    ActiveSpline.points[idx] = pt;
                    
                    SelectedNodeIndex = idx;
                    EditorUtility.SetDirty(ActiveSpline);
                    
                    _needsPreviewUpdate = true;
                    
                    e.Use();
                }
            }

            // Shift + Click: Insert new node into the closest segment
            if (e.type == EventType.MouseDown && e.button == 0 && e.shift && !e.control && !e.alt)
            {
                Ray ray = HandleUtility.GUIPointToWorldRay(e.mousePosition);
                if (RaycastTerrain(ActiveSpline, ray, out Vector3 hitPoint, out Vector3 hitNormal))
                {
                    int bestSegment = -1;
                    float minDist = float.MaxValue;

                    for (int i = 0; i < ActiveSpline.SegmentCount; i++)
                    {
                        float dist = GetClosestDistanceToSegment(ActiveSpline, i, hitPoint);
                        if (dist < minDist)
                        {
                            minDist = dist;
                            bestSegment = i;
                        }
                    }

                    // If we found a valid segment, insert the point after it
                    if (bestSegment != -1)
                    {
                        Undo.RecordObject(ActiveSpline, "Insert Spline Point");
                        ActiveSpline.InsertPoint(bestSegment + 1, hitPoint);
                        
                        int idx = bestSegment + 1;
                        var pt = ActiveSpline.points[idx];
                        pt.rotation = Quaternion.FromToRotation(Vector3.up, hitNormal);
                        ActiveSpline.points[idx] = pt;
                        
                        SelectedNodeIndex = idx;
                        EditorUtility.SetDirty(ActiveSpline);
                        e.Use();
                    }
                }
            }

            // Delete / Backspace: Remove selected node
            if (e.type == EventType.KeyDown && (e.keyCode == KeyCode.Delete || e.keyCode == KeyCode.Backspace))
            {
                if (SelectedNodeIndex >= 0 && SelectedNodeIndex < ActiveSpline.points.Count)
                {
                    Undo.RecordObject(ActiveSpline, "Delete Spline Point");
                    ActiveSpline.RemovePoint(SelectedNodeIndex);
                    SelectedNodeIndex = Mathf.Min(SelectedNodeIndex, ActiveSpline.points.Count - 1);
                    EditorUtility.SetDirty(ActiveSpline);
                    
                    if (ShowPreview)
                    {
                        TerrainPreviewManager.BeginPreviewUpdate(ActiveSpline);
                        TerrainPreviewManager.ApplyPreview();
                    }
                    _needsPreviewUpdate = false;
                    
                    e.Use();
                }
            }

            // Escape: Deselect / stop editing
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Escape)
            {
                if (SelectedNodeIndex >= 0)
                {
                    SelectedNodeIndex = -1;
                    e.Use();
                }
                else
                {
                    ActiveSpline = null;
                    e.Use();
                }
            }

            // Tab: Cycle through nodes
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.Tab && ActiveSpline.points.Count > 0)
            {
                if (e.shift)
                    SelectedNodeIndex = (SelectedNodeIndex - 1 + ActiveSpline.points.Count) % ActiveSpline.points.Count;
                else
                    SelectedNodeIndex = (SelectedNodeIndex + 1) % ActiveSpline.points.Count;
                e.Use();
                SceneView.RepaintAll();
            }
            // Toggle Local/Global space (X Key)
            if (e.type == EventType.KeyDown && e.keyCode == KeyCode.X)
            {
                Tools.pivotRotation = Tools.pivotRotation == PivotRotation.Local ? PivotRotation.Global : PivotRotation.Local;
                e.Use();
                SceneView.RepaintAll();
            }

            // Focus (FrameSelected command)
            bool isFrameCommand = (e.type == EventType.ValidateCommand || e.type == EventType.ExecuteCommand) && e.commandName == "FrameSelected";
            bool isFKey = e.type == EventType.KeyDown && e.keyCode == KeyCode.F;

            if ((isFrameCommand || isFKey) && ActiveSpline != null && ActiveSpline.points.Count > 0)
            {
                if (e.type == EventType.ExecuteCommand || e.type == EventType.KeyDown)
                {
                    // Frame selected node
                    if (e.keyCode == KeyCode.F && SelectedNodeIndex >= 0 && SelectedNodeIndex < ActiveSpline.points.Count)
                    {
                        sceneView.FrameSelected();
                        sceneView.LookAt(ActiveSpline.points[SelectedNodeIndex].position);
                        e.Use();
                    }

                    if (SelectedNodeIndex >= 0 && SelectedNodeIndex < ActiveSpline.points.Count && e.type != EventType.KeyDown)
                    {
                        Vector3 pos = ActiveSpline.points[SelectedNodeIndex].position;
                        // Frame selected node
                        sceneView.Frame(new Bounds(pos, Vector3.one * 10f), false);
                    }
                    else if (e.type != EventType.KeyDown)
                    {
                        // Frame entire spline
                        Bounds bounds = new Bounds(ActiveSpline.points[0].position, Vector3.zero);
                        for (int i = 1; i < ActiveSpline.points.Count; i++)
                        {
                            bounds.Encapsulate(ActiveSpline.points[i].position);
                        }
                        sceneView.Frame(bounds, false);
                    }
                }
                e.Use();
            }
        }

        // ─────────────────────────────────────────────
        // Node & Handle Drawing
        // ─────────────────────────────────────────────

        private static void DrawNodesAndHandles(TerrainSplineData data)
        {
            if (data.points.Count == 0) return;

            for (int i = 0; i < data.points.Count; i++)
            {
                SplinePoint pt = data.points[i];
                bool isSelected = (i == SelectedNodeIndex);
                float handleSize = HandleUtility.GetHandleSize(pt.position);

                // ── Node Selection (Click to select) ──
                float nodeSize = handleSize * 0.05f;

                if (isSelected)
                {
                    // Glow ring for selected node
                    Vector3 camFwd = Camera.current != null ? Camera.current.transform.forward : Vector3.forward;
                    Handles.color = NodeGlowColor;
                    Handles.DrawSolidDisc(pt.position, camFwd, handleSize * 0.16f);
                    Handles.color = NodeRingColor;
                    Handles.DrawWireDisc(pt.position, camFwd, handleSize * 0.16f, 2f);
                }

                // Clickable dot
                Handles.color = isSelected ? NodeColorSelected : NodeColorDefault;
                if (Handles.Button(pt.position, Quaternion.identity, nodeSize, nodeSize * 2f, Handles.DotHandleCap))
                {
                    SelectedNodeIndex = i;
                    SceneView.RepaintAll();
                }

                if (!isSelected) continue;

                // Fix uninitialized rotation/scale on old assets
                float sqrMag = pt.rotation.x * pt.rotation.x + pt.rotation.y * pt.rotation.y + pt.rotation.z * pt.rotation.z + pt.rotation.w * pt.rotation.w;
                if (sqrMag < 0.001f)
                    pt.rotation = Quaternion.identity;
                else
                    pt.rotation = pt.rotation.normalized;

                if (pt.scale == Vector3.zero)
                    pt.scale = Vector3.one;

                // Determine handle rotation space
                Quaternion handleRot = Tools.pivotRotation == PivotRotation.Local ? pt.rotation : Quaternion.identity;

                // ── Transformation Handles (Q, W, E, R) ──
                if (Tools.current == Tool.Move)
                {
                    EditorGUI.BeginChangeCheck();
                    Vector3 newPos = Handles.PositionHandle(pt.position, handleRot);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(data, "Move Spline Point");
                        
                        if (Event.current.control)
                        {
                            Ray ray = new Ray(newPos + Vector3.up * 1000f, Vector3.down);
                            if (RaycastTerrain(data, ray, out Vector3 hitPoint, out Vector3 hitNormal))
                            {
                                newPos = hitPoint;
                                Vector3 forward = pt.rotation * Vector3.forward;
                                Vector3 projectedForward = Vector3.ProjectOnPlane(forward, hitNormal).normalized;
                                if (projectedForward != Vector3.zero)
                                {
                                    pt.rotation = Quaternion.LookRotation(projectedForward, hitNormal);
                                }
                                else 
                                {
                                    pt.rotation = Quaternion.FromToRotation(Vector3.up, hitNormal);
                                }
                            }
                        }
                        
                        pt.position = newPos;
                        data.points[i] = pt;
                        EditorUtility.SetDirty(data);
                        _needsPreviewUpdate = true;
                    }
                }
                else if (Tools.current == Tool.Rotate)
                {
                    EditorGUI.BeginChangeCheck();
                    Quaternion newHandleRot = Handles.RotationHandle(handleRot, pt.position);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(data, "Rotate Spline Point");
                        // Calculate delta rotation
                        Quaternion deltaRot = newHandleRot * Quaternion.Inverse(handleRot);
                        
                        // Apply delta to node rotation and tangents
                        pt.tangentIn = deltaRot * pt.tangentIn;
                        pt.tangentOut = deltaRot * pt.tangentOut;
                        pt.rotation = deltaRot * pt.rotation;
                        
                        data.points[i] = pt;
                        EditorUtility.SetDirty(data);
                        _needsPreviewUpdate = true;
                    }
                }
                else if (Tools.current == Tool.Scale)
                {
                    // Radius/Scale Handle
                    EditorGUI.BeginChangeCheck();
                    Handles.color = new Color(0.1f, 0.6f, 1f, 0.4f);
                    float currentRadius = data.brushSize * pt.scale.x;
                    float newRadius = Handles.RadiusHandle(pt.rotation, pt.position, currentRadius);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(data, "Scale Spline Point");
                        float baseSize = Mathf.Max(0.01f, data.brushSize);
                        float scaleFactor = Mathf.Max(0.01f, newRadius / baseSize);
                        pt.scale = new Vector3(scaleFactor, pt.scale.y, scaleFactor);
                        data.points[i] = pt;
                        EditorUtility.SetDirty(data);
                        _needsPreviewUpdate = true;
                    }

                    // Standard Scale Handle
                    EditorGUI.BeginChangeCheck();
                    Vector3 newScale = Handles.ScaleHandle(pt.scale, pt.position, pt.rotation, handleSize * 1.5f);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(data, "Scale Spline Point");
                        pt.scale = newScale;
                        data.points[i] = pt;
                        EditorUtility.SetDirty(data);
                        _needsPreviewUpdate = true;
                    }
                }
                // Fallback handle for View/Rect tools
                else
                {
                    EditorGUI.BeginChangeCheck();
                    Vector3 newPos = Handles.PositionHandle(pt.position, handleRot);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(data, "Move Spline Point");
                        pt.position = newPos;
                        data.points[i] = pt;
                        EditorUtility.SetDirty(data);
                        _needsPreviewUpdate = true;
                    }
                }

                // ── Tangent Handles ──
                if (ShowTangentHandles)
                {
                    // Tangent In — dashed line + rectangle handle
                    Handles.color = TangentColor;
                    Handles.DrawDottedLine(pt.position, pt.TangentInWorld, 3f);

                    Handles.color = TangentHandleColor;
                    EditorGUI.BeginChangeCheck();
                    Vector3 newTangentIn = Handles.FreeMoveHandle(
                        pt.TangentInWorld, handleSize * 0.08f,
                        Vector3.zero, Handles.RectangleHandleCap);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(data, "Adjust Tangent In");
                        pt.tangentIn = newTangentIn - pt.position;
                        pt.EnforceHandleMode(true);
                        data.points[i] = pt;
                        EditorUtility.SetDirty(data);
                        _needsPreviewUpdate = true;
                    }

                    // Tangent Out — dashed line + rectangle handle
                    Handles.color = TangentColor;
                    Handles.DrawDottedLine(pt.position, pt.TangentOutWorld, 3f);

                    Handles.color = TangentHandleColor;
                    EditorGUI.BeginChangeCheck();
                    Vector3 newTangentOut = Handles.FreeMoveHandle(
                        pt.TangentOutWorld, handleSize * 0.08f,
                        Vector3.zero, Handles.RectangleHandleCap);
                    if (EditorGUI.EndChangeCheck())
                    {
                        Undo.RecordObject(data, "Adjust Tangent Out");
                        pt.tangentOut = newTangentOut - pt.position;
                        pt.EnforceHandleMode(false);
                        data.points[i] = pt;
                        EditorUtility.SetDirty(data);
                        _needsPreviewUpdate = true;
                    }
                }
            }
        }

        // ─────────────────────────────────────────────
        // Spline Drawing
        // ─────────────────────────────────────────────

        private static void DrawSpline(TerrainSplineData data)
        {
            if (data.points.Count < 2) return;

            Color color = data.splineMode == SplineMode.Path ? SplineColorPath : SplineColorShape;

            for (int i = 0; i < data.SegmentCount; i++)
            {
                SplinePoint a = data.points[i];
                SplinePoint b = data.points[(i + 1) % data.points.Count];

                Handles.DrawBezier(
                    a.position, b.position,
                    a.TangentOutWorld, b.TangentInWorld,
                    color, null, 3f);
            }
        }

        // ─────────────────────────────────────────────
        // Preview Drawing
        // ─────────────────────────────────────────────

        private static void DrawPreview(TerrainSplineData data)
        {
            if (data.points.Count < 2) return;

            if (data.splineMode == SplineMode.Path)
            {
                // Mavi renkli preview kaldırıldı. Canlı arazi önizlemesi zaten aktif.
            }
            else if (data.splineMode == SplineMode.Shape && data.points.Count >= 3)
            {
                DrawShapeFillPreview(data);
            }
        }

        private static void DrawPathWidthPreview(TerrainSplineData data)
        {
            List<SplineSample> samples = SplineUtils.SampleSplineDetailed(data, Mathf.Max(0.5f, data.sampleStep * 2f));
            if (samples.Count < 2) return;

            Color fillColor = new Color(0.2f, 0.6f, 1f, 0.25f);
            Color edgeColor = new Color(0.4f, 0.8f, 1f, 0.8f);

            Handles.zTest = UnityEngine.Rendering.CompareFunction.LessEqual;

            for (int i = 0; i < samples.Count; i++)
            {
                SplineSample s = samples[i];
                Vector3 right = s.rotation * Vector3.right;
                float scaledRadius = data.brushSize * s.scale.x;

                Vector3 leftPt = s.position - right * scaledRadius;
                Vector3 rightPt = s.position + right * scaledRadius;

                if (i > 0)
                {
                    SplineSample prevS = samples[i - 1];
                    Vector3 prevRight = prevS.rotation * Vector3.right;
                    float prevScaledRadius = data.brushSize * prevS.scale.x;

                    Vector3 prevLeftPt = prevS.position - prevRight * prevScaledRadius;
                    Vector3 prevRightPt = prevS.position + prevRight * prevScaledRadius;

                    // Draw filled quad
                    Handles.color = fillColor;
                    Handles.DrawAAConvexPolygon(prevLeftPt, leftPt, rightPt, prevRightPt);

                    // Draw edges
                    Handles.color = edgeColor;
                    Handles.DrawLine(prevLeftPt, leftPt, 2f);
                    Handles.DrawLine(prevRightPt, rightPt, 2f);
                }

                // Draw occasional cross section
                if (i % 5 == 0)
                {
                    Handles.color = new Color(edgeColor.r, edgeColor.g, edgeColor.b, 0.3f);
                    Handles.DrawLine(leftPt, rightPt);
                }
            }
        }

        /// <summary>
        /// Draw a filled polygon preview for Shape mode.
        /// </summary>
        private static void DrawShapeFillPreview(TerrainSplineData data)
        {
            List<Vector3> samples = SplineUtils.SampleSpline(data, data.sampleStep * 2f);
            if (samples.Count < 3) return;

            // Draw filled polygon using triangles
            Handles.color = ShapeFillColor;
            Vector3 center = Vector3.zero;
            foreach (var s in samples) center += s;
            center /= samples.Count;

            for (int i = 0; i < samples.Count; i++)
            {
                int next = (i + 1) % samples.Count;
                Handles.DrawAAConvexPolygon(center, samples[i], samples[next]);
            }

            // Draw outline
            Handles.color = SplineColorShape;
            for (int i = 0; i < samples.Count; i++)
            {
                int next = (i + 1) % samples.Count;
                Handles.DrawLine(samples[i], samples[next]);
            }
        }

        // ─────────────────────────────────────────────

        // ─────────────────────────────────────────────
        // Utility
        // ─────────────────────────────────────────────

        /// <summary>
        /// Calculates the shortest squared distance from a point to a specific spline segment.
        /// </summary>
        private static float GetClosestDistanceToSegment(TerrainSplineData data, int segmentIndex, Vector3 point)
        {
            SplinePoint a = data.points[segmentIndex];
            SplinePoint b = data.points[(segmentIndex + 1) % data.points.Count];
            SplineUtils.GetSegmentControlPoints(a, b, out var p0, out var p1, out var p2, out var p3);

            float minDistSq = float.MaxValue;
            int steps = 20; // Approximation steps
            for (int i = 0; i <= steps; i++)
            {
                float t = (float)i / steps;
                Vector3 p = SplineUtils.CubicBezier(p0, p1, p2, p3, t);
                float distSq = (p - point).sqrMagnitude;
                if (distSq < minDistSq)
                {
                    minDistSq = distSq;
                }
            }
            return minDistSq;
        }

        /// <summary>
        /// Raycast against the terrain to find the hit point and surface normal.
        /// </summary>
        public static bool RaycastTerrain(TerrainSplineData data, Ray ray, out Vector3 hitPoint, out Vector3 hitNormal)
        {
            hitPoint = Vector3.zero;
            hitNormal = Vector3.up;

            // Try the assigned terrain first
            Terrain terrain = data.targetTerrain;
            if (terrain == null)
            {
                // Fallback: find any terrain in scene
                terrain = Object.FindObjectOfType<Terrain>();
            }

            if (terrain == null) return false;

            // Use Unity's terrain raycast
            if (terrain.GetComponent<Collider>() != null)
            {
                if (terrain.GetComponent<Collider>().Raycast(ray, out RaycastHit hit, 10000f))
                {
                    hitPoint = hit.point;
                    hitNormal = hit.normal;
                    return true;
                }
            }

            // Fallback: manual plane intersection at terrain height
            Plane terrainPlane = new Plane(Vector3.up, terrain.transform.position);
            if (terrainPlane.Raycast(ray, out float enter))
            {
                Vector3 point = ray.GetPoint(enter);
                // Sample terrain height at this position
                float terrainHeight = terrain.SampleHeight(point);
                hitPoint = new Vector3(point.x, terrainHeight + terrain.transform.position.y, point.z);
                
                // Sample terrain normal
                Vector3 localPos = hitPoint - terrain.transform.position;
                float normX = Mathf.InverseLerp(0, terrain.terrainData.size.x, localPos.x);
                float normZ = Mathf.InverseLerp(0, terrain.terrainData.size.z, localPos.z);
                hitNormal = terrain.terrainData.GetInterpolatedNormal(normX, normZ);
                return true;
            }

            return false;
        }
    }
}
