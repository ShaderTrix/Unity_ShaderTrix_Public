using UnityEngine;

namespace SoftBodySimulation
{
    /// <summary>
    /// Base interface for cuboids that handle collision responses.
    /// </summary>
    public interface ICuboidCollisionResponder : ICollisionResponder
    {
        // Vector3 Minimum { get; }
        // Vector3 Maximum { get; }
        Vector3 Center { get; }
        Vector3 HalfSize { get; }
        Matrix4x4 Orientation { get; }
    }
}