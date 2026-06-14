using System.Runtime.CompilerServices;
using Unity.Mathematics;
using UnityEngine;
using static Unity.Mathematics.math;

namespace NativeTreesJobs
{
    /// <summary>
    /// 2D axis aligned bounding box with support for fast ray intersection checking.
    /// </summary>
    public struct AABB2D
    {
        public readonly float2 min;
        public readonly float2 max;

        public float2 Center => .5f * (min + max);
        public float2 Size => max - min;
        public bool IsValid => all(max >= min);
        public AABB2D(float2 min, float2 max)
        {
            this.min = min;
            this.max = max;
        }

        //these functions are wirtten inside of a struct for fast inlining
        //when we write into classes they are can be stored anywhere into the heap memory 
        //while structs are in stack memory and also no GC allocation so data are all paired together 
        /// <summary>
        /// Returns wether this AABB overlaps with another AABB
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)] //its basically inline from HLSL/GLSL
        public bool Overlaps(in AABB2D other)
        {
            return all(max >= other.min) && //all = max.x and max.y
                   all(other.max >= min);
        }

        /// <summary>
        /// Returns wether this AABB fully contains another
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(in AABB2D other, float eps = 1e-5f)
        {
            return all(min <= other.min + eps) &&
                   all(max >= other.max - eps);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Contains(float2 point) => all(point >= min) && all(point <= max);

        /// <summary>
        /// Returns the closest point on this AABB from a given point. If the point lies in this AABB, the point itself is returned.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float2 ClosestPoint(float2 point) => clamp(point, min, max);

        /// <summary>
        /// Returns the squared distance of a point to this AABB. If the point lies in the box, zero is returned.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float DistanceSquared(float2 point) => distancesq(point, ClosestPoint(point));

        // [MethodImpl(MethodImplOptions.AggressiveInlining)]
        // public bool IntersectsRay(in Ray2D ray) => IntersectsRay((PrecomputedRay2D) ray);

        // [MethodImpl(MethodImplOptions.AggressiveInlining)]
        // public bool IntersectsRay(in PrecomputedRay2D ray) => IntersectsRay(ray.origin, ray.invDir, out _);

        // [MethodImpl(MethodImplOptions.AggressiveInlining)]
        // public bool IntersectsRay(in PrecomputedRay2D ray, out float2 point)
        // {
        //     if (IntersectsRay(ray.origin, ray.invDir, out float t))
        //     {
        //         point = ray.origin + ray.dir * t;
        //         return true;
        //     }

        //     point = default;
        //     return false;
        // }


        /// <summary>
        /// Checks if this AABB intersects with a ray.
        /// Fast implementation:
        /// https://tavianator.com/2011/ray_box.html
        /// </summary>
        /// <param name="rayPos">Ray origin position</param>
        /// <param name="rayInvDir">One over the ray's direction</param>
        /// <returns>If the ray intersected this AABB</returns>
        /// <remarks>This method does not handle the case where a component of the ray is on the edge of the box.
        /// And may return a false positive in that case. Checking is ommited for performance and the fact that the intersecter
        /// generally implements a further check.
        /// See https://tavianator.com/2011/ray_box.html and https://tavianator.com/2015/ray_box_nan.html</remarks>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IntersectsRay(in float2 rayPos, in float2 rayInvDir, out float tMin)
        {
            float2 t1 = (min - rayPos) * rayInvDir; // 1/rayDir is to avoid divisions 
            float2 t2 = (max - rayPos) * rayInvDir;

            float2 tMin1 = min(t1, t2);
            float2 tMax1 = max(t1, t2);

            tMin = max(0, cmax(tMin1));
            float tMax = cmin(tMax1);

            return tMax >= tMin;
        }
    }
}