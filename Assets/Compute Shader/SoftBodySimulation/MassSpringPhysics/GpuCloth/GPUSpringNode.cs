using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using System.Runtime.InteropServices;

namespace SoftBodySimulation
{
    /// <summary>
    /// Represents the output of the spring nodes.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct SpringNodeOutput
    {
        public Vector3 Position;
        public Vector3 Velocity;
    }
    /// <summary>
    /// Represents a spring node input.
    /// Since a Gpu can't use pointers, we use a slice to refer to which spring dampers have an influence on
    /// this spring node.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct GPUSpringNode
    {
        public Slice SpringDamper;
        public Vector3 Position;
        public Vector3 Velocity;
        public readonly float Radius;
        public int isPinned;
        public Vector3 PinnedPos;
        /// <summary>
        /// Creates a new SpringNode.
        /// </summary>
        /// <param name="initialPosition">The initial starting position before the first update.</param>
        /// <param name="radius">The radius. Does not change ever.</param>
        public GPUSpringNode(Vector3 initialPosition, float radius)
        {
            Position = initialPosition;
            SpringDamper = new Slice(0, 0);
            Velocity = Vector3.zero;
            Radius = radius;
            isPinned = 0;
            PinnedPos = Vector3.zero;
        }
        /// <summary>
        /// Updates which slice of spring dampers influence this spring node.
        /// </summary>
        /// <param name="springDampers">The slice of spring dampers.</param>
        public void SetSlice(Slice springDamper)
        {
            SpringDamper = springDamper;
        }
        /// <summary>
        /// Snaps/teleports this spring node to a given new position.
        /// Its velocity will be updated.
        /// </summary>
        /// <param name="newPosition">The new position.</param>
        /// <param name="updateVelocity">Whether velocity should reflect this update.</param>
        public void SnapTo(Vector3 newPos,bool updateVelocity)
        {
            if (updateVelocity)
            {
                Velocity = (newPos - Position) / Time.fixedDeltaTime;
            }
            else
            {
                Velocity = Vector3.zero;
                Position = newPos;
            }
            isPinned = 1;
            PinnedPos = newPos;
        }
    }
}


