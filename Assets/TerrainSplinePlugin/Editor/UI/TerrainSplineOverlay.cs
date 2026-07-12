// TerrainSplinePlugin - Editor Only
// Unity 2022.3 LTS Compatible — Scene View Overlay

using TerrainSplinePlugin.Editor.Core;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;

namespace TerrainSplinePlugin
{
    /// <summary>
    /// Native Scene View overlay panel for Terrain Spline editing.
    /// Snappable to edges, collapsible, and draggable — just like Unity's built-in panels.
    /// </summary>
    [Overlay(typeof(SceneView), "terrain-spline-overlay", "Terrain Spline")]
    [Icon("d_EditCollider")]
    public class TerrainSplineOverlay : IMGUIOverlay, ITransientOverlay
    {
        public bool visible => TerrainSplineTool.OwnerWindow != null && TerrainSplineTool.ActiveSpline != null;

        public override void OnGUI()
        {
            if (TerrainSplineTool.ActiveSpline == null) return;

            var data = TerrainSplineTool.ActiveSpline;
            bool hasSelection = TerrainSplineTool.SelectedNodeIndex >= 0 &&
                                TerrainSplineTool.SelectedNodeIndex < data.points.Count;
            bool canSplit = hasSelection &&
                            data.splineMode == SplineMode.Path &&
                            data.points.Count >= 3 &&
                            TerrainSplineTool.SelectedNodeIndex > 0 &&
                            TerrainSplineTool.SelectedNodeIndex < data.points.Count - 1;

            // ── Spline Name + Mode ──
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            string newName = EditorGUILayout.TextField(data.displayName, GUILayout.MinWidth(80));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(data, "Rename Spline");
                data.displayName = newName;
                EditorUtility.SetDirty(data);
            }

            EditorGUI.BeginChangeCheck();
            SplineMode newMode = (SplineMode)EditorGUILayout.EnumPopup(data.splineMode, GUILayout.Width(60));
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(data, "Change Spline Mode");
                data.splineMode = newMode;
                data.EnforceModeConstraints();
                EditorUtility.SetDirty(data);

                if (TerrainSplineTool.ShowPreview)
                {
                    TerrainPreviewManager.BeginPreviewUpdate(data);
                    TerrainPreviewManager.ApplyPreview();
                }

                if (TerrainSplineTool.OwnerWindow != null)
                    TerrainSplineTool.OwnerWindow.Repaint();
            }
            EditorGUILayout.EndHorizontal();

            // ── Info ──
            EditorGUILayout.LabelField(
                $"Points: {data.points.Count}  |  Selected: {(hasSelection ? TerrainSplineTool.SelectedNodeIndex.ToString() : "—")}",
                EditorStyles.miniLabel);

            // ── Toggles ──
            EditorGUILayout.BeginHorizontal();
            EditorGUI.BeginChangeCheck();
            TerrainSplineTool.ShowPreview = GUILayout.Toggle(
                TerrainSplineTool.ShowPreview,
                TerrainSplineTool.ShowPreview ? "Preview ●" : "Preview ○",
                EditorStyles.miniButtonLeft, GUILayout.Height(18));
            if (EditorGUI.EndChangeCheck())
            {
                if (TerrainSplineTool.ShowPreview)
                {
                    TerrainPreviewManager.BeginPreviewUpdate(data);
                    TerrainPreviewManager.ApplyPreview();
                }
                else
                {
                    TerrainPreviewManager.RestoreAndClear();
                }
                SceneView.RepaintAll();
            }

            EditorGUI.BeginChangeCheck();
            TerrainSplineTool.ShowTangentHandles = GUILayout.Toggle(
                TerrainSplineTool.ShowTangentHandles,
                TerrainSplineTool.ShowTangentHandles ? "Tangents ●" : "Tangents ○",
                EditorStyles.miniButtonRight, GUILayout.Height(18));
            if (EditorGUI.EndChangeCheck())
            {
                SceneView.RepaintAll();
            }
            EditorGUILayout.EndHorizontal();

            // ── Handle Mode (when node selected) ──
            if (hasSelection)
            {
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Handle:", GUILayout.Width(45));
                SplinePoint pt = data.points[TerrainSplineTool.SelectedNodeIndex];

                EditorGUI.BeginChangeCheck();
                HandleMode newHandleMode = (HandleMode)EditorGUILayout.EnumPopup(pt.handleMode);
                if (EditorGUI.EndChangeCheck())
                {
                    Undo.RecordObject(data, "Change Handle Mode");
                    pt.handleMode = newHandleMode;
                    pt.EnforceHandleMode(true);
                    data.points[TerrainSplineTool.SelectedNodeIndex] = pt;
                    EditorUtility.SetDirty(data);
                }
                EditorGUILayout.EndHorizontal();

                // Split button
                // Actions (Split / Delete)
                EditorGUILayout.BeginHorizontal();
                
                if (canSplit)
                {
                    GUIContent splitContent = new GUIContent(" Split", EditorGUIUtility.IconContent("d_EditCollider").image);
                    if (GUILayout.Button(splitContent, EditorStyles.miniButtonLeft, GUILayout.Height(18)))
                    {
                        if (EditorUtility.DisplayDialog("Split Path",
                            $"Split at node {TerrainSplineTool.SelectedNodeIndex}?\nThis creates a new spline asset.", "Split", "Cancel"))
                        {
                            TerrainSplineData newSpline = SplineOperations.SplitSpline(data, TerrainSplineTool.SelectedNodeIndex);
                            if (newSpline != null)
                            {
                                TerrainSplineTool.SelectedNodeIndex = -1;
                                if (TerrainSplineTool.ShowPreview)
                                {
                                    TerrainPreviewManager.BeginPreviewUpdate(data);
                                    TerrainPreviewManager.ApplyPreview();
                                }
                            }
                        }
                    }
                }

                // Delete Node Button
                GUIStyle deleteBtnStyle = canSplit ? EditorStyles.miniButtonRight : EditorStyles.miniButton;
                GUIContent deleteContent = new GUIContent(" Delete", EditorGUIUtility.IconContent("TreeEditor.Trash").image);
                if (GUILayout.Button(deleteContent, deleteBtnStyle, GUILayout.Height(18)))
                {
                    if (data.points.Count > 2)
                    {
                        data.RemovePoint(TerrainSplineTool.SelectedNodeIndex);
                        TerrainSplineTool.SelectedNodeIndex = -1;
                        if (TerrainSplineTool.ShowPreview)
                        {
                            TerrainPreviewManager.BeginPreviewUpdate(data);
                            TerrainPreviewManager.ApplyPreview();
                        }
                        SceneView.RepaintAll();
                    }
                    else
                    {
                        EditorUtility.DisplayDialog("Cannot Delete", "A spline must have at least 2 points.", "OK");
                    }
                }
                
                EditorGUILayout.EndHorizontal();
            }

            // ── Shortcuts ──
            GUIStyle shortcutStyle = new GUIStyle(EditorStyles.miniLabel) { fontSize = 9 };
            shortcutStyle.normal.textColor = new Color(0.5f, 0.5f, 0.5f);
            GUILayout.Label("Shift+Click: Insert | Ctrl+Drag: Snap | Del: Remove", shortcutStyle);
        }
    }
}
