// TerrainSplinePlugin - Editor Only
// Unity 2022.3 LTS Compatible

namespace TerrainSplinePlugin
{
    /// <summary>
    /// Spline mode — determines how the spline affects the terrain.
    /// Path: affects terrain along the spline curve (roads, rivers).
    /// Shape: fills the closed spline interior (plateaus, valleys).
    /// </summary>
    public enum SplineMode
    {
        Path,
        Shape
    }

    /// <summary>
    /// Handle mode for spline control points.
    /// Free: tangent in/out move independently.
    /// Aligned: tangent in/out stay collinear but can have different lengths.
    /// Mirrored: tangent in/out are exact mirrors.
    /// </summary>
    public enum HandleMode
    {
        Free,
        Aligned,
        Mirrored
    }
}
