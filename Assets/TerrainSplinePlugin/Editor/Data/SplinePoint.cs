// TerrainSplinePlugin - Editor Only
// Unity 2022.3 LTS Compatible

using UnityEngine;

namespace TerrainSplinePlugin
{
    /// <summary>
    /// A single point on a spline with position and bezier tangent handles.
    /// Tangents are stored as local offsets from the position.
    /// </summary>
    [System.Serializable]
    public struct SplinePoint
    {
        /// <summary>World-space position of this spline node.</summary>
        public Vector3 position;

        /// <summary>Rotation of this spline node.</summary>
        public Quaternion rotation;

        /// <summary>Scale of this spline node.</summary>
        public Vector3 scale;

        /// <summary>Incoming tangent handle (local offset from position).</summary>
        public Vector3 tangentIn;

        /// <summary>Outgoing tangent handle (local offset from position).</summary>
        public Vector3 tangentOut;

        /// <summary>How the tangent handles behave when one is moved.</summary>
        public HandleMode handleMode;

        public SplinePoint(Vector3 pos)
        {
            position = pos;
            rotation = Quaternion.identity;
            scale = Vector3.one;
            tangentIn = new Vector3(-1f, 0f, 0f);
            tangentOut = new Vector3(1f, 0f, 0f);
            handleMode = HandleMode.Mirrored;
        }

        /// <summary>World-space position of the incoming tangent handle.</summary>
        public Vector3 TangentInWorld => position + tangentIn;

        /// <summary>World-space position of the outgoing tangent handle.</summary>
        public Vector3 TangentOutWorld => position + tangentOut;

        /// <summary>
        /// Enforce handle constraints based on the current HandleMode.
        /// Call after modifying tangentIn to update tangentOut, or vice versa.
        /// </summary>
        /// <param name="movedIn">True if tangentIn was moved, false if tangentOut was moved.</param>
        public void EnforceHandleMode(bool movedIn)
        {
            switch (handleMode)
            {
                case HandleMode.Aligned:
                    if (movedIn)
                    {
                        float outLength = tangentOut.magnitude;
                        if (tangentIn.magnitude > 0.0001f)
                            tangentOut = -tangentIn.normalized * outLength;
                    }
                    else
                    {
                        float inLength = tangentIn.magnitude;
                        if (tangentOut.magnitude > 0.0001f)
                            tangentIn = -tangentOut.normalized * inLength;
                    }
                    break;

                case HandleMode.Mirrored:
                    if (movedIn)
                        tangentOut = -tangentIn;
                    else
                        tangentIn = -tangentOut;
                    break;

                case HandleMode.Free:
                default:
                    // No constraint
                    break;
            }
        }
    }
}
