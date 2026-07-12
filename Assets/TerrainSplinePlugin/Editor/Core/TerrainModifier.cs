// TerrainSplinePlugin - Editor Only
// Unity 2022.3 LTS Compatible

using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace TerrainSplinePlugin
{
    /// <summary>
    /// Applies spline operations to terrain data.
    /// Supports both Path mode (along the curve) and Shape mode (fill interior).
    /// Operations: Height modification and Texture painting.
    /// </summary>
    public static class TerrainModifier
    {
        // ─────────────────────────────────────────────
        // Main Entry Point
        // ─────────────────────────────────────────────

        /// <summary>
        /// Apply all enabled operations for the given spline data.
        /// Automatically chooses Path or Shape mode based on spline settings.
        /// </summary>
        public static void ApplySpline(TerrainSplineData data)
        {
            if (data == null || data.targetTerrain == null || !data.IsValid)
            {
                Debug.LogWarning("[TerrainSpline] Cannot apply: invalid spline data or no terrain assigned.");
                return;
            }

            TerrainData terrainData = data.targetTerrain.terrainData;
            
            System.Collections.Generic.List<UnityEngine.Object> undoObjects = new System.Collections.Generic.List<UnityEngine.Object> { terrainData };
            if (terrainData.alphamapTextures != null)
                undoObjects.AddRange(terrainData.alphamapTextures);
                
            Undo.RegisterCompleteObjectUndo(undoObjects.ToArray(), "Terrain Spline Apply");

            if (data.applyHeight)
            {
                if (data.splineMode == SplineMode.Path)
                    ApplyPathHeight(data);
                else
                    ApplyShapeHeight(data);
            }

            if (data.applyPaint)
            {
                if (data.splineMode == SplineMode.Path)
                    ApplyPathPaint(data);
                else
                    ApplyShapePaint(data);
            }

            // Force terrain update
            data.targetTerrain.Flush();
        }

        // ─────────────────────────────────────────────
        // Path Mode — Height
        // ─────────────────────────────────────────────

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct GPUSplineSample
        {
            public Vector3 position;
            public Vector4 rotation;
            public Vector3 scale;
        }

        private static ComputeShader _computeShader;
        private static ComputeShader GetComputeShader()
        {
            if (_computeShader == null)
            {
                _computeShader = AssetDatabase.LoadAssetAtPath<ComputeShader>("Assets/TerrainSplinePlugin/Editor/Shaders/TerrainSplineCompute.compute");
            }
            return _computeShader;
        }

        static TerrainModifier()
        {
            AssemblyReloadEvents.beforeAssemblyReload += ReleaseStaticBuffers;
        }

        private static void ReleaseStaticBuffers()
        {
            if (_cachedHeightsBuffer != null)
            {
                _cachedHeightsBuffer.Release();
                _cachedHeightsBuffer = null;
                _cachedHeightsCapacity = 0;
            }
            if (_cachedSamplesBuffer != null)
            {
                _cachedSamplesBuffer.Release();
                _cachedSamplesBuffer = null;
                _cachedSamplesCapacity = 0;
            }
            if (_cachedPolyBuffer != null)
            {
                _cachedPolyBuffer.Release();
                _cachedPolyBuffer = null;
                _cachedPolyCapacity = 0;
            }
            if (_cachedSplineBuffer != null)
            {
                _cachedSplineBuffer.Release();
                _cachedSplineBuffer = null;
                _cachedSplineCapacity = 0;
            }
        }

        private static ComputeBuffer _cachedHeightsBuffer;
        private static int _cachedHeightsCapacity = 0;
        private static ComputeBuffer GetHeightsBuffer(int requiredCapacity)
        {
            if (_cachedHeightsBuffer == null || _cachedHeightsCapacity < requiredCapacity)
            {
                if (_cachedHeightsBuffer != null) _cachedHeightsBuffer.Release();
                int newCapacity = Mathf.Max(256, Mathf.NextPowerOfTwo(requiredCapacity));
                _cachedHeightsBuffer = new ComputeBuffer(newCapacity, sizeof(float));
                _cachedHeightsCapacity = newCapacity;
            }
            return _cachedHeightsBuffer;
        }

        private static ComputeBuffer _cachedSamplesBuffer;
        private static int _cachedSamplesCapacity = 0;
        private static ComputeBuffer GetSamplesBuffer(int requiredCapacity)
        {
            if (_cachedSamplesBuffer == null || _cachedSamplesCapacity < requiredCapacity)
            {
                if (_cachedSamplesBuffer != null) _cachedSamplesBuffer.Release();
                int newCapacity = Mathf.Max(64, Mathf.NextPowerOfTwo(requiredCapacity));
                _cachedSamplesBuffer = new ComputeBuffer(newCapacity, 40); // GPUSplineSample is 40 bytes
                _cachedSamplesCapacity = newCapacity;
            }
            return _cachedSamplesBuffer;
        }

        private static ComputeBuffer _cachedPolyBuffer;
        private static int _cachedPolyCapacity = 0;
        private static ComputeBuffer GetPolyBuffer(int requiredCapacity)
        {
            if (_cachedPolyBuffer == null || _cachedPolyCapacity < requiredCapacity)
            {
                if (_cachedPolyBuffer != null) _cachedPolyBuffer.Release();
                int newCapacity = Mathf.Max(16, Mathf.NextPowerOfTwo(requiredCapacity));
                _cachedPolyBuffer = new ComputeBuffer(newCapacity, sizeof(float) * 2);
                _cachedPolyCapacity = newCapacity;
            }
            return _cachedPolyBuffer;
        }

        private static ComputeBuffer _cachedSplineBuffer;
        private static int _cachedSplineCapacity = 0;
        private static ComputeBuffer GetSplineBuffer(int requiredCapacity)
        {
            if (_cachedSplineBuffer == null || _cachedSplineCapacity < requiredCapacity)
            {
                if (_cachedSplineBuffer != null) _cachedSplineBuffer.Release();
                int newCapacity = Mathf.Max(64, Mathf.NextPowerOfTwo(requiredCapacity));
                _cachedSplineBuffer = new ComputeBuffer(newCapacity, sizeof(float) * 3);
                _cachedSplineCapacity = newCapacity;
            }
            return _cachedSplineBuffer;
        }

        /// <summary>
        /// Modify terrain height along the spline path with brush settings.
        /// </summary>
        private static void ApplyPathHeight(TerrainSplineData data)
        {
            Terrain terrain = data.targetTerrain;
            TerrainData td = terrain.terrainData;

            int heightmapRes = td.heightmapResolution;
            Vector3 terrainPos = terrain.transform.position;
            Vector3 terrainSize = td.size;

            float denseStep = Mathf.Min(data.sampleStep, 0.5f);
            List<SplineSample> samples = SplineUtils.SampleSplineDetailed(data, denseStep);
            
            if (samples.Count == 0) return;

            float baseBrushRadius = data.brushSize;
            
            Vector2 minBounds = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 maxBounds = new Vector2(float.MinValue, float.MinValue);
            float maxScaledRadius = 0f;
            foreach (var s in samples)
            {
                minBounds.x = Mathf.Min(minBounds.x, s.position.x);
                minBounds.y = Mathf.Min(minBounds.y, s.position.z);
                maxBounds.x = Mathf.Max(maxBounds.x, s.position.x);
                maxBounds.y = Mathf.Max(maxBounds.y, s.position.z);
                if (s.scale.x > maxScaledRadius) maxScaledRadius = s.scale.x;
            }
            
            float maxRadius = baseBrushRadius * Mathf.Max(1f, maxScaledRadius);
            minBounds -= Vector2.one * maxRadius;
            maxBounds += Vector2.one * maxRadius;

            int minX = Mathf.Max(0, Mathf.FloorToInt((minBounds.x - terrainPos.x) / terrainSize.x * (heightmapRes - 1)));
            int maxX = Mathf.Min(heightmapRes - 1, Mathf.CeilToInt((maxBounds.x - terrainPos.x) / terrainSize.x * (heightmapRes - 1)));
            int minZ = Mathf.Max(0, Mathf.FloorToInt((minBounds.y - terrainPos.z) / terrainSize.z * (heightmapRes - 1)));
            int maxZ = Mathf.Min(heightmapRes - 1, Mathf.CeilToInt((maxBounds.y - terrainPos.z) / terrainSize.z * (heightmapRes - 1)));

            int width = maxX - minX + 1;
            int height = maxZ - minZ + 1;

            if (width <= 0 || height <= 0) return;

            float[,] heights = td.GetHeights(minX, minZ, width, height);
            float maxPossibleRadius = baseBrushRadius * Mathf.Max(1f, maxScaledRadius);

            ComputeShader cs = GetComputeShader();
            if (cs != null && SystemInfo.supportsComputeShaders)
            {
                // GPU path
                float[] heights1D = new float[width * height];
                for (int z = 0; z < height; z++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        heights1D[z * width + x] = heights[z, x];
                    }
                }

                GPUSplineSample[] gpuSamples = new GPUSplineSample[samples.Count];
                for (int i = 0; i < samples.Count; i++)
                {
                    gpuSamples[i] = new GPUSplineSample
                    {
                        position = samples[i].position,
                        rotation = new Vector4(samples[i].rotation.x, samples[i].rotation.y, samples[i].rotation.z, samples[i].rotation.w),
                        scale = samples[i].scale
                    };
                }

                ComputeBuffer heightsBuffer = GetHeightsBuffer(width * height);
                ComputeBuffer samplesBuffer = GetSamplesBuffer(samples.Count);
                try
                {
                    heightsBuffer.SetData(heights1D);
                    samplesBuffer.SetData(gpuSamples);

                    int kernel = cs.FindKernel("CSApplyPathHeight");
                    cs.SetBuffer(kernel, "Heights", heightsBuffer);
                    cs.SetBuffer(kernel, "Samples", samplesBuffer);

                    cs.SetInt("Width", width);
                    cs.SetInt("Height", height);
                    cs.SetInt("MinX", minX);
                    cs.SetInt("MinZ", minZ);
                    cs.SetInt("HeightmapRes", heightmapRes);
                    cs.SetVector("TerrainPos", terrainPos);
                    cs.SetVector("TerrainSize", terrainSize);
                    cs.SetFloat("BaseBrushRadius", baseBrushRadius);
                    cs.SetFloat("BrushHardness", data.brushHardness);
                    cs.SetFloat("BrushStrength", data.brushStrength);
                    cs.SetFloat("HeightOffset", data.heightOffset);
                    cs.SetInt("SampleCount", samples.Count);

                    int threadGroupsX = Mathf.CeilToInt(width / 8f);
                    int threadGroupsY = Mathf.CeilToInt(height / 8f);
                    cs.Dispatch(kernel, threadGroupsX, threadGroupsY, 1);

                    heightsBuffer.GetData(heights1D);

                    for (int z = 0; z < height; z++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            heights[z, x] = heights1D[z * width + x];
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError("[TerrainSpline] Compute Shader failed: " + ex.Message);
                }
            }
            else
            {
                // CPU Fallback (Optimized subgrid)
                Parallel.For(minZ, maxZ + 1, z =>
                {
                    int localZ = z - minZ;
                    for (int x = minX; x <= maxX; x++)
                    {
                        int localX = x - minX;
                        float worldX = (float)x / (heightmapRes - 1) * terrainSize.x + terrainPos.x;
                        float worldZ = (float)z / (heightmapRes - 1) * terrainSize.z + terrainPos.z;
                        Vector2 worldXZ = new Vector2(worldX, worldZ);

                        // Quick bounds check
                        SplineUtils.GetClosestSampleInfo(samples, worldXZ, out float minDistance, out SplineSample closestSample);
                        if (minDistance > maxPossibleRadius) continue;

                        float totalWeight = 0f;
                        float blendedTargetHeight = 0f;
                        float maxStrength = 0f;

                        for (int i = 0; i < samples.Count; i++)
                        {
                            var s = samples[i];
                            float dx = worldX - s.position.x;
                            float dz = worldZ - s.position.z;
                            float distSq = dx * dx + dz * dz;

                            float scaledRadius = baseBrushRadius * s.scale.x;
                            float scaledRadiusSq = scaledRadius * scaledRadius;

                            if (distSq > scaledRadiusSq || scaledRadiusSq <= 0.0001f) continue;

                            float dist = Mathf.Sqrt(distSq);
                            float falloff = SplineUtils.CalculateFalloff(dist, scaledRadius, data.brushHardness);
                            float strength = falloff * data.brushStrength;

                            if (strength > maxStrength) maxStrength = strength;

                            Vector3 up = s.rotation * Vector3.up;
                            float planeY = s.position.y;
                            if (Mathf.Abs(up.y) > 0.001f)
                            {
                                planeY = s.position.y - (up.x * dx + up.z * dz) / up.y;
                            }

                            float targetHeight = (planeY + data.heightOffset - terrainPos.y) / terrainSize.y;

                            // Smooth blending weight (1 at center, 0 at edge)
                            float w = 1f - (dist / scaledRadius);
                            w = w * w * w; // Cubic for smooth overlap blending

                            blendedTargetHeight += targetHeight * w;
                            totalWeight += w;
                        }

                        if (totalWeight > 0f)
                        {
                            blendedTargetHeight /= totalWeight;
                            heights[localZ, localX] = Mathf.Lerp(heights[localZ, localX], blendedTargetHeight, maxStrength);
                        }
                    }
                });
            }

            td.SetHeights(minX, minZ, heights);
        }

        // ─────────────────────────────────────────────
        // Path Mode — Paint
        // ─────────────────────────────────────────────

        /// <summary>
        /// Paint terrain textures along the spline path.
        /// </summary>
        private static void ApplyPathPaint(TerrainSplineData data)
        {
            Terrain terrain = data.targetTerrain;
            TerrainData td = terrain.terrainData;

            int alphamapRes = td.alphamapResolution;
            int layerCount = td.alphamapLayers;

            if (data.paintLayerIndex < 0 || data.paintLayerIndex >= layerCount)
            {
                Debug.LogWarning($"[TerrainSpline] Paint layer index {data.paintLayerIndex} is out of range (0-{layerCount - 1}).");
                return;
            }

            float[,,] alphamaps = td.GetAlphamaps(0, 0, alphamapRes, alphamapRes);

            Vector3 terrainPos = terrain.transform.position;
            Vector3 terrainSize = td.size;

            float denseStep = Mathf.Min(data.sampleStep, 0.5f);
            List<SplineSample> samples = SplineUtils.SampleSplineDetailed(data, denseStep);
            
            if (samples.Count == 0) return;

            float baseBrushRadius = data.brushSize;

            Vector2 minBounds = new Vector2(float.MaxValue, float.MaxValue);
            Vector2 maxBounds = new Vector2(float.MinValue, float.MinValue);
            float maxScaledRadius = 0f;
            foreach (var s in samples)
            {
                minBounds.x = Mathf.Min(minBounds.x, s.position.x);
                minBounds.y = Mathf.Min(minBounds.y, s.position.z);
                maxBounds.x = Mathf.Max(maxBounds.x, s.position.x);
                maxBounds.y = Mathf.Max(maxBounds.y, s.position.z);
                if (s.scale.x > maxScaledRadius) maxScaledRadius = s.scale.x;
            }

            float maxRadius = baseBrushRadius * Mathf.Max(1f, maxScaledRadius);
            minBounds -= Vector2.one * maxRadius;
            maxBounds += Vector2.one * maxRadius;

            int minX = Mathf.Max(0, Mathf.FloorToInt((minBounds.x - terrainPos.x) / terrainSize.x * (alphamapRes - 1)));
            int maxX = Mathf.Min(alphamapRes - 1, Mathf.CeilToInt((maxBounds.x - terrainPos.x) / terrainSize.x * (alphamapRes - 1)));
            int minZ = Mathf.Max(0, Mathf.FloorToInt((minBounds.y - terrainPos.z) / terrainSize.z * (alphamapRes - 1)));
            int maxZ = Mathf.Min(alphamapRes - 1, Mathf.CeilToInt((maxBounds.y - terrainPos.z) / terrainSize.z * (alphamapRes - 1)));

            Parallel.For(minZ, maxZ + 1, z =>
            {
                for (int x = minX; x <= maxX; x++)
                {
                    float worldX = (float)x / (alphamapRes - 1) * terrainSize.x + terrainPos.x;
                    float worldZ = (float)z / (alphamapRes - 1) * terrainSize.z + terrainPos.z;
                    Vector2 worldXZ = new Vector2(worldX, worldZ);

                    SplineUtils.GetClosestSampleInfo(samples, worldXZ, out float distance, out SplineSample closestSample);

                    float scaledRadius = baseBrushRadius * closestSample.scale.x;
                    if (distance > scaledRadius || scaledRadius <= 0.001f) continue;

                    float falloff = SplineUtils.CalculateFalloff(distance, scaledRadius, data.brushHardness);
                    float strength = falloff * data.paintStrength * data.brushStrength;

                    ApplyPaintAtPixel(alphamaps, x, z, data.paintLayerIndex, layerCount, strength, data.paintBlend);
                }
            });

            td.SetAlphamaps(0, 0, alphamaps);
        }

        // ─────────────────────────────────────────────
        // Shape Mode — Height
        // ─────────────────────────────────────────────

        /// <summary>
        /// Modify terrain height inside the closed spline shape.
        /// Uses point-in-polygon testing to fill the interior.
        /// </summary>
        private static void ApplyShapeHeight(TerrainSplineData data)
        {
            Terrain terrain = data.targetTerrain;
            TerrainData td = terrain.terrainData;

            int heightmapRes = td.heightmapResolution;
            Vector3 terrainPos = terrain.transform.position;
            Vector3 terrainSize = td.size;

            // Generate polygon from closed spline
            Vector2[] polygon = SplineUtils.GeneratePolygonFromSpline(data, data.sampleStep);
            if (polygon.Length < 3)
            {
                Debug.LogWarning("[TerrainSpline] Shape mode needs at least 3 points.");
                return;
            }

            // Get bounding box for optimization
            PolygonUtils.GetPolygonBounds(polygon, out Vector2 boundsMin, out Vector2 boundsMax);

            // Expand bounds by edge falloff
            boundsMin -= Vector2.one * data.shapeEdgeFalloff;
            boundsMax += Vector2.one * data.shapeEdgeFalloff;

            // Convert bounds to heightmap coordinates
            int hmMinX = Mathf.Max(0, Mathf.FloorToInt((boundsMin.x - terrainPos.x) / terrainSize.x * (heightmapRes - 1)));
            int hmMaxX = Mathf.Min(heightmapRes - 1, Mathf.CeilToInt((boundsMax.x - terrainPos.x) / terrainSize.x * (heightmapRes - 1)));
            int hmMinZ = Mathf.Max(0, Mathf.FloorToInt((boundsMin.y - terrainPos.z) / terrainSize.z * (heightmapRes - 1)));
            int hmMaxZ = Mathf.Min(heightmapRes - 1, Mathf.CeilToInt((boundsMax.y - terrainPos.z) / terrainSize.z * (heightmapRes - 1)));

            int width = hmMaxX - hmMinX + 1;
            int height = hmMaxZ - hmMinZ + 1;

            if (width <= 0 || height <= 0) return;

            float[,] heights = td.GetHeights(hmMinX, hmMinZ, width, height);

            // Sample spline heights for interpolation
            List<Vector3> splineSamples = SplineUtils.SampleSpline(data, data.sampleStep);

            ComputeShader cs = GetComputeShader();
            if (cs != null && SystemInfo.supportsComputeShaders)
            {
                // GPU path
                float[] heights1D = new float[width * height];
                for (int z = 0; z < height; z++)
                {
                    for (int x = 0; x < width; x++)
                    {
                        heights1D[z * width + x] = heights[z, x];
                    }
                }

                ComputeBuffer heightsBuffer = GetHeightsBuffer(width * height);
                ComputeBuffer polyBuffer = GetPolyBuffer(polygon.Length);
                ComputeBuffer splineBuffer = GetSplineBuffer(Mathf.Max(1, splineSamples.Count));
                try
                {
                    heightsBuffer.SetData(heights1D);
                    polyBuffer.SetData(polygon);

                    if (splineSamples.Count > 0)
                    {
                        splineBuffer.SetData(splineSamples.ToArray());
                    }

                    int kernel = cs.FindKernel("CSApplyShapeHeight");
                    cs.SetBuffer(kernel, "Heights", heightsBuffer);
                    cs.SetBuffer(kernel, "PolygonPoints", polyBuffer);
                    cs.SetBuffer(kernel, "SplineSamples", splineBuffer);

                    cs.SetInt("Width", width);
                    cs.SetInt("Height", height);
                    cs.SetInt("MinX", hmMinX);
                    cs.SetInt("MinZ", hmMinZ);
                    cs.SetInt("HeightmapRes", heightmapRes);
                    cs.SetVector("TerrainPos", terrainPos);
                    cs.SetVector("TerrainSize", terrainSize);
                    cs.SetFloat("BrushStrength", data.brushStrength);
                    cs.SetFloat("HeightOffset", data.heightOffset);
                    cs.SetFloat("ShapeEdgeFalloff", data.shapeEdgeFalloff);
                    cs.SetFloat("ShapeFillHeight", data.shapeFillHeight);
                    cs.SetInt("ShapeUseSplineHeight", data.shapeUseSplineHeight ? 1 : 0);
                    cs.SetInt("PolygonPointCount", polygon.Length);
                    cs.SetInt("SplineSampleCount", splineSamples.Count);

                    int threadGroupsX = Mathf.CeilToInt(width / 8f);
                    int threadGroupsY = Mathf.CeilToInt(height / 8f);
                    cs.Dispatch(kernel, threadGroupsX, threadGroupsY, 1);

                    heightsBuffer.GetData(heights1D);

                    for (int z = 0; z < height; z++)
                    {
                        for (int x = 0; x < width; x++)
                        {
                            heights[z, x] = heights1D[z * width + x];
                        }
                    }
                }
                catch (System.Exception ex)
                {
                    Debug.LogError("[TerrainSpline] Compute Shader failed: " + ex.Message);
                }
            }
            else
            {
                // CPU Fallback (Optimized subgrid)
                Parallel.For(hmMinZ, hmMaxZ + 1, z =>
                {
                    int localZ = z - hmMinZ;
                    for (int x = hmMinX; x <= hmMaxX; x++)
                    {
                        int localX = x - hmMinX;
                        float worldX = (float)x / (heightmapRes - 1) * terrainSize.x + terrainPos.x;
                        float worldZ = (float)z / (heightmapRes - 1) * terrainSize.z + terrainPos.z;
                        Vector2 point = new Vector2(worldX, worldZ);

                        bool isInside = PolygonUtils.IsPointInPolygon(point, polygon);
                        float edgeDist = PolygonUtils.DistanceToPolygonEdge(point, polygon);

                        float strength = PolygonUtils.CalculateContinuousEdgeFalloff(isInside, edgeDist, data.shapeEdgeFalloff);

                        if (strength <= 0.001f) continue;

                        strength *= data.brushStrength;

                        // Determine target height
                        float targetHeight;
                        if (data.shapeUseSplineHeight)
                        {
                            // Use IDW interpolated height
                            float closestHeight = GetSmoothIDWHeight(splineSamples, worldX, worldZ);
                            targetHeight = (closestHeight + data.heightOffset - terrainPos.y) / terrainSize.y;
                        }
                        else
                        {
                            targetHeight = (data.shapeFillHeight + data.heightOffset - terrainPos.y) / terrainSize.y;
                        }

                        heights[localZ, localX] = Mathf.Lerp(heights[localZ, localX], targetHeight, strength);
                    }
                });
            }

            td.SetHeights(hmMinX, hmMinZ, heights);
        }

        // ─────────────────────────────────────────────
        // Shape Mode — Paint
        // ─────────────────────────────────────────────

        /// <summary>
        /// Paint terrain textures inside the closed spline shape.
        /// </summary>
        private static void ApplyShapePaint(TerrainSplineData data)
        {
            Terrain terrain = data.targetTerrain;
            TerrainData td = terrain.terrainData;

            int alphamapRes = td.alphamapResolution;
            int layerCount = td.alphamapLayers;

            if (data.paintLayerIndex < 0 || data.paintLayerIndex >= layerCount)
            {
                Debug.LogWarning($"[TerrainSpline] Paint layer index {data.paintLayerIndex} is out of range.");
                return;
            }

            float[,,] alphamaps = td.GetAlphamaps(0, 0, alphamapRes, alphamapRes);

            Vector3 terrainPos = terrain.transform.position;
            Vector3 terrainSize = td.size;

            Vector2[] polygon = SplineUtils.GeneratePolygonFromSpline(data, data.sampleStep);
            if (polygon.Length < 3) return;

            PolygonUtils.GetPolygonBounds(polygon, out Vector2 boundsMin, out Vector2 boundsMax);
            boundsMin -= Vector2.one * data.shapeEdgeFalloff;
            boundsMax += Vector2.one * data.shapeEdgeFalloff;

            int amMinX = Mathf.Max(0, Mathf.FloorToInt((boundsMin.x - terrainPos.x) / terrainSize.x * (alphamapRes - 1)));
            int amMaxX = Mathf.Min(alphamapRes - 1, Mathf.CeilToInt((boundsMax.x - terrainPos.x) / terrainSize.x * (alphamapRes - 1)));
            int amMinZ = Mathf.Max(0, Mathf.FloorToInt((boundsMin.y - terrainPos.z) / terrainSize.z * (alphamapRes - 1)));
            int amMaxZ = Mathf.Min(alphamapRes - 1, Mathf.CeilToInt((boundsMax.y - terrainPos.z) / terrainSize.z * (alphamapRes - 1)));

            Parallel.For(amMinZ, amMaxZ + 1, z =>
            {
                for (int x = amMinX; x <= amMaxX; x++)
                {
                    float worldX = (float)x / (alphamapRes - 1) * terrainSize.x + terrainPos.x;
                    float worldZ = (float)z / (alphamapRes - 1) * terrainSize.z + terrainPos.z;
                    Vector2 point = new Vector2(worldX, worldZ);

                    bool isInside = PolygonUtils.IsPointInPolygon(point, polygon);
                    float edgeDist = PolygonUtils.DistanceToPolygonEdge(point, polygon);

                    float strength = PolygonUtils.CalculateContinuousEdgeFalloff(isInside, edgeDist, data.shapeEdgeFalloff);

                    if (strength <= 0.001f) continue;

                    strength *= data.paintStrength * data.brushStrength;

                    ApplyPaintAtPixel(alphamaps, x, z, data.paintLayerIndex, layerCount, strength, data.paintBlend);
                }
            });

            td.SetAlphamaps(0, 0, alphamaps);
        }

        // ─────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────

        /// <summary>
        /// Apply paint to a single alphamap pixel.
        /// Increases the target layer weight and normalizes all layers.
        /// </summary>
        private static void ApplyPaintAtPixel(float[,,] alphamaps, int x, int z,
            int targetLayer, int layerCount, float strength, float blend)
        {
            // Get current weight of target layer
            float currentWeight = alphamaps[z, x, targetLayer];
            float newWeight = Mathf.Lerp(currentWeight, 1f, strength * blend);

            // Set the target layer weight
            alphamaps[z, x, targetLayer] = newWeight;

            // Normalize: reduce other layers proportionally
            float remaining = 1f - newWeight;
            float otherTotal = 0f;

            for (int l = 0; l < layerCount; l++)
            {
                if (l != targetLayer)
                    otherTotal += alphamaps[z, x, l];
            }

            if (otherTotal > 0.0001f)
            {
                float scale = remaining / otherTotal;
                for (int l = 0; l < layerCount; l++)
                {
                    if (l != targetLayer)
                        alphamaps[z, x, l] *= scale;
                }
            }
            else
            {
                // If all other layers are zero, distribute evenly
                float perLayer = remaining / Mathf.Max(1, layerCount - 1);
                for (int l = 0; l < layerCount; l++)
                {
                    if (l != targetLayer)
                        alphamaps[z, x, l] = perLayer;
                }
            }
        }

        /// <summary>
        /// Smoothly interpolates the height for an interior point using Inverse Distance Weighting (IDW) 
        /// over all boundary samples. Eliminates the Voronoi "stair step" effect on medial axes.
        /// </summary>
        private static float GetSmoothIDWHeight(List<Vector3> samples, float worldX, float worldZ)
        {
            float sumWeight = 0f;
            float sumHeight = 0f;
            int count = samples.Count;

            for (int i = 0; i < count; i++)
            {
                float dx = samples[i].x - worldX;
                float dz = samples[i].z - worldZ;
                float distSq = dx * dx + dz * dz;

                if (distSq < 0.0001f)
                {
                    return samples[i].y;
                }

                // Power of 4 IDW (w = 1 / d^4)
                float weight = 1f / (distSq * distSq);
                sumWeight += weight;
                sumHeight += samples[i].y * weight;
            }

            return sumWeight > 0f ? sumHeight / sumWeight : 0f;
        }

        // ─────────────────────────────────────────────
        // Leveling Utilities
        // ─────────────────────────────────────────────

        /// <summary>
        /// Offset the entire terrain height by a given value.
        /// </summary>
        public static void OffsetTerrain(Terrain terrain, float offsetMeters)
        {
            if (terrain == null) return;

            TerrainData td = terrain.terrainData;
            Undo.RegisterCompleteObjectUndo(td, "Terrain Offset");

            int res = td.heightmapResolution;
            float[,] heights = td.GetHeights(0, 0, res, res);
            float normalizedOffset = offsetMeters / td.size.y;

            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    heights[z, x] = Mathf.Clamp01(heights[z, x] + normalizedOffset);

            td.SetHeights(0, 0, heights);
            terrain.Flush();
        }

        /// <summary>
        /// Level the terrain to its lowest point + offset.
        /// </summary>
        public static void LevelTerrain(Terrain terrain, float offsetMeters)
        {
            if (terrain == null) return;

            TerrainData td = terrain.terrainData;
            Undo.RegisterCompleteObjectUndo(td, "Terrain Level");

            int res = td.heightmapResolution;
            float[,] heights = td.GetHeights(0, 0, res, res);

            // Find minimum height
            float minH = float.MaxValue;
            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    minH = Mathf.Min(minH, heights[z, x]);

            float targetH = Mathf.Clamp01(minH + offsetMeters / td.size.y);

            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    heights[z, x] = targetH;

            td.SetHeights(0, 0, heights);
            terrain.Flush();
        }

        /// <summary>
        /// Clear all paint layers, setting everything to the first layer.
        /// </summary>
        public static void ClearPaint(Terrain terrain)
        {
            if (terrain == null) return;

            TerrainData td = terrain.terrainData;
            System.Collections.Generic.List<UnityEngine.Object> undoObjects = new System.Collections.Generic.List<UnityEngine.Object> { td };
            if (td.alphamapTextures != null)
                undoObjects.AddRange(td.alphamapTextures);
                
            Undo.RegisterCompleteObjectUndo(undoObjects.ToArray(), "Terrain Clear Paint");

            int res = td.alphamapResolution;
            int layers = td.alphamapLayers;
            if (layers == 0) return;

            float[,,] alphamaps = new float[res, res, layers];

            for (int z = 0; z < res; z++)
                for (int x = 0; x < res; x++)
                    alphamaps[z, x, 0] = 1f; // First layer gets full weight

            td.SetAlphamaps(0, 0, alphamaps);
            terrain.Flush();
        }
    }
}
