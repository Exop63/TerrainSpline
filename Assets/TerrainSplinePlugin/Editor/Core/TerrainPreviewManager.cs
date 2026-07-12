using UnityEngine;
using UnityEditor;

namespace TerrainSplinePlugin.Editor.Core
{
    /// <summary>
    /// Handles real-time terrain preview by caching the original terrain state
    /// and allowing temporary modifications that can be restored.
    /// </summary>
    public static class TerrainPreviewManager
    {
        private static TerrainData _targetTerrainData;
        
        // Height backup
        private static float[,] _originalHeights;
        private static int _backupX;
        private static int _backupZ;
        private static int _backupWidth;
        private static int _backupHeight;

        // Alpha backup
        private static float[,,] _originalAlphamaps;
        private static int _alphaBackupX;
        private static int _alphaBackupZ;
        private static int _alphaBackupWidth;
        private static int _alphaBackupHeight;

        private static bool _isPreviewActive = false;
        private static TerrainSplineData _activeData = null;

        static TerrainPreviewManager()
        {
            // Subscribe to undo/redo to cancel preview if user undoes
            Undo.undoRedoPerformed += RestoreAndClear;
            // Clear on play mode change or script reload to prevent stuck terrain
            EditorApplication.playModeStateChanged += (state) => RestoreAndClear();
            AssemblyReloadEvents.beforeAssemblyReload += RestoreAndClear;
        }

        public static bool IsPreviewActive => _isPreviewActive;

        /// <summary>
        /// Begins a preview session or prepares for an update.
        /// If a preview is already active, it restores the original heights first.
        /// </summary>
        public static void BeginPreviewUpdate(TerrainSplineData data)
        {
            if (data == null || data.targetTerrain == null) return;
            
            TerrainData td = data.targetTerrain.terrainData;
            if (td == null) return;

            if (_isPreviewActive && _targetTerrainData == td)
            {
                // Restore old state before capturing the new bounds
                RestoreTerrain();
            }

            _activeData = data;
            _targetTerrainData = td;
            _isPreviewActive = true;

            // Calculate the bounds for the current spline configuration
            CalculateBounds(data, out _backupX, out _backupZ, out _backupWidth, out _backupHeight,
                                  out _alphaBackupX, out _alphaBackupZ, out _alphaBackupWidth, out _alphaBackupHeight);

            // Backup the original terrain data in that area
            if (data.applyHeight)
                _originalHeights = _targetTerrainData.GetHeights(_backupX, _backupZ, _backupWidth, _backupHeight);
            
            if (data.applyPaint)
                _originalAlphamaps = _targetTerrainData.GetAlphamaps(_alphaBackupX, _alphaBackupZ, _alphaBackupWidth, _alphaBackupHeight);
        }

        /// <summary>
        /// Applies the current spline configuration to the terrain temporarily.
        /// </summary>
        public static void ApplyPreview()
        {
            if (!_isPreviewActive || _activeData == null) return;
            TerrainModifier.ApplySpline(_activeData);
        }

        /// <summary>
        /// Restores the terrain to its original state but keeps the preview active (for continuous dragging).
        /// </summary>
        private static void RestoreTerrain()
        {
            if (!_isPreviewActive || _targetTerrainData == null) return;
            
            // Restore heightmap using SetHeightsDelayLOD for better performance during drag
            if (_originalHeights != null)
                _targetTerrainData.SetHeightsDelayLOD(_backupX, _backupZ, _originalHeights);
            
            if (_originalAlphamaps != null)
                _targetTerrainData.SetAlphamaps(_alphaBackupX, _alphaBackupZ, _originalAlphamaps);
        }

        /// <summary>
        /// Fully restores the terrain and cancels the preview session.
        /// </summary>
        public static void RestoreAndClear()
        {
            if (_isPreviewActive)
            {
                RestoreTerrain();
                if (_targetTerrainData != null)
                {
                    _targetTerrainData.SyncHeightmap(); // Finalize LODs
                }
            }
            
            _isPreviewActive = false;
            _activeData = null;
            _targetTerrainData = null;
            _originalHeights = null;
            _originalAlphamaps = null;
        }

        /// <summary>
        /// Called when the user clicks "Apply" permanently.
        /// Discards the backup and syncs the changes.
        /// </summary>
        public static void CommitPreview()
        {
            if (_isPreviewActive && _targetTerrainData != null)
            {
                _targetTerrainData.SyncHeightmap();
            }
            _isPreviewActive = false;
            _activeData = null;
            _targetTerrainData = null;
            _originalHeights = null;
            _originalAlphamaps = null;
        }

        private static void CalculateBounds(TerrainSplineData data, out int hX, out int hZ, out int hW, out int hH,
                                            out int aX, out int aZ, out int aW, out int aH)
        {
            TerrainData td = data.targetTerrain.terrainData;
            int hRes = td.heightmapResolution;
            int aRes = td.alphamapResolution;

            Vector2 minBounds = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 maxBounds = new Vector2(float.MinValue, float.MinValue);
            float maxScaledRadius = 0f;

            if (data.splineMode == SplineMode.Path)
            {
                var samples = SplineUtils.SampleSplineDetailed(data, Mathf.Min(data.sampleStep, 0.5f));
                foreach (var s in samples)
                {
                    minBounds.x = Mathf.Min(minBounds.x, s.position.x);
                    minBounds.y = Mathf.Min(minBounds.y, s.position.z);
                    maxBounds.x = Mathf.Max(maxBounds.x, s.position.x);
                    maxBounds.y = Mathf.Max(maxBounds.y, s.position.z);
                    if (s.scale.x > maxScaledRadius) maxScaledRadius = s.scale.x;
                }
            }
            else
            {
                var samples = SplineUtils.SampleSpline(data, data.sampleStep);
                foreach (var s in samples)
                {
                    minBounds.x = Mathf.Min(minBounds.x, s.x);
                    minBounds.y = Mathf.Min(minBounds.y, s.z);
                    maxBounds.x = Mathf.Max(maxBounds.x, s.x);
                    maxBounds.y = Mathf.Max(maxBounds.y, s.z);
                }
            }

            float maxRadius = data.brushSize * Mathf.Max(1f, maxScaledRadius);
            // Add padding
            minBounds -= Vector2.one * (maxRadius + 10f);
            maxBounds += Vector2.one * (maxRadius + 10f);

            Vector3 tPos = data.targetTerrain.transform.position;
            Vector3 tSize = td.size;

            // Height bounds
            int hMinX = Mathf.FloorToInt((minBounds.x - tPos.x) / tSize.x * (hRes - 1));
            int hMaxX = Mathf.CeilToInt((maxBounds.x - tPos.x) / tSize.x * (hRes - 1));
            int hMinZ = Mathf.FloorToInt((minBounds.y - tPos.z) / tSize.z * (hRes - 1));
            int hMaxZ = Mathf.CeilToInt((maxBounds.y - tPos.z) / tSize.z * (hRes - 1));

            hX = Mathf.Clamp(hMinX, 0, hRes - 1);
            hZ = Mathf.Clamp(hMinZ, 0, hRes - 1);
            
            int clampMaxHX = Mathf.Clamp(hMaxX, 0, hRes - 1);
            int clampMaxHZ = Mathf.Clamp(hMaxZ, 0, hRes - 1);

            hW = clampMaxHX - hX + 1;
            hH = clampMaxHZ - hZ + 1;

            // Alpha bounds
            int aMinX = Mathf.FloorToInt((minBounds.x - tPos.x) / tSize.x * (aRes - 1));
            int aMaxX = Mathf.CeilToInt((maxBounds.x - tPos.x) / tSize.x * (aRes - 1));
            int aMinZ = Mathf.FloorToInt((minBounds.y - tPos.z) / tSize.z * (aRes - 1));
            int aMaxZ = Mathf.CeilToInt((maxBounds.y - tPos.z) / tSize.z * (aRes - 1));

            aX = Mathf.Clamp(aMinX, 0, aRes - 1);
            aZ = Mathf.Clamp(aMinZ, 0, aRes - 1);
            
            int clampMaxAX = Mathf.Clamp(aMaxX, 0, aRes - 1);
            int clampMaxAZ = Mathf.Clamp(aMaxZ, 0, aRes - 1);

            aW = clampMaxAX - aX + 1;
            aH = clampMaxAZ - aZ + 1;
        }
    }
}
