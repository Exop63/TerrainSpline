using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace TerrainSplinePlugin.Editor.Core
{
    /// <summary>
    /// Utility class performing core spline merging and splitting operations.
    /// </summary>
    public static class SplineOperations
    {
        private static SplinePoint ReversePoint(SplinePoint pt)
        {
            SplinePoint rev = pt;
            Vector3 temp = rev.tangentIn;
            rev.tangentIn = -rev.tangentOut;
            rev.tangentOut = -temp;
            return rev;
        }

        /// <summary>
        /// Merges target spline into source spline, welding their closest endpoints if within weldDistance.
        /// Deletes the target spline asset upon completion.
        /// </summary>
        public static void MergeSplines(TerrainSplineData source, TerrainSplineData target, float weldDistance)
        {
            if (source == null || target == null) return;
            if (source.points.Count == 0 || target.points.Count == 0) return;

            Undo.RecordObject(source, "Merge Splines");

            List<SplinePoint> pointsA = new List<SplinePoint>(source.points);
            List<SplinePoint> pointsB = new List<SplinePoint>(target.points);

            float d_EndA_StartB = Vector3.Distance(pointsA[pointsA.Count - 1].position, pointsB[0].position);
            float d_StartA_EndB = Vector3.Distance(pointsA[0].position, pointsB[pointsB.Count - 1].position);
            float d_EndA_EndB = Vector3.Distance(pointsA[pointsA.Count - 1].position, pointsB[pointsB.Count - 1].position);
            float d_StartA_StartB = Vector3.Distance(pointsA[0].position, pointsB[0].position);

            float minDist = Mathf.Min(d_EndA_StartB, d_StartA_EndB, d_EndA_EndB, d_StartA_StartB);

            List<SplinePoint> mergedPoints = new List<SplinePoint>();

            if (minDist <= weldDistance)
            {
                if (Mathf.Approximately(minDist, d_EndA_StartB))
                {
                    // Weld End A and Start B
                    SplinePoint w = pointsA[pointsA.Count - 1];
                    w.tangentOut = pointsB[0].tangentOut;
                    w.handleMode = HandleMode.Free;

                    mergedPoints.AddRange(pointsA.GetRange(0, pointsA.Count - 1));
                    mergedPoints.Add(w);
                    mergedPoints.AddRange(pointsB.GetRange(1, pointsB.Count - 1));
                }
                else if (Mathf.Approximately(minDist, d_StartA_EndB))
                {
                    // Weld Start A and End B
                    SplinePoint w = pointsA[0];
                    w.tangentIn = pointsB[pointsB.Count - 1].tangentIn;
                    w.handleMode = HandleMode.Free;

                    mergedPoints.AddRange(pointsB.GetRange(0, pointsB.Count - 1));
                    mergedPoints.Add(w);
                    mergedPoints.AddRange(pointsA.GetRange(1, pointsA.Count - 1));
                }
                else if (Mathf.Approximately(minDist, d_EndA_EndB))
                {
                    // Weld End A and End B (Reverse B)
                    SplinePoint w = pointsA[pointsA.Count - 1];
                    w.tangentOut = -pointsB[pointsB.Count - 1].tangentIn;
                    w.handleMode = HandleMode.Free;

                    mergedPoints.AddRange(pointsA.GetRange(0, pointsA.Count - 1));
                    mergedPoints.Add(w);

                    // Add B in reverse order
                    for (int i = pointsB.Count - 2; i >= 0; i--)
                    {
                        mergedPoints.Add(ReversePoint(pointsB[i]));
                    }
                }
                else // d_StartA_StartB
                {
                    // Weld Start A and Start B (Reverse B, prepend to A)
                    SplinePoint w = pointsA[0];
                    w.tangentIn = -pointsB[0].tangentOut;
                    w.handleMode = HandleMode.Free;

                    // Add B in reverse order (excluding first node which is welded)
                    for (int i = pointsB.Count - 1; i >= 1; i--)
                    {
                        mergedPoints.Add(ReversePoint(pointsB[i]));
                    }
                    mergedPoints.Add(w);
                    mergedPoints.AddRange(pointsA.GetRange(1, pointsA.Count - 1));
                }
                Debug.Log($"[TerrainSpline] Welded endpoints (distance: {minDist:F3}m).");
            }
            else
            {
                // No weld: directly append B to A (connects End A to Start B)
                mergedPoints.AddRange(pointsA);
                mergedPoints.AddRange(pointsB);
                Debug.Log($"[TerrainSpline] Endpoints outside weld distance. Directly connected.");
            }

            source.points = mergedPoints;
            source.EnforceModeConstraints();
            EditorUtility.SetDirty(source);

            // Save names for logging
            string targetName = target.displayName;
            string sourceName = source.displayName;

            // Delete target spline
            string targetPath = AssetDatabase.GetAssetPath(target);
            if (!string.IsNullOrEmpty(targetPath))
            {
                AssetDatabase.DeleteAsset(targetPath);
                AssetDatabase.SaveAssets();
            }

            // Refresh open TerrainSplineWindow instances
            if (EditorWindow.HasOpenInstances<TerrainSplineWindow>())
            {
                TerrainSplineWindow window = EditorWindow.GetWindow<TerrainSplineWindow>();
                if (window != null)
                {
                    window.RefreshSplineAssets();
                    window.Repaint();
                }
            }

            // Restore preview and reapply new combined path
            TerrainPreviewManager.RestoreAndClear();
            TerrainPreviewManager.BeginPreviewUpdate(source);
            TerrainPreviewManager.ApplyPreview();

            Debug.Log($"[TerrainSpline] Merged spline '{targetName}' into '{sourceName}'.");
        }

        /// <summary>
        /// Splits a spline at a specified index, modifying the original and creating a new spline asset.
        /// </summary>
        public static TerrainSplineData SplitSpline(TerrainSplineData data, int splitIndex)
        {
            if (data == null || splitIndex <= 0 || splitIndex >= data.points.Count - 1)
            {
                Debug.LogWarning("[TerrainSpline] Cannot split at endpoint or invalid index.");
                return null;
            }

            string path = AssetDatabase.GetAssetPath(data);
            if (string.IsNullOrEmpty(path))
            {
                Debug.LogError("[TerrainSpline] Original spline is not a saved asset.");
                return null;
            }

            // Save to project Assets folder (not inside the package, which may be immutable)
            string dir = "Assets/TerrainSplineData";
            if (!AssetDatabase.IsValidFolder(dir))
            {
                AssetDatabase.CreateFolder("Assets", "TerrainSplineData");
            }

            string baseName = Path.GetFileNameWithoutExtension(path);

            // Calculate unique name for the new split spline
            int index = 1;
            string newAssetName = $"{baseName}_Split";
            string newPath = $"{dir}/{newAssetName}.asset";
            while (AssetDatabase.LoadAssetAtPath<TerrainSplineData>(newPath) != null)
            {
                index++;
                newAssetName = $"{baseName}_Split_{index:D2}";
                newPath = $"{dir}/{newAssetName}.asset";
            }

            Undo.RecordObject(data, "Split Spline");

            List<SplinePoint> pointsA = data.points.GetRange(0, splitIndex + 1);
            List<SplinePoint> pointsB = data.points.GetRange(splitIndex, data.points.Count - splitIndex);

            // Modify original spline (Part 1)
            data.points = pointsA;
            data.isClosedLoop = false; // Split forces open loop
            EditorUtility.SetDirty(data);

            // Create new spline (Part 2)
            TerrainSplineData newSpline = ScriptableObject.CreateInstance<TerrainSplineData>();
            newSpline.displayName = newAssetName;
            newSpline.splineMode = data.splineMode;
            newSpline.isClosedLoop = false;
            newSpline.targetTerrain = data.targetTerrain;
            newSpline.applyHeight = data.applyHeight;
            newSpline.applyPaint = data.applyPaint;
            newSpline.brushSize = data.brushSize;
            newSpline.brushHardness = data.brushHardness;
            newSpline.brushStrength = data.brushStrength;
            newSpline.sampleStep = data.sampleStep;
            newSpline.heightOffset = data.heightOffset;
            newSpline.shapeFillHeight = data.shapeFillHeight;
            newSpline.shapeUseSplineHeight = data.shapeUseSplineHeight;
            newSpline.paintLayerIndex = data.paintLayerIndex;
            newSpline.paintStrength = data.paintStrength;
            newSpline.paintBlend = data.paintBlend;
            newSpline.shapeEdgeFalloff = data.shapeEdgeFalloff;
            
            newSpline.points = pointsB;

            AssetDatabase.CreateAsset(newSpline, newPath);
            AssetDatabase.SaveAssets();

            // Refresh open TerrainSplineWindow instances
            if (EditorWindow.HasOpenInstances<TerrainSplineWindow>())
            {
                TerrainSplineWindow window = EditorWindow.GetWindow<TerrainSplineWindow>();
                if (window != null)
                {
                    window.RefreshSplineAssets();
                    window.Repaint();
                }
            }

            // Refresh preview
            TerrainPreviewManager.RestoreAndClear();
            TerrainPreviewManager.BeginPreviewUpdate(data);
            TerrainPreviewManager.ApplyPreview();

            Debug.Log($"[TerrainSpline] Split '{data.displayName}' at node {splitIndex}. Created new spline: '{newAssetName}'");
            return newSpline;
        }

        /// <summary>
        /// Smooths the spline by adjusting tangents to point towards adjacent nodes.
        /// </summary>
        public static void SmoothSpline(TerrainSplineData data)
        {
            if (data == null || data.points.Count < 2) return;

            Undo.RecordObject(data, "Smooth Spline");

            int count = data.points.Count;
            for (int i = 0; i < count; i++)
            {
                SplinePoint pt = data.points[i];
                Vector3 prevPos;
                Vector3 nextPos;

                if (i == 0)
                {
                    if (data.isClosedLoop && count > 2)
                        prevPos = data.points[count - 1].position;
                    else
                        prevPos = pt.position - (data.points[1].position - pt.position);
                }
                else
                {
                    prevPos = data.points[i - 1].position;
                }

                if (i == count - 1)
                {
                    if (data.isClosedLoop && count > 2)
                        nextPos = data.points[0].position;
                    else
                        nextPos = pt.position + (pt.position - data.points[i - 1].position);
                }
                else
                {
                    nextPos = data.points[i + 1].position;
                }

                Vector3 tangent = (nextPos - prevPos) * 0.25f;
                pt.tangentOut = tangent;
                pt.tangentIn = -tangent;
                
                pt.handleMode = HandleMode.Aligned;
                
                data.points[i] = pt;
            }

            EditorUtility.SetDirty(data);
            TerrainPreviewManager.RestoreAndClear();
            TerrainPreviewManager.BeginPreviewUpdate(data);
            TerrainPreviewManager.ApplyPreview();

            if (EditorWindow.HasOpenInstances<SceneView>())
            {
                SceneView.RepaintAll();
            }

            Debug.Log($"[TerrainSpline] Smoothed spline '{data.displayName}'.");
        }
    }
}
