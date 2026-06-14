using System.Runtime.InteropServices;
using UnityEngine;
using static Unity.Mathematics.math;

namespace SoftBodySimulation
{
    /// <summary>
    /// Adapter for Unity cuboid objects that should have no impact on them due to collisions.
    /// They act as if they are immovable.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct ImmovableCuboidCollisionAdapter : ICuboidCollisionResponder
    {
        public Vector3 Velocity => Vector3.zero;
        // public Vector3 Minimum { get; }
        // public Vector3 Maximum { get; }
        public Vector3 Center { get; }
        public Vector3 HalfSize { get; }
        public Matrix4x4 Orientation { get; }        
        public float InverseMass => 0f;

        public ImmovableCuboidCollisionAdapter(Vector3 pos,Vector3 rot,Vector3 size)
        {
            // Minimum = centerPosition - size;
            // Maximum = centerPosition + size;
            Center = pos;  
            HalfSize = size * 0.5f;
            Orientation = Matrix4x4.Rotate(Quaternion.Euler(rot));
        }

        public ImmovableCuboidCollisionAdapter(BoxCollider cuboid)
        {    
            // Minimum = cuboid.bounds.min;
            // Maximum = cuboid.bounds.max;
            var t = cuboid.transform;
            Center = t.TransformPoint(cuboid.center);
            HalfSize = Vector3.Scale(cuboid.size * 0.5f, abs(t.lossyScale));
            Orientation = Matrix4x4.Rotate(t.rotation);
        }

        /// <inheritdoc cref="ICuboidCollisionResponder.ApplyCollisionEffect"/>
        public void ApplyCollisionEffect(Vector3 translation, Vector3 impulse)
        {
            // Do nothing.
        }
    }
}