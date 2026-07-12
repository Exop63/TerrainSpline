// TerrainSplinePlugin - Editor Only
// Unity 2022.3 LTS Compatible

using System.Collections.Generic;
using TerrainSplinePlugin.Editor.Core;
using UnityEditor;
using UnityEngine;

namespace TerrainSplinePlugin
{
    /// <summary>
    /// Custom inspector for TerrainSplineData ScriptableObject.
    /// Provides a convenient view when selecting spline assets in the Project window.
    /// </summary>
    [CustomEditor(typeof(TerrainSplineData))]
    public class TerrainSplineInspector : UnityEditor.Editor
    {
        private TerrainSplineData data;

        private void OnEnable()
        {
            data = (TerrainSplineData)target;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Header
            EditorGUILayout.LabelField("Terrain Spline Data", EditorStyles.boldLabel);
            EditorGUILayout.Space(4);

            // Basic Info
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.PropertyField(serializedObject.FindProperty("displayName"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("splineMode"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("isClosedLoop"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("targetTerrain"));
            EditorGUILayout.LabelField("Points", data.points.Count.ToString());
            EditorGUILayout.LabelField("Valid", data.IsValid ? "✓ Yes" : "✗ No (need more points)");
            EditorGUILayout.EndVertical();

            // Operations
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Operations", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.PropertyField(serializedObject.FindProperty("applyHeight"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("applyPaint"));
            EditorGUILayout.EndVertical();

            // Brush
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Brush Settings", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");
            EditorGUILayout.PropertyField(serializedObject.FindProperty("brushSize"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("brushHardness"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("brushStrength"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("sampleStep"));
            EditorGUILayout.EndVertical();

            // Height Settings
            if (data.applyHeight)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Height Settings", EditorStyles.boldLabel);
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.PropertyField(serializedObject.FindProperty("heightOffset"));

                if (data.splineMode == SplineMode.Shape)
                {
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("shapeUseSplineHeight"));
                    if (!data.shapeUseSplineHeight)
                        EditorGUILayout.PropertyField(serializedObject.FindProperty("shapeFillHeight"));
                    EditorGUILayout.PropertyField(serializedObject.FindProperty("shapeEdgeFalloff"));
                }
                EditorGUILayout.EndVertical();
            }

            // Paint Settings
            if (data.applyPaint)
            {
                EditorGUILayout.Space(4);
                EditorGUILayout.LabelField("Paint Settings", EditorStyles.boldLabel);
                EditorGUILayout.BeginVertical("box");
                EditorGUILayout.PropertyField(serializedObject.FindProperty("paintLayerIndex"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("paintStrength"));
                EditorGUILayout.PropertyField(serializedObject.FindProperty("paintBlend"));
                EditorGUILayout.EndVertical();
            }

            // Points list (collapsed by default)
            EditorGUILayout.Space(4);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("points"), true);

            DrawMergeSection();

            serializedObject.ApplyModifiedProperties();

            // Quick Actions
            EditorGUILayout.Space(8);
            EditorGUILayout.BeginHorizontal();

            GUI.backgroundColor = new Color(0.4f, 0.8f, 0.4f);
            if (GUILayout.Button("Apply to Terrain", GUILayout.Height(28)))
            {
                if (data.targetTerrain != null && data.IsValid)
                {
                    TerrainModifier.ApplySpline(data);
                    Debug.Log($"[TerrainSpline] Applied '{data.displayName}'");
                }
                else
                {
                    Debug.LogWarning("[TerrainSpline] Cannot apply: check terrain reference and point count.");
                }
            }
            GUI.backgroundColor = Color.white;

            if (GUILayout.Button("Edit in Scene", GUILayout.Height(28)))
            {
                // Open the window and select this spline
                TerrainSplineWindow.ShowWindow();
                TerrainSplineTool.ActiveSpline = data;

                if (SceneView.lastActiveSceneView != null)
                    SceneView.lastActiveSceneView.Focus();
            }

            EditorGUILayout.EndHorizontal();

            // Spline length info
            if (data.points.Count >= 2)
            {
                EditorGUILayout.Space(4);
                float length = SplineUtils.CalculateSplineLength(data);
                EditorGUILayout.HelpBox($"Spline Length: {length:F1} meters | Segments: {data.SegmentCount}", MessageType.None);
            }
        }

        private void DrawMergeSection()
        {
            if (data == null) return;

            List<TerrainSplineData> allSplines = new List<TerrainSplineData>();
            string[] guids = AssetDatabase.FindAssets("t:TerrainSplineData");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                TerrainSplineData s = AssetDatabase.LoadAssetAtPath<TerrainSplineData>(path);
                if (s != null && s != data)
                    allSplines.Add(s);
            }
            allSplines.Sort((a, b) => string.Compare(a.displayName, b.displayName, System.StringComparison.Ordinal));

            if (allSplines.Count == 0) return;

            EditorGUILayout.Space(6);
            EditorGUILayout.LabelField("🔗 Merge Paths", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("box");

            // Weld Distance
            float weldDistance = EditorPrefs.GetFloat("TerrainSpline_MergeWeldDistance", 1.0f);
            EditorGUI.BeginChangeCheck();
            weldDistance = EditorGUILayout.Slider("Weld Distance (m)", weldDistance, 0.05f, 20f);
            if (EditorGUI.EndChangeCheck())
            {
                EditorPrefs.SetFloat("TerrainSpline_MergeWeldDistance", weldDistance);
            }

            // Find "path below it" as default target
            List<TerrainSplineData> sortedWithSelf = new List<TerrainSplineData>(allSplines);
            sortedWithSelf.Add(data);
            sortedWithSelf.Sort((a, b) => string.Compare(a.displayName, b.displayName, System.StringComparison.Ordinal));
            int selfIdx = sortedWithSelf.IndexOf(data);
            
            TerrainSplineData defaultTarget = null;
            if (selfIdx >= 0 && selfIdx + 1 < sortedWithSelf.Count)
            {
                defaultTarget = sortedWithSelf[selfIdx + 1];
            }
            else if (allSplines.Count > 0)
            {
                defaultTarget = allSplines[0];
            }

            int defaultTargetIdx = defaultTarget != null ? allSplines.IndexOf(defaultTarget) : 0;
            if (defaultTargetIdx < 0) defaultTargetIdx = 0;

            string[] names = new string[allSplines.Count];
            for (int i = 0; i < allSplines.Count; i++)
            {
                names[i] = allSplines[i].displayName;
            }

            int mergeTargetIndex = EditorPrefs.GetInt("TerrainSpline_MergeTargetIndex", defaultTargetIdx);
            if (mergeTargetIndex >= allSplines.Count || mergeTargetIndex < 0)
            {
                mergeTargetIndex = defaultTargetIdx;
            }

            mergeTargetIndex = EditorGUILayout.Popup("Target Spline", mergeTargetIndex, names);
            EditorPrefs.SetInt("TerrainSpline_MergeTargetIndex", mergeTargetIndex);

            TerrainSplineData targetSpline = allSplines[mergeTargetIndex];

            // Render Merge button
            GUI.backgroundColor = new Color(0.3f, 0.7f, 1f);
            if (GUILayout.Button($"Merge with '{targetSpline.displayName}'", GUILayout.Height(24)))
            {
                if (EditorUtility.DisplayDialog("Merge Splines", 
                    $"Are you sure you want to merge '{data.displayName}' and '{targetSpline.displayName}'?\n" +
                    $"This will weld endpoints (if within {weldDistance}m) and DELETE the target spline '{targetSpline.displayName}'.", "Merge", "Cancel"))
                {
                    SplineOperations.MergeSplines(data, targetSpline, weldDistance);
                }
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndVertical();
        }
    }
}
